//! C ABI over mborgerson/dsp56300 for the F# tooling (`Dsp56k.Native` P/Invokes these).
//!
//! Strings cross as caller-owned buffers: a function writes at most `cap - 1` bytes plus a NUL
//! and returns what it would have written, so the caller can retry with a larger buffer.

use std::ffi::{CStr, c_char};

use dsp56300_core::MemSpace;
use dsp56300_emu::core::{DspState, MemoryMap, MemoryRegion, PowerState, RegionKind};
use dsp56300_emu::jit::JitEngine;

fn write_str(text: &str, out: *mut u8, cap: usize) -> usize {
    if !out.is_null() && cap > 0 {
        let n = text.len().min(cap - 1);
        unsafe {
            std::ptr::copy_nonoverlapping(text.as_ptr(), out, n);
            *out.add(n) = 0;
        }
    }
    text.len()
}

fn space_of(space: u32) -> MemSpace {
    match space {
        0 => MemSpace::X,
        1 => MemSpace::Y,
        _ => MemSpace::P,
    }
}

/// Disassemble one instruction; returns the words it takes (1 or 2), text in `out`.
///
/// # Safety
/// `out` must point to `cap` writable bytes (or be null with `cap` 0).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsd_disassemble(pc: u32, op: u32, next: u32, out: *mut u8, cap: usize) -> u32 {
    let (text, len) = dsp56300_disasm::disassemble(pc, op, next);
    write_str(&text, out, cap);
    len
}

/// Assemble one instruction line at `pc`. Returns the word count (1 or 2) with the words in
/// `words[0..2]`, or -1 with the message in `err`.
///
/// # Safety
/// `line` must be a NUL-terminated UTF-8 string, `words` must hold 2 words, `err` `errcap` bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsd_assemble_line(
    line: *const c_char,
    pc: u32,
    words: *mut u32,
    err: *mut u8,
    errcap: usize,
) -> i32 {
    let text = match unsafe { CStr::from_ptr(line) }.to_str() {
        Ok(t) => t,
        Err(e) => {
            write_str(&e.to_string(), err, errcap);
            return -1;
        }
    };
    match dsp56300_asm::assemble_line(text, pc) {
        Ok(enc) => unsafe {
            *words = enc.word0;
            match enc.word1 {
                Some(w) => {
                    *words.add(1) = w;
                    2
                }
                None => 1,
            }
        },
        Err(e) => {
            write_str(&e.to_string(), err, errcap);
            -1
        }
    }
}

/// Assemble a whole source; writes "P:addr word" lines (hex) or the error to `out`.
/// Returns the length of the full text and 0/1 for success in `ok`.
///
/// # Safety
/// `source` must be NUL-terminated UTF-8; `out` must hold `cap` bytes; `ok` must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsd_assemble(source: *const c_char, out: *mut u8, cap: usize, ok: *mut i32) -> usize {
    let text = unsafe { CStr::from_ptr(source) }.to_string_lossy();
    let (result, success) = match dsp56300_asm::assemble(&text) {
        Ok(r) => {
            let mut s = String::new();
            for seg in &r.segments {
                let tag = format!("{:?}", seg.space);
                for (i, w) in seg.words.iter().enumerate() {
                    s.push_str(&format!("{} {:06x} {:06x}\n", tag, seg.org + i as u32, w));
                }
            }
            (s, 1)
        }
        Err(e) => (e.to_string(), 0),
    };
    unsafe { *ok = success };
    write_str(&result, out, cap)
}

/// One emulated DSP with flat X, Y and P RAM from address 0.
pub struct Emu {
    _x: Vec<u32>,
    _y: Vec<u32>,
    _p: Vec<u32>,
    state: DspState,
    jit: JitEngine,
}

fn region(buf: &mut [u32]) -> Vec<MemoryRegion> {
    vec![MemoryRegion {
        start: 0,
        end: buf.len() as u32,
        kind: RegionKind::Buffer { base: buf.as_mut_ptr(), offset: 0 },
    }]
}

#[unsafe(no_mangle)]
pub extern "C" fn fsd_emu_create(p_words: u32, x_words: u32, y_words: u32) -> *mut Emu {
    let mut x = vec![0u32; x_words as usize];
    let mut y = vec![0u32; y_words as usize];
    let mut p = vec![0u32; p_words as usize];
    let map = MemoryMap { x_regions: region(&mut x), y_regions: region(&mut y), p_regions: region(&mut p) };
    let state = DspState::new(map);
    let jit = JitEngine::new(p_words as usize);
    Box::into_raw(Box::new(Emu { _x: x, _y: y, _p: p, state, jit }))
}

/// # Safety
/// `emu` must come from `fsd_emu_create` and not be used afterwards.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsd_emu_destroy(emu: *mut Emu) {
    if !emu.is_null() {
        drop(unsafe { Box::from_raw(emu) });
    }
}

/// # Safety
/// `emu` must be a live emulator.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsd_emu_read(emu: *const Emu, space: u32, addr: u32) -> u32 {
    let emu = unsafe { &*emu };
    emu.state.read_memory(space_of(space), addr)
}

/// # Safety
/// `emu` must be a live emulator.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsd_emu_write(emu: *mut Emu, space: u32, addr: u32, value: u32) {
    let emu = unsafe { &mut *emu };
    let s = space_of(space);
    if s == MemSpace::P && emu.state.map.read_pram(addr) != value & 0xff_ffff {
        emu.state.pram_dirty.mark_dirty(addr);
    }
    emu.state.write_memory(s, addr, value);
}

/// Registers by their six-bit DDDDDD code (x0 = 4 ... lc = 63).
///
/// # Safety
/// `emu` must be a live emulator.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsd_emu_get_reg(emu: *const Emu, code: u32) -> u32 {
    let emu = unsafe { &*emu };
    emu.state.registers[code as usize & 63]
}

/// # Safety
/// `emu` must be a live emulator.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsd_emu_set_reg(emu: *mut Emu, code: u32, value: u32) {
    let emu = unsafe { &mut *emu };
    emu.state.registers[code as usize & 63] = value;
}

/// # Safety
/// `emu` must be a live emulator.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsd_emu_get_pc(emu: *const Emu) -> u32 {
    unsafe { &*emu }.state.pc
}

/// # Safety
/// `emu` must be a live emulator.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsd_emu_set_pc(emu: *mut Emu, pc: u32) {
    unsafe { &mut *emu }.state.pc = pc;
}

/// Run until a STOP executes or `max_cycles` pass. Returns 1 when stopped (and clears the stop so
/// the next call runs), 0 when the budget ran out.
///
/// # Safety
/// `emu` must be a live emulator.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsd_emu_run_to_stop(emu: *mut Emu, max_cycles: i32) -> i32 {
    let emu = unsafe { &mut *emu };
    emu.state.cycle_budget = 0;
    emu.state.run(&mut emu.jit, max_cycles);
    if emu.state.power_state == PowerState::Stop {
        emu.state.power_state = PowerState::Normal;
        1
    } else {
        0
    }
}

/// # Safety
/// `emu` must be a live emulator.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsd_emu_cycles(emu: *const Emu) -> u32 {
    unsafe { &*emu }.state.cycle_count
}

/// Single-step until a STOP executes or `max_cycles` pass, adding each instruction's cycles to
/// `counts[pc]` (for pc < `len`). Returns 1 when stopped (and clears the stop), 0 otherwise.
///
/// # Safety
/// `emu` must be a live emulator; `counts` must hold `len` words.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsd_emu_profile_to_stop(emu: *mut Emu, max_cycles: i32, counts: *mut u32, len: u32) -> i32 {
    let emu = unsafe { &mut *emu };
    let start = emu.state.cycle_count;
    loop {
        if emu.state.power_state == PowerState::Stop {
            emu.state.power_state = PowerState::Normal;
            return 1;
        }
        let used = emu.state.cycle_count.wrapping_sub(start);
        if used as i64 >= max_cycles as i64 {
            return 0;
        }
        let pc = emu.state.pc;
        let before = emu.state.cycle_count;
        emu.state.execute_one(&mut emu.jit);
        let delta = emu.state.cycle_count.wrapping_sub(before);
        if pc < len && !counts.is_null() {
            unsafe { *counts.add(pc as usize) += delta };
        }
    }
}

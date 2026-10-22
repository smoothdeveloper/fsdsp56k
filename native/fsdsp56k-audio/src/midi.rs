//! MIDI input through midir: a port's channel messages queued for the F# tooling to poll between
//! blocks. The queue is filled on midir's thread and drained on the caller's; it locks, which is
//! fine here (neither side is the audio callback).

use std::collections::VecDeque;
use std::ffi::{CStr, c_char};
use std::sync::{Arc, Mutex};

use midir::{Ignore, MidiInput, MidiInputConnection};

use crate::{set_error, write_text};

const CLIENT: &str = "fsdsp56k";

/// An open input: the connection (closed on drop) and the messages not yet polled, each packed
/// as status | data1 << 8 | data2 << 16.
pub struct MidiIn {
    _connection: MidiInputConnection<()>,
    queue: Arc<Mutex<VecDeque<u32>>>,
}

fn pack(message: &[u8]) -> Option<u32> {
    match message {
        [status, rest @ ..] if *status >= 0x80 && *status < 0xf0 && rest.len() <= 2 => {
            let d1 = rest.first().copied().unwrap_or(0) as u32;
            let d2 = rest.get(1).copied().unwrap_or(0) as u32;
            Some(*status as u32 | d1 << 8 | d2 << 16)
        }
        _ => None,
    }
}

fn input() -> Result<MidiInput, String> {
    let mut m = MidiInput::new(CLIENT).map_err(|e| e.to_string())?;
    // SysEx, timing and active sensing are not what audition plays from
    m.ignore(Ignore::All);
    Ok(m)
}

/// The input ports' names, one per line.
pub fn input_names() -> Result<Vec<String>, String> {
    let m = input()?;
    m.ports().iter().map(|p| m.port_name(p).map_err(|e| e.to_string())).collect()
}

fn open(port: &str) -> Result<MidiIn, String> {
    let m = input()?;
    let queue = Arc::new(Mutex::new(VecDeque::new()));
    let writer = queue.clone();
    let callback = move |_: u64, message: &[u8], _: &mut ()| {
        if let Some(w) = pack(message) {
            if let Ok(mut q) = writer.lock() {
                q.push_back(w);
            }
        }
    };
    if let Some(name) = port.strip_prefix("virtual:") {
        #[cfg(unix)]
        {
            use midir::os::unix::VirtualInput;
            let connection = m.create_virtual(name, callback, ()).map_err(|e| e.to_string())?;
            return Ok(MidiIn { _connection: connection, queue });
        }
        #[cfg(not(unix))]
        return Err(format!("virtual port {name}: not on this system"));
    }
    let ports = m.ports();
    let names: Vec<String> = ports.iter().map(|p| m.port_name(p).unwrap_or_default()).collect();
    let index = match port.parse::<usize>() {
        Ok(i) if i < ports.len() => i,
        _ => names
            .iter()
            .position(|n| n.to_lowercase().contains(&port.to_lowercase()))
            .ok_or_else(|| format!("no MIDI input '{port}' (have: {})", names.join(", ")))?,
    };
    let connection = m.connect(&ports[index], "fsdsp56k-in", callback, ()).map_err(|e| e.to_string())?;
    Ok(MidiIn { _connection: connection, queue })
}

/// The input ports' names, one per line, as UTF-8 into `buffer`; the full length.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsdsp56k_midi_inputs(buffer: *mut c_char, length: u32) -> u32 {
    match input_names() {
        Ok(names) => unsafe { write_text(&names.join("\n"), buffer, length) },
        Err(e) => {
            set_error(e);
            u32::MAX
        }
    }
}

/// Open an input: an index, part of a port's name, or `virtual:NAME` for a port of our own that
/// other programs send to (macOS and Linux). Null on failure (`fsdsp56k_audio_last_error`).
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsdsp56k_midi_in_open(port: *const c_char) -> *mut MidiIn {
    if port.is_null() {
        set_error("no port".into());
        return std::ptr::null_mut();
    }
    let port = unsafe { CStr::from_ptr(port) }.to_string_lossy().into_owned();
    match open(&port) {
        Ok(m) => Box::into_raw(Box::new(m)),
        Err(e) => {
            set_error(e);
            std::ptr::null_mut()
        }
    }
}

/// Up to `max` queued messages into `out`, oldest first; how many.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsdsp56k_midi_in_poll(input: *mut MidiIn, out: *mut u32, max: u32) -> u32 {
    if input.is_null() || out.is_null() {
        return 0;
    }
    let input = unsafe { &*input };
    let Ok(mut q) = input.queue.lock() else { return 0 };
    let n = q.len().min(max as usize);
    for i in 0..n {
        unsafe { *out.add(i) = q.pop_front().unwrap_or(0) };
    }
    n as u32
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsdsp56k_midi_in_close(input: *mut MidiIn) {
    if !input.is_null() {
        drop(unsafe { Box::from_raw(input) });
    }
}

#[cfg(test)]
mod tests {
    use super::pack;

    #[test]
    fn channel_messages_pack_and_the_rest_do_not() {
        assert_eq!(pack(&[0x91, 36, 100]), Some(0x91 | 36 << 8 | 100 << 16));
        assert_eq!(pack(&[0xb0, 16, 64]), Some(0xb0 | 16 << 8 | 64 << 16));
        assert_eq!(pack(&[0xc0, 5]), Some(0xc0 | 5 << 8));
        assert_eq!(pack(&[0xf0, 0x7e, 0xf7]), None);
        assert_eq!(pack(&[0xf8]), None);
    }
}

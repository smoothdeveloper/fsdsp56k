//! The default output device, fed by the F# tooling. One producer (the .NET thread rendering the
//! track) pushes mono frames into a single-producer single-consumer ring; the device callback is
//! the one consumer. Neither side locks or allocates once the player is open: an empty ring plays
//! silence and counts an underrun. MIDI input for audition is in `midi`.

pub mod midi;

use std::cell::RefCell;
use std::ffi::c_char;
use std::sync::Arc;
use std::sync::atomic::{AtomicU32, AtomicU64, AtomicUsize, Ordering};

use cpal::traits::{DeviceTrait, HostTrait, StreamTrait};

/// A single-producer single-consumer ring of f32 frames. `head` is written by the producer only,
/// `tail` by the consumer only; one slot stays empty to tell full from empty.
struct Ring {
    slots: Box<[AtomicU32]>,
    head: AtomicUsize,
    tail: AtomicUsize,
    underruns: AtomicU64,
}

impl Ring {
    fn new(capacity: usize) -> Ring {
        let slots = (0..capacity + 1).map(|_| AtomicU32::new(0)).collect();
        Ring { slots, head: AtomicUsize::new(0), tail: AtomicUsize::new(0), underruns: AtomicU64::new(0) }
    }

    fn len(&self) -> usize {
        let n = self.slots.len();
        (self.head.load(Ordering::Acquire) + n - self.tail.load(Ordering::Acquire)) % n
    }

    /// Producer side: as many of `frames` as fit; how many went in.
    fn push(&self, frames: &[f32]) -> usize {
        let n = self.slots.len();
        let mut head = self.head.load(Ordering::Relaxed);
        let tail = self.tail.load(Ordering::Acquire);
        let free = (tail + n - head - 1) % n;
        let count = frames.len().min(free);
        for &f in &frames[..count] {
            self.slots[head].store(f.to_bits(), Ordering::Relaxed);
            head = (head + 1) % n;
        }
        self.head.store(head, Ordering::Release);
        count
    }

    /// Consumer side: the next frame, or None when the producer has fallen behind.
    fn pop(&self) -> Option<f32> {
        let n = self.slots.len();
        let tail = self.tail.load(Ordering::Relaxed);
        if tail == self.head.load(Ordering::Acquire) {
            return None;
        }
        let f = f32::from_bits(self.slots[tail].load(Ordering::Relaxed));
        self.tail.store((tail + 1) % n, Ordering::Release);
        Some(f)
    }
}

/// An open output: the stream (dropped on close, which stops it) and the ring it reads.
pub struct Player {
    _stream: cpal::Stream,
    ring: Arc<Ring>,
    sample_rate: u32,
    channels: u16,
}

thread_local! {
    static LAST_ERROR: RefCell<String> = const { RefCell::new(String::new()) };
}

pub(crate) fn set_error(message: String) {
    LAST_ERROR.with(|e| *e.borrow_mut() = message);
}

fn fail(message: String) -> *mut Player {
    set_error(message);
    std::ptr::null_mut()
}

/// `text` as UTF-8 into `buffer` (cut to fit, NUL-terminated); its full length.
pub(crate) unsafe fn write_text(text: &str, buffer: *mut c_char, length: u32) -> u32 {
    let bytes = text.as_bytes();
    if !buffer.is_null() && length > 0 {
        let n = bytes.len().min(length as usize - 1);
        unsafe {
            std::ptr::copy_nonoverlapping(bytes.as_ptr(), buffer as *mut u8, n);
            *buffer.add(n) = 0;
        }
    }
    bytes.len() as u32
}

fn open(sample_rate: u32, capacity: usize) -> Result<Player, String> {
    let host = cpal::default_host();
    let device = host.default_output_device().ok_or("no default output device")?;
    // the machine's rate when the device offers it as f32; else the device's default
    let supported = device
        .supported_output_configs()
        .map_err(|e| e.to_string())?
        .filter(|c| c.sample_format() == cpal::SampleFormat::F32)
        .find_map(|c| c.try_with_sample_rate(sample_rate));
    let supported = match supported {
        Some(c) => c,
        None => device.default_output_config().map_err(|e| e.to_string())?,
    };
    if supported.sample_format() != cpal::SampleFormat::F32 {
        // TODO: integer output formats (convert in the callback)
        return Err(format!("the output device wants {:?} samples; only f32 is written", supported.sample_format()));
    }
    let config = supported.config();
    let channels = config.channels;
    let ring = Arc::new(Ring::new(capacity));
    let reader = ring.clone();
    let stream = device
        .build_output_stream::<f32, _, _>(
            config.clone(),
            move |out: &mut [f32], _| {
                // one mono frame to every channel of each output frame
                for frame in out.chunks_mut(channels as usize) {
                    let v = match reader.pop() {
                        Some(v) => v,
                        None => {
                            reader.underruns.fetch_add(1, Ordering::Relaxed);
                            0.0
                        }
                    };
                    frame.fill(v);
                }
            },
            |e| eprintln!("fsdsp56k-audio: {e}"),
            None,
        )
        .map_err(|e| e.to_string())?;
    stream.play().map_err(|e| e.to_string())?;
    Ok(Player { _stream: stream, ring, sample_rate: config.sample_rate, channels })
}

/// Open the default output, asking for `sample_rate` (the device may give another: see
/// `fsdsp56k_audio_sample_rate`), with a ring of `capacity_frames`. Null on failure, with the
/// reason in `fsdsp56k_audio_last_error`.
#[unsafe(no_mangle)]
pub extern "C" fn fsdsp56k_audio_open(sample_rate: u32, capacity_frames: u32) -> *mut Player {
    match open(sample_rate, capacity_frames.max(64) as usize) {
        Ok(p) => Box::into_raw(Box::new(p)),
        Err(e) => fail(e),
    }
}

/// The reason the last open (audio or MIDI) on this thread failed, as UTF-8 into `buffer`; its
/// full length.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsdsp56k_audio_last_error(buffer: *mut c_char, length: u32) -> u32 {
    LAST_ERROR.with(|e| unsafe { write_text(&e.borrow(), buffer, length) })
}

/// Push mono frames (-1..1); how many fitted. The caller waits and pushes the rest.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsdsp56k_audio_push(player: *mut Player, frames: *const f32, count: u32) -> u32 {
    if player.is_null() || frames.is_null() {
        return 0;
    }
    let p = unsafe { &*player };
    let frames = unsafe { std::slice::from_raw_parts(frames, count as usize) };
    p.ring.push(frames) as u32
}

/// Frames waiting in the ring.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsdsp56k_audio_queued(player: *const Player) -> u32 {
    if player.is_null() { 0 } else { unsafe { &*player }.ring.len() as u32 }
}

/// Output frames the device asked for while the ring was empty.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsdsp56k_audio_underruns(player: *const Player) -> u64 {
    if player.is_null() { 0 } else { unsafe { &*player }.ring.underruns.load(Ordering::Relaxed) }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsdsp56k_audio_sample_rate(player: *const Player) -> u32 {
    if player.is_null() { 0 } else { unsafe { &*player }.sample_rate }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsdsp56k_audio_channels(player: *const Player) -> u32 {
    if player.is_null() { 0 } else { unsafe { &*player }.channels as u32 }
}

/// Stop the output and free the player.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn fsdsp56k_audio_close(player: *mut Player) {
    if !player.is_null() {
        drop(unsafe { Box::from_raw(player) });
    }
}

#[cfg(test)]
mod tests {
    use super::Ring;

    #[test]
    fn the_ring_keeps_order_and_refuses_what_does_not_fit() {
        let r = Ring::new(4);
        assert_eq!(r.push(&[1.0, 2.0, 3.0, 4.0, 5.0]), 4);
        assert_eq!(r.len(), 4);
        assert_eq!(r.pop(), Some(1.0));
        assert_eq!(r.push(&[6.0]), 1);
        let rest: Vec<f32> = std::iter::from_fn(|| r.pop()).collect();
        assert_eq!(rest, vec![2.0, 3.0, 4.0, 6.0]);
        assert_eq!(r.pop(), None);
    }
}

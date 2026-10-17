/// jmamma/MDS machines/examples/imports/pluck.asm56k as a kernel: a Karplus-Strong string in the
/// track's own external memory (MDS import 6), one period long: a trigger fills it with noise,
/// then each sample is the average of the two oldest times the loop gain.
[<ReflectedDefinition>]
module samples.kernels.Pluck

open dsp56k.kernel

type Params =
  { ptch : Frac       // oscillator increment P: the frequency is P x 44,100 / 2^21
    dec  : Frac       // loop gain, Q23
    lev  : Frac }     // output level, unipolar Q23

type State =
  { mutable region  : Frac    // this track's 1,536 words, as an offset in the tracks' memory
    mutable last    : Frac    // the string's length - 1 (its modulo); zero until a trigger
    mutable pos     : Frac    // the string's position
    mutable pending : Frac    // noise samples still to put into the string
    mutable prev    : Frac    // the oldest sample of the previous step
    mutable noise   : Frac }  // the noise register

/// x^24 + x^23 + x^22 + x^17 + 1
let taps = 0xc20001

let init (_: Params) (s: State) =
  // each track owns 1,536 words, one region after another
  let t = Acc.ofFrac (Track.number ())
  s.region <- Acc.hi (((t <<< 1) + Acc.hi t) <<< 9)
  s.pos <- s.region
  s.last <- Frac.zero
  s.pending <- Frac.zero
  s.prev <- Frac.zero
  s.noise <- Frac.ofRaw 0x5a5a5a

let trig (p: Params) (s: State) =
  // L = 2^21 / P samples, at most 512 (86 Hz), at least 2
  let mutable length = Acc.ofFrac (Frac.ofRaw 512)
  if Acc.ofFrac (Frac.ofRaw 4096) <= Acc.ofFrac p.ptch then
    length <- Acc.ofFrac (Acc.lo (Acc.divide (Acc.ofRaw 0x200000L) p.ptch))
    if length < Acc.ofFrac (Frac.ofRaw 2) then length <- Acc.ofFrac (Frac.ofRaw 2)
  s.pending <- Acc.hi length              // one period of noise first
  s.last <- Acc.hi (length - Frac.ofRaw 1)
  s.pos <- s.region
  s.prev <- Frac.zero

let render (p: Params) (s: State) (out: Output) =
  if s.last = Frac.zero then
    for _ in 0 .. 31 do
      out.Write Frac.zero                 // not triggered yet
  else
    let string = Cursor.modulo Track.memory s.pos s.last
    if s.pending > Frac.zero then
      // the pluck: noise into the string, played as it goes in
      s.pending <- Acc.hi (Acc.ofFrac s.pending - Frac.ofRaw 32)
      let mutable n = s.noise
      for _ in 0 .. 31 do
        n <- Frac.lfsr taps n
        string.Put n
        out.Write(Acc.sat (n * p.lev))
      s.noise <- n
    else
      // the string: the average of the two oldest samples, times the loop gain
      let mutable prev = s.prev
      for _ in 0 .. 31 do
        let oldest = string.Peek()
        let average = Acc.sat ((Acc.ofFrac oldest + prev) >>> 1)
        prev <- oldest
        let v = Acc.sat (average * p.dec)
        string.Put v
        out.Write(Acc.sat (v * p.lev))
      s.prev <- prev
    s.pos <- string.Position()

let kernel = Kernel.define "pluck" <@ init @> <@ trig @> <@ render @>

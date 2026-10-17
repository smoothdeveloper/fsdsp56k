/// jmamma/MDS machines/examples/sine (sine.asm56k) as a kernel: the same workset layout and
/// arithmetic, written as F#. The grade compares it with the shipped package.
[<ReflectedDefinition>]
module samples.kernels.Sine

open dsp56k.kernel

type Params =
  { pitch : Frac      // the host's pitch increment
    amp   : Frac }    // unsigned Q23

type State =
  { mutable phase   : Long      // Y+9..10: table index . fraction
    mutable ampPrev : Frac }    // Y+11: the amplitude the last block ended on

let init (_: Params) (s: State) =
  s.phase <- Long.zero
  s.ampPrev <- Frac.one

let trig (_: Params) (s: State) =
  s.phase <- Long.zero

let render (p: Params) (s: State) (out: Output) =
  let step = Acc.ofFrac p.pitch >>> 6                         // P / 64 table words per frame
  let ramp = (Acc.ofFrac p.amp - Acc.ofFrac s.ampPrev) >>> 5  // the gain reaches AMP over the block
  let mutable gain = Acc.ofFrac s.ampPrev
  let mutable phase = Acc.ofLong s.phase
  for _ in 0 .. 31 do
    gain <- gain + ramp
    let v = Table.read Rom.sine (Acc.hi phase)
    out.Write(Acc.sat ((v * Acc.hi gain) >>> 2))
    phase <- Acc.andHi 0x7fff (phase + step)
  s.phase <- Long.ofAcc phase
  s.ampPrev <- p.amp

let kernel = Kernel.define "sine" <@ init @> <@ trig @> <@ render @>

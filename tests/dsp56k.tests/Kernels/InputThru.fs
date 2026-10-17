/// jmamma/MDS machines/examples/imports/input_thru.asm56k as a kernel: external input A or B,
/// read from the firmware's input ring.
[<ReflectedDefinition>]
module samples.kernels.InputThru

open dsp56k.kernel

type Params =
  { side : Frac       // 0 input A, 1 input B (the second word of each pair)
    lev  : Frac }     // output level, unipolar Q23

type State =
  { mutable pos : Frac }   // this track's position, as an offset in the ring

let init (_: Params) (s: State) =
  s.pos <- Acc.hi (Acc.ofFrac (Cell.read Input.position) - Region.addressOf Input.ring)

let trig (_: Params) (_: State) = ()       // the input plays all the time

let render (p: Params) (s: State) (out: Output) =
  // 48 pairs behind the position, wrapping in the 256-word ring, one pair a frame
  let start = Acc.hi (Acc.andHi 0xff (Acc.ofFrac s.pos - Frac.ofRaw 96 + p.side))
  let input = Cursor.ring Input.ring start 2
  for _ in 0 .. 31 do
    out.Write(Acc.sat (input.Next() * p.lev))
  s.pos <- Acc.hi (Acc.andHi 0xff (Acc.ofFrac s.pos + Frac.ofRaw 64))

let kernel = Kernel.define "input-thru" <@ init @> <@ trig @> <@ render @>

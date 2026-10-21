// Attribution: ported from https://github.com/jmamma/MDS (machines/examples/imports/main_tap.asm56k) at c446179, by Justin Mammarella (jmamma); no licence published. See THIRD-PARTY-NOTICES.md.
/// jmamma/MDS machines/examples/imports/main_tap.asm56k as a kernel: the main output, mono, now
/// or up to two blocks late.
[<ReflectedDefinition>]
module samples.kernels.MainTap

open dsp56k.kernel

type Params =
  { back : Frac       // 0 this block, 1 or 2 blocks older
    lev  : Frac }     // output level, unipolar Q23

let init (_: Params) () = ()
let trig (_: Params) () = ()               // the main output plays all the time

let render (p: Params) () (out: Output) =
  // BACK blocks of 64 words before the block the firmware points at, inside the ring
  let block = Acc.hi (Acc.andHi 0xff (Acc.ofFrac (Cell.read Main.position) - (Acc.ofFrac p.back <<< 6) - Region.addressOf Main.ring))
  let main = Cursor.linear Main.ring block
  for _ in 0 .. 31 do
    let left = main.Next()
    let right = main.Next()
    let mono = Acc.sat ((Acc.ofFrac left + right) >>> 1)
    out.Write(Acc.sat (mono * p.lev))

let kernel = Kernel.define "main-tap" <@ init @> <@ trig @> <@ render @>

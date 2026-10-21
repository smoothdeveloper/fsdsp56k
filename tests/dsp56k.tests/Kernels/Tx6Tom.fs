// Attribution: ported from https://github.com/etcetc000/kitbasher (mds/tx6/lt/lt.asm, mds/tx6/ht/ht.asm) at 510bdbd, by Matthew Fecher (AnalogMatthew), Fadedlimes; MIT. See THIRD-PARTY-NOTICES.md.
/// kitbasher's TX6 LT and HT toms (mds/tx6/lt/lt.asm and ht/ht.asm, MIT; Simple606-inspired,
/// by Matthew Fecher and Fadedlimes, see Tx6TomTables.fs for the notice) as one engine and two
/// kernels. Two damped resonator modes and LCG noise through a biquad, mixed under a gain that
/// ramps per sample. The two voices differ in tables, seeds, starting levels and LT's pitch glide;
/// here those are a `Voice` record known when compiling, so `if voice.glide` and the table widths
/// are decided by the compiler and each kernel contains only its own code.
[<ReflectedDefinition>]
module samples.kernels.Tx6Tom

open dsp56k.kernel
open samples.kernels.Tx6TomTables

type Voice =
  { control1 : Table      // from DEC: remaining, fp, sp, r0, r1, d0, d1
    control2 : Table      // from TUNE: p0, cos0, p1, cos1, [qbase, glide,] b0, a1, a2
    width2   : int
    layers   : Table      // from NOIS: fast, slow
    glide    : bool       // LT sweeps the first mode's pitch
    seed     : Frac
    start0   : Frac       // the modes' first samples
    start1   : Frac }

let lt =
  { control1 = ltControl1; control2 = ltControl2; width2 = 9; layers = ltLayers; glide = true
    seed = Frac.ofRaw 0x006061; start0 = Frac.ofRaw 0x26c493; start1 = Frac.ofRaw 0x059833 }

let ht =
  { control1 = htControl1; control2 = htControl2; width2 = 7; layers = htLayers; glide = false
    seed = Frac.ofRaw 0x006062; start0 = Frac.ofRaw 0x1713ec; start1 = Frac.ofRaw 0x04047f }

type Params =
  { tune : Frac       // raw 14-bit settings: 128 x the front-panel value
    dec  : Frac
    nois : Frac
    over : Frac }

type State =
  { mutable remaining : Frac
    mutable fp : Frac             // per-block decay of the fast and slow gains
    mutable sp : Frac
    mutable r0 : Frac             // mode radii and damping
    mutable r1 : Frac
    mutable d0 : Frac
    mutable d1 : Frac
    mutable p0 : Frac             // mode 0: previous sample, cosine
    mutable cos0 : Frac
    mutable p1 : Frac             // mode 1
    mutable cos1 : Frac
    mutable qbase : Frac          // LT's glide: base and offset
    mutable glide : Frac
    mutable b0 : Frac             // noise biquad
    mutable a1 : Frac
    mutable a2 : Frac
    mutable rng : Frac
    mutable z1 : Frac
    mutable z2 : Frac
    mutable fast : Frac           // the gain is fast + slow
    mutable slow : Frac
    mutable lastGain : Frac
    mutable s0 : Frac             // mode 0: sample, coefficient
    mutable c0 : Frac
    mutable s1 : Frac             // mode 1
    mutable c1 : Frac }

let row (p: Frac) = Acc.hi (Acc.andHi 0x3f (Acc.ofFrac p >>> 8))

let init (v: Voice) (s: State) =
  s.remaining <- Frac.zero
  s.fp <- Frac.zero
  s.sp <- Frac.zero
  s.r0 <- Frac.zero
  s.r1 <- Frac.zero
  s.d0 <- Frac.zero
  s.d1 <- Frac.zero
  s.p0 <- Frac.zero
  s.cos0 <- Frac.zero
  s.p1 <- Frac.zero
  s.cos1 <- Frac.zero
  s.qbase <- Frac.zero
  s.glide <- Frac.zero
  s.b0 <- Frac.zero
  s.a1 <- Frac.zero
  s.a2 <- Frac.zero
  s.rng <- v.seed
  s.z1 <- Frac.zero
  s.z2 <- Frac.zero
  s.fast <- Frac.zero
  s.slow <- Frac.zero
  s.lastGain <- Frac.zero
  s.s0 <- Frac.zero
  s.c0 <- Frac.zero
  s.s1 <- Frac.zero
  s.c1 <- Frac.zero

let trig (v: Voice) (p: Params) (s: State) =
  s.z1 <- Frac.zero
  s.z2 <- Frac.zero
  s.lastGain <- Frac.zero
  let d = row p.dec
  s.remaining <- Table.row v.control1 7 d 0
  s.fp <- Table.row v.control1 7 d 1
  s.sp <- Table.row v.control1 7 d 2
  s.r0 <- Table.row v.control1 7 d 3
  s.r1 <- Table.row v.control1 7 d 4
  s.d0 <- Table.row v.control1 7 d 5
  s.d1 <- Table.row v.control1 7 d 6
  let t = row p.tune
  s.p0 <- Table.row v.control2 v.width2 t 0
  s.cos0 <- Table.row v.control2 v.width2 t 1
  s.p1 <- Table.row v.control2 v.width2 t 2
  s.cos1 <- Table.row v.control2 v.width2 t 3
  if v.glide then
    s.qbase <- Table.row v.control2 v.width2 t 4
    s.glide <- Table.row v.control2 v.width2 t 5
  s.b0 <- Table.row v.control2 v.width2 t (v.width2 - 3)
  s.a1 <- Table.row v.control2 v.width2 t (v.width2 - 2)
  s.a2 <- Table.row v.control2 v.width2 t (v.width2 - 1)
  s.s0 <- v.start0
  s.s1 <- v.start1
  s.c0 <- Acc.hi (Acc.rnd (s.cos0 * s.r0))
  s.c1 <- Acc.hi (Acc.rnd (s.cos1 * s.r1))
  // NOIS picks the fast and slow gains
  let layer = Acc.hi (Acc.andHi 0x7e (Acc.ofFrac p.nois >>> 7))
  s.fast <- Table.row v.layers 1 layer 0
  s.slow <- Table.row v.layers 1 layer 1
  // OVER below 127 scales the second mode
  let o = Acc.ofFrac p.over >>> 7
  if o <> Acc.ofFrac (Frac.ofRaw 127) then
    let k = Acc.hi (o <<< 16)
    s.s1 <- Acc.hi (Acc.rnd (k * s.s1))
    s.p1 <- Acc.hi (Acc.rnd (k * s.p1))

/// One damped resonator mode: next = 2 (c s - d p).
let mode (c: Frac) (s: Frac) (d: Frac) (p: Frac) = (c * s - d * p) <<< 1

let render (v: Voice) (s: State) (out: Output) =
  if s.remaining = Frac.zero then
    for _ in 0 .. 31 do
      out.Write Frac.zero
  else
    s.remaining <- Acc.hi (Acc.ofFrac s.remaining - Frac.ofRaw 1)

    if v.glide then
      // the first mode's frequency glides down to qbase
      let q = Acc.hi (Acc.ofFrac s.qbase + s.glide)
      s.glide <- Acc.hi (Acc.rnd (Frac.ofRaw 0x7e2da6 * s.glide))
      let cosq = Acc.hi (-((q * q) >>> 1) + Frac.one)
      s.c0 <- Acc.hi (Acc.rnd (cosq * s.r0))

    // noise: a linear congruential generator, the low word of the product
    let noise = Cursor.linear Scratch.y Frac.zero
    let mutable rng = s.rng
    for _ in 0 .. 31 do
      rng <- Acc.hi (Acc.ofFrac (Acc.lo ((Frac.ofRaw 0xfd43fd * rng) >>> 1)) + Frac.ofRaw 0xc39ec3)
      noise.Put(Acc.hi (rng * Frac.ofRaw 0x400000))
    s.rng <- rng

    // the noise biquad, in place (transposed direct form)
    let filter = Cursor.linear Scratch.y Frac.zero
    let mutable z1 = s.z1
    let mutable z2 = s.z2
    for _ in 0 .. 31 do
      let fed = (filter.Peek() * s.b0) <<< 1
      let y = Acc.hi (fed + z1)
      filter.Put y
      z1 <- Acc.hi (((s.a1 * y) <<< 1) + z2)
      z2 <- Acc.hi (((s.a2 * y) <<< 1) - Acc.hi fed)
    s.z1 <- z1
    s.z2 <- z2

    // the two modes, summed into the temporary X memory
    let tone = Cursor.linear Scratch.x Frac.zero
    let mutable s0 = s.s0
    let mutable p0 = s.p0
    for _ in 0 .. 31 do
      let next = Acc.hi (mode s.c0 s0 s.d0 p0)
      p0 <- s0
      s0 <- next
      tone.Put next
    s.s0 <- s0
    s.p0 <- p0
    let sum = Cursor.linear Scratch.x Frac.zero
    let mutable s1 = s.s1
    let mutable p1 = s.p1
    for _ in 0 .. 31 do
      let next = Acc.rnd (mode s.c1 s1 s.d1 p1)
      p1 <- s1
      s1 <- Acc.hi next
      sum.Put(Acc.hi (next + sum.Peek()))
    s.s1 <- s1
    s.p1 <- p1

    // the gain: fast + slow, ramped from the last block's
    s.fast <- Acc.hi (Acc.rnd (s.fp * s.fast))
    s.slow <- Acc.hi (Acc.rnd (s.sp * s.slow))
    let gain = Acc.hi (Acc.ofFrac s.slow + s.fast)
    let from = s.lastGain
    s.lastGain <- gain
    let step = Acc.hi ((Acc.ofFrac gain - from) >>> 5)
    let toneIn = Cursor.linear Scratch.x Frac.zero
    let noiseIn = Cursor.linear Scratch.y Frac.zero
    let mutable g = from
    for _ in 0 .. 31 do
      let t = toneIn.Next()
      out.Write(Acc.sat (noiseIn.Next() * g + t))
      g <- Acc.hi (Acc.ofFrac g + step)

let ltInit (_: Params) (s: State) = init lt s
let ltTrig (p: Params) (s: State) = trig lt p s
let ltRender (_: Params) (s: State) (out: Output) = render lt s out
let htInit (_: Params) (s: State) = init ht s
let htTrig (p: Params) (s: State) = trig ht p s
let htRender (_: Params) (s: State) (out: Output) = render ht s out

let ltKernel = Kernel.define "tx6-lt" <@ ltInit @> <@ ltTrig @> <@ ltRender @>
let htKernel = Kernel.define "tx6-ht" <@ htInit @> <@ htTrig @> <@ htRender @>

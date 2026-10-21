// Attribution: ported from https://github.com/etcetc000/kitbasher (mds/tx6/ch/ch.asm, mds/tx6/oh/oh.asm, mds/tx6/cy/cy.asm) at 510bdbd, by Matthew Fecher (AnalogMatthew), Fadedlimes; MIT. See THIRD-PARTY-NOTICES.md.
/// kitbasher's TX6 CH and OH hats and CY cymbal (mds/tx6/ch, oh and cy, MIT; Simple606-inspired,
/// by Matthew Fecher and Fadedlimes, see Tx6MetalTables.fs for the notice) as one engine and
/// three kernels. LCG noise through a biquad and two undamped "metal" partials, each under its
/// own gain ramped per sample; the gains follow fast, slow, attack, bell and click envelopes and
/// a fade on the remaining length. The voices differ in tables, seed, envelope rates and what
/// the third control does (CH/OH: METL picks the metal mix; CY: BELL sets the bell level): a
/// `Voice` record known when compiling, whose `layer` union is matched by the compiler.
[<ReflectedDefinition>]
module samples.kernels.Tx6Metal

open dsp56k.kernel
open samples.kernels.Tx6MetalTables

/// What the third control sets.
type Layer =
  /// METL: a row of metal tone, metal noise and click levels; the bell starts at a constant.
  | Metal of Table
  /// BELL: the bell level from a table; fixed metal tone and noise; the click at a constant.
  | Bell of Table * tone: Frac * noise: Frac * click: Frac

type Voice =
  { control1   : Table      // from DEC: remaining, fp, sp, fade
    control2   : Table      // from TUNE: b0, a1, a2, s0, p0, c0, s1, p1, c1
    layer      : Layer
    seed       : Frac
    fast0      : Frac
    slow0      : Frac
    bellDecay  : Frac
    clickDecay : Frac }

let ch =
  { control1 = chControl1; control2 = chControl2; layer = Metal chMetal
    seed = Frac.ofRaw 0x606301; fast0 = Frac.ofRaw 0x700000; slow0 = Frac.zero
    bellDecay = Frac.ofRaw 0x7db2f1; clickDecay = Frac.ofRaw 0x647ff1 }

let oh =
  { ch with
      control1 = ohControl1; control2 = ohControl2; layer = Metal ohMetal
      seed = Frac.ofRaw 0x606302; fast0 = Frac.ofRaw 0x0b79a7; slow0 = Frac.ofRaw 0x5cd845 }

let cy =
  { control1 = cyControl1; control2 = cyControl2
    layer = Bell(cyLayers, Frac.ofRaw 0x170a3e, Frac.ofRaw 0x347ae1, Frac.ofRaw 0x2ca3d7)
    seed = Frac.ofRaw 0x606303; fast0 = Frac.ofRaw 0x07ef9e; slow0 = Frac.ofRaw 0x5b4395
    bellDecay = Frac.ofRaw 0x7e7620; clickDecay = Frac.ofRaw 0x6eb582 }

type Params =
  { tune : Frac       // raw 14-bit settings: 128 x the front-panel value
    dec  : Frac
    layer : Frac }      // METL (CH, OH) or BELL (CY)

type State =
  { mutable remaining : Frac
    mutable fp : Frac             // per-block decay of the fast and slow envelopes
    mutable sp : Frac
    mutable fade : Frac           // how the remaining length fades the level
    mutable b0 : Frac             // noise biquad
    mutable a1 : Frac
    mutable a2 : Frac
    mutable s0 : Frac             // partial 0: sample, previous, coefficient
    mutable p0 : Frac
    mutable c0 : Frac
    mutable s1 : Frac             // partial 1
    mutable p1 : Frac
    mutable c1 : Frac
    mutable rng : Frac
    mutable z1 : Frac
    mutable z2 : Frac
    mutable fast : Frac           // envelopes
    mutable slow : Frac
    mutable attack : Frac
    mutable bell : Frac
    mutable click : Frac
    mutable toneGain : Frac       // last block's gains
    mutable noiseGain : Frac
    mutable metalTone : Frac
    mutable metalNoise : Frac }

let init (v: Voice) (s: State) =
  s.remaining <- Frac.zero
  s.fp <- Frac.zero
  s.sp <- Frac.zero
  s.fade <- Frac.zero
  s.b0 <- Frac.zero
  s.a1 <- Frac.zero
  s.a2 <- Frac.zero
  s.s0 <- Frac.zero
  s.p0 <- Frac.zero
  s.c0 <- Frac.zero
  s.s1 <- Frac.zero
  s.p1 <- Frac.zero
  s.c1 <- Frac.zero
  s.rng <- v.seed
  s.z1 <- Frac.zero
  s.z2 <- Frac.zero
  s.fast <- Frac.zero
  s.slow <- Frac.zero
  s.attack <- Frac.zero
  s.bell <- Frac.zero
  s.click <- Frac.zero
  s.toneGain <- Frac.zero
  s.noiseGain <- Frac.zero
  s.metalTone <- Frac.zero
  s.metalNoise <- Frac.zero

let trig (v: Voice) (p: Params) (s: State) =
  s.toneGain <- Frac.zero
  s.noiseGain <- Frac.zero
  s.z1 <- Frac.zero
  s.z2 <- Frac.zero
  let d = Acc.hi (Acc.andHi 0x3f (Acc.ofFrac p.dec >>> 8))
  s.remaining <- Table.row v.control1 4 d 0
  s.fp <- Table.row v.control1 4 d 1
  s.sp <- Table.row v.control1 4 d 2
  s.fade <- Table.row v.control1 4 d 3
  let t = Acc.hi (Acc.andHi 0x3f (Acc.ofFrac p.tune >>> 8))
  s.b0 <- Table.row v.control2 9 t 0
  s.a1 <- Table.row v.control2 9 t 1
  s.a2 <- Table.row v.control2 9 t 2
  s.s0 <- Table.row v.control2 9 t 3
  s.p0 <- Table.row v.control2 9 t 4
  s.c0 <- Table.row v.control2 9 t 5
  s.s1 <- Table.row v.control2 9 t 6
  s.p1 <- Table.row v.control2 9 t 7
  s.c1 <- Table.row v.control2 9 t 8
  s.fast <- v.fast0
  s.slow <- v.slow0
  s.attack <- Frac.one
  let m = Acc.hi (Acc.ofFrac p.layer >>> 8)
  match v.layer with
  | Metal table ->
    // METL blends the metal partials against the noise
    s.bell <- Frac.ofRaw 0x2ccccd
    s.metalTone <- Table.row table 3 m 0
    s.metalNoise <- Table.row table 3 m 1
    s.click <- Table.row table 3 m 2
  | Bell(table, _, _, click) ->
    // BELL sets the bell level
    s.click <- click
    s.bell <- Table.read table m

let render (v: Voice) (s: State) (out: Output) =
  if s.remaining = Frac.zero then
    for _ in 0 .. 31 do
      out.Write Frac.zero
  else
    s.remaining <- Acc.hi (Acc.ofFrac s.remaining - Frac.ofRaw 1)
    // the level fades over the last blocks: remaining x fade, at most 1
    let faded = (s.fade * s.remaining) <<< 23
    let mutable fadeGain = Acc.hi faded
    if faded > Acc.ofFrac Frac.one then fadeGain <- Frac.one

    // envelopes, once a block
    let oldTone = s.toneGain
    let oldNoise = s.noiseGain
    s.fast <- Acc.hi (Acc.rnd (s.fp * s.fast))
    s.slow <- Acc.hi (Acc.rnd (s.sp * s.slow))
    s.attack <- Acc.hi (Acc.rnd (Frac.ofRaw 0x1dfcd2 * s.attack))
    s.bell <- Acc.hi (Acc.rnd (v.bellDecay * s.bell))
    s.click <- Acc.hi (Acc.rnd (v.clickDecay * s.click))
    let metalTone = match v.layer with Metal _ -> s.metalTone | Bell(_, tone, _, _) -> tone
    let metalNoise = match v.layer with Metal _ -> s.metalNoise | Bell(_, _, noise, _) -> noise
    let opened = Acc.hi (Acc.ofFrac Frac.one - s.attack)
    let amp = Acc.hi (Acc.rnd (fadeGain * Acc.hi (opened * Acc.hi (Acc.ofFrac s.fast + s.slow))))
    let tone = Acc.rnd (amp * Acc.hi (metalTone * Acc.hi (Acc.ofFrac s.bell + Frac.ofRaw 0x400000)))
    s.toneGain <- Acc.hi tone
    let toneStep = Acc.hi ((tone - Acc.ofFrac oldTone) >>> 5)
    let noise = (metalNoise * amp + s.click) >>> 1
    s.noiseGain <- Acc.hi noise
    let noiseStep = Acc.hi ((noise - Acc.ofFrac oldNoise) >>> 5)

    // noise: a linear congruential generator, the low word of the product
    let fill = Cursor.linear Scratch.y Frac.zero
    let mutable rng = s.rng
    for _ in 0 .. 31 do
      rng <- Acc.hi (Acc.ofFrac (Acc.lo ((Frac.ofRaw 0xfd43fd * rng) >>> 1)) + Frac.ofRaw 0xc39ec3)
      fill.Put(Acc.hi (rng * Frac.ofRaw 0x400000))
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

    // two undamped partials, limited, summed into the temporary X memory
    let partial = Cursor.linear Scratch.x Frac.zero
    let mutable s0 = s.s0
    let mutable p0 = s.p0
    for _ in 0 .. 31 do
      let next = Acc.sat (((s0 * s.c0) <<< 1) - p0)
      p0 <- s0
      s0 <- next
      partial.Put next
    s.s0 <- s0
    s.p0 <- p0
    let sum = Cursor.linear Scratch.x Frac.zero
    let mutable s1 = s.s1
    let mutable p1 = s.p1
    for _ in 0 .. 31 do
      let next = ((s1 * s.c1) <<< 1) - p1
      p1 <- s1
      s1 <- Acc.hi next
      sum.Put(Acc.hi (next + sum.Peek()))
    s.s1 <- s1
    s.p1 <- p1

    // the mix: noise and partials, each at its gain ramped per sample (24-bit wrap)
    let toneIn = Cursor.linear Scratch.x Frac.zero
    let noiseIn = Cursor.linear Scratch.y Frac.zero
    let mutable tg = oldTone
    let mutable ng = oldNoise
    for _ in 0 .. 31 do
      let t = toneIn.Next()
      out.Write(Acc.sat ((noiseIn.Next() * ng + tg * t) <<< 2))
      ng <- Acc.hi (Acc.ofFrac ng + noiseStep)
      tg <- Acc.hi (Acc.ofFrac tg + toneStep)

let chInit (_: Params) (s: State) = init ch s
let chTrig (p: Params) (s: State) = trig ch p s
let chRender (_: Params) (s: State) (out: Output) = render ch s out
let ohInit (_: Params) (s: State) = init oh s
let ohTrig (p: Params) (s: State) = trig oh p s
let ohRender (_: Params) (s: State) (out: Output) = render oh s out

let cyInit (_: Params) (s: State) = init cy s
let cyTrig (p: Params) (s: State) = trig cy p s
let cyRender (_: Params) (s: State) (out: Output) = render cy s out

let chKernel = Kernel.define "tx6-ch" <@ chInit @> <@ chTrig @> <@ chRender @>
let ohKernel = Kernel.define "tx6-oh" <@ ohInit @> <@ ohTrig @> <@ ohRender @>
let cyKernel = Kernel.define "tx6-cy" <@ cyInit @> <@ cyTrig @> <@ cyRender @>

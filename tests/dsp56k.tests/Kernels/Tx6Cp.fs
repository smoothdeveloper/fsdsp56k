/// kitbasher's TX6 CP (mds/tx6/cp/cp.asm, MIT; Simple606-inspired, by Matthew Fecher and
/// Fadedlimes, see Tx6CpTables.fs for the notice) as a kernel: LCG noise through a TUNE biquad,
/// under a gain built per block from a burst sequence, a tail, a late tail, a foundation thump
/// and air, then ramped per sample. A frame counter (256 per block, faster while TGHT
/// compresses the opening) indexes the burst, air and fade tables.
[<ReflectedDefinition>]
module samples.kernels.Tx6Cp

open dsp56k.kernel
open samples.kernels.Tx6CpTables

type Params =
  { tune : Frac       // raw 14-bit settings: 128 x the front-panel value
    dec  : Frac
    nois : Frac
    body : Frac
    tght : Frac }

type State =
  { mutable sp : Frac             // tail and late decays, warped by NOIS
    mutable lp : Frac
    mutable b0 : Frac             // noise biquad
    mutable a1 : Frac
    mutable a2 : Frac
    mutable tailStart : Frac
    mutable lateStart : Frac
    mutable airBase : Frac
    mutable airSpread : Frac
    mutable durationWarp : Frac
    mutable rng : Frac
    mutable z1 : Frac
    mutable z2 : Frac
    mutable frame : Frac          // frames x 256
    mutable burstBase : Frac      // the DEC band's first burst word
    mutable tail : Frac
    mutable late : Frac
    mutable tailPole : Frac
    mutable latePole : Frac
    mutable foundation : Frac
    mutable rise : Frac
    mutable foundationStart : Frac
    mutable risePole : Frac
    mutable gain : Frac           // this block's gain
    mutable burstGain : Frac      // BODY, 0..127
    mutable tightStep : Frac }    // frame advance while compressed

/// Row ((setting >> 8) & 63) of a control table.
let row (p: Frac) = Acc.hi (Acc.andHi 0x3f (Acc.ofFrac p >>> 8))

/// A decay coefficient warped by the duration control: a short series for e^-w, then raised to
/// the 32nd power by squaring.
let warp (w: Frac) (x: Frac) =
  let t = Acc.hi (Acc.rnd (w * x))
  let e1 = Acc.hi (Acc.ofFrac (Frac.ofRaw 0x400000) - Acc.rnd (Frac.ofRaw 0x155555 * t))
  let e2 = Acc.hi (Acc.ofFrac Frac.one - Acc.rnd (e1 * t))
  let mutable e = Acc.hi (Acc.ofFrac Frac.one - Acc.rnd (e2 * t))
  for _ in 1 .. 5 do
    e <- Acc.hi (Acc.rnd (e * e))
  e

let at (n: int) = Frac.ofRaw n

let init (_: Params) (s: State) =
  s.sp <- Frac.zero
  s.lp <- Frac.zero
  s.b0 <- Frac.zero
  s.a1 <- Frac.zero
  s.a2 <- Frac.zero
  s.tailStart <- Frac.zero
  s.lateStart <- Frac.zero
  s.airBase <- Frac.zero
  s.airSpread <- Frac.zero
  s.durationWarp <- Frac.zero
  s.rng <- Frac.ofRaw 0x06c1a9
  s.z1 <- Frac.zero
  s.z2 <- Frac.zero
  s.frame <- at 106240                  // past the end: silent until triggered
  s.burstBase <- Frac.zero
  s.tail <- Frac.zero
  s.late <- Frac.zero
  s.tailPole <- Frac.zero
  s.latePole <- Frac.zero
  s.foundation <- Frac.zero
  s.rise <- Frac.zero
  s.foundationStart <- Frac.zero
  s.risePole <- Frac.zero
  s.gain <- Frac.zero
  s.burstGain <- Frac.zero
  s.tightStep <- Frac.zero

let trig (p: Params) (s: State) =
  s.gain <- Frac.zero
  s.z1 <- Frac.zero
  s.z2 <- Frac.zero
  s.frame <- Frac.zero
  s.tail <- Frac.zero
  s.late <- Frac.zero
  s.foundation <- Frac.zero
  s.rise <- Frac.zero
  let d = row p.dec
  s.sp <- Table.row control1 2 d 0
  s.lp <- Table.row control1 2 d 1
  let t = row p.tune
  s.b0 <- Table.row control2 3 t 0
  s.a1 <- Table.row control2 3 t 1
  s.a2 <- Table.row control2 3 t 2
  let n = row p.nois
  s.tailStart <- Table.row control3 5 n 0
  s.lateStart <- Table.row control3 5 n 1
  s.airBase <- Table.row control3 5 n 2
  s.airSpread <- Table.row control3 5 n 3
  s.durationWarp <- Table.row control3 5 n 4
  s.sp <- warp s.durationWarp s.sp
  s.lp <- warp s.durationWarp s.lp
  // the DEC band picks 96 frames of bursts
  let band = Acc.ofFrac (Acc.hi (Acc.andHi 0xf (Acc.ofFrac p.dec >>> 10)))
  s.burstBase <- Acc.hi (((band <<< 1) + band) <<< 5)
  s.tailPole <- Frac.one
  s.latePole <- Frac.one
  s.risePole <- Frac.ofRaw 0x770a7e
  let decay = Acc.hi (Acc.andHi 0x3f80 (Acc.ofFrac p.dec) <<< 9)
  s.foundationStart <- Acc.hi (Acc.rnd (Frac.ofRaw 0x579913 * Acc.hi (Acc.rnd (decay * s.lateStart))))
  s.burstGain <- Acc.hi (Acc.ofFrac p.body >>> 7)
  let tight = Acc.ofFrac p.tght >>> 7
  s.tightStep <- Acc.hi ((((tight <<< 3) - Acc.hi tight) <<< 1) + at 256)

let render (_: Params) (s: State) (out: Output) =
  let previous = s.gain
  let position = Acc.ofFrac s.frame >>> 8
  if position >= Acc.ofFrac (at 415) then
    for _ in 0 .. 31 do
      out.Write Frac.zero
  else
    let frame = Acc.hi position
    // the opening runs faster while TGHT compresses it, then 256 a block
    if position >= Acc.ofFrac (at 45) then
      s.frame <- Acc.hi (Acc.ofFrac s.frame + at 256)
    else
      let mutable next = Acc.ofFrac s.frame + s.tightStep
      if next > Acc.ofFrac (at 11520) then next <- Acc.ofFrac (at 11520)
      s.frame <- Acc.hi next

    // the tails start at frame 45 and take their decays at 72
    if frame = at 45 then
      s.tail <- s.tailStart
      s.late <- s.lateStart
    if frame = at 72 then
      s.tailPole <- s.sp
      s.latePole <- s.lp
    s.tail <- Acc.hi (Acc.rnd (s.tailPole * s.tail))
    s.late <- Acc.hi (Acc.rnd (s.latePole * s.late))

    // the foundation thump, injected at frames 229 and 346
    if frame = at 229 then
      s.foundation <- Acc.hi (Acc.ofFrac s.foundation + s.foundationStart)
      s.rise <- Acc.hi (Acc.ofFrac s.rise + s.foundationStart)
    elif frame = at 346 then
      let second = Acc.hi (Acc.rnd (Frac.ofRaw 0x1745d1 * s.foundationStart))
      s.foundation <- Acc.hi (Acc.ofFrac s.foundation + second)
      s.rise <- Acc.hi (Acc.ofFrac s.rise + second)
    s.foundation <- Acc.hi (Acc.rnd (s.lp * s.foundation))
    s.rise <- Acc.hi (Acc.rnd (s.risePole * s.rise))
    let envelope = Acc.hi (Acc.ofFrac s.foundation - Acc.ofFrac s.rise + s.tail + s.late)

    // air: a level per frame, spread around a base
    let mutable airFrame = frame
    if airFrame > at 235 then airFrame <- at 235
    let airLevel = Acc.hi (s.airSpread * Table.read air airFrame + Acc.ofFrac s.airBase)
    s.gain <- Acc.hi ((envelope * airLevel) <<< 2)

    // the burst sequence, scaled by BODY below 127
    let mutable burstFrame = frame
    if burstFrame > at 95 then burstFrame <- at 95
    let mutable burst = Table.read bursts (Acc.hi (Acc.ofFrac s.burstBase + burstFrame))
    if s.burstGain <> at 127 then
      burst <- Acc.hi (Acc.rnd (Acc.hi (Acc.ofFrac s.burstGain <<< 16) * burst))
    s.gain <- Acc.hi (Acc.ofFrac s.gain + burst)

    // the last 16 frames fade out
    let fromEnd = Acc.ofFrac frame - at 399
    if fromEnd >= Acc.zero then
      s.gain <- Acc.hi (Acc.rnd (s.gain * Table.read fade (Acc.hi fromEnd)))
    let step = Acc.hi ((Acc.ofFrac s.gain - previous) >>> 5)

    // noise: a linear congruential generator, the low word of the product
    let fill = Cursor.linear Scratch.y Frac.zero
    let mutable rng = s.rng
    for _ in 0 .. 31 do
      rng <- Acc.hi (Acc.ofFrac (Acc.lo ((Frac.ofRaw 0xfd43fd * rng) >>> 1)) + Frac.ofRaw 0xc39ec3)
      fill.Put(Acc.hi (rng * Frac.ofRaw 0x200000))
    s.rng <- rng

    // the TUNE biquad, straight to the output at the ramped gain
    let noise = Cursor.linear Scratch.y Frac.zero
    let mutable z1 = s.z1
    let mutable z2 = s.z2
    let mutable g = previous
    for _ in 0 .. 31 do
      let fed = (noise.Next() * s.b0) <<< 1
      let y = Acc.hi (fed + z1)
      g <- Acc.hi (Acc.ofFrac g + step)
      z1 <- Acc.hi (((s.a1 * y) <<< 1) + z2)
      z2 <- Acc.hi (((s.a2 * y) <<< 1) - Acc.hi fed)
      out.Write(Acc.sat ((g * y) <<< 4))
    s.z1 <- z1
    s.z2 <- z2

let kernel = Kernel.define "tx6-cp" <@ init @> <@ trig @> <@ render @>

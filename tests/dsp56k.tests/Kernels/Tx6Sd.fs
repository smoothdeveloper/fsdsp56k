/// kitbasher's TX6 SD (mds/tx6/sd/sd.asm, MIT; Simple606-inspired, by Matthew Fecher and
/// Fadedlimes, see Tx6SdTables.fs for the notice) as a kernel: a gliding damped body mode and LCG
/// noise through a COLR biquad, each under a fast + slow gain ramped per sample. A gate holds the
/// snare open for DEC's hold time, then speeds every decay up once.
[<ReflectedDefinition>]
module samples.kernels.Tx6Sd

open dsp56k.kernel
open samples.kernels.Tx6SdTables

type Params =
  { tune : Frac       // raw 14-bit settings: 128 x the front-panel value
    dec  : Frac
    snap : Frac
    colr : Frac
    body : Frac }

type State =
  { mutable remaining : Frac
    mutable gateHold : Frac       // blocks before the gate closes; -1 once it has
    mutable gatePole : Frac       // what closing the gate multiplies the decays by
    mutable p0 : Frac             // body mode: previous sample, cosine base
    mutable cos0 : Frac
    mutable qbase : Frac          // pitch glide: base and offset
    mutable bodyPole0 : Frac      // body gain decays, fast and slow
    mutable b0 : Frac             // noise biquad
    mutable a1 : Frac
    mutable a2 : Frac
    mutable fast : Frac           // noise gain, fast + slow
    mutable slow : Frac
    mutable rng : Frac
    mutable z1 : Frac
    mutable z2 : Frac
    mutable fp : Frac             // noise gain decays
    mutable sp : Frac
    mutable noiseAmp : Frac       // last block's noise gain
    mutable glide : Frac
    mutable s0 : Frac             // body mode: sample, coefficient, damping
    mutable c0 : Frac
    mutable d0 : Frac
    mutable r0 : Frac
    mutable bodyFast : Frac       // body gain, fast + slow
    mutable bodySlow : Frac
    mutable bodyPole1 : Frac }

/// Row ((setting >> 8) & 63) of a control table.
let row (p: Frac) = Acc.hi (Acc.andHi 0x3f (Acc.ofFrac p >>> 8))

let init (_: Params) (s: State) =
  s.remaining <- Frac.zero
  s.gateHold <- Frac.zero
  s.gatePole <- Frac.zero
  s.p0 <- Frac.zero
  s.cos0 <- Frac.zero
  s.qbase <- Frac.zero
  s.bodyPole0 <- Frac.zero
  s.b0 <- Frac.zero
  s.a1 <- Frac.zero
  s.a2 <- Frac.zero
  s.fast <- Frac.zero
  s.slow <- Frac.zero
  s.rng <- Frac.ofRaw 0x006063
  s.z1 <- Frac.zero
  s.z2 <- Frac.zero
  s.fp <- Frac.zero
  s.sp <- Frac.zero
  s.noiseAmp <- Frac.zero
  s.glide <- Frac.zero
  s.s0 <- Frac.zero
  s.c0 <- Frac.zero
  s.d0 <- Frac.zero
  s.r0 <- Frac.zero
  s.bodyFast <- Frac.zero
  s.bodySlow <- Frac.zero
  s.bodyPole1 <- Frac.zero

let trig (p: Params) (s: State) =
  s.z1 <- Frac.zero
  s.z2 <- Frac.zero
  s.noiseAmp <- Frac.zero
  let d = row p.dec
  s.remaining <- Table.row control1 3 d 0
  s.gateHold <- Table.row control1 3 d 1
  s.gatePole <- Table.row control1 3 d 2
  let t = row p.tune
  s.p0 <- Table.row control2 4 t 0
  s.cos0 <- Table.row control2 4 t 1
  s.qbase <- Table.row control2 4 t 2
  s.bodyPole0 <- Table.row control2 4 t 3
  let c = row p.colr
  s.b0 <- Table.row control4 3 c 0
  s.a1 <- Table.row control4 3 c 1
  s.a2 <- Table.row control4 3 c 2
  let n = row p.snap
  s.fast <- Table.row control3 2 n 0
  s.slow <- Table.row control3 2 n 1
  s.fp <- Frac.ofRaw 0x544ba2
  s.sp <- Frac.ofRaw 0x7e62d0
  s.s0 <- Frac.ofRaw 0x3af49a
  s.r0 <- Frac.one
  s.glide <- Frac.ofRaw 0x02a601
  s.bodyPole1 <- Frac.ofRaw 0x6c4e06
  s.d0 <- Frac.ofRaw 0x400000
  s.c0 <- s.cos0
  // BODY picks the body gains
  let b = Acc.hi (Acc.andHi 0x7e (Acc.ofFrac p.body >>> 7))
  s.bodyFast <- Table.row layers 1 b 0
  s.bodySlow <- Table.row layers 1 b 1

let render (_: Params) (s: State) (out: Output) =
  if s.remaining = Frac.zero then
    for _ in 0 .. 31 do
      out.Write Frac.zero
  else
    s.remaining <- Acc.hi (Acc.ofFrac s.remaining - Frac.ofRaw 1)

    // the gate: counts down, then speeds every decay up once
    if s.gateHold < Frac.zero then ()
    elif s.gateHold = Frac.zero then
      s.gateHold <- Frac.ofRaw 0xffffff
      s.fp <- Acc.hi (Acc.rnd (s.gatePole * s.fp))
      s.sp <- Acc.hi (Acc.rnd (s.gatePole * s.sp))
      s.bodyPole0 <- Acc.hi (Acc.rnd (s.gatePole * s.bodyPole0))
      s.bodyPole1 <- Acc.hi (Acc.rnd (s.gatePole * s.bodyPole1))
    else
      s.gateHold <- Acc.hi (Acc.ofFrac s.gateHold - Frac.ofRaw 1)

    // the body's pitch glides down to qbase
    let q = Acc.hi (Acc.ofFrac s.qbase + s.glide)
    s.glide <- Acc.hi (Acc.rnd (Frac.ofRaw 0x79380e * s.glide))
    s.c0 <- Acc.hi (-((q * q) >>> 1) + Frac.one)

    // noise: a linear congruential generator, the low word of the product
    let noise = Cursor.linear Scratch.y Frac.zero
    let mutable rng = s.rng
    for _ in 0 .. 31 do
      rng <- Acc.hi (Acc.ofFrac (Acc.lo ((Frac.ofRaw 0xfd43fd * rng) >>> 1)) + Frac.ofRaw 0xc39ec3)
      noise.Put(Acc.hi (rng * Frac.ofRaw 0x400000))
    s.rng <- rng

    // the COLR biquad, in place (transposed direct form)
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

    // the body mode, rounded: next = 2 (c s - d p)
    let tone = Cursor.linear Scratch.x Frac.zero
    let mutable s0 = s.s0
    let mutable p0 = s.p0
    for _ in 0 .. 31 do
      let next = Acc.hi (Acc.rnd ((s.c0 * s0 - s.d0 * p0) <<< 1))
      p0 <- s0
      s0 <- next
      tone.Put next
    s.s0 <- s0
    s.p0 <- p0

    // gains: body and noise, each fast + slow, ramped from the last block's
    let bodyFrom = Acc.hi (Acc.ofFrac s.bodySlow + s.bodyFast)
    s.bodyFast <- Acc.hi (Acc.rnd (s.bodyPole0 * s.bodyFast))
    s.bodySlow <- Acc.hi (Acc.rnd (s.bodyPole1 * s.bodySlow))
    let bodyStep = Acc.hi (((Acc.ofFrac s.bodySlow + s.bodyFast) - bodyFrom) >>> 5)
    s.fast <- Acc.hi (Acc.rnd (s.fp * s.fast))
    s.slow <- Acc.hi (Acc.rnd (s.sp * s.slow))
    let noiseFrom = s.noiseAmp
    s.noiseAmp <- Acc.hi (Acc.ofFrac s.slow + s.fast)
    let noiseStep = Acc.hi ((Acc.ofFrac s.noiseAmp - noiseFrom) >>> 5)

    // the mix: noise doubled, plus the body (24-bit gain ramps, as address registers)
    let toneIn = Cursor.linear Scratch.x Frac.zero
    let noiseIn = Cursor.linear Scratch.y Frac.zero
    let mutable ng = noiseFrom
    let mutable bg = bodyFrom
    for _ in 0 .. 31 do
      let t = toneIn.Next()
      out.Write(Acc.sat (((noiseIn.Next() * ng) <<< 1) + bg * t))
      ng <- Acc.hi (Acc.ofFrac ng + noiseStep)
      bg <- Acc.hi (Acc.ofFrac bg + bodyStep)

let kernel = Kernel.define "tx6-sd" <@ init @> <@ trig @> <@ render @>

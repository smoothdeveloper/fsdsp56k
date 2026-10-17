/// kitbasher's TX6 BD (mds/tx6/bd/bd.asm, MIT; Simple606-inspired, by Matthew Fecher and
/// Fadedlimes, see Tx6BdTables.fs for the notice) as a kernel: a swept sine body from a
/// two-pole resonator, a HEAT-controlled third harmonic, a low-passed noise click whose gain
/// ramps per sample, and a gain envelope interpolated per sample. The arithmetic is the
/// original's, step for step (which products are rounded, which accumulator bits are kept), so
/// the output is word for word the same; the structure is F#.
[<ReflectedDefinition>]
module samples.kernels.Tx6Bd

open dsp56k.kernel
open samples.kernels.Tx6BdTables

type Params =
  { tune : Frac       // raw 14-bit settings: 128 x the front-panel value
    dec  : Frac
    tran : Frac
    heat : Frac
    xl   : Frac
    clik : Frac }

type State =
  { mutable amp        : Frac      // body gain, decaying by ampPole each block
    mutable freq       : Frac      // resonator frequency, sweeping towards target
    mutable pitch      : Frac      // sweep rate
    mutable click      : Frac      // click level, decaying by clickPole
    mutable clickPole  : Frac
    mutable clickGain  : Frac
    mutable decayNormal: Frac
    mutable decayXl    : Frac
    mutable tune       : Frac
    mutable heat       : Frac
    mutable norm       : Frac
    mutable rng        : Frac      // noise generator state
    mutable remaining  : Frac      // blocks left to play
    mutable ampPole    : Frac
    mutable target     : Frac
    mutable u          : Frac      // body resonator: this and the previous sample
    mutable v          : Frac
    mutable clickLp    : Frac      // click low-pass state
    mutable bodyGain   : Frac      // last block's amp
    mutable harmonic   : Frac      // third harmonic resonator
    mutable harmonicPrevious : Frac }

let half = Frac.ofRaw 0x400000

/// Row ((setting >> 8) & 63) of a control table: the 64 rows cover the 128 settings.
let row (p: Frac) = Acc.hi (Acc.andHi 0x3f (Acc.ofFrac p >>> 8))

let init (_: Params) (s: State) =
  s.amp <- Frac.zero
  s.freq <- Frac.zero
  s.pitch <- Frac.zero
  s.click <- Frac.zero
  s.clickPole <- Frac.zero
  s.clickGain <- Frac.zero
  s.decayNormal <- Frac.zero
  s.decayXl <- Frac.zero
  s.tune <- Frac.zero
  s.heat <- Frac.zero
  s.norm <- Frac.zero
  s.rng <- Frac.ofRaw 0x606606
  s.remaining <- Frac.zero
  s.ampPole <- Frac.zero
  s.target <- Frac.zero
  s.u <- Frac.zero
  s.v <- Frac.zero
  s.clickLp <- Frac.zero
  s.bodyGain <- Frac.zero
  s.harmonic <- Frac.zero
  s.harmonicPrevious <- Frac.zero

let trig (p: Params) (s: State) =
  // a short click impulse; the click filter drains it immediately
  s.clickLp <- Frac.ofRaw 0x880000
  // the controls, through their tables
  let t = row p.tran
  s.amp <- Table.row control1 6 t 0
  s.freq <- Table.row control1 6 t 1
  s.pitch <- Table.row control1 6 t 2
  s.click <- Table.row control1 6 t 3
  s.clickPole <- Table.row control1 6 t 4
  s.clickGain <- Table.row control1 6 t 5
  let d = row p.dec
  s.decayNormal <- Table.row control2 2 d 0
  s.decayXl <- Table.row control2 2 d 1
  s.tune <- Table.row control3 1 (row p.tune) 0
  let h = row p.heat
  s.heat <- Table.row control4 2 h 0
  s.norm <- Table.row control4 2 h 1
  s.freq <- Acc.hi ((s.tune * s.freq) <<< 2)
  s.target <- Acc.hi ((s.tune * Frac.ofRaw 0x01de71) <<< 2)
  // XL blends the two decay coefficients
  let w = Table.read xlWeights (Acc.hi (Acc.ofFrac p.xl >>> 7))
  let span = Acc.hi (Acc.ofFrac s.decayXl - s.decayNormal)
  s.ampPole <- Acc.hi (Acc.rnd (span * w) + s.decayNormal)
  // both resonators start from rest, a quarter period in
  s.u <- Frac.zero
  s.harmonic <- Frac.zero
  let quarter = -(Acc.ofFrac s.freq >>> 2)
  s.v <- Acc.hi quarter
  let third = Acc.hi ((quarter <<< 1) + Acc.hi quarter)
  s.harmonicPrevious <- Acc.hi (Acc.rnd (s.heat * third))
  s.remaining <- Frac.ofRaw 16384
  // CLIK below 127 scales the click
  let c = Acc.ofFrac p.clik >>> 7
  if c <> Acc.ofFrac (Frac.ofRaw 127) then
    s.clickGain <- Acc.hi (Acc.rnd (Acc.hi (c <<< 16) * s.clickGain))

let render (_: Params) (s: State) (out: Output) =
  if s.remaining = Frac.zero then
    for _ in 0 .. 31 do
      out.Write Frac.zero
  else
    s.remaining <- Acc.hi (Acc.ofFrac s.remaining - Frac.ofRaw 1)

    // the sweep: freq moves towards target; the resonator runs at the block's mean frequency
    let oldFreq = s.freq
    let swept = Acc.hi (Acc.ofFrac oldFreq - s.target) * s.pitch + Acc.ofFrac s.target
    s.freq <- Acc.hi swept
    let mean = Acc.hi ((swept + oldFreq) >>> 2)

    // envelopes: body and click, each with a per-sample ramp over the block
    let oldAmp = s.amp
    s.amp <- Acc.hi (Acc.rnd (s.ampPole * s.amp))
    s.bodyGain <- oldAmp
    let clickStart = Acc.hi (Acc.rnd (s.clickGain * s.click))
    s.click <- Acc.hi (Acc.rnd (s.clickPole * s.click))
    let clickStep = Acc.hi ((Acc.rnd (s.clickGain * s.click) - Acc.ofFrac clickStart) >>> 5)

    // the body: a two-pole resonator, u' = 2 (c u - v / 2)
    let squared = (mean * mean) >>> 1
    let coef = Acc.hi (-squared + Frac.one)
    let body = Cursor.linear Scratch.x Frac.zero
    let mutable u = s.u
    let mutable v = s.v
    for _ in 0 .. 31 do
      let next = Acc.hi ((coef * u - half * v) <<< 1)
      v <- u
      u <- next
      body.Put next
    s.u <- u
    s.v <- v

    // the third harmonic (9x the squared frequency), rounded, added to the body
    let sq = Acc.hi squared
    let coef3 = Acc.hi (-((Acc.ofFrac sq <<< 3) + sq) + Frac.one)
    let mix = Cursor.linear Scratch.x Frac.zero
    let mutable h = s.harmonic
    let mutable hp = s.harmonicPrevious
    for _ in 0 .. 31 do
      let next = Acc.rnd ((coef3 * h - half * hp) <<< 1)
      hp <- h
      h <- Acc.hi next
      mix.Put(Acc.hi (next + mix.Peek()))
    s.harmonic <- h
    s.harmonicPrevious <- hp

    // white noise: a linear congruential generator, the low word of the product
    let white = Cursor.linear Scratch.y Frac.zero
    let mutable rng = s.rng
    for _ in 0 .. 31 do
      rng <- Acc.hi (Acc.ofFrac (Acc.lo ((Frac.ofRaw 0x19660d * rng) >>> 1)) + Frac.ofRaw 0x6ef35f)
      white.Put(Acc.hi (rng * Frac.ofRaw 0x080000))
    s.rng <- rng

    // the click: low-passed noise, its gain stepping by clickStep (24-bit wrap, as an address register)
    let click = Cursor.linear Scratch.y Frac.zero
    let mutable lp = s.clickLp
    let mutable gain = clickStart
    for _ in 0 .. 31 do
      lp <- Acc.hi (Acc.hi (Acc.ofFrac (click.Peek()) - lp) * Frac.ofRaw 0x2c8537 + lp)
      click.Put(Acc.hi (lp * gain))
      gain <- Acc.hi (Acc.ofFrac gain + clickStep)
    s.clickLp <- lp

    // the mix: body at the interpolated gain, plus the click, doubled
    let from = Acc.rnd (s.norm * s.bodyGain)
    let step = Acc.hi ((Acc.rnd (s.norm * s.amp) - from) >>> 5)
    let bodyIn = Cursor.linear Scratch.x Frac.zero
    let clickIn = Cursor.linear Scratch.y Frac.zero
    let mutable level = from
    let mutable g = Acc.hi from
    for _ in 0 .. 31 do
      level <- level + step
      out.Write(Acc.sat ((g * bodyIn.Next() + clickIn.Next()) <<< 1))
      g <- Acc.sat level

let kernel = Kernel.define "tx6-bd" <@ init @> <@ trig @> <@ render @>

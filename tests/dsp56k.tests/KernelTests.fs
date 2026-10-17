/// Kernel F#: the .NET side of every intrinsic against the compiled program on the emulator
/// (random and extreme parameter words), the sine kernel's grade against the shipped machine,
/// and the compiler's refusals.
module dsp56k.tests.KernelTests

open System
open Expecto
open dsp56k.kernel
open dsp56k.kernel.Grade
open machinedrum
open dsp56k.tests.Fixtures

[<ReflectedDefinition>]
module Probes =
  type P =
    { a : Frac
      b : Frac
      c : Frac
      d : Frac }

  type S =
    { mutable acc : Long
      mutable w   : Frac }

  let init (_: P) (s: S) =
    s.acc <- Long.zero
    s.w <- Frac.zero

  let trig (_: P) (_: S) = ()

  /// Arithmetic: every Acc operation, the limiter, spills of non-leaf operands.
  let arithmetic (p: P) (s: S) (out: Output) =
    let mutable x = Acc.ofLong s.acc
    for _ in 0 .. 3 do
      x <- Acc.mac x p.a p.b
      out.Write(Acc.sat x)
      out.Write(Acc.hi (x >>> 3))
      out.Write(Acc.lo x)
      out.Write(Acc.sat (x <<< 4))
      out.Write(Acc.sat (p.c * p.d - x))
      out.Write(Acc.hi (Acc.andHi 0x00ff0f (x + p.c)))
      out.Write(Acc.sat (-x + Acc.ofFrac p.d - p.a))
      out.Write(Acc.sat ((p.a * p.a) + (p.b * p.c)))
    s.acc <- Long.ofAcc x
    s.w <- Acc.sat x

  let half (x: Frac) = Acc.hi (Acc.ofFrac x >>> 1)

  /// Pipelines, as in the DigiPRO translation: `... |> Acc.sat`.
  let piped (p: P) (s: S) (out: Output) =
    for _ in 0 .. 15 do
      out.Write(Acc.mac (Acc.ofFrac p.a) p.b p.c |> Acc.sat)
      s.w <- p.d |> half
      out.Write <| (Acc.ofFrac s.w >>> 2 |> Acc.hi)

  /// Control: comparisons, if/else, a table, constants, an inlined helper.
  let control (p: P) (s: S) (out: Output) =
    let mutable phase = Acc.ofLong s.acc
    let step = Acc.ofFrac p.a >>> 8
    for _ in 0 .. 15 do
      phase <- Acc.andHi 0x7fff (phase + step)
      let v = Table.read Rom.sine (Acc.hi phase)
      let mutable y = Acc.ofFrac v
      if y < Acc.ofFrac p.b then y <- Acc.ofFrac p.b
      if p.c >= v then y <- y - Acc.ofFrac Frac.one else y <- y + Frac.q23 0.25
      out.Write(Acc.sat y)
      out.Write(half v)
    s.acc <- Long.ofAcc phase
    s.w <- Acc.hi phase

  let probeTable = Table.program "probe" [| for i in 0 .. 47 -> (i * 0x05a5a5 + 0x123) &&& 0xffffff |]

  /// Memory: cursors (linear, ring with a step, peek/put/next), the shared temporary memory, the
  /// input ring and position, program tables, MPYR/MACR/MAC-, the LFSR.
  let memory (p: P) (s: S) (out: Output) =
    let fill = Cursor.linear Scratch.x Frac.zero
    let mutable n = s.w
    for _ in 0 .. 7 do
      n <- Frac.lfsr 0xc20001 n
      fill.Put n
    let r = Cursor.linear Scratch.x Frac.zero
    let input = Cursor.ring Input.ring (Acc.hi (Acc.andHi 0xff (Acc.ofFrac p.a))) 3
    for _ in 0 .. 7 do
      let x = r.Peek()
      r.Put(Acc.hi (Acc.rnd (x * p.b)))
      out.Write(Acc.sat (Acc.rnd (Acc.ofFrac p.c + x * p.d)))
      out.Write(Acc.sat (Acc.ofFrac p.c - x * input.Next()))
      out.Write(Table.row probeTable 3 (Acc.hi (Acc.andHi 0xf (Acc.ofFrac x))) 2)
      out.Write(Acc.hi (Acc.ofFrac (Cell.read Input.position) - Region.addressOf Input.ring))
    s.w <- n
    s.acc <- Long.ofAcc (Acc.rnd (-(p.a * p.b)))

  /// DIV: 24 non-restoring steps from dividends and divisors of either sign.
  let divide (p: P) (s: S) (out: Output) =
    let mutable x = Acc.ofLong s.acc
    for _ in 0 .. 7 do
      let q = Acc.divide (Acc.ofFrac p.a >>> 3) p.b
      out.Write(Acc.lo q)
      out.Write(Acc.hi q)
      let r = Acc.divide (x + p.c) p.d
      out.Write(Acc.lo r)
      out.Write(Acc.sat r)
      x <- Acc.ofFrac (Acc.lo r) >>> 5
    s.acc <- Long.ofAcc x

  /// DIV with no ASL first and the carry given: both carries, dividends and divisors of either sign.
  let divideCarry (p: P) (s: S) (out: Output) =
    let mutable x = Acc.ofLong s.acc
    for _ in 0 .. 7 do
      let q = Acc.divideCarry 0 (Acc.ofFrac p.a >>> 4) p.b
      out.Write(Acc.lo q)
      out.Write(Acc.hi q)
      let r = Acc.divideCarry 1 (x + p.c) p.d
      out.Write(Acc.lo r)
      out.Write(Acc.sat r)
      x <- Acc.ofFrac (Acc.lo r) >>> 6
    s.acc <- Long.ofAcc x

  /// MPYSU, MACSU and DMACSS: a word times a 48-bit level hi:lo, and the level decaying by it.
  let mulSu (p: P) (s: S) (out: Output) =
    let mutable level = Acc.ofLong s.acc
    for _ in 0 .. 15 do
      let hi = Acc.hi level
      let lo = Acc.lo level
      let g = Acc.dmac (Frac.mulSU p.a lo) p.a hi
      out.Write(Acc.sat g)
      out.Write(Acc.lo (Acc.macSU (Frac.mulSU p.b p.c) p.c lo))
      level <- Acc.dmac (Frac.mulSU p.d lo) p.d hi
    s.acc <- Long.ofAcc level

  /// CLB then NORMF: values of either sign normalised, their counts, and shifts both ways.
  let normalise (p: P) (s: S) (out: Output) =
    let mutable x = Acc.ofLong s.acc
    for _ in 0 .. 7 do
      let c = Acc.hi (Acc.clb x)
      out.Write(c)
      out.Write(Acc.hi (Acc.normf c x))
      out.Write(Acc.lo (Acc.normf p.a (Acc.ofFrac p.b)))
      out.Write(Acc.hi (Acc.clb (Acc.ofFrac p.c >>> 7)))
      x <- (x >>> 3) + Acc.ofFrac p.d
    s.acc <- Long.ofAcc x

  /// CLB and NORMF: leading bits counted (zero, all ones, the extension in use, products) and
  /// accumulators shifted by them and by any signed count (|count| mod 64, either way).
  let normaliseEdges (p: P) (s: S) (out: Output) =
    let mutable x = Acc.ofLong s.acc + p.a
    for _ in 0 .. 1 do
      let e = Acc.hi (Acc.clb x)
      out.Write e
      let n = Acc.normf e x
      out.Write(Acc.hi n)
      out.Write(Acc.lo n)
      out.Write(Acc.hi (Acc.clb n))
      out.Write(Acc.sat (Acc.normf (Acc.hi (Acc.clb x)) x))
      let k = Acc.hi (Acc.ofFrac p.b >>> 17)
      let y = Acc.normf k (p.c * p.d)
      out.Write(Acc.sat y)
      out.Write(Acc.lo y)
      out.Write(Acc.hi (Acc.clb y))
      let z = Acc.normf p.a (Acc.ofFrac p.d + Acc.ofFrac p.d)
      out.Write(Acc.sat z)
      out.Write(Acc.hi z)
      out.Write(Acc.lo z)
      out.Write(Acc.hi (Acc.clb z))
      out.Write(Acc.hi (Acc.clb (Acc.ofFrac p.d + Acc.ofFrac p.d)))
      out.Write(Acc.hi (Acc.normf k x))
      out.Write(Acc.hi (Acc.clb (Acc.ofFrac p.c - Acc.ofFrac p.c)))
      out.Write(Acc.hi (Acc.clb (Acc.ofFrac p.c - Acc.ofFrac p.c - Acc.ofRaw 1L)))
      x <- (x >>> 5) + p.b * p.c
    s.acc <- Long.ofAcc x

  let readsIndex (_: P) (_: S) (out: Output) =
    for i in 0 .. 31 do
      out.Write(Frac.ofRaw i)

  let scalesIndex (_: P) (_: S) (out: Output) =
    for i in 0 .. 31 do
      out.Write(Frac.ofRaw (i * 2))

  let arithmeticKernel = Kernel.define "probe-arithmetic" <@ init @> <@ trig @> <@ arithmetic @>
  let controlKernel = Kernel.define "probe-control" <@ init @> <@ trig @> <@ control @>
  let divideKernel = Kernel.define "probe-divide" <@ init @> <@ trig @> <@ divide @>
  let divideCarryKernel = Kernel.define "probe-divide-carry" <@ init @> <@ trig @> <@ divideCarry @>
  let mulSuKernel = Kernel.define "probe-mulsu-dmac" <@ init @> <@ trig @> <@ mulSu @>
  let normaliseKernel = Kernel.define "probe-clb-normf" <@ init @> <@ trig @> <@ normalise @>
  let normaliseEdgesKernel = Kernel.define "probe-clb-normf-edges" <@ init @> <@ trig @> <@ normaliseEdges @>
  let pipedKernel = Kernel.define "probe-piped" <@ init @> <@ trig @> <@ piped @>
  let memoryKernel = Kernel.define "probe-memory" <@ init @> <@ trig @> <@ memory @>
  let indexKernel = Kernel.define "probe-index" <@ init @> <@ trig @> <@ readsIndex @>
  let scaledIndexKernel = Kernel.define "probe-scaled-index" <@ init @> <@ trig @> <@ scalesIndex @>

/// A reflected helper taking
/// the parameter and state records and mutating the state.
[<ReflectedDefinition>]
module Helpers =
  type Coefficients =
    { b0 : Frac
      b1 : Frac
      b2 : Frac
      a1 : Frac
      a2 : Frac }

  type History =
    { mutable x1 : Frac
      mutable x2 : Frac
      mutable y1 : Frac
      mutable y2 : Frac
      mutable noise : Frac }

  let init (_: Coefficients) (h: History) =
    h.x1 <- Frac.zero
    h.x2 <- Frac.zero
    h.y1 <- Frac.zero
    h.y2 <- Frac.zero
    h.noise <- Frac.ofRaw 0x123456

  let trig (_: Coefficients) (_: History) = ()

  let biquadStep (c: Coefficients) (h: History) (x: Frac) =
    let acc = c.b0 * x + c.b1 * h.x1 + c.b2 * h.x2 - c.a1 * h.y1 - c.a2 * h.y2
    let y = Acc.sat (acc <<< 1)
    h.x2 <- h.x1
    h.x1 <- x
    h.y2 <- h.y1
    h.y1 <- y
    y

  let render (c: Coefficients) (h: History) (out: Output) =
    for _ in 0 .. 31 do
      h.noise <- Frac.lfsr 0xc20001 h.noise
      out.Write(biquadStep c h (Acc.hi (Acc.ofFrac h.noise >>> 3)))

  let kernel = Kernel.define "probe-helper-records" <@ init @> <@ trig @> <@ render @>

/// Persistent state beyond words: an Acc kept exact across blocks (extension byte included) and a
/// fixed-length array of words.
[<ReflectedDefinition>]
module Banks =
  type P =
    { a : Frac
      b : Frac
      c : Frac
      d : Frac }

  type S =
    { mutable total : Acc
      [<Length 4>] mutable history : Frac array
      mutable noise : Frac }

  let init (_: P) (s: S) =
    s.total <- Acc.zero
    s.history[0] <- Frac.zero
    s.history[1] <- Frac.zero
    s.history[2] <- Frac.zero
    s.history[3] <- Frac.zero
    s.noise <- Frac.ofRaw 0x5a5a5a

  let trig (_: P) (_: S) = ()

  /// One-pole stage k of the cascade.
  let stage (s: S) (pole: Frac) (k: int) (x: Frac) =
    s.history[k] <- Acc.hi (Acc.rnd (pole * s.history[k]) + x)
    s.history[k]

  let render (p: P) (s: S) (out: Output) =
    for _ in 0 .. 15 do
      s.noise <- Frac.lfsr 0xc20001 s.noise
      let x = Acc.hi (Acc.ofFrac s.noise >>> 2)
      let y = stage s p.d 3 (stage s p.c 2 (stage s p.b 1 (stage s p.a 0 x)))
      s.total <- s.total + y * p.d
      out.Write(Acc.sat s.total)
      out.Write(Acc.hi (s.total >>> 8))

  let kernel = Kernel.define "probe-banks" <@ init @> <@ trig @> <@ render @>

/// Arrays indexed by the loop: a delay line shifted in place and an FIR over it.
[<ReflectedDefinition>]
module Fir =
  type S =
    { [<Length 8>] mutable line : Frac array
      mutable noise : Frac }

  let taps = Table.program "fir_taps" [| 0x0a0000; 0x180000; 0x2c0000; 0x3a0000; 0x3a0000; 0x2c0000; 0x180000; 0x0a0000 |]

  let init (_: Banks.P) (s: S) =
    for i in 0 .. 7 do
      s.line[i] <- Frac.zero
    s.noise <- Frac.ofRaw 0x2468ac

  let trig (_: Banks.P) (_: S) = ()

  let render (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 31 do
      for i in 0 .. 6 do
        s.line[i] <- s.line[i + 1]
      s.noise <- Frac.lfsr 0xc20001 s.noise
      s.line[7] <- Acc.hi (Acc.ofFrac s.noise >>> 1)
      let mutable sum = Acc.zero
      for i in 0 .. 7 do
        sum <- sum + s.line[i] * Table.read taps (Frac.ofRaw i)
      out.Write(Acc.sat (Acc.sat sum * p.a))

  let kernel = Kernel.define "probe-fir" <@ init @> <@ trig @> <@ render @>

/// State in X memory: every field [<XMemory>] (a word, a Long, an Acc, a delay line), the
/// parameters still in Y.
[<ReflectedDefinition>]
module XOnly =
  type S =
    { [<XMemory>] mutable noise : Frac
      [<XMemory>] mutable phase : Long
      [<XMemory>] mutable total : Acc
      [<XMemory; Length 8>] mutable line : Frac array }

  let init (_: Banks.P) (s: S) =
    s.noise <- Frac.ofRaw 0x13579b
    s.phase <- Long.zero
    s.total <- Acc.zero
    for i in 0 .. 7 do
      s.line[i] <- Frac.zero

  let trig (_: Banks.P) (s: S) = s.phase <- Long.zero

  let render (p: Banks.P) (s: S) (out: Output) =
    let mutable phase = Acc.ofLong s.phase
    for _ in 0 .. 31 do
      for i in 0 .. 6 do
        s.line[i] <- s.line[i + 1]
      s.noise <- Frac.lfsr 0xc20001 s.noise
      s.line[7] <- Acc.hi (Acc.ofFrac s.noise >>> 2)
      phase <- Acc.andHi 0x7fffff (phase + (Acc.ofFrac p.a >>> 6))
      s.total <- s.total + s.line[0] * p.b - s.line[3] * p.c
      out.Write(Acc.sat (Acc.hi phase * p.d + (s.total >>> 4)))
    s.phase <- Long.ofAcc phase

  let kernel = Kernel.define "probe-x-state" <@ init @> <@ trig @> <@ render @>

/// Eight state arrays (four in X, four in Y) stepped by one loop index: more than r1..r5, so
/// the arrays past the fifth ride on a register the loop steps already, at their distance.
[<ReflectedDefinition>]
module ManyArrays =
  type S =
    { mutable seed : Frac
      [<XMemory; Length 8>] mutable x0 : Frac array
      [<XMemory; Length 8>] mutable x1 : Frac array
      [<XMemory; Length 8>] mutable x2 : Frac array
      [<XMemory; Length 8>] mutable x3 : Frac array
      [<Length 8>] mutable y0 : Frac array
      [<Length 8>] mutable y1 : Frac array
      [<Length 8>] mutable y2 : Frac array
      [<Length 8>] mutable y3 : Frac array }

  let init (_: Banks.P) (s: S) =
    s.seed <- Frac.ofRaw 0x13579b
    for i in 0 .. 7 do
      s.x0[i] <- Frac.zero
      s.x1[i] <- Frac.zero
      s.x2[i] <- Frac.zero
      s.x3[i] <- Frac.zero
      s.y0[i] <- Frac.zero
      s.y1[i] <- Frac.zero
      s.y2[i] <- Frac.zero
      s.y3[Frac.index (Frac.ofRaw i)] <- Frac.zero

  let trig (_: Banks.P) (_: S) = ()

  let render (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 31 do
      for i in 0 .. 6 do
        s.x0[i] <- s.x0[i + 1]
        s.x1[i] <- s.x1[i + 1]
        s.x2[i] <- s.x2[i + 1]
        s.x3[i] <- s.x3[i + 1]
        s.y0[i] <- s.y0[i + 1]
        s.y1[i] <- s.y1[i + 1]
        s.y2[i] <- s.y2[i + 1]
        s.y3[i] <- s.y3[i + 1]
      s.seed <- Frac.lfsr 0xc20001 s.seed
      s.x0[7] <- s.seed
      s.x1[7] <- Acc.hi (Acc.ofFrac s.seed >>> 1)
      s.x2[7] <- p.a
      s.x3[7] <- p.b
      s.y0[7] <- p.c
      s.y1[7] <- p.d
      s.y2[7] <- Acc.sat (s.seed * p.a)
      s.y3[7] <- Acc.hi (Acc.ofFrac p.b >>> 2)
      let mutable sum = Acc.zero
      for i in 0 .. 7 do
        sum <- sum + s.x0[i] * s.y0[i] + s.x1[i] * s.y1[i] + s.x2[i] * s.y2[i] + s.x3[i] * s.y3[i]
      out.Write(Acc.sat (sum >>> 3))

  /// Five cursors live across a loop that indexes arrays: no register left for the loop.
  let crowded (_: Banks.P) (s: S) (out: Output) =
    let c1 = Cursor.linearIn s.x0 Frac.zero 1
    let c2 = Cursor.linearIn s.x1 Frac.zero 1
    let c3 = Cursor.linearIn s.x2 Frac.zero 1
    let c4 = Cursor.linearIn s.x3 Frac.zero 1
    let c5 = Cursor.linearIn s.y0 Frac.zero 1
    for _ in 0 .. 3 do
      for i in 0 .. 7 do
        s.y1[i] <- s.y2[i]
      out.Write(c1.Next())
      out.Write(c2.Next())
      out.Write(c3.Next())
      out.Write(c4.Next())
      out.Write(c5.Next())
    for _ in 0 .. 11 do
      out.Write(Frac.zero)

  let kernel = Kernel.define "probe-many-arrays" <@ init @> <@ trig @> <@ render @>
  let crowdedKernel = Kernel.define "probe-crowded-registers" <@ init @> <@ trig @> <@ crowded @>

/// State in both memories, interleaved in the record: an X delay line against Y coefficients
/// (an FIR whose loads can share one instruction, x:(rn)+ and y:(rm)+), Y and X words read
/// and written in one expression, a helper given both records.
[<ReflectedDefinition>]
module XAndY =
  type S =
    { mutable seed : Frac
      [<XMemory; Length 8>] mutable line : Frac array
      [<Length 8>] mutable taps : Frac array
      [<XMemory>] mutable lp : Frac
      mutable gain : Frac
      [<XMemory>] mutable sum : Acc }

  let init (_: Banks.P) (s: S) =
    s.seed <- Frac.ofRaw 0x2468ac
    for i in 0 .. 7 do
      s.line[i] <- Frac.zero
    s.taps[0] <- Frac.q23 0.0625
    s.taps[1] <- Frac.q23 0.125
    s.taps[2] <- Frac.q23 0.25
    s.taps[3] <- Frac.q23 0.375
    s.taps[4] <- Frac.q23 0.375
    s.taps[5] <- Frac.q23 0.25
    s.taps[6] <- Frac.q23 -0.125
    s.taps[7] <- Frac.q23 -0.0625
    s.lp <- Frac.zero
    s.gain <- Frac.q23 0.5
    s.sum <- Acc.zero

  let trig (p: Banks.P) (s: S) =
    s.gain <- p.d
    s.lp <- Frac.zero

  /// One-pole smoothing of an X word by a Y word.
  let smooth (s: S) (x: Frac) =
    s.lp <- Acc.hi (Acc.rnd (Acc.ofFrac s.lp + Acc.sat (Acc.ofFrac x - s.lp) * s.gain))
    s.lp

  let render (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 31 do
      for i in 0 .. 6 do
        s.line[i] <- s.line[i + 1]
      s.seed <- Frac.lfsr 0xc20001 s.seed
      s.line[7] <- Acc.hi (Acc.ofFrac s.seed >>> 1)
      let mutable fir = Acc.zero
      for i in 0 .. 7 do
        fir <- fir + s.line[i] * s.taps[i]
      s.sum <- s.sum + Acc.sat fir * p.a - (s.sum >>> 5)
      out.Write(Acc.sat (Acc.ofFrac (smooth s (Acc.sat s.sum)) + s.line[2] * p.b))
    s.gain <- Acc.hi (Acc.ofFrac s.gain + Acc.hi (Acc.ofFrac p.c >>> 8))

  let kernel = Kernel.define "probe-x-and-y-state" <@ init @> <@ trig @> <@ render @>

  /// The same record but for its X fields: the Y layout X fields leave unchanged.
  type YOnly =
    { mutable seed : Frac
      [<Length 8>] mutable taps : Frac array
      mutable gain : Frac }

  let none (_: Banks.P) (_: YOnly) = ()
  let silent (_: Banks.P) (_: YOnly) (out: Output) =
    for _ in 0 .. 31 do
      out.Write Frac.zero
  let yOnly = Kernel.define "probe-y-only" <@ none @> <@ none @> <@ silent @>

/// X state that fills the X workset, as TX8-HH's: the compiler's locals go to Y after the state.
[<ReflectedDefinition>]
module XFull =
  type S =
    { [<XMemory; Length 64>] mutable buffer : Frac array
      mutable seed  : Frac
      mutable total : Acc }

  let init (_: Banks.P) (s: S) =
    for i in 0 .. 63 do
      s.buffer[i] <- Frac.zero
    s.seed <- Frac.ofRaw 0x55aa55
    s.total <- Acc.zero

  let trig (_: Banks.P) (_: S) = ()

  let render (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 31 do
      for i in 0 .. 62 do
        s.buffer[i] <- s.buffer[i + 1]
      s.seed <- Frac.lfsr 0xc20001 s.seed
      s.buffer[63] <- Acc.hi (Acc.ofFrac s.seed >>> 1)
      let a = s.buffer[0] * p.a
      let b = s.buffer[31] * p.b
      s.total <- s.total + Acc.sat a * Acc.sat b - (s.total >>> 3)
      out.Write(Acc.sat (s.total + s.buffer[63] * p.c))

  let kernel = Kernel.define "probe-x-full" <@ init @> <@ trig @> <@ render @>

  /// Both worksets full: no word left for a local.
  type Over =
    { [<XMemory; Length 64>] mutable buffer : Frac array
      [<Length 55>] mutable rest : Frac array }
  let none (_: Banks.P) (_: Over) = ()
  let mix (p: Banks.P) (s: Over) (out: Output) =
    for _ in 0 .. 31 do
      let a = s.buffer[0] * p.a
      let b = s.rest[0] * p.b
      out.Write(Acc.sat (Acc.sat a * Acc.sat b))
  let over = Kernel.define "probe-x-over" <@ none @> <@ none @> <@ mix @>

  type Beyond = { [<XMemory; Length 65>] mutable buffer : Frac array }
  let nothing (_: Banks.P) (_: Beyond) = ()
  let silent (_: Banks.P) (_: Beyond) (out: Output) =
    for _ in 0 .. 31 do
      out.Write Frac.zero
  let beyond = Kernel.define "probe-x-beyond" <@ nothing @> <@ nothing @> <@ silent @>

/// Arrays indexed by a word computed at run time, as TX8-HH's and TX8-CY's residual writes: a
/// masked index (CY's `and #7`), a negative one from the end of a line (HH's (r+n) with n < 0),
/// a wrapping position, and the scratch regions.
[<ReflectedDefinition>]
module Computed =
  type S =
    { mutable seed : Frac
      [<XMemory; Length 32>] mutable hist : Frac array
      [<Length 16>] mutable ring : Frac array
      [<XMemory>] mutable pos : Frac }

  let init (_: Banks.P) (s: S) =
    s.seed <- Frac.ofRaw 0x3c5a96
    for i in 0 .. 31 do
      s.hist[i] <- Frac.zero
    for i in 0 .. 15 do
      s.ring[i] <- Frac.zero
    s.pos <- Frac.zero

  let trig (_: Banks.P) (s: S) = s.pos <- Frac.zero

  let render (p: Banks.P) (s: S) (out: Output) =
    let fill = Cursor.linear Scratch.x Frac.zero
    let mutable n = s.seed
    for _ in 0 .. 15 do
      n <- Frac.lfsr 0xc20001 n
      fill.Put n
    for _ in 0 .. 31 do
      s.seed <- Frac.lfsr 0xc20001 s.seed
      let k = Acc.hi (Acc.andHi 0xf (Acc.ofFrac s.seed))
      let d = Acc.hi (Acc.ofFrac k - Acc.ofFrac (Frac.ofRaw 16))
      let residual = Acc.hi (Acc.rnd (s.seed * p.a))
      s.hist[Frac.index d + 32] <- residual
      s.ring[Frac.index k] <- Acc.hi (Acc.rnd (Acc.ofFrac s.ring[Frac.index k] + residual * p.b))
      s.pos <- Acc.hi (Acc.andHi 0x1f (Acc.ofFrac s.pos + Frac.ofRaw 1))
      s.hist[Frac.index s.pos] <- Acc.hi (Acc.ofFrac s.hist[Frac.index s.pos] >>> 1)
      let j = Acc.hi (Acc.andHi 0xf (Acc.ofFrac residual))
      Region.write Scratch.x j (Region.read Scratch.x k)
      Region.write Scratch.x k residual
      out.Write(Acc.sat (Acc.ofFrac (Region.read Scratch.x j) + s.hist[Frac.index d + 16] * p.c + s.ring[Frac.index j] * p.d))

  let kernel = Kernel.define "probe-computed-index" <@ init @> <@ trig @> <@ render @>

  type Short = { [<XMemory; Length 4>] mutable line : Frac array }
  let none (_: Banks.P) (_: Short) = ()
  let past (p: Banks.P) (s: Short) (out: Output) =
    for _ in 0 .. 31 do
      s.line[Frac.index p.a] <- p.b
      out.Write Frac.zero
  let short = Kernel.define "probe-computed-past" <@ none @> <@ none @> <@ past @>

/// Cursors over state arrays, as TX8-CY's filter ring: six Y words at Y+$10 walked by r4 with
/// m4 = 5, `(r4)+`, `(r4)-` and `(r4)+n4` (n4 = 3), so two three-word sections share the ring and
/// it turns three words a sample. Also an X ring stepping down from a kept position and a linear
/// cursor over a Y array.
[<ReflectedDefinition>]
module StateRing =
  type S =
    { mutable seed : Frac
      [<Length 6>] mutable pre : Frac array        // Y+$a..$f, after seed at Y+9
      [<Length 6>] mutable ring : Frac array       // Y+$10, as CY's
      [<XMemory; Length 6>] mutable back : Frac array
      mutable pos : Frac }

  let init (_: Banks.P) (s: S) =
    s.seed <- Frac.ofRaw 0x1d2c3b
    for i in 0 .. 5 do
      s.pre[i] <- Frac.zero
    for i in 0 .. 5 do
      s.ring[i] <- Frac.zero
      s.back[i] <- Frac.zero
    s.pos <- Frac.zero

  let trig (_: Banks.P) (s: S) = s.pos <- Frac.ofRaw 3

  let render (p: Banks.P) (s: S) (out: Output) =
    let c = Cursor.ringIn s.ring Frac.zero 3
    let x = Cursor.ringIn s.back s.pos -1
    for _ in 0 .. 31 do
      s.seed <- Frac.lfsr 0xc20001 s.seed
      let input = Acc.hi (Acc.ofFrac s.seed >>> 2)
      let y0 = c.Up()
      let y1 = c.Up()
      let z = c.Peek()
      let a = Acc.sat (input * p.a + y0 * p.b - y1 * p.c)
      c.PutDown(Acc.sat (Acc.rnd (Acc.ofFrac z + a * p.d)))
      c.PutDown y0
      c.Put a
      let older = x.Peek()
      x.Put(Acc.hi (Acc.rnd (Acc.ofFrac a - older * p.b)))
      out.Write(Acc.sat (Acc.ofFrac older + z * p.c))
    // CY's correction pass: (r4)+ read, (r4)- read, (r4)+ write twice
    for _ in 0 .. 2 do
      let u = c.Up()
      let v = c.Down()
      c.PutUp(Acc.sat (Acc.ofFrac u - Acc.ofFrac v))
      c.PutUp(Acc.hi (Acc.ofFrac v >>> 1))
    let line = Cursor.linearIn s.pre (Frac.ofRaw 2) 1
    for _ in 0 .. 3 do
      line.Put(c.Next())
    s.pos <- x.Position()

  let kernel = Kernel.define "probe-state-ring" <@ init @> <@ trig @> <@ render @>

  /// A ring not on its power of two: Y+9.
  type Off = { [<Length 6>] mutable ring : Frac array }
  let none (_: Banks.P) (_: Off) = ()
  let walk (_: Banks.P) (s: Off) (out: Output) =
    let c = Cursor.ringIn s.ring Frac.zero 1
    for _ in 0 .. 31 do
      out.Write(c.Next())
  let off = Kernel.define "probe-state-ring-off" <@ none @> <@ none @> <@ walk @>


/// Several reads and effects in one expression, in .NET's order (left to right, arguments before
/// the call): two cursor reads, a helper that changes state beside a read of that state, mpysu
/// given cursor reads.
[<ReflectedDefinition>]
module Order =
  type S =
    { mutable seed : Frac
      mutable count : Frac }

  let init (_: Banks.P) (s: S) =
    s.seed <- Frac.ofRaw 0x5a3c21
    s.count <- Frac.zero

  let trig (_: Banks.P) (_: S) = ()

  /// Steps the seed, gives the new one.
  let step (s: S) =
    s.seed <- Frac.lfsr 0xc20001 s.seed
    s.seed

  let mpysu (x: Frac) (u: Frac) =
    let mutable a = x * u
    if u < Frac.zero then a <- a + (Acc.ofFrac x <<< 1)
    a

  let render (p: Banks.P) (s: S) (out: Output) =
    let fill = Cursor.linear Scratch.x Frac.zero
    for _ in 0 .. 31 do
      fill.Put(step s)
    let r = Cursor.linear Scratch.x Frac.zero
    for _ in 0 .. 7 do
      // left before right, both orders of operand kinds
      out.Write(Acc.sat (Acc.ofFrac (r.Next()) - r.Next() * p.a))
      out.Write(Acc.sat (r.Next() * p.b - Acc.ofFrac (r.Next())))
    for _ in 0 .. 3 do
      // the state read beside the call that changes it: before it on the left, after on the right
      out.Write(Acc.sat (Acc.ofFrac s.seed - Acc.ofFrac (step s)))
      out.Write(Acc.sat (Acc.ofFrac (step s) - s.seed * p.c))
    let q = Cursor.linear Scratch.x (Frac.ofRaw 4)
    for _ in 0 .. 7 do
      out.Write(Acc.sat (mpysu (q.Next()) (q.Next())))
    s.count <- Acc.hi (Acc.ofFrac s.count + Frac.ofRaw 1)

  let kernel = Kernel.define "probe-order" <@ init @> <@ trig @> <@ render @>

/// Quotation shapes: helpers taking
/// unit, if/else as a value, and while loops on data.
[<ReflectedDefinition>]
module Shapes =
  type S =
    { mutable seed : Frac
      mutable phase : Acc
      mutable events : Frac }

  let init (_: Banks.P) (s: S) =
    s.seed <- Frac.ofRaw 0x3a5c17
    s.phase <- Acc.zero
    s.events <- Frac.zero

  let trig (_: Banks.P) (_: S) = ()

  /// A read of the scratch memory, no argument.
  let third () = Region.read Scratch.x (Frac.ofRaw 3)

  /// A statement helper, no argument.
  let fill () =
    let c = Cursor.linear Scratch.x Frac.zero
    let mutable v = Frac.ofRaw 0x101010
    for _ in 0 .. 7 do
      c.Put v
      v <- Frac.lfsr 0xc20001 v

  let units (p: Banks.P) (s: S) (out: Output) =
    fill ()
    for _ in 0 .. 31 do
      s.seed <- Frac.lfsr 0xc20001 s.seed
      out.Write(Acc.sat (Acc.ofFrac (third ()) + s.seed * p.a))

  let unitsKernel = Kernel.define "probe-shape-units" <@ init @> <@ trig @> <@ units @>

  let positive (v: Frac) = if v >= Frac.zero then v else Frac.zero

  /// Both branches computed values.
  let pick (a: Frac) (b: Frac) (c: Frac) = if a > b then a * c else Acc.ofFrac b - b * c

  /// Steps the seed, gives the new one.
  let step (s: S) =
    s.seed <- Frac.lfsr 0xc20001 s.seed
    s.seed

  let selects (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 15 do
      s.seed <- Frac.lfsr 0xc20001 s.seed
      let x = positive s.seed
      out.Write(Acc.sat (pick x p.a p.b + (if s.seed < p.c then Acc.ofFrac p.d else Acc.zero)))
      // a branch with an effect, the other the state it changes
      out.Write(if p.a > Frac.zero then step s else s.seed)
    s.events <- (if p.b < p.c then Acc.hi (p.b * p.c) else p.c)

  let selectsKernel = Kernel.define "probe-shape-selects" <@ init @> <@ trig @> <@ selects @>

  /// SuperWave's event scheduler: events until the phase passes the block, then a countdown on
  /// a local, a value held across both, and fixed loops inside and around them.
  let events (p: Banks.P) (s: S) (out: Output) =
    let interval = Acc.ofFrac (Acc.hi (Acc.andHi 0x0f0000 (Acc.ofFrac p.a))) + Acc.ofFrac (Frac.ofRaw 0x010000)
    let held = s.seed * p.c
    s.phase <- s.phase - Acc.ofFrac (Frac.ofRaw 0x080000)
    let mutable n = Frac.zero
    while s.phase <= Acc.zero do
      s.phase <- s.phase + interval
      n <- Acc.hi (Acc.ofFrac n + Frac.ofRaw 1)
      for _ in 0 .. 1 do
        s.seed <- Frac.lfsr 0xc20001 s.seed
    s.events <- n
    for _ in 0 .. 3 do
      let mutable x = Acc.ofFrac s.seed
      while x > Acc.ofFrac p.b do
        x <- x - Acc.ofFrac (Frac.ofRaw 0x100000)
      for _ in 0 .. 7 do
        out.Write(Acc.sat (x + held))

  let eventsKernel = Kernel.define "probe-shape-events" <@ init @> <@ trig @> <@ events @>

/// Programs of a known size: a few words of code and a table of `n` words.
[<ReflectedDefinition>]
module Sizes =
  let init (_: Banks.P) () = ()

  let small = Table.program "size_small" [| for i in 0 .. 39 -> i * 0x010101 |]
  let near = Table.program "size_near" [| for i in 0 .. 3399 -> i |]
  let mono = Table.program "size_mono" [| for i in 0 .. 8099 -> i |]
  let over = Table.program "size_over" [| for i in 0 .. 14099 -> i |]

  let index (p: Banks.P) = Acc.hi (Acc.andHi 0x7 (Acc.ofFrac p.a))

  let renderSmall (p: Banks.P) () (out: Output) =
    for _ in 0 .. 31 do
      out.Write(Table.read small (index p))
  let renderNear (p: Banks.P) () (out: Output) =
    for _ in 0 .. 31 do
      out.Write(Table.read near (index p))
  let renderMono (p: Banks.P) () (out: Output) =
    for _ in 0 .. 31 do
      out.Write(Table.read mono (index p))
  let renderOver (p: Banks.P) () (out: Output) =
    for _ in 0 .. 31 do
      out.Write(Table.read over (index p))

  let smallKernel = Kernel.define "probe-size-small" <@ init @> <@ init @> <@ renderSmall @>
  let nearKernel = Kernel.define "probe-size-3400" <@ init @> <@ init @> <@ renderNear @>
  let monoKernel = Kernel.define "probe-size-8100" <@ init @> <@ init @> <@ renderMono @>
  let overKernel = Kernel.define "probe-size-14100" <@ init @> <@ init @> <@ renderOver @>

/// [<Subroutine>] helpers: TX8-CY's coefficient refresh, called from three places; a helper with
/// four Frac arguments, one with an Acc argument calling another subroutine, one without
/// arguments; the caller's locals held in registers and B across the calls.
[<ReflectedDefinition>]
module Subs =
  type S =
    { mutable seed : Frac
      mutable c0 : Frac
      mutable c1 : Frac
      [<Length 4>] mutable hist : Frac array }

  [<Subroutine>]
  let refresh (p: Banks.P) (s: S) (x: Frac) =
    s.c0 <- Acc.hi (x * p.a + Acc.ofFrac p.b)
    s.c1 <- Acc.sat (Acc.ofFrac s.c0 - x * p.c)
    for i in 0 .. 3 do
      s.hist[i] <- Acc.hi (s.hist[i] * p.d + Acc.ofFrac x)

  [<Subroutine>]
  let mix (x: Frac) (y: Frac) (z: Frac) (w: Frac) = Acc.sat (x * y + z * w)

  [<Subroutine>]
  let shape (a: Frac) (b: Acc) = (b >>> 1) + a * mix a a a Frac.one

  [<Subroutine>]
  let third () = Region.read Scratch.x (Frac.ofRaw 3)

  let init (p: Banks.P) (s: S) =
    s.seed <- Frac.ofRaw 0x2b5d13
    for i in 0 .. 3 do
      s.hist[i] <- Frac.zero
    for _ in 0 .. 1 do
      refresh p s Frac.zero

  let trig (_: Banks.P) (_: S) = ()

  let render (p: Banks.P) (s: S) (out: Output) =
    let fill = Cursor.linear Scratch.x Frac.zero
    for _ in 0 .. 7 do
      s.seed <- Frac.lfsr 0xc20001 s.seed
      fill.Put s.seed
    let held = s.seed * p.b
    let mutable k = s.c0
    for _ in 0 .. 3 do
      refresh p s k
      for _ in 0 .. 7 do
        s.seed <- Frac.lfsr 0xc20001 s.seed
        k <- mix s.seed k s.c1 p.a
        out.Write(Acc.sat (shape k held + Acc.ofFrac (third ())))
      k <- Acc.hi (Acc.ofFrac k + held)
    refresh p s s.hist[2]

  let kernel = Kernel.define "probe-subroutines" <@ init @> <@ trig @> <@ render @>

  /// Six DO loops deep, then a call: more stack than a machine may take.
  let deep (p: Banks.P) (s: S) =
    for _ in 0 .. 1 do
      for _ in 0 .. 1 do
        for _ in 0 .. 1 do
          for _ in 0 .. 1 do
            for _ in 0 .. 1 do
              for _ in 0 .. 1 do
                refresh p s s.seed

  let deepKernel = Kernel.define "probe-subroutines-deep" <@ init @> <@ deep @> <@ render @>

/// Subroutine frames over the caller's dead locals: RENDER's Acc locals t1..t4 are read only
/// before the call, so middle's frame (and leaf's above it) start below them; middle's own
/// locals u, v, m are dead by its last call to leaf.
[<ReflectedDefinition>]
module Frames =
  type S =
    { mutable w : Frac
      mutable z : Frac }

  [<Subroutine>]
  let leaf (s: S) (x: Frac) (y: Frac) =
    let a = x * y
    let b = Acc.sat (a + s.w * x)
    let c = Acc.ofFrac b - y * b
    let d = Acc.hi (c >>> 1)
    Acc.sat (Acc.ofFrac d + Acc.sat c * x)

  [<Subroutine>]
  let middle (s: S) (x: Frac) =
    let u = leaf s x s.w
    let v = leaf s u x
    let m = Acc.ofFrac v + u * x
    let w = leaf s v (Acc.sat m)
    Acc.sat (Acc.ofFrac w + v * x)

  let init (_: Banks.P) (s: S) =
    s.w <- Frac.ofRaw 0x123456
    s.z <- Frac.zero

  let trig (_: Banks.P) (_: S) = ()

  let render (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 31 do
      let t1 = p.a * p.b
      let t2 = p.c * p.d + t1
      let t3 = t2 - p.a * p.c
      let t4 = t3 + p.b * p.d - t1
      let early = Acc.sat (t4 + (t2 >>> 1))
      let r = middle s early
      out.Write(Acc.sat (Acc.ofFrac r + p.d * early))
      s.w <- Acc.hi (Acc.ofFrac r >>> 1)

  let kernel = Kernel.define "probe-subroutine-frames" <@ init @> <@ trig @> <@ render @>

/// Loops counted when running (TX8-CY's samples to the next oscillator edge): `for _ in 1 ..
/// Frac.index n`, zero passes for a count of zero or less, a block split at a computed edge.
[<ReflectedDefinition>]
module Counted =
  type S =
    { mutable phase : Frac
      mutable seed : Frac }

  let init (_: Banks.P) (s: S) =
    s.phase <- Frac.zero
    s.seed <- Frac.ofRaw 0x61a3c5

  let trig (_: Banks.P) (_: S) = ()

  let render (p: Banks.P) (s: S) (out: Output) =
    let n = Acc.hi (Acc.andHi 0x0f (Acc.ofFrac p.a))          // 0 .. 15
    for _ in 1 .. Frac.index n do
      s.seed <- Frac.lfsr 0xc20001 s.seed
    let m = Acc.hi (Acc.ofFrac p.b >>> 20)                     // -8 .. 7: none for 0 and less
    for _ in 0 .. 1 do
      for _ in 0 .. Frac.index m - 1 do
        s.phase <- Acc.hi (Acc.ofFrac s.phase + p.c)
    // the block in two runs, split at an edge
    let k = Acc.hi (Acc.andHi 0x1f (Acc.ofFrac s.seed))        // 0 .. 31
    for _ in 1 .. Frac.index k do
      s.phase <- Acc.hi (Acc.ofFrac s.phase + p.d)
      out.Write s.phase
    let rest = Acc.hi (Acc.ofFrac (Frac.ofRaw 32) - k)
    for _ in 1 .. Frac.index rest do
      s.seed <- Frac.lfsr 0xc20001 s.seed
      out.Write(Acc.sat (s.seed * p.a))

  let kernel = Kernel.define "probe-counted-loops" <@ init @> <@ trig @> <@ render @>

/// A loop whose body starts with a test of the parameters (TX8-HH's): unswitched by default;
/// kept whole by [<NoUnswitch>], by Loop.everyPass, or by a size budget.
[<ReflectedDefinition>]
module Unswitching =
  type S =
    { mutable seed : Frac
      mutable level : Frac }

  let init (_: Banks.P) (s: S) =
    s.seed <- Frac.ofRaw 0x3c5a97
    s.level <- Frac.zero

  let trig (_: Banks.P) (_: S) = ()

  let body (p: Banks.P) (s: S) (out: Output) =
    s.seed <- Frac.lfsr 0xc20001 s.seed
    out.Write(Acc.sat (s.seed * p.c + s.level * p.d))

  let render (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 31 do
      if p.a > p.b then s.level <- Acc.hi (Acc.ofFrac s.level + s.seed * p.a)
      else s.level <- Acc.hi (s.level * p.b)
      body p s out

  [<NoUnswitch>]
  let renderKept (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 31 do
      if p.a > p.b then s.level <- Acc.hi (Acc.ofFrac s.level + s.seed * p.a)
      else s.level <- Acc.hi (s.level * p.b)
      body p s out

  let renderEach (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 31 do
      if Loop.everyPass (p.a > p.b) then s.level <- Acc.hi (Acc.ofFrac s.level + s.seed * p.a)
      else s.level <- Acc.hi (s.level * p.b)
      body p s out

  let kernel = Kernel.define "probe-unswitch" <@ init @> <@ trig @> <@ render @>
  let keptKernel = Kernel.define "probe-unswitch-kept" <@ init @> <@ trig @> <@ renderKept @>
  let eachKernel = Kernel.define "probe-unswitch-each" <@ init @> <@ trig @> <@ renderEach @>

[<ReflectedDefinition>]
module Unsized =
  type S = { mutable history : Frac array }
  let init (_: Banks.P) (_: S) = ()
  let render (_: Banks.P) (_: S) (_: Output) = ()
  let kernel = Kernel.define "probe-unsized" <@ init @> <@ init @> <@ render @>
  let twice (_: Banks.P) () (out: Output) =
    for _ in 0 .. 63 do
      out.Write Frac.zero
  let none (_: Banks.P) () = ()
  let wide = Kernel.define "probe-wide" <@ none @> <@ none @> <@ twice @>

/// A stereo kernel: a sawtooth phase, each frame the phase times the
/// level on the left and its negation on the right.
[<ReflectedDefinition>]
module Stereo =
  type P =
    { level : Frac
      step  : Frac }

  type S = { mutable phase : Frac }

  let init (_: P) (s: S) = s.phase <- Frac.zero
  let trig (_: P) (s: S) = s.phase <- Frac.zero

  let render (p: P) (s: S) (out: Output) =
    for _ in 0 .. 15 do
      let ph = Acc.hi (Acc.ofFrac s.phase + p.step)
      s.phase <- ph
      out.WritePair(Acc.sat (ph * p.level), Acc.sat (-(ph * p.level)))

  let kernel = Kernel.define "probe-stereo" <@ init @> <@ trig @> <@ render @>


let probeMachine =
  { Mds.machine "org.fsdsp56k.probe" "PROBE" "GND" "PR" with
      parameters = [| for n in [| "A"; "B"; "C"; "D" |] -> Mds.param n Mds.UnipolarQ23 |] }

/// Words that exercise the limits half the time.
let randomWords (rng: Random) =
  let edges = [| 0; 1; 0x7fffff; 0x800000; 0xffffff; 0x400000; 0xc00000 |]
  Array.init 4 (fun _ -> if rng.Next 2 = 0 then edges[rng.Next edges.Length] else rng.Next 0x1000000)

let helperMachine =
  { Mds.machine "org.fsdsp56k.probe-helper" "PROBE" "GND" "PH" with
      parameters = [| for n in [| "B0"; "B1"; "B2"; "A1"; "A2" |] -> Mds.param n Mds.UnipolarQ23 |] }

/// Where the tests keep what they grade (untracked, emptied when the tests start: Main.fs).
let keptRoot = repoRoot </> "out" </> "kernels" </> "tests"

/// A graded kernel kept for listening in out/kernels/tests/<path>.
let keepAt (path: string list) (r: Report) =
  try Audio.save (System.IO.Path.Combine(Array.ofList (keptRoot :: path))) r.kernel r |> ignore
  with _ -> ()        // a render that cannot be written does not fail the grade
  r

/// In out/kernels/tests/<group>/<kernel>.
let keepIn (group: string list) (r: Report) = keepAt (group @ [ r.kernel ]) r

let keep (r: Report) = keepIn [ "probes" ] r

/// A text kept in out/kernels/tests/<path>.
let keepText (path: string list) (text: string) =
  try
    let file = System.IO.Path.Combine(Array.ofList (keptRoot :: path))
    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName file) |> ignore
    System.IO.File.WriteAllText(file, text + "\n")
  with _ -> ()

let conformanceWith (machine: Mds.Machine) (k: Kernel.Kernel) seed =
  let rng = Random(seed: int)
  let words () = Array.init machine.parameters.Length (fun _ -> if rng.Next 3 = 0 then [| 0; 0x7fffff; 0x800000; 0x400000 |][rng.Next 4] else rng.Next 0x1000000)
  let steps =
    [ yield Words(words ())
      yield Init
      for _ in 1 .. 100 do
        yield Words(words ())
        yield Render 1 ]
  match grade k machine None { name = "random words"; steps = steps } |> Result.map keep with
  | Error e -> failtest e
  | Ok r -> Expect.isEmpty r.correctness (text r)

let conformance (k: Kernel.Kernel) seed =
  let rng = Random(seed: int)
  let steps =
    [ yield Words(randomWords rng)
      yield Init
      for _ in 1 .. 200 do
        yield Words(randomWords rng)
        yield Render 1 ]
  match grade k probeMachine None { name = "random words"; steps = steps } |> Result.map keep with
  | Error e -> failtest e
  | Ok r -> Expect.isEmpty r.correctness (text r)


let tests =
  testList "kernels" [

    test "arithmetic intrinsics: compiled program equals the .NET kernel" {
      needNative ()
      conformance Probes.arithmeticKernel 1
    }

    test "control, tables, constants and helpers: compiled program equals the .NET kernel" {
      needNative ()
      conformance Probes.controlKernel 2
    }

    test "memory: cursors, rings, tables, rounding and the LFSR: compiled program equals the .NET kernel" {
      needNative ()
      conformance Probes.memoryKernel 3
    }

    test "a reflected helper taking the parameter and state records compiles and equals the .NET kernel" {
      needNative ()
      conformanceWith helperMachine Helpers.kernel 5
    }

    test "Acc and [<Length n>] array state: compiled program equals the .NET kernel" {
      needNative ()
      conformance Banks.kernel 6
    }

    test "a RENDER writing other than one word per frame is refused" {
      match grade Unsized.wide probeMachine None { name = "one block"; steps = [ Init; Render 1 ] } with
      | Ok _ -> failtest "graded"
      | Error e -> Expect.stringContains e "64 words" "the message gives the count"
    }

    test "arrays indexed by the loop (shift, FIR with Frac.ofRaw i): compiled program equals the .NET kernel" {
      needNative ()
      conformance Fir.kernel 7
    }

    test "an array state field needs its length" {
      match dsp56k.kernel.Compile.compile Kernel.mds Unsized.kernel with
      | Ok _ -> failtest "compiled"
      | Error e -> Expect.stringContains e "[<Length n>]" "the message says how"
    }

    testList "[<XMemory>] state" [
      test "X fields are laid out from X+0, Y fields keep the offsets they have without them" {
        let l = Kernel.layout Kernel.mds XAndY.kernel
        Expect.equal (l.xState |> Array.map (fun f -> f.name, f.offset)) [| "line", 0; "lp", 8; "sum", 9 |] "X+0 on, in order"
        Expect.equal l.xWorkset 12 "eight words, one, and an Acc's three"
        Expect.isTrue (l.xState |> Array.forall (fun f -> f.bank = XMemory)) "in X"
        let y = Kernel.layout Kernel.mds XAndY.yOnly
        Expect.equal (l.state |> Array.map (fun f -> f.name, f.offset)) (y.state |> Array.map (fun f -> f.name, f.offset)) "the Y offsets"
        Expect.equal l.yWorkset y.yWorkset "the Y workset"
      }

      test "the compiled program reads and writes X state at x:(r6+k), locals after it" {
        match Optimize.compile Optimize.Peephole Kernel.mds XAndY.kernel with
        | Error e -> failtest e
        | Ok c ->
          let lines = c.items |> Array.map dsp56k.Print.item
          Expect.exists lines (fun l -> l.Contains "x:(r6+$8)") "lp at X+8"
          Expect.isGreaterThanOrEqual c.xWorkset 12 "the X workset covers the state"
          let spills =
            lines |> Array.choose (fun l ->
              let m = Text.RegularExpressions.Regex.Match(l, @"x:\(r6\+\$([0-9a-f]+)\)")
              if m.Success then Some(Convert.ToInt32(m.Groups[1].Value, 16)) else None)
          Expect.isTrue (spills |> Array.forall (fun k -> k < c.xWorkset)) "inside the declared X workset"
          Expect.exists lines (fun l -> l.Contains "x:(r" && l.Contains "y:(r") "an X and a Y load in one instruction"
      }

      test "X-only state: compiled program equals the .NET kernel, X words included" {
        needNative ()
        conformance XOnly.kernel 21
      }

      test "X and Y state: compiled program equals the .NET kernel, both memories compared" {
        needNative ()
        let rng = Random 22
        let steps =
          [ yield Words(randomWords rng)
            yield Init
            for b in 1 .. 120 do
              yield Words(randomWords rng)
              if b % 17 = 0 then yield Trig
              yield Render 1 ]
        match grade XAndY.kernel probeMachine None { name = "random words, retriggers"; steps = steps } |> Result.map keep with
        | Error e -> failtest e
        | Ok r ->
          Expect.isEmpty r.correctness (text r)
          let l = r.compiled.layout
          Expect.equal r.dsp.states[0].Length (l.yWorkset - Kernel.mds.stateBase + l.xWorkset) "Y and X state words"
          Expect.exists (r.dsp.states |> Array.map (fun s -> s[s.Length - l.xWorkset .. ]) |> Array.concat) ((<>) 0) "the X state moves"
      }

      test "the host gives the Y state words, then the X state words" {
        let host = Host.Instance(Kernel.mds, XAndY.kernel)
        host.SetParameters [| 0x400000; 0x200000; 0x100000; 0x300000 |]
        host.Init()
        host.Render() |> ignore
        Expect.equal (host.StateWords()) (Array.append (host.YStateWords()) (host.XStateWords())) "Y, then X"
        Expect.equal (host.XStateWords()).Length 12 "the X state's words"
      }

      test "X state filling the X workset: locals go to Y after the state, and the program equals the .NET kernel" {
        needNative ()
        match Optimize.compile Optimize.Peephole Kernel.mds XFull.kernel with
        | Error e -> failtest e
        | Ok c ->
          Expect.equal c.xWorkset 64 "all of X"
          Expect.isGreaterThan c.yWorkset c.layout.yWorkset "Y words past the state"
          let lines = c.items |> Array.map dsp56k.Print.item
          Expect.exists lines (fun l -> l.Contains $"y:(r6+${c.layout.yWorkset:x})") "a local at the first Y word after the state"
        conformance XFull.kernel 23
      }

      test "the refusals name what ran out" {
        match dsp56k.kernel.Compile.compile Kernel.mds XFull.over with
        | Ok _ -> failtest "compiled"
        | Error e -> Expect.stringContains e "the [<XMemory>] state (64 words), locals and spills need more than 64 X words and the Y words after the state" "both worksets"
        match dsp56k.kernel.Compile.compile Kernel.mds XFull.beyond with
        | Ok _ -> failtest "compiled"
        | Error e -> Expect.stringContains e "X+64" "the message names the word"
      } ]

    testList "computed indices" [
      test "an (r0+n0) access in X and in Y: r0 the workset or the region, n0 the index" {
        match Optimize.compile Optimize.Peephole Kernel.mds Computed.kernel with
        | Error e -> failtest e
        | Ok c ->
          let lines = c.items |> Array.map dsp56k.Print.item
          Expect.exists lines (fun l -> l.Contains "x:(r0+n0)") "X"
          Expect.exists lines (fun l -> l.Contains "y:(r0+n0)") "Y"
          Expect.exists lines (fun l -> l.Contains "r6,r0") "r0 at the workset"
          Expect.exists lines (fun l -> l.Contains ",n0") "n0 the index"
      }

      test "masked, negative-from-the-end and wrapping indices, region scatter and gather: compiled program equals the .NET kernel" {
        needNative ()
        conformance Computed.kernel 24
      }

      test "with retriggers, both memories compared" {
        needNative ()
        let rng = Random 25
        let steps =
          [ yield Words(randomWords rng)
            yield Init
            for b in 1 .. 120 do
              yield Words(randomWords rng)
              if b % 13 = 0 then yield Trig
              yield Render 1 ]
        match grade Computed.kernel probeMachine None { name = "random words, retriggers"; steps = steps } |> Result.map keep with
        | Error e -> failtest e
        | Ok r -> Expect.isEmpty r.correctness (text r)
      }

      test "an index past the array fails on .NET (on the DSP it is another word)" {
        let host = Host.Instance(Kernel.mds, Computed.short)
        host.SetParameters [| 0x000004; 0x200000; 0; 0 |]
        host.Init()
        Expect.throws (fun () -> host.Render() |> ignore) "outside the array"
      } ]

    testList "cursors over state" [
      test "TX8-CY's ring: lua (r6+$10), M = 5, n = 3, (rn)+ and (rn)-" {
        match Optimize.compile Optimize.Peephole Kernel.mds StateRing.kernel with
        | Error e -> failtest e
        | Ok c ->
          let lines = c.items |> Array.map dsp56k.Print.item
          Expect.exists lines (fun l -> l.Contains "lua" && l.Contains "(r6+$10)") "the ring's first word"
          Expect.exists lines (fun l -> l.Contains "movec" && l.Contains "$5,m") "M = 5"
          Expect.exists lines (fun l -> l.Contains "movec" && l.Contains "$ffffff,m") "linear again after"
          Expect.exists lines (fun l -> l.Contains "y:(r" && l.Contains ")-") "a step down"
          Expect.exists lines (fun l -> l.Contains "y:(r" && l.Contains ")+n") "a step by n"
      }

      test "the ring, an X ring stepping down from a kept position and a linear cursor: compiled program equals the .NET kernel" {
        needNative ()
        conformance StateRing.kernel 26
      }

      test "with retriggers, both memories compared" {
        needNative ()
        let rng = Random 27
        let steps =
          [ yield Words(randomWords rng)
            yield Init
            for b in 1 .. 120 do
              yield Words(randomWords rng)
              if b % 11 = 0 then yield Trig
              yield Render 1 ]
        match grade StateRing.kernel probeMachine None { name = "random words, retriggers"; steps = steps } |> Result.map keep with
        | Error e -> failtest e
        | Ok r -> Expect.isEmpty r.correctness (text r)
      }

      test "a ring off its power of two is refused" {
        match dsp56k.kernel.Compile.compile Kernel.mds StateRing.off with
        | Ok _ -> failtest "compiled"
        | Error e -> Expect.stringContains e "ring starts on a multiple of 8" "says where it may start"
      } ]

    testList "quotation shapes" [
      for name, k, seed in [ "helpers taking unit, as a value and as a statement", Shapes.unitsKernel, 41
                             "if/else as a value: Frac and Acc, in arithmetic, a branch with an effect", Shapes.selectsKernel, 42
                             "while on state and on a local: zero or more passes, fixed loops inside and around", Shapes.eventsKernel, 43 ] ->
        test name {
          needNative ()
          conformance k seed
        } ]

    testList "program size" [
      let compiled level target k =
        match Optimize.compile level target k with
        | Ok c -> c
        | Error e -> failtest e
      test "code and table words" {
        let c = compiled Optimize.Peephole Kernel.mds Sizes.smallKernel
        match Compile.size c with
        | Error e -> failtest e
        | Ok s ->
          Expect.equal s.tables 40 "the table's words"
          Expect.isGreaterThan s.code 0 "some code"
          match Grade.build probeMachine c with
          | Error e -> failtest e
          | Ok b -> Expect.equal b.package.programWords.Length s.total "the package holds code and tables"
      }
      test "the MDS track harness refuses a program reaching its services" {
        let low = { machinedrum.run.Track.defaultLayout with programBase = 0x100 }    // services at P:$0e00
        Expect.isNone (machinedrum.run.Track.overflow low 0xd00) "3328 words fit"
        match machinedrum.run.Track.overflow low 0xd01 with
        | None -> failtest "3329 fit"
        | Some why ->
          Expect.stringContains why "room for 3328 words" "names the room"
          Expect.stringContains why "services at P:$0e00" "and what bounds it"
        Expect.isNone (machinedrum.run.Track.overflow machinedrum.run.Track.defaultLayout Mds.programMaxWords) "the default layout holds the largest package"
      }
      test "the MDS harness: the X mirror of the largest package overlaps no shared area" {
        let l = Grade.layout
        let mirror = l.programBase, l.programBase + Mds.programMaxWords
        let areas =
          [ "workset", l.workset, 64; "scratch X", l.tempX, 32; "input ring", l.inputRing, 256; "input position", l.inputPosition, 1
            "main ring", l.mainRing, 256; "main position", l.mainPosition, 1; "service words", machinedrum.run.Services.info, 0x7ff - machinedrum.run.Services.info
            "sine", l.sineTable, Rom.sine.words.Length ]
        for name, at, n in areas do
          Expect.isTrue (at + n <= fst mirror || at >= snd mirror) $"{name} X:{at:x4}..{at + n - 1:x4} is clear of X:{fst mirror:x4}..{snd mirror - 1:x4}"
      }
      test "a program of 3400 words on the MDS harness equals the .NET kernel, its tables read past X:$0400" {
        needNative ()
        conformance Sizes.nearKernel 62
      }
      test "a kernel past the MDS package limit does not compile" {
        for result in [ Compile.compile Kernel.mds Sizes.overKernel; Optimize.compile Optimize.Peephole Kernel.mds Sizes.overKernel ] do
          match result with
          | Ok _ -> failtest "compiled"
          | Error e ->
            Expect.stringContains e "at most 14052 program words" "names the limit"
            Expect.stringContains e "tables 14100" "and the sizes"
        match Mds.pack probeMachine (Array.zeroCreate 14_053) (Map [ "INIT", 0L; "TRIG", 0L; "RENDER", 0L ]) None with
        | Ok _ -> failtest "packed"
        | Error e -> Expect.stringContains $"%A{e}" "at most 14052" "the package format refuses it too"
      } ]

    testList "[<Subroutine>] helpers" [
      test "compiled program equals the .NET kernel" {
        needNative ()
        conformance Subs.kernel 51
      }
      test "one copy of each, called with bsr" {
        for level in [ Optimize.Direct; Optimize.Peephole ] do
          match Optimize.compile level Kernel.mds Subs.kernel with
          | Error e -> failtest e
          | Ok c ->
            let text = c.items |> Array.map dsp56k.Print.item
            let count (s: string) = text |> Array.filter (fun l -> l.Contains s) |> Array.length
            Expect.equal (count "SUB_REFRESH_1:") 1 "refresh once"
            Expect.equal (text |> Array.filter (fun l -> l.Contains "bsr" && l.Contains "SUB_REFRESH_") |> Array.length) 3 "called from INIT's loop, RENDER's loop and RENDER's end"
            Expect.isGreaterThan (text |> Array.filter (fun l -> l.Contains "bsr" && l.Contains "SUB_MIX_") |> Array.length) 1 "mix from render and from shape"
      }
      test "the system stack: a call six DO loops deep is refused" {
        match Optimize.compile Optimize.Peephole Kernel.mds Subs.deepKernel with
        | Ok _ -> failtest "compiled"
        | Error e -> Expect.stringContains e "takes 15 system stack levels" "six loops, the call, refresh's own loop"
      }
      test "frames over the caller's dead locals: compiled program equals the .NET kernel" {
        needNative ()
        conformance Frames.kernel 52
      }
      test "frames over the caller's dead locals: a chain of calls takes the words its live locals need" {
        match Optimize.compile Optimize.Peephole Kernel.mds Frames.kernel with
        | Error e -> failtest e
        | Ok c ->
          // stacked on the caller's every local (as before), the chain took 32 X words
          Expect.isLessThanOrEqual c.xWorkset 17 "t1..t4 and middle's dead locals given to the frames"
      } ]

    test "loops counted when running: compiled program equals the .NET kernel" {
      needNative ()
      conformance Counted.kernel 61
    }

    testList "loop unswitching" [
      let loops (c: Compile.Compiled) = c.items |> Array.filter (fun it -> match it with dsp56k.Item.Op(dsp56k.Loop _) -> true | _ -> false) |> Array.length
      let compiled k =
        match Optimize.compile Optimize.Peephole Kernel.mds k with
        | Ok c -> c
        | Error e -> failtest e
      let words c = match Compile.size c with Ok s -> s.total | Error e -> failtest e
      yield! [ for name, k, seed in [ "unswitched", Unswitching.kernel, 71; "[<NoUnswitch>]", Unswitching.keptKernel, 72
                                      "Loop.everyPass", Unswitching.eachKernel, 73 ] ->
                 test $"{name}: compiled program equals the .NET kernel" {
                   needNative ()
                   conformance k seed
                 } ]
      test "by default the loop is split in two; [<NoUnswitch>] and Loop.everyPass keep it whole, smaller" {
        let split, kept, each = compiled Unswitching.kernel, compiled Unswitching.keptKernel, compiled Unswitching.eachKernel
        Expect.equal (loops split) 2 "a copy for each outcome"
        Expect.equal (loops kept) 1 "[<NoUnswitch>]: one loop"
        Expect.equal (loops each) 1 "Loop.everyPass: one loop"
        Expect.isLessThan (words kept) (words split) "smaller"
        Expect.equal (words each) (words kept) "the same code"
      }
      test "a size budget keeps loops whole when the split program does not fit" {
        let split = compiled Unswitching.kernel
        match Optimize.compileWithin (Some(words split - 1)) Optimize.Peephole Kernel.mds Unswitching.kernel with
        | Error e -> failtest e
        | Ok c ->
          Expect.equal (loops c) 1 "kept whole"
          let code (c: Compile.Compiled) = c.items |> Array.filter (fun it -> match it with dsp56k.Item.Comment _ -> false | _ -> true)
          Expect.equal (code c) (code (compiled Unswitching.keptKernel)) "as [<NoUnswitch>] compiles it"
        match Optimize.compileWithin (Some(words split)) Optimize.Peephole Kernel.mds Unswitching.kernel with
        | Error e -> failtest e
        | Ok c -> Expect.equal c.items split.items "split when it fits"
      } ]

    test "pipelines (|>, <|): compiled program equals the .NET kernel" {
      needNative ()
      conformance Probes.pipedKernel 9
    }

    test "DIV: compiled program equals the .NET kernel" {
      needNative ()
      conformance Probes.divideKernel 4
    }

    test "DIV with the carry given, no ASL: compiled program equals the .NET kernel" {
      needNative ()
      conformance Probes.divideCarryKernel 5
    }

    test "MPYSU, MACSU and DMACSS: compiled program equals the .NET kernel" {
      needNative ()
      conformance Probes.mulSuKernel 6
    }

    test "CLB and NORMF: compiled program equals the .NET kernel" {
      needNative ()
      conformance Probes.normaliseKernel 6
    }

    test "CLB and NORMF, .NET: leading bits as 9 minus their count, shifts by a signed count" {
      let acc (v: int64) = Acc.ofRaw v
      let clb v = (Acc.hi (Acc.clb (acc v))).Raw
      Expect.equal (clb 0L) 0 "zero gives 0"
      Expect.equal (clb (0x400000L <<< 24)) 0 "0.5 is normalised"
      Expect.equal (clb (0x200000L <<< 24)) -1 "0.25: one left shift"
      Expect.equal (clb (-0x800000L <<< 24)) 0 "-1 is normalised"
      Expect.equal (clb (-0x400000L <<< 24)) -1 "-0.5: one left shift"
      Expect.equal (clb (0x800000L <<< 24)) 1 "+1 in the extension: one right shift"
      Expect.equal (clb 0x7fffffffffffffL) 8 "the largest: eight right shifts"
      Expect.equal (clb 1L) -46 "2^-47"
      Expect.equal (clb -1L) -47 "all ones"
      let normf (k: int) v = (Acc.normf (Frac.ofRaw k) (acc v)).Raw
      Expect.equal (normf -1 (0x200000L <<< 24)) (0x400000L <<< 24) "a negative count shifts left"
      Expect.equal (normf 1 (0x400000L <<< 24)) (0x200000L <<< 24) "a positive count shifts right"
      Expect.equal (normf 2 (-0x400000L <<< 24)) (-0x100000L <<< 24) "right shifts keep the sign"
      Expect.equal (normf 64 12345L) 12345L "the count is taken modulo 64"
      Expect.equal (normf -65 12345L) 24690L "on either side"
      Expect.equal (normf 0x800000 12345L) 12345L "-2^23: no shift"
      Expect.equal (normf -8 (0x0100000000000L)) (Bits.sext56 (0x0100000000000L <<< 8)) "bits shifted past bit 55 are lost"
      for v in [ 1L; -1L; 3L <<< 30; -(5L <<< 40); 0x123456789abcL; 0x7fffffffffffffL; -0x80000000000000L ] do
        let n = (Acc.normf (Acc.hi (Acc.clb (acc v))) (acc v)).Raw
        Expect.equal (Acc.hi (Acc.clb (acc n))).Raw 0 $"normf (clb a) a is normalised for {v:x}"
    }

    test "CLB and NORMF, edges (zero, all ones, extension, counts past 23): compiled program equals the .NET kernel" {
      needNative ()
      conformance Probes.normaliseEdgesKernel 7
    }

    test "more arrays than address registers in one loop: compiled program equals the .NET kernel" {
      match Optimize.compile Optimize.Peephole Kernel.mds ManyArrays.kernel with
      | Error e -> failtest e
      | Ok c ->
        let lines = c.items |> Array.map dsp56k.Print.item
        Expect.exists lines (fun l -> Text.RegularExpressions.Regex.IsMatch(l, @"[xy]:\(r[1-5]\+\$[0-9a-f]+\)")) "an array at its distance from a stepped register"
      needNative ()
      let rng = Random 23
      let steps =
        [ yield Words(randomWords rng)
          yield Init
          for _ in 1 .. 60 do
            yield Words(randomWords rng)
            yield Render 1 ]
      match grade ManyArrays.kernel probeMachine None { name = "random words"; steps = steps } |> Result.map keep with
      | Error e -> failtest e
      | Ok r -> Expect.isEmpty r.correctness (text r)
    }

    test "no address register left for a loop's arrays: the message names the holders and the way out" {
      match dsp56k.kernel.Compile.compile Kernel.mds ManyArrays.crowdedKernel with
      | Ok _ -> failtest "compiled"
      | Error e ->
        Expect.stringContains e "c1 (r" "names a cursor holding a register"
        Expect.stringContains e "Frac.index (Frac.ofRaw i)" "gives the workaround"
    }

    testList "samples reproduce the machines they were ported from, word for word" [
      for sample in samples.kernels.Grading.samples ->
        test sample.title {
          needNative ()
          needFixture "MDS"
          needFixture "kitbasher"
          match samples.kernels.Grading.run (fixture "") sample with
          | Error e -> failtest e
          | Ok { report = r } ->
            keepAt ("samples" :: sample.group @ [ sample.folder ]) r |> ignore
            Expect.isEmpty r.correctness (text r)
            Expect.equal r.fidelity (Some None) (text r)
            // the optimizer's ground so far: regressions past it fail
            Expect.isLessThan r.ours.renderMean (3.0 * r.reference.Value.renderMean) "within 3x the hand-written RENDER"
        } ]

    testList "every pass pays for itself on the samples: turning one off is never faster, or as fast and smaller" [
      for sample in samples.kernels.Grading.samples ->
        test sample.title {
          needNative ()
          needFixture "MDS"
          needFixture "kitbasher"
          let rows =
            Grade.passTable (fun level -> samples.kernels.Grading.runAt level (fixture "") sample |> Result.map _.report)
            |> List.map (function Ok r -> r | Error e -> failtest e)
          let table =
            rows
            |> List.map (fun r -> sprintf "  %-12s %10.1f %8d %6d %s" r.off r.renderMean r.renderWorst r.words (if r.correct then "" else "WRONG"))
            |> String.concat "\n"
          keepText ("samples" :: sample.group @ [ sample.folder; "passes.txt" ]) (sprintf "  %-12s %10s %8s %6s\n%s" "off" "mean" "worst" "words" table)
          for r in rows do Expect.isTrue r.correct $"{r.off} off: the compiled program differs from the .NET kernel\n{table}"
          let all = rows.Head
          for r in rows.Tail do
            let faster = r.renderMean < all.renderMean - 1e-9 || r.renderWorst < all.renderWorst
            let smallerAsFast = r.renderMean = all.renderMean && r.renderWorst = all.renderWorst && r.words < all.words
            Expect.isFalse (faster || smallerAsFast) $"with {r.off} off the program is better than with every pass\n{table}"
        } ]

    test "the optimizer is deterministic: compiling a sample twice gives the same listing" {
      for sample in samples.kernels.Grading.samples do
        let listing () =
          match Optimize.compile Optimize.Peephole Kernel.mds sample.kernel with
          | Ok c -> c.items |> Array.map dsp56k.Print.item |> String.concat "\n"
          | Error e -> failtest e
        let first = listing ()
        Expect.equal (listing ()) first $"{sample.title}: a second compilation differs"
    }

    test "a stereo RENDER is refused on a mono target" {
      match dsp56k.kernel.Compile.compile Kernel.mds Stereo.kernel with
      | Ok _ -> failtest "compiled"
      | Error e -> Expect.stringContains e "stereo target" "the message names the target"
    }

    test "a loop index read as Frac.ofRaw i: compiled program equals the .NET kernel" {
      needNative ()
      conformance Probes.indexKernel 8
    }

    test "other uses of a loop index are refused" {
      match dsp56k.kernel.Compile.compile Kernel.mds Probes.scaledIndexKernel with
      | Ok _ -> failtest "compiled"
      | Error e -> Expect.stringContains e "loop index" "the message names the problem"
    }
  ]

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

  /// LSR (a1 only, a2 and a0 kept) and shifts by a count computed when running (`asr s,a,a`,
  /// `asl s,a,a`, `lsr s,a`), the count from a word's top bits as TXZ-EP takes it from its exp
  /// table. The logical counts stay under 32: past that the emulator's LSR by a register differs
  /// from the chip (it takes the count modulo 32 on a1).
  let shifts (p: P) (s: S) (out: Output) =
    let mutable x = Acc.ofLong s.acc + p.a * p.b
    for _ in 0 .. 1 do
      out.Write(Frac.lsr 9 p.c)
      out.Write(Frac.lsr 23 p.c)
      let l = Acc.lsr 5 x
      out.Write(Acc.hi l)
      out.Write(Acc.lo l)
      out.Write(Acc.sat l)
      out.Write(Acc.sat (Acc.lsr 1 x))
      let k = Frac.lsr 18 p.b
      let k31 = Acc.hi (Acc.andHi 0xffffdf (Acc.ofFrac p.a))
      let r = Acc.asrBy k x
      out.Write(Acc.sat r)
      out.Write(Acc.lo r)
      let up = Acc.aslBy k x
      out.Write(Acc.sat up)
      let lr = Acc.lsrBy k31 x
      out.Write(Acc.hi lr)
      out.Write(Acc.lo lr)
      out.Write(Acc.sat lr)
      out.Write(Frac.lsrBy k31 p.d)
      out.Write(Acc.sat (Acc.asrBy (Frac.ofRaw 0x47) x))
      out.Write(Acc.hi (Acc.lsrBy (Frac.ofRaw 26) x))
      out.Write(Acc.lo (Acc.aslBy (Frac.ofRaw 13) x))
      x <- Acc.asrBy k x + p.c * p.d
    s.acc <- Long.ofAcc x

  /// Bit tests as conditions (`btst #n,d`, a branch on the carry): the half sine's sign bit as
  /// TXZ-EP reads it, a parameter's low bit, `not` a bit.
  let bits (p: P) (s: S) (out: Output) =
    let mutable phase = Acc.ofLong s.acc
    for _ in 0 .. 7 do
      let ph = Acc.hi phase
      let v = Acc.ofFrac (Frac.lsr 9 ph)
      out.Write(if Frac.bit 23 ph then Acc.sat (-v) else Acc.sat v)
      if Frac.bit 0 p.a then out.Write(p.b) else out.Write(p.c)
      if not (Frac.bit 14 ph) then out.Write(Acc.hi (Acc.ofFrac p.d >>> 1)) else out.Write(ph)
      out.Write(if Frac.bit 5 p.b then p.c else p.d)
      phase <- phase + Acc.ofFrac p.c
    s.acc <- Long.ofAcc phase

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
  let shiftsKernel = Kernel.define "probe-shifts" <@ init @> <@ trig @> <@ shifts @>
  let bitsKernel = Kernel.define "probe-bit-test" <@ init @> <@ trig @> <@ bits @>
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

/// Four Acc locals live across a loop body (P-I-HP's `pair`: two coupled-form resonators a
/// pass, a, b and c, d, with MACs and the limiter between them).
[<ReflectedDefinition>]
module FourAcc =
  type S =
    { mutable wa : Long
      mutable wb : Long
      mutable wc : Long
      mutable wd : Long
      [<Length 32>] mutable pulse : Frac array
      mutable w1 : Frac
      mutable r1 : Frac
      mutable w2 : Frac
      mutable r2 : Frac }

  let init (_: Banks.P) (s: S) =
    s.wa <- Long.ofRaw 0x123456789aL
    s.wb <- Long.ofRaw -0x0fedcba987L
    s.wc <- Long.ofRaw 0x2468ace013L
    s.wd <- Long.ofRaw 0x13579bdf02L
    s.w1 <- Frac.ofRaw 0x0c0000
    s.r1 <- Frac.ofRaw 0x001000
    s.w2 <- Frac.ofRaw 0x1a0000
    s.r2 <- Frac.ofRaw 0x002000

  let trig (_: Banks.P) (_: S) = ()

  let pair (s: S) (g1: Frac) (g2: Frac) (k1: Frac) (k2: Frac) (out: Output) =
    let mutable a = Acc.ofLong s.wa
    let mutable b = Acc.ofLong s.wb
    let mutable c = Acc.ofLong s.wc
    let mutable d = Acc.ofLong s.wd
    for i in 0 .. 31 do
      let e = s.pulse[i]
      a <- Acc.mac a e g1
      a <- a - Acc.sat b * s.w1
      let x = Acc.sat a
      a <- a - x * s.r1
      b <- Acc.mac b x s.w1
      c <- Acc.mac c e g2
      c <- c - Acc.sat d * s.w2
      let y = Acc.sat c
      c <- c - y * s.r2
      d <- Acc.mac d y s.w2
      out.Write(Acc.sat (Acc.rnd (Acc.mac (Acc.mac (Acc.ofFrac e) k1 x) k2 y)))
    s.wa <- Long.ofAcc a
    s.wb <- Long.ofAcc b
    s.wc <- Long.ofAcc c
    s.wd <- Long.ofAcc d

  let render (p: Banks.P) (s: S) (out: Output) =
    for i in 0 .. 31 do
      s.pulse[i] <- Acc.hi (Acc.ofFrac p.d >>> 4)
    pair s p.a p.b p.c p.d out

  let kernel = Kernel.define "probe-four-acc" <@ init @> <@ trig @> <@ render @>

/// Long.ofAccSat (`move a,l:`): a Long state kept to 48 bits, limited, as it grows past them.
[<ReflectedDefinition>]
module LongSat =
  type S = { mutable wa : Long; mutable wb : Long }

  let init (_: Banks.P) (s: S) = s.wa <- Long.ofRaw 0x123456789abcL; s.wb <- Long.ofRaw -0x0fedcba98765L

  let trig (_: Banks.P) (_: S) = ()

  let render (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 15 do
      let a = Acc.ofLong s.wa
      s.wa <- Long.ofAccSat (a + a + p.a * p.b)
      let b = Acc.mac (Acc.ofLong s.wb) p.c p.d
      s.wb <- Long.ofAccSat (b + b + Acc.ofFrac p.c)
      out.Write(Acc.lo (Acc.ofLong s.wa))
      out.Write(Acc.hi (Acc.ofLong s.wb))

  let kernel = Kernel.define "probe-long-sat" <@ init @> <@ trig @> <@ render @>

/// `let` of a condition: a word, its uses tests of it (in an if, under not, in && and ||, in a
/// value's selection), a constant one folded.
[<ReflectedDefinition>]
module BoolLet =
  type S = { mutable w : Frac }

  let wide = true

  let init (_: Banks.P) (s: S) = s.w <- Frac.ofRaw 0x123456

  let trig (_: Banks.P) (_: S) = ()

  let render (p: Banks.P) (s: S) (out: Output) =
    let loud = p.a > p.b
    let both = loud && p.c < Frac.zero
    let either = wide || p.d > Frac.zero
    for _ in 0 .. 15 do
      let up = s.w > p.d
      if loud then s.w <- Acc.hi (Acc.ofFrac s.w >>> 1) else s.w <- Acc.sat (Acc.ofFrac s.w + p.c)
      if not both then out.Write s.w else out.Write p.a
      let x = if up || both then p.b else p.c
      if either then out.Write x

  let kernel = Kernel.define "probe-bool-let" <@ init @> <@ trig @> <@ render @>

/// A loop index passed to helpers that index state arrays with it.
[<ReflectedDefinition>]
module IndexArg =
  type S = { [<Length 16>] h : Frac array; [<Length 16>] l : Frac array }

  let init (_: Banks.P) (_: S) = ()

  let trig (_: Banks.P) (_: S) = ()

  let get (s: S) (k: int) = Acc.sat (Acc.ofFrac s.h[k] + Acc.ofFrac s.l[k])
  let put (s: S) (k: int) (x: Frac) = s.h[k] <- x
  let next (s: S) (k: int) = s.l[k + 1]

  let render (p: Banks.P) (s: S) (out: Output) =
    for i in 0 .. 14 do
      out.Write(get s i)
      put s i (Acc.sat (p.a * next s i))
    out.Write(get s 15)
    for i in 0 .. 15 do
      s.l[i] <- Acc.sat (Acc.ofFrac s.l[i] + p.b)
      out.Write(next s (i - 1))

  let kernel = Kernel.define "probe-index-arg" <@ init @> <@ trig @> <@ render @>

/// Ring taps: reads of `base + ((p - k) & mask)` in a row, k a run-time word plus 0, 1, 2 (P-I-ST's
/// ladder), and `((p - c) & mask) + base` (NFX-EN's cubic). The base is aligned to the ring for
/// some parameter words and not for others: the modulo path and the fallback both run.
[<ReflectedDefinition>]
module RingTaps =
  type S =
    { mutable origin : Frac
      mutable pos : Frac }

  let tap (s: S) (k: Frac) =
    Region.read Scratch.x (Acc.hi (Acc.ofFrac s.origin + Acc.hi (Acc.andHi 7 (Acc.ofFrac s.pos - k))))

  let word (s: S) (from: Frac) (back: int) =
    Region.read Scratch.y (Acc.hi (Acc.andHi 15 (Acc.ofFrac from - Frac.ofRaw back) + s.origin))

  let init (_: Banks.P) (s: S) =
    s.origin <- Frac.zero
    s.pos <- Frac.zero

  let trig (_: Banks.P) (_: S) = ()

  let render (p: Banks.P) (s: S) (out: Output) =
    s.origin <- Acc.hi (Acc.andHi 0xc (Acc.ofFrac p.a))
    let mutable fill = p.c
    for i in 0 .. 31 do
      Region.write Scratch.x (Frac.ofRaw i) fill
      Region.write Scratch.y (Frac.ofRaw i) (Acc.hi (Acc.ofFrac p.d - fill))
      fill <- Acc.hi (Acc.ofFrac fill + Frac.ofRaw 0x10101)
    let w = Acc.hi (Acc.andHi 3 (Acc.ofFrac p.b))
    for _ in 0 .. 7 do
      let d0 = tap s w
      let d1 = tap s (Acc.hi (Acc.ofFrac w + Frac.ofRaw 1))
      let d2 = tap s (Acc.hi (Acc.ofFrac w + Frac.ofRaw 2))
      out.Write d0
      out.Write(Acc.sat (Acc.ofFrac d1 + d2))
      let from = Acc.hi (Acc.ofFrac s.pos - w)
      let p0 = word s from 0
      let p1 = word s from 1
      let p2 = word s from 2
      let p3 = word s from 3
      out.Write(Acc.sat (Acc.ofFrac p0 - p3))
      out.Write(Acc.sat (Acc.ofFrac p1 + p2))
      s.pos <- Acc.hi (Acc.andHi 15 (Acc.ofFrac s.pos + Frac.ofRaw 3))

  let kernel = Kernel.define "probe-ring-taps" <@ init @> <@ trig @> <@ render @>

/// Region indexes a DO loop steps: a local stepped by a constant (`w <- w + 2`, read as w and
/// w + 1, its value used after the loop) and indexes affine in the loop index through lets and a
/// helper (`n = h + 2k`, `n + 1`): address registers stepped instead of r0/n0 rebuilt each access.
[<ReflectedDefinition>]
module RegionInduction =
  type S =
    { mutable at : Frac
      mutable last : Frac }

  let inc (v: Frac) (d: Frac) = Acc.hi (Acc.ofFrac v + d)

  let init (_: Banks.P) (s: S) =
    s.at <- Frac.zero
    s.last <- Frac.zero

  let trig (_: Banks.P) (_: S) = ()

  let render (p: Banks.P) (s: S) (out: Output) =
    let mutable w = Acc.hi (Acc.andHi 0x3 (Acc.ofFrac p.a))
    for k in 0 .. 9 do
      Region.write Scratch.x w (Acc.hi (Acc.ofFrac p.c + Frac.ofRaw k))
      Region.write Scratch.y (Acc.hi (Acc.ofFrac w + Frac.ofRaw 1)) p.d
      w <- Acc.hi (Acc.ofFrac w + Frac.ofRaw 2)
    let h = Acc.hi (Acc.andHi 0x7 (Acc.ofFrac p.b))
    for k in 0 .. 3 do
      let n = inc h (Acc.hi (Acc.ofFrac (Frac.ofRaw k) <<< 1))
      Region.write Scratch.x (inc n (Frac.ofRaw 1)) (Acc.hi (Acc.ofFrac (Region.read Scratch.y n) + p.a))
    let mutable r = Acc.hi (Acc.andHi 0x1 (Acc.ofFrac p.b))
    for _ in 0 .. 15 do
      out.Write(Region.read Scratch.x r)
      out.Write(Region.read Scratch.y (Acc.hi (Acc.ofFrac r + Frac.ofRaw 1)))
      r <- Acc.hi (Acc.ofFrac r + Frac.ofRaw 1)
    s.at <- w
    s.last <- r

  let kernel = Kernel.define "probe-region-induction" <@ init @> <@ trig @> <@ render @>

/// A region's address taken twice in one function, a branch between, then zeros stored: the
/// second `move #>region,x0` is patched by the loader, so x0 does not hold the 0 the assembler
/// wrote (MDX P-I-PN's INIT: its zeros took the region's address).
[<ReflectedDefinition>]
module AddressTwice =
  type S =
    { mutable base1 : Frac
      mutable base2 : Frac
      mutable z1 : Frac
      mutable z2 : Frac }

  let init (p: Banks.P) (s: S) =
    let start = Acc.hi (Acc.ofFrac (Region.addressOf Track.memory) + Acc.hi (Acc.andHi 0x3ff (Acc.ofFrac p.a)))
    let aligned = Acc.hi (Acc.andHi 0xfffc00 (Acc.ofFrac start + Frac.ofRaw 0x3ff))
    let mutable second = Acc.ofFrac p.b
    if start = aligned then second <- second + Frac.ofRaw 0x400
    s.base1 <- Acc.hi (Acc.ofFrac aligned - Region.addressOf Track.memory)
    s.base2 <- Acc.hi second
    s.z1 <- Frac.zero
    s.z2 <- Frac.zero

  let trig (_: Banks.P) (_: S) = ()

  let render (_: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 7 do
      out.Write s.base1
      out.Write s.base2
      out.Write s.z1
      out.Write s.z2

  let kernel = Kernel.define "probe-address-twice" <@ init @> <@ trig @> <@ render @>

/// [<Length n>] Long arrays (element k hi:lo at words 2k, 2k+1), in Y and in X: constant
/// indexes, the loop's index (its register stepping two words), two arrays in one loop, and a
/// helper given the index.
[<ReflectedDefinition>]
module LongArray =
  type S =
    { [<Length 8>] m : Long array
      mutable w : Long
      [<XMemory; Length 4>] xm : Long array }

  let init (_: Banks.P) (s: S) =
    s.m[0] <- Long.ofRaw 0x7fff00ffff00L
    s.m[5] <- Long.ofRaw -0x123456789aL
    s.xm[3] <- Long.ofRaw 0x0000017fffffL
    s.w <- Long.ofRaw 0x400000000001L

  let trig (_: Banks.P) (_: S) = ()

  let mode (s: S) (k: int) = Acc.ofLong s.m[k]

  let render (p: Banks.P) (s: S) (out: Output) =
    for i in 0 .. 7 do
      let a = mode s i
      s.m[i] <- Long.ofAccSat (a + a + p.a * p.b)
      out.Write(Acc.hi a)
      out.Write(Acc.lo a)
    for i in 0 .. 3 do
      s.xm[i] <- Long.ofAccSat (Acc.ofLong s.xm[i] + Acc.ofLong s.m[i + 4] + Acc.ofLong s.w)
      out.Write(Acc.lo (Acc.ofLong s.xm[i]))
    for i in 1 .. 6 do
      out.Write(Acc.hi (Acc.ofLong s.m[i - 1] + Acc.ofLong s.m[i + 1]))
      out.Write(Acc.lo (Acc.ofLong s.m[i]))
    s.w <- Long.ofAccSat (Acc.ofLong s.m[7] + Acc.ofLong s.xm[0])

  let kernel = Kernel.define "probe-long-array" <@ init @> <@ trig @> <@ render @>

/// Locals in X1 and Y1 read in both branches of an if: the then branch, past its last read of
/// them, gives their registers to its own locals; the else branch still reads them (P-I-HP:
/// `if s.left = 0 then ... modes p4 ring; range s else force s; modes p4 ring`).
[<ReflectedDefinition>]
module Branches =
  type S = { mutable w : Frac }

  let init (_: Banks.P) (s: S) = s.w <- Frac.ofRaw 0x123456

  let trig (_: Banks.P) (_: S) = ()

  let render (p: Banks.P) (s: S) (out: Output) =
    let k = Acc.hi (Acc.ofFrac p.a >>> 1)
    let j = Acc.hi (Acc.ofFrac p.b >>> 2)
    if p.c > Frac.zero then
      for _ in 0 .. 15 do out.Write(Acc.sat (k * j + s.w * k))
      let u = Acc.sat (p.d * s.w)
      let v = Acc.hi (Acc.ofFrac u >>> 3)
      for _ in 0 .. 15 do out.Write(Acc.sat (u * v + v * p.d))
      s.w <- Acc.hi (Acc.ofFrac u + v)
    else
      let q = Acc.sat (p.d * p.d)
      let r = Acc.hi (Acc.ofFrac q >>> 2)
      for _ in 0 .. 15 do out.Write(Acc.sat (q * r + r * p.a))
      for _ in 0 .. 15 do out.Write(Acc.sat (k * j + q * k))
      s.w <- Acc.hi (Acc.ofFrac r + j)

  let kernel = Kernel.define "probe-branch-registers" <@ init @> <@ trig @> <@ render @>

/// [<Unroll>]: four calls deep, a DO loop at each level, then an 8-tap loop: 14 system stack
/// levels with the taps a DO loop, 12 with them written out.
[<ReflectedDefinition>]
module Unrolled =
  type S =
    { mutable acc : Acc
      [<Length 8>] mutable taps : Frac array
      [<Length 8>] mutable line : Frac array }

  let tapsBody (s: S) (x: Frac) =
    let mutable sum = Acc.ofFrac x >>> 2
    for i in 0 .. 7 do
      sum <- sum + s.taps[i] * s.line[i]
      s.line[i] <- Acc.hi (Acc.ofFrac s.line[i] + x * s.taps[i])
    Acc.sat sum

  [<Subroutine>]
  let taps (s: S) (x: Frac) = tapsBody s x

  /// The same taps written out ([<Unroll>] reaches the loops of the function it marks, not
  /// those of the helpers it inlines).
  [<Subroutine; Unroll>]
  let tapsOut (s: S) (x: Frac) =
    let mutable sum = Acc.ofFrac x >>> 2
    for i in 0 .. 7 do
      sum <- sum + s.taps[i] * s.line[i]
      s.line[i] <- Acc.hi (Acc.ofFrac s.line[i] + x * s.taps[i])
    Acc.sat sum

  [<Subroutine>]
  let level3 (s: S) (x: Frac) =
    let mutable y = x
    for _ in 0 .. 1 do
      y <- taps s y
    y

  [<Subroutine>]
  let level2 (s: S) (x: Frac) =
    let mutable y = x
    for _ in 0 .. 1 do
      y <- level3 s y
    y

  [<Subroutine>]
  let level1 (s: S) (x: Frac) =
    let mutable y = x
    for _ in 0 .. 1 do
      y <- level2 s y
    y

  [<Subroutine>]
  let level3Out (s: S) (x: Frac) =
    let mutable y = x
    for _ in 0 .. 1 do
      y <- tapsOut s y
    y

  [<Subroutine>]
  let level2Out (s: S) (x: Frac) =
    let mutable y = x
    for _ in 0 .. 1 do
      y <- level3Out s y
    y

  [<Subroutine>]
  let level1Out (s: S) (x: Frac) =
    let mutable y = x
    for _ in 0 .. 1 do
      y <- level2Out s y
    y

  [<Unroll>]
  let init (_: Banks.P) (s: S) =
    s.acc <- Acc.zero
    for i in 0 .. 7 do
      s.taps[i] <- Frac.ofRaw (0x100000 - i * 0x020000)
      s.line[i] <- Frac.zero

  let trig (_: Banks.P) (_: S) = ()

  let render (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 31 do
      let y = level1Out s p.a
      out.Write y
      s.acc <- Acc.ofFrac y + p.b * p.c

  let renderDeep (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 31 do
      out.Write(level1 s p.a)

  /// Unrolled in RENDER itself, nested loops included, the indices constants in each copy.
  [<Unroll>]
  let renderFlat (p: Banks.P) (s: S) (out: Output) =
    for k in 0 .. 3 do
      for i in 0 .. 7 do
        out.Write(Acc.sat (s.taps[i] * p.a + Acc.ofFrac (Frac.ofRaw (k * 8 + i))))
      s.line[k + 1] <- Acc.hi (Acc.ofFrac s.line[k] + p.d)

  let kernel = Kernel.define "probe-unroll" <@ init @> <@ trig @> <@ render @>
  let deepKernel = Kernel.define "probe-unroll-deep" <@ init @> <@ trig @> <@ renderDeep @>
  let flatKernel = Kernel.define "probe-unroll-flat" <@ init @> <@ trig @> <@ renderFlat @>

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


/// Table reads at constant indexes, a program table (`movem p:>TABLE+k`, relocated), a row of it
/// and the sine resource (`move x:>k`, patched), against the same reads through indexes held in
/// state, whose addresses are computed when the program runs.
[<ReflectedDefinition>]
module ConstTable =
  let gains = Table.program "const_gains" [| 0x100000; 0x200000; 0x300000; 0x400000; 0x500000; 0x600000; 0x700000; 0x7fffff |]

  type S =
    { mutable i3 : Frac
      mutable i5 : Frac
      mutable i1 : Frac
      mutable ix : Frac
      mutable v  : Frac }

  let init (_: Banks.P) (s: S) =
    s.i3 <- Frac.ofRaw 3
    s.i5 <- Frac.ofRaw 5
    s.i1 <- Frac.ofRaw 1
    s.ix <- Frac.ofRaw 0x2000
    s.v  <- Frac.zero

  let trig (_: Banks.P) (_: S) = ()

  let constant (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 7 do
      s.v <- Acc.sat (Acc.ofFrac s.v + Table.read gains (Frac.ofRaw 3) * p.a)
      out.Write s.v
      out.Write(Table.row gains 2 (Frac.ofRaw 1) 1)
      out.Write(Acc.sat (Table.read Rom.sine (Frac.ofRaw 0x2000) * p.b))
      out.Write(Table.read gains (Frac.ofRaw 5))

  let held (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 7 do
      s.v <- Acc.sat (Acc.ofFrac s.v + Table.read gains s.i3 * p.a)
      out.Write s.v
      out.Write(Table.row gains 2 s.i1 1)
      out.Write(Acc.sat (Table.read Rom.sine s.ix * p.b))
      out.Write(Table.read gains s.i5)

  let constantKernel = Kernel.define "probe-table-constant" <@ init @> <@ trig @> <@ constant @>
  let heldKernel     = Kernel.define "probe-table-held" <@ init @> <@ trig @> <@ held @>


/// Subroutines the compiler chooses (Optimize.Subroutines): `settle`, called twice from INIT and
/// twice from TRIG, becomes one; the same body marked [<Inline>] stays inlined; called in RENDER's
/// loop it stays inlined; called from RENDER, outside its loop, next to a table that leaves no room
/// for four copies, it becomes one only because the program would not fit otherwise.
[<ReflectedDefinition>]
module AutoSubs =
  type S =
    { mutable a : Frac
      mutable b : Frac
      mutable c : Frac
      mutable v : Frac }

  let settle (p: Banks.P) (s: S) (x: Frac) =
    s.a <- Acc.sat (x * p.a + s.b * p.b)
    s.b <- Acc.sat (s.a * p.c - x * p.d)
    s.c <- Acc.hi (Acc.ofFrac s.a + s.b)
    s.v <- Acc.sat (s.c * p.a + s.a * p.d)

  [<Inline>]
  let settleInline (p: Banks.P) (s: S) (x: Frac) =
    s.a <- Acc.sat (x * p.a + s.b * p.b)
    s.b <- Acc.sat (s.a * p.c - x * p.d)
    s.c <- Acc.hi (Acc.ofFrac s.a + s.b)
    s.v <- Acc.sat (s.c * p.a + s.a * p.d)

  let init (p: Banks.P) (s: S) =
    s.b <- Frac.zero
    settle p s p.b
    settle p s p.c

  let trig (p: Banks.P) (s: S) =
    settle p s p.d
    settle p s s.v

  let initInline (p: Banks.P) (s: S) =
    s.b <- Frac.zero
    settleInline p s p.b
    settleInline p s p.c

  let trigInline (p: Banks.P) (s: S) =
    settleInline p s p.d
    settleInline p s s.v

  let render (_: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 31 do out.Write s.v

  let renderHot (p: Banks.P) (s: S) (out: Output) =
    for _ in 0 .. 31 do
      settle p s s.v
      out.Write s.v

  let big = Table.program "auto_big" [| for i in 0 .. 13959 -> i |]

  let renderBig (p: Banks.P) (s: S) (out: Output) =
    settle p s p.a
    settle p s p.b
    settle p s p.c
    settle p s p.d
    for _ in 0 .. 31 do out.Write(Table.read big (Acc.hi (Acc.andHi 0x7 (Acc.ofFrac s.v))))

  let initBig (_: Banks.P) (s: S) = s.b <- Frac.zero

  let subKernel    = Kernel.define "probe-auto-sub" <@ init @> <@ trig @> <@ render @>
  let inlineKernel = Kernel.define "probe-auto-inline" <@ initInline @> <@ trigInline @> <@ render @>
  let hotKernel    = Kernel.define "probe-auto-hot" <@ init @> <@ trig @> <@ renderHot @>
  let sizeKernel   = Kernel.define "probe-auto-size" <@ initBig @> <@ initBig @> <@ renderBig @>


/// Two regions indexed by the same loop-bound let (`let n = h + 2k`): `drop` substitutes one
/// index expression into both accesses, and each keeps its own region's stepped register
/// (`x:(r1)` and `y:(r4)`).
[<ReflectedDefinition>]
module RegionSharedIndex =
  let render (p: Banks.P) (_: RegionInduction.S) (out: Output) =
    for k in 0 .. 15 do
      let n = RegionInduction.inc Frac.zero (Acc.hi (Acc.ofFrac (Frac.ofRaw k) <<< 1))
      Region.write Scratch.x n (Acc.hi (Acc.ofFrac p.a + Frac.ofRaw k))
      Region.write Scratch.y n (Acc.hi (Acc.ofFrac p.b - Frac.ofRaw k))
    let h = Acc.hi (Acc.andHi 0x1 (Acc.ofFrac p.c))
    for k in 0 .. 15 do
      let n = RegionInduction.inc h (Acc.hi (Acc.ofFrac (Frac.ofRaw k) <<< 1))
      out.Write(Acc.sat (Region.read Scratch.x n * p.c + Region.read Scratch.y n * p.d))
      out.Write(Acc.sat (Acc.ofFrac (Region.read Scratch.x n) - Region.read Scratch.y n))

  let kernel = Kernel.define "probe-region-shared-index" <@ RegionInduction.init @> <@ RegionInduction.trig @> <@ render @>


/// Two X regions read and written in a DO loop through one let-bound index (`let t = Frac.ofRaw i`,
/// then `s1.write t (s1.read t)` and `s2.write t (s2.read t)`): each access keeps its own region's
/// stepped register, the scratch words on one and the input ring on the other, where keying the
/// accesses by their shared index expression gave all four the last region's.
[<ReflectedDefinition>]
module RegionSharedLet =
  let s1 = Scratch.x
  let s2 = Input.ring

  let init (_: Banks.P) (_: RegionInduction.S) = ()

  let trig (_: Banks.P) (_: RegionInduction.S) =
    for i in 0 .. 31 do
      let t = Frac.ofRaw i
      Region.write s1 t (Region.read s1 t)
      Region.write s2 t (Region.read s2 t)

  let render (_: Banks.P) (_: RegionInduction.S) (out: Output) =
    for i in 0 .. 31 do
      let t = Frac.ofRaw i
      out.Write(Acc.sat (Acc.ofFrac (Region.read s2 t) - Region.read s1 t))

  let kernel = Kernel.define "probe-region-shared-let" <@ init @> <@ trig @> <@ render @>


/// State words in Y memory read and written on every pass of RENDER's loop: `walk` points a free
/// address register at the first state word before the loop (`lua (r6+$9),r4`) and steps it with
/// `(r4)+` / `(r4)-` back to its start each pass, turning 2-cycle `y:(r6+k)` moves into 1-cycle
/// parallel-eligible moves.
[<ReflectedDefinition>]
module WorksetWalk =
  type S =
    { mutable phase : Long
      mutable level : Long }

  let init (_: Banks.P) (s: S) =
    s.phase <- Long.zero
    s.level <- Long.zero

  let trig (_: Banks.P) (s: S) =
    s.phase <- Long.zero

  let render (p: Banks.P) (s: S) (out: Output) =
    let step = Acc.ofFrac p.a >>> 4
    let ramp = Acc.ofFrac p.b >>> 5
    for _ in 0 .. 31 do
      let ph = Acc.andHi 0x7fffff (Acc.ofLong s.phase + step)
      let lv = Acc.andHi 0x7fffff (Acc.ofLong s.level + ramp)
      s.phase <- Long.ofAcc ph
      s.level <- Long.ofAcc lv
      out.Write(Acc.sat (Acc.hi ph * Acc.hi lv))

  let kernel = Kernel.define "probe-workset-walk" <@ init @> <@ trig @> <@ render @>


/// Live-range allocation across a 32-sample loop: a five-word biquad loop where `z1`, `z2` and the
/// feedback sample live in data registers and `b1`/`b0` (with `a` the only accumulator), a `btst`
/// phase-wrap branch across which register facts survive, and a leaf-on-left `Acc` sum that avoids
/// a 3-word accumulator spill.
[<ReflectedDefinition>]
module LiveRanges =
  type S =
    { mutable z1  : Frac
      mutable z2  : Frac
      mutable b0  : Frac
      mutable b1  : Frac
      mutable b2  : Frac
      mutable a1  : Frac
      mutable a2  : Frac
      mutable ph  : Frac }

  let init (_: Banks.P) (s: S) =
    s.z1 <- Frac.zero
    s.z2 <- Frac.zero
    s.b0 <- Frac.ofRaw 0x100000
    s.b1 <- Frac.ofRaw 0x080000
    s.b2 <- Frac.ofRaw 0x040000
    s.a1 <- Frac.ofRaw 0x180000
    s.a2 <- Frac.ofRaw -0x060000
    s.ph <- Frac.zero

  let trig (_: Banks.P) (s: S) =
    s.z1 <- Frac.zero
    s.z2 <- Frac.zero

  let render (p: Banks.P) (s: S) (out: Output) =
    let c = Cursor.linear Scratch.x Frac.zero
    for _ in 0 .. 31 do
      let next = Acc.hi (Acc.ofFrac s.ph + p.a)
      s.ph <- if Frac.bit 23 next then Acc.hi (Acc.andHi 0x7fffff (Acc.ofFrac next)) else next
      let x = Acc.sat (Acc.ofFrac s.ph + (p.b * p.c - s.ph * p.d))
      let mutable acc = x * s.b0
      acc <- Acc.mac  acc s.z1 s.b1
      acc <- Acc.mac  acc s.z2 s.b2
      acc <- Acc.mac  acc s.z1 s.a1
      acc <- Acc.mac  acc s.z2 s.a2
      let y = Acc.sat (acc <<< 1)
      s.z2 <- s.z1
      s.z1 <- y
      c.Put y
    let r  = Cursor.linear Scratch.x Frac.zero
    let b0 = s.b0
    let b1 = s.b1
    let mutable z1 = s.z1
    let mutable z2 = s.z2
    for _ in 0 .. 31 do
      let fed = Acc.sat (Acc.ofFrac (r.Next()) + z1 * s.a1 + z2 * s.a2)
      let mutable acc = fed * b0
      acc <- Acc.mac acc z1 b1
      acc <- Acc.mac acc z2 s.b2
      let y = Acc.sat (acc <<< 1)
      z2 <- z1
      z1 <- y
      out.Write y
    s.z1 <- z1
    s.z2 <- z2

  let kernel = Kernel.define "probe-live-ranges" <@ init @> <@ trig @> <@ render @>


/// Cross-stage cursor loop fusion and dual X:Y cursor loads: a 32-sample producer loop writing
/// `Scratch.x` via `c1.Put` fuses with a 32-sample consumer loop updating `Scratch.x` in place via
/// `c2.Peek()` / `c2.Put`, eliminating the intermediate buffer read and second `do #32`; a second
/// loop mixing `Scratch.x` and `Scratch.y` packs both cursor reads into one `move x:(r1)+,x0 y:(r4)+,y0`.
[<ReflectedDefinition>]
module LoopFusion =
  type S =
    { mutable seed : Frac
      mutable z1   : Frac }

  let init (_: Banks.P) (s: S) =
    s.seed <- Frac.ofRaw 0x2468ac
    s.z1   <- Frac.zero

  let trig (_: Banks.P) (s: S) =
    s.z1 <- Frac.zero

  let render (p: Banks.P) (s: S) (out: Output) =
    let yFill = Cursor.linear Scratch.y Frac.zero
    let mutable seed = s.seed
    for _ in 0 .. 31 do
      seed <- Frac.lfsr 0xc20001 seed
      yFill.Put(Acc.hi (seed * p.a))
    s.seed <- seed
    let c1 = Cursor.linear Scratch.x Frac.zero
    let mutable ph = Frac.zero
    for _ in 0 .. 31 do
      ph <- Acc.hi (Acc.ofFrac ph + p.b)
      c1.Put ph
    let c2 = Cursor.linear Scratch.x Frac.zero
    let mutable z1 = s.z1
    for _ in 0 .. 31 do
      let x = Acc.sat (c2.Peek() * p.c + z1 * p.d)
      c2.Put x
      z1 <- x
    s.z1 <- z1
    let rx = Cursor.linear Scratch.x Frac.zero
    let ry = Cursor.linear Scratch.y Frac.zero
    for _ in 0 .. 31 do
      let x = rx.Next()
      let y = ry.Next()
      out.Write(Acc.sat (x * p.c + y * p.d))

  let kernel = Kernel.define "probe-loop-fusion" <@ init @> <@ trig @> <@ render @>


/// Helpers wrapping `Region.read` and `Region.write` called inside a `DO` loop at two distinct
/// affine offsets (`i` and `i + 16`), split onto two post-incrementing address registers (`x:(r1)+`
/// and `x:(r2)+`); followed by an outer `DO` loop with a nested `DO` loop and a contiguous Y-state
/// block walked by `y:(r4)+`.
[<ReflectedDefinition>]
module OuterWalkAndRegions =
  type S =
    { mutable z1 : Frac
      mutable z2 : Frac
      mutable z3 : Frac
      mutable z4 : Frac }

  let readX (idx: Frac) = Region.read Scratch.x idx
  let writeX (idx: Frac) (v: Frac) = Region.write Scratch.x idx v

  let tapPair (p: Banks.P) (i: int) =
    let a = readX (Frac.ofRaw i)
    let b = readX (Acc.hi (Acc.ofFrac (Frac.ofRaw i) + Frac.ofRaw 16))
    Acc.sat (a * p.a + b * p.b)

  let init (_: Banks.P) (s: S) =
    s.z1 <- Frac.zero
    s.z2 <- Frac.zero
    s.z3 <- Frac.zero
    s.z4 <- Frac.zero

  let trig (_: Banks.P) (s: S) =
    s.z1 <- Frac.zero
    s.z2 <- Frac.zero

  let render (p: Banks.P) (s: S) (out: Output) =
    for i in 0 .. 31 do
      writeX (Frac.ofRaw i) (Acc.hi (Acc.ofFrac p.a + Frac.ofRaw i))
    for i in 0 .. 15 do
      let t = tapPair p i
      for _ in 0 .. 1 do
        s.z1 <- Acc.sat (Acc.ofFrac s.z1 + (Acc.ofFrac t >>> 2))
      s.z2 <- Acc.sat (Acc.ofFrac s.z2 + (Acc.ofFrac s.z1 >>> 1))
      s.z3 <- Acc.sat (Acc.ofFrac s.z3 + (Acc.ofFrac s.z2 >>> 1))
      s.z4 <- Acc.sat (Acc.ofFrac s.z4 + (Acc.ofFrac s.z3 >>> 1))
      out.Write s.z3
      out.Write s.z4

  let kernel = Kernel.define "probe-outer-walk-regions" <@ init @> <@ trig @> <@ render @>


/// Back-to-back region accesses at computed offsets from a state word (reusing `r0` across
/// `mds: import` markers without `move a1,a` or redundant `move #>$0,r0`), an accumulator
/// clamp whose branch `tfr` folds into `b` so the 3-word accumulator save/reload drops, and
/// a state-array loop whose trailing `move (r1)+` folds into `x:(r1)+`.
[<ReflectedDefinition>]
module ImportAndClampForward =
  [<XMemory>]
  type S =
    { mutable baseOff : Frac
      [<Length 8>] tap : Frac array }

  let init (_: Banks.P) (s: S) =
    s.baseOff <- Frac.ofRaw 2
    for i in 0 .. 7 do
      s.tap[i] <- Frac.zero

  let trig (_: Banks.P) (s: S) =
    for i in 0 .. 7 do
      s.tap[i] <- Frac.ofRaw i

  let render (p: Banks.P) (s: S) (out: Output) =
    let i0 = Acc.hi (Acc.ofFrac s.baseOff + Frac.ofRaw 1)
    let i1 = Acc.hi (Acc.ofFrac s.baseOff + Frac.ofRaw 2)
    let i2 = Acc.hi (Acc.ofFrac s.baseOff + Frac.ofRaw 3)
    Region.write Scratch.x i0 p.a
    Region.write Scratch.x i1 p.b
    let r0 = Region.read Scratch.x i0
    let r1 = Region.read Scratch.x i1
    Region.write Scratch.x i2 (Acc.sat (Acc.ofFrac r0 + Acc.ofFrac r1))
    let r2 = Region.read Scratch.x i2
    for i in 0 .. 7 do
      s.tap[i] <- Acc.sat (Acc.ofFrac s.tap[i] + (Acc.ofFrac r2 >>> 3))
    for _ in 0 .. 31 do
      let x = p.a * r2 + p.b * p.c
      let c1 = Acc.sat (if x > Acc.ofFrac p.d then Acc.ofFrac p.d else x)
      let c2 = Acc.sat (if x < Acc.zero then Acc.zero else x)
      out.Write (Acc.sat (Acc.ofFrac c1 + Acc.ofFrac c2))

  let kernel = Kernel.define "probe-import-and-clamp-forward" <@ init @> <@ trig @> <@ render @>


/// A counted `while` loop accessing a region at `s.baseOff + i` through a helper (canonicalized to
/// a `DO` loop with `x:(r1)+` post-increment), an outer local read twice across a 32-sample loop
/// that yields `X1`/`Y1` to the loop's inner locals, and a two-sided clamp across `Bcc`.
[<ReflectedDefinition>]
module CountedWhileAndLoopYield =
  type S =
    { mutable baseOff : Frac
      mutable z       : Frac }

  let writeAt (s: S) (idx: Frac) (v: Frac) =
    Region.write Scratch.x (Acc.hi (Acc.ofFrac s.baseOff + idx)) v

  let readAt (s: S) (idx: Frac) =
    Region.read Scratch.x (Acc.hi (Acc.ofFrac s.baseOff + idx))

  let init (_: Banks.P) (s: S) =
    s.baseOff <- Frac.ofRaw 4
    s.z <- Frac.zero

  let trig (_: Banks.P) (s: S) =
    s.z <- Frac.zero

  let render (p: Banks.P) (s: S) (out: Output) =
    let outer1 = Acc.sat (p.a * p.b)
    let outer2 = Acc.sat (p.c * p.d)
    s.z <- Acc.sat (Acc.ofFrac outer1 + Acc.ofFrac outer2)
    let mutable k = Frac.zero
    while k <= Frac.ofRaw 15 do
      writeAt s k (Acc.hi (Acc.ofFrac p.a + k))
      k <- Acc.hi (Acc.ofFrac k + Frac.ofRaw 1)
    for i in 0 .. 15 do
      let x = readAt s (Frac.ofRaw i)
      let u = Acc.sat (x * p.a + s.z * p.b)
      let v = Acc.sat (u * p.c - x * p.d)
      let raw = u * v + v * p.a
      let hi = Acc.ofFrac (Frac.ofRaw 0x300000)
      let lo = Acc.ofFrac (Frac.ofRaw -0x300000)
      let c1 = if raw > hi then hi else raw
      let c2 = if c1 < lo then lo else c1
      out.Write (Acc.sat c2)
      out.Write (Acc.sat (c2 + u * p.d))
    s.z <- Acc.sat (Acc.ofFrac s.z + outer1 * outer2)

  let kernel = Kernel.define "probe-counted-while-loop-yield" <@ init @> <@ trig @> <@ render @>


/// Step 2 intrinsics: Min / Max / Clamp (tupled, curried, FSharp.Core min/max, compile-time
/// System.Math), Acc.abs / Frac.abs / FSharp.Core abs, and Long.pack / Long.hi / Long.lo.
[<ReflectedDefinition>]
module Step2Intrinsics =
  type S =
    { mutable w    : Frac
      mutable acc  : Acc
      mutable pair : Long
      [<Length 4>]
      pairs        : Long array }

  [<Const>]
  let constLo () = Frac.ofRaw (System.Math.Min(-0x200000, 0x100000))

  [<Const>]
  let constHi () = Frac.ofRaw (System.Math.Max(-0x200000, 0x300000))

  [<Const>]
  let constMid () = Frac.ofRaw (System.Math.Clamp(0x500000, -0x100000, 0x250000))

  [<Const>]
  let constAbs () = Frac.ofRaw (System.Math.Abs(-0x123456))

  [<Subroutine>]
  let clampSub (x: Frac) (lo: Frac) (hi: Frac) =
    let a = Frac.Clamp(x, lo, hi)
    let b = min a hi
    max b lo

  [<Subroutine>]
  let absSub (x: Frac) (y: Frac) =
    let ax = Frac.abs x
    let ay = abs y
    Acc.sat (Acc.abs (x * y) + ax - ay)

  [<Subroutine>]
  let packSub (hi: Frac) (lo: Frac) =
    let p = Long.pack hi lo
    Acc.sat (Acc.ofFrac (Long.hi p) + Long.lo p)

  let init (_: Banks.P) (s: S) =
    s.w <- Frac.zero
    s.acc <- Acc.zero
    s.pair <- Long.pack (Frac.ofRaw 0x123456) (Frac.ofRaw 0x800001)
    for i in 0 .. 3 do
      s.pairs[i] <- Long.Pack(Frac.ofRaw i, Frac.ofRaw 0x800005)

  let trig (p: Banks.P) (s: S) =
    let fMin = Frac.Min(p.a, p.b)
    let fMax = Frac.max p.c p.d
    s.w <- Frac.clamp (constLo ()) (constHi ()) (min fMin fMax)
    let a1 = Acc.Min(p.a * p.b, p.c * p.d)
    let a2 = Acc.max a1 (Acc.ofFrac (constMid ()))
    s.acc <- Acc.Clamp(max a1 a2, Acc.ofFrac (constLo ()), Acc.ofFrac (constHi ()))
    let l1 = Long.pack p.a p.b
    let l2 = Long.Pack(p.c, p.d)
    let lMin = Long.Min(l1, l2)
    let lMax = max l1 l2
    s.pair <- Long.Clamp(lMin, Long.min l1 l2, Long.Max(lMax, s.pair))

  let renderMinMax (p: Banks.P) (s: S) (out: Output) =
    let lo = Frac.Min(p.a, p.b)
    let hi = Frac.Max(p.a, p.b)
    let mutable running = s.acc
    for i in 0 .. 31 do
      let raw = Acc.sat (Acc.ofFrac p.c + Frac.ofRaw i)
      let c1 = Frac.Clamp(raw, lo, hi)
      let c2 = clampSub c1 (constLo ()) (constHi ())
      running <- Acc.Clamp(running + c1 * p.d, Acc.ofFrac lo, Acc.ofFrac hi)
      running <- min running (Acc.ofFrac (constHi ()))
      out.Write(Acc.sat (running + c2))
    s.acc <- running
    s.w <- Frac.Max(s.w, lo)

  let renderAbs (p: Banks.P) (s: S) (out: Output) =
    let c = constAbs ()
    let negOne = Frac.ofRaw 0x800000
    s.w <- Frac.Abs negOne
    let mutable running = Acc.abs (p.a * p.b - p.c * p.d)
    for _ in 0 .. 31 do
      running <- Acc.Abs(running - p.a * p.c)
      running <- abs running
      let f1 = Frac.abs p.a
      let f2 = absSub p.b p.d
      out.Write(Acc.sat (running + f1 * c + f2 * s.w))
    s.acc <- running

  let renderLongPack (p: Banks.P) (s: S) (out: Output) =
    let lowSet = Frac.ofRaw 0x800005
    s.pair <- Long.pack p.a lowSet
    let directHi = Long.hi (Long.pack p.b p.c)
    let directLo = Long.Lo(Long.Pack(p.b, p.d))
    s.w <- Acc.sat (Acc.ofFrac directHi + directLo)
    for i in 0 .. 3 do
      let prev = s.pairs[i]
      let nextHi = Acc.sat (Acc.ofFrac (Long.hi prev) + p.a)
      let nextLo = Acc.sat (Acc.ofFrac (Long.lo prev) + p.b)
      s.pairs[i] <- Long.pack nextHi nextLo
    for _ in 0 .. 7 do
      for i in 0 .. 3 do
        let elem = s.pairs[i]
        let h = Long.Hi elem
        let l = Long.lo elem
        let sub = packSub p.c p.d
        out.Write(Acc.sat (Acc.ofFrac h + l + Long.hi s.pair + Long.lo s.pair + sub + s.w))

  let renderRuntimeMath (p: Banks.P) (s: S) (out: Output) =
    let bad = Frac.ofRaw (System.Math.Min(p.a.Raw, p.b.Raw))
    s.w <- bad
    for _ in 0 .. 31 do
      out.Write bad

  let minMaxKernel = Kernel.define "probe-step2-minmax" <@ init @> <@ trig @> <@ renderMinMax @>
  let absKernel = Kernel.define "probe-step2-abs" <@ init @> <@ trig @> <@ renderAbs @>
  let longPackKernel = Kernel.define "probe-step2-long-pack" <@ init @> <@ trig @> <@ renderLongPack @>
  let runtimeMathKernel = Kernel.define "probe-step2-runtime-math" <@ init @> <@ trig @> <@ renderRuntimeMath @>


/// Step 3 optimiser passes: `select` (branch diamonds over pure register/temporary updates lowered
/// to branchless `Tcc`/`IfCc`), `tableWalk` (sequential `Table.read` and `Table.row` chained on
/// `r0` via `p:(r0)+`/`p:(r0)-` and constant offset folding), and `narrow` (24-bit `Acc.ofFrac`
/// webs narrowed to 1-word slots while preserving 56-bit product webs).
[<ReflectedDefinition>]
module Step3Optimiser =
  let tbl =
    Table.program "step3_tbl" [|
      0x040000; 0x080000; 0x0c0000; 0x100000; 0x140000; 0x180000; 0x1c0000; 0x200000
      0x240000; 0x280000; 0x2c0000; 0x300000; 0x340000; 0x380000; 0x3c0000; 0x400000
      0x440000; 0x480000; 0x4c0000; 0x500000; 0x540000; 0x580000; 0x5c0000; 0x600000
      0x640000; 0x680000; 0x6c0000; 0x700000; 0x740000; 0x780000; 0x7c0000; 0x7fffff
    |]

  type S =
    { mutable w    : Frac
      mutable acc  : Acc
      mutable seed : Frac }

  let init (_: Banks.P) (s: S) =
    s.w <- Frac.zero
    s.acc <- Acc.zero
    s.seed <- Frac.ofRaw 0x13579b

  let trig (_: Banks.P) (s: S) =
    s.w <- Frac.zero
    s.acc <- Acc.zero

  let renderSelect (p: Banks.P) (s: S) (out: Output) =
    let mutable v = s.w
    let mutable seed = s.seed
    for _ in 0 .. 31 do
      seed <- Frac.lfsr 0xc20001 seed
      let x = Acc.sat (seed * p.a + v * p.b)
      let hi = if x > p.c then p.c else x
      let clamped = if hi < p.d then p.d else hi
      let mutable a = Acc.ofFrac clamped
      if Frac.bit 0 seed then a <- -a
      if Frac.bit 1 seed then a <- a >>> 1
      let stepped = Acc.sat a
      v <- if stepped = Frac.zero then seed else stepped
      out.Write v
    s.w <- v
    s.seed <- seed

  let renderTableWalk (p: Banks.P) (s: S) (out: Output) =
    let mutable acc = s.acc
    for i in 0 .. 31 do
      let idx = Acc.hi (Acc.andHi 0x03 (Acc.ofFrac p.a + Frac.ofRaw i))
      let c0 = Table.row tbl 4 idx 0
      let c1 = Table.row tbl 4 idx 1
      let c2 = Table.row tbl 4 idx 2
      let c3 = Table.row tbl 4 idx 3
      let baseIdx = Acc.hi (Acc.andHi 0x0f (Acc.ofFrac p.b + Frac.ofRaw i))
      let t0 = Table.read tbl baseIdx
      let t1 = Table.read tbl (Acc.hi (Acc.ofFrac baseIdx + Frac.ofRaw 1))
      let t2 = Table.read tbl (Acc.hi (Acc.ofFrac baseIdx + Frac.ofRaw 5))
      let c5Idx = Acc.hi (Acc.andHi 0x0f (Acc.ofFrac p.c + Frac.ofRaw i))
      let t5 = Table.read tbl (Acc.hi (Acc.ofFrac c5Idx + Frac.ofRaw 5))
      acc <- Acc.sat (acc + c0 * p.a + c1 * p.b + c2 * p.c + c3 * p.d + t0 * t1 + Acc.ofFrac t2 + Acc.ofFrac t5) |> Acc.ofFrac
      out.Write(Acc.sat acc)
    s.acc <- acc

  let renderNarrow (p: Banks.P) (s: S) (out: Output) =
    let mutable v = s.w
    for _ in 0 .. 31 do
      let w1 = Acc.ofFrac p.a
      let w2 = Acc.ofFrac p.b
      let w3 = Acc.ofFrac p.c
      let w4 = Acc.ofFrac p.d
      let w5 = Acc.ofFrac v
      let full56 = p.a * p.b + p.c * p.d
      let comb = full56 + (w1 >>> 2) + (w2 >>> 3) - (w3 >>> 2) + (w4 >>> 3) + (w5 >>> 1)
      let loWord = Acc.lo full56
      v <- Acc.sat (comb + Acc.ofFrac loWord)
      out.Write v
    s.w <- v

  type DeadCursorS =
    { [<Length 16>]
      mutable a1  : Frac array
      [<Length 16>]
      mutable a2  : Frac array
      [<Length 16>]
      mutable a3  : Frac array
      mutable z   : Frac }

  let initDeadCursor (_: Banks.P) (s: DeadCursorS) =
    for i in 0 .. 15 do
      s.a1[i] <- Frac.zero
      s.a2[i] <- Frac.zero
      s.a3[i] <- Frac.zero
    s.z <- Frac.zero

  let trigDeadCursor (_: Banks.P) (s: DeadCursorS) =
    s.z <- Frac.zero

  let renderDeadCursor (p: Banks.P) (s: DeadCursorS) (out: Output) =
    let c1 = Cursor.linear Scratch.x Frac.zero
    let c2 = Cursor.linear Scratch.x (Frac.ofRaw 8)
    let c3 = Cursor.linear Scratch.y Frac.zero
    let c4 = Cursor.linear Scratch.y (Frac.ofRaw 8)
    let c5 = Cursor.linear Scratch.x (Frac.ofRaw 16)
    c1.Put p.a
    c2.Put p.b
    c3.Put p.c
    c4.Put p.d
    c5.Put p.a
    for i in 0 .. 15 do
      s.a1[i] <- Acc.sat (Acc.ofFrac s.a1[i] + (Acc.ofFrac p.a >>> 3))
      s.a2[i] <- Acc.sat (Acc.ofFrac s.a2[i] + (Acc.ofFrac p.b >>> 3))
      s.a3[i] <- Acc.sat (Acc.ofFrac s.a3[i] + (Acc.ofFrac p.c >>> 3))
    let seed = Acc.sat (Acc.ofFrac (c4.Peek()) + c5.Peek())
    for i in 0 .. 15 do
      let v = Acc.sat (Acc.ofFrac s.a1[i] + s.a2[i] + s.a3[i] + seed * p.d)
      out.Write v
      out.Write(Acc.sat (v * p.a))
    s.z <- seed

  type HighS =
    { [<Length 64>]
      mutable pad : Frac array
      mutable z1  : Frac
      mutable z2  : Frac
      mutable z3  : Frac
      mutable z4  : Frac }

  let initHigh (_: Banks.P) (s: HighS) =
    for i in 0 .. 63 do
      s.pad[i] <- Frac.zero
    s.z1 <- Frac.zero
    s.z2 <- Frac.zero
    s.z3 <- Frac.zero
    s.z4 <- Frac.zero

  let trigHigh (_: Banks.P) (s: HighS) =
    s.z1 <- Frac.zero
    s.z2 <- Frac.zero
    s.z3 <- Frac.zero
    s.z4 <- Frac.zero

  let renderWalkHigh (p: Banks.P) (s: HighS) (out: Output) =
    for _ in 0 .. 31 do
      s.z1 <- Acc.sat (s.z1 * p.a + p.b)
      s.z2 <- Acc.sat (s.z2 * p.b + s.z1 * p.c)
      s.z3 <- Acc.sat (s.z3 * p.c + s.z2 * p.d)
      s.z4 <- Acc.sat (s.z4 * p.d + s.z3 * p.a)
      out.Write(Acc.sat (Acc.ofFrac s.z1 + s.z2 + s.z3 + s.z4))

  let renderUnswitch2 (p: Banks.P) (s: S) (out: Output) =
    let mutable v = s.w
    let mutable seed = s.seed
    for _ in 0 .. 31 do
      seed <- Frac.lfsr 0xc20001 seed
      if p.a > Frac.zero then
        v <- Acc.sat (v * p.a + seed * p.c)
      else
        v <- Acc.sat (v * p.b - seed * p.c)
      if p.b > Frac.zero then
        v <- Acc.sat (Acc.ofFrac v + seed * p.d)
      else
        v <- Acc.sat (Acc.ofFrac v - seed * p.d)
      out.Write v
    s.w <- v
    s.seed <- seed

  type WalkPhaseS =
    { mutable z1   : Frac
      mutable z2   : Frac
      mutable z3   : Frac
      mutable z4   : Frac
      [<Length 12>]
      mutable pad  : Frac array
      mutable w1   : Frac
      mutable w2   : Frac
      mutable w3   : Frac
      mutable w4   : Frac
      mutable seed : Frac }

  let initWalkPhase (_: Banks.P) (s: WalkPhaseS) =
    s.z1 <- Frac.zero
    s.z2 <- Frac.zero
    s.z3 <- Frac.zero
    s.z4 <- Frac.zero
    for i in 0 .. 11 do
      s.pad[i] <- Frac.zero
    s.w1 <- Frac.zero
    s.w2 <- Frac.zero
    s.w3 <- Frac.zero
    s.w4 <- Frac.zero
    s.seed <- Frac.ofRaw 0x2468ac

  let trigWalkPhase (_: Banks.P) (s: WalkPhaseS) =
    s.z1 <- Frac.zero
    s.z2 <- Frac.zero
    s.z3 <- Frac.zero
    s.z4 <- Frac.zero
    s.w1 <- Frac.zero
    s.w2 <- Frac.zero
    s.w3 <- Frac.zero
    s.w4 <- Frac.zero

  let renderWalkPhase (p: Banks.P) (s: WalkPhaseS) (out: Output) =
    let mutable seed = s.seed
    for _ in 0 .. 31 do
      seed <- Frac.lfsr 0xc20001 seed
      s.z1 <- Acc.sat (s.z1 * p.a + p.b)
      s.z2 <- Acc.sat (s.z2 * p.b + s.z1 * p.c)
      s.z3 <- Acc.sat (s.z3 * p.c + s.z2 * p.d)
      s.z4 <- Acc.sat (s.z4 * p.d + s.z3 * p.a)
      if Frac.bit 0 seed then
        s.w1 <- Acc.sat (s.w1 * p.a + s.z4 * p.b)
        s.w2 <- Acc.sat (s.w2 * p.b + s.w1 * p.c)
        s.w3 <- Acc.sat (s.w3 * p.c + s.w2 * p.d)
        s.w4 <- Acc.sat (s.w4 * p.d + s.w3 * p.a)
      out.Write(Acc.sat (Acc.ofFrac s.z4 + s.w4 * p.c))
    s.seed <- seed

  let selectKernel     = Kernel.define "probe-step3-select" <@ init @> <@ trig @> <@ renderSelect @>
  let tableWalkKernel  = Kernel.define "probe-step3-table-walk" <@ init @> <@ trig @> <@ renderTableWalk @>
  let narrowKernel     = Kernel.define "probe-step3-narrow" <@ init @> <@ trig @> <@ renderNarrow @>
  let deadCursorKernel = Kernel.define "probe-step3-dead-cursor" <@ initDeadCursor @> <@ trigDeadCursor @> <@ renderDeadCursor @>
  let walkHighKernel   = Kernel.define "probe-step3-walk-high" <@ initHigh @> <@ trigHigh @> <@ renderWalkHigh @>
  let walkPhaseKernel  = Kernel.define "probe-step3-walk-phase" <@ initWalkPhase @> <@ trigWalkPhase @> <@ renderWalkPhase @>
  let unswitch2Kernel  = Kernel.define "probe-step3-unswitch2" <@ init @> <@ trig @> <@ renderUnswitch2 @>


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


module JoinCases =
  open dsp56k.dsl
  open dsp56k.dsl.Asm

  let tests = testList "accumulator join equality" [
    let openCode body =
      body |> Array.mapi (fun i item -> item, i)
      |> Optimize.joinAccumulators 20 23
      |> Array.map fst
    let spill =
      [| move a1 (xm (disp r6 20))
         move a0 (xm (disp r6 21))
         move a2 (xm (disp r6 22)) |]
    let reload =
      [| move (xm (disp r6 20)) a
         move (xm (disp r6 21)) a0
         move (xm (disp r6 22)) a2 |]
    let diamond change =
      Array.concat
        [ [| label "ENTRY" |]
          spill
          [| tst a; bge "JOIN" |]
          change
          [| label "JOIN" |]
          reload
          [| rts |] ]
    test "different values on both paths still equal their shared home" {
      let body = diamond (Array.append [| clr a |] spill)
      Expect.equal (openCode body).Length (body.Length - 3) "the whole reload is redundant"
    }
    test "a changed low part without a matching store retains the reload" {
      let body = diamond [| move (imm 1) a0 |]
      Expect.equal (openCode body) body "one unequal raw part prevents forwarding"
    }
    test "an extension write without a matching store retains the reload" {
      let body = diamond [| move (imm 1) a2 |]
      Expect.equal (openCode body) body "extension equality is required too"
    }
    test "an X-memory state home is outside the temporary range" {
      let body = diamond (Array.append [| clr a |] spill)
      let actual =
        body |> Array.map (fun item -> item, 0)
        |> Optimize.joinAccumulators 23 26
        |> Array.map fst
      Expect.equal actual body "state must not be treated as disposable spill storage"
    }
    test "an indirect X store may alias the home" {
      let body = diamond [| move x0 (xm (ind r0)) |]
      Expect.equal (openCode body) body "unknown X alias invalidates equality"
    }
    test "moving the workset base invalidates the home" {
      let body = diamond [| move r0 r6 |]
      Expect.equal (openCode body) body "R6 must keep its meaning"
    }
    test "a call is a barrier even when its local target is known" {
      let body = Array.append (diamond [| bsr "CALLEE" |]) [| label "CALLEE"; rts |]
      Expect.equal (openCode body) body "callee effects cannot be assumed"
    } ]

let tests =
  testList "kernels" [
    JoinCases.tests

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
      }

      // 64 is the MDS's: it renders track t with r6 = $800 + $40 t, so X and Y words past r6+63
      // are the next track's. Another ABI is another Target record.
      test "the workset caps are the target's: a target with a wider X workset takes what the MDS refuses" {
        let wide = { Kernel.mds with name = "MDS, a 128-word X workset"; xLimit = 128 }
        match dsp56k.kernel.Compile.compile wide XFull.beyond with
        | Error e -> failtest e
        | Ok c -> Expect.isGreaterThan c.xWorkset 64 "X words past X+63"
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
      test "Long.ofAccSat keeps a growing Long state limited to 48 bits: compiled program equals the .NET kernel" {
        needNative ()
        conformance LongSat.kernel 57
      }
      test "let of a condition, read in ifs, not, && and ||: compiled program equals the .NET kernel" {
        needNative ()
        conformance BoolLet.kernel 58
      }
      test "a loop index passed to helpers indexing state arrays: compiled program equals the .NET kernel" {
        needNative ()
        conformance IndexArg.kernel 59
      }
      test "[<Length n>] Long arrays in Y and X, by constant and loop index: compiled program equals the .NET kernel" {
        needNative ()
        conformance LongArray.kernel 60
      }
      test "table reads at a constant index: direct addresses, fewer words and cycles, the same words" {
        needNative ()
        let rng = Random 64
        let steps =
          [ yield Words(randomWords rng)
            yield Init
            for _ in 1 .. 50 do
              yield Words(randomWords rng)
              yield Render 1 ]
        let graded k =
          match grade k probeMachine None { name = "random words"; steps = steps } |> Result.map keep with
          | Error e -> failtest e
          | Ok r ->
            Expect.isEmpty r.correctness (text r)
            r
        let direct, held = graded ConstTable.constantKernel, graded ConstTable.heldKernel
        let listing = dsp56k.dsl.Program.source direct.compiled.items
        Expect.isFalse (listing.Contains "p:(r0)") "program words read at a constant index without r0"
        Expect.isFalse (listing.Contains "(r0+") "the sine word read at a constant index without r0"
        Expect.equal direct.dsp.outputs held.dsp.outputs "the same output as indexes held in state"
        Expect.equal direct.dsp.states held.dsp.states "the same state as indexes held in state"
        Expect.isLessThan direct.ours.programWords held.ours.programWords "fewer program words"
        Expect.isLessThan direct.ours.renderWorst held.ours.renderWorst "fewer RENDER cycles"
      }
      test "chosen subroutines: a helper called from INIT and TRIG is one, the same words as inlined" {
        needNative ()
        let rng = Random 65
        let steps =
          [ yield Words(randomWords rng)
            yield Init
            for _ in 1 .. 50 do
              yield Words(randomWords rng)
              yield Trig
              yield Render 1 ]
        let graded k =
          match grade k probeMachine None { name = "random words, trigs"; steps = steps } |> Result.map keep with
          | Error e -> failtest e
          | Ok r ->
            Expect.isEmpty r.correctness (text r)
            r
        let sub, inlined = graded AutoSubs.subKernel, graded AutoSubs.inlineKernel
        let listing (r: Report) = dsp56k.dsl.Program.source r.compiled.items
        let lines (r: Report) (start: string) = (listing r).Split('\n') |> Array.filter (fun l -> l.Trim().StartsWith start)
        Expect.equal (lines sub "SUB_").Length 1 "one subroutine"
        Expect.equal (lines sub "bsr").Length 4 "called from the four places"
        Expect.isEmpty (lines inlined "SUB_") "[<Inline>]: no subroutine"
        Expect.equal sub.dsp.outputs inlined.dsp.outputs "the same output as inlined"
        Expect.equal sub.dsp.states inlined.dsp.states "the same state as inlined"
        Expect.isLessThan sub.ours.programWords (inlined.ours.programWords - Optimize.minSaving) "fewer program words"
        Expect.equal sub.ours.renderWorst inlined.ours.renderWorst "RENDER as it was"
      }
      test "chosen subroutines: Off keeps every helper inlined" {
        match Optimize.compileChoosing Optimize.Off None Optimize.Peephole Kernel.mds AutoSubs.subKernel with
        | Error e -> failtest e
        | Ok c -> Expect.isFalse ((dsp56k.dsl.Program.source c.items).Contains "SUB_") "no subroutine"
      }
      test "chosen subroutines: a helper in RENDER's loop stays inlined" {
        needNative ()
        conformance AutoSubs.hotKernel 66
        match Optimize.compile Optimize.Peephole Kernel.mds AutoSubs.hotKernel with
        | Error e -> failtest e
        | Ok c -> Expect.isFalse ((dsp56k.dsl.Program.source c.items).Contains "SUB_") "inlined in RENDER's loop"
      }
      test "chosen subroutines: a kernel too large inlined fits with RENDER's helper as a subroutine" {
        needNative ()
        let limit = Kernel.programLimit Kernel.mds |> Option.map fst
        match Optimize.compileChoosing Optimize.Off limit Optimize.Peephole Kernel.mds AutoSubs.sizeKernel |> Result.bind Compile.checkSize with
        | Ok _ -> failtest "inlined, the kernel should not fit"
        | Error _ -> ()
        match Optimize.compile Optimize.Peephole Kernel.mds AutoSubs.sizeKernel with
        | Error e -> failtest e
        | Ok c -> Expect.isTrue ((dsp56k.dsl.Program.source c.items).Contains "SUB_SETTLE") "settle as a subroutine"
        conformance AutoSubs.sizeKernel 67
      }
      test "a region's address taken twice, then zeros: the patched immediate is not a known 0" {
        needNative ()
        conformance AddressTwice.kernel 61
      }
      test "ring taps in a row, aligned base (modulo) and not (fallback): compiled program equals the .NET kernel" {
        needNative ()
        conformance RingTaps.kernel 63
      }
      test "ring taps in a row: grouped under one modulo register" {
        match Optimize.compile Optimize.Direct Kernel.mds RingTaps.kernel with
        | Error e -> failtest e
        | Ok c ->
          let text = c.items |> Array.map dsp56k.dsl.Transcribe.item
          Expect.equal (text |> Array.filter (fun l -> l.Contains "RING_FALLBACK") |> Array.length) 4 "two groups: a branch and a label each"
      }
      test "region indexes a loop steps (a stepped local, affine in the index): compiled program equals the .NET kernel" {
        needNative ()
        conformance RegionInduction.kernel 62
      }
      test "region indexes a loop steps: no r0/n0 rebuilt in the loops" {
        match Optimize.compile Optimize.Direct Kernel.mds RegionInduction.kernel with
        | Error e -> failtest e
        | Ok c ->
          let text = c.items |> Array.map dsp56k.dsl.Transcribe.item
          Expect.isFalse (text |> Array.exists (fun l -> l.Contains "(idx r0)")) "no (r0+n0) access left"
          Expect.isTrue (text |> Array.exists (fun l -> l.StartsWith "lua")) "a register stepped by lua"
      }
      test "two regions indexed by the same loop-bound let keep their own registers: compiled program equals the .NET kernel" {
        needNative ()
        match Optimize.compile Optimize.Direct Kernel.mds RegionSharedIndex.kernel with
        | Error e -> failtest e
        | Ok c ->
          let lines = c.items |> Array.map dsp56k.Print.item
          Expect.exists lines (fun l -> l.Contains "x:(r1)") "Scratch.x on r1"
          Expect.exists lines (fun l -> l.Contains "y:(r4)") "Scratch.y on r4"
        let steps = [ Words [| 0x200000; 0x100000; 0x400000; 0x300000 |]; Init; Render 1 ]
        match grade RegionSharedIndex.kernel probeMachine None { name = "one block"; steps = steps } |> Result.map keep with
        | Error e -> failtest e
        | Ok r ->
          Expect.isEmpty r.correctness (text r)
          Expect.equal (r.dsp.outputs[0][0] &&& 0xffffff) 0x160000 "word 0: 0.25*0.5 + 0.125*0.375"
          Expect.equal (r.dsp.outputs[0][1] &&& 0xffffff) 0x100000 "word 1: 0.25 - 0.125"
          Expect.isLessThanOrEqual r.ours.renderWorst 850 "stepped address registers, within cycle ceiling"
        conformance RegionSharedIndex.kernel 68
      }
      test "two X regions through one let-bound index keep their own registers: compiled program equals the .NET kernel" {
        needNative ()
        match Optimize.compile Optimize.Direct Kernel.mds RegionSharedLet.kernel with
        | Error e -> failtest e
        | Ok c ->
          let lines = c.items |> Array.map dsp56k.Print.item
          let at (label: string) = Array.findIndex ((=) label) lines
          let registers (body: string array) =
            body |> Array.collect (fun l -> [| for m in Text.RegularExpressions.Regex.Matches(l, @"x:\((r\d)\)") -> m.Groups[1].Value |])
                 |> Array.countBy id
          let trig   = registers lines[at "TRIG:" + 1 .. at "RENDER:" - 1]
          let render = registers lines[at "RENDER:" + 1 ..]
          Expect.equal (Array.map snd trig) [| 2; 2 |] $"TRIG: a read and a write on each region's own register, {trig}"
          Expect.equal (Array.map snd render) [| 1; 1 |] $"RENDER: a read on each region's own register, {render}"
          Expect.isFalse (lines |> Array.exists (fun l -> l.Contains "(r0+n0)")) "no region word through r0+n0"
        let steps = [ Words [| 0; 0; 0; 0 |]; Init; Trig; Render 1 ]
        match grade RegionSharedLet.kernel probeMachine None { name = "one block"; steps = steps } |> Result.map keep with
        | Error e -> failtest e
        | Ok r ->
          Expect.isEmpty r.correctness (text r)
          Expect.equal (r.dsp.outputs[0][5] &&& 0xffffff) (Input.ring.words[5] &&& 0xffffff) "word 5: the input ring's, less the cleared scratch word"
          Expect.isLessThanOrEqual r.ours.renderWorst 240 "stepped address registers, within cycle ceiling"
        conformance RegionSharedLet.kernel 69
      }
      test "workset words in a DO loop walked by a pointer register: compiled program equals the .NET kernel" {
        needNative ()
        match Optimize.compile Optimize.Peephole Kernel.mds WorksetWalk.kernel with
        | Error e -> failtest e
        | Ok c ->
          let lines = c.items |> Array.map dsp56k.Print.item
          let at (label: string) = Array.findIndex ((=) label) lines
          let render = lines[at "RENDER:" + 1 ..]
          let doIdx  = render |> Array.findIndex (fun l -> l.TrimStart().StartsWith "do ")
          Expect.exists render[.. doIdx - 1] (fun l -> l.Contains "lua" && l.Contains "(r6+$9),r4") "pointer set up before the loop"
          Expect.exists render[doIdx + 1 ..] (fun l -> l.Contains "y:(r4)+") "stepped forward in the loop"
          Expect.exists render[doIdx + 1 ..] (fun l -> l.Contains "y:(r4)-") "stepped back in the loop"
          Expect.isFalse (render[doIdx + 1 ..] |> Array.exists (fun l -> l.Contains "y:(r6+$")) "no indexed Y state access left in the loop"
        let steps = [ Words [| 0x100000; 0x080000; 0; 0 |]; Init; Trig; Render 1 ]
        let noWalk = Optimize.Chosen { Optimize.Passes.all with walk = false }
        match Grade.gradeMdsAt noWalk WorksetWalk.kernel probeMachine None { name = "one block"; steps = steps },
              grade WorksetWalk.kernel probeMachine None { name = "one block"; steps = steps } |> Result.map keep with
        | Ok before, Ok after ->
          Expect.isEmpty after.correctness (text after)
          Expect.isLessThan after.ours.renderWorst (before.ours.renderWorst - 200L) "walk saves >200 cycles on the 32-sample loop"
          Expect.isLessThanOrEqual after.ours.renderWorst 1100L "walked state words, within cycle ceiling"
        | Error e, _ | _, Error e -> failtest e
        conformance WorksetWalk.kernel 70
      }
      test "live-range allocation across biquad and btst loops: compiled program equals the .NET kernel" {
        needNative ()
        match Optimize.compile Optimize.Peephole Kernel.mds LiveRanges.kernel with
        | Error e -> failtest e
        | Ok c ->
          let lines = c.items |> Array.map dsp56k.Print.item
          let at (label: string) = Array.findIndex ((=) label) lines
          let render = lines[at "RENDER:" + 1 ..]
          let doIdxs = [| for i in 0 .. render.Length - 1 do if render[i].TrimStart().StartsWith "do " then i |]
          Expect.equal doIdxs.Length 2 "two 32-sample loops in RENDER"
          let secondLoop = render[doIdxs[1] + 1 ..]
          Expect.isFalse (secondLoop |> Array.exists (fun l -> l.Contains "x:(r6" || l.Contains "x:(r2")) "biquad loop keeps z2 and fed in b1/b0 without X workset spills"
          Expect.isFalse (render |> Array.exists (fun l -> l.Contains "a0,x:" || l.Contains "a2,x:")) "no 3-word accumulator spill in RENDER"
        conformance LiveRanges.kernel 74
      }
      test "cross-stage cursor loop fusion and dual X:Y loads: compiled program equals the .NET kernel" {
        needNative ()
        match Optimize.compile Optimize.Peephole Kernel.mds LoopFusion.kernel with
        | Error e -> failtest e
        | Ok c ->
          let lines = c.items |> Array.map dsp56k.Print.item
          let at (label: string) = Array.findIndex ((=) label) lines
          let render = lines[at "RENDER:" + 1 ..]
          let doIdxs = [| for i in 0 .. render.Length - 1 do if render[i].TrimStart().StartsWith "do " then i |]
          Expect.equal doIdxs.Length 3 "four source loops fused to three DO loops in RENDER"
          let mixLoop = render[doIdxs[2] + 1 ..]
          Expect.exists mixLoop (fun l -> l.Contains "x:(r1)+" && l.Contains "y:(r4)+") "final mix loop packs both cursor reads into one dual X:Y move"
        conformance LoopFusion.kernel 75
      }
      test "helper-wrapped region affine splitting and outer DO loop workset walking: compiled program equals the .NET kernel" {
        needNative ()
        match Optimize.compile Optimize.Peephole Kernel.mds OuterWalkAndRegions.kernel with
        | Error e -> failtest e
        | Ok c ->
          let lines = c.items |> Array.map dsp56k.Print.item
          let at (label: string) = Array.findIndex ((=) label) lines
          let render = lines[at "RENDER:" + 1 ..]
          Expect.isFalse (render |> Array.exists (fun l -> l.Contains "(r0+n0)")) "no (r0+n0) region access in RENDER"
          Expect.exists render (fun l -> l.Contains "x:(r1)+") "first region offset stepped with x:(r1)+"
          Expect.exists render (fun l -> l.Contains "x:(r2)+") "second region offset split onto x:(r2)+"
          Expect.exists render (fun l -> l.Contains "y:(r4)+") "outer DO loop state block walked with y:(r4)+"
        conformance OuterWalkAndRegions.kernel 76
      }
      test "import base forwarding, accumulator clamp tfr folding, and DO loop post-increment folding: compiled program equals the .NET kernel" {
        needNative ()
        match Optimize.compile Optimize.Peephole Kernel.mds ImportAndClampForward.kernel with
        | Error e -> failtest e
        | Ok c ->
          let lines = c.items |> Array.map dsp56k.Print.item
          let at (label: string) = Array.findIndex ((=) label) lines
          let render = lines[at "RENDER:" + 1 ..]
          let importCount = render |> Array.filter (fun l -> l.Contains "mds: import") |> Array.length
          Expect.equal importCount 1 "six back-to-back region accesses share one r0 import base load in RENDER"
          Expect.isFalse (render |> Array.exists (fun l -> l.Contains "move    a1,a")) "no redundant move a1,a before move a1,n0"
          Expect.isFalse (render |> Array.exists (fun l -> l.Contains "move    (r1)+")) "trailing move (r1)+ folded into x:(r1)+"
          Expect.isFalse (render |> Array.exists (fun l -> l.Contains ",a2")) "accumulator clamp needs no 3-word save/reload"
        conformance ImportAndClampForward.kernel 77
      }
      test "counted while loop canonicalization, loop register yielding, and two-sided clamp folding: compiled program equals the .NET kernel" {
        needNative ()
        match Optimize.compile Optimize.Peephole Kernel.mds CountedWhileAndLoopYield.kernel with
        | Error e -> failtest e
        | Ok c ->
          let lines = c.items |> Array.map dsp56k.Print.item
          let at (label: string) = Array.findIndex ((=) label) lines
          let render = lines[at "RENDER:" + 1 ..]
          let doIdxs = [| for i in 0 .. render.Length - 1 do if render[i].TrimStart().StartsWith "do " then i |]
          Expect.equal doIdxs.Length 2 "while loop and for loop both compile to DO loops in RENDER"
          Expect.isFalse (render |> Array.exists (fun l -> l.Contains "(r0+n0)")) "region while loop and for loop both step address registers without (r0+n0)"
        conformance CountedWhileAndLoopYield.kernel 78
      }
      test "register locals read in both branches of an if: compiled program equals the .NET kernel" {
        needNative ()
        conformance Branches.kernel 56
      }
      test "four Acc locals live across a loop body: compiled program equals the .NET kernel" {
        needNative ()
        conformance FourAcc.kernel 55
      }
      test "frames over the caller's dead locals: a chain of calls takes the words its live locals need" {
        match Optimize.compile Optimize.Peephole Kernel.mds Frames.kernel with
        | Error e -> failtest e
        | Ok c ->
          // stacked on the caller's every local (as before), the chain took 32 X words
          Expect.isLessThanOrEqual c.xWorkset 17 "t1..t4 and middle's dead locals given to the frames"
      } ]

    testList "[<Unroll>]" [
      test "written out: compiled program equals the .NET kernel" {
        needNative ()
        conformance Unrolled.kernel 53
      }
      test "in an entry, nested, the index a constant: compiled program equals the .NET kernel" {
        needNative ()
        conformance Unrolled.flatKernel 54
      }
      test "no DO loop left where the loops are unrolled" {
        match Optimize.compile Optimize.Peephole Kernel.mds Unrolled.flatKernel with
        | Error e -> failtest e
        | Ok c ->
          let loops = c.items |> Array.filter (fun it -> match it with dsp56k.Item.Op(dsp56k.Loop _) -> true | _ -> false)
          Expect.isEmpty loops "RENDER's two loops written out"
      }
      test "the system stack: the taps as a DO loop take 14 levels, written out 12" {
        match Optimize.compile Optimize.Peephole Kernel.mds Unrolled.deepKernel with
        | Ok _ -> failtest "compiled"
        | Error e -> Expect.stringContains e "takes 14 system stack levels" "four calls, five DO loops"
        match Optimize.compile Optimize.Peephole Kernel.mds Unrolled.kernel with
        | Error e -> failtest e
        | Ok _ -> ()
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

    test "a factor in a register the multiplier cannot take (B, A1): loaded into a data register" {
      let data r = r = dsp56k.Reg.X0 || r = dsp56k.Reg.X1 || r = dsp56k.Reg.Y0 || r = dsp56k.Reg.Y1
      for held in [ dsp56k.Reg.B; dsp56k.Reg.A1; dsp56k.Reg.B1 ] do
        for other in [ Compile.WImm 5; Compile.WReg dsp56k.Reg.X1; Compile.WReg dsp56k.Reg.Y0; Compile.WReg held ] do
          for w1, w2 in [ Compile.WReg held, other; other, Compile.WReg held ] do
            let _, (s, t) = Compile.placeFactors w1 w2
            Expect.isTrue (data s && data t && (Compile.multiplierPairs.Contains((s, t)) || Compile.multiplierPairs.Contains((t, s)))) $"{w1} * {w2}: {s},{t}"
    }

    test "LSR and shifts by a count when running: compiled program equals the .NET kernel" {
      needNative ()
      conformance Probes.shiftsKernel 8
    }

    test "LSR and shifts by a count, .NET: a1 alone, the count's low six bits" {
      let acc (v: int64) = Acc.ofRaw v
      let w (v: int) = Frac.ofRaw v
      Expect.equal (Frac.lsr 4 (w 0x800000)).Raw 0x080000 "zeros in from the top"
      Expect.equal (Frac.lsr 24 (w 0xffffff)).Raw 0 "24 on gives 0"
      Expect.equal (Frac.lsrBy (w 0x41) (w 0xfffffe)).Raw 0x7fffff "the count's low six bits"
      Expect.equal (Frac.lsrBy (w 30) (w 0xffffff)).Raw 0 "a count of 24 on gives 0"
      Expect.equal (Acc.lsr 4 (acc 0xff800000123456L)).Raw (Bits.sext56 0xff080000123456L) "a2 and a0 kept"
      Expect.equal (Acc.lsrBy (w 25) (acc 0x12abcdef654321L)).Raw 0x12000000654321L "a1 cleared, a2 and a0 kept"
      Expect.equal (Acc.asrBy (w 0x43) (acc -0x80L)).Raw -0x10L "arithmetic, by the low six bits"
      Expect.equal (Acc.asrBy (w 63) (acc -5L)).Raw -1L "all sign"
      Expect.equal (Acc.aslBy (w 4) (acc 0x10000000000000L)).Raw 0L "bits past bit 55 are lost"
      Expect.equal (Acc.aslBy (w 0x7fffc1) (acc 3L)).Raw 6L "the count's low six bits"
      Expect.equal (Acc.aslBy (w 56) (acc -1L)).Raw 0L "56 on gives 0"
    }

    test "LSR and shifts by a count: the register forms, no loop" {
      match Optimize.compile Optimize.Peephole Kernel.mds Probes.shiftsKernel with
      | Error e -> failtest e
      | Ok c ->
        let has f = c.items |> Array.exists (fun it -> match it with dsp56k.Item.Op i -> f i | _ -> false)
        Expect.isTrue (has (function dsp56k.ShiftReg(dsp56k.ShAsr, _, _, _) -> true | _ -> false)) "asr s,a,a"
        Expect.isTrue (has (function dsp56k.ShiftReg(dsp56k.ShAsl, _, _, _) -> true | _ -> false)) "asl s,a,a"
        Expect.isTrue (has (function dsp56k.ShiftReg(dsp56k.ShLsr, _, _, _) -> true | _ -> false)) "lsr s,a"
        Expect.isTrue (has (function dsp56k.ShiftImm(dsp56k.ShLsr, 26, _, _) -> true | _ -> false)) "a constant count: lsr #26,a"
        Expect.isTrue (has (function dsp56k.ShiftImm(dsp56k.ShAsr, 7, _, _) -> true | _ -> false)) "the low six bits of $47: asr #7"
        Expect.isFalse (has (function dsp56k.Rep _ -> true | _ -> false)) "no REP"
    }

    test "bit tests as conditions: compiled program equals the .NET kernel" {
      needNative ()
      conformance Probes.bitsKernel 9
    }

    test "bit tests: btst and a branch on the carry, the bit checked on .NET" {
      Expect.isTrue (Frac.bit 23 (Frac.ofRaw 0x800000)) "the sign bit"
      Expect.isFalse (Frac.bit 22 (Frac.ofRaw 0x800000)) "another"
      Expect.isTrue (Frac.bit 0 (Frac.ofRaw -1)) "bit 0 of all ones"
      match Optimize.compile Optimize.Peephole Kernel.mds Probes.bitsKernel with
      | Error e -> failtest e
      | Ok c ->
        let tests = c.items |> Array.filter (fun it -> match it with dsp56k.Item.Op(dsp56k.BitOp(dsp56k.Btst, _, _)) -> true | _ -> false)
        Expect.isGreaterThanOrEqual tests.Length 3 "a btst per test"
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

    test "Min, Max and Clamp (Frac, Acc, Long, FSharp.Core min/max, compile-time System.Math): branchless Tcc, equals the .NET kernel" {
      match Optimize.compile Optimize.Peephole Kernel.mds Step2Intrinsics.minMaxKernel with
      | Error e -> failtest e
      | Ok c ->
        let hasTcc = c.items |> Array.exists (function dsp56k.Item.Op(dsp56k.Tcc((dsp56k.Cond.Lt | dsp56k.Cond.Gt), _, _)) -> true | _ -> false)
        let hasBcc = c.items |> Array.exists (function dsp56k.Item.Op(dsp56k.Branch(dsp56k.Bcc _, _)) -> true | _ -> false)
        Expect.isTrue hasTcc "emits tlt/tgt conditional transfers"
        Expect.isFalse hasBcc "zero conditional branches emitted for min/max/clamp"
      needNative ()
      conformance Step2Intrinsics.minMaxKernel 71
    }

    test "System.Math.Min/Max/Clamp/Abs at runtime gives a clear error pointing to min, max, abs and Frac.Min/Max/Clamp" {
      match dsp56k.kernel.Compile.compile Kernel.mds Step2Intrinsics.runtimeMathKernel with
      | Ok _ -> failtest "runtime System.Math.Min should be refused"
      | Error e ->
        Expect.stringContains e "System.Math.Min is only supported on compile-time constants" "explains compile-time constant restriction"
        Expect.stringContains e "Frac.Clamp" "points to Frac.Min / Frac.Max / Frac.Clamp"
    }

    test "Acc.abs, Frac.abs and FSharp.Core abs: single-word hardware abs instruction, equals the .NET kernel" {
      Expect.equal (Frac.abs (Frac.ofRaw 0x800000)).Raw 0x7fffff "-1.0f<f> saturates to +0x7fffff on .NET"
      Expect.equal (Frac.abs (Frac.ofRaw -0x123456)).Raw 0x123456 "negative Frac magnitude"
      Expect.equal (Acc.abs (Acc.ofRaw -0x123456789abcL)).Raw 0x123456789abcL "negative Acc magnitude"
      match Optimize.compile Optimize.Peephole Kernel.mds Step2Intrinsics.absKernel with
      | Error e -> failtest e
      | Ok c ->
        let absOps = c.items |> Array.filter (function dsp56k.Item.Op(dsp56k.Par(Some(dsp56k.Unary(dsp56k.AluUnary.Abs, _)), _)) -> true | _ -> false)
        let hasBcc = c.items |> Array.exists (function dsp56k.Item.Op(dsp56k.Branch(dsp56k.Bcc _, _)) -> true | _ -> false)
        Expect.isGreaterThanOrEqual absOps.Length 4 "emits hardware abs instructions"
        Expect.isFalse hasBcc "zero conditional branches emitted for abs"
      needNative ()
      conformance Step2Intrinsics.absKernel 72
    }

    test "Long.pack, Long.hi and Long.lo: direct sub-register and word moves without 24-bit shifts, equals the .NET kernel" {
      let packed = Long.pack (Frac.ofRaw 0x800000) (Frac.ofRaw 0x800001)
      Expect.equal (Long.hi packed).Raw -0x800000 "Long.hi preserves signed upper word"
      Expect.equal (Long.lo packed).Raw -0x7fffff "Long.lo preserves bit-23-set lower word"
      Expect.equal packed.Raw (Bits.sext48 ((0x800000L <<< 24) ||| 0x800001L)) "48-bit raw representation"
      match Optimize.compile Optimize.Peephole Kernel.mds Step2Intrinsics.longPackKernel with
      | Error e -> failtest e
      | Ok c ->
        let hasShift24 =
          c.items |> Array.exists (function
            | dsp56k.Item.Op(dsp56k.Rep(dsp56k.Count { expr = dsp56k.Num 24L }))
            | dsp56k.Item.Op(dsp56k.ShiftImm(_, 24, _, _)) -> true
            | _ -> false)
        Expect.isFalse hasShift24 "zero 24-bit shifts emitted for Long.pack / Long.hi / Long.lo"
      needNative ()
      conformance Step2Intrinsics.longPackKernel 73
    }

    test "select pass: branch diamonds over pure register/temporary updates lower to branchless Tcc/IfCc, idempotent and equals the .NET kernel" {
      match Optimize.compile Optimize.Peephole Kernel.mds Step3Optimiser.selectKernel with
      | Error e -> failtest e
      | Ok c ->
        let lines = c.items |> Array.map dsp56k.Print.item
        let at (label: string) = Array.findIndex ((=) label) lines
        let render = lines[at "RENDER:" + 1 ..]
        Expect.isTrue (c.items |> Array.exists (function dsp56k.Item.Op(dsp56k.Tcc _) -> true | _ -> false)) "emits Tcc conditional transfers"
        Expect.isTrue (c.items |> Array.exists (function dsp56k.Item.Op(dsp56k.IfCc _) -> true | _ -> false)) "emits IfCc conditional ALU op"
        Expect.isFalse (render |> Array.exists (fun l -> l.Contains "K_ELSE_")) "zero K_ELSE_ branch diamonds left in RENDER"
        let zip = Array.zip c.items c.origins
        let once = Optimize.select 0 c.xWorkset 0 c.yWorkset zip
        let twice = Optimize.select 0 c.xWorkset 0 c.yWorkset once
        Expect.equal (Array.map fst twice) (Array.map fst once) "select pass is idempotent"
      needNative ()
      conformance Step3Optimiser.selectKernel 79
    }

    test "tableWalk pass: sequential Table.row and Table.read chain on p:(r0)+ and fold constant offsets, idempotent and equals the .NET kernel" {
      match Optimize.compile Optimize.Peephole Kernel.mds Step3Optimiser.tableWalkKernel with
      | Error e -> failtest e
      | Ok c ->
        let lines = c.items |> Array.map dsp56k.Print.item
        let at (label: string) = Array.findIndex ((=) label) lines
        let render = lines[at "RENDER:" + 1 ..]
        Expect.exists render (fun l -> l.Contains "p:(r0)+") "chains sequential table reads with p:(r0)+"
        Expect.exists render (fun l -> l.Contains "lua" && l.Contains "(r0+$4),r0") "steps r0 by +4 across non-unit offset"
        Expect.exists render (fun l -> l.Contains "+$5,a") "folds standalone constant index offset +5 into table base address"
        let r0Loads = render |> Array.filter (fun l -> l.Contains ",r0" && not (l.TrimStart().StartsWith "lua")) |> Array.length
        Expect.equal r0Loads 3 "one r0 setup for the 4-column row, one for the 3-tap read chain, and one for the standalone +5 read"
        let zip = Array.zip c.items c.origins
        let once = Optimize.tableWalk zip
        let twice = Optimize.tableWalk once
        Expect.equal (Array.map fst twice) (Array.map fst once) "tableWalk pass is idempotent"
      needNative ()
      conformance Step3Optimiser.tableWalkKernel 80
    }

    test "narrow pass: 24-bit Acc.ofFrac webs narrow to 1-word slots while 56-bit product webs keep a0/a2, idempotent and equals the .NET kernel" {
      let countStores (c: Compile.Compiled) =
        c.items |> Array.sumBy (function
          | dsp56k.Item.Op(dsp56k.Par(_, moves)) ->
            moves |> Array.filter (function dsp56k.Store(_, dsp56k.Space.X, _) -> true | _ -> false) |> Array.length
          | _ -> 0)
      match Optimize.compile (Optimize.Chosen { Optimize.Passes.all with narrow = false }) Kernel.mds Step3Optimiser.narrowKernel,
            Optimize.compile Optimize.Peephole Kernel.mds Step3Optimiser.narrowKernel with
      | Ok withoutNarrow, Ok withNarrow ->
        Expect.isLessThan (countStores withNarrow) (countStores withoutNarrow) "narrow reduces X memory stores needed by 24-bit Acc.ofFrac temporaries"
        let zip = Array.zip withNarrow.items withNarrow.origins
        let once = Optimize.narrow 0 withNarrow.xWorkset zip
        let twice = Optimize.narrow 0 withNarrow.xWorkset once
        Expect.equal (Array.map fst twice) (Array.map fst once) "narrow pass is idempotent"
      | Error e, _ | _, Error e -> failtest e
      needNative ()
      conformance Step3Optimiser.narrowKernel 81
    }

    test "walkWith high displacement and dead-cursor live-range shrinking: chained lua setup above +63, pre-loop cursors freed before DO loop, idempotent and equals the .NET kernel" {
      let highTarget = { Kernel.mds with name = "Probe high Y"; yLimit = 192 }
      match Optimize.compile Optimize.Peephole highTarget Step3Optimiser.walkHighKernel with
      | Error e -> failtest e
      | Ok c ->
        let lines = c.items |> Array.map dsp56k.Print.item
        let at (label: string) = Array.findIndex ((=) label) lines
        let render = lines[at "RENDER:" + 1 ..]
        Expect.exists render (fun l -> l.Contains "lua" && l.Contains "(r6+$3f),r") "chains lua from (r6+$3f) for state words at offset > 63"
        Expect.exists render (fun l -> l.Contains "y:(r4)+") "walks high-displacement Y state words via y:(r4)+"
        let zip = Array.zip c.items c.origins
        let once = Optimize.walkWith c.layout.xWorkset c.layout.yWorkset zip
        let twice = Optimize.walkWith c.layout.xWorkset c.layout.yWorkset once
        Expect.equal (Array.map fst twice) (Array.map fst once) "walkWith pass is idempotent"
      needNative ()
      let rng = Random 84
      let steps =
        [ yield Words(randomWords rng)
          yield Init
          for _ in 1 .. 60 do
            yield Words(randomWords rng)
            yield Render 1 ]
      match gradeWith (imageHarness highTarget memoryMap) Step3Optimiser.walkHighKernel None { name = "random words"; steps = steps } with
      | Error e -> failtest e
      | Ok r -> Expect.isEmpty r.correctness (text r)
      conformance Step3Optimiser.deadCursorKernel 82
    }

    test "two-level loop unswitching: two invariant conditions hoist to 4 specialized DO loops, unswitchMax sheds 2nd-level splits first, equals the .NET kernel" {
      let countDo (c: Compile.Compiled) =
        c.items |> Array.filter (function dsp56k.Item.Op(dsp56k.Loop _) -> true | _ -> false) |> Array.length
      match Compile.generateUnswitching None Kernel.mds Step3Optimiser.unswitch2Kernel,
            Compile.generateUnswitching (Some 5000) Kernel.mds Step3Optimiser.unswitch2Kernel with
      | Ok(full, costs), Ok(oneLevel, _) ->
        Expect.isTrue (costs |> List.exists (fun c -> c >= 10000)) "records 2nd-level unswitch costs in the 10000+ tier"
        Expect.isTrue (costs |> List.exists (fun c -> c < 10000)) "records 1st-level unswitch cost below 10000"
        Expect.equal (countDo full) 4 "two invariant conditions produce 2^2 = 4 specialized DO loops"
        Expect.equal (countDo oneLevel) 2 "unswitchMax between tiers sheds 2nd-level splits first while keeping 1st-level unswitching"
      | Error e, _ | _, Error e -> failtest e
      needNative ()
      conformance Step3Optimiser.unswitch2Kernel 83
    }

    test "walkWith conditional SESE and multi-cluster DO-loop walking: walks both unconditional and branch-local state clusters, idempotent and equals the .NET kernel" {
      match Optimize.compile Optimize.Peephole Kernel.mds Step3Optimiser.walkPhaseKernel with
      | Error e -> failtest e
      | Ok c ->
        let lines = c.items |> Array.map dsp56k.Print.item
        let at (label: string) = Array.findIndex ((=) label) lines
        let render = lines[at "RENDER:" + 1 ..]
        Expect.exists render (fun l -> l.Contains "y:(r4)+") "walks unconditional state cluster via y:(r4)+"
        Expect.exists render (fun l -> l.Contains "y:(r5)+") "walks branch-local SESE state cluster via y:(r5)+"
        let zip = Array.zip c.items c.origins
        let once = Optimize.walkWith c.layout.xWorkset c.layout.yWorkset zip
        let twice = Optimize.walkWith c.layout.xWorkset c.layout.yWorkset once
        Expect.equal (Array.map fst twice) (Array.map fst once) "walkWith pass is idempotent"
      needNative ()
      conformance Step3Optimiser.walkPhaseKernel 85
    }
  ]

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

  let readsIndex (_: P) (_: S) (out: Output) =
    for i in 0 .. 31 do
      out.Write(Frac.ofRaw i)

  let scalesIndex (_: P) (_: S) (out: Output) =
    for i in 0 .. 31 do
      out.Write(Frac.ofRaw (i * 2))

  let arithmeticKernel = Kernel.define "probe-arithmetic" <@ init @> <@ trig @> <@ arithmetic @>
  let controlKernel = Kernel.define "probe-control" <@ init @> <@ trig @> <@ control @>
  let divideKernel = Kernel.define "probe-divide" <@ init @> <@ trig @> <@ divide @>
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

let conformanceWith (machine: Mds.Machine) (k: Kernel.Kernel) seed =
  let rng = Random(seed: int)
  let words () = Array.init machine.parameters.Length (fun _ -> if rng.Next 3 = 0 then [| 0; 0x7fffff; 0x800000; 0x400000 |][rng.Next 4] else rng.Next 0x1000000)
  let steps =
    [ yield Words(words ())
      yield Init
      for _ in 1 .. 100 do
        yield Words(words ())
        yield Render 1 ]
  match grade k machine None { name = "random words"; steps = steps } with
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
  match grade k probeMachine None { name = "random words"; steps = steps } with
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

    test "pipelines (|>, <|): compiled program equals the .NET kernel" {
      needNative ()
      conformance Probes.pipedKernel 9
    }

    test "DIV: compiled program equals the .NET kernel" {
      needNative ()
      conformance Probes.divideKernel 4
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
            Expect.isEmpty r.correctness (text r)
            Expect.equal r.fidelity (Some None) (text r)
            // the optimizer's ground so far: regressions past it fail
            Expect.isLessThan r.ours.renderMean (3.0 * r.reference.Value.renderMean) "within 3x the hand-written RENDER"
        } ]

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

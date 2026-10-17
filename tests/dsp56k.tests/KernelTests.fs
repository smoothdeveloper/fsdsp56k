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

  let readsIndex (_: P) (_: S) (out: Output) =
    for i in 0 .. 31 do
      out.Write(Frac.ofRaw i)

  let arithmeticKernel = Kernel.define "probe-arithmetic" <@ init @> <@ trig @> <@ arithmetic @>
  let controlKernel = Kernel.define "probe-control" <@ init @> <@ trig @> <@ control @>
  let indexKernel = Kernel.define "probe-index" <@ init @> <@ trig @> <@ readsIndex @>

let probeMachine =
  { Mds.machine "org.fsdsp56k.probe" "PROBE" "GND" "PR" with
      parameters = [| for n in [| "A"; "B"; "C"; "D" |] -> Mds.param n Mds.UnipolarQ23 |] }

/// Words that exercise the limits half the time.
let randomWords (rng: Random) =
  let edges = [| 0; 1; 0x7fffff; 0x800000; 0xffffff; 0x400000; 0xc00000 |]
  Array.init 4 (fun _ -> if rng.Next 2 = 0 then edges[rng.Next edges.Length] else rng.Next 0x1000000)

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

    test "the sine kernel reproduces jmamma/MDS sine.mds word for word" {
      needNative ()
      needFixture "MDS"
      match samples.kernels.Grading.sine (fixture "") with
      | Error e -> failtest e
      | Ok r ->
        Expect.isEmpty r.correctness (text r)
        Expect.equal r.fidelity (Some None) (text r)
        // not optimised yet: the grade records the gap rather than requiring parity
        Expect.isLessThan r.ours.renderMean (5.0 * r.reference.Value.renderMean) "within 5x the hand-written RENDER"
    }

    test "a kernel reading a DO loop's index is refused" {
      match dsp56k.kernel.Compile.compile Kernel.mds Probes.indexKernel with
      | Ok _ -> failtest "compiled"
      | Error e -> Expect.stringContains e "loop index" "the message names the problem"
    }
  ]

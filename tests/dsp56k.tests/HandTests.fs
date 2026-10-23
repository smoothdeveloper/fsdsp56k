/// Helpers ported to hand assembly one at a time: a [<HandWritten>] routine written at each call or
/// called as a [<Subroutine>], graded against the helper's F# word for word; the routines the
/// compiler refuses; the optimizer leaving them as written; the F# compiled instead on request;
/// and the workshop sample's three stages through `fsdsp56k grade`.
module dsp56k.tests.HandTests

open System
open Expecto
open dsp56k
open dsp56k.kernel
open dsp56k.kernel.Grade
open dsp56k.tests.Fixtures

/// The routines, in the [<Subroutine>] convention (Frac arguments in x0, y0, the result in x0).
module Routines =
  open dsp56k.dsl.Asm

  /// scale x g = Acc.sat ((x * g) <<< 1)
  let scale = [|
    mpy x0 y0 a
    asl a
    move a x0
    rts |]

  /// scale without its shift: wrong on every word but zero.
  let scaleWrong = [|
    mpy x0 y0 a
    move a x0
    rts |]

  /// twice x = Acc.sat (Acc.ofFrac x <<< 2), with a DO loop and its label: written twice in a
  /// kernel, the label is renamed at each call.
  let twice = [|
    move x0 a
    doTo (imm 2) "TWICE_END"
    asl a
    label "TWICE_END"
    move a x0
    rts |]

  let writesR6 = [| move (imm 0) r6; move x0 x0; rts |]
  let writesOmr = [| move (imm 0) omr; rts |]
  let returnsEarly = [| move x0 a; rts; move a x0; rts |]
  let notRoutine = 42

[<ReflectedDefinition>]
module Probes =
  type P =
    { a : Frac
      b : Frac
      c : Frac
      d : Frac }

  type S =
    { mutable x : Frac }

  let init (_: P) (s: S) = s.x <- Frac.zero
  let trig (p: P) (s: S) = s.x <- p.d

  [<HandWritten "dsp56k.tests.HandTests.Routines.scale">]
  let scale (x: Frac) (g: Frac) = Acc.sat ((x * g) <<< 1)

  [<HandWritten "dsp56k.tests.HandTests.Routines.scale"; Subroutine>]
  let scaleSub (x: Frac) (g: Frac) = Acc.sat ((x * g) <<< 1)

  [<HandWritten "dsp56k.tests.HandTests.Routines.scaleWrong">]
  let scaleWrong (x: Frac) (g: Frac) = Acc.sat ((x * g) <<< 1)

  [<HandWritten "twiceHand">]
  let twice (x: Frac) = Acc.sat (Acc.ofFrac x <<< 2)

  /// A bare name is looked up in the helper's own module.
  let twiceHand = Routines.twice

  [<HandWritten "dsp56k.tests.HandTests.Routines.writesR6">]
  let badR6 (x: Frac) = x

  [<HandWritten "dsp56k.tests.HandTests.Routines.writesOmr">]
  let badOmr (x: Frac) = x

  [<HandWritten "dsp56k.tests.HandTests.Routines.returnsEarly">]
  let badRts (x: Frac) = x

  [<HandWritten "dsp56k.tests.HandTests.Routines.missing">]
  let badMissing (x: Frac) = x

  [<HandWritten "dsp56k.tests.HandTests.Routines.notRoutine">]
  let badType (x: Frac) = x

  let step (s: S) (p: P) =
    s.x <- Acc.hi (Acc.ofFrac s.x + (Acc.ofFrac p.c >>> 3))

  let renderInline (p: P) (s: S) (out: Output) =
    for _ in 0 .. 31 do
      step s p
      out.Write(scale (twice s.x) (twice p.b))

  let renderSub (p: P) (s: S) (out: Output) =
    for _ in 0 .. 31 do
      step s p
      out.Write(scaleSub s.x p.b)

  let renderWrong (p: P) (s: S) (out: Output) =
    for _ in 0 .. 31 do
      step s p
      out.Write(scaleWrong s.x p.b)

  let renderR6 (p: P) (s: S) (out: Output) = for _ in 0 .. 31 do out.Write(badR6 p.a)
  let renderOmr (p: P) (s: S) (out: Output) = for _ in 0 .. 31 do out.Write(badOmr p.a)
  let renderRts (p: P) (s: S) (out: Output) = for _ in 0 .. 31 do out.Write(badRts p.a)
  let renderMissing (p: P) (s: S) (out: Output) = for _ in 0 .. 31 do out.Write(badMissing p.a)
  let renderType (p: P) (s: S) (out: Output) = for _ in 0 .. 31 do out.Write(badType p.a)

  let inlineKernel  = Kernel.define "probe-hand-inline" <@ init @> <@ trig @> <@ renderInline @>
  let subKernel     = Kernel.define "probe-hand-sub" <@ init @> <@ trig @> <@ renderSub @>
  let wrongKernel   = Kernel.define "probe-hand-wrong" <@ init @> <@ trig @> <@ renderWrong @>
  let r6Kernel      = Kernel.define "probe-hand-r6" <@ init @> <@ trig @> <@ renderR6 @>
  let omrKernel     = Kernel.define "probe-hand-omr" <@ init @> <@ trig @> <@ renderOmr @>
  let rtsKernel     = Kernel.define "probe-hand-rts" <@ init @> <@ trig @> <@ renderRts @>
  let missingKernel = Kernel.define "probe-hand-missing" <@ init @> <@ trig @> <@ renderMissing @>
  let typeKernel    = Kernel.define "probe-hand-type" <@ init @> <@ trig @> <@ renderType @>

let private scenario seed =
  let rng = Random(seed: int)
  { name = "random words, trigs"
    steps =
      [ yield Words(KernelTests.randomWords rng)
        yield Init
        for i in 1 .. 200 do
          yield Words(KernelTests.randomWords rng)
          if i % 50 = 0 then yield Trig
          yield Render 1 ] }

let private graded (k: Kernel.Kernel) =
  match grade k KernelTests.probeMachine None (scenario 7) with
  | Error e -> failtest e
  | Ok r -> r

let private compiled (k: Kernel.Kernel) =
  match Optimize.compile Optimize.Peephole Kernel.mds k with
  | Error e -> failtest $"%A{e}"
  | Ok c -> c

let private refusal (k: Kernel.Kernel) =
  match Optimize.compile Optimize.Peephole Kernel.mds k with
  | Ok _ -> failtest $"{k.name} compiled"
  | Error e -> $"%A{e}"

let private listing (c: Compile.Compiled) = c.items |> Array.map Print.item |> String.concat "\n"

/// The items from each "hand: <routine>" comment to its "hand: end".
let private regions (c: Compile.Compiled) =
  let items = c.items
  [ for i in 0 .. items.Length - 1 do
      match items[i] with
      | Comment t when t.StartsWith "hand: " && t <> "hand: end" ->
        let stop = [ i + 1 .. items.Length - 1 ] |> List.find (fun j -> items[j] = Comment "hand: end")
        yield t.Substring 6, items[i + 1 .. stop - 1]
      | _ -> () ]

let private normalized (text: string) =
  text.Split('\n') |> Array.map (fun l -> String.Join(" ", l.Split(' ', StringSplitOptions.RemoveEmptyEntries)))

let workshop = repoRoot </> "samples" </> "workshop" </> "Workshop.fs"

let tests =
  testList "hand-written helpers" [
    test "a routine written at each call matches its F# word for word, its labels renamed per call and its loop ends relocated" {
      let r = graded Probes.inlineKernel
      Expect.isEmpty r.correctness (text r)
      let c = compiled Probes.inlineKernel
      let found = regions c
      Expect.equal (found |> List.map fst |> List.distinct |> List.sort)
        [ "dsp56k.tests.HandTests.Routines.scale"; "twiceHand" ] "the routines, by the names their attributes give"
      Expect.equal (found |> List.filter (fun (n, _) -> n = "twiceHand") |> List.length) 2 "twice is written at both its calls"
      let labels = c.items |> Array.choose (function Label l when l.StartsWith "TWICE_END" -> Some l | _ -> None)
      Expect.equal labels.Length 2 "a label per call"
      Expect.equal (Array.distinct labels).Length 2 "renamed apart"
      match Grade.build KernelTests.probeMachine c with
      | Error e -> failtest $"%A{e}"
      | Ok b -> Expect.equal b.package.relocations.Length 3 "the RENDER loop's end and each routine's, relocated where the package loads"
    }

    test "the optimizer leaves a routine as written" {
      let c = compiled Probes.inlineKernel
      for name, body in regions c do
        if name = "dsp56k.tests.HandTests.Routines.scale" then
          Expect.equal body (Routines.scale[.. Routines.scale.Length - 2]) "scale, its rts dropped"
    }

    test "a [<Subroutine>] routine is called with bsr, written once" {
      let r = graded Probes.subKernel
      Expect.isEmpty r.correctness (text r)
      let c = compiled Probes.subKernel
      Expect.equal (regions c |> List.length) 1 "written once"
      Expect.stringContains (listing c) "bsr" "called"
      Expect.equal (snd (List.head (regions c))) (Routines.scale[.. Routines.scale.Length - 2]) "as written, with its own rts after"
    }

    test "a wrong routine fails the grade; its F# compiled instead does not" {
      let r = graded Probes.wrongKernel
      Expect.isNonEmpty r.correctness "the grade sees it"
      let gradeOff handOff = gradeAtWith handOff Optimize.Peephole (mdsHarness KernelTests.probeMachine) Probes.wrongKernel None (scenario 7)
      match HandCheck.inKernel gradeOff r Probes.wrongKernel with
      | Error e -> failtest e
      | Ok [ one ] ->
        Expect.equal one.helper "scaleWrong" "the helper"
        Expect.isTrue one.correct "with its F# compiled instead the kernel matches"
      | Ok other -> failtestf "%A" other
    }

    test "on its own, each routine against its F# run as .NET code, edge words first" {
      let check (k: Kernel.Kernel) =
        match HandCheck.alone Kernel.mds 512 1 k with
        | [] -> failtest "no helper"
        | all -> all
      for a in check Probes.inlineKernel do
        Expect.isNone a.skipped $"{a.helper} is checked"
        Expect.equal a.hand a.arguments $"{a.helper}: every call"
        Expect.equal a.compiled (Some a.arguments) $"{a.helper}: its F# compiled alone, every call"
      match check Probes.wrongKernel with
      | [ a ] ->
        Expect.isLessThan a.hand a.arguments "the wrong routine differs"
        Expect.isGreaterThan a.hand 0 "but not on zero"
      | other -> failtestf "%A" other
    }

    test "the F# compiled instead, on request" {
      let all = compiled Probes.inlineKernel
      let keys = HandCheck.helpers Probes.inlineKernel |> List.map _.Key |> Set.ofList
      match Optimize.compileHandOff keys Optimize.Peephole Kernel.mds Probes.inlineKernel with
      | Error e -> failtest $"%A{e}"
      | Ok c ->
        Expect.isEmpty (regions c) "no routine"
        Expect.isNonEmpty (regions all) "routines otherwise"
    }

    test "routines the compiler refuses, with the reason" {
      Expect.stringContains (refusal Probes.r6Kernel) "r6" "r6 is the workset"
      Expect.stringContains (refusal Probes.omrKernel) "omr" "control registers"
      Expect.stringContains (refusal Probes.rtsKernel) "rts" "an rts inside a routine written at its call"
      Expect.stringContains (refusal Probes.missingKernel) "missing" "a name that is not there"
      Expect.stringContains (refusal Probes.typeKernel) "notRoutine" "a value that is not items"
    }

    testSequenced <| testList "the workshop sample, one helper at a time" [
      let stage (defines: string list) =
        let code, out = ToolTests.tool [| "grade"; workshop; for d in defines do yield! [ "--define"; d ] |]
        Expect.equal code 0 out
        normalized out
      let has (lines: string array) (line: string) = Expect.contains lines line (String.concat "\n" lines)
      test "all F#" {
        let o = stage []
        has o "correctness: the compiled program matches the .NET kernel, output and state"
        has o "RENDER mean 1539"
        Expect.isFalse (o |> Array.exists (fun l -> l.Contains "hand-written")) "no routine"
      }
      test "shape by hand" {
        let o = stage [ "HAND_SHAPE" ]
        has o "correctness: the compiled program matches the .NET kernel, output and state"
        has o "RENDER mean 2107"
        has o "shape workshop.hand.shape 1539.0 2107.0 -568.0 -568 matches .NET"
        has o "shape workshop.hand.shape 1024/1024 23 11 12.0"
      }
      test "a routine gone wrong fails the grade, and the table shows which" {
        let dir = ToolTests.scratch ()
        let broken = IO.Path.Combine(dir, "Workshop.fs")
        IO.File.WriteAllText(broken, IO.File.ReadAllText(workshop).Replace("asli 2 a a", "asli 1 a a"))
        let code, out = ToolTests.tool [| "grade"; broken; "--define"; "HAND_SHAPE" |]
        Expect.equal code 1 out
        let o = normalized out
        Expect.isTrue (o |> Array.exists (fun l -> l.StartsWith "correctness FAILS: output differs")) out
        Expect.contains o "shape workshop.hand.shape 1539.0 2107.0 -568.0 -568 matches .NET" "its F# instead matches"
        Expect.contains o "shape workshop.hand.shape 211/1024 23 11 12.0" "on its own, 211 calls of 1024 right"
        Expect.contains o "first difference ($7fffff, $000001): expected $000004, got $000001" "the first arguments it is wrong on"
      }
      test "--define is for a file: a project sets its own symbols" {
        let code, out = ToolTests.tool [| "grade"; "Some.fsproj"; "--define"; "HAND_SHAPE" |]
        Expect.equal code 1 out
        Expect.stringContains out "--define is for a .fs or .fsx kernel" out
      }
      test "shape and follow by hand" {
        let o = stage [ "HAND_SHAPE"; "HAND_FOLLOW" ]
        has o "correctness: the compiled program matches the .NET kernel, output and state"
        has o "RENDER mean 2075"
        has o "follow workshop.hand.follow 2107.0 2075.0 32.0 32 matches .NET"
        has o "shape workshop.hand.shape 2427.0 2075.0 352.0 352 matches .NET"
        has o "follow workshop.hand.follow 1024/1024 21 4 17.0"
        let listing = IO.Path.Combine(ToolTests.scratch (), "workshop.asm")
        let code, out = ToolTests.tool [| "grade"; workshop; "--define"; "HAND_SHAPE"; "--define"; "HAND_FOLLOW"; "--listing"; listing |]
        Expect.equal code 0 out
        let code, out = ToolTests.tool [| "check"; listing |]
        Expect.equal code 0 out
        Expect.stringContains out "70 instructions, 0 disagree with the oracle" "the listing, as source the oracle agrees with"
      }
    ]
  ]

/// The grade library's sweep functions on the public samples: the exhaustive scenario, players
/// compared word for word, and the grade row.
module dsp56k.tests.SweepTests

open Expecto
open dsp56k.kernel
open dsp56k.kernel.Grade
open dsp56k.tests.Fixtures
open samples.kernels

/// PLUCK takes words: a harness with no settings conversion plays it.
let words =
  { name           = "words only"
    target         = Kernel.mds
    parameterNames = [||]
    settings       = fun _ -> failwith "a words-only scenario"
    cycleLimit     = None
    load           = fun _ -> Error "not loaded" }

let pluckControls =
  [| { name = "PTCH"; low = Grading.hz 55.0;    high = Grading.hz 1760.0;  standard = Grading.hz 220.0 }
     { name = "DEC";  low = float 0x700000;     high = float 0x7ff000;     standard = float 0x7f8000 }
     { name = "LEV";  low = float 0x200000;     high = float 0x7fffff;     standard = float 0x600000 } |]

let pluckSweep = Sweep.scenario 32 pluckControls (fun v -> Words(Array.map int v)) Grading.pluckPattern

/// The kernel's .NET host behind the portable protocol's instance: the in-process player's plumbing.
let hosted (k: Kernel.Kernel) () : dsp56k.Portable.Instance =
  let host = Host.Instance(Kernel.mds, k)
  { parameters = host.SetParameters
    memory     = fun _ _ _ -> failwith "no resources"
    init       = host.Init
    trig       = host.Trig
    render     = host.Render
    state      = host.StateWords
    buffers    = dsp56k.Portable.buffers }

/// The portable .NET runner, an external process like the Rust wasm one.
let portable (h: Harness) (k: Kernel.Kernel) =
  Sweep.runner "portable-dotnet" "dotnet" [ portableNet ] (repoRoot </> "out" </> "tests" </> "sweep") k.name Kernel.mds h.settings

let tests =
  testList "sweep grading" [
    test "a sample's scenario is its kernel's sweep" {
      Expect.equal pluckSweep Grading.pluck.scenario "PLUCK"
      Expect.equal (Sweep.blocks pluckSweep) 19294 "PLUCK's blocks"
      let sine = Sweep.scenario 32 (Grading.controls [| "PTCH"; "AMP" |] [| 64.0; 100.0 |] []) Settings Grading.sinePattern
      Expect.equal sine Grading.sine.scenario "SINE"
      Expect.equal (Sweep.blocks sine) 16538 "SINE's blocks"
    }

    test "an in-process player plays the sweep's steps as .NET does" {
      let row = Sweep.grade "0123abc" "pluck" (Sweep.net words Pluck.kernel) (Sweep.direct "hosted" words.settings (hosted Pluck.kernel)) pluckSweep
      Expect.equal row.mismatches [] "no differing word"
      Expect.equal row.blocks 19294 "every block"
      Expect.equal (Sweep.row row) "pluck\thosted\tdotnet\tsweep\t19294\tmatch\t-\t0123abc" "the row"
    }

    test "PLUCK's portable adapter, run by the portable runner, matches .NET over the whole sweep" {
      needPortable ()
      let row = Sweep.grade "-" "pluck" (Sweep.net words Pluck.kernel) (portable words Pluck.kernel) pluckSweep
      Expect.equal row.mismatches [] "no differing word"
      Expect.equal row.blocks 19294 "every block"
    }

    test "compare names the first differing word of the output and of the state" {
      let run = (Sweep.net words Pluck.kernel).play pluckSweep
      let outputs = run.outputs |> Array.map Array.copy
      outputs[1234][5] <- outputs[1234][5] ^^^ 1
      outputs[4000][0] <- outputs[4000][0] ^^^ 1
      let states = run.states |> Array.map Array.copy
      states[77][2] <- states[77][2] ^^^ 0x800000
      match Sweep.compare run { run with outputs = outputs; states = states } with
      | [ output; state ] ->
        Expect.equal (output.what, output.block, output.index, output.differing) ("output", 1234, 5, 2) "output"
        Expect.equal output.actual (output.expected ^^^ 1) "the flipped bit"
        Expect.equal (state.what, state.block, state.index, state.differing) ("state", 77, 2, 1) "state"
      | other -> failtestf "two mismatches expected, got %A" other
      Expect.equal (Sweep.compare run { run with outputs = run.outputs[1..] }).Head.what "output" "a missing block differs"
    }

    test "a row with a mismatch says so" {
      let m = { what = "output"; block = 3; index = 4; expected = 0x10; actual = 0x11; differing = 7 }
      let row = { Sweep.kernel = "sine"; Sweep.runtime = "fable-rust-wasm"; Sweep.reference = "dotnet"
                  Sweep.blocks = 16538; Sweep.mismatches = [ m ]; Sweep.commit = "0123abc" }
      Expect.equal (Sweep.row row)
        "sine\tfable-rust-wasm\tdotnet\tsweep\t16538\tmismatch\toutput block 3 word 4: expected 000010, got 000011 (7 differing)\t0123abc"
        "the row"
      Expect.equal (Sweep.header.Split '\t').Length ((Sweep.row row).Split '\t').Length "a column each"
    }

    test "SINE's portable adapter, run by the portable runner, matches .NET word for word over the whole sweep" {
      needFixture "MDS"
      needNative ()
      match Grading.packageBytes (fixture Grading.sine.package) |> Result.bind (fun b -> machinedrum.run.Import.package b |> Result.mapError (sprintf "%A")) with
      | Error e -> failtest e
      | Ok imported ->
        let h = mdsHarness imported.machine
        needPortable ()
        let row = Sweep.grade "-" "sine" (Sweep.net h Sine.kernel) (portable h Sine.kernel) Grading.sine.scenario
        Expect.equal row.mismatches [] "no differing word"
        Expect.equal row.blocks 16538 "every block"
    }
  ]

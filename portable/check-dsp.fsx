/// Integration check: a normal DSP grade, then the optional direct-target adapter on its report.
#r "../src/dsp56k.kernel.grade/bin/Debug/net10.0/Dsp56k.dll"
#r "../src/dsp56k.kernel.grade/bin/Debug/net10.0/Dsp56k.Dsl.dll"
#r "../src/dsp56k.kernel.grade/bin/Debug/net10.0/Dsp56k.Native.dll"
#r "../src/dsp56k.kernel.grade/bin/Debug/net10.0/Machinedrum.dll"
#r "../src/dsp56k.kernel.grade/bin/Debug/net10.0/Machinedrum.Run.dll"
#r "../src/dsp56k.kernel.grade/bin/Debug/net10.0/Dsp56k.Kernel.Intrinsics.dll"
#r "../src/dsp56k.kernel.grade/bin/Debug/net10.0/Dsp56k.Kernel.dll"
#r "../src/dsp56k.kernel.grade/bin/Debug/net10.0/Dsp56k.Kernel.Grade.dll"
#load "../tests/dsp56k.tests/Kernels/Sine.fs"

open System.IO
open dsp56k.kernel
open dsp56k.kernel.Grade

if not (dsp56k.Native.load()) then failwith "Build the native DSP emulator first"
let map = { firmwareMap with resources = standIns; setup = loadResources }
let harness = firmware Kernel.mds map
let scenario =
  { name = "portable adapter integration"
    steps = [ Words [| 0x1800; 0x500000 |]; Init; Trig; Render 16
              Words [| 0x3500; 0x100000 |]; Render 8; Trig; Render 8 ] }
let report =
  match gradeWith harness samples.kernels.Sine.kernel None scenario with
  | Ok report when report.correctness.IsEmpty -> report
  | Ok _ -> failwith "DSP and .NET output/state disagree"
  | Error error -> failwith error
let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let runner: PortableGrade.Runner =
  { executable = "node"; arguments = [ Path.Combine(root, "out/portable/js/grade.mjs") ]
    warmup = 1; trials = 3 }
match PortableGrade.check (Path.Combine(root, "out/portable/dsp-check")) runner [||] report with
| Ok result ->
  assert (result.measurement.outputChecks = 32 * 32)
  assert (result.measurement.stateChecks = 32 * 3)
  assert (result.referenceOutput.IsNone)
  printfn "[1/1] Optional Fable grade replays DSP scenario with exact output/state: passed"
| Error error -> failwith error

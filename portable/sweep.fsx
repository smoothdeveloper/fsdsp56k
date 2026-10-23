/// Every portable kernel with a graded sample, swept on each target: the sample's whole scenario
/// (its pattern, each control swept, then pairwise) played by the target's runner and compared word
/// for word with the kernel on .NET, which the DSP grade holds to the compiled program and to the
/// original. Writes out/portable/<kernel>.fixture.json (the sweep, expected words included) and
/// out/portable/sweeps.tsv; exits 1 on any differing word.
/// Usage: dotnet fsi portable/sweep.fsx --targets dotnet,rust-wasm (after dotnet build tests/dsp56k.tests)
#r "../tests/dsp56k.tests/bin/Debug/net10.0/Dsp56k.dll"
#r "../tests/dsp56k.tests/bin/Debug/net10.0/Dsp56k.Native.dll"
#r "../tests/dsp56k.tests/bin/Debug/net10.0/Dsp56k.Kernel.Intrinsics.dll"
#r "../tests/dsp56k.tests/bin/Debug/net10.0/Dsp56k.Kernel.dll"
#r "../tests/dsp56k.tests/bin/Debug/net10.0/Dsp56k.Kernel.Grade.dll"
#r "../tests/dsp56k.tests/bin/Debug/net10.0/Machinedrum.dll"
#r "../tests/dsp56k.tests/bin/Debug/net10.0/Machinedrum.Run.dll"
#r "../tests/dsp56k.tests/bin/Debug/net10.0/Dsp56k.Tests.dll"
#load "Targets.fsx"
open System
open System.IO
open System.Diagnostics
open System.Text.Json
open dsp56k.kernel
open samples.kernels

let root          = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let configuration = Targets.configure root
let targets       = Targets.selected fsi.CommandLineArgs[1..] [| "dotnet" |]
let commit        = Sweep.commit root

/// The kernels the portable runner has adapters for.
let adapters =
  let info = ProcessStartInfo("dotnet", UseShellExecute = false, RedirectStandardOutput = true)
  info.ArgumentList.Add configuration.dotnet
  info.ArgumentList.Add "--kernels"
  use child = Process.Start info
  let names = child.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
  child.WaitForExit()
  if child.ExitCode <> 0 then failwith "Portable.Net --kernels failed: build portable/portable.net.fsproj"
  names |> Array.map _.Trim()

let rows = ResizeArray<Grade.Mismatch list * string>()
for sample in Grading.samples |> List.filter (fun s -> Array.contains s.kernel.name adapters) do
  let name    = sample.kernel.name
  let package = Path.Combine(root, "fixtures", sample.package)
  let machine =
    Grading.packageBytes package
    |> Result.bind (fun bytes -> machinedrum.run.Import.package bytes |> Result.mapError (sprintf "%A"))
    |> function Ok imported -> imported.machine | Error e -> failwith $"{name}: {e}"
  let h        = Grade.mdsHarness machine
  let timer    = Stopwatch.StartNew()
  let expected = (Sweep.net h sample.kernel).play sample.scenario
  printfn "%s: %d sweep blocks on .NET in %d ms" name expected.outputs.Length timer.ElapsedMilliseconds
  let fixture = Sweep.fixture name h.target h.settings sample.scenario expected
  File.WriteAllText(Path.Combine(configuration.output, name + ".fixture.json"), JsonSerializer.Serialize fixture)
  let reference = { Sweep.name = "dotnet"; Sweep.play = fun _ -> expected }
  for target in targets do
    let executable, arguments = Targets.command configuration target
    let player = Sweep.runner target executable arguments (Path.Combine(configuration.output, "sweeps")) name h.target h.settings
    let timer  = Stopwatch.StartNew()
    let row    = Sweep.grade commit name reference player sample.scenario
    printfn "%s %s: %s in %d ms" name target (if row.mismatches.IsEmpty then "match" else "MISMATCH") timer.ElapsedMilliseconds
    rows.Add(row.mismatches, Sweep.row row)

File.WriteAllLines(Path.Combine(configuration.output, "sweeps.tsv"), Sweep.header :: [ for _, line in rows -> line ])
printfn "Report: out/portable/sweeps.tsv (toolchain %s)" commit
let failed = [ for mismatches, line in rows do if not mismatches.IsEmpty then line ]
if not failed.IsEmpty then
  eprintfn "Sweep mismatches:\n%s" (String.concat "\n" failed)
  exit 1

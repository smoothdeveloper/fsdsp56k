/// Opt-in direct-target benchmark. Existing DSP grading remains unchanged.
#load "../src/dsp56k.kernel.intrinsics/Intrinsics.fs"
#load "Host.fs"
open System
open System.IO
open System.Diagnostics
open System.Text.Json
open dsp56k.Portable

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let output = Path.Combine(root, "out", "portable")
Directory.CreateDirectory output |> ignore

let run executable arguments =
  let info = ProcessStartInfo(executable, WorkingDirectory = root, UseShellExecute = false)
  for argument in arguments do info.ArgumentList.Add argument
  use child = Process.Start info
  child.WaitForExit()
  if child.ExitCode <> 0 then failwith $"{executable} exited {child.ExitCode}"

let dotnet = Path.Combine(__SOURCE_DIRECTORY__, "bin", "Release", "net10.0", "Portable.Net.dll")
let javascript = Path.Combine(output, "js", "grade.mjs")
let args = fsi.CommandLineArgs[1..]
if args |> Array.exists (fun value -> value <> "--javascript" && value <> "--dotnet" && value <> "--rust-wasm") then
  failwith "Usage: dotnet fsi portable/grade.fsx [--dotnet] [--javascript] [--rust-wasm]"
let targets = if args.Length = 0 then [| "--dotnet"; "--javascript" |] else args

let step action words blocks = { action = action; words = words; blocks = blocks }
let scenario first second =
  [| step "parameters" first 0; step "init" [||] 0; step "trig" [||] 0
     step "render" [||] 256; step "parameters" second 0; step "render" [||] 128
     step "trig" [||] 0; step "render" [||] 128 |]
let cases =
  [| "sine", scenario [| 0x1800; 0x500000 |] [| 0x3500; 0x100000 |]
     "pluck", scenario [| 0x1800; 0x7f0000; 0x500000 |] [| 0x3500; 0x700000; 0x300000 |]
     "input-thru", scenario [| 0; 0x500000 |] [| 1; 0x700000 |] |]

run "dotnet" [ "fsi"; "portable/prepare.fsx" ]
run "dotnet" [ "build"; "portable/portable.net.fsproj"; "-c"; "Release"; "--ignore-failed-sources"; "-p:NuGetAudit=false" ]
let rows = ResizeArray<string>()
rows.Add "kernel\ttarget\truntime\tblocks\tmedian_render_ms\tworst_render_ms\taudio_ms\treal_time_factor\toutput_checks\tstate_checks\tbuffer_checks"
let mutable completed = 0
for kernel, steps in cases do
  let fixture: Fixture =
    { version = 1; kernel = kernel; frames = 32; channels = 1; sampleRate = 44100
      steps = steps; expected = [||]; buffers = [||] }
  let path = Path.Combine(output, kernel + ".fixture.json")
  File.WriteAllText(path, JsonSerializer.Serialize fixture)
  run "dotnet" [ dotnet; "--reference"; path ]
  for target in targets do
    completed <- completed + 1
    printfn "[%d/%d] %s %s: checking and measuring" completed (cases.Length * targets.Length) kernel target
    let result = Path.Combine(output, kernel + "." + target[2..] + ".json")
    let arguments = [ path; result; "2"; "5" ]
    if target = "--dotnet" then run "dotnet" (dotnet :: arguments)
    elif target = "--javascript" then run "node" (javascript :: arguments)
    else
      let wasm = Path.Combine(output, "cargo", "wasm32-unknown-unknown", "release", "dsp56k_portable.wasm")
      run "node" (javascript :: "--wasm" :: wasm :: arguments)
    let m = JsonSerializer.Deserialize<Measurement>(File.ReadAllText result)
    rows.Add(String.concat "\t"
      [ m.kernel; m.target; m.runtime; string m.blocks; string m.medianRenderMs
        string m.worstRenderMs; string m.audioMs; string m.realTimeFactor
        string m.outputChecks; string m.stateChecks; string m.bufferChecks ])
File.WriteAllLines(Path.Combine(output, "grades.tsv"), rows)
printfn "Report: out/portable/grades.tsv"

// Revisions alone do not identify an uncommitted experiment: hash sources and built artifacts too.
let provenance = ResizeArray<string>()
provenance.Add "kind\tpath\tvalue"
let hash path =
  System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes path) |> Convert.ToHexString
let git = ProcessStartInfo("git", WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true)
git.ArgumentList.Add "rev-parse"
git.ArgumentList.Add "HEAD"
let revision = Process.Start git
let commit = revision.StandardOutput.ReadToEnd().Trim()
revision.WaitForExit()
let exitCode = revision.ExitCode
revision.Dispose()
if exitCode <> 0 then failwith "Cannot identify source revision"
provenance.Add $"revision\tfsdsp56k\t{commit}"
let sourceFiles =
  Seq.append
    (Directory.EnumerateFiles(__SOURCE_DIRECTORY__) |> Seq.filter (fun path ->
      [| ".fs"; ".fsx"; ".fsproj"; ".props"; ".yaml"; ".toml"; ".lock"; ".mts" |]
      |> Array.contains (Path.GetExtension path)))
    [ Path.Combine(root, "src/dsp56k.kernel.intrinsics/Intrinsics.fs")
      Path.Combine(root, "tests/dsp56k.tests/Kernels/Sine.fs")
      Path.Combine(root, "tests/dsp56k.tests/Kernels/Pluck.fs")
      Path.Combine(root, "tests/dsp56k.tests/Kernels/InputThru.fs") ]
for path in sourceFiles |> Seq.sort do
  provenance.Add $"source_sha256\t{Path.GetRelativePath(root, path)}\t{hash path}"
let artifacts =
  seq {
    yield dotnet
    yield! Directory.EnumerateFiles(Path.Combine(output, "js"), "*.mjs")
    yield Path.Combine(output, "cargo/wasm32-unknown-unknown/release/dsp56k_portable.wasm")
  }
for path in artifacts |> Seq.sort do
  if File.Exists path then
    provenance.Add $"artifact_sha256\t{Path.GetRelativePath(root, path)}\t{hash path}"
let compiler = Path.Combine(__SOURCE_DIRECTORY__, "node_modules/vite-plugin-fable/bin/Fable.Transforms.dll")
if File.Exists compiler then
  provenance.Add $"fable_product_version\tvite-plugin-fable\t{FileVersionInfo.GetVersionInfo(compiler).ProductVersion}"
  provenance.Add $"fable_compiler_sha256\tvite-plugin-fable\t{hash compiler}"
provenance.Add "fable_rust_version\tbuild-rust.fsx\t5.18.0"
File.WriteAllLines(Path.Combine(output, "provenance.tsv"), provenance)

/// Opt-in direct-target benchmark on each portable kernel's full sweep (portable/sweep.fsx grades it
/// word for word first). Existing DSP grading remains unchanged.
#load "../src/dsp56k.kernel.intrinsics/Intrinsics.fs"
#load "Host.fs"
#load "Targets.fsx"
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

let args = fsi.CommandLineArgs[1..]
let legacy = args |> Array.filter (fun value -> Targets.names |> Array.exists (fun name -> value = "--" + name))
for index, value in Array.indexed args do
  if value <> "--targets" && not (Array.contains value legacy)
     && (index = 0 || args[index - 1] <> "--targets") then
    invalidArg "arguments" $"Unknown option: {value}"
let targets =
  Targets.selected args
    (if legacy.Length = 0 then [| "dotnet"; "javascript" |] else legacy |> Array.map (fun value -> value[2..]))
let configuration = Targets.configure root
let dotnet        = configuration.dotnet

run "dotnet" [ "fsi"; "portable/prepare.fsx" ]
Targets.timedBuild configuration "dotnet" "dotnet"
  [ "build"; "portable/portable.net.fsproj"; "-c"; "Release"; "--ignore-failed-sources"; "-p:NuGetAudit=false" ]
if targets |> Array.exists (fun target -> target = "javascript" || target = "rust-wasm" || target.StartsWith "dotnet-wasm") then
  Targets.timedBuild configuration "javascript" "pnpm" [ "--dir"; "portable"; "build" ]
if targets |> Array.exists (fun target -> target = "rust-native" || target = "rust-wasm") then
  let arguments =
    [ "fsi"; "portable/build-rust.fsx" ]
    @ (if Array.contains "rust-wasm" targets then [] else [ "--emit-only" ])
  Targets.timedBuild configuration "fable-rust" "dotnet" arguments
Targets.build configuration targets
Targets.timedBuild configuration "tests" "dotnet" [ "build"; "tests/dsp56k.tests"; "-v"; "q"; "-nologo" ]
run "dotnet" [ "fsi"; "portable/sweep.fsx"; "--targets"; String.concat "," targets ]
let sweeps =
  File.ReadAllLines(Path.Combine(output, "sweeps.tsv"))[1..] |> Array.map _.Split('\t')
let cases  = sweeps |> Array.map (fun row -> row[0]) |> Array.distinct
let toolchain = sweeps |> Array.map (fun row -> row[7]) |> Array.distinct |> Array.exactlyOne
let rows = ResizeArray<string>()
rows.Add "kernel\ttarget\truntime\tblocks\tmedian_render_ms\tworst_render_ms\taudio_ms\treal_time_factor\toutput_checks\tstate_checks\tbuffer_checks\tscenario\ttoolchain_commit"
let mutable completed = 0
for kernel in cases do
  let path = Path.Combine(output, kernel + ".fixture.json")
  // the buffers the runners check besides output and state words: from the portable .NET build, which
  // sweep.fsx has just graded word for word against the kernel
  run "dotnet" [ dotnet; "--reference"; path ]
  for target in targets do
    completed <- completed + 1
    printfn "[%d/%d] %s %s: checking and measuring" completed (cases.Length * targets.Length) kernel target
    let result = Path.Combine(output, kernel + "." + target + ".json")
    Targets.grade configuration target path result 2 5
    let m = JsonSerializer.Deserialize<Measurement>(File.ReadAllText result)
    rows.Add(String.concat "\t"
      [ m.kernel; m.target; m.runtime; string m.blocks; string m.medianRenderMs
        string m.worstRenderMs; string m.audioMs; string m.realTimeFactor
        string m.outputChecks; string m.stateChecks; string m.bufferChecks; "sweep"; toolchain ])
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
    yield! Targets.artifacts configuration targets
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

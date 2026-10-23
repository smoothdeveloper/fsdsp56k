// Optional direct runtimes share one fixture protocol and ignored local measurements.

open System
open System.IO
open System.Diagnostics
open System.Runtime.InteropServices

type Configuration =
  { root       : string
    project    : string
    output     : string
    manifest   : string
    javascript : string
    dotnet     : string }

/// The paths grade.fsx and sweep.fsx share, under the checkout `root`.
let configure root =
  let output = Path.Combine(root, "out", "portable")
  { root       = root
    project    = Path.Combine(__SOURCE_DIRECTORY__, "portable.net.fsproj")
    output     = output
    manifest   = Path.Combine(__SOURCE_DIRECTORY__, "Cargo.toml")
    javascript = Path.Combine(output, "js")
    dotnet     = Path.Combine(__SOURCE_DIRECTORY__, "bin", "Release", "net10.0", "Portable.Net.dll") }

let private runId = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ")
let private measurements configuration =
  Path.Combine(configuration.output, "measurements", runId)

let names =
  [| "dotnet"; "javascript"; "rust-wasm"; "rust-native"
     "dotnet-aot"; "dotnet-wasm"; "dotnet-wasm-aot" |]

let selected (arguments: string array) defaults =
  match arguments |> Array.tryFindIndex ((=) "--targets") with
  | None -> defaults
  | Some index ->
    if index + 1 >= arguments.Length then invalidArg "--targets" "Expected comma-separated targets or all"
    let values = arguments[index + 1].Split ',' |> Array.distinct
    if values = [| "all" |] then names
    else
      for value in values do
        if not (Array.contains value names) then invalidArg "--targets" $"Unknown target: {value}"
      values

let private run root executable arguments =
  let info = ProcessStartInfo(executable, WorkingDirectory = root, UseShellExecute = false)
  for argument in arguments do info.ArgumentList.Add argument
  use child = Process.Start info
  child.WaitForExit()
  if child.ExitCode <> 0 then failwith $"{executable} exited {child.ExitCode}"

let private nativeName =
  if OperatingSystem.IsWindows() then "dsp56k_portable.dll"
  elif OperatingSystem.IsMacOS() then "libdsp56k_portable.dylib"
  else "libdsp56k_portable.so"

let private localDotnet =
  Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../out/toolchains/dotnet10",
    if OperatingSystem.IsWindows() then "dotnet.exe" else "dotnet"))

let private wasmDotnet () =
  let configured = Environment.GetEnvironmentVariable "FSDSP56K_WASM_DOTNET"
  if not (String.IsNullOrWhiteSpace configured) then configured
  elif File.Exists localDotnet then localDotnet
  else "dotnet"

let private identify executable arguments =
  try
    let info = ProcessStartInfo(executable, UseShellExecute = false,
      RedirectStandardOutput = true, RedirectStandardError = true)
    for argument in arguments do info.ArgumentList.Add argument
    use child = Process.Start info
    let text = child.StandardOutput.ReadToEnd().Trim()
    child.StandardError.ReadToEnd() |> ignore
    child.WaitForExit()
    if child.ExitCode = 0 then text.Replace('\n', ' ') else "unavailable"
  with _ -> "unavailable"

let private cpu () =
  if OperatingSystem.IsMacOS() then identify "sysctl" [ "-n"; "machdep.cpu.brand_string" ]
  elif File.Exists "/proc/cpuinfo" then
    File.ReadLines "/proc/cpuinfo"
    |> Seq.tryFind (fun line -> line.StartsWith "model name")
    |> Option.defaultValue "unavailable"
  else Environment.GetEnvironmentVariable "PROCESSOR_IDENTIFIER" |> string

/// Append only locally: build cache, hardware and workload change these numbers.
let context configuration =
  let directory = measurements configuration
  Directory.CreateDirectory directory |> ignore
  File.WriteAllLines(Path.Combine(directory, "context.tsv"),
    [| "key\tvalue"
       $"utc\t{DateTimeOffset.UtcNow:O}"
       $"os\t{RuntimeInformation.OSDescription}"
       $"architecture\t{RuntimeInformation.ProcessArchitecture}"
       $"runtime_identifier\t{RuntimeInformation.RuntimeIdentifier}"
       $"logical_processors\t{Environment.ProcessorCount}"
       $"cpu\t{cpu()}"
       $"harness_runtime\t{RuntimeInformation.FrameworkDescription}"
       "dotnet_sdk\t" + identify "dotnet" [ "--version" ]
       "wasm_dotnet_sdk\t" + identify (wasmDotnet()) [ "--version" ]
       "wasm_workloads\t" + identify (wasmDotnet()) [ "workload"; "list" ]
       "rustc\t" + identify "rustup" [ "run"; "stable"; "rustc"; "--version" ]
       "node\t" + identify "node" [ "--version" ]
       "scope\trender loop only; exact comparison and snapshots excluded"
       "instruction_counts\tunavailable for direct runtimes; DSP counts remain in DSP grading"
       "compile_times\twall time, current caches; excludes workload installation and dynamic JIT compilation"
       "background_load\tuncontrolled; timing is not a callback-deadline guarantee" |])

let timedBuild configuration label executable arguments =
  let directory = measurements configuration
  Directory.CreateDirectory directory |> ignore
  printfn "Build %s" label
  let timer = Stopwatch.StartNew()
  let mutable success = false
  try
    run configuration.root executable arguments
    success <- true
  finally
    timer.Stop()
    let path = Path.Combine(directory, "compile-times.tsv")
    if not (File.Exists path) then File.WriteAllText(path, "utc\tstage\twall_ms\tsuccess\n")
    File.AppendAllText(path,
      $"{DateTimeOffset.UtcNow:O}\t{label}\t{timer.Elapsed.TotalMilliseconds}\t{success}\n")

let build configuration targets =
  context configuration
  let build label executable arguments = timedBuild configuration label executable arguments
  if Array.contains "rust-native" targets then
    let lookup = ProcessStartInfo("rustup", RedirectStandardOutput = true, UseShellExecute = false)
    lookup.ArgumentList.Add "which"
    lookup.ArgumentList.Add "rustc"
    use child = Process.Start lookup
    let compiler = child.StandardOutput.ReadToEnd().Trim()
    child.WaitForExit()
    if child.ExitCode <> 0 then failwith "rustup rustc unavailable"
    Environment.SetEnvironmentVariable("RUSTC", compiler)
    build "rust-native" "cargo"
      [ "build"; "--manifest-path"; configuration.manifest
        "--target-dir"; Path.Combine(configuration.output, "cargo"); "--release"; "--locked" ]
  if Array.contains "dotnet-aot" targets then
    build "dotnet-aot" (wasmDotnet())
      [ "publish"; configuration.project; "-c"; "Release"; "-r"; RuntimeInformation.RuntimeIdentifier
        "--self-contained"; "true"; "-p:PublishAot=true"; "-p:NuGetAudit=false"
        "-o"; Path.Combine(configuration.output, "dotnet-aot") ]
  for target in [| "dotnet-wasm"; "dotnet-wasm-aot" |] do
    if Array.contains target targets then
      let aot = if target = "dotnet-wasm-aot" then "true" else "false"
      build target (wasmDotnet())
        [ "publish"; Path.Combine(Path.GetDirectoryName configuration.project, "portable.browser.fsproj")
          "-c"; "Release"; "-p:NuGetAudit=false"; $"-p:RunAOTCompilation={aot}"
          "-p:WasmMainJSPath=" + Path.Combine(configuration.javascript, "dotnet-wasm-grade.mjs")
          "-o"; Path.Combine(configuration.output, target) ]

let command configuration target =
  let grade = Path.Combine(configuration.javascript, "grade.mjs")
  let wasm = Path.Combine(configuration.output, "cargo/wasm32-unknown-unknown/release/dsp56k_portable.wasm")
  match target with
  | "dotnet"     -> "dotnet", [ configuration.dotnet ]
  | "javascript" -> "node", [ grade ]
  | "rust-wasm"  -> "node", [ grade; "--wasm"; wasm ]
  | "rust-native" ->
    "dotnet", [ configuration.dotnet; "--native"
                Path.Combine(configuration.output, "cargo/release", nativeName) ]
  | "dotnet-aot" ->
    let name = Path.GetFileNameWithoutExtension configuration.project
    Path.Combine(configuration.output, target, name + if OperatingSystem.IsWindows() then ".exe" else ""), []
  | "dotnet-wasm" | "dotnet-wasm-aot" ->
    let directory = Path.Combine(configuration.output, target)
    let published = Path.Combine(directory, "wwwroot/_framework/dotnet.js")
    let modulePath =
      if File.Exists published then published
      else Directory.GetFiles(directory, "dotnet.js", SearchOption.AllDirectories) |> Array.exactlyOne
    "node", [ Path.Combine(configuration.javascript, "dotnet-wasm-grade.mjs"); modulePath; target ]
  | value -> invalidArg "target" value

let artifacts configuration targets =
  seq {
    for target in targets do
      match target with
      | "rust-native" -> yield Path.Combine(configuration.output, "cargo/release", nativeName)
      | "dotnet-aot" | "dotnet-wasm" | "dotnet-wasm-aot" ->
        let directory = Path.Combine(configuration.output, target)
        if Directory.Exists directory then
          yield! Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
      | _ -> ()
  }

let grade configuration target fixture result warmup trials =
  let executable, prefix = command configuration target
  let arguments = prefix @ [ fixture; result; string warmup; string trials ]
  run configuration.root executable arguments
  let directory = measurements configuration
  Directory.CreateDirectory directory |> ignore
  let saved = Path.Combine(directory, Path.GetFileNameWithoutExtension(fixture) + "." + target + ".json")
  File.Copy(result, saved, true)
  let path = Path.Combine(directory, "workloads.tsv")
  if not (File.Exists path) then
    File.WriteAllText(path, "utc\ttarget\tfixture\tfixture_sha256\twarmup\ttrials\tresult\n")
  let hash = Security.Cryptography.SHA256.HashData(File.ReadAllBytes fixture) |> Convert.ToHexString
  let relative path = Path.GetRelativePath(configuration.root, path)
  let row =
    String.concat "\t"
      [ DateTimeOffset.UtcNow.ToString("O"); target; relative fixture; hash
        string warmup; string trials; relative saved ]
  File.AppendAllText(path, row + "\n")

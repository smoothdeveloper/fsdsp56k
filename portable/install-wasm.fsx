/// Reuse an installed .NET 10 SDK in a writable toolchain; no system workload changes.
open System
open System.IO
open System.Diagnostics

let sdkVersion = "10.0.401"
let executable = if OperatingSystem.IsWindows() then "dotnet.exe" else "dotnet"
let info = ProcessStartInfo("dotnet", RedirectStandardOutput = true, UseShellExecute = false)
info.ArgumentList.Add "--list-sdks"
let sdks =
  use lookup = Process.Start info
  let sdks = lookup.StandardOutput.ReadToEnd().Split '\n'
  lookup.WaitForExit()
  if lookup.ExitCode <> 0 then failwith "Cannot list installed .NET SDKs"
  sdks
let sdk =
  sdks |> Array.tryFind (fun line -> line.StartsWith(sdkVersion + " "))
  |> Option.defaultWith (fun () -> failwith $"Install .NET SDK {sdkVersion} first")
let start = sdk.IndexOf '['
let sdkDirectory = sdk.Substring(start + 1, sdk.IndexOf(']') - start - 1)
let source = Directory.GetParent(sdkDirectory).FullName
let target = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../out/toolchains/dotnet10"))
Directory.CreateDirectory target |> ignore
if not (File.Exists(Path.Combine(target, executable))) then
  File.Copy(Path.Combine(source, executable), Path.Combine(target, executable))
let link name destination =
  let path = Path.Combine(target, name)
  if not (Directory.Exists path) then Directory.CreateSymbolicLink(path, destination) |> ignore
for name in [| "host"; "shared"; "sdk-manifests"; "templates" |] do
  if Directory.Exists(Path.Combine(source, name)) then link name (Path.Combine(source, name))
Directory.CreateDirectory(Path.Combine(target, "sdk")) |> ignore
let installedSdk = Path.Combine(target, "sdk", sdkVersion)
// MSBuild resolves SDK symlinks back to the system root and misses local workloads.
if not (Directory.Exists installedSdk) || not (isNull (DirectoryInfo(installedSdk).LinkTarget)) then
  let staged = installedSdk + ".copy"
  let original = Path.Combine(sdkDirectory, sdkVersion)
  printfn "Copy SDK %s into the writable toolchain" sdkVersion
  Directory.CreateDirectory staged |> ignore
  for path in Directory.EnumerateFiles(original, "*", SearchOption.AllDirectories) do
    let destination = Path.Combine(staged, Path.GetRelativePath(original, path))
    Directory.CreateDirectory(Path.GetDirectoryName destination) |> ignore
    File.Copy(path, destination, true)
  if Directory.Exists installedSdk then Directory.Delete installedSdk
  Directory.Move(staged, installedSdk)
Directory.CreateDirectory(Path.Combine(target, "packs")) |> ignore
for pack in Directory.GetDirectories(Path.Combine(source, "packs")) do
  link ("packs/" + Path.GetFileName pack) pack
let install = ProcessStartInfo(Path.Combine(target, executable), UseShellExecute = false)
for argument in [| "workload"; "install"; "wasm-tools"; "--skip-manifest-update" |] do
  install.ArgumentList.Add argument
do
  use child = Process.Start install
  child.WaitForExit()
  if child.ExitCode <> 0 then failwith $"WASM workload installation exited {child.ExitCode}"
printfn "WASM toolchain: %s" target

/// Optional Fable 5.18 Rust/WASM build. The compiler is local; no global tool changes.
open System
open System.IO
open System.Diagnostics

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let run command arguments =
  let info = ProcessStartInfo(command, WorkingDirectory = root, UseShellExecute = false)
  for argument in arguments do info.ArgumentList.Add argument
  use child = Process.Start info
  child.WaitForExit()
  if child.ExitCode <> 0 then failwith $"{command} exited {child.ExitCode}"
let tool = Path.Combine(root, ".bench", "tools", "fable-5.18.0", "fable")
if not (File.Exists tool) then
  run "dotnet" [ "tool"; "install"; "fable"; "--version"; "5.18.0"; "--tool-path"; ".bench/tools/fable-5.18.0" ]
run "dotnet" [ "fsi"; "portable/prepare.fsx" ]
printfn "[1/2] Fable 5.18.0 -> Rust"
run tool [ "portable/portable.rust.fsproj"; "--lang"; "rust"; "--outDir"; "out/portable/rust" ]
if fsi.CommandLineArgs |> Array.contains "--emit-only" then exit 0
printfn "[2/2] Rust -> wasm32-unknown-unknown (release)"
// rustup's target libraries must match rustc; a Homebrew rustc can shadow it on macOS.
let info = ProcessStartInfo("rustup", WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true)
info.ArgumentList.Add "which"
info.ArgumentList.Add "rustc"
use lookup = Process.Start info
let compiler = lookup.StandardOutput.ReadToEnd().Trim()
lookup.WaitForExit()
if lookup.ExitCode <> 0 then failwith "rustup rustc unavailable"
Environment.SetEnvironmentVariable("RUSTC", compiler)
run "cargo" [ "build"; "--manifest-path"; "portable/Cargo.toml"; "--target-dir"; "out/portable/cargo"
              "--target"; "wasm32-unknown-unknown"; "--release"; "--locked" ]

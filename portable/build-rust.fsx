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
// fable-library-rust prints a value's Rust type name for Debug/Display of sequences and other library
// types, which would carry F# module and type names into the binary. A neutral word instead.
let library = Path.Combine(root, "out", "portable", "rust", "fable_modules", "fable-library-rust", "src")
if Directory.Exists library then
  for file in Directory.GetFiles(library, "*.rs", SearchOption.AllDirectories) do
    let text = File.ReadAllText file
    let neutral = text.Replace("write!(f, \"{}\", core::any::type_name::<Self>())", "f.write_str(\"object\")")
    if neutral <> text then File.WriteAllText(file, neutral)
if fsi.CommandLineArgs |> Array.contains "--emit-only" then exit 0
printfn "[2/2] Rust -> wasm32-unknown-unknown (release)"
// rustup's target libraries must match rustc; a Homebrew rustc can shadow it on macOS.
// By default rustup's nightly rebuilds the standard library with immediate-abort panics and no panic
// locations, so no source path or file name is left in the wasm (tested with nightly 1.87, 2025-03-20;
// later nightlies spell it -Zunstable-options -Cpanic=immediate-abort). --stable keeps the stable compiler:
// same stripped profile and remapped paths, but file names stay in panic messages.
let stable = fsi.CommandLineArgs |> Array.contains "--stable"
let channel = if stable then "stable" else "nightly"
let info = ProcessStartInfo("rustup", WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true)
for argument in [ "which"; "--toolchain"; channel; "rustc" ] do info.ArgumentList.Add argument
use lookup = Process.Start info
let compiler = lookup.StandardOutput.ReadToEnd().Trim()
lookup.WaitForExit()
if lookup.ExitCode <> 0 then failwith $"rustup {channel} rustc unavailable"
Environment.SetEnvironmentVariable("RUSTC", compiler)
let home = Environment.GetFolderPath Environment.SpecialFolder.UserProfile
// rustc applies the last matching prefix: the home folder first, then the checkout inside it.
let remap = [ $"--remap-path-prefix={home}=~"; $"--remap-path-prefix={root}=." ]
let flags = if stable then remap else "-Zlocation-detail=none" :: remap
Environment.SetEnvironmentVariable("CARGO_ENCODED_RUSTFLAGS", null)
Environment.SetEnvironmentVariable("RUSTFLAGS", String.concat " " flags)
let cargo = [ "build"; "--manifest-path"; "portable/Cargo.toml"; "--target-dir"; "out/portable/cargo"
              "--target"; "wasm32-unknown-unknown"; "--release"; "--locked" ]
if stable then run "cargo" cargo
else run "rustup" ([ "run"; "nightly"; "cargo" ] @ cargo @ [ "-Zbuild-std=std,panic_abort"; "-Zbuild-std-features=panic_immediate_abort" ])

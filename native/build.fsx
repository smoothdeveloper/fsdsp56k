/// The native libraries, release builds into native/target/release: the emulator (native/, the
/// oracle and the track harness) and the audio output (native/fsdsp56k-audio, for audition).
/// `dotnet fsi native/build.fsx [--offline]`.
open System
open System.Diagnostics
open System.IO

let here = __SOURCE_DIRECTORY__
let target = Path.Combine(here, "target")
let offline = fsi.CommandLineArgs |> Array.contains "--offline"

let cargo (manifest: string) =
  let info = ProcessStartInfo("cargo", WorkingDirectory = here, UseShellExecute = false)
  for a in [ "build"; "--release"; "--manifest-path"; manifest; "--target-dir"; target ] do info.ArgumentList.Add a
  if offline then info.ArgumentList.Add "--offline"
  use child = Process.Start info
  child.WaitForExit()
  if child.ExitCode <> 0 then failwith $"cargo build {manifest}: exit {child.ExitCode}"

for manifest in [ "Cargo.toml"; Path.Combine("fsdsp56k-audio", "Cargo.toml") ] do
  printfn "cargo build --release %s" manifest
  cargo manifest
printfn "built into %s" (Path.Combine(target, "release"))

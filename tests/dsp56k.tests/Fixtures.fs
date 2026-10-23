/// Paths to the third-party sources scripts/fetch-fixtures.sh checks out, and to the native
/// oracle; tests that need one skip when it is missing.
module dsp56k.tests.Fixtures

open System.IO
open Expecto

let (</>) (a: string) (b: string) = Path.Combine(a, b)

let repoRoot =
  let rec up (dir: DirectoryInfo | null) =
    match dir with
    | null -> Directory.GetCurrentDirectory()
    | d when File.Exists(d.FullName </> "fsdsp56k.slnx") -> d.FullName
    | d -> up d.Parent
  up (DirectoryInfo(System.AppContext.BaseDirectory))

let fixture name = repoRoot </> "fixtures" </> name

/// What a skipped test needed, with the command that provides it: Main reports these after the
/// run, so a missing prerequisite is not just a count of ignored tests.
let missing = System.Collections.Concurrent.ConcurrentDictionary<string, int>()

let private skipFor (what: string) =
  missing.AddOrUpdate(what, 1, fun _ n -> n + 1) |> ignore
  skiptest what

let needFixture name =
  if not (Directory.Exists(fixture name)) then skipFor $"fixture {name} missing: run scripts/fetch-fixtures.sh"

/// The portable .NET runner (Portable.Net.dll) that portable/grade.fsx builds.
let portableNet = repoRoot </> "portable" </> "bin" </> "Release" </> "net10.0" </> "Portable.Net.dll"

let needPortable () =
  if not (File.Exists portableNet) then skipFor "portable runner missing: dotnet fsi portable/grade.fsx"

let needNative () =
  if not (dsp56k.Native.load ()) then skipFor "native oracle missing: cargo build --release in native/ (or set FSDSP56K_NATIVE)"

let needAudio () =
  if not (dsp56k.audio.Output.load ()) then skipFor "audio and MIDI library missing: dotnet fsi native/build.fsx (or set FSDSP56K_AUDIO)"

/// The skipped tests' prerequisites, one line each with how many tests they held back.
let reportMissing () =
  if not missing.IsEmpty then
    eprintfn "\nSkipped for missing prerequisites:"
    for KeyValue(what, n) in missing |> Seq.sortBy (fun kv -> kv.Key) do
      eprintfn "  %3d test(s): %s" n what

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

let needFixture name =
  if not (Directory.Exists(fixture name)) then skiptest $"fixture {name} missing: run scripts/fetch-fixtures.sh"

let needNative () =
  if not (dsp56k.Native.load ()) then skiptest "native oracle missing: cargo build --release in native/"

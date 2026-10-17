/// Kernels graded against the shipped machines they reproduce.
module samples.kernels.Grading

open System.IO
open dsp56k.kernel
open dsp56k.kernel.Grade

let (</>) (a: string) (b: string) = Path.Combine(a, b)

/// INIT, a note, a pitch and level change while it sounds, a retrigger.
let sineScenario =
  { name = "sine: notes, a change mid-note, a retrigger"
    steps =
      [ Settings [| 64.0; 100.0 |]
        Init
        Trig
        Render 8
        Settings [| 96.0; 127.0 |]
        Render 8
        Settings [| 20.0; 30.0 |]
        Trig
        Render 16 ] }

/// The sine kernel against jmamma/MDS machines/examples/sine/sine.mds.
let sine (fixtures: string) =
  let shipped = fixtures </> "MDS" </> "machines" </> "examples" </> "sine" </> "sine.mds"
  if not (File.Exists shipped) then Error $"{shipped} missing: run scripts/fetch-fixtures.sh"
  else
    let bytes = File.ReadAllBytes shipped
    match machinedrum.run.Import.package bytes with
    | Error e -> Error $"%A{e}"
    | Ok imported -> grade Sine.kernel imported.machine (Some bytes) sineScenario

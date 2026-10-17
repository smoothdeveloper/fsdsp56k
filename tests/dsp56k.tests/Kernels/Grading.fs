/// Kernels graded against the shipped machines they were ported from, and the report page.
module samples.kernels.Grading

open System.IO
open dsp56k.kernel
open dsp56k.kernel.Grade

let (</>) (a: string) (b: string) = Path.Combine(a, b)

/// A kernel, the machine it reproduces and how to exercise it.
type Sample =
  { title    : string
    origin   : string
    license  : string
    notes    : string
    kernel   : Kernel.Kernel
    package  : string             // the reference .mds or .syx, under fixtures/
    source   : string             // the reference's source, under fixtures/
    scenario : Scenario }

/// The package bytes of an .mds, or of the single machine in a .syx.
let packageBytes (path: string) =
  let bytes = File.ReadAllBytes path
  if path.EndsWith ".syx" then
    match machinedrum.Sysex.packages bytes with
    | Ok [| _, p |] -> Ok p
    | Ok ps -> Error $"{path}: {ps.Length} packages, expected one"
    | Error e -> Error $"{path}: %A{e}"
  else Ok bytes

let run (fixtures: string) (s: Sample) =
  let package = fixtures </> s.package
  if not (File.Exists package) then Error $"{package} missing: run scripts/fetch-fixtures.sh"
  else
    packageBytes package
    |> Result.bind (fun bytes ->
      match machinedrum.run.Import.package bytes with
      | Error e -> Error $"%A{e}"
      | Ok imported -> grade s.kernel imported.machine (Some bytes) s.scenario)
    |> Result.map (fun report ->
      let src = fixtures </> s.source
      { Html.title = s.title; Html.origin = s.origin; Html.license = s.license; Html.notes = s.notes
        Html.referenceSource = (if File.Exists src then Some(string (Path.GetFileName src), File.ReadAllText src) else None)
        Html.report = report })

let sine =
  { title = "SINE"
    origin = "jmamma/MDS machines/examples/sine"
    license = "no licence published"
    notes = "The MDS standard's sine generator: a 24.24 phase through the 32,768-word ROM sine, its gain ramped from the previous block's level to AMP over the block."
    kernel = Sine.kernel
    package = "MDS/machines/examples/sine/sine.mds"
    source = "MDS/machines/examples/sine/sine.asm56k"
    scenario =
      { name = "notes, a change mid-note, a retrigger"
        steps =
          [ Settings [| 64.0; 100.0 |]
            Init
            Trig
            Render 8
            Settings [| 96.0; 127.0 |]
            Render 8
            Settings [| 20.0; 30.0 |]
            Trig
            Render 16 ] } }

let inputThru =
  { title = "INPUT THRU"
    origin = "jmamma/MDS machines/examples/imports/input_thru"
    license = "no licence published"
    notes = "External input A or B through the firmware's 256-word input ring (imports 2 and 3): a ring cursor reads one pair a frame, 48 pairs behind the input position. The harness fills the ring with two test tones."
    kernel = InputThru.kernel
    package = "MDS/machines/examples/imports/input_thru.mds"
    source = "MDS/machines/examples/imports/input_thru.asm56k"
    scenario =
      { name = "input A, input B, level changes"
        steps =
          [ Settings [| 0.0; 100.0 |]
            Init
            Trig
            Render 8
            Settings [| 1.0; 127.0 |]
            Render 8
            Settings [| 0.0; 40.0 |]
            Trig
            Render 8 ] } }

let mainTap =
  { title = "MAIN TAP"
    origin = "jmamma/MDS machines/examples/imports/main_tap"
    license = "no licence published"
    notes = "The main output, mono, now or one or two blocks late, from the firmware's main-output ring (imports 4 and 5). The harness fills the ring with a stereo test signal."
    kernel = MainTap.kernel
    package = "MDS/machines/examples/imports/main_tap.mds"
    source = "MDS/machines/examples/imports/main_tap.asm56k"
    scenario =
      { name = "this block, one and two blocks back"
        steps =
          [ Settings [| 0.0; 90.0 |]
            Init
            Trig
            Render 4
            Settings [| 1.0; 90.0 |]
            Render 4
            Settings [| 2.0; 127.0 |]
            Render 4 ] } }

let pluck =
  { title = "PLUCK"
    origin = "jmamma/MDS machines/examples/imports/pluck"
    license = "no licence published"
    notes = "A Karplus-Strong string in the track's own external memory (import 6): the trigger fills one period (2^21 / P words, by 24 DIV steps) with shift-register noise; then each sample is the average of the two oldest, times the loop gain, through a cursor whose modulus is the string length. The scenario gives DEC as Q23 words (the harness does not model the decay transform)."
    kernel = Pluck.kernel
    package = "MDS/machines/examples/imports/pluck.mds"
    source = "MDS/machines/examples/imports/pluck.asm56k"
    scenario =
      { name = "a 172 Hz pluck, a high short one, a low one"
        steps =
          [ Words [| 8192; 0x7f0000; 0x600000 |]
            Init
            Render 2
            Trig
            Render 40
            Words [| 40000; 0x7a0000; 0x7fffff |]
            Trig
            Render 24
            Words [| 3000; 0x7ff000; 0x500000 |]
            Trig
            Render 40 ] } }

let tx6Bd =
  { title = "TX6 BD"
    origin = "kitbasher mds/tx6/bd (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "A 606-style kick: a swept two-pole resonator body with a HEAT-controlled third harmonic, a low-passed noise click whose gain ramps per sample, and a per-sample interpolated gain envelope. Control tables live in program memory; the per-block arrays in the shared temporary memory (imports 7 and 8)."
    kernel = Tx6Bd.kernel
    package = "kitbasher/mds/tx6/bd/bd.syx"
    source = "kitbasher/mds/tx6/bd/bd.asm"
    scenario =
      { name = "default kick, a bright long one, a short dark one"
        steps =
          [ Settings [| 75.0; 102.0; 51.0; 0.0; 0.0; 127.0 |]
            Init
            Trig
            Render 40
            Settings [| 40.0; 60.0; 100.0; 127.0; 127.0; 64.0 |]
            Trig
            Render 40
            Settings [| 127.0; 127.0; 0.0; 64.0; 0.0; 0.0 |]
            Trig
            Render 24 ] } }

let tomScenario =
  { name = "default tom, low and noisy, high and clean"
    steps =
      [ Settings [| 64.0; 102.0; 127.0; 127.0 |]
        Init
        Trig
        Render 40
        Settings [| 20.0; 127.0; 64.0; 0.0 |]
        Trig
        Render 40
        Settings [| 127.0; 40.0; 0.0; 80.0 |]
        Trig
        Render 24 ] }

let tx6Lt =
  { title = "TX6 LT"
    origin = "kitbasher mds/tx6/lt (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "A 606-style low tom: two damped resonator modes, the first gliding down in pitch, and LCG noise through a biquad, under a fast + slow gain ramped per sample. One engine (Tx6Tom.fs) with HT: the voice is a record the compiler reads, so LT's glide is generated here and not in HT."
    kernel = Tx6Tom.ltKernel
    package = "kitbasher/mds/tx6/lt/lt.syx"
    source = "kitbasher/mds/tx6/lt/lt.asm"
    scenario = tomScenario }

let tx6Ht =
  { title = "TX6 HT"
    origin = "kitbasher mds/tx6/ht (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "The high tom from the same engine as LT: other tables, seeds and starting levels, and no pitch glide (the `if voice.glide` branches are decided when compiling)."
    kernel = Tx6Tom.htKernel
    package = "kitbasher/mds/tx6/ht/ht.syx"
    source = "kitbasher/mds/tx6/ht/ht.asm"
    scenario = tomScenario }

let metalScenario =
  { name = "default hat, all metal and long, all noise and short"
    steps =
      [ Settings [| 64.0; 64.0; 64.0 |]
        Init
        Trig
        Render 40
        Settings [| 100.0; 127.0; 127.0 |]
        Trig
        Render 40
        Settings [| 10.0; 10.0; 0.0 |]
        Trig
        Render 24 ] }

let tx6Ch =
  { title = "TX6 CH"
    origin = "kitbasher mds/tx6/ch (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "A 606-style closed hat: LCG noise through a biquad and two undamped metal partials, each under a gain ramped per sample from fast, slow, attack, bell and click envelopes. One engine (Tx6Metal.fs) with OH."
    kernel = Tx6Metal.chKernel
    package = "kitbasher/mds/tx6/ch/ch.syx"
    source = "kitbasher/mds/tx6/ch/ch.asm"
    scenario = metalScenario }

let tx6Oh =
  { title = "TX6 OH"
    origin = "kitbasher mds/tx6/oh (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "The open hat from the CH engine: its own tables, noise seed and starting envelopes."
    kernel = Tx6Metal.ohKernel
    package = "kitbasher/mds/tx6/oh/oh.syx"
    source = "kitbasher/mds/tx6/oh/oh.asm"
    scenario = metalScenario }

let tx6Cy =
  { title = "TX6 CY"
    origin = "kitbasher mds/tx6/cy (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "The cymbal from the hat engine: slower bell and click decays, fixed metal levels, and BELL setting the bell layer from a table instead of METL's mix; the `match voice.layer` is decided when compiling."
    kernel = Tx6Metal.cyKernel
    package = "kitbasher/mds/tx6/cy/cy.syx"
    source = "kitbasher/mds/tx6/cy/cy.asm"
    scenario =
      { metalScenario with
          name = "default cymbal, long and belled, short without bell" } }

let tx6Sd =
  { title = "TX6 SD"
    origin = "kitbasher mds/tx6/sd (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "A 606-style snare: a gliding damped body mode and LCG noise through a COLR biquad, each under a fast + slow gain ramped per sample; a gate holds it open for DEC's hold time, then speeds every decay up once."
    kernel = Tx6Sd.kernel
    package = "kitbasher/mds/tx6/sd/sd.syx"
    source = "kitbasher/mds/tx6/sd/sd.asm"
    scenario =
      { name = "default snare, a long bright one, a tight dark one"
        steps =
          [ Settings [| 64.0; 102.0; 95.0; 64.0; 127.0 |]
            Init
            Trig
            Render 40
            Settings [| 100.0; 127.0; 127.0; 127.0; 60.0 |]
            Trig
            Render 40
            Settings [| 20.0; 20.0; 0.0; 0.0; 0.0 |]
            Trig
            Render 24 ] } }

let tx6Cp =
  { title = "TX6 CP"
    origin = "kitbasher mds/tx6/cp (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "A 606-style clap: LCG noise through a TUNE biquad under a gain built per block from a burst sequence, tails, a foundation thump and air, ramped per sample. A frame counter, compressed by TGHT at the start, indexes the burst, air and fade tables; the duration warp is one F# helper used for both tail decays."
    kernel = Tx6Cp.kernel
    package = "kitbasher/mds/tx6/cp/cp.syx"
    source = "kitbasher/mds/tx6/cp/cp.asm"
    scenario =
      { name = "default clap to its end, a tight short one, a long airy one"
        steps =
          [ Settings [| 64.0; 102.0; 64.0; 127.0; 0.0 |]
            Init
            Trig
            Render 420
            Settings [| 100.0; 30.0; 0.0; 60.0; 127.0 |]
            Trig
            Render 60
            Settings [| 20.0; 127.0; 127.0; 100.0; 40.0 |]
            Trig
            Render 80 ] } }

let samples = [ sine; inputThru; mainTap; pluck; tx6Bd; tx6Lt; tx6Ht; tx6Ch; tx6Oh; tx6Cy; tx6Sd; tx6Cp ]

/// Every sample graded; the page written to `outDir/index.html`.
let report (fixtures: string) (outDir: string) =
  let results = samples |> List.map (fun s -> s, run fixtures s)
  let entries = results |> List.choose (fun (_, r) -> match r with Ok e -> Some e | Error _ -> None)
  Directory.CreateDirectory outDir |> ignore
  let page = outDir </> "index.html"
  File.WriteAllText(page, Html.page entries)
  for e in entries do
    File.WriteAllText(outDir </> (e.report.kernel + ".kernel.asm56k"), dsp56k.dsl.Program.source e.report.compiled.items)
  results, page

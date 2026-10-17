/// Kernels graded against the shipped machines they were ported from, and the report page.
module samples.kernels.Grading

open System.IO
open dsp56k.kernel
open dsp56k.kernel.Grade

let (</>) (a: string) (b: string) = Path.Combine(a, b)

/// A kernel, the machine it reproduces and how to exercise it.
type Sample =
  { title    : string
    group    : string list        // where it comes from: its output folder, e.g. kitbasher/tx6
    folder   : string             // its own folder in the group
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

/// Graded with the optimizer at `level`.
let runAt (level: Optimize.Level) (fixtures: string) (s: Sample) =
  let package = fixtures </> s.package
  if not (File.Exists package) then Error $"{package} missing: run scripts/fetch-fixtures.sh"
  else
    packageBytes package
    |> Result.bind (fun bytes ->
      match machinedrum.run.Import.package bytes with
      | Error e -> Error $"%A{e}"
      | Ok imported -> gradeMdsAt level s.kernel imported.machine (Some bytes) s.scenario)
    |> Result.map (fun report ->
      let src = fixtures </> s.source
      { Html.title = s.title; Html.origin = s.origin; Html.license = s.license; Html.notes = s.notes
        Html.referenceSource = (if File.Exists src then Some(string (Path.GetFileName src), File.ReadAllText src) else None)
        Html.report = report })

/// Graded with every pass.
let run (fixtures: string) (s: Sample) = runAt Optimize.Peephole fixtures s

// ---------------------------------------------------------------------------------------------
// The scenarios: two bars at 120 BPM as a sequencer plays them, a few trigs, parameter locks on
// some (lasting their step) and other base settings in the second bar.

let bar values trigs = { values = Settings values; trigs = trigs }

/// Front-panel controls 0..127 with their defaults (`ranges` overrides an Index control's range).
let controls (names: string array) (defaults: float array) (ranges: (string * float * float) list) =
  Array.map2 (fun n d ->
    let low, high = ranges |> List.tryFind (fun (r, _, _) -> r = n) |> Option.map (fun (_, l, h) -> l, h) |> Option.defaultValue (0.0, 127.0)
    { name = n; low = low; high = high; standard = d }) names defaults

/// The musical bars, then each control swept and the pairwise combinations.
let full (cs: Control array) (p: Pattern) = exhaustive cs Settings p

/// Notes as Pitch words for SINE (the harness converts settings x 128).
let sinePattern =
  { name = "a bass line, AMP accents, a second bar an octave up"; bpm = 120.0; names = [| "PTCH"; "AMP" |]
    bars =
      [ bar [| 40.0; 90.0 |] [ 0, []; 3, [ 0, 47.0 ]; 6, [ 0, 52.0; 1, 127.0 ]; 8, []; 11, [ 0, 35.0 ]; 14, [ 0, 59.0; 1, 60.0 ] ]
        bar [| 64.0; 100.0 |] [ 0, []; 2, [ 0, 71.0 ]; 4, [ 1, 40.0 ]; 7, [ 0, 76.0 ]; 10, []; 12, [ 0, 59.0; 1, 127.0 ] ] ] }

let inputPattern =
  { name = "input A, B on locks, a quieter second bar on B"; bpm = 120.0; names = [| "SIDE"; "LEV" |]
    bars =
      [ bar [| 0.0; 100.0 |] [ 0, []; 4, [ 0, 1.0 ]; 8, [ 1, 127.0 ]; 12, [ 0, 1.0; 1, 50.0 ] ]
        bar [| 1.0; 70.0 |] [ 0, []; 6, [ 0, 0.0; 1, 120.0 ]; 10, [ 1, 20.0 ] ] ] }

let tapPattern =
  { name = "now, one and two blocks back on locks"; bpm = 120.0; names = [| "BACK"; "LEV" |]
    bars =
      [ bar [| 0.0; 90.0 |] [ 0, []; 4, [ 0, 1.0 ]; 8, [ 0, 2.0; 1, 127.0 ]; 12, [ 1, 40.0 ] ]
        bar [| 2.0; 110.0 |] [ 0, []; 5, [ 0, 0.0 ]; 9, [ 0, 1.0; 1, 60.0 ] ] ] }

/// PLUCK's parameters as words: P = Hz x 2^21 / 44,100, DEC and LEV in Q23.
let hz (f: float) = System.Math.Round(f * 2097152.0 / 44100.0)
let pluckPattern =
  { name = "an A minor arpeggio, damped notes on locks, a brighter second bar"; bpm = 120.0; names = [| "PTCH"; "DEC"; "LEV" |]
    bars =
      [ { values = Words [| int (hz 110.0); 0x7f8000; 0x600000 |]
          trigs = [ 0, []; 3, [ 0, hz 130.81 ]; 6, [ 0, hz 164.81 ]; 8, [ 0, hz 220.0; 1, float 0x7a0000 ]; 11, [ 0, hz 196.0 ]; 14, [ 0, hz 164.81 ] ] }
        { values = Words [| int (hz 220.0); 0x7ff000; 0x7fffff |]
          trigs = [ 0, []; 2, [ 0, hz 261.63 ]; 4, [ 0, hz 329.63 ]; 7, [ 0, hz 440.0; 1, float 0x7c0000 ]; 10, [ 0, hz 392.0 ]; 12, [ 0, hz 87.31; 2, float 0x500000 ] ] } ] }

let bdPattern =
  { name = "four on the floor, a pitched-down kick on locks, a long heated second bar"; bpm = 120.0
    names = [| "TUNE"; "DEC"; "TRAN"; "HEAT"; "XL"; "CLIK" |]
    bars =
      [ bar [| 75.0; 102.0; 51.0; 0.0; 0.0; 127.0 |] [ 0, []; 4, []; 8, []; 11, [ 0, 40.0; 5, 30.0 ]; 12, []; 14, [ 1, 60.0 ] ]
        bar [| 60.0; 120.0; 90.0; 100.0; 127.0; 64.0 |] [ 0, []; 4, [ 3, 0.0 ]; 8, []; 10, [ 0, 100.0; 1, 50.0 ]; 12, [] ] ] }

let tomPattern =
  { name = "a fill, tuned on locks, darker and noisier in the second bar"; bpm = 120.0; names = [| "TUNE"; "DEC"; "NOIS"; "OVER" |]
    bars =
      [ bar [| 64.0; 102.0; 127.0; 127.0 |] [ 6, []; 10, [ 0, 80.0 ]; 13, [ 0, 50.0; 1, 70.0 ]; 14, [] ]
        bar [| 40.0; 127.0; 64.0; 60.0 |] [ 0, []; 7, [ 2, 0.0 ]; 11, [ 0, 90.0; 3, 127.0 ]; 15, [] ] ] }

let metalPattern =
  { name = "eighths, accents and long decays on locks, a brighter second bar"; bpm = 120.0; names = [| "TUNE"; "DEC"; "METL" |]
    bars =
      [ bar [| 64.0; 89.0; 64.0 |] [ 0, []; 2, [ 1, 40.0 ]; 4, []; 6, [ 1, 120.0 ]; 8, []; 10, [ 2, 127.0 ]; 12, []; 14, [ 0, 100.0; 2, 0.0 ] ]
        bar [| 90.0; 60.0; 110.0 |] [ 0, []; 2, []; 4, [ 1, 127.0 ]; 6, []; 8, [ 0, 30.0 ]; 10, []; 12, []; 14, [ 1, 20.0 ] ] ] }

let cymbalPattern =
  { name = "a crash, a ride on locks, a darker second bar"; bpm = 120.0; names = [| "TUNE"; "DEC"; "BELL" |]
    bars =
      [ bar [| 64.0; 102.0; 127.0 |] [ 0, []; 8, [ 1, 60.0; 2, 30.0 ]; 12, [ 1, 60.0 ] ]
        bar [| 40.0; 120.0; 60.0 |] [ 0, []; 6, [ 2, 127.0 ]; 10, [] ] ] }

let snarePattern =
  { name = "backbeat, a ghost note on a lock, a tight bright second bar"; bpm = 120.0; names = [| "TUNE"; "DEC"; "SNAP"; "COLR"; "BODY" |]
    bars =
      [ bar [| 64.0; 102.0; 95.0; 64.0; 127.0 |] [ 4, []; 12, []; 15, [ 1, 30.0; 4, 40.0 ] ]
        bar [| 90.0; 60.0; 127.0; 110.0; 80.0 |] [ 4, []; 10, [ 0, 40.0 ]; 12, [ 2, 0.0; 3, 0.0 ] ] ] }

let clapPattern =
  { name = "backbeat, tightened on a lock, a long airy second bar"; bpm = 120.0; names = [| "TUNE"; "DEC"; "NOIS"; "BODY"; "TGHT" |]
    bars =
      [ bar [| 64.0; 102.0; 64.0; 127.0; 0.0 |] [ 4, []; 12, [ 4, 127.0; 1, 40.0 ] ]
        bar [| 30.0; 127.0; 127.0; 90.0; 40.0 |] [ 4, []; 12, [ 0, 100.0 ] ] ] }

let sine =
  { title = "SINE"
    group = [ "jmamma-mds"; "examples" ]
    folder = "sine"
    origin = "jmamma/MDS machines/examples/sine"
    license = "no licence published"
    notes = "The MDS standard's sine generator: a 24.24 phase through the 32,768-word ROM sine, its gain ramped from the previous block's level to AMP over the block."
    kernel = Sine.kernel
    package = "MDS/machines/examples/sine/sine.mds"
    source = "MDS/machines/examples/sine/sine.asm56k"
    scenario = play 32 (full (controls [| "PTCH"; "AMP" |] [| 64.0; 100.0 |] []) sinePattern) }

let inputThru =
  { title = "INPUT THRU"
    group = [ "jmamma-mds"; "examples"; "imports" ]
    folder = "input-thru"
    origin = "jmamma/MDS machines/examples/imports/input_thru"
    license = "no licence published"
    notes = "External input A or B through the firmware's 256-word input ring (imports 2 and 3): a ring cursor reads one pair a frame, 48 pairs behind the input position. The harness fills the ring with two test tones."
    kernel = InputThru.kernel
    package = "MDS/machines/examples/imports/input_thru.mds"
    source = "MDS/machines/examples/imports/input_thru.asm56k"
    scenario = play 32 (full (controls [| "SIDE"; "LEV" |] [| 0.0; 100.0 |] [ "SIDE", 0.0, 1.0 ]) inputPattern) }

let mainTap =
  { title = "MAIN TAP"
    group = [ "jmamma-mds"; "examples"; "imports" ]
    folder = "main-tap"
    origin = "jmamma/MDS machines/examples/imports/main_tap"
    license = "no licence published"
    notes = "The main output, mono, now or one or two blocks late, from the firmware's main-output ring (imports 4 and 5). The harness fills the ring with a stereo test signal."
    kernel = MainTap.kernel
    package = "MDS/machines/examples/imports/main_tap.mds"
    source = "MDS/machines/examples/imports/main_tap.asm56k"
    scenario = play 32 (full (controls [| "BACK"; "LEV" |] [| 0.0; 90.0 |] [ "BACK", 0.0, 2.0 ]) tapPattern) }

let pluck =
  { title = "PLUCK"
    group = [ "jmamma-mds"; "examples"; "imports" ]
    folder = "pluck"
    origin = "jmamma/MDS machines/examples/imports/pluck"
    license = "no licence published"
    notes = "A Karplus-Strong string in the track's own external memory (import 6): the trigger fills one period (2^21 / P words, by 24 DIV steps) with shift-register noise; then each sample is the average of the two oldest, times the loop gain, through a cursor whose modulus is the string length. The scenario gives DEC as Q23 words (the harness does not model the decay transform)."
    kernel = Pluck.kernel
    package = "MDS/machines/examples/imports/pluck.mds"
    source = "MDS/machines/examples/imports/pluck.asm56k"
    scenario =
      play 32 (exhaustive
                 [| { name = "PTCH"; low = hz 55.0; high = hz 1760.0; standard = hz 220.0 }
                    { name = "DEC"; low = float 0x700000; high = float 0x7ff000; standard = float 0x7f8000 }
                    { name = "LEV"; low = float 0x200000; high = float 0x7fffff; standard = float 0x600000 } |]
                 (fun v -> Words(Array.map int v)) pluckPattern) }

let tx6Bd =
  { title = "TX6 BD"
    group = [ "kitbasher"; "tx6" ]
    folder = "bd"
    origin = "kitbasher mds/tx6/bd (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "A 606-style kick: a swept two-pole resonator body with a HEAT-controlled third harmonic, a low-passed noise click whose gain ramps per sample, and a per-sample interpolated gain envelope. Control tables live in program memory; the per-block arrays in the shared temporary memory (imports 7 and 8)."
    kernel = Tx6Bd.kernel
    package = "kitbasher/mds/tx6/bd/bd.syx"
    source = "kitbasher/mds/tx6/bd/bd.asm"
    scenario = play 32 (full (controls [| "TUNE"; "DEC"; "TRAN"; "HEAT"; "XL"; "CLIK" |] [| 75.0; 102.0; 51.0; 0.0; 0.0; 127.0 |] []) bdPattern) }

let tx6Lt =
  { title = "TX6 LT"
    group = [ "kitbasher"; "tx6" ]
    folder = "lt"
    origin = "kitbasher mds/tx6/lt (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "A 606-style low tom: two damped resonator modes, the first gliding down in pitch, and LCG noise through a biquad, under a fast + slow gain ramped per sample. One engine (Tx6Tom.fs) with HT: the voice is a record the compiler reads, so LT's glide is generated here and not in HT."
    kernel = Tx6Tom.ltKernel
    package = "kitbasher/mds/tx6/lt/lt.syx"
    source = "kitbasher/mds/tx6/lt/lt.asm"
    scenario = play 32 (full (controls [| "TUNE"; "DEC"; "NOIS"; "OVER" |] [| 64.0; 102.0; 127.0; 127.0 |] []) tomPattern) }

let tx6Ht =
  { title = "TX6 HT"
    group = [ "kitbasher"; "tx6" ]
    folder = "ht"
    origin = "kitbasher mds/tx6/ht (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "The high tom from the same engine as LT: other tables, seeds and starting levels, and no pitch glide (the `if voice.glide` branches are decided when compiling)."
    kernel = Tx6Tom.htKernel
    package = "kitbasher/mds/tx6/ht/ht.syx"
    source = "kitbasher/mds/tx6/ht/ht.asm"
    scenario = play 32 (full (controls [| "TUNE"; "DEC"; "NOIS"; "OVER" |] [| 64.0; 102.0; 127.0; 127.0 |] []) tomPattern) }

let tx6Ch =
  { title = "TX6 CH"
    group = [ "kitbasher"; "tx6" ]
    folder = "ch"
    origin = "kitbasher mds/tx6/ch (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "A 606-style closed hat: LCG noise through a biquad and two undamped metal partials, each under a gain ramped per sample from fast, slow, attack, bell and click envelopes. One engine (Tx6Metal.fs) with OH."
    kernel = Tx6Metal.chKernel
    package = "kitbasher/mds/tx6/ch/ch.syx"
    source = "kitbasher/mds/tx6/ch/ch.asm"
    scenario = play 32 (full (controls [| "TUNE"; "DEC"; "METL" |] [| 64.0; 89.0; 64.0 |] []) metalPattern) }

let tx6Oh =
  { title = "TX6 OH"
    group = [ "kitbasher"; "tx6" ]
    folder = "oh"
    origin = "kitbasher mds/tx6/oh (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "The open hat from the CH engine: its own tables, noise seed and starting envelopes."
    kernel = Tx6Metal.ohKernel
    package = "kitbasher/mds/tx6/oh/oh.syx"
    source = "kitbasher/mds/tx6/oh/oh.asm"
    scenario = play 32 (full (controls [| "TUNE"; "DEC"; "METL" |] [| 64.0; 89.0; 64.0 |] []) metalPattern) }

let tx6Cy =
  { title = "TX6 CY"
    group = [ "kitbasher"; "tx6" ]
    folder = "cy"
    origin = "kitbasher mds/tx6/cy (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "The cymbal from the hat engine: slower bell and click decays, fixed metal levels, and BELL setting the bell layer from a table instead of METL's mix; the `match voice.layer` is decided when compiling."
    kernel = Tx6Metal.cyKernel
    package = "kitbasher/mds/tx6/cy/cy.syx"
    source = "kitbasher/mds/tx6/cy/cy.asm"
    scenario = play 32 (full (controls [| "TUNE"; "DEC"; "BELL" |] [| 64.0; 102.0; 127.0 |] []) cymbalPattern) }

let tx6Sd =
  { title = "TX6 SD"
    group = [ "kitbasher"; "tx6" ]
    folder = "sd"
    origin = "kitbasher mds/tx6/sd (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "A 606-style snare: a gliding damped body mode and LCG noise through a COLR biquad, each under a fast + slow gain ramped per sample; a gate holds it open for DEC's hold time, then speeds every decay up once."
    kernel = Tx6Sd.kernel
    package = "kitbasher/mds/tx6/sd/sd.syx"
    source = "kitbasher/mds/tx6/sd/sd.asm"
    scenario = play 32 (full (controls [| "TUNE"; "DEC"; "SNAP"; "COLR"; "BODY" |] [| 64.0; 102.0; 95.0; 64.0; 127.0 |] []) snarePattern) }

let tx6Cp =
  { title = "TX6 CP"
    group = [ "kitbasher"; "tx6" ]
    folder = "cp"
    origin = "kitbasher mds/tx6/cp (Simple606-inspired, by Matthew Fecher and Fadedlimes)"
    license = "MIT"
    notes = "A 606-style clap: LCG noise through a TUNE biquad under a gain built per block from a burst sequence, tails, a foundation thump and air, ramped per sample. A frame counter, compressed by TGHT at the start, indexes the burst, air and fade tables; the duration warp is one F# helper used for both tail decays."
    kernel = Tx6Cp.kernel
    package = "kitbasher/mds/tx6/cp/cp.syx"
    source = "kitbasher/mds/tx6/cp/cp.asm"
    scenario = play 32 (full (controls [| "TUNE"; "DEC"; "NOIS"; "BODY"; "TGHT" |] [| 64.0; 102.0; 64.0; 127.0; 0.0 |] []) clapPattern) }

let samples = [ sine; inputThru; mainTap; pluck; tx6Bd; tx6Lt; tx6Ht; tx6Ch; tx6Oh; tx6Cy; tx6Sd; tx6Cp ]

/// Every sample graded; the page written to `outDir/index.html`.
/// A sample's folder under a root: its group, then its own.
let folderOf (root: string) (s: Sample) = Path.Combine(Array.ofList (root :: s.group @ [ s.folder ]))

/// Every sample graded. Under `outDir`: report.html, and <samples folder>/<group>/<machine>/ with each
/// kernel's renders, scenario script, Audacity labels and listing. What a previous run wrote
/// there is deleted first: it is all regenerated.
let report (fixtures: string) (outDir: string) =
  let samplesDir = outDir </> "samples"
  let page = outDir </> "report.html"
  dsp56k.kernel.Audio.clean samplesDir
  if File.Exists page then File.Delete page
  let results = samples |> List.map (fun s -> s, run fixtures s)
  let kept =
    [ for s, r in results do
        match r with
        | Ok e ->
          dsp56k.kernel.Audio.save (folderOf samplesDir s) e.report.kernel e.report |> ignore
          yield e, String.concat "/" ("samples" :: s.group @ [ s.folder ])
        | Error _ -> () ]
  Directory.CreateDirectory outDir |> ignore
  let media = dict [ for e, folder in kept -> e.report.kernel, folder ]
  File.WriteAllText(page, Html.pageWithMedia "Kernel grades" Html.lede (fun e -> Some media[e.report.kernel]) (List.map fst kept))
  results, page

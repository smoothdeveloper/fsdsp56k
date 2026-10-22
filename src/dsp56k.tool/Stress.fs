/// `fsdsp56k stress`: a machine's worst-case cycles over many front-panel settings. A case is what
/// a sequencer step does to a track: the settings, INIT, TRIG, then RENDER blocks. The cases come
/// from the grade's sweeps (`Grade.exhaustive`: each control over its range from the defaults,
/// then pairwise combinations of low, default and high) or from seeded random settings.
module dsp56k.tool.Stress

open machinedrum
open machinedrum.run
open dsp56k.kernel

type Sweep =
  | Exhaustive
  | RandomCases of cases: int * seed: int

/// The machine's parameters as the grade sees controls: range and default.
let controls (m: Mds.Machine) : Grade.Control array =
  m.parameters
  |> Array.map (fun p ->
    { name     = p.name
      low      = float p.minValue
      high     = float p.maxValue
      standard = float p.defaultValue })

/// The settings of every trig `Grade.exhaustive` plays (its locks over the bar's values), the
/// defaults first, each once.
let exhaustiveSettings (cs: Grade.Control array) =
  let empty : Grade.Pattern = { name = "stress"; bpm = 120.0; names = [||]; bars = [] }
  let pattern = Grade.exhaustive cs Grade.Settings empty
  let locked (values: float array) (locks: (int * float) list) =
    values |> Array.mapi (fun i v -> locks |> List.tryFind (fst >> (=) i) |> Option.map snd |> Option.defaultValue v)
  [ yield cs |> Array.map _.standard
    for bar in pattern.bars do
      match bar.values with
      | Grade.Settings values ->
        for _, locks in bar.trigs do
          yield locked values locks
      | _ -> () ]
  |> List.distinct

/// `cases` settings, each control a whole number over its range, from `seed`.
let randomSettings (cs: Grade.Control array) (cases: int) (seed: int) =
  let rng = System.Random seed
  [ for _ in 1 .. cases -> cs |> Array.map (fun c -> float (rng.Next(int c.low, int c.high + 1))) ]

let settingsOf (cs: Grade.Control array) sweep =
  match sweep with
  | Exhaustive -> exhaustiveSettings cs
  | RandomCases(n, seed) -> randomSettings cs n seed

/// One case's cycles (JSR to STOP, service stand-ins included, as `run` counts them).
type Case =
  { settings  : float array
    init      : int64
    trig      : int64
    render    : int64      // the worst RENDER block
    trigBlock : int64 }    // TRIG and the RENDER after it: the block a trig lands in

/// A case: the settings, INIT, TRIG, then `blocks` RENDER blocks.
// TODO: the state INIT leaves is all a case starts from; a machine whose cost grows with state
// INIT does not reset (a delay line filled over seconds) needs longer runs or --trig-every.
let measure (track: Track.Track) (blocks: int) (settings: float array) =
  track.SetSettings settings
  let init = track.Init 0
  let trig = track.Trig 0
  let renders = [| for _ in 1 .. max 1 blocks -> snd (track.Render 0) |]
  { settings  = settings
    init      = init
    trig      = trig
    render    = Array.max renders
    trigBlock = trig + renders[0] }

type Phase =
  | Init
  | Trig
  | Render
  | TrigBlock

let phases = [ Init; Trig; Render; TrigBlock ]

let phaseName phase =
  match phase with
  | Init      -> "INIT"
  | Trig      -> "TRIG"
  | Render    -> "RENDER"
  | TrigBlock -> "TRIG+RENDER"

let cycles phase (c: Case) =
  match phase with
  | Init      -> c.init
  | Trig      -> c.trig
  | Render    -> c.render
  | TrigBlock -> c.trigBlock

let settingsText (cs: Grade.Control array) (settings: float array) =
  Array.map2 (fun (c: Grade.Control) v -> $"{c.name}={v}") cs settings |> String.concat " "

/// The worst case per phase, as a text table; and whether the trig block stays within the
/// declared cycles and the track limit.
// TODO: which calls the declared figure covers is read here as `run` reads it (TRIG+RENDER);
// INIT is shown, not judged.
let report (cs: Grade.Control array) (declared: int) (cases: Case list) =
  let worst phase = cases |> List.maxBy (cycles phase)
  let mean phase = cases |> List.averageBy (cycles phase >> float)
  let judged phase = phase = Render || phase = TrigBlock
  let lines =
    [ $"""{"phase",-12} {"worst",7} {"mean",7} {"declared",8} {"limit",6}  settings at worst"""
      for phase in phases do
        let w = worst phase
        let d, l = if judged phase then string declared, string Abi.trackCycleLimit else "-", "-"
        let flag = if judged phase && (cycles phase w > int64 declared || cycles phase w > int64 Abi.trackCycleLimit) then " OVER" else ""
        $"{phaseName phase,-12} {cycles phase w,7} {mean phase,7:F0} {d,8} {l,6}  {settingsText cs w.settings}{flag}" ]
  let within =
    let w = cycles TrigBlock (worst TrigBlock)
    w <= int64 declared && w <= int64 Abi.trackCycleLimit
  lines, within

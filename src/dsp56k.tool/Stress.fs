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
    trigBlock : int64      // TRIG and the RENDER after it: the block a trig lands in
    peak      : float      // the output's largest magnitude over the case, full scale 1
    rms       : float      // its RMS over the case
    returned  : bool }     // false: a call did not reach its RTS within the emulator's limit

/// A case: the settings, INIT, TRIG, then `blocks` RENDER blocks, retriggered every `trigEvery`
/// blocks if given, each block's words to `sink`.
// TODO: the state INIT leaves is all a case starts from; a machine whose cost grows with state
// INIT does not reset (a delay line filled over seconds) needs longer runs or --trig-every.
let measureWith (track: Track.Track) (blocks: int) (trigEvery: int option) (sink: int array -> unit) (settings: float array) =
  track.SetSettings settings
  let mutable returned = true
  // a call that never returns ends the case, which keeps what it measured: a sweep goes on
  let call f = if not returned then 0L else (try f () with e when e.Message = "the routine did not return" -> returned <- false; 0L)
  let init = call (fun () -> track.Init 0)
  let mutable trig = 0L
  let mutable render = 0L
  let mutable trigBlock = 0L
  let mutable peak = 0.0
  let mutable energy = 0.0
  let mutable samples = 0
  for b in 0 .. max 1 blocks - 1 do
    if returned then
      let triggered = b = 0 || (trigEvery |> Option.exists (fun n -> n > 0 && b % n = 0))
      let t = if triggered then call (fun () -> track.Trig 0) else 0L
      let mutable words = Array.empty
      let r = call (fun () -> let w, c = track.Render 0 in words <- w; c)
      sink words
      for w in words do
        let x = Track.toFloat w
        peak <- max peak (abs x)
        energy <- energy + x * x
      samples <- samples + words.Length
      trig <- max trig t
      render <- max render r
      if triggered then trigBlock <- max trigBlock (t + r)
  { settings  = settings
    init      = init
    trig      = trig
    render    = render
    trigBlock = trigBlock
    peak      = peak
    rms       = if samples = 0 then 0.0 else sqrt (energy / float samples)
    returned  = returned }

/// A case as `stress` measures it without listening: one TRIG, `blocks` RENDER blocks.
let measure (track: Track.Track) (blocks: int) (settings: float array) = measureWith track blocks None ignore settings

/// At most `n` of `cases`, evenly spread, the first (the defaults) kept.
let sample (n: int) (cases: 'a list) =
  let all = List.length cases
  if n <= 0 || all <= n then cases
  else
    let a = Array.ofList cases
    [ for k in 0 .. n - 1 -> a[k * all / n] ]

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
// The declared figure covers TRIG plus RENDER for a block in which the track is triggered (the
// MDS standard, "Per-track cycle budget"); INIT is shown, not judged.
// TODO: the standard's figure also includes the firmware's fixed dispatch overhead per track,
// which the harness does not model: a margin under the declared figure is needed, not shown.
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
        $"{phaseName phase,-12} {cycles phase w,7} {mean phase,7:F0} {d,8} {l,6}  {settingsText cs w.settings}{flag}"
      let loud = cases |> List.maxBy _.peak
      let dbfs x = if x <= 0.0 then "silent" else $"{20.0 * log10 x:F1} dBFS"
      $"""{"output",-12} peak {loud.peak:F4} ({dbfs loud.peak}), RMS {loud.rms:F4} ({dbfs loud.rms}); {cases |> List.filter (fun c -> c.peak > 0.0) |> List.length} of {List.length cases} cases sound; loudest at {settingsText cs loud.settings}"""
      let hung = cases |> List.filter (fun c -> not c.returned)
      if not hung.IsEmpty then $"""{"no return",-12} {hung.Length} of {List.length cases} cases: a call did not return, first at {settingsText cs hung.Head.settings}""" ]
  let within =
    let w = cycles TrigBlock (worst TrigBlock)
    w <= int64 declared && w <= int64 Abi.trackCycleLimit
  lines, within

// ---------------------------------------------------------------------------------------------
// Worst-case search. Cycles are a step function of the settings, measured as a black box: no
// gradient to follow, so the search is simulated annealing over whole-number settings, with
// restarts from the best case found.

/// What the search maximises: one phase, or the worst of all of them.
type Objective =
  | OnePhase of Phase
  | AnyPhase
  | Peak      // the loudest output peak: settings that make a machine sound
  | Rms       // the loudest output RMS

let objectiveName objective =
  match objective with
  | OnePhase phase -> phaseName phase
  | AnyPhase       -> "the worst phase"
  | Peak           -> "the output peak"
  | Rms            -> "the output RMS"

let score objective (c: Case) =
  match objective with
  | OnePhase phase -> cycles phase c
  | AnyPhase       -> phases |> List.map (fun phase -> cycles phase c) |> List.max
  | Peak           -> int64 (System.Math.Round(c.peak * 8388608.0))
  | Rms            -> int64 (System.Math.Round(c.rms * 8388608.0))

/// Where the search starts: the defaults, the range edges (every control low, high, mid), each
/// control alone at its edges, and `random` random settings.
let starts (cs: Grade.Control array) (rng: System.Random) (random: int) =
  let mid (c: Grade.Control) = System.Math.Round((c.low + c.high) / 2.0)
  [ yield cs |> Array.map _.standard
    yield cs |> Array.map _.low
    yield cs |> Array.map _.high
    yield cs |> Array.map mid
    for i, c in Array.indexed cs do
      for edge in [ c.low; c.high ] do
        yield cs |> Array.mapi (fun j d -> if i = j then edge else d.standard)
    for _ in 1 .. random do
      yield cs |> Array.map (fun c -> float (rng.Next(int c.low, int c.high + 1))) ]
  |> List.distinct

/// One control moved: by a small step, a large one (a quarter of its range), or to an edge.
let neighbour (cs: Grade.Control array) (rng: System.Random) (settings: float array) =
  let i = rng.Next cs.Length
  let c = cs[i]
  let range = c.high - c.low
  let moved =
    match rng.Next 6 with
    | 0 -> settings[i] + 1.0
    | 1 -> settings[i] - 1.0
    | 2 -> settings[i] + max 1.0 (System.Math.Round(range / 4.0))
    | 3 -> settings[i] - max 1.0 (System.Math.Round(range / 4.0))
    | 4 -> c.low
    | _ -> c.high
  settings |> Array.mapi (fun j v -> if i = j then max c.low (min c.high moved) else v)

/// Every whole-number setting of every control: the grid the search stands in for.
let gridSize (cs: Grade.Control array) =
  cs |> Array.fold (fun n c -> n * bigint (int (c.high - c.low) + 1)) 1I

type Found =
  { best        : Case
    cases       : Case list    // every distinct case measured
    evaluations : int }

/// Simulated annealing for the case that maximises `objective`, within `budget` distinct
/// measurements. A move that costs fewer cycles is still taken with a chance that shrinks as the
/// temperature falls; after `patience` moves without a new best, it restarts from the best.
// TODO: the temperature schedule and the move mix are first guesses, tuned on the TX6 fixtures
// only; a machine with narrow peaks (one setting of one control) may need more random starts.
let anneal (cs: Grade.Control array) (evaluate: float array -> Case) objective (budget: int) (seed: int) =
  let rng = System.Random seed
  let seen = System.Collections.Generic.Dictionary<string, Case>()
  let order = ResizeArray<Case>()
  let measureOnce (settings: float array) =
    let key = settings |> Array.map string |> String.concat ","
    match seen.TryGetValue key with
    | true, c -> c
    | _ ->
      let c = evaluate settings
      seen[key] <- c
      order.Add c
      c
  let budget = max 1 budget
  let initial = starts cs rng (max 1 (budget / 20)) |> List.truncate budget |> List.map measureOnce
  let mutable best = initial |> List.maxBy (score objective)
  let mutable current = best
  let mutable stale = 0
  let patience = max 8 (budget / 10)
  let mutable moves = 0
  // a small space runs out of new settings before the budget does: bounded moves
  while order.Count < budget && moves < 20 * budget do
    moves <- moves + 1
    let temperature = max 0.5 (0.05 * float (score objective best) * (1.0 - float order.Count / float budget))
    let next = measureOnce (neighbour cs rng current.settings)
    let delta = float (score objective next - score objective current)
    if delta >= 0.0 || rng.NextDouble() < exp (delta / temperature) then current <- next
    if score objective next > score objective best then
      best <- next
      stale <- 0
    else
      stale <- stale + 1
      if stale >= patience then
        current <- best
        stale <- 0
  { best = best; cases = List.ofSeq order; evaluations = order.Count }

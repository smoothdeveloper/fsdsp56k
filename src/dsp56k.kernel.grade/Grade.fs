/// Grading a compiled kernel. One scenario (parameter settings, INIT, TRIG, RENDER blocks) runs
/// three ways: the kernel as .NET code, the compiled package on the emulator and, when there is
/// one, a reference package (a shipped machine) on the emulator. The grade reports
///
/// - correctness: the compiled program against the .NET kernel, output and state, word for word;
/// - fidelity: the compiled program's output against the reference's, word for word;
/// - cost: program words, workset words, cycles per entry point, against the reference;
/// - shape: per entry point, the compiled listing aligned with the reference's disassembly
///   (labels canonicalized), with the mnemonic edit distance, to show where cycles go.
module dsp56k.kernel.Grade

open System
open System.Text.RegularExpressions
open dsp56k
open dsp56k.Native
open dsp56k.kernel
open machinedrum
open machinedrum.run

type Step =
  | Settings of float array      // front-panel settings, converted by the machine's transforms
  | Words of int array           // parameter words as the DSP reads them
  | Init
  | Trig
  | Render of blocks: int

type Scenario =
  { name  : string
    steps : Step list }

/// What one execution produced, per RENDER block, and the cycles of each call.
type Run =
  { outputs : int array array
    states  : int array array
    init    : int64 list
    trig    : int64 list
    render  : int64 list }

type Mismatch =
  { what      : string
    block     : int
    index     : int
    expected  : int
    actual    : int
    differing : int }

/// First difference and count over all blocks.
let compare what (expected: int array array) (actual: int array array) =
  let pairs =
    [| for b in 0 .. min expected.Length actual.Length - 1 do
         let e, a = expected[b], actual[b]
         for i in 0 .. max e.Length a.Length - 1 do
           let ev = if i < e.Length then e[i] else -1
           let av = if i < a.Length then a[i] else -1
           if ev <> av then b, i, ev, av |]
  if pairs.Length = 0 && expected.Length = actual.Length then None
  elif pairs.Length = 0 then Some { what = what; block = min expected.Length actual.Length; index = 0; expected = expected.Length; actual = actual.Length; differing = 1 }
  else
    let b, i, e, a = pairs[0]
    Some { what = what; block = b; index = i; expected = e; actual = a; differing = pairs.Length }

// ---------------------------------------------------------------------------------------------
// Executions.

let layout = { Track.defaultLayout with sineTable = 0x8000 }

let parameterWords (machine: Mds.Machine) (settings: float array) =
  settings |> Array.mapi (fun i s -> Track.convert machine.parameters[i] s)

let runNet (target: Kernel.Target) (machine: Mds.Machine) (k: Kernel.Kernel) (scenario: Scenario) =
  let host = Host.Instance(target, k)
  let outputs, states = ResizeArray(), ResizeArray()
  for step in scenario.steps do
    match step with
    | Settings s -> host.SetParameters(parameterWords machine s)
    | Words w -> host.SetParameters w
    | Init -> host.Init()
    | Trig -> host.Trig()
    | Render n ->
      for _ in 1 .. n do
        outputs.Add(host.Render())
        states.Add(host.StateWords())
  { outputs = outputs.ToArray(); states = states.ToArray(); init = []; trig = []; render = [] }

/// A package on the emulator; `stateWords` Y words from Y+stateBase are read after each block.
let runTrack (package: Mds.Package) (machine: Mds.Machine) (stateBase: int) (stateWords: int) (scenario: Scenario) =
  use track = new Track.Track(package, Some machine, layout, Map.empty)
  let emu = track.Emulator
  emu.Load(Native.Space.X, layout.sineTable, Rom.sine.words)
  let outputs, states = ResizeArray(), ResizeArray()
  let inits, trigs, renders = ResizeArray(), ResizeArray(), ResizeArray()
  for step in scenario.steps do
    match step with
    | Settings s -> parameterWords machine s |> Array.iteri (fun i w -> track.SetParameter(i, w))
    | Words w -> w |> Array.iteri (fun i w -> track.SetParameter(i, w))
    | Init -> inits.Add(track.Init 0)
    | Trig -> trigs.Add(track.Trig 0)
    | Render n ->
      for _ in 1 .. n do
        let words, cycles = track.Render 0
        outputs.Add words
        renders.Add cycles
        states.Add [| for k in 0 .. stateWords - 1 -> emu.Read(Native.Space.Y, layout.workset + stateBase + k) |]
  { outputs = outputs.ToArray(); states = states.ToArray()
    init = List.ofSeq inits; trig = List.ofSeq trigs; render = List.ofSeq renders }

// ---------------------------------------------------------------------------------------------
// Packaging.

type Built =
  { compiled : Compile.Compiled
    machine  : Mds.Machine
    bytes    : byte array
    package  : Mds.Package }

/// The compiled kernel as an MDS package, with `machine`'s identity and controls.
let build (machine: Mds.Machine) (compiled: Compile.Compiled) =
  match Assembler.assembleItems compiled.items with
  | Error e -> Error(Assembler.errorText e)
  | Ok r ->
    let _, words = Assembler.image dsp56k.Space.P r
    let m =
      { machine with
          patches = Abi.patches compiled.items
          initSymbol = "INIT"; trigSymbol = "TRIG"; renderSymbol = "RENDER"
          xWorkset = compiled.xWorkset
          yWorkset = compiled.layout.yWorkset }
    match Mds.pack m words r.symbols None with
    | Error e -> Error $"%A{e}"
    | Ok bytes ->
      match Mds.read bytes with
      | Error e -> Error $"%A{e}"
      | Ok pkg -> Ok { compiled = compiled; machine = m; bytes = bytes; package = pkg }

// ---------------------------------------------------------------------------------------------
// Shape: listings side by side.

let entries = [| "INIT"; "TRIG"; "RENDER" |]

let canonical (it: Item) =
  Regex.Replace(Print.item it, @"\b(L_[0-9a-f]+|K_[A-Z0-9_]+|IMPORT_[0-9a-f]+|RELOC_[0-9a-f]+)\b", "@").Trim()

let mnemonic (text: string) = (text.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)).[0]

/// The instructions under each entry label, in listing order.
let sections (items: Item array) =
  let mutable current = ""
  let found = Collections.Generic.Dictionary<string, ResizeArray<string>>()
  for it in items do
    match it with
    | Label name when Array.contains name entries ->
      current <- name
      found[name] <- ResizeArray()
    | Op _ when current <> "" -> found[current].Add(canonical it)
    | _ -> ()
  entries |> Array.map (fun e -> e, (match found.TryGetValue e with | true, l -> l.ToArray() | _ -> [||]))

type Shape =
  { entry     : string
    ours      : int
    reference : int
    distance  : int
    diff      : string array }

/// Levenshtein over mnemonics; the alignment as `=` same, `~` different instruction, `+` only
/// ours, `-` only the reference.
let align entry (ours: string array) (reference: string array) =
  let n, m = ours.Length, reference.Length
  let d = Array2D.zeroCreate (n + 1) (m + 1)
  for i in 0 .. n do d[i, 0] <- i
  for j in 0 .. m do d[0, j] <- j
  for i in 1 .. n do
    for j in 1 .. m do
      let same = if mnemonic ours[i - 1] = mnemonic reference[j - 1] then 0 else 1
      d[i, j] <- min (min (d[i - 1, j] + 1) (d[i, j - 1] + 1)) (d[i - 1, j - 1] + same)
  let lines = ResizeArray()
  let mutable i, j = n, m
  while i > 0 || j > 0 do
    if i > 0 && j > 0 && d[i, j] = d[i - 1, j - 1] + (if mnemonic ours[i - 1] = mnemonic reference[j - 1] then 0 else 1) then
      let mark = if mnemonic ours[i - 1] = mnemonic reference[j - 1] then "=" else "~"
      lines.Add(sprintf "%s %-34s | %s" mark ours[i - 1] reference[j - 1])
      i <- i - 1
      j <- j - 1
    elif i > 0 && d[i, j] = d[i - 1, j] + 1 then
      lines.Add(sprintf "+ %-34s |" ours[i - 1])
      i <- i - 1
    else
      lines.Add(sprintf "- %-34s | %s" "" reference[j - 1])
      j <- j - 1
  lines.Reverse()
  { entry = entry; ours = n; reference = m; distance = d[n, m]; diff = lines.ToArray() }

// ---------------------------------------------------------------------------------------------
// The grade.

type Cost =
  { programWords : int
    xWorkset     : int
    yWorkset     : int
    init         : int64
    trig         : int64
    renderMean   : float
    renderWorst  : int64 }

let cost (pkg: Mds.Package) (m: Mds.Machine) (r: Run) =
  let worst xs = if List.isEmpty xs then 0L else List.max xs
  { programWords = pkg.programWords.Length; xWorkset = m.xWorkset; yWorkset = m.yWorkset
    init = worst r.init; trig = worst r.trig
    renderMean = (if r.render.IsEmpty then 0.0 else List.averageBy float r.render); renderWorst = worst r.render }

type Report =
  { kernel      : string
    scenario    : string
    blocks      : int
    correctness : Mismatch list           // compiled vs .NET kernel
    fidelity    : Mismatch option option  // compiled vs reference (None: no reference)
    ours        : Cost
    reference   : Cost option
    shapes      : Shape array }

/// Compile, build, run and compare. `reference` is a shipped package, when there is one.
let grade (k: Kernel.Kernel) (machine: Mds.Machine) (reference: byte array option) (scenario: Scenario) =
  let target = Kernel.mds
  Compile.compile target k
  |> Result.bind (build machine)
  |> Result.bind (fun built ->
    let layout = built.compiled.layout
    let stateWords = layout.yWorkset - target.stateBase
    let net = runNet target machine k scenario
    let dsp = runTrack built.package built.machine target.stateBase stateWords scenario
    let correctness = [ compare "output" net.outputs dsp.outputs; compare "state" net.states dsp.states ] |> List.choose id
    let refRun, refCost, shapes =
      match reference with
      | None -> None, None, [||]
      | Some bytes ->
        match Mds.read bytes, Import.package bytes with
        | Ok pkg, Ok imported ->
          let refMachine = imported.machine
          let r = runTrack pkg refMachine target.stateBase 0 scenario
          let ours = sections built.compiled.items
          let theirs = sections imported.items |> Map.ofArray
          Some r, Some(cost pkg refMachine r), ours |> Array.map (fun (e, o) -> align e o theirs[e])
        | Error e, _ -> failwithf "reference: %A" e
        | _, Error e -> failwithf "reference: %A" e
    Ok { kernel = k.name; scenario = scenario.name; blocks = dsp.outputs.Length
         correctness = correctness
         fidelity = refRun |> Option.map (fun r -> compare "output" r.outputs dsp.outputs)
         ours = cost built.package built.machine dsp
         reference = refCost
         shapes = shapes })

let text (r: Report) =
  let mismatch (m: Mismatch) =
    $"{m.what} differs in {m.differing} words, first at block {m.block} word {m.index}: expected ${m.expected:x6}, got ${m.actual:x6}"
  let ratio (a: float) (b: float) = if b = 0.0 then "" else sprintf "  (x%.2f)" (a / b)
  let row name (ours: float) (theirs: float option) =
    match theirs with
    | Some t -> sprintf "  %-14s %10.0f %10.0f%s" name ours t (ratio ours t)
    | None -> sprintf "  %-14s %10.0f" name ours
  let t = r.reference
  [| yield $"kernel {r.kernel}, scenario {r.scenario}, {r.blocks} blocks"
     match r.correctness with
     | [] -> yield "  correctness: the compiled program matches the .NET kernel, output and state"
     | ms -> for m in ms -> "  correctness FAILS: " + mismatch m
     match r.fidelity with
     | None -> yield "  fidelity: no reference"
     | Some None -> yield "  fidelity: output identical to the reference"
     | Some(Some m) -> yield "  fidelity FAILS: " + mismatch m
     yield ""
     yield sprintf "  %-14s %10s %10s" "cost" "kernel" "reference"
     yield row "program words" (float r.ours.programWords) (t |> Option.map (fun c -> float c.programWords))
     yield row "X workset" (float r.ours.xWorkset) (t |> Option.map (fun c -> float c.xWorkset))
     yield row "Y workset" (float r.ours.yWorkset) (t |> Option.map (fun c -> float c.yWorkset))
     yield row "INIT cycles" (float r.ours.init) (t |> Option.map (fun c -> float c.init))
     yield row "TRIG cycles" (float r.ours.trig) (t |> Option.map (fun c -> float c.trig))
     yield row "RENDER mean" r.ours.renderMean (t |> Option.map _.renderMean)
     yield row "RENDER worst" (float r.ours.renderWorst) (t |> Option.map (fun c -> float c.renderWorst))
     for s in r.shapes do
       yield ""
       yield $"  {s.entry}: {s.ours} instructions, reference {s.reference}, mnemonic edit distance {s.distance}"
       for l in s.diff -> "    " + l |]
  |> String.concat "\n"

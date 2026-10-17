/// Grading a compiled kernel. One scenario (parameter settings, INIT, TRIG, RENDER blocks) runs
/// three ways: the kernel as .NET code, the compiled program on the emulator and, when there is
/// one, a reference on the emulator. Where programs run is a `Harness`: the Machinedrum MDS
/// track (packages, the shipped machines as references) or a firmware's DSP (`firmware`, with
/// routines at absolute addresses as references, `image`). The grade reports
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
  | Mark of string               // a label for the timeline (a sequencer step); does nothing

type Scenario =
  { name  : string
    steps : Step list }

/// Frames per second of the harnesses' audio.
let sampleRate = 44100

/// One bar of a 16-step pattern: the parameter values it plays with (Settings or Words), and its
/// trigs: a step (0..15) and the parameter locks of that trig (parameter index, value), which
/// last until the next step as a sequencer's locks do.
type Bar =
  { values : Step
    trigs  : (int * (int * float) list) list }

/// Bars as a sequencer plays them at `bpm`, sixteen steps a bar.
type Pattern =
  { name  : string
    bpm   : float
    names : string array      // parameter names, for the timeline's labels
    bars  : Bar list }

/// The scenario a pattern makes on a target rendering `blockFrames` frames a call: INIT, then each
/// step's changes and trig at the block boundary nearest the step's time, RENDER blocks between.
let play (blockFrames: int) (p: Pattern) =
  let framesPerStep = float sampleRate * 60.0 / p.bpm / 4.0
  let blockOf (step: int) = int (System.Math.Round(float step * framesPerStep / float blockFrames))
  let locked (values: Step) (locks: (int * float) list) =
    let pick i x = locks |> List.tryFind (fst >> (=) i) |> Option.map snd |> Option.defaultValue x
    match values with
    | Settings v -> Settings(Array.mapi pick v)
    | Words w -> Words(Array.mapi (fun i x -> int (pick i (float x))) w)
    | other -> other
  let name i = if i < p.names.Length then p.names[i] else string i
  let describe (locks: (int * float) list) =
    locks |> List.map (fun (i, v) -> $"{name i} {v}") |> String.concat ", "
  // (step, what happens there)
  let events =
    [ for b, bar in List.indexed p.bars do
        let first = b * 16
        yield first, [ Mark $"bar {b + 1}"; bar.values ]
        for step, locks in bar.trigs |> List.sortBy fst do
          let g = first + step
          let label = $"{b + 1}.{step / 4 + 1}.{step % 4 + 1} TRIG" + (if locks.IsEmpty then "" else $" ({describe locks})")
          yield g, [ Mark label; (if locks.IsEmpty then bar.values else locked bar.values locks); Trig ]
          // a lock lasts its step
          if not locks.IsEmpty && not (bar.trigs |> List.exists (fun (s, _) -> s = step + 1)) && step + 1 < 16 then
            yield g + 1, [ bar.values ] ]
    |> List.groupBy fst |> List.map (fun (g, xs) -> g, List.collect snd xs) |> List.sortBy fst
  let total = blockOf (16 * p.bars.Length)
  let steps =
    [ match p.bars with
      | bar :: _ -> yield bar.values
      | [] -> ()
      yield Init
      let mutable at = 0
      for g, changes in events do
        let block = blockOf g
        if block > at then
          yield Render(block - at)
          at <- block
        yield! changes
      if total > at then yield Render(total - at) ]
  { name = $"{p.name} ({p.bars.Length} bars at {p.bpm} BPM)"; steps = steps }

/// A control as a sweep sees it: its range and its default.
type Control =
  { name     : string
    low      : float
    high     : float
    standard : float }

/// `n` values spread over a control's range, ends included (whole numbers when the range is).
let spread (c: Control) (n: int) =
  [ for k in 0 .. n - 1 ->
      System.Math.Round(c.low + (c.high - c.low) * float k / float (max 1 (n - 1))) ]
  |> List.distinct

/// From the defaults, one bar per control: `n` trigs two steps apart, each locking that control
/// to the next of `n` values over its range.
let sweeps (controls: Control array) (n: int) (values: float array -> Step) =
  let defaults = controls |> Array.map _.standard
  [ for i, c in Array.indexed controls ->
      { values = values defaults
        trigs = spread c n |> List.truncate 8 |> List.mapi (fun k v -> 2 * k, [ i, v ]) } ]

/// Rows of control levels (low, default, high) in which every pair of controls meets in every
/// pair of levels: a pairwise covering set, built greedily (each row takes the levels that cover
/// the most pairs not yet seen), far fewer rows than every combination.
let pairwiseRows (controls: Control array) =
  let levels = controls |> Array.map (fun c -> [ c.low; c.standard; c.high ] |> List.distinct |> List.toArray)
  let n = controls.Length
  let uncovered = System.Collections.Generic.HashSet<int * int * int * int>()
  for i in 0 .. n - 1 do
    for j in i + 1 .. n - 1 do
      for a in 0 .. levels[i].Length - 1 do
        for b in 0 .. levels[j].Length - 1 do
          uncovered.Add((i, a, j, b)) |> ignore
  let rows = ResizeArray<int array>()
  while uncovered.Count > 0 do
    let row = Array.create n -1
    // start from a pair still uncovered
    let i, a, j, b = Seq.head uncovered
    row[i] <- a
    row[j] <- b
    for k in 0 .. n - 1 do
      if row[k] < 0 then
        let gain l =
          [ for m in 0 .. n - 1 do
              if row[m] >= 0 then
                let key = if k < m then (k, l, m, row[m]) else (m, row[m], k, l)
                if uncovered.Contains key then yield 1 ] |> List.sum
        row[k] <- [ 0 .. levels[k].Length - 1 ] |> List.maxBy gain
    for x in 0 .. n - 1 do
      for y in x + 1 .. n - 1 do
        uncovered.Remove((x, row[x], y, row[y])) |> ignore
    rows.Add row
  [ for row in rows -> row |> Array.mapi (fun k l -> levels[k][l]) ]

/// The pairwise rows as bars: a trig every two steps, its locks the row's values.
let pairwise (controls: Control array) (values: float array -> Step) =
  let defaults = controls |> Array.map _.standard
  pairwiseRows controls
  |> List.chunkBySize 8
  |> List.map (fun rows ->
    { values = values defaults
      trigs = rows |> List.mapi (fun k row -> 2 * k, [ for i, v in Array.indexed row -> i, v ]) })

/// A pattern's musical bars, then every control swept, then the pairwise combinations.
let exhaustive (controls: Control array) (values: float array -> Step) (p: Pattern) =
  { p with
      name = $"{p.name}; then each control swept from its default, then pairwise combinations"
      names = controls |> Array.map _.name
      bars = p.bars @ sweeps controls 8 values @ pairwise controls values }

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
// Harnesses: where a program runs and how it is called.

/// A program loaded on an emulator, called the way its host calls a machine.
type Session =
  inherit IDisposable
  abstract Emulator : Emulator
  /// The workset's address (r6 on each call).
  abstract Workset : int
  /// Parameter words, parameter 0 first, where the target reads them.
  abstract SetParameters : int array -> unit
  /// Each call returns its cycles (0 for an entry the program does not have).
  abstract Init : unit -> int64
  abstract Trig : unit -> int64
  /// One RENDER: the block's words (Target.blockWords) and the cycles.
  abstract Render : unit -> int array * int64

/// A program ready to run: the compiled kernel or a reference.
type Program =
  { name           : string
    programWords   : int
    xWorkset       : int
    yWorkset       : int
    declaredCycles : int            // the RENDER budget the program declares (0: none)
    items          : Item array     // its listing, entries labelled INIT, TRIG, RENDER, for the shape ([||]: none)
    start          : unit -> Session }

/// Where kernels are graded: the target they compile for, how front-panel settings become
/// parameter words, and how a compiled kernel becomes a Program.
type Harness =
  { name           : string
    target         : Kernel.Target
    parameterNames : string array
    settings       : float array -> int array
    cycleLimit     : int option     // per RENDER call, when the host has one
    load           : Compile.Compiled -> Result<Program, string> }

/// The MDS harness memory map: the firmware resources where the intrinsics' stand-ins say they are.
let layout =
  { Track.defaultLayout with
      sineTable = Rom.sine.address
      inputRing = Input.ring.address; inputPosition = Input.position.address
      mainRing = Main.ring.address; mainPosition = Main.position.address
      tempX = Scratch.x.address; tempY = Scratch.y.address
      trackMemory = Track.memory.address }

/// The stand-ins' contents into the emulator.
let loadResources (emu: Emulator) =
  emu.Load(Native.Space.X, Rom.sine.address, Rom.sine.words)
  emu.Load(Native.Space.X, Input.ring.address, Input.ring.words)
  emu.Load(Native.Space.X, Main.ring.address, Main.ring.words)
  emu.Write(Native.Space.X, Input.position.address, Input.position.value)
  emu.Write(Native.Space.X, Main.position.address, Main.position.value)

/// The import symbols of the stand-ins, at their addresses: a firmware map's `resources` for
/// kernels that read them, with `loadResources` as its setup.
let standIns =
  Map [ 1, Rom.sine.address; 2, Input.ring.address; 3, Input.position.address; 4, Main.ring.address
        5, Main.position.address; 6, Track.memory.address; 7, Scratch.x.address; 8, Scratch.y.address ]

let parameterWords (machine: Mds.Machine) (settings: float array) =
  settings |> Array.mapi (fun i s -> Track.convert machine.parameters[i] s)

/// The .NET side's shared temporary memory is one array per region, as on the DSP: kernels run
/// on .NET one at a time.
let sharedMemory = obj ()

let runNet (h: Harness) (k: Kernel.Kernel) (scenario: Scenario) =
  let target = h.target
  lock sharedMemory (fun () ->
  // shared temporary memory starts cleared, as the emulator's does
  Array.fill Scratch.x.words 0 Scratch.x.words.Length 0
  Array.fill Scratch.y.words 0 Scratch.y.words.Length 0
  Array.fill Track.memory.words 0 Track.memory.words.Length 0
  Track.current <- 0                    // the harness plays track 0
  let host = Host.Instance(target, k)
  let outputs, states = ResizeArray(), ResizeArray()
  for step in scenario.steps do
    match step with
    | Settings s -> host.SetParameters(h.settings s)
    | Words w -> host.SetParameters w
    | Mark _ -> ()
    | Init -> host.Init()
    | Trig -> host.Trig()
    | Render n ->
      for _ in 1 .. n do
        let words = host.Render()
        if words.Length <> target.blockWords then
          raise (Kernel.KernelError $"{k.name}: RENDER wrote {words.Length} words, the block is {target.blockText}")
        outputs.Add words
        states.Add(host.StateWords())
  { outputs = outputs.ToArray(); states = states.ToArray(); init = []; trig = []; render = [] })

/// A program on the emulator; `stateWords` Y words from Y:(r6+stateBase), then `xStateWords` X
/// words from X:(r6+0) (the [<XMemory>] state), are read after each block.
let runStates (h: Harness) (p: Program) (stateWords: int) (xStateWords: int) (scenario: Scenario) =
  use session = p.start ()
  let emu = session.Emulator
  let outputs, states = ResizeArray(), ResizeArray()
  let inits, trigs, renders = ResizeArray(), ResizeArray(), ResizeArray()
  for step in scenario.steps do
    match step with
    | Settings s -> session.SetParameters(h.settings s)
    | Words w -> session.SetParameters w
    | Mark _ -> ()
    | Init -> inits.Add(session.Init())
    | Trig -> trigs.Add(session.Trig())
    | Render n ->
      for _ in 1 .. n do
        let words, cycles = session.Render()
        outputs.Add words
        renders.Add cycles
        states.Add
          [| for k in 0 .. stateWords - 1 -> emu.Read(Native.Space.Y, session.Workset + h.target.stateBase + k)
             for k in 0 .. xStateWords - 1 -> emu.Read(Native.Space.X, session.Workset + k) |]
  { outputs = outputs.ToArray(); states = states.ToArray()
    init = List.ofSeq inits; trig = List.ofSeq trigs; render = List.ofSeq renders }

/// A program on the emulator; `stateWords` Y words from Y:(r6+stateBase) are read after each block.
let run (h: Harness) (p: Program) (stateWords: int) (scenario: Scenario) = runStates h p stateWords 0 scenario

// ---------------------------------------------------------------------------------------------
// The MDS harness: packages loaded the way the Machinedrum loader does it.

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
          yWorkset = compiled.yWorkset }
    match Mds.pack m words r.symbols None with
    | Error e -> Error $"%A{e}"
    | Ok bytes ->
      match Mds.read bytes with
      | Error e -> Error $"%A{e}"
      | Ok pkg -> Ok { compiled = compiled; machine = m; bytes = bytes; package = pkg }

/// A package on track 0, the stand-ins loaded.
let trackSession (package: Mds.Package) (machine: Mds.Machine) =
  let track = new Track.Track(package, Some machine, layout, Map.empty)
  loadResources track.Emulator
  { new Session with
      member _.Emulator = track.Emulator
      member _.Workset = layout.workset
      member _.SetParameters words = words |> Array.iteri (fun i w -> track.SetParameter(i, w))
      member _.Init() = track.Init 0
      member _.Trig() = track.Trig 0
      member _.Render() = track.Render 0
      member _.Dispose() = (track :> IDisposable).Dispose() }

let mdsProgram name (package: Mds.Package) (machine: Mds.Machine) (items: Item array) =
  { name = name; programWords = package.programWords.Length; xWorkset = machine.xWorkset; yWorkset = machine.yWorkset
    declaredCycles = machine.maxTrackCycles; items = items; start = fun () -> trackSession package machine }

/// MDS packages with `machine`'s identity and controls, on the Machinedrum track harness.
let mdsHarness (machine: Mds.Machine) =
  { name = "Machinedrum MDS"; target = Kernel.mds
    parameterNames = machine.parameters |> Array.map _.name
    settings = parameterWords machine
    cycleLimit = Some Abi.trackCycleLimit
    load = fun compiled -> build machine compiled |> Result.map (fun b -> mdsProgram compiled.kernel.name b.package b.machine compiled.items) }

/// A shipped package (.mds bytes, or one machine of a .syx) as a reference, disassembled.
let mdsReference (bytes: byte array) =
  match Mds.read bytes, Import.package bytes with
  | Ok pkg, Ok imported -> Ok(mdsProgram "reference" pkg imported.machine imported.items)
  | Error e, _ -> Error $"reference: %A{e}"
  | _, Error e -> Error $"reference: %A{e}"

// ---------------------------------------------------------------------------------------------
// Firmware harnesses: programs at absolute addresses, called the way a host program
// calls a routine. The firmware's words and tables come from the caller.

/// Where things are on the firmware's DSP.
type FirmwareMap =
  { memory     : int * int * int        // P, X, Y words of the emulator
    workset    : int                    // r6 on each call
    output     : int                    // r7 on each call: the RENDER block, in Y
    stub       : int                    // P: three free words for the jsr/stop call stub
    kernelBase : int                    // P: where a compiled kernel is loaded
    registers  : (int * int) list       // further registers on each call (emulator codes), after r6, r7, m0..m7 linear
    resources  : Map<int, int>          // MDS import symbol to address, for kernels reading firmware memory
    setup      : Emulator -> unit       // what both programs find in memory (ROM tables, ...), once per session
    maxCycles  : int }                  // per call

let firmwareMap =
  { memory = 0x10000, 0x10000, 0x10000; workset = 0x100; output = 0x200; stub = 0xff00; kernelBase = 0x1000
    registers = []; resources = Map.empty; setup = ignore; maxCycles = 2_000_000 }

/// A reference made of firmware routines: program words at absolute P addresses and the entry
/// points a host program calls.
type FirmwareImage =
  { name         : string
    program      : (int * int array) list                  // P words at absolute addresses
    data         : (Native.Space * int * int array) list   // words loaded after the program: tables, patches
    init         : int option                              // entry addresses (None: not called)
    trig         : int option
    render       : int
    programWords : int                                     // the routines' size, for the cost
    xWorkset     : int
    yWorkset     : int
    items        : Item array }                            // the routines, labelled INIT, TRIG, RENDER, for the shape

/// A listing of firmware routines for the shape: `words` (loaded at `origin`) disassembled from
/// the entry points, labelled with their names (INIT, TRIG, RENDER). Pass the routines' own span:
/// every word becomes an instruction or `dc`, and jumps out of the span stay addresses.
let disassemble (origin: int) (words: int array) (entries: (string * int) list) =
  let relative = entries |> List.map (fun (name, address) -> address - origin, name)
  let names = relative |> List.groupBy fst |> List.map (fun (pc, ns) -> pc, List.map snd ns) |> Map.ofList
  Disassembler.items words (relative |> List.map fst |> Array.ofList) (names |> Map.map (fun _ ns -> List.head ns)) Set.empty
  |> Array.collect (fun it ->
    match it with
    | Label n ->
      // the other names of the same address, as labels beside it
      match names |> Map.tryFindKey (fun _ ns -> List.head ns = n) with
      | Some pc -> names[pc] |> List.map Label |> Array.ofList
      | None -> [| it |]
    | _ -> [| it |])

/// An emulator with the map's memory, `load` run after the map's setup.
let firmwareSession (target: Kernel.Target) (map: FirmwareMap) (load: Emulator -> unit) (init: int option, trig: int option, render: int) =
  let p, x, y = map.memory
  let emu = new Emulator(p, x, y)
  map.setup emu
  load emu
  let call (entry: int option) =
    match entry with
    | None -> 0L
    | Some address ->
      emu.SetReg(22, map.workset)
      emu.SetReg(23, map.output)
      for m in 32 .. 39 do emu.SetReg(m, 0xffffff)
      for r, v in map.registers do emu.SetReg(r, v)
      match emu.Call(address, map.stub, map.maxCycles) with
      | Some c -> c
      | None -> failwith $"P:{address:x6} did not return within {map.maxCycles} cycles"
  { new Session with
      member _.Emulator = emu
      member _.Workset = map.workset
      member _.SetParameters words = words |> Array.iteri (fun i w -> emu.Write(Native.Space.Y, map.workset + target.paramBase + i, w))
      member _.Init() = call init
      member _.Trig() = call trig
      member _.Render() =
        let cycles = call (Some render)
        [| for i in 0 .. target.blockWords - 1 -> emu.Read(Native.Space.Y, map.output + i) |], cycles
      member _.Dispose() = (emu :> IDisposable).Dispose() }

/// The compiled kernel's words at `map.kernelBase`, relocated and its imports patched from
/// `map.resources`, and its entry addresses.
let place (map: FirmwareMap) (compiled: Compile.Compiled) =
  match Assembler.assembleItems compiled.items with
  | Error e -> Error(Assembler.errorText e)
  | Ok r ->
    let org, image = Assembler.image dsp56k.Space.P r
    let words = Array.copy image
    let at label delta = int r.symbols[label] - org + delta
    let missing =
      [ for patch in Abi.patches compiled.items do
          match patch with
          | Mds.Relocation(l, d) -> words[at l d] <- (words[at l d] + map.kernelBase) &&& 0xffffff
          | Mds.Import(l, d, kind, symbol) ->
            match map.resources.TryFind symbol with
            | Some address -> words[at l d] <- (words[at l d] + address) &&& 0xffffff
            | None -> yield $"import kind {kind} symbol {symbol}" ]
    if not missing.IsEmpty then Error $"""{compiled.kernel.name}: the firmware map has no address for {String.concat ", " (List.distinct missing)}"""
    else
      let entry name = map.kernelBase + int r.symbols[name] - org
      Ok(words, (entry "INIT", entry "TRIG", entry "RENDER"))

/// Compiled kernels on a firmware's DSP: placed at `map.kernelBase` and called the way a host
/// program calls a routine. Settings are refused until the caller gives `settings`.
let firmware (target: Kernel.Target) (map: FirmwareMap) =
  { name = target.name; target = target
    parameterNames = [||]
    settings = fun _ -> raise (Kernel.KernelError $"{target.name}: this harness has no settings conversion; give parameter words (Words), or the harness's settings")
    cycleLimit = None
    load = fun compiled ->
      place map compiled
      |> Result.map (fun (words, (init, trig, render)) ->
        { name = compiled.kernel.name; programWords = words.Length; xWorkset = compiled.xWorkset; yWorkset = compiled.yWorkset
          declaredCycles = 0; items = compiled.items
          start = fun () -> firmwareSession target map (fun emu -> emu.Load(Native.Space.P, map.kernelBase, words)) (Some init, Some trig, render) }) }

/// A firmware image as a reference on the same map.
let image (target: Kernel.Target) (map: FirmwareMap) (img: FirmwareImage) =
  { name = img.name; programWords = img.programWords; xWorkset = img.xWorkset; yWorkset = img.yWorkset
    declaredCycles = 0; items = img.items
    start = fun () ->
      let load (emu: Emulator) =
        for address, words in img.program do emu.Load(Native.Space.P, address, words)
        for space, address, words in img.data do emu.Load(space, address, words)
      firmwareSession target map load (img.init, img.trig, img.render) }

// ---------------------------------------------------------------------------------------------
// Shape: listings side by side.

let entries = [| "INIT"; "TRIG"; "RENDER" |]

let canonical (it: Item) =
  Regex.Replace(Print.item it, @"\b(L_[0-9a-f]+|K_[A-Z0-9_]+|IMPORT_[0-9a-f]+|RELOC_[0-9a-f]+)\b", "@").Trim()

let mnemonic (text: string) = (text.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)).[0]

/// The instructions under each entry label, in listing order. Entry labels with nothing between
/// them (INIT and TRIG at one address) share what follows.
let sections (items: Item array) =
  let mutable current = []
  let mutable adjacent = false
  let found = Collections.Generic.Dictionary<string, ResizeArray<string>>()
  for it in items do
    match it with
    | Label name when Array.contains name entries ->
      current <- if adjacent then name :: current else [ name ]
      adjacent <- true
      found[name] <- ResizeArray()
    | Op _ when not current.IsEmpty ->
      adjacent <- false
      for c in current do found[c].Add(canonical it)
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

let cost (p: Program) (r: Run) =
  let worst xs = if List.isEmpty xs then 0L else List.max xs
  { programWords = p.programWords; xWorkset = p.xWorkset; yWorkset = p.yWorkset
    init = worst r.init; trig = worst r.trig
    renderMean = (if r.render.IsEmpty then 0.0 else List.averageBy float r.render); renderWorst = worst r.render }

type Report =
  { kernel      : string
    scenario    : string
    steps       : Step list
    blocks      : int
    harness     : Harness
    compiled    : Compile.Compiled
    program     : Program                 // the compiled kernel, as the harness loads it
    net         : Run                     // the .NET kernel
    dsp         : Run                     // the compiled program on the emulator
    refRun      : Run option              // the reference on the emulator
    refItems    : Item array              // the reference's listing
    correctness : Mismatch list           // compiled vs .NET kernel
    fidelity    : Mismatch option option  // compiled vs reference (None: no reference)
    ours        : Cost
    reference   : Cost option
    shapes      : Shape array }

/// Compile for the harness's target, load, run and compare. `reference` is the program the
/// kernel reproduces, when there is one.
let gradeWith (h: Harness) (k: Kernel.Kernel) (reference: Program option) (scenario: Scenario) =
  let target = h.target
  Optimize.compile Optimize.Peephole target k
  |> Result.bind (fun compiled -> h.load compiled |> Result.map (fun p -> compiled, p))
  |> Result.bind (fun (compiled, program) ->
    try
      let stateWords = compiled.layout.yWorkset - target.stateBase
      let net = runNet h k scenario
      let dsp = runStates h program stateWords compiled.layout.xWorkset scenario
      let correctness = [ compare "output" net.outputs dsp.outputs; compare "state" net.states dsp.states ] |> List.choose id
      let refRun = reference |> Option.map (fun r -> run h r 0 scenario)
      let shapes =
        match reference with
        | Some r when r.items.Length > 0 ->
          let theirs = sections r.items |> Map.ofArray
          sections compiled.items |> Array.map (fun (e, o) -> align e o (theirs.TryFind e |> Option.defaultValue [||]))
        | _ -> [||]
      Ok { kernel = k.name; scenario = scenario.name; steps = scenario.steps; blocks = dsp.outputs.Length
           harness = h; compiled = compiled; program = program
           net = net; dsp = dsp; refRun = refRun; refItems = (reference |> Option.map _.items |> Option.defaultValue [||])
           correctness = correctness
           fidelity = refRun |> Option.map (fun r -> compare "output" r.outputs dsp.outputs)
           ours = cost program dsp
           reference = Option.map2 cost reference refRun
           shapes = shapes }
    with Kernel.KernelError m -> Error m)

/// On the MDS harness; `reference` is a shipped package, when there is one.
let grade (k: Kernel.Kernel) (machine: Mds.Machine) (reference: byte array option) (scenario: Scenario) =
  let reference =
    match reference with
    | None -> Ok None
    | Some bytes -> mdsReference bytes |> Result.map Some
  reference |> Result.bind (fun r -> gradeWith (mdsHarness machine) k r scenario)

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

/// The tool's commands over the libraries: Dsp56k (assembler, parser, printer), Dsp56k.Native
/// (oracle, emulator), Dsp56k.Dsl (DSL source), Machinedrum (packages, SysEx) and
/// Machinedrum.Run (binary import, track harness). Each returns an exit code.
module dsp56k.tool.Commands

open System
open System.IO
open Argu
open dsp56k
open machinedrum
open machinedrum.run
open dsp56k.tool.Cli

let (</>) (a: string) (b: string) = Path.Combine(a, b)

let extension (path: string) = (string (Path.GetExtension path)).ToLowerInvariant()
let stem (path: string) = string (Path.GetFileNameWithoutExtension path)

let fail (message: string) =
  eprintfn "error: %s" message
  1

let hex (ws: int array) = ws |> Array.map (fun w -> $"{w:x6}") |> String.concat " "

let defines (values: string list) =
  values
  |> List.map (fun nv ->
    match nv.Split('=', 2) with
    | [| n; v |] when v.StartsWith "$" -> n, Convert.ToInt64(v[1..], 16)
    | [| n; v |] -> n, int64 v
    | _ -> nv, 1L)
  |> Map.ofList

let assemble (includes: string list) (defs: string list) (path: string) =
  Assembler.assembleFile (Array.ofList includes) (defines defs) path |> Result.mapError Assembler.errorText

let write (output: string option) (text: string) =
  match output with
  | Some path -> File.WriteAllText(path, text)
  | None -> stdout.Write text

let listingText (r: Assembler.Assembled) =
  [| for l in r.listing do
       let ws = hex l.words
       if l.words.Length > 0 then $"{Print.space l.space}{l.address:x4} {ws,-14} {l.text}"
       else $"{String(' ', 21)} {l.text}" |]
  |> String.concat "\n"

// ---------------------------------------------------------------------------------------------
// Pure DSP56300.

let asm (args: ParseResults<AsmArgs>) =
  match assemble (args.GetResults AsmArgs.Include) (args.GetResults AsmArgs.Define) (args.GetResult AsmArgs.Source) with
  | Error e -> fail e
  | Ok r ->
    write (args.TryGetResult AsmArgs.Listing) (listingText r + "\n")
    match args.TryGetResult AsmArgs.Hex with
    | Some path ->
      let org, words = Assembler.image Space.P r
      File.WriteAllLines(path, [| $"@{org:x4}"; yield! words |> Array.map (fun w -> $"{w:x6}") |])
    | None -> ()
    0

/// Every P instruction word as the oracle reads it: it must decode to the same length and its
/// own text must assemble back to the same words (long immediates kept long).
let check (args: ParseResults<CheckArgs>) =
  match assemble (args.GetResults CheckArgs.Include) (args.GetResults CheckArgs.Define) (args.GetResult CheckArgs.Source) with
  | Error e -> fail e
  | Ok r ->
    let lines =
      r.listing |> Array.filter (fun l ->
        l.space = Space.P && l.words.Length > 0
        && (match Parse.parseLine 0 l.text with Ok { body = Parse.Instruction _ } -> true | _ -> false))
    let failures =
      [| for l in lines do
           let next = if l.words.Length > 1 then l.words[1] else 0
           let text, n = Native.disassemble l.address l.words[0] next
           // the oracle prints a long immediate without `>` when the short form would hold it
           let forcedLong () = Native.assembleLine l.address (Native.oracleSpelling (text.Replace("#", "#>")))
           match Native.assembleLine l.address (Native.oracleSpelling text) with
           | Ok ws when n = l.words.Length && ws = l.words -> ()
           | Ok _ when n = l.words.Length && forcedLong () = Ok l.words -> ()
           | Ok ws -> $"p:{l.address:x4} {hex l.words,-14} {l.text.Trim()}\n           oracle: {text} -> {hex ws}"
           | Error(Native.AssembleLineError e) -> $"p:{l.address:x4} {hex l.words,-14} {l.text.Trim()}\n           oracle: {text} ({e})" |]
    for f in failures do printfn "%s" f
    printfn "%d instructions, %d disagree with the oracle" lines.Length failures.Length
    if failures.Length = 0 then 0 else 1

// ---------------------------------------------------------------------------------------------
// MDS packages.

/// The packages in a .mds or .syx file (name, bytes).
let packages (path: string) =
  let bytes = File.ReadAllBytes path
  if extension path = ".syx" then
    Sysex.packages bytes
    |> Result.map (Array.map (fun (slot, p) -> $"{stem path}-slot{slot}", slot, p))
    |> Result.mapError (sprintf "%A")
  else Ok [| stem path, 0, bytes |]

let onePackage (path: string) (slot: int option) =
  packages path
  |> Result.bind (fun ps ->
    match slot, ps with
    | None, [| p |] -> Ok p
    | None, ps -> Error $"""{path} holds {ps.Length} machines (slots {ps |> Array.map (fun (_, s, _) -> string s) |> String.concat ", "}): pick one with --slot"""
    | Some s, ps ->
      match ps |> Array.tryFind (fun (_, slot, _) -> slot = s) with
      | Some p -> Ok p
      | None -> Error $"no machine for slot {s} in {path}")

let readPackage bytes = Mds.read bytes |> Result.mapError (sprintf "%A")

let disasm (args: ParseResults<DisasmArgs>) =
  match onePackage (args.GetResult DisasmArgs.Input) (args.TryGetResult DisasmArgs.Slot) |> Result.bind (fun (_, _, p) -> Import.package p |> Result.mapError (sprintf "%A")) with
  | Error e -> fail e
  | Ok imported ->
    printfn "%s" (Print.program imported.items)
    0

let moduleName (path: string) =
  let raw = stem path
  let cleaned = String(raw |> Seq.map (fun c -> if Char.IsLetterOrDigit c then c else '_') |> Seq.toArray)
  if cleaned.Length > 0 && Char.IsLetter cleaned[0] then string (Char.ToUpperInvariant cleaned[0]) + cleaned[1..] else "M" + cleaned

let machineHeader (m: Mds.Machine) =
  [| $"{m.displayName} ({m.id}), category {m.category}, max {m.maxTrackCycles} cycles per track"
     for i, p in Array.indexed m.parameters do
       $"  param {i + 1}: {p.name,-4} {p.transform} default {p.defaultValue} range {p.minValue}..{p.maxValue}" |]

let import (args: ParseResults<ImportArgs>) =
  let input = args.GetResult ImportArgs.Input
  let name = args.GetResult(ImportArgs.Module, moduleName input)
  let output = args.TryGetResult ImportArgs.Output
  let header = [| $"Imported from {Path.GetFileName input} by fsdsp56k import." |]
  match extension input with
  | ".mds" | ".syx" ->
    match onePackage input (args.TryGetResult ImportArgs.Slot) |> Result.bind (fun (_, _, p) -> Import.package p |> Result.mapError (sprintf "%A")) with
    | Error e -> fail e
    | Ok imported ->
      write output (dsl.Transcribe.moduleSource name (Array.append header (machineHeader imported.machine)) imported.items)
      0
  | _ ->
    match dsl.Import.file (Array.ofList (args.GetResults ImportArgs.Include)) (defines (args.GetResults ImportArgs.Define)) input with
    | Error e -> fail (Assembler.errorText e)
    | Ok sources ->
      write output (dsl.Import.source name header sources)
      0

let mdsInfo (args: ParseResults<MdsInfoArgs>) =
  match packages (args.GetResult MdsInfoArgs.Input) with
  | Error e -> fail e
  | Ok ps ->
    for name, _, bytes in ps do
      match readPackage bytes with
      | Error e -> printfn "%s: %s" name e
      | Ok pkg ->
        let m = Mds.machineOf pkg bytes
        let verified = match Mds.verify bytes with Ok() -> "CRCs and content ID verify" | Error e -> $"%A{e}"
        printfn "%s: %d bytes, %s" name bytes.Length verified
        for line in machineHeader m do printfn "  %s" line
        printfn "  program %d words, INIT $%04x TRIG $%04x RENDER $%04x, workset X %d Y %d" pkg.programWords.Length pkg.init pkg.trig pkg.render pkg.xWorkset pkg.yWorkset
        printfn "  relocations %s" (pkg.relocations |> Array.map (fun w -> $"${w:x4}") |> String.concat " ")
        for w, kind, symbol in pkg.imports do printfn "  import $%04x kind %d symbol %d" w kind symbol
    0

let mdsPack (args: ParseResults<MdsPackArgs>) =
  let result =
    Mds.fromJson (File.ReadAllText(args.GetResult MdsPackArgs.Json)) |> Result.mapError (sprintf "%A")
    |> Result.bind (fun m ->
      assemble (args.GetResults MdsPackArgs.Include) (args.GetResults MdsPackArgs.Define) (args.GetResult MdsPackArgs.Source)
      |> Result.bind (fun r ->
        let _, words = Assembler.image Space.P r
        let sprite = args.TryGetResult MdsPackArgs.Sprite |> Option.map (File.ReadAllText >> Mds.spriteOfPbm)
        Mds.pack m words r.symbols sprite |> Result.mapError (sprintf "%A")))
  match result with
  | Error e -> fail e
  | Ok bytes ->
    File.WriteAllBytes(args.GetResult MdsPackArgs.Output, bytes)
    printfn "%s: %d bytes" (args.GetResult MdsPackArgs.Output) bytes.Length
    0

let mdsSyx (args: ParseResults<MdsSyxArgs>) =
  let bytes = File.ReadAllBytes(args.GetResult MdsSyxArgs.Input)
  match Mds.verify bytes with
  | Error e -> fail $"%A{e}"
  | Ok() ->
    File.WriteAllBytes(args.GetResult MdsSyxArgs.Output, Sysex.ofPackage (args.GetResult(MdsSyxArgs.Slot, 1)) bytes)
    0

let mdsUnsyx (args: ParseResults<MdsUnsyxArgs>) =
  match packages (args.GetResult MdsUnsyxArgs.Input) with
  | Error e -> fail e
  | Ok ps ->
    let dir = args.GetResult(MdsUnsyxArgs.Output, ".")
    Directory.CreateDirectory dir |> ignore
    for name, _, bytes in ps do
      File.WriteAllBytes(dir </> (name + ".mds"), bytes)
      printfn "%s" (dir </> (name + ".mds"))
    0

let machinedrum (args: ParseResults<MachinedrumArgs>) =
  match args.GetSubCommand() with
  | Info a -> mdsInfo a
  | Pack a -> mdsPack a
  | Syx a -> mdsSyx a
  | Unsyx a -> mdsUnsyx a

// ---------------------------------------------------------------------------------------------
// The track harness.

/// A WAV as the internal and userwave sample banks, or none.
let sampleBanks (wav: string option) =
  match wav with
  | Some wav ->
    let words = fst (Wav.read wav) |> Array.truncate 0x18000
    let bank : Track.Bank = { words = words; step = 0x10000; origin = 0x4000 }
    Map.ofList [ 0, bank; 1, bank ]
  | None -> Map.empty

/// A machine from a .mds or .syx: its name, package and definition.
let loadMachine (path: string) (slot: int option) =
  onePackage path slot
  |> Result.bind (fun (name, _, p) -> readPackage p |> Result.map (fun pkg -> name, pkg, Mds.machineOf pkg p))

/// How run, stress and audition pick their machine: the host, and for F# source the kernel and
/// definition; a package's slot.
type Choice = { host: string option; slot: int option; kernel: string option; json: string option; defines: string list }

/// A machine for the chosen host from a package, or from F# source (built and compiled here); and
/// whether it came from source, so it runs on the harness with the grade's stand-ins.
let load (choice: Choice) (path: string) =
  Kernels.host (defaultArg choice.host "machinedrum")
  |> Result.bind (fun host ->
    match host with
    | dsp56k.kernel.Kernel.KernelHost.Machinedrum ->
      if Kernels.isSource path then Kernels.load host path choice.defines choice.kernel choice.json |> Result.map (fun m -> m, true)
      else loadMachine path choice.slot |> Result.map (fun m -> m, false))

/// A machine on the track harness with the grade's stand-ins (the sine ROM, the input and main
/// rings) loaded, so machines that read them make their sound.
let harnessTrack (pkg: Mds.Package) (machine: Mds.Machine) banks =
  let track = new Track.Track(pkg, Some machine, Track.clearOfProgram dsp56k.kernel.Grade.layout pkg.programWords.Length, banks)
  dsp56k.kernel.Grade.loadResources track.Emulator
  track

/// The track a loaded machine plays on: a kernel built from source on the grade's harness, which its
/// layout assumes; a package on the default track, as before.
let trackFor fromSource (pkg: Mds.Package) (machine: Mds.Machine) banks =
  if fromSource then harnessTrack pkg machine banks else new Track.Track(pkg, Some machine, Track.defaultLayout, banks)

let run (args: ParseResults<RunArgs>) =
  let choice = { host = args.TryGetResult RunArgs.Host; slot = args.TryGetResult RunArgs.Slot; kernel = args.TryGetResult RunArgs.Kernel; json = args.TryGetResult RunArgs.Json; defines = args.GetResults RunArgs.Define }
  match load choice (args.GetResult RunArgs.Input) with
  | Error e -> fail e
  | Ok((name, pkg, machine), fromSource) ->
    let settings =
      match args.TryGetResult RunArgs.Settings with
      | Some s -> s.Split ',' |> Array.map float
      | None -> machine.parameters |> Array.map (fun p -> float p.defaultValue)
    let banks = sampleBanks (args.TryGetResult RunArgs.Sample)
    use track = trackFor fromSource pkg machine banks
    track.SetSettings settings
    let blocks = args.GetResult(RunArgs.Blocks, 1378)
    let every = args.TryGetResult RunArgs.Trig_Every
    if args.Contains RunArgs.Profile then
      let report what (cost: Track.Cost, counts: uint32 array) =
        printfn "%-6s own %5d cycles, %d SAMPLE_INFO, %d SAMPLE_READ (%d frames), budget view %d" what cost.own cost.infoCalls cost.readCalls cost.readFrames cost.budget
        let hot =
          counts
          |> Array.indexed
          |> Array.filter (fun (pc, c) -> c > 0u && pc >= track.Layout.programBase && pc < track.Layout.programBase + pkg.programWords.Length)
          |> Array.sortByDescending snd
          |> Array.truncate 8
        for pc, c in hot do
          let local = pc - track.Layout.programBase
          let text, _ = Native.disassemble local pkg.programWords[local] (if local + 1 < pkg.programWords.Length then pkg.programWords[local + 1] else 0)
          printfn "         $%04x %6d  %s" local c text
      report "INIT" (track.CallProfiled(pkg.init, 0))
      report "TRIG" (track.CallProfiled(pkg.trig, 0))
      report "RENDER" (track.CallProfiled(pkg.render, 0))
    track.Init 0 |> ignore
    let output = ResizeArray<int>()
    let renderCycles = ResizeArray<int64>()
    for b in 0 .. blocks - 1 do
      let trig = b = 0 || (every |> Option.exists (fun n -> n > 0 && b % n = 0))
      let t = if trig then track.Trig 0 else 0L
      let words, cycles = track.Render 0
      output.AddRange words
      renderCycles.Add(cycles + t)
    let worst = Seq.max renderCycles
    printfn "%s: %d blocks, TRIG+RENDER cycles mean %.0f, worst %d (declared %d, limit %d; service stand-ins included)" name blocks (Seq.averageBy float renderCycles) worst pkg.maxCycles Abi.trackCycleLimit
    let peak = output |> Seq.map (fun w -> abs (Track.toFloat w)) |> Seq.max
    printfn "peak %.3f" peak
    match args.TryGetResult RunArgs.Wav with
    | Some path ->
      Wav.write path 44100 (output.ToArray())
      printfn "wrote %s" path
    | None -> ()
    0

let parseSweep (how: string) (seed: int) =
  match how.Split ':' with
  | [| "exhaustive" |] -> Ok Stress.Exhaustive
  | [| "random"; n |] ->
    match System.Int32.TryParse n with
    | true, n when n > 0 -> Ok(Stress.RandomCases(n, seed))
    | _ -> Error $"random:N needs a positive count, not '{n}'"
  | _ -> Error $"unknown sweep '{how}': exhaustive or random:N"

let parsePhase (name: string) =
  match name.ToLowerInvariant() with
  | "worst"       -> Ok Stress.AnyPhase
  | "peak"        -> Ok Stress.Peak
  | "rms"         -> Ok Stress.Rms
  | "init"        -> Ok(Stress.OnePhase Stress.Init)
  | "trig"        -> Ok(Stress.OnePhase Stress.Trig)
  | "render"      -> Ok(Stress.OnePhase Stress.Render)
  | "trig+render" -> Ok(Stress.OnePhase Stress.TrigBlock)
  | other         -> Error $"unknown phase '{other}': init, trig, render, trig+render, worst, peak or rms"

let parseSearch (how: string) =
  match how.Split ':' with
  | [| "anneal"; n |] ->
    match System.Int32.TryParse n with
    | true, n when n > 0 -> Ok n
    | _ -> Error $"anneal:N needs a positive count, not '{n}'"
  | _ -> Error $"unknown search '{how}': anneal:N"

/// One machine's stress: its cases measured by `evaluate` (a sweep, or the annealed search), the
/// table printed; the worst case of each phase returned with whether the trig block stays within.
let stressMachine name (pkg: Mds.Package) (machine: Mds.Machine) (evaluate: float array -> Stress.Case) (sweep: Result<Stress.Sweep, (int * int * Stress.Objective)>) (maxCases: int) blocks =
  let cs = Stress.controls machine
  let cases =
    match sweep with
    | Ok sweep ->
      let settings = Stress.settingsOf cs sweep
      let picked = Stress.sample maxCases settings
      let how = match sweep with Stress.Exhaustive -> "exhaustive" | Stress.RandomCases(n, seed) -> $"random:{n}, seed {seed}"
      let sampled = if picked.Length < settings.Length then $" of {settings.Length}" else ""
      printfn "%s: %d cases%s (%s), %d RENDER blocks each; cycles JSR to STOP, service stand-ins included" name picked.Length sampled how blocks
      picked |> List.map evaluate
    | Error(budget, seed, objective) ->
      let found = Stress.anneal cs evaluate objective budget seed
      printfn "%s: anneal:%d, seed %d, maximising %s: %d measurements (the exhaustive sweep: %d; every setting: %A); %d RENDER blocks each" name budget seed (Stress.objectiveName objective) found.evaluations (Stress.exhaustiveSettings cs).Length (Stress.gridSize cs) blocks
      match objective with
      | Stress.Peak | Stress.Rms -> printfn "loudest found: peak %.4f, RMS %.4f at %s" found.best.peak found.best.rms (Stress.settingsText cs found.best.settings)
      | _ -> printfn "worst found: %d cycles at %s" (Stress.score objective found.best) (Stress.settingsText cs found.best.settings)
      found.cases
  let lines, within = Stress.report cs pkg.maxCycles cases
  for l in lines do printfn "%s" l
  cases, within

let stress (args: ParseResults<StressArgs>) =
  let seed = args.GetResult(StressArgs.Seed, 1)
  let plan =
    match args.TryGetResult StressArgs.Search with
    | Some how ->
      match parseSearch how, parsePhase (args.GetResult(StressArgs.Phase, "worst")) with
      | Error e, _ | _, Error e -> Error e
      | Ok budget, Ok objective -> Ok(Error(budget, seed, objective))
    | None -> parseSweep (args.GetResult(StressArgs.Sweep, "exhaustive")) seed |> Result.map Ok
  match plan with
  | Error e -> fail e
  | Ok plan ->
    let listening = args.Contains StressArgs.Audition
    let choice = { host = args.TryGetResult StressArgs.Host; slot = args.TryGetResult StressArgs.Slot; kernel = args.TryGetResult StressArgs.Kernel; json = args.TryGetResult StressArgs.Json; defines = args.GetResults StressArgs.Define }
    let rate = 44100
    let caseSeconds = args.GetResult(StressArgs.Case_Seconds, 0.75)
    let blocks = if listening then max 1 (int (caseSeconds * float rate / 32.0)) else args.GetResult(StressArgs.Blocks, 16)
    let trigEvery = args.TryGetResult StressArgs.Trig_Every |> Option.orElse (if listening then Some 344 else None)
    let maxCases = args.GetResult(StressArgs.Max_Cases, if listening then 40 else 0)
    let player = if listening then Some(new dsp56k.audio.Output.Player(rate, rate / 2)) else None
    use _closePlayer = { new IDisposable with member _.Dispose() = player |> Option.iter (fun p -> p.Drain(TimeSpan.FromSeconds 2.0); (p :> IDisposable).Dispose()) }
    player |> Option.iter (fun p -> if p.SampleRate <> rate then eprintfn "warning: the output plays at %d Hz, not %d: pitch off by %.3f" p.SampleRate rate (float p.SampleRate / float rate))
    let shaper = Audition.Shaper(args.GetResult(StressArgs.Gain, 0.35), 0.5)
    let session = ResizeArray<float>()
    let mutable stopped = false
    let onCancel = ConsoleCancelEventHandler(fun _ e -> e.Cancel <- true; stopped <- true)
    Console.CancelKeyPress.AddHandler onCancel
    try
      let summaries =
        [ for input in args.GetResult StressArgs.Inputs do
            if not stopped then
              match load choice input with
              | Error e -> eprintfn "error: %s" e
              | Ok((name, pkg, machine), _) ->
                printfn "\n== %s: %s (%s), declared %d cycles, limit %d" name machine.displayName machine.id pkg.maxCycles Abi.trackCycleLimit
                use track = harnessTrack pkg machine (sampleBanks (args.TryGetResult StressArgs.Sample))
                let cs = Stress.controls machine
                let evaluate =
                  match player with
                  | None -> Stress.measureWith track blocks trigEvery ignore
                  | Some p ->
                    let mutable count = 0
                    let mutable worst = Map.empty<Stress.Phase, int64>
                    fun settings ->
                      if stopped then Stress.measure track 1 settings
                      else
                        let frames = ResizeArray<float>()
                        let case = Stress.measureWith track blocks trigEvery (fun words -> for w in words do frames.Add(Track.toFloat w)) settings
                        count <- count + 1
                        let fresh =
                          [ for phase in Stress.phases do
                              let c = Stress.cycles phase case
                              match worst.TryFind phase with
                              | Some w when c <= w -> ()
                              | previous ->
                                worst <- worst.Add(phase, c)
                                if previous.IsSome then yield Stress.phaseName phase ]
                        let flag = if fresh.IsEmpty then "" else $"""  NEW WORST {String.concat ", " fresh}"""
                        printfn "  [%3d] INIT %4d TRIG %4d RENDER %5d T+R %5d  %s%s" count case.init case.trig case.render case.trigBlock (Stress.settingsText cs settings) flag
                        let heard = Audition.shapeCase shaper 220 (rate / 20) (frames.ToArray())
                        session.AddRange heard
                        p.Write(heard |> Array.map float32)
                        case
                let cases, within = stressMachine name pkg machine evaluate plan maxCases blocks
                yield name, cs, cases, pkg.maxCycles, within ]
      if summaries.Length > 1 then
        printfn "\nsummary (limit %d; T+R judged against the declared figure and the limit)" Abi.trackCycleLimit
        printfn "%-18s %5s %5s %5s %6s %6s %8s %7s  %s" "machine" "cases" "INIT" "TRIG" "RENDER" "T+R" "declared" "peak" "settings at the worst T+R"
        for name, cs, cases, declared, within in summaries do
          let worst phase = cases |> List.map (Stress.cycles phase) |> List.max
          let at = cases |> List.maxBy (Stress.cycles Stress.TrigBlock)
          printfn "%-18s %5d %5d %5d %6d %6d %8d %7.4f  %s%s" name cases.Length (worst Stress.Init) (worst Stress.Trig) (worst Stress.Render) (worst Stress.TrigBlock) declared (cases |> List.map _.peak |> List.max) (Stress.settingsText cs at.settings) (if within then "" else "  OVER")
      match args.TryGetResult StressArgs.Wav with
      | Some path when session.Count > 0 ->
        Wav.write path rate (session.ToArray() |> Array.map (fun x -> int (Math.Round(max -1.0 (min 1.0 x) * 8388607.0)) &&& 0xffffff))
        printfn "wrote %s (%.1f s)" path (float session.Count / float rate)
      | _ -> ()
      if summaries |> List.forall (fun (_, _, _, _, within) -> within) then 0 else 1
    finally
      Console.CancelKeyPress.RemoveHandler onCancel

// ---------------------------------------------------------------------------------------------
// Live output.

let midiList () =
  for what, ports in [ "inputs", dsp56k.audio.Midi.inputs (); "outputs", dsp56k.audio.Midi.outputs () ] do
    printfn "MIDI %s:%s" what (if Array.isEmpty ports then " none" else "")
    for i, name in Array.indexed ports do printfn "%4d  %s" i name
  0

let audition (args: ParseResults<AuditionArgs>) =
  match args.Contains AuditionArgs.Midi_List, args.TryGetResult AuditionArgs.Input with
  | true, _ -> midiList ()
  | false, None -> fail "audition needs a machine: .mds, .syx, .fsproj, .fsx or .fs (or --midi-list)"
  | false, Some input ->
  let choice = { host = args.TryGetResult AuditionArgs.Host; slot = args.TryGetResult AuditionArgs.Slot; kernel = args.TryGetResult AuditionArgs.Kernel; json = args.TryGetResult AuditionArgs.Json; defines = args.GetResults AuditionArgs.Define }
  match load choice input with
  | Error e -> fail e
  | Ok((name, pkg, machine), fromSource) ->
    let settings =
      match args.TryGetResult AuditionArgs.Settings with
      | Some s -> s.Split ',' |> Array.map float
      | None -> machine.parameters |> Array.map (fun p -> float p.defaultValue)
    let trackNumber = args.GetResult(AuditionArgs.Track, 1)
    let baseChannel = args.GetResult(AuditionArgs.Base_Channel, 1)
    if trackNumber < 1 || trackNumber > 16 then failwith "--track is 1..16"
    use track = trackFor fromSource pkg machine (sampleBanks (args.TryGetResult AuditionArgs.Sample))
    track.SetSettings settings
    track.Init 0 |> ignore
    let midiOut =
      match args.TryGetResult AuditionArgs.Midi_Out with
      | Some port -> Some(new dsp56k.audio.Midi.Output(port))
      | None -> None
    let voice, finish =
      match midiOut with
      | Some out ->
        let mirrored, finish = Audition.mirror out baseChannel trackNumber (Audition.ofTrack track machine)
        // the settings it starts from, as CCs
        settings |> Array.iteri (fun i s -> if i < 8 then mirrored.parameter i s)
        mirrored, finish
      | None -> Audition.ofTrack track machine, ignore
    use _closeMidiOut = { new IDisposable with member _.Dispose() = finish (); midiOut |> Option.iter (fun m -> (m :> IDisposable).Dispose()) }
    let midi =
      match args.TryGetResult AuditionArgs.Midi_In with
      | Some port -> Some(new dsp56k.audio.Midi.Input(port))
      | None -> None
    use _closeMidi = { new IDisposable with member _.Dispose() = midi |> Option.iter (fun m -> (m :> IDisposable).Dispose()) }
    let between =
      match midi with
      | Some m -> fun () -> for msg in m.Poll() do Audition.respond voice baseChannel trackNumber msg |> ignore
      | None -> ignore
    let rate = 44100
    // a quarter second of ring: latency against underruns
    // TODO: with --midi-in a shorter ring (lower latency) would play better
    use player = new dsp56k.audio.Output.Player(rate, rate / 4)
    if player.SampleRate <> rate then
      // TODO: resample; until then the pitch is off by the ratio
      eprintfn "warning: the output plays at %d Hz, not %d: pitch off by %.3f" player.SampleRate rate (float player.SampleRate / float rate)
    let seconds = args.TryGetResult AuditionArgs.Seconds |> Option.defaultValue (if midi.IsSome then infinity else 4.0)
    let blocks = if Double.IsInfinity seconds then Int32.MaxValue else int (seconds * float rate / 32.0)
    let mutable stopped = false
    let onCancel = System.ConsoleCancelEventHandler(fun _ e -> e.Cancel <- true; stopped <- true)
    System.Console.CancelKeyPress.AddHandler onCancel
    try
      let length = if Double.IsInfinity seconds then "until Ctrl+C" else sprintf "%.1f s" seconds
      let listening = match args.TryGetResult AuditionArgs.Midi_In with Some p -> $"; MIDI from {p}, track {trackNumber} (note {Audition.MidiMap.note trackNumber}, channel {Audition.MidiMap.channel baseChannel trackNumber + 1})" | None -> ""
      printfn "%s: playing %s at %d Hz, %d channel(s)%s; Ctrl+C stops" name length player.SampleRate player.Channels listening
      let played = Audition.play player.Write voice blocks (args.TryGetResult AuditionArgs.Trig_Every) between (fun () -> stopped)
      player.Drain(System.TimeSpan.FromSeconds 1.0)
      printfn "played %d blocks, %d trigs, %d underrun frames" played.blocks played.trigs player.Underruns
      0
    finally
      System.Console.CancelKeyPress.RemoveHandler onCancel

/// The scenario `grade` plays: two bars at the defaults, four trigs a bar, then each control swept
/// from its default and the pairwise combinations (the grade's own sweeps).
let gradeScenario (machine: Mds.Machine) =
  let cs = Stress.controls machine
  let defaults = cs |> Array.map _.standard
  let pattern : dsp56k.kernel.Grade.Pattern =
    { name = "the defaults, four trigs a bar"; bpm = 120.0; names = cs |> Array.map _.name
      bars = [ for _ in 1 .. 2 -> { values = dsp56k.kernel.Grade.Settings defaults; trigs = [ 0, []; 4, []; 8, []; 12, [] ] } ] }
  dsp56k.kernel.Grade.play dsp56k.kernel.Kernel.mds.frames (dsp56k.kernel.Grade.exhaustive cs dsp56k.kernel.Grade.Settings pattern)

/// `grade`: an F# kernel compiled and run on the emulator against the same F# run as .NET code, word
/// for word, with its costs; then each hand-written helper in the kernel (the RENDER cycles with its
/// F# compiled instead) and on its own (its routine against its F#, call by call). Exit code 1 when
/// anything differs.
let grade (args: ParseResults<GradeArgs>) =
  let layerRes =
    match args.TryGetResult GradeArgs.Lint_Layer with
    | None -> Ok None
    | Some s ->
      match dsp56k.kernel.Ir.Layer.tryParse s with
      | Some l -> Ok(Some l)
      | None -> Error $"unknown --lint-layer '{s}': expected ir, asm or both"
  match layerRes with
  | Error e -> fail e
  | Ok lintLayer ->
  let source = args.GetResult GradeArgs.Input
  match Kernels.kernelAndMachine source (args.GetResults GradeArgs.Define) (args.TryGetResult GradeArgs.Kernel) (args.TryGetResult GradeArgs.Json) with
  | Error e -> fail e
  | Ok(k, machine) ->
    let scenario = gradeScenario machine
    let harness = dsp56k.kernel.Grade.mdsHarness machine
    let graded handOff = dsp56k.kernel.Grade.gradeAtWith handOff dsp56k.kernel.Optimize.Peephole harness k None scenario
    match graded Set.empty with
    | Error e when e.Contains " threw " && not (args.Contains GradeArgs.Json) ->
      // the machine made from the fields gives each a unipolar word; a kernel reading raw settings
      // (128 x the front-panel value, as the TX6 ports do) indexes its tables past their ends
      fail $"{e}\nthe machine here is one made from the parameter fields, each a unipolar control (a Q23 word up to 7fffff); a kernel that reads its parameters as other words needs its machine definition: --json"
    | Error e -> fail e
    | Ok report ->
      printfn "%s" (dsp56k.kernel.Grade.text report)
      args.TryGetResult GradeArgs.Listing |> Option.iter (fun path -> File.WriteAllText(path, dsp56k.dsl.Program.source report.compiled.items))
      match dsp56k.kernel.HandCheck.inKernel graded report k with
      | Error e -> fail e
      | Ok inside ->
        let alone = dsp56k.kernel.HandCheck.alone harness.target (args.GetResult(GradeArgs.Calls, 1024)) (args.GetResult(GradeArgs.Seed, 1)) k
        if not (inside.IsEmpty && alone.IsEmpty) then printfn "\n%s" (dsp56k.kernel.HandCheck.text inside alone)
        let same =
          report.correctness.IsEmpty
          && inside |> List.forall _.correct
          && alone |> List.forall (fun a -> a.skipped.IsSome || (a.hand = a.arguments && a.compiled |> Option.forall ((=) a.arguments)))
        let kudosOn = args.Contains GradeArgs.Kudos
        let directCycles =
          if not kudosOn then None
          else
            match dsp56k.kernel.Grade.gradeAtWith Set.empty (dsp56k.kernel.Optimize.Chosen dsp56k.kernel.Optimize.Passes.none) harness k None scenario with
            | Ok dr -> Some(float (dr.ours.trig + dr.ours.renderWorst))
            | Error _ -> None
        let netAlloc = if kudosOn then Some(dsp56k.kernel.Lint.measureNetAllocation harness.target k) else None
        let lintOpts : dsp56k.kernel.Lint.Options =
          { layer = lintLayer
            kudos = kudosOn
            bitExact = same
            directCycles = directCycles
            optimisedCycles = Some(float (report.ours.trig + report.ours.renderWorst))
            declaredBudget = Some machine.maxTrackCycles
            netAllocatedBytes = netAlloc }
        match dsp56k.kernel.Lint.checkKernelWith lintOpts harness.target k with
        | Ok lintReport ->
          let text = dsp56k.kernel.Lint.formatReport lintOpts lintReport
          if text <> "" then printfn "\n%s" text
        | Error _ -> ()
        if same then 0 else 1

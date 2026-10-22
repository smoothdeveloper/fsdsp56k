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

let mds (args: ParseResults<MdsArgs>) =
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

let run (args: ParseResults<RunArgs>) =
  match onePackage (args.GetResult RunArgs.Input) (args.TryGetResult RunArgs.Slot) |> Result.bind (fun (name, _, p) -> readPackage p |> Result.map (fun pkg -> name, p, pkg)) with
  | Error e -> fail e
  | Ok(name, bytes, pkg) ->
    let machine = Mds.machineOf pkg bytes
    let settings =
      match args.TryGetResult RunArgs.Settings with
      | Some s -> s.Split ',' |> Array.map float
      | None -> machine.parameters |> Array.map (fun p -> float p.defaultValue)
    let banks = sampleBanks (args.TryGetResult RunArgs.Sample)
    use track = new Track.Track(pkg, Some machine, Track.defaultLayout, banks)
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

let stress (args: ParseResults<StressArgs>) =
  let sweep = parseSweep (args.GetResult(StressArgs.Sweep, "exhaustive")) (args.GetResult(StressArgs.Seed, 1))
  match loadMachine (args.GetResult StressArgs.Input) (args.TryGetResult StressArgs.Slot), sweep with
  | Error e, _ | _, Error e -> fail e
  | Ok(name, pkg, machine), Ok sweep ->
    let cs = Stress.controls machine
    let blocks = args.GetResult(StressArgs.Blocks, 16)
    use track = new Track.Track(pkg, Some machine, Track.defaultLayout, sampleBanks (args.TryGetResult StressArgs.Sample))
    let cases = Stress.settingsOf cs sweep |> List.map (Stress.measure track blocks)
    let how = match sweep with Stress.Exhaustive -> "exhaustive" | Stress.RandomCases(n, seed) -> $"random:{n}, seed {seed}"
    printfn "%s: %d cases (%s), %d RENDER blocks each; cycles JSR to STOP, service stand-ins included" name cases.Length how blocks
    let lines, within = Stress.report cs pkg.maxCycles cases
    for l in lines do printfn "%s" l
    if within then 0 else 1

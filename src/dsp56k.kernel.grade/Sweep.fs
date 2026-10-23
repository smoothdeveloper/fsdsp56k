/// Sweep grading on any runtime: a kernel's whole scenario (its pattern, each control swept, then
/// pairwise), played by a Player (.NET, a DSP56300 program, a portable runner), compared word for
/// word with another Player's run, and written as one grade row.
module dsp56k.kernel.Sweep

open System
open System.IO
open System.Diagnostics
open System.Text.Json
open dsp56k.kernel.Grade

module Portable = dsp56k.Portable

/// The pattern's bars, then every control swept over 8 values from its default, then the pairwise
/// combinations: played in blocks of `frames`.
let scenario (frames: int) (controls: Control array) (values: float array -> Step) (pattern: Pattern) : Scenario =
  play frames (exhaustive controls values pattern)

/// The RENDER blocks a scenario plays.
let blocks (s: Scenario) = s.steps |> List.sumBy (function Render n -> n | _ -> 0)

/// A runtime that plays a scenario and returns every block's output and state words.
type Player =
  { name : string
    play : Scenario -> Run }

/// The kernel's F# on .NET, as the DSP grade runs it.
let net (h: Harness) (k: Kernel.Kernel) =
  { name = "dotnet"; play = runNet h k }

/// A program on the DSP56300 emulator: the compiled kernel or the original.
let dsp (h: Harness) (p: Program) (stateWords: int) (xStateWords: int) =
  { name = p.name; play = runStates h p stateWords xStateWords }

/// The portable protocol's steps: settings become parameter words through `settings`.
let steps (settings: float array -> int array) (s: Scenario) : Portable.Step array =
  let step action words blocks : Portable.Step = { action = action; words = words; blocks = blocks }
  s.steps |> List.choose (function
    | Settings values -> Some(step "parameters" (settings values) 0)
    | Words words     -> Some(step "parameters" words 0)
    | Init            -> Some(step "init" [||] 0)
    | Trig            -> Some(step "trig" [||] 0)
    | Render n        -> Some(step "render" [||] n)
    | Mark _          -> None) |> List.toArray

let private ofBlocks (blocks: Portable.Block array) : Run =
  { outputs = blocks |> Array.map (fun b -> b.output); states = blocks |> Array.map (fun b -> b.state); init = []; trig = []; render = [] }

/// A typed portable adapter in this process (the shared memory is the .NET side's, so one at a time).
let direct (name: string) (settings: float array -> int array) (create: unit -> Portable.Instance) =
  { name = name
    play = fun s -> lock sharedMemory (fun () -> Portable.capture create (steps settings s) |> fst |> ofBlocks) }

/// A portable fixture of the scenario; `expected` is the run the runner checks against ([||]: none).
let fixture (kernel: string) (target: Kernel.Target) (settings: float array -> int array) (s: Scenario) (expected: Run) : Portable.Fixture =
  { version  = 1; kernel = kernel; frames = target.frames; channels = target.channels; sampleRate = sampleRate
    steps    = steps settings s
    expected = Array.map2 (fun output state -> ({ output = output; state = state }: Portable.Block)) expected.outputs expected.states
    buffers  = [||] }

/// An external portable runner (`executable arguments --capture fixture.json blocks.tsv`, the
/// Rust wasm build under node for one): it plays the fixture and writes each block's output and
/// state words, one block a line, output and state separated by a tab.
let runner (name: string) (executable: string) (arguments: string list) (directory: string)
           (kernel: string) (target: Kernel.Target) (settings: float array -> int array) =
  { name = name
    play = fun s ->
      Directory.CreateDirectory directory |> ignore
      let input  = Path.GetFullPath(Path.Combine(directory, $"{kernel}.{name}.capture.json"))
      let output = Path.GetFullPath(Path.Combine(directory, $"{kernel}.{name}.capture.tsv"))
      File.WriteAllText(input, JsonSerializer.Serialize(fixture kernel target settings s { outputs = [||]; states = [||]; init = []; trig = []; render = [] }))
      if File.Exists output then File.Delete output
      let info = ProcessStartInfo(executable, UseShellExecute = false)
      for argument in arguments @ [ "--capture"; input; output ] do info.ArgumentList.Add argument
      use child = Process.Start info |> nonNull
      child.WaitForExit()
      if child.ExitCode <> 0 then failwith $"{name} {kernel}: capture exited {child.ExitCode}"
      let words (text: string) = text.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> Array.map int
      let lines = File.ReadAllLines output
      { outputs = lines |> Array.map (fun line -> words (line.Split('\t')[0]))
        states  = lines |> Array.map (fun line -> words (line.Split('\t')[1]))
        init = []; trig = []; render = [] } }

/// The first differing output word and the first differing state word ([]: word for word equal).
let compare (expected: Run) (actual: Run) : Mismatch list =
  [ Grade.compare "output" expected.outputs actual.outputs
    Grade.compare "state"  expected.states  actual.states ] |> List.choose id

/// A checkout's commit, "+dirty" when tracked files differ from it.
let commit (directory: string) =
  let git (arguments: string list) =
    let info = ProcessStartInfo("git", WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true)
    for argument in arguments do info.ArgumentList.Add argument
    use child = Process.Start info |> nonNull
    let text = child.StandardOutput.ReadToEnd().Trim()
    child.WaitForExit()
    if child.ExitCode <> 0 then failwith $"""git {String.concat " " arguments}: exit {child.ExitCode} in {directory}"""
    text
  let head = git [ "rev-parse"; "HEAD" ]
  if git [ "status"; "--porcelain"; "--untracked-files=no" ] = "" then head else head + "+dirty"

/// One runtime's sweep against a reference run.
type Row =
  { kernel     : string
    runtime    : string
    reference  : string
    blocks     : int
    mismatches : Mismatch list
    commit     : string }

let header = String.concat "\t" [ "kernel"; "runtime"; "reference"; "scenario"; "blocks"; "grade"; "first_mismatch"; "toolchain_commit" ]

let mismatchText (m: Mismatch) =
  $"{m.what} block {m.block} word {m.index}: expected {m.expected:x6}, got {m.actual:x6} ({m.differing} differing)"

/// The row: "match" only when no word differs.
let row (r: Row) =
  String.concat "\t"
    [ r.kernel; r.runtime; r.reference; "sweep"; string r.blocks
      (if List.isEmpty r.mismatches then "match" else "mismatch")
      (match r.mismatches with [] -> "-" | m :: _ -> mismatchText m)
      r.commit ]

/// Plays the scenario on both and writes the row.
let grade (commit: string) (kernel: string) (reference: Player) (actual: Player) (s: Scenario) =
  let expected = reference.play s
  { kernel = kernel; runtime = actual.name; reference = reference.name
    blocks = expected.outputs.Length; mismatches = compare expected (actual.play s); commit = commit }

/// Optional external direct-F# target checks, using the very same DSP grading scenario.
/// A runner receives fixture.json result.json warmup trials, then exits nonzero on any difference.
module dsp56k.kernel.PortableGrade

open System
open System.IO
open System.Diagnostics
open System.Text.Json
open dsp56k.Portable

/// Arguments before the protocol's four arguments (for example the Fable-built Node entry).
type Runner =
  { executable : string
    arguments  : string list
    warmup     : int
    trials     : int }

type Comparison =
  { measurement      : Measurement
    referenceOutput  : Grade.Mismatch option option }

/// Use a typed adapter with the same id and state layout as the kernel in the DSP report.
/// Empty buffers means no shared-buffer claim: a caller must supply its expected final buffers.
/// Dynamic beforeRender inputs require an adapter-specific fixture; they are not inferred here.
let check (directory: string) (runner: Runner) (buffers: Buffer array) (report: Grade.Report) =
  try
    if runner.warmup < 0 || runner.trials < 1 then invalidArg "runner" "Invalid benchmark trial counts"
    Directory.CreateDirectory directory |> ignore
    let steps =
      report.steps |> List.choose (function
        | Grade.Settings values -> Some { action = "parameters"; words = report.harness.settings values; blocks = 0 }
        | Grade.Words words -> Some { action = "parameters"; words = words; blocks = 0 }
        | Grade.Init -> Some { action = "init"; words = [||]; blocks = 0 }
        | Grade.Trig -> Some { action = "trig"; words = [||]; blocks = 0 }
        | Grade.Render count -> Some { action = "render"; words = [||]; blocks = count }
        | Grade.Mark _ -> None) |> List.toArray
    let expected = Array.map2 (fun output state -> { output = output; state = state }) report.net.outputs report.net.states
    let fixture: Fixture =
      { version = 1; kernel = report.kernel
        frames = report.harness.target.frames; channels = report.harness.target.channels
        sampleRate = Grade.sampleRate; steps = steps; expected = expected; buffers = buffers }
    let input = Path.GetFullPath(Path.Combine(directory, "fixture.json"))
    let output = Path.GetFullPath(Path.Combine(directory, "measurement.json"))
    File.WriteAllText(input, JsonSerializer.Serialize fixture)
    // A failed runner must never leave a previous successful measurement looking current.
    if File.Exists output then File.Delete output
    let info = ProcessStartInfo(runner.executable, UseShellExecute = false)
    for argument in runner.arguments @ [ input; output; string runner.warmup; string runner.trials ] do
      info.ArgumentList.Add argument
    use child = Process.Start info |> nonNull
    if not (child.WaitForExit(300_000)) then
      child.Kill(true)
      failwith "Portable grader exceeded five minutes"
    if child.ExitCode <> 0 then failwith $"Portable grader exited {child.ExitCode}"
    let measurement = JsonSerializer.Deserialize<Measurement>(File.ReadAllText output) |> nonNull
    if measurement.kernel <> report.kernel || measurement.blocks <> report.blocks
       || measurement.samples <> runner.trials || measurement.medianRenderMs <= 0.
       || not (Double.IsFinite measurement.realTimeFactor)
       || measurement.outputChecks <> (expected |> Array.sumBy (fun block -> block.output.Length))
       || measurement.stateChecks <> (expected |> Array.sumBy (fun block -> block.state.Length))
       || measurement.bufferChecks <> (buffers |> Array.sumBy (fun buffer -> buffer.words.Length)) then
      failwith "Portable grader returned incomplete or inconsistent measurements"
    Ok { measurement = measurement
         referenceOutput = report.refRun |> Option.map (fun reference -> Grade.compare "output" reference.outputs report.net.outputs) }
  with error -> Error error.Message

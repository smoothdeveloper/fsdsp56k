/// A graded kernel's sound, kept for listening: the compiled program's output and the
/// reference's as WAV files, with the scenario that produced them beside them, as a script
/// (each step at its frame and time, then the verdicts) and as an Audacity label track.
module dsp56k.kernel.Audio

open System
open System.IO
open System.Text
open dsp56k.kernel.Grade

let sampleRate = Grade.sampleRate

/// 24-bit PCM, `channels` interleaved words a frame (the target's output layout).
let writeWav (path: string) (channels: int) (words: int array) =
  use f = File.Create path
  use w = new BinaryWriter(f)
  let data = words.Length * 3
  w.Write("RIFF"B)
  w.Write(36 + data)
  w.Write("WAVEfmt "B)
  w.Write(16)
  w.Write(1s)
  w.Write(int16 channels)
  w.Write(sampleRate)
  w.Write(sampleRate * 3 * channels)
  w.Write(int16 (3 * channels))
  w.Write(24s)
  w.Write("data"B)
  w.Write(data)
  for s in words do
    w.Write(byte s)
    w.Write(byte (s >>> 8))
    w.Write(byte (s >>> 16))

let inv (v: float) = v.ToString("0.###", Globalization.CultureInfo.InvariantCulture)

/// Each step with the frame it happens at.
let timeline (r: Report) =
  let t = r.harness.target
  let names = r.harness.parameterNames
  let mutable frame = 0
  [ for s in r.steps do
      match s with
      | Settings v ->
        let text = v |> Array.mapi (fun i x -> $"{(if i < names.Length then names[i] else string i)} {inv x}") |> String.concat ", "
        yield frame, 0, $"settings {text}"
      | Words w -> yield frame, 0, $"""words {w |> Array.map (fun x -> $"${x:x6}") |> String.concat " "}"""
      | Mark text -> yield frame, 0, $"-- {text}"
      | Init -> yield frame, 0, "INIT"
      | Trig -> yield frame, 0, "TRIG"
      | Render n ->
        yield frame, n * t.frames, $"RENDER x{n} (frames {frame}..{frame + n * t.frames - 1})"
        frame <- frame + n * t.frames ]

let seconds (frame: int) = float frame / float sampleRate

/// `<dir>/<name>.kernel.wav`, `.reference.wav` (with a reference), `.script.txt`, `.labels.txt`
/// and the compiled listing `.kernel.asm56k`; the files written.
let save (dir: string) (name: string) (r: Report) =
  Directory.CreateDirectory dir |> ignore
  let t = r.harness.target
  let path suffix = Path.Combine(dir, $"{name}.{suffix}")
  let kernelWav = path "kernel.wav"
  writeWav kernelWav t.channels (Array.concat r.dsp.outputs)
  let referenceWav =
    r.refRun |> Option.map (fun run ->
      let p = path "reference.wav"
      writeWav p t.channels (Array.concat run.outputs)
      p)
  let steps = timeline r
  let script = StringBuilder()
  let line (s: string) = script.AppendLine s |> ignore
  line $"# {r.kernel}: {r.scenario}"
  line $"# {sampleRate} Hz, {t.channels} channel(s) of 24-bit words, {t.frames} frames a RENDER ({r.harness.name})"
  line $"# {Path.GetFileName kernelWav}: the compiled kernel on the emulator"
  referenceWav |> Option.iter (fun p -> line $"# {Path.GetFileName p}: the reference on the emulator")
  line ""
  line "frame     time      step"
  for frame, _, text in steps do
    line (sprintf "%-9d %-9s %s" frame (sprintf "%.3fs" (seconds frame)) text)
  line ""
  let correctness = if r.correctness.IsEmpty then "the compiled program equals the .NET kernel" else $"DIFFERS from the .NET kernel ({r.correctness.Length} mismatches)"
  let fidelity =
    match r.fidelity with
    | None -> "no reference"
    | Some None -> "output identical to the reference"
    | Some(Some m) -> $"DIFFERS from the reference: {m.differing} words, first at block {m.block} word {m.index}"
  line $"# correctness: {correctness}"
  line $"# fidelity: {fidelity}"
  let cycles =
    match r.reference with
    | Some c when c.renderMean > 0.0 ->
      let ratio = (r.ours.renderMean / c.renderMean).ToString("0.00", Globalization.CultureInfo.InvariantCulture)
      $"RENDER mean {inv r.ours.renderMean} cycles, reference {inv c.renderMean} (x{ratio})"
    | _ -> $"RENDER mean {inv r.ours.renderMean} cycles"
  line $"# {cycles}"
  File.WriteAllText(path "script.txt", script.ToString())
  // Audacity: start, end (seconds), label
  // a pattern's own labels when it has them, every step otherwise
  let marks = steps |> List.filter (fun (_, _, text) -> text.StartsWith "-- ")
  let labelled = if marks.IsEmpty then steps else marks |> List.map (fun (f, l, t) -> f, l, t.Substring 3)
  let labels =
    labelled |> List.map (fun (frame, length, text) -> sprintf "%.6f\t%.6f\t%s" (seconds frame) (seconds (frame + length)) text)
  File.WriteAllLines(path "labels.txt", labels)
  File.WriteAllText(path "kernel.asm56k", dsp56k.dsl.Program.source r.compiled.items)
  [ yield kernelWav; yield! Option.toList referenceWav; yield path "script.txt"; yield path "labels.txt"; yield path "kernel.asm56k" ]

/// Delete a folder of generated output before writing it again, so nothing stale stays.
let clean (dir: string) = if Directory.Exists dir then Directory.Delete(dir, true)

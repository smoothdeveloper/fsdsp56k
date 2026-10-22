/// The `fsdsp56k` command line, run as a process (CliWrap) on shipped machines.
module dsp56k.tests.ToolTests

open System
open System.IO
open CliWrap
open CliWrap.Buffered
open UnMango.CliWrap.FSharp
open Expecto
open dsp56k.tests.Fixtures

let tool (args: string array) =
  let dll = Path.Combine(AppContext.BaseDirectory, "fsdsp56k.dll")
  let result =
    Cli.wrap "dotnet"
    |> Cli.args [| dll; yield! args |]
    |> Cli.validation CommandResultValidation.None
    |> fun c -> c.ExecuteBufferedAsync().GetAwaiter().GetResult()
  result.ExitCode, result.StandardOutput + result.StandardError

let scratch () =
  let d = Path.Combine(Path.GetTempPath(), "fsdsp56k-tests", string (Guid.NewGuid()))
  Directory.CreateDirectory d |> ignore
  d

let tests =
  testList "tool" [
    test "mds pack rebuilds a shipped package byte for byte, and syx/unsyx keep it" {
      needFixture "murdenihcam"
      let dir = fixture "murdenihcam" </> "SMP-GR[ANULATOR]" </> "SMP-GR_allbanks"
      let out = scratch ()
      let code, text =
        tool [| "mds"; "pack"; "-j"; dir </> "SMP-GR_allbanks.json"; "-s"; dir </> "SMP-GR_allbanks.asm56k"
                "-I"; dir </> ".." </> "source"; "--sprite"; dir </> "SMP-GR_allbanks.pbm"; "-o"; out </> "gr.mds" |]
      Expect.equal code 0 text
      let shipped = File.ReadAllBytes(dir </> "SMP-GR_allbanks.mds")
      Expect.equal (File.ReadAllBytes(out </> "gr.mds")) shipped "packed as shipped"
      Expect.equal (fst (tool [| "mds"; "syx"; out </> "gr.mds"; "--slot"; "3"; "-o"; out </> "gr.syx" |])) 0 "syx"
      Expect.equal (fst (tool [| "mds"; "unsyx"; out </> "gr.syx"; "-o"; out |])) 0 "unsyx"
      Expect.equal (File.ReadAllBytes(out </> "gr-slot3.mds")) shipped "the dump holds the package"
    }

    test "check agrees with the oracle on a shipped listing" {
      needFixture "murdenihcam"
      needNative ()
      let dir = fixture "murdenihcam" </> "SMP-GR[ANULATOR]"
      let code, text = tool [| "check"; dir </> "SMP-GR_allbanks" </> "SMP-GR_allbanks.asm56k"; "-I"; dir </> "source" |]
      Expect.equal code 0 text
      Expect.stringContains text "0 disagree" "every instruction agrees"
    }

    test "run renders a machine within its declared budget" {
      needFixture "kitbasher"
      needNative ()
      let out = scratch ()
      let code, text = tool [| "run"; fixture "kitbasher" </> "mds" </> "tx6" </> "cp" </> "cp.syx"; "--blocks"; "64"; "--wav"; out </> "cp.wav" |]
      Expect.equal code 0 text
      Expect.isGreaterThan (FileInfo(out </> "cp.wav").Length) (64L * 32L * 3L) "the WAV holds every block"
    }

    test "stress sweeps a machine's settings and reports its worst cycles as a table" {
      needFixture "kitbasher"
      needNative ()
      let code, text = tool [| "stress"; fixture "kitbasher" </> "mds" </> "tx6" </> "sd" </> "sd.syx"; "--blocks"; "4" |]
      Expect.equal code 0 text
      Expect.stringContains text "(exhaustive)" "the sweep is named"
      for phase in [ "INIT"; "TRIG"; "RENDER"; "TRIG+RENDER" ] do
        Expect.isTrue (text.Split('\n') |> Array.exists (fun l -> l.StartsWith(phase + " "))) $"a row for {phase}"
      Expect.isFalse (text.Contains "{") "text, not JSON"
    }

    test "stress random:N is repeatable from its seed" {
      needFixture "kitbasher"
      needNative ()
      let sd = fixture "kitbasher" </> "mds" </> "tx6" </> "sd" </> "sd.syx"
      let once () = tool [| "stress"; sd; "--sweep"; "random:40"; "--seed"; "3"; "--blocks"; "4" |]
      let code, first = once ()
      Expect.equal code 0 first
      Expect.equal (snd (once ())) first "the same seed, the same cases"
    }

    test "stress --search anneal finds the exhaustive sweep's worst case in fewer measurements" {
      needFixture "kitbasher"
      needNative ()
      match dsp56k.tool.Commands.loadMachine (fixture "kitbasher" </> "mds" </> "tx6" </> "sd" </> "sd.syx") None with
      | Error e -> failtest e
      | Ok(_, pkg, machine) ->
        use track = new machinedrum.run.Track.Track(pkg, Some machine, machinedrum.run.Track.defaultLayout, Map.empty)
        let cs = dsp56k.tool.Stress.controls machine
        let evaluate = dsp56k.tool.Stress.measure track 4
        let objective = dsp56k.tool.Stress.AnyPhase
        let sweep = dsp56k.tool.Stress.exhaustiveSettings cs |> List.map evaluate
        let worst = sweep |> List.map (dsp56k.tool.Stress.score objective) |> List.max
        let found = dsp56k.tool.Stress.anneal cs evaluate objective 30 1
        Expect.isLessThan found.evaluations sweep.Length "fewer measurements than the sweep"
        Expect.isGreaterThanOrEqual (dsp56k.tool.Stress.score objective found.best) worst "at least the sweep's worst"
    }

    test "stress anneal climbs to an inner peak no edge or sweep holds" {
      let cs : dsp56k.kernel.Grade.Control array =
        [| { name = "A"; low = 0.0; high = 127.0; standard = 64.0 }
           { name = "B"; low = 0.0; high = 127.0; standard = 64.0 } |]
      // cycles peaking at A=37, B=90: what a machine with a costly middle setting looks like
      let evaluate (s: float array) : dsp56k.tool.Stress.Case =
        let c = 20000L - int64 ((s[0] - 37.0) ** 2.0 + (s[1] - 90.0) ** 2.0)
        { settings = s; init = 0L; trig = 0L; render = c; trigBlock = c }
      let found = dsp56k.tool.Stress.anneal cs evaluate (dsp56k.tool.Stress.OnePhase dsp56k.tool.Stress.Render) 400 1
      Expect.equal found.best.settings [| 37.0; 90.0 |] "the peak"
      Expect.isLessThanOrEqual found.evaluations 400 "within the budget"
    }

    test "audition's loop triggers on schedule and writes every block's frames" {
      needFixture "kitbasher"
      needNative ()
      match dsp56k.tool.Commands.loadMachine (fixture "kitbasher" </> "mds" </> "tx6" </> "cp" </> "cp.syx") None with
      | Error e -> failtest e
      | Ok(_, pkg, machine) ->
        use track = new machinedrum.run.Track.Track(pkg, Some machine, machinedrum.run.Track.defaultLayout, Map.empty)
        track.SetSettings(machine.parameters |> Array.map (fun p -> float p.defaultValue))
        track.Init 0 |> ignore
        let written = ResizeArray<float32>()
        let played = dsp56k.tool.Audition.play written.AddRange (dsp56k.tool.Audition.ofTrack track machine) 40 (Some 16) ignore (fun () -> false)
        Expect.equal played { blocks = 40; trigs = 3 } "blocks 0, 16 and 32 trigger"
        Expect.equal written.Count (40 * 32) "32 frames a block"
        Expect.isTrue (written |> Seq.exists (fun f -> f <> 0.0f)) "it makes a sound"
    }

    test "audition --midi-in answers the Machinedrum's default map: the track's note and its CCs" {
      let trigs = ResizeArray<unit>()
      let set = ResizeArray<int * float>()
      let voice : dsp56k.tool.Audition.Voice =
        { trig = trigs.Add; render = (fun () -> Array.zeroCreate 32); parameter = fun i v -> set.Add((i, v)) }
      let message status d1 d2 : dsp56k.audio.Midi.Message = { status = status; data1 = d1; data2 = d2 }
      let respond = dsp56k.tool.Audition.respond voice 1
      // track 1: channel 1, note 36, parameters at CC 16..23
      Expect.isTrue (respond 1 (message 0x90 36 100)) "BD's note"
      Expect.isFalse (respond 1 (message 0x90 36 0)) "a note-on of velocity 0 is a note-off"
      Expect.isFalse (respond 1 (message 0x90 38 100)) "SD's note"
      Expect.isTrue (respond 1 (message 0xb0 18 99)) "parameter 3"
      Expect.isFalse (respond 1 (message 0xb0 24 99)) "a track effect, not the machine"
      // track 7: channel 2 (base + 1), note 47, third of its group: CC 72..79
      Expect.isTrue (respond 7 (message 0x91 47 1)) "RS's note on channel 2"
      Expect.isFalse (respond 7 (message 0x90 47 1)) "the wrong channel"
      Expect.isTrue (respond 7 (message 0xb1 79 5)) "parameter 8"
      Expect.equal trigs.Count 2 "two trigs"
      Expect.equal (List.ofSeq set) [ 2, 99.0; 7, 5.0 ] "two parameters"
    }

    test "MIDI loops back through a virtual port: what the output sends, the input reads" {
      needAudio ()
      if OperatingSystem.IsWindows() then skiptest "no virtual MIDI ports on Windows"
      let name = $"fsdsp56k-loop-{Guid.NewGuid():N}"
      use out = new dsp56k.audio.Midi.Output($"virtual:{name}")
      Expect.contains (dsp56k.audio.Midi.inputs ()) name "the virtual output is an input to others"
      use input = new dsp56k.audio.Midi.Input(name)
      out.NoteOn(0, 36, 100)
      out.Control(0, 18, 99)
      let received = ResizeArray<dsp56k.audio.Midi.Message>()
      let until = DateTime.UtcNow.AddSeconds 2.0
      while received.Count < 2 && DateTime.UtcNow < until do
        received.AddRange(input.Poll())
        Threading.Thread.Sleep 5
      Expect.equal (List.ofSeq received) [ { status = 0x90; data1 = 36; data2 = 100 }; { status = 0xb0; data1 = 18; data2 = 99 } ] "the note and the CC"
    }

    test "audition --midi-list lists the MIDI inputs" {
      needAudio ()
      let code, text = tool [| "audition"; "--midi-list" |]
      Expect.equal code 0 text
    }

    test "stress --audition's shaping: never over the ceiling, faded at both ends, a gap after" {
      let shaper = dsp56k.tool.Audition.Shaper(0.35, 0.5)
      let loud = Array.init 4410 (fun i -> if i % 2 = 0 then 1.0 else -1.0)
      let heard = dsp56k.tool.Audition.shapeCase shaper 220 100 loud
      Expect.equal heard.Length (4410 + 100) "the case, then the gap"
      Expect.isTrue (heard |> Array.forall (fun x -> abs x <= 0.5 + 1e-9)) "under the ceiling"
      Expect.equal heard[0] 0.0 "faded in"
      Expect.equal heard[4409] 0.0 "faded out"
      Expect.isTrue (heard[4410..] |> Array.forall ((=) 0.0)) "silence between cases"
      let boosted = dsp56k.tool.Audition.Shaper(4.0, 0.5)
      Expect.isTrue ([ for x in loud -> boosted.Shape x ] |> List.forall (fun x -> abs x <= 0.5 + 1e-9)) "a large gain is limited too"
    }

    test "stress takes several machines and sums them up" {
      needFixture "kitbasher"
      needNative ()
      let tx6 d = fixture "kitbasher" </> "mds" </> "tx6" </> d </> (d + ".syx")
      let code, text = tool [| "stress"; tx6 "bd"; tx6 "cp"; "--blocks"; "2"; "--max-cases"; "5" |]
      Expect.equal code 0 text
      Expect.stringContains text "5 cases of" "sampled down"
      Expect.stringContains text "summary" "a summary of both"
    }

    test "stress cases: the defaults first, then each control swept and the pairwise rows" {
      let cs : dsp56k.kernel.Grade.Control array =
        [| { name = "A"; low = 0.0; high = 127.0; standard = 64.0 }
           { name = "B"; low = 0.0; high = 1.0; standard = 0.0 } |]
      let settings = dsp56k.tool.Stress.exhaustiveSettings cs
      Expect.equal settings.Head [| 64.0; 0.0 |] "the defaults first"
      Expect.equal (List.distinct settings) settings "each case once"
      Expect.contains settings [| 127.0; 1.0 |] "a pairwise corner"
      Expect.contains settings [| 0.0; 0.0 |] "A at its low end"
    }

    test "machinedrum, md and the old mds name show the same package" {
      needFixture "MDS"
      let sine = fixture "MDS" </> "machines" </> "examples" </> "sine" </> "sine.mds"
      let code, long = tool [| "machinedrum"; "info"; sine |]
      Expect.equal code 0 long
      let code, short = tool [| "md"; "info"; sine |]
      Expect.equal code 0 short
      Expect.equal short long "md is machinedrum"
      let code, old = tool [| "mds"; "info"; sine |]
      Expect.equal code 0 old
      Expect.stringContains old "'mds' is now 'machinedrum'" "the old name says so"
    }

    test "run takes an F# kernel as one file, built and compiled for the host" {
      let dir = scratch ()
      let source = dir </> "Sine.fs"
      File.Copy(repoRoot </> "tests" </> "dsp56k.tests" </> "Kernels" </> "Sine.fs", source)
      let code, text = tool [| "run"; source; "--blocks"; "16"; "--settings"; "100,127" |]
      Expect.equal code 0 text
      Expect.stringContains text "sine: 16 blocks" "the kernel by its name"
      Expect.isFalse (text.Contains "peak 0.000") "it makes a sound"
      let code, text = tool [| "run"; source; "--kernel"; "saw" |]
      Expect.notEqual code 0 text
      Expect.stringContains text "no kernel 'saw' among sine" "--kernel names one that is there"
      let code, text = tool [| "run"; source; "--host"; "nord" |]
      Expect.notEqual code 0 text
      Expect.stringContains text "unknown host 'nord'" "hosts by key"
    }

    test "run takes an F# script, its #r lines dropped and a module wrapped round it" {
      let dir = scratch ()
      let lines = File.ReadAllLines(repoRoot </> "tests" </> "dsp56k.tests" </> "Kernels" </> "Sine.fs")
      let body = lines |> Array.filter (fun l -> not (l.StartsWith "[<ReflectedDefinition>]" || l.StartsWith "module " || l.StartsWith "//" || l.StartsWith "///"))
      let script = dir </> "sine.fsx"
      File.WriteAllLines(script, Array.append [| "#r \"Dsp56k.Kernel.dll\"" |] body)
      let code, text = tool [| "stress"; script; "--blocks"; "2"; "--max-cases"; "3" |]
      Expect.equal code 0 text
      Expect.stringContains text "sine: 3 cases" "the script's kernel, stressed"
    }
  ]

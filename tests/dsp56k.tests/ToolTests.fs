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
  ]

/// Differential MACRI test: Rust native oracle versus dsp56300 WASM, including CCR and ties.
#load "../src/dsp56k.native/Native.fs"

open System
open System.IO
open System.Diagnostics
open System.Text.Json
open dsp56k.Native

type Write = { bank: int; address: int; data: int array }
type Register = { register: int; word: int }
type Fixture =
  { trigger: bool; writes: Write array; registers: Register array; output: int array; state: obj array }

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let output = Path.Combine(root, ".bench/wasm/macri")
Directory.CreateDirectory output |> ignore
if not (load ()) then failwith "Build the native Rust emulator first"
let registerCodes = [| 4; 5; 6; 7; 8; 12; 10; 9; 13; 11 |]
let fixtures = ResizeArray<Fixture>()
let assemble source =
  let words = ResizeArray<int>()
  for line in source do
    match assembleLine (0x100 + words.Count) (oracleSpelling line) with
    | Ok code -> words.AddRange code
    | Error error -> failwith $"{line}: {error}"
  words.ToArray()
let emulator = new Emulator(512, 512, 512)
for immediate in [| 0; 1; 0x6186; 0x400000; 0x7fffff; 0x800000; 0xffffff |] do
  for operand in [| 0; 1; 0x400000; 0x7fffff; 0x800000; 0xffffff |] do
    for source in [| "x0"; "x1"; "y0"; "y1" |] do
      for destination in [| "a"; "b" |] do
        for sign in [| "+"; "-" |] do
          let code =
            assemble
              [| "andi #$0,ccr"; $"macri {sign}#>${immediate:x6},{source},{destination}"
                 "move a0,y:>$0"; "move a1,y:>$1"; "move a2,y:>$2"
                 "move b0,y:>$3"; "move b1,y:>$4"; "move b2,y:>$5"
                 "movec sr,x0"; "move x0,a"; "and #>$ff,a"; "move a1,y:>$6"
                 "move #>0,x0"; "move x0,y:>$7"; "rts" |]
          let registers =
            [| for register in 24 .. 27 do { register = register; word = operand }
               { register = 28; word = [| 0x7fffff; 0x800000; 0x800001 |][fixtures.Count % 3] }
               { register = 29; word = fixtures.Count % 2 }; { register = 30; word = 0 }
               { register = 31; word = [| 0x800001; 0x800000; 0x7fffff |][fixtures.Count % 3] }
               { register = 32; word = fixtures.Count % 2 }; { register = 33; word = 0 } |]
          emulator.Load(Space.P, 0x100, code)
          for value in registers do emulator.SetReg(registerCodes[value.register - 24], value.word)
          if emulator.Call(0x100, 0x1f0, 1000).IsNone then failwith "Rust MACRI case did not return"
          fixtures.Add
            { trigger = false; writes = [| { bank = 0; address = 0x100; data = code } |]
              registers = registers; output = Array.init 8 (fun i -> emulator.Read(Space.Y, i)); state = [||] }
let image =
  {| version = 1; words = 512; frames = 4; channels = 2; sampleRate = 44100
     segments = fixtures[0].writes; registers = fixtures[0].registers
     beforeRender = ([||]: Register array)
     initialize = ([||]: int array); render = 0x100; trig = (None: int option)
     outputBank = 2; output = 0; inputBank = -1; input = 0 |}
let imagePath = Path.Combine(output, "image.json")
let referencePath = Path.Combine(output, "reference.json")
File.WriteAllText(imagePath, JsonSerializer.Serialize {| title = "MACRI signed/negated/A/B/CCR/ties"; image = image |})
File.WriteAllText(referencePath, JsonSerializer.Serialize fixtures)
printfn "[1/2] Rust oracle: %d MACRI cases generated; accumulator words and CCR recorded" fixtures.Count
printfn "[2/2] dsp56300 WASM: replay the same instructions and operands"
let start = ProcessStartInfo("node", WorkingDirectory = root)
for argument in [| "out/wasm/check.mjs"; imagePath; referencePath |] do start.ArgumentList.Add argument
let child = Process.Start start
child.WaitForExit()
if child.ExitCode <> 0 then failwith $"WASM MACRI differential failed ({child.ExitCode})"
child.Dispose()
(emulator :> IDisposable).Dispose()

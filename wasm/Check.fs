/// Node entry point: safety smoke checks, then optional image/reference pairs from any repository.
module dsp56k.WasmCheck

open Fable.Core
open Fable.Core.JsInterop
open dsp56k.Wasm
open dsp56k.WasmKernel

[<Import("readFileSync", "node:fs")>]
let readFile (path: string) (encoding: string) : string = jsNative

[<Import("writeFileSync", "node:fs")>]
let writeFile (path: string) (text: string) : unit = jsNative

[<Emit("JSON.stringify($0)")>]
let serialize (value: obj) : string = jsNative

[<Emit("process.argv.slice(2)")>]
let arguments () : string array = jsNative

[<Emit("JSON.parse($0)")>]
let parse<'a> (text: string) : 'a = jsNative

[<Emit("performance.now()")>]
let now () : float = jsNative

[<Emit("$0.then($1).catch(error => { console.error(error); process.exitCode = 1; })")>]
let finish (pending: JS.Promise<'a>) (success: 'a -> unit) : unit = jsNative

type StateWord = { bank: int; address: int; word: int }
type Fixture = { trigger: bool; writes: Segment array; registers: RegisterWrite array; output: int array; state: StateWord array }
type Bundle = { title: string; image: KernelImage }

let equal name expected actual =
  if actual <> expected then failwith $"{name}: expected {expected}, got {actual}"

let measure exports image path =
  let defaults = Instance(exports, image)
  for _ in 1 .. 16 do defaults.Render() |> ignore
  let mutable cycles, operations = 0., 0.
  for _ in 1 .. 128 do
    defaults.Render() |> ignore
    cycles <- cycles + defaults.LastCycles
    operations <- operations + defaults.LastOperations
  if cycles <= 0. || operations <= 0. then failwith "Missing DSP counters"
  let metrics =
    createObj [ "cycles" ==> cycles / 128.; "operations" ==> operations / 128.
                "frames" ==> image.frames; "sampleRate" ==> image.sampleRate
                "warmup" ==> 16; "blocks" ==> 128 ]
  writeFile (path + ".metrics.json") (serialize metrics)
  printfn "  Default benchmark: %.2f cycles, %.2f executed instructions / %d frames (128 blocks, 16 warmup)"
    (cycles / 128.) (operations / 128.) image.frames

finish (createDsp (createObj [])) (fun exports ->
  equal "invalid size" -1 (exports._dsp_create 0)
  equal "oversized memory" -1 (exports._dsp_create 0x180001)
  equal "create" 0 (exports._dsp_create 256)
  equal "bank bound" -1 (exports._dsp_write(3, 0, 0))
  equal "address bound" -1 (exports._dsp_write(0, 256, 0))
  equal "word bound" -1 (exports._dsp_write(0, 0, 0x1000000))
  equal "register bound" -1 (exports._dsp_reg_write(34, 0))
  equal "load RTS" 0 (exports._dsp_write(0, 0, 0x00000c))
  equal "return" 1 (exports._dsp_call(0, 100))
  equal "RTS instruction counter" 1. (exports._dsp_operations())
  if exports._dsp_cycles() <= 0. then failwith "RTS cycle counter did not advance"
  equal "replace cached RTS" 0 (exports._dsp_write(0, 0, 0))
  equal "instruction watchdog" -2 (exports._dsp_call(0, 10))
  equal "reset after watchdog" 0 (exports._dsp_create 256)
  equal "invalid external boundary" -1 (exports._dsp_bridge 256)
  equal "external bridge" 0 (exports._dsp_bridge 128)
  equal "shared X write" 0 (exports._dsp_write(1, 128, 0x123456))
  equal "shared Y read" 0x123456 (exports._dsp_read(2, 128))
  equal "shared P read" 0x123456 (exports._dsp_read(0, 128))
  equal "internal X write" 0 (exports._dsp_write(1, 127, 0x654321))
  equal "internal Y independent" 0 (exports._dsp_read(2, 127))
  equal "internal P independent" 0 (exports._dsp_read(0, 127))
  equal "shared RTS" 0 (exports._dsp_write(0, 128, 0xc))
  equal "call shared RTS" 1 (exports._dsp_call(128, 100))
  equal "replace shared opcode via X" 0 (exports._dsp_write(1, 128, 0))
  equal "shared return via Y" 0 (exports._dsp_write(2, 129, 0xc))
  equal "shared opcode cache invalidation" 2 (exports._dsp_call(128, 100))
  exports._dsp_destroy()
  printfn "[1/1] Runtime bounds, RTS, code invalidation and instruction watchdog: passed"
  let paths = arguments()
  if paths.Length % 2 <> 0 then failwith "Pass image/reference pairs"
  for index in 0 .. 2 .. paths.Length - 1 do
    let bundle = readFile paths[index] "utf8" |> parse<Bundle>
    let fixtures = readFile paths[index + 1] "utf8" |> parse<Fixture array>
    // Timing remains useful for an image whose subsequent differential check fails.
    measure exports bundle.image paths[index]
    let engine = Instance(exports, bundle.image)
    let start = now()
    for block, expected in fixtures |> Array.indexed do
      engine.ConfigureRegisters expected.registers
      for segment in expected.writes do
        segment.data |> Array.iteri (fun i word -> engine.Write(segment.bank, segment.address + i, word))
      if expected.trigger then engine.Trigger()
      let output = engine.Render()
      Array.iteri (fun i word -> equal $"{bundle.title} block {block} sample {i}" word output[i]) expected.output
      for word in expected.state do
        equal $"{bundle.title} block {block} bank {word.bank} address {word.address}"
          word.word (exports._dsp_read(word.bank, word.address))
    let duration = now() - start
    printfn "[%d/%d] %s: %d blocks, exact output/state/scratch passed (%.1f ms including assertions)"
      (index / 2 + 1) (paths.Length / 2) bundle.title fixtures.Length duration
    let blocks = 2048
    let start = now()
    for _ = 1 to blocks do engine.Render() |> ignore
    let renderTime = now() - start
    let audioTime = float (blocks * bundle.image.frames) / float bundle.image.sampleRate * 1000.
    printfn "  Interpreter: %.0f ms audio / %.0f ms rendering (%.1f times real time)"
      audioTime renderTime (audioTime / renderTime)
    exports._dsp_destroy())

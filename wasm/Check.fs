/// Node entry point: safety smoke checks, then optional image/reference pairs from any repository.
module dsp56k.WasmCheck

open Fable.Core
open Fable.Core.JsInterop
open dsp56k.Wasm
open dsp56k.WasmKernel

[<Import("readFileSync", "node:fs")>]
let readFile (path: string) (encoding: string) : string = jsNative

[<Emit("process.argv.slice(2)")>]
let arguments () : string array = jsNative

[<Emit("JSON.parse($0)")>]
let parse<'a> (text: string) : 'a = jsNative

[<Emit("performance.now()")>]
let now () : float = jsNative

[<Emit("$0.then($1).catch(error => { console.error(error); process.exitCode = 1; })")>]
let finish (pending: JS.Promise<'a>) (success: 'a -> unit) : unit = jsNative

type StateWord = { bank: int; address: int; word: int }
type Fixture = { writes: Segment array; registers: RegisterWrite array; output: int array; state: StateWord array }
type Bundle = { title: string; image: KernelImage }

let equal name expected actual =
  if actual <> expected then failwith $"{name}: expected {expected}, got {actual}"

finish (createDsp (createObj [])) (fun exports ->
  equal "invalid size" -1 (exports._dsp_create 0)
  equal "create" 0 (exports._dsp_create 256)
  equal "bank bound" -1 (exports._dsp_write(3, 0, 0))
  equal "address bound" -1 (exports._dsp_write(0, 256, 0))
  equal "word bound" -1 (exports._dsp_write(0, 0, 0x1000000))
  equal "register bound" -1 (exports._dsp_reg_write(34, 0))
  equal "load RTS" 0 (exports._dsp_write(0, 0, 0x00000c))
  equal "return" 1 (exports._dsp_call(0, 100))
  equal "replace cached RTS" 0 (exports._dsp_write(0, 0, 0))
  equal "instruction watchdog" -2 (exports._dsp_call(0, 10))
  exports._dsp_destroy()
  printfn "[1/1] Runtime bounds, RTS, code invalidation and instruction watchdog: passed"
  let paths = arguments()
  if paths.Length % 2 <> 0 then failwith "Pass image/reference pairs"
  for index in 0 .. 2 .. paths.Length - 1 do
    let bundle = readFile paths[index] "utf8" |> parse<Bundle>
    let fixtures = readFile paths[index + 1] "utf8" |> parse<Fixture array>
    let engine = Instance(exports, bundle.image)
    let start = now()
    for block, expected in fixtures |> Array.indexed do
      engine.ConfigureRegisters expected.registers
      for segment in expected.writes do
        segment.data |> Array.iteri (fun i word -> engine.Write(segment.bank, segment.address + i, word))
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

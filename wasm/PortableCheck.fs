/// Replay a graded scenario through the built processor's message and process methods.
module dsp56k.PortableProcessorCheck

open Fable.Core
open Fable.Core.JsInterop
open dsp56k.Wasm

[<Import("readFileSync", "node:fs")>]
let read (path: string) : obj = jsNative
[<Emit("JSON.parse($0.toString())")>]
let parse (bytes: obj) : obj = jsNative
[<Emit("process.argv.slice(2)")>]
let arguments () : string array = jsNative
[<Emit("globalThis.AudioWorkletProcessor = class { constructor() { this.port = { postMessage: data => { this.lastMessage = data; } }; } }; globalThis.registerProcessor = (name, ctor) => { globalThis.portableConstructor = ctor; }")>]
let install () : unit = jsNative
[<Emit("import('./portable-processor.mjs')")>]
let load () : JS.Promise<obj> = jsNative
[<Emit("new WebAssembly.Module($0)")>]
let compile (bytes: obj) : obj = jsNative
[<Emit("new globalThis.portableConstructor({processorOptions:{module:$0}})")>]
let create (compiled: obj) : obj = jsNative
[<Emit("$0.then($1).catch(e => { console.error(e); process.exitCode = 1; })")>]
let finish (pending: JS.Promise<obj>) (success: obj -> unit) : unit = jsNative

let args = arguments()
if args.Length < 2 || args.Length > 3 then failwith "Usage: portable-check fixture.json kernels.wasm [128]"
let quantum = if args.Length = 3 && args[2] = "128" then 8 else 1
let fixture = read args[0] |> parse
install()
finish (load()) (fun _ ->
  let processor = read args[1] |> compile |> create
  let send command =
    processor?message command |> ignore
    if string processor?lastMessage?``type`` = "error" then failwith (string processor?lastMessage?message)
  let parameters = Array.zeroCreate<int> 8
  let audio: obj = fixture?audio
  let id =
    if not (isNull audio) then int audio?kernel
    else
      match string fixture?kernel with
      | "NOIS" -> 0 | "FM+ PAR" -> 1 | "GND SIN" -> 2
      | "FM+ STAT" -> 3 | "FM+ DYN" -> 4 | "THRU" -> 5
      | name -> failwith name
  let inputResource = if not (isNull audio) then int audio?inputResource elif id = 5 then 7 else -1
  let inputWords = Array.zeroCreate<int> 32
  send (createObj [ "type" ==> "load"; "image" ==> createObj [
    "kernel" ==> id; "frames" ==> 16; "sampleRate" ==> 44100
    "parameters" ==> parameters; "resources" ==> ([||]: obj array); "inputResource" ==> inputResource ] ])
  send (createObj [ "type" ==> "gain"; "value" ==> 0.25 ])
  let traceRegions: TraceRegion array = if isNull audio || isNull audio?traceRegions then [||] else audio?traceRegions
  if traceRegions.Length > 0 then
    send (createObj [ "type" ==> "resource-trace"; "regions" ==> traceRegions ])
  let expected: obj array = fixture?expected
  let steps: obj array = fixture?steps
  let mutable block = 0
  for step in steps do
    let words: int array = step?words
    let action = string step?action
    match action with
    | "parameters" ->
      for index in 0 .. words.Length - 1 do
        send (createObj [ "type" ==> "parameter"; "index" ==> index; "word" ==> words[index] ])
    | "init" -> send (createObj [ "type" ==> "init" ])
    | "trig" -> send (createObj [ "type" ==> "trig" ])
    | "render" ->
      send (createObj [ "type" ==> "play"; "value" ==> true ])
      let count: int = step?blocks
      let mutable remaining = count
      while remaining > 0 do
        let batch = min quantum remaining
        let output = [| Array.zeroCreate<float32> (batch * 16); Array.zeroCreate<float32> (batch * 16) |]
        let inputs =
          if inputResource < 0 then [||]
          else
            [| Array.init 2 (fun channel -> Array.init (batch * 16) (fun frame ->
                 float32 (float (inputWords[(frame % 16) * 2 + channel] <<< 8 >>> 8) / 8388608.))) |]
        processor?``process``(inputs, [| output |]) |> ignore
        for offset in 0 .. batch - 1 do
          let audio: int array = expected[block + offset]?output
          for index in 0 .. 31 do
            let signed = audio[index] <<< 8 >>> 8
            if output[index % 2][offset * 16 + index / 2] <> float32 (float signed / 8388608. * 0.25) then
              failwith $"Worklet output mismatch block{block + offset} word{index}"
        send (createObj [ "type" ==> "snapshot" ])
        let actual: int array = processor?lastMessage?state
        let state: int array = expected[block + batch - 1]?state
        if actual <> state then failwith $"Worklet state mismatch block{block + batch - 1}"
        block <- block + batch
        remaining <- remaining - batch
    | action when action.StartsWith "memory:" ->
      let name = action.Split(':')[1]
      let id =
        if not (isNull audio) then
          let resources: string array = audio?resources
          resources |> Array.findIndex ((=) name)
        else
          match name with
          | "fm_runtime_sine" -> 0 | "fm_prepared_pitch" -> 1
          | "fm_frequency_ratios" -> 2 | "fm_tone_coefficients" -> 3
          | "runtime_sine" -> 4 | "sin_prepared_pitch" -> 5
          | "fm_dyn_signed_envelope_rates" -> 6 | "thru_input" -> 7
          | _ -> failwith name
      if id = inputResource then Array.blit words 0 inputWords 0 (min 32 words.Length)
      send (createObj [ "type" ==> "resource"; "id" ==> id; "words" ==> words ])
    | _ -> failwith action
  for index in 0 .. traceRegions.Length - 1 do
    let region = traceRegions[index]
    let names: string array = audio?resources
    let buffers: obj array = fixture?buffers
    let buffer = buffers |> Array.find (fun buffer -> string buffer?name = names[region.bank])
    let words: int array = buffer?words
    let traces: int array array = processor?regionTraces
    let cursors: int array = processor?regionCursors
    for word in 0 .. region.words - 1 do
      let actual = traces[index][(cursors[index] + 1024 - region.words + word) % 1024]
      if actual <> words[region.address + word] then failwith $"Resource trace mismatch {index}/{word}"
  printfn "[1/1] Built audio processor: %d blocks exact stereo output/state; %d resource traces (up to %d frames)" block traceRegions.Length (quantum * 16))

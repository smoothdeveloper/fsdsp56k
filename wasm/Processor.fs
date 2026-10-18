/// Real-time DSP interpreter hosted in the browser's audio rendering thread.
module dsp56k.WasmProcessor

open Fable.Core
open Fable.Core.JsInterop
open dsp56k.Wasm
open dsp56k.WasmKernel

[<Global; AllowNullLiteral>]
type AudioWorkletProcessor() =
  member _.port: MessagePort = jsNative

[<Emit("$0.then($1).catch($2)")>]
let finish (pending: JS.Promise<'a>) (success: 'a -> unit) (failure: obj -> unit) : unit = jsNative

// AudioWorkletGlobalScope has no fetch or URL. Compile on the page, instantiate on the audio thread.
[<Emit("({instantiateWasm: (imports, receive) => { const instance = new WebAssembly.Instance($0, imports); receive(instance); return instance.exports; }})")>]
let instantiateOptions (compiledModule: obj) : obj = jsNative

[<AttachMembers>]
type DspProcessor(options: obj) as this =
  inherit AudioWorkletProcessor()
  let mutable pending: JS.Promise<Exports> option = None
  let mutable kernel: Instance option = None
  let mutable block = [||]
  let mutable cursor = 0
  let mutable playing = false
  let mutable gain = 0.15
  let mutable level = 0.
  let mutable quanta = 0
  let mutable loadRequest = 0
  let trace = Array.zeroCreate<int> 1024
  let mutable traceCursor = 0

  do this.port.onmessage <- fun event -> this.message event?data

  member _.error(error: obj) =
    playing <- false
    this.port.postMessage (createObj [ "type" ==> "error"; "message" ==> string error ])

  member _.message(data: obj) =
    try
      match string data?``type`` with
      | "load" ->
        playing <- false
        kernel <- None
        loadRequest <- loadRequest + 1
        let request = loadRequest
        let load =
          match pending with
          | Some load -> load
          | None ->
            let load = createDsp (instantiateOptions options?processorOptions?``module``)
            pending <- Some load
            load
        finish load
          (fun exports ->
            if request = loadRequest then
              kernel <- Some (Instance(exports, data?image))
              block <- [||]
              cursor <- 0
              Array.fill trace 0 trace.Length 0
              traceCursor <- 0
              this.port.postMessage (createObj [ "type" ==> "ready" ])) this.error
      | "play" -> playing <- data?value
      | "gain" ->
        let value: float = data?value
        if System.Double.IsNaN value || System.Double.IsInfinity value then failwith "Invalid gain"
        gain <- max 0. (min 0.5 value)
      | command ->
        let engine = kernel |> Option.defaultWith (fun () -> failwith "DSP not ready")
        match command with
        | "memory" -> engine.Write(data?bank, data?address, data?word)
        | "registers" -> engine.ConfigureRegisters data?values
        | "trig" -> engine.Image.trig |> Option.iter (fun entry -> engine.Call entry |> ignore)
        | _ -> failwith "Unknown runtime message"
    with error -> this.error (box error.Message)

  member _.``process``(_inputs: float32 array array array, outputs: float32 array array array) =
    match kernel with
    | Some engine when playing && outputs.Length > 0 && outputs[0].Length = 2 ->
      let left, right = outputs[0][0], outputs[0][1]
      try
        for i = 0 to left.Length - 1 do
          if cursor = block.Length then
            block <- engine.Render()
            cursor <- 0
          let signed word = float (word <<< 8 >>> 8) / 8388608.
          trace[traceCursor] <- block[cursor]
          trace[traceCursor + 1] <- block[cursor + 1]
          traceCursor <- (traceCursor + 2) % trace.Length
          left[i] <- float32 (signed block[cursor] * gain)
          right[i] <- float32 (signed block[cursor + 1] * gain)
          cursor <- cursor + 2
          level <- max level (max (abs (float left[i])) (abs (float right[i])))
        quanta <- quanta + 1
        if quanta = 64 then
          let samples = Array.init trace.Length (fun i -> trace[(traceCursor + i) % trace.Length])
          this.port.postMessage (createObj [ "type" ==> "level"; "peak" ==> level; "samples" ==> samples ])
          level <- 0.
          quanta <- 0
      with error ->
        Array.fill left 0 left.Length 0.f
        Array.fill right 0 right.Length 0.f
        this.error (box error.Message)
    | _ -> ()
    true

[<Emit("registerProcessor('dsp56300', $0)")>]
let register (constructor: obj) : unit = jsNative

register (jsConstructor<DspProcessor>)

/// Direct F# kernels compiled by Fable/Rust, on the audio rendering thread.
module dsp56k.PortableProcessor

open System
open Fable.Core
open Fable.Core.JsInterop
open dsp56k.Wasm
open dsp56k.PortableAudio

[<Global; AllowNullLiteral>]
type AudioWorkletProcessor() =
  member _.port: MessagePort = jsNative

[<Emit("new WebAssembly.Instance($0, {}).exports")>]
let instantiate (compiledModule: obj) : obj = jsNative
[<Emit("typeof performance !== 'undefined' ? performance.now() : Date.now()")>]
let clock () : float = jsNative
[<Emit("typeof performance !== 'undefined' ? 'high-resolution' : '1ms'")>]
let precision () : string = jsNative

[<AttachMembers>]
type PortableProcessor(options: obj) as this =
  inherit AudioWorkletProcessor()
  let exports = instantiate options?processorOptions?``module``
  let mutable image: Image option = None
  let mutable playing = false
  let mutable gain = 0.15
  let mutable cursor = 32
  let block = Array.zeroCreate<int> 32
  let trace = Array.zeroCreate<int> 1024
  let inputTrace = Array.zeroCreate<int> 1024
  let mutable inputCursor = 0
  let mutable traceCursor, quanta = 0, 0
  let mutable peak = 0.
  let mutable regions: TraceRegion array = [||]
  let mutable regionTraces: int array array = [||]
  let mutable regionCursors: int array = [||]
  let timing = dsp56k.ProcessingTiming.Window()
  let timer = precision()
  do this.port.onmessage <- fun event -> this.message event?data

  member _.error(error: obj) =
    playing <- false
    this.port.postMessage (createObj [ "type" ==> "error"; "message" ==> string error ])

  member _.message(data: obj) =
    try
      match string data?``type`` with
      | "load" ->
        playing <- false
        image <- None
        let requested: Image = data?image
        if requested.frames <> 16 || requested.sampleRate <> 44100 || requested.parameters.Length <> 8 then
          failwith "Portable audio requires 16 stereo frames at 44100 Hz and eight parameter slots"
        exports?dsp_select requested.kernel |> ignore
        for resource in requested.resources do
          for index in 0 .. resource.words.Length - 1 do
            exports?dsp_resource_write(resource.id, index, resource.words[index]) |> ignore
        for index in 0 .. 7 do exports?dsp_parameter(index, requested.parameters[index]) |> ignore
        exports?dsp_init() |> ignore
        cursor <- block.Length
        traceCursor <- 0
        Array.fill trace 0 trace.Length 0
        Array.fill inputTrace 0 inputTrace.Length 0
        inputCursor <- 0
        quanta <- 0
        peak <- 0.
        timing.Reset()
        regions <- [||]
        regionTraces <- [||]
        regionCursors <- [||]
        image <- Some requested
        this.port.postMessage (createObj [ "type" ==> "ready" ])
      | "play" ->
        playing <- data?value
        timing.Reset()
        quanta <- 0
      | "gain" ->
        let value: float = data?value
        if Double.IsNaN value || Double.IsInfinity value then failwith "Invalid gain"
        gain <- max 0. (min 0.5 value)
      | "parameter" -> exports?dsp_parameter(data?index, data?word) |> ignore
      | "resource" ->
        let words: int array = data?words
        for index in 0 .. words.Length - 1 do
          exports?dsp_resource_write(data?id, index, words[index]) |> ignore
      | "resource-trace" ->
        let requested: TraceRegion array = data?regions
        if requested.Length > 8 then failwith "Too many trace regions"
        for region in requested do
          if region.bank < 0 || region.address < 0 || region.words < 1 || region.words > 64 then
            failwith "Invalid resource trace"
          exports?dsp_resource_word(region.bank, region.address) |> ignore
          exports?dsp_resource_word(region.bank, region.address + region.words - 1) |> ignore
        regions <- requested
        regionTraces <- Array.init regions.Length (fun _ -> Array.zeroCreate 1024)
        regionCursors <- Array.zeroCreate regions.Length
      | "init" ->
        exports?dsp_init() |> ignore
        cursor <- block.Length
      | "trig" -> exports?dsp_trig() |> ignore
      | "snapshot" ->
        let count: int = exports?dsp_state()
        this.port.postMessage (createObj [ "type" ==> "snapshot"; "state" ==> Array.init count (fun index -> exports?dsp_state_word index) ])
      | _ -> failwith "Unknown portable audio message"
    with error -> this.error (box error.Message)

  member _.``process``(inputs: float32 array array array, outputs: float32 array array array) =
    match image with
    | Some image when playing && outputs.Length > 0 && outputs[0].Length = 2 ->
      let started = clock()
      let mutable rendering = 0.
      let left, right = outputs[0][0], outputs[0][1]
      try
        for index in 0 .. left.Length - 1 do
          if cursor = block.Length then
            if image.inputResource >= 0 then
              if left.Length % image.frames <> 0 then failwith "Input blocks must divide the audio quantum"
              for frame in 0 .. image.frames - 1 do
                for channel in 0 .. 1 do
                  let sample =
                    if inputs.Length > 0 && inputs[0].Length > 0 then
                      inputs[0][min channel (inputs[0].Length - 1)][index + frame]
                    else 0.f
                  let word =
                    int (Math.Round(max -8388608. (min 8388607. (float sample * 8388608.)))) &&& 0xffffff
                  exports?dsp_resource_write(image.inputResource, frame * 2 + channel, word) |> ignore
                  inputTrace[inputCursor] <- word
                  inputCursor <- (inputCursor + 1) % inputTrace.Length
            let renderStart = clock()
            let count: int = exports?dsp_render()
            if count <> block.Length then failwith "Invalid portable stereo block"
            for word in 0 .. block.Length - 1 do block[word] <- exports?dsp_output word
            for regionIndex in 0 .. regions.Length - 1 do
              let region = regions[regionIndex]
              for word in 0 .. region.words - 1 do
                let position = regionCursors[regionIndex]
                regionTraces[regionIndex][position] <- exports?dsp_resource_word(region.bank, region.address + word)
                regionCursors[regionIndex] <- (position + 1) % 1024
            rendering <- rendering + max 0. (clock() - renderStart)
            cursor <- 0
          let signed word = float (word <<< 8 >>> 8) / 8388608.
          left[index] <- float32 (signed block[cursor] * gain)
          right[index] <- float32 (signed block[cursor + 1] * gain)
          peak <- max peak (max (abs (float left[index])) (abs (float right[index])))
          trace[traceCursor] <- block[cursor]
          trace[traceCursor + 1] <- block[cursor + 1]
          traceCursor <- (traceCursor + 2) % trace.Length
          cursor <- cursor + 2
        timing.Add(clock() - started, rendering, float left.Length / float image.sampleRate * 1000.,
          if timer = "1ms" then 1. else 0.)
        quanta <- quanta + 1
        if quanta = 64 then
          this.port.postMessage (createObj [
            "type" ==> "level"; "peak" ==> peak
            "samples" ==> Array.init trace.Length (fun i -> trace[(traceCursor + i) % trace.Length])
            "traces" ==> (if regions.Length > 0 then
                            regionTraces |> Array.mapi (fun index values ->
                              Array.init values.Length (fun i -> values[(regionCursors[index] + i) % values.Length]))
                          elif image.inputResource < 0 then [||]
                          else [| Array.init inputTrace.Length (fun i -> inputTrace[(inputCursor + i) % inputTrace.Length]) |])
            "timing" ==> timing.Take(); "timer" ==> timer ])
          quanta <- 0
          peak <- 0.
      with error ->
        Array.fill left 0 left.Length 0.f
        Array.fill right 0 right.Length 0.f
        this.error (box error.Message)
    | _ -> ()
    true

[<Emit("registerProcessor('fsharp-rust-wasm', $0)")>]
let register (constructor: obj) : unit = jsNative
register (jsConstructor<PortableProcessor>)

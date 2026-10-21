/// Real-time DSP interpreter hosted in the browser's audio rendering thread.
module dsp56k.WasmProcessor

open Fable.Core
open Fable.Core.JsInterop
open dsp56k.Wasm
open dsp56k.WasmKernel

// AudioWorkletGlobalScope does not expose performance.now in every browser.
[<Emit("(typeof performance !== 'undefined' && typeof performance.now === 'function') ? performance.now() : Date.now()")>]
let clock () : float = jsNative

[<Emit("(typeof performance !== 'undefined' && typeof performance.now === 'function') ? 'high-resolution' : '1ms'")>]
let clockPrecision () : string = jsNative

[<Global; AllowNullLiteral>]
type AudioWorkletProcessor() =
  member _.port: MessagePort = jsNative

[<Emit("$0.then($1).catch($2)")>]
let finish (pending: JS.Promise<'a>) (success: 'a -> unit) (failure: obj -> unit) : unit = jsNative

// AudioWorkletGlobalScope has no fetch or URL. Compile on the page, instantiate on the audio thread; locateFile keeps the
// glue from resolving the .wasm against import.meta.url (new URL), which it does at load when no locateFile is given.
[<Emit("({locateFile: (path) => path, instantiateWasm: (imports, receive) => { const instance = new WebAssembly.Instance($0, imports); receive(instance); return instance.exports; }})")>]
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
  let timing = dsp56k.ProcessingTiming.Window()
  let timer = clockPrecision()
  let mutable loadRequest = 0
  let trace = Array.zeroCreate<int> 1024
  let mutable traceCursor = 0
  let mutable regions: TraceRegion array = [||]
  let mutable regionTraces: int array array = [||]
  let mutable regionCursors: int array = [||]
  // register activity: how many blocks changed each register since the last report (`activity` turns it on)
  let mutable writes: int array option = None
  let mutable steps = 0
  // words a step answers with, each (bank, offset from r6): the locals and state of a step view
  let mutable watch: (int * int) array = [||]

  do this.port.onmessage <- fun event -> this.message event?data

  /// The watched words at r6 (snapshot word 6) now; -1 for one outside memory.
  member _.watched(snapshot: int array) =
    match kernel with
    | Some engine when snapshot.Length > 6 ->
      [| for bank, offset in watch ->
           let address = (snapshot[6] + offset) &&& 0xffffff
           try engine.Read(bank, address) with _ -> -1 |]
    | _ -> [||]

  member _.error(error: obj) =
    playing <- false
    this.port.postMessage (createObj [ "type" ==> "error"; "message" ==> string error ])

  member _.message(data: obj) =
    try
      match string data?``type`` with
      | "load" ->
        timing.Reset()
        quanta <- 0
        playing <- false
        kernel <- None
        regions <- [||]
        regionTraces <- [||]
        regionCursors <- [||]
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
      | "play" ->
        playing <- data?value
        timing.Reset()
        quanta <- 0
      | "gain" ->
        let value: float = data?value
        if System.Double.IsNaN value || System.Double.IsInfinity value then failwith "Invalid gain"
        gain <- max 0. (min 0.5 value)
      | command ->
        let engine = kernel |> Option.defaultWith (fun () -> failwith "DSP not ready")
        match command with
        | "trace" ->
          let requested: TraceRegion array = data?regions
          if requested.Length > 8 then failwith "Too many trace regions"
          for region in requested do
            if not (integer region.words) || region.words < 1 || region.words > 64 then
              failwith "Invalid trace length"
            engine.Read(region.bank, region.address) |> ignore
            engine.Read(region.bank, region.address + region.words - 1) |> ignore
          regions <- requested
          regionTraces <- Array.init regions.Length (fun _ -> Array.zeroCreate 1024)
          regionCursors <- Array.zeroCreate regions.Length
        | "memory" -> engine.Write(data?bank, data?address, data?word)
        | "watch" ->
          let words: obj array = data?words
          if words.Length > 256 then failwith "Too many watched words"
          watch <- [| for w in words -> (w?bank: int), (w?offset: int) |]
        | "registers" -> engine.ConfigureRegisters data?values
        | "trig" -> engine.Trigger()
        | "activity" -> writes <- if (data?value: bool) then Some(Array.zeroCreate 40) else None
        | "begin" ->
          // stepping stops the audio until `play`
          playing <- false
          let entry: int = if isNull data?entry then engine.Image.render else data?entry
          engine.Begin entry
          steps <- 0
          let now = engine.Snapshot()
          this.port.postMessage (createObj [ "type" ==> "step"; "status" ==> 0; "pc" ==> engine.PC; "steps" ==> steps; "registers" ==> now
                                             "watched" ==> this.watched now ])
        | "step" ->
          let count: int = if isNull data?count then 1 else data?count
          if not (integer count) || count < 1 || count > 100000 then failwith "Invalid step count"
          let before = engine.Snapshot()
          let mutable status = 0
          let mutable taken = 0
          while status = 0 && taken < count do
            status <- engine.Step()
            taken <- taken + 1
          steps <- steps + taken
          let after = engine.Snapshot()
          let written = [| for i in 0 .. after.Length - 1 do if after[i] <> before[i] then i |]
          this.port.postMessage (createObj [
            "type" ==> "step"; "status" ==> status; "pc" ==> engine.PC; "steps" ==> steps
            "registers" ==> after; "written" ==> written; "watched" ==> this.watched after ])
        | _ -> failwith "Unknown runtime message"
    with error -> this.error (box error.Message)

  member _.``process``(inputs: float32 array array array, outputs: float32 array array array) =
    match kernel with
    | Some engine when playing && outputs.Length > 0 && outputs[0].Length = 2 ->
      let started = clock()
      let mutable rendering = 0.
      let left, right = outputs[0][0], outputs[0][1]
      try
        for i = 0 to left.Length - 1 do
          if cursor = block.Length then
            if engine.Image.inputBank >= 0 then
              if left.Length % engine.Image.frames <> 0 then failwith "Input blocks must divide the audio quantum"
              for frame in 0 .. engine.Image.frames - 1 do
                for channel in 0 .. 1 do
                  let sample =
                    if inputs.Length > 0 && inputs[0].Length > 0 then
                      inputs[0][min channel (inputs[0].Length - 1)][i + frame]
                    else 0.f
                  let word = int (System.Math.Round(max -8388608. (min 8388607. (float sample * 8388608.)))) &&& 0xffffff
                  engine.Write(engine.Image.inputBank, engine.Image.input + frame * 2 + channel, word)
            let renderStart = clock()
            block <- (match writes with Some counts -> engine.RenderCounting counts | None -> engine.Render())
            rendering <- rendering + max 0. (clock() - renderStart)
            for index in 0 .. regions.Length - 1 do
              let region = regions[index]
              for offset in 0 .. region.words - 1 do
                regionTraces[index][regionCursors[index]] <- engine.Read(region.bank, region.address + offset)
                regionCursors[index] <- (regionCursors[index] + 1) % 1024
            cursor <- 0
          let signed word = float (word <<< 8 >>> 8) / 8388608.
          trace[traceCursor] <- block[cursor]
          trace[traceCursor + 1] <- block[cursor + 1]
          traceCursor <- (traceCursor + 2) % trace.Length
          left[i] <- float32 (signed block[cursor] * gain)
          right[i] <- float32 (signed block[cursor + 1] * gain)
          cursor <- cursor + 2
          level <- max level (max (abs (float left[i])) (abs (float right[i])))
        timing.Add(clock() - started, rendering,
          float left.Length / float engine.Image.sampleRate * 1000., if timer = "1ms" then 1. else 0.)
        quanta <- quanta + 1
        if quanta = 64 then
          let samples = Array.init trace.Length (fun i -> trace[(traceCursor + i) % trace.Length])
          let traces =
            regionTraces |> Array.mapi (fun index values ->
              Array.init values.Length (fun i -> values[(regionCursors[index] + i) % values.Length]))
          this.port.postMessage (createObj [
            "type" ==> "level"; "peak" ==> level
            "samples" ==> samples; "traces" ==> traces
            "timing" ==> timing.Take(); "timer" ==> timer
            "writes" ==> (match writes with Some counts -> box (Array.copy counts) | None -> null) ])
          writes |> Option.iter (fun counts -> Array.fill counts 0 counts.Length 0)
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

/// Node/browser-compatible adapter to the direct Fable/Rust exports. No DSP emulation.
module dsp56k.PortableRustWasm

open Fable.Core
open Fable.Core.JsInterop
open dsp56k.Portable

[<Emit("WebAssembly.instantiate($0, {}).then(r => r.instance.exports)")>]
let instantiate (bytes: obj) : JS.Promise<obj> = jsNative

let create (exports: obj) name () =
  let id = PortableAdapters.names |> Array.findIndex ((=) name)
  exports?dsp_select id |> ignore
  { parameters = fun words ->
      for index in 0 .. 7 do
        exports?dsp_parameter(index, if index < words.Length then words[index] else 0) |> ignore
    memory = fun name offset words ->
      let resource = PortableAdapters.resources |> Array.findIndex (fun (key, _) -> key = name)
      for index in 0 .. words.Length - 1 do
        exports?dsp_resource_write(resource, offset + index, words[index]) |> ignore
    init = fun () -> exports?dsp_init() |> ignore
    trig = fun () -> exports?dsp_trig() |> ignore
    render = fun () ->
      let count: int = exports?dsp_render()
      Array.init count (fun index -> exports?dsp_output index)
    state = fun () ->
      let count: int = exports?dsp_state()
      Array.init count (fun index -> exports?dsp_state_word index)
    buffers = fun () ->
      [| for bank, (name, length) in Array.indexed [| "scratch_x", 32; "scratch_y", 32; "track_memory", 24576 |] do
           { name = name; words = Array.init length (fun index -> exports?dsp_buffer_word(bank, index)) }
         for id, (name, words) in Array.indexed PortableAdapters.resources do
           { name = name; words = Array.init words.Length (fun index -> exports?dsp_resource_word(id, index)) } |] }

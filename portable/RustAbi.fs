/// Small C ABI around the same typed adapters; no wasm-bindgen or JavaScript synthesis code.
module dsp56k.PortableRustAbi

open Fable.Core.Rust
open dsp56k.kernel

let mutable private current: Portable.Instance option = None
let private parameters = Array.zeroCreate 8
let mutable private output: int array = [||]
let mutable private state: int array = [||]
let private host () = current |> Option.defaultWith (fun () -> failwith "Select a kernel first")

[<Extern("C"); OuterAttr("no_mangle")>]
let dsp_select id =
  let name = PortableAdapters.names[id]
  Portable.reset()
  Array.fill parameters 0 parameters.Length 0
  current <- Some (PortableAdapters.create name)
  output <- [||]
  state <- [||]

[<Extern("C"); OuterAttr("no_mangle")>]
let dsp_parameter index word =
  parameters[index] <- word
  (host()).parameters parameters

[<Extern("C"); OuterAttr("no_mangle")>]
let dsp_init () = (host()).init()

[<Extern("C"); OuterAttr("no_mangle")>]
let dsp_trig () = (host()).trig()

[<Extern("C"); OuterAttr("no_mangle")>]
let dsp_render () =
  output <- (host()).render()
  output.Length

[<Extern("C"); OuterAttr("no_mangle")>]
let dsp_output index = output[index]

[<Extern("C"); OuterAttr("no_mangle")>]
let dsp_state () =
  state <- (host()).state()
  state.Length

[<Extern("C"); OuterAttr("no_mangle")>]
let dsp_state_word index = state[index]

[<Extern("C"); OuterAttr("no_mangle")>]
let dsp_buffer_word bank index =
  let region = match bank with 0 -> Scratch.x | 1 -> Scratch.y | 2 -> Track.memory | _ -> failwith "Unknown buffer"
  region.words[index]

[<Extern("C"); OuterAttr("no_mangle")>]
let dsp_resource_write resource index word =
  let name, _ = PortableAdapters.resources[resource]
  (host()).memory name index [| word |]

[<Extern("C"); OuterAttr("no_mangle")>]
let dsp_resource_word resource index =
  let _, words = PortableAdapters.resources[resource]
  words[index]

/// Typed adapters for existing sample kernels. The synthesis functions are linked unchanged.
module dsp56k.PortableAdapters

open dsp56k.kernel
open dsp56k.Portable
open samples.kernels

let resources: (string * int array) array = [||]

let names = [| "sine"; "pluck"; "input-thru" |]

let private word (words: int array) index =
  Frac.ofRaw (if index < words.Length then words[index] else 0)

let private output render =
  let output = Output()
  fun () ->
    output.Clear()
    render output
    output.Words

let create name =
  match name with
  | "sine" ->
    let mutable p: Sine.Params = { pitch = Frac.zero; amp = Frac.zero }
    let s: Sine.State = { phase = Long.zero; ampPrev = Frac.zero }
    { memory = writeResource resources
      buffers = buffers
      parameters = fun words -> p <- { pitch = word words 0; amp = word words 1 }
      init = fun () -> Sine.init p s
      trig = fun () -> Sine.trig p s
      render = output (fun out -> Sine.render p s out)
      state = fun () ->
        [| int ((s.phase.Raw >>> 24) &&& 0xffffffL)
           int (s.phase.Raw &&& 0xffffffL)
           s.ampPrev.Word |] }
  | "pluck" ->
    let mutable p: Pluck.Params = { ptch = Frac.zero; dec = Frac.zero; lev = Frac.zero }
    let s: Pluck.State =
      { region = Frac.zero; last = Frac.zero; pos = Frac.zero
        pending = Frac.zero; prev = Frac.zero; noise = Frac.zero }
    { memory = writeResource resources
      buffers = buffers
      parameters = fun words ->
        p <- { ptch = word words 0; dec = word words 1; lev = word words 2 }
      init = fun () -> Pluck.init p s
      trig = fun () -> Pluck.trig p s
      render = output (fun out -> Pluck.render p s out)
      state = fun () ->
        [| s.region.Word; s.last.Word; s.pos.Word; s.pending.Word; s.prev.Word; s.noise.Word |] }
  | "input-thru" ->
    let mutable p: InputThru.Params = { side = Frac.zero; lev = Frac.zero }
    let s: InputThru.State = { pos = Frac.zero }
    { memory = writeResource resources
      buffers = buffers
      parameters = fun words -> p <- { side = word words 0; lev = word words 1 }
      init = fun () -> InputThru.init p s
      trig = fun () -> InputThru.trig p s
      render = output (fun out -> InputThru.render p s out)
      state = fun () -> [| s.pos.Word |] }
  | _ -> invalidArg "name" $"No portable adapter registered for {name}"

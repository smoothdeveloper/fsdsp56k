/// The same C ABI as Rust/WASM, called through native function pointers.
module dsp56k.PortableRustNative

open System
open System.Runtime.InteropServices
open dsp56k.Portable

[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type private Read0 = delegate of unit -> int
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type private Read1 = delegate of int -> int
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type private Read2 = delegate of int * int -> int
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type private Write0 = delegate of unit -> unit
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type private Write1 = delegate of int -> unit
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type private Write2 = delegate of int * int -> unit
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type private Write3 = delegate of int * int * int -> unit

let load path =
  let library = NativeLibrary.Load(IO.Path.GetFullPath path)
  try
    let symbol name = NativeLibrary.GetExport(library, name)
    let select = Marshal.GetDelegateForFunctionPointer<Write1>(symbol "dsp_select")
    let parameter = Marshal.GetDelegateForFunctionPointer<Write2>(symbol "dsp_parameter")
    let initialize = Marshal.GetDelegateForFunctionPointer<Write0>(symbol "dsp_init")
    let trigger = Marshal.GetDelegateForFunctionPointer<Write0>(symbol "dsp_trig")
    let render = Marshal.GetDelegateForFunctionPointer<Read0>(symbol "dsp_render")
    let output = Marshal.GetDelegateForFunctionPointer<Read1>(symbol "dsp_output")
    let snapshot = Marshal.GetDelegateForFunctionPointer<Read0>(symbol "dsp_state")
    let stateWord = Marshal.GetDelegateForFunctionPointer<Read1>(symbol "dsp_state_word")
    let bufferWord = Marshal.GetDelegateForFunctionPointer<Read2>(symbol "dsp_buffer_word")
    let resourceWord = Marshal.GetDelegateForFunctionPointer<Read2>(symbol "dsp_resource_word")
    let write = Marshal.GetDelegateForFunctionPointer<Write3>(symbol "dsp_resource_write")
    let create name () =
      select.Invoke(PortableAdapters.names |> Array.findIndex ((=) name))
      { parameters = fun words ->
          for index in 0 .. 7 do parameter.Invoke(index, if index < words.Length then words[index] else 0)
        memory = fun name offset words ->
          let resource = PortableAdapters.resources |> Array.findIndex (fun (key, _) -> key = name)
          let _, storage = PortableAdapters.resources[resource]
          if offset < 0 || offset > storage.Length - words.Length
             || words |> Array.exists (fun word -> word < 0 || word > 0xffffff) then
            invalidArg "words" "Resource write outside its buffer or 24-bit range"
          for index in 0 .. words.Length - 1 do write.Invoke(resource, offset + index, words[index])
        init = initialize.Invoke
        trig = trigger.Invoke
        render = fun () -> Array.init (render.Invoke()) output.Invoke
        state = fun () -> Array.init (snapshot.Invoke()) stateWord.Invoke
        buffers = fun () ->
          let regions = [| "scratch_x", 32; "scratch_y", 32; "track_memory", 24576 |]
          [| for bank, (name, length) in Array.indexed regions do
               { name = name; words = Array.init length (fun index -> bufferWord.Invoke(bank, index)) }
             for id, (name, words) in Array.indexed PortableAdapters.resources do
               { name  = name
                 words = Array.init words.Length (fun index -> resourceWord.Invoke(id, index)) } |] }
    create, { new IDisposable with member _.Dispose() = NativeLibrary.Free library }
  with _ ->
    NativeLibrary.Free library
    reraise()

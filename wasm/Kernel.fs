/// Assembly-image loading and block execution, shared by the worklet and differential checks.
module dsp56k.WasmKernel

open Fable.Core
open dsp56k.Wasm

[<AllowNullLiteral>]
type Exports =
  abstract _dsp_create: int -> int
  abstract _dsp_destroy: unit -> unit
  abstract _dsp_write: int * int * int -> int
  abstract _dsp_read: int * int -> int
  abstract _dsp_reg_write: int * int -> int
  abstract _dsp_reg_read: int -> int
  abstract _dsp_call: int * int -> int

[<ImportDefault("./dsp56300.mjs")>]
let createDsp (options: obj) : JS.Promise<Exports> = jsNative

[<Emit("Number.isInteger($0)")>]
let integer (value: int) : bool = jsNative

let check action result =
  if result < 0 then failwith $"DSP {action} failed ({result})"
  result

let validWord word = integer word && word >= 0 && word <= 0xffffff

type Instance(exports: Exports, image: KernelImage) as this =
  let mutable beforeRender = Array.copy image.beforeRender
  do
    if image.version <> 1 || not (integer image.words) || image.words < 256 || image.words > 0x160000 then
      failwith "Unsupported kernel image"
    if not (integer image.frames) || image.frames < 1 || image.frames > 1024 then
      failwith "Invalid block size"
    if not (integer image.output) || image.output < 0 || image.output + 2 * image.frames > image.words then
      failwith "Output buffer outside image"
    if image.outputBank < 0 || image.outputBank > 2 then failwith "Invalid output bank"
    exports._dsp_create image.words |> check "create" |> ignore
    try
      for segment in image.segments do
        if segment.address + segment.data.Length > image.words then failwith "Memory segment outside image"
        segment.data |> Array.iteri (fun index word -> this.Write(segment.bank, segment.address + index, word))
      this.Registers image.registers
      for entry in image.initialize do this.Call entry |> ignore
    with error ->
      exports._dsp_destroy()
      reraise()

  member _.Image = image

  member _.Write(bank, address, word) =
    if not (integer bank && integer address && validWord word)
       || bank < 0 || bank > 2 || address < 0 || address >= image.words then
      failwith "Invalid memory write"
    exports._dsp_write(bank, address, word) |> check "write" |> ignore

  member _.Registers(values: RegisterWrite array) =
    for value in values do
      if not (integer value.register && validWord value.word) || value.register < 0 || value.register > 33 then
        failwith "Invalid register write"
      exports._dsp_reg_write(value.register, value.word) |> check "register" |> ignore

  /// Prepared arguments are restored before each render, because the routine clobbers registers.
  member this.ConfigureRegisters(values: RegisterWrite array) =
    this.Registers values
    for value in values do
      beforeRender <-
        beforeRender |> Array.filter (fun previous -> previous.register <> value.register)
        |> fun previous -> Array.append previous [| value |]

  member _.Call entry =
    if not (integer entry) || entry < 0 || entry >= image.words - 1 then failwith "Invalid entry point"
    exports._dsp_call(entry, 1000000) |> check "call"

  member this.Render() =
    this.Registers beforeRender
    this.Call image.render |> ignore
    Array.init (image.frames * 2) (fun i ->
      exports._dsp_read(image.outputBank, image.output + i) |> check "read")

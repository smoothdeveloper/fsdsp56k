/// MIDI ports through native/fsdsp56k-audio (midir): the ports' names, an input whose channel
/// messages the caller polls between blocks, and an output.
module dsp56k.audio.Midi

open System
open System.Runtime.InteropServices
open System.Text

module Raw =
  [<DllImport(Output.Lib)>]
  extern uint32 fsdsp56k_midi_inputs(byte[] buffer, uint32 length)

  [<DllImport(Output.Lib)>]
  extern nativeint fsdsp56k_midi_in_open(byte[] port)

  [<DllImport(Output.Lib)>]
  extern uint32 fsdsp56k_midi_in_poll(nativeint input, uint32[] out, uint32 max)

  [<DllImport(Output.Lib)>]
  extern void fsdsp56k_midi_in_close(nativeint input)

  [<DllImport(Output.Lib)>]
  extern uint32 fsdsp56k_midi_outputs(byte[] buffer, uint32 length)

  [<DllImport(Output.Lib)>]
  extern nativeint fsdsp56k_midi_out_open(byte[] port)

  [<DllImport(Output.Lib)>]
  extern int fsdsp56k_midi_out_send(nativeint output, byte[] bytes, uint32 length)

  [<DllImport(Output.Lib)>]
  extern void fsdsp56k_midi_out_close(nativeint output)

let private need () =
  if not (Output.load ()) then failwith "MIDI missing: dotnet fsi native/build.fsx (or set FSDSP56K_AUDIO)"

let private lastError () =
  let buffer = Array.zeroCreate<byte> 512
  let n = Output.Raw.fsdsp56k_audio_last_error(buffer, uint32 buffer.Length)
  Encoding.UTF8.GetString(buffer, 0, min (int n) (buffer.Length - 1))

let private names (call: byte array -> uint32) =
  need ()
  let mutable buffer = Array.zeroCreate<byte> 4096
  let mutable n = call buffer
  if n = UInt32.MaxValue then failwith (lastError ())
  if int n >= buffer.Length then
    buffer <- Array.zeroCreate<byte> (int n + 1)
    n <- call buffer
  Encoding.UTF8.GetString(buffer, 0, min (int n) (buffer.Length - 1)).Split('\n', StringSplitOptions.RemoveEmptyEntries)

/// The input ports, in the order an index picks them.
let inputs () = names (fun b -> Raw.fsdsp56k_midi_inputs(b, uint32 b.Length))

/// The output ports, in the order an index picks them.
let outputs () = names (fun b -> Raw.fsdsp56k_midi_outputs(b, uint32 b.Length))

/// A channel message: status (kind and channel 0..15) and two data bytes.
type Message =
  { status : int
    data1  : int
    data2  : int }
  member m.Kind = m.status &&& 0xf0
  member m.Channel = m.status &&& 0x0f

let unpack (w: uint32) =
  { status = int (w &&& 0xffu)
    data1  = int ((w >>> 8) &&& 0x7fu)
    data2  = int ((w >>> 16) &&& 0x7fu) }

/// An open input: an index, part of a port's name, or `virtual:NAME` (a port of our own other
/// programs send to; macOS and Linux).
type Input(port: string) =
  do need ()
  let handle =
    match Raw.fsdsp56k_midi_in_open(Encoding.UTF8.GetBytes(port + "\000")) with
    | 0n -> failwith $"MIDI input: {lastError ()}"
    | h -> h
  let buffer = Array.zeroCreate<uint32> 256
  let mutable closed = false

  /// The messages that arrived since the last poll, oldest first.
  member _.Poll() =
    [| let mutable n = Raw.fsdsp56k_midi_in_poll(handle, buffer, uint32 buffer.Length)
       while n > 0u do
         for i in 0 .. int n - 1 -> unpack buffer[i]
         n <- if int n = buffer.Length then Raw.fsdsp56k_midi_in_poll(handle, buffer, uint32 buffer.Length) else 0u |]

  interface IDisposable with
    member _.Dispose() =
      if not closed then
        closed <- true
        Raw.fsdsp56k_midi_in_close handle

/// An open output: an index, part of a port's name, or `virtual:NAME` (a port of our own other
/// programs read from; macOS and Linux).
type Output(port: string) =
  do need ()
  let handle =
    match Raw.fsdsp56k_midi_out_open(Encoding.UTF8.GetBytes(port + "\000")) with
    | 0n -> failwith $"MIDI output: {lastError ()}"
    | h -> h
  let mutable closed = false

  member _.Send(bytes: byte array) =
    if Raw.fsdsp56k_midi_out_send(handle, bytes, uint32 bytes.Length) <> 0 then failwith $"MIDI send: {lastError ()}"

  member this.NoteOn(channel: int, note: int, velocity: int) = this.Send [| byte (0x90 ||| channel); byte note; byte velocity |]
  member this.NoteOff(channel: int, note: int) = this.Send [| byte (0x80 ||| channel); byte note; 0uy |]
  member this.Control(channel: int, cc: int, value: int) = this.Send [| byte (0xb0 ||| channel); byte cc; byte value |]

  interface IDisposable with
    member _.Dispose() =
      if not closed then
        closed <- true
        Raw.fsdsp56k_midi_out_close handle

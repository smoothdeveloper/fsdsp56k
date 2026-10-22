/// The default audio output, through native/fsdsp56k-audio (cpal): mono frames pushed into a
/// lock-free ring the device callback reads. The C ABI is native/fsdsp56k-audio/src/lib.rs.
module dsp56k.audio.Output

open System
open System.IO
open System.Runtime.InteropServices
open System.Text
open System.Threading

[<Literal>]
let Lib = "fsdsp56k_audio"

type Marker = class end

module Raw =
  [<DllImport(Lib)>]
  extern nativeint fsdsp56k_audio_open(uint32 sampleRate, uint32 capacityFrames)

  [<DllImport(Lib)>]
  extern uint32 fsdsp56k_audio_last_error(byte[] buffer, uint32 length)

  [<DllImport(Lib)>]
  extern uint32 fsdsp56k_audio_push(nativeint player, float32[] frames, uint32 count)

  [<DllImport(Lib)>]
  extern uint32 fsdsp56k_audio_queued(nativeint player)

  [<DllImport(Lib)>]
  extern uint64 fsdsp56k_audio_underruns(nativeint player)

  [<DllImport(Lib)>]
  extern uint32 fsdsp56k_audio_sample_rate(nativeint player)

  [<DllImport(Lib)>]
  extern uint32 fsdsp56k_audio_channels(nativeint player)

  [<DllImport(Lib)>]
  extern void fsdsp56k_audio_close(nativeint player)

let libraryFile =
  if OperatingSystem.IsWindows() then "fsdsp56k_audio.dll"
  elif OperatingSystem.IsMacOS() then "libfsdsp56k_audio.dylib"
  else "libfsdsp56k_audio.so"

/// Where the library is looked for, as for the emulator: $FSDSP56K_AUDIO (file or folder), then
/// native/target/release in the working directory, the assembly's folder or their parents, then
/// runtimes/<rid>/native in the assembly's folder.
let candidates () =
  [| match Option.ofObj (Environment.GetEnvironmentVariable "FSDSP56K_AUDIO") with
     | None | Some "" -> ()
     | Some p when Directory.Exists p -> Path.Combine(p, libraryFile)
     | Some p -> p
     for start in [| Environment.CurrentDirectory; AppContext.BaseDirectory |] do
       let mutable dir : DirectoryInfo | null = DirectoryInfo(start)
       while not (isNull dir) do
         let d = nonNull dir
         Path.Combine(d.FullName, "native", "target", "release", libraryFile)
         dir <- d.Parent
     Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", libraryFile) |]

let locate () = candidates () |> Array.tryFind File.Exists

let mutable registered = false
let gate = obj ()

/// Install the resolver (once, from any thread); true when the library was found.
let load () =
  lock gate (fun () ->
    if not registered then
      registered <- true
      NativeLibrary.SetDllImportResolver(typeof<Marker>.Assembly, DllImportResolver(fun name _ _ ->
        if name <> Lib then IntPtr.Zero
        else
          match locate () with
          | Some path -> NativeLibrary.Load path
          | None -> IntPtr.Zero))
    (locate ()).IsSome)

/// The default output device, asked for `sampleRate`, with a ring of `capacity` frames.
type Player(sampleRate: int, capacity: int) =
  do
    if not (load ()) then failwith "audio output missing: dotnet fsi native/build.fsx (or set FSDSP56K_AUDIO)"
  let handle =
    match Raw.fsdsp56k_audio_open(uint32 sampleRate, uint32 capacity) with
    | 0n ->
      let buffer = Array.zeroCreate<byte> 512
      let n = Raw.fsdsp56k_audio_last_error(buffer, uint32 buffer.Length)
      failwith $"no audio output: {Encoding.UTF8.GetString(buffer, 0, min (int n) (buffer.Length - 1))}"
    | h -> h
  let mutable closed = false

  /// The rate the device plays at (it may differ from the one asked for).
  member _.SampleRate = int (Raw.fsdsp56k_audio_sample_rate handle)
  member _.Channels = int (Raw.fsdsp56k_audio_channels handle)
  member _.Queued = int (Raw.fsdsp56k_audio_queued handle)
  member _.Underruns = int64 (Raw.fsdsp56k_audio_underruns handle)

  /// Push what fits; how many frames went in.
  member _.Push(frames: float32 array) = int (Raw.fsdsp56k_audio_push(handle, frames, uint32 frames.Length))

  /// Push every frame, waiting for room: the device's pace sets the caller's.
  member this.Write(frames: float32 array) =
    let mutable rest = frames
    while rest.Length > 0 do
      let n = this.Push rest
      rest <- rest[n..]
      if rest.Length > 0 then Thread.Sleep 1

  /// Wait until the ring has played out, or `timeout` passes.
  member this.Drain(timeout: TimeSpan) =
    let until = DateTime.UtcNow + timeout
    while this.Queued > 0 && DateTime.UtcNow < until do Thread.Sleep 5

  interface IDisposable with
    member _.Dispose() =
      if not closed then
        closed <- true
        Raw.fsdsp56k_audio_close handle

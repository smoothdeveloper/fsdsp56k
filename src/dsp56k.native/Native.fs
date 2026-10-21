/// In-process bindings to mborgerson/dsp56300 (MIT): its disassembler and assembler, used as an
/// oracle for the F# encoder, and its JIT emulator (validated against silicon), used to run
/// programs and count cycles. The C ABI is native/src/lib.rs (`cargo build --release` in native/).
module dsp56k.Native

open System
open System.IO
open System.Runtime.InteropServices
open System.Text

[<Literal>]
let Lib = "fsdsp56k_native"

type Marker = class end

module Raw =
  [<DllImport(Lib)>]
  extern uint32 fsd_disassemble(uint32 pc, uint32 op, uint32 next, byte[] out, unativeint cap)

  [<DllImport(Lib)>]
  extern int fsd_assemble_line(byte[] line, uint32 pc, uint32[] words, byte[] err, unativeint errcap)

  [<DllImport(Lib)>]
  extern unativeint fsd_assemble(byte[] source, byte[] out, unativeint cap, int& ok)

  [<DllImport(Lib)>]
  extern nativeint fsd_emu_create(uint32 pWords, uint32 xWords, uint32 yWords)

  [<DllImport(Lib)>]
  extern nativeint fsd_emu_create_bridged(uint32 pWords, uint32 xWords, uint32 yWords, uint32 boundary)

  [<DllImport(Lib)>]
  extern void fsd_emu_destroy(nativeint emu)

  [<DllImport(Lib)>]
  extern uint32 fsd_emu_read(nativeint emu, uint32 space, uint32 addr)

  [<DllImport(Lib)>]
  extern void fsd_emu_write(nativeint emu, uint32 space, uint32 addr, uint32 value)

  [<DllImport(Lib)>]
  extern uint32 fsd_emu_get_reg(nativeint emu, uint32 code)

  [<DllImport(Lib)>]
  extern void fsd_emu_set_reg(nativeint emu, uint32 code, uint32 value)

  [<DllImport(Lib)>]
  extern uint32 fsd_emu_get_pc(nativeint emu)

  [<DllImport(Lib)>]
  extern void fsd_emu_set_pc(nativeint emu, uint32 pc)

  [<DllImport(Lib)>]
  extern int fsd_emu_run_to_stop(nativeint emu, int maxCycles)

  [<DllImport(Lib)>]
  extern uint32 fsd_emu_cycles(nativeint emu)

  [<DllImport(Lib)>]
  extern int fsd_emu_profile_to_stop(nativeint emu, int maxCycles, uint32[] counts, uint32 len)

let libraryFile =
  if OperatingSystem.IsWindows() then "fsdsp56k_native.dll"
  elif OperatingSystem.IsMacOS() then "libfsdsp56k_native.dylib"
  else "libfsdsp56k_native.so"

/// Where the library is looked for: $FSDSP56K_NATIVE (file or folder), then native/target/release
/// in the working directory, the assembly's folder, or any of their parents, then runtimes/<rid>/native in the assembly's
/// folder (where a package reference puts the library the Dsp56k.Native package carries).
let candidates () =
  [| match Environment.GetEnvironmentVariable "FSDSP56K_NATIVE" with
     | null | "" -> ()
     | p when Directory.Exists p -> Path.Combine(p, libraryFile)
     | p -> p
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

let cstr (s: string) = Encoding.UTF8.GetBytes(s + "\000")

let text (buf: byte array) =
  let n = Array.IndexOf(buf, 0uy)
  Encoding.UTF8.GetString(buf, 0, (if n < 0 then buf.Length else n))

/// The oracle's text for one instruction, and the words it takes.
let disassemble pc (op: int) (next: int) =
  load () |> ignore
  let buf = Array.zeroCreate<byte> 256
  let n = Raw.fsd_disassemble(uint32 pc, uint32 op, uint32 next, buf, unativeint buf.Length)
  text buf, int n

/// A line as the oracle assembler reads it: no size forcing right after a memory space prefix.
let oracleSpelling (text: string) =
  [| "x:"; "y:"; "l:"; "p:" |]
  |> Array.fold (fun (t: string) s -> t.Replace(s + ">", s).Replace(s + "<", s)) text

type AssembleLineError = AssembleLineError of message: string

/// The oracle's encoding of one instruction line.
let assembleLine pc (line: string) =
  load () |> ignore
  let words = Array.zeroCreate<uint32> 2
  let err = Array.zeroCreate<byte> 512
  match Raw.fsd_assemble_line(cstr line, uint32 pc, words, err, unativeint err.Length) with
  | n when n > 0 -> Ok(words[.. n - 1] |> Array.map int)
  | _ -> Error(AssembleLineError(text err))

type Space =
  | X = 0u
  | Y = 1u
  | P = 2u

/// One DSP56300 with independent internal banks and optional shared external P/X/Y RAM.
type Emulator(pWords: int, xWords: int, yWords: int, ?externalMemory: int) =
  do load () |> ignore
  let handle =
    match externalMemory with
    | None -> Raw.fsd_emu_create(uint32 pWords, uint32 xWords, uint32 yWords)
    | Some boundary when boundary > 0 && boundary < min pWords (min xWords yWords) ->
      Raw.fsd_emu_create_bridged(uint32 pWords, uint32 xWords, uint32 yWords, uint32 boundary)
    | _ -> invalidArg "externalMemory" "Boundary must be inside all three banks"
  do if handle = 0n then failwith "Could not create DSP emulator"
  let mutable disposed = false

  /// The P, X and Y words it was made with.
  member _.Sizes = pWords, xWords, yWords
  member _.Read(space: Space, addr: int) = int (Raw.fsd_emu_read(handle, uint32 space, uint32 addr))
  member _.Write(space: Space, addr: int, value: int) = Raw.fsd_emu_write(handle, uint32 space, uint32 addr, uint32 (value &&& 0xffffff))
  member this.Load(space: Space, origin: int, words: int array) = words |> Array.iteri (fun i w -> this.Write(space, origin + i, w))

  /// Registers by their DDDDDD code (Encode.code6).
  member _.Reg(code: int) = int (Raw.fsd_emu_get_reg(handle, uint32 code))
  member _.SetReg(code: int, value: int) = Raw.fsd_emu_set_reg(handle, uint32 code, uint32 value)
  member _.Pc with get () = int (Raw.fsd_emu_get_pc handle) and set (v: int) = Raw.fsd_emu_set_pc(handle, uint32 v)
  member _.Cycles = int64 (Raw.fsd_emu_cycles handle)

  /// Run from the current PC until a STOP executes; the cycles it took, or None past the budget.
  member _.RunToStop(maxCycles: int) =
    let before = Raw.fsd_emu_cycles handle
    match Raw.fsd_emu_run_to_stop(handle, maxCycles) with
    | 1 -> Some(int64 (Raw.fsd_emu_cycles handle - before))
    | _ -> None

  /// Single-step from the current PC until a STOP, adding each instruction's cycles to
  /// `counts[pc]`; the cycles it took, or None past the budget.
  member _.ProfileToStop(maxCycles: int, counts: uint32 array) =
    let before = Raw.fsd_emu_cycles handle
    match Raw.fsd_emu_profile_to_stop(handle, maxCycles, counts, uint32 counts.Length) with
    | 1 -> Some(int64 (Raw.fsd_emu_cycles handle - before))
    | _ -> None

  /// Call a routine through a `jsr >routine` + `stop` stub at `stub`: the cycles from the JSR to
  /// the STOP, or None if it ran past the budget.
  member this.Call(routine: int, stub: int, maxCycles: int) =
    this.Write(Space.P, stub, 0x0bf080)          // jsr >routine
    this.Write(Space.P, stub + 1, routine)
    this.Write(Space.P, stub + 2, 0x000087)      // stop
    this.Pc <- stub
    this.RunToStop maxCycles

  /// As Call, with the cycles of every instruction added to `counts[pc]`.
  member this.CallProfiled(routine: int, stub: int, maxCycles: int, counts: uint32 array) =
    this.Write(Space.P, stub, 0x0bf080)
    this.Write(Space.P, stub + 1, routine)
    this.Write(Space.P, stub + 2, 0x000087)
    this.Pc <- stub
    this.ProfileToStop(maxCycles, counts)

  interface IDisposable with
    member _.Dispose() =
      if not disposed then
        disposed <- true
        Raw.fsd_emu_destroy handle

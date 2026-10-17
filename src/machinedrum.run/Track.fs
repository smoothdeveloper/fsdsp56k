/// One MDS machine on one track of an emulated DSP56300: the package loaded the way the
/// firmware loader does it (relocations applied, imports patched), the ABI's entry registers,
/// parameter conversion, and per-call cycle counts.
module machinedrum.run.Track

open dsp56k
open dsp56k.Native
open machinedrum

/// Memory map (the layout murdenihcam's simulator tests use).
type Layout =
  { programBase : int        // P: where the package's program is loaded
    workset     : int        // X/Y: the track's 64-word workset (R6)
    output      : int        // Y: the 32-word output buffer (R7, M7 = $1f), 32-aligned
    services    : int        // P: the service stand-ins
    stub        : int        // P: the jsr/stop call stub
    trackMemory : int        // X: track 0's external memory (import 6)
    sineTable   : int        // X: the sine resource (import symbol 1); its contents are the caller's
    inputRing   : int        // X: the 256-word input ring (symbol 2)
    inputPosition : int      // X: the input position word (symbol 3)
    mainRing    : int        // X: the 256-word main-output ring (symbol 4)
    mainPosition : int       // X: the main-output position word (symbol 5)
    tempX       : int        // X: the 32-word shared temporary memory (symbol 7)
    tempY       : int }      // Y: the same (symbol 8)

let defaultLayout =
  { programBase = 0x100; workset = 0x100; output = 0x200; services = 0xe00; stub = 0xef0; trackMemory = 0x10000; sineTable = 0
    inputRing = 0; inputPosition = 0; mainRing = 0; mainPosition = 0; tempX = 0; tempY = 0 }

type Bank =
  { words : int array
    step  : int
    origin: int }

/// A setting (0..127, fractions for modulation) as the firmware converts it for the DSP.
let convert (p: Mds.Param) (setting: float) =
  match p.transform with
  | Mds.Raw14 -> int (System.Math.Round(setting * 128.0))
  | Mds.UnipolarQ23 -> int (System.Math.Round(setting * 65536.0))
  | Mds.UnipolarSquareQ23 -> int (System.Math.Round((setting / 128.0) ** 2.0 * 8388608.0))
  | Mds.BipolarQ23 -> int (System.Math.Round((setting - 64.0) * 131072.0)) &&& 0xffffff
  | Mds.Index -> int setting - p.minValue
  | Mds.Boolean -> if setting >= 0.5 then 1 else 0
  | Mds.Pitch | Mds.Decay -> int (System.Math.Round(setting * 128.0))   // not modelled: tuning and decay laws

/// The cost of one call: the machine's own cycles (its program and the call stub), the firmware
/// services it called, and the declared-budget view: own cycles plus each service's documented
/// worst case.
type Cost =
  { own          : int64
    infoCalls    : int
    readCalls    : int
    readFrames   : int
    budget       : int64 }

type Track(package: Mds.Package, machine: Mds.Machine option, layout: Layout, banks: Map<int, Bank>) =
  let emu = new Emulator(0x10000, 0x20000, 0x10000)
  let services = Assembler.assembleItems (Services.program layout.services) |> Result.defaultWith (fun e -> failwith (Assembler.errorText e))
  let serviceAt name = int services.symbols[name]
  do
    // the package, relocated and patched
    let words = Array.copy package.programWords
    for w in package.relocations do words[w] <- (words[w] + layout.programBase) &&& 0xffffff
    for w, kind, symbol in package.imports do
      let resourceBase =
        match kind, symbol with
        | 5, 9 -> serviceAt "SAMPLE_INFO"
        | 5, 10 -> serviceAt "SAMPLE_READ"
        | 1, 1 -> layout.sineTable
        | 1, 2 -> layout.inputRing
        | 1, 3 -> layout.inputPosition
        | 1, 4 -> layout.mainRing
        | 1, 5 -> layout.mainPosition
        | 2, 6 -> layout.trackMemory
        | 3, _ -> layout.tempX                              // temporary areas, X/Y:$0000 by default
        | 4, _ -> layout.tempY
        | _ -> 0
      words[w] <- (words[w] + resourceBase) &&& 0xffffff
    emu.Load(Space.P, layout.programBase, words)
    let org, serviceWords = Assembler.image dsp56k.Space.P services
    emu.Load(Space.P, org, serviceWords)
    for KeyValue(bank, b) in banks do
      emu.Write(Space.X, Services.info + 3 * bank, b.words.Length)
      emu.Write(Space.X, Services.info + 3 * bank + 1, b.step)
      emu.Write(Space.X, Services.info + 3 * bank + 2, b.origin)
      emu.Load(Space.X, b.origin, b.words)

  member _.Emulator = emu
  member _.Layout = layout

  /// Write a parameter's converted value (index 0..7) to Y:(R6+1+index).
  member _.SetParameter(index: int, value: int) = emu.Write(Space.Y, layout.workset + 1 + index, value)

  /// Set parameters from front-panel settings, converted by the machine's transforms.
  member this.SetSettings(settings: float array) =
    match machine with
    | Some m -> settings |> Array.iteri (fun i s -> this.SetParameter(i, convert m.parameters[i] s))
    | None -> failwith "settings need the machine definition"

  member _.Call(entry: int, track: int) =
    // entry registers: R6 workset, R7 output (M7 = $1f), R1 track, M0..M5 linear
    emu.SetReg(22, layout.workset)
    emu.SetReg(23, layout.output)
    emu.SetReg(39, 0x1f)
    emu.SetReg(17, track)
    for m in 32 .. 37 do emu.SetReg(m, 0xffffff)
    match emu.Call(layout.programBase + entry, layout.stub, 2_000_000) with
    | Some c -> c
    | None -> failwith "the routine did not return"

  /// A call single-stepped: its cost and the cycles per program address.
  member _.CallProfiled(entry: int, track: int) =
    emu.SetReg(22, layout.workset)
    emu.SetReg(23, layout.output)
    emu.SetReg(39, 0x1f)
    emu.SetReg(17, track)
    for m in 32 .. 37 do emu.SetReg(m, 0xffffff)
    for c in [| Services.infoCalls; Services.readCalls; Services.readFrames |] do emu.Write(Space.X, c, 0)
    let counts = Array.zeroCreate<uint32> 0x10000
    match emu.CallProfiled(layout.programBase + entry, layout.stub, 2_000_000, counts) with
    | None -> failwith "the routine did not return"
    | Some _ ->
      let inRange lo hi = seq { lo .. hi - 1 } |> Seq.sumBy (fun pc -> int64 counts[pc])
      let own = inRange layout.programBase (layout.programBase + package.programWords.Length) + inRange layout.stub (layout.stub + 3)
      let info = emu.Read(Space.X, Services.infoCalls)
      let reads = emu.Read(Space.X, Services.readCalls)
      let frames = emu.Read(Space.X, Services.readFrames)
      let worst = int64 (info * Abi.serviceCycles Abi.SampleInfo 0) + int64 (reads * 202 + 33 * frames)
      { own = own; infoCalls = info; readCalls = reads; readFrames = frames; budget = own + worst }, counts

  member this.Init(track) = this.Call(package.init, track)
  member this.Trig(track) = this.Call(package.trig, track)

  /// One RENDER: the 32 output words and the cycles it took (JSR to STOP).
  member this.Render(track) =
    let cycles = this.Call(package.render, track)
    [| for i in 0 .. 31 -> emu.Read(Space.Y, layout.output + i) |], cycles

  interface System.IDisposable with
    member _.Dispose() = (emu :> System.IDisposable).Dispose()

/// Signed 24-bit word to float.
let toFloat (w: int) = float (if w &&& 0x800000 <> 0 then w - 0x1000000 else w) / 8388608.0

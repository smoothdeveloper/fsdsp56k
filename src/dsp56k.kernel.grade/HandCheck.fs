/// The hand-written helpers of a kernel ([<HandWritten>]) checked one by one. On its own, each
/// routine and its F# reference compiled as a subroutine are called on the emulator with the same
/// arguments, both compared word for word with the reference run as .NET code, and each call's
/// cycles kept. In the kernel, the grade's scenario runs again with that one helper compiled from
/// its F#: the RENDER cycles its routine saves where the kernel calls it.
module dsp56k.kernel.HandCheck

open System
open dsp56k
open dsp56k.Native
open dsp56k.kernel
open dsp56k.kernel.Ir

/// The cycles of the calls: the fewest, the most, the mean (from the call to the return).
type Calls =
  { least : int64
    most  : int64
    mean  : float }

/// One helper on its own.
type Alone =
  { helper     : string
    routine    : string
    arguments  : int              // the argument sets each was called with
    hand       : int              // of them, the hand routine's results equal to the .NET run's
    compiled   : int option       // the F# reference compiled, the same (None: not compiled alone)
    firstWrong : string option    // the first set the hand routine differs on
    handCycles : Calls option
    fsCycles   : Calls option
    skipped    : string option }  // why it is checked in the kernel only

/// One helper in the kernel.
type InKernel =
  { helper     : string
    routine    : string
    correct    : bool             // with its F# compiled instead, still the .NET run's words
    fsMean     : float            // RENDER mean and worst cycles with its F# compiled instead
    fsWorst    : int64
    handMean   : float            // with every routine
    handWorst  : int64 }

/// The kernel's hand-written helpers, in the order its entries reach them.
let helpers (k: Kernel.Kernel) =
  let ik = FromQuotation.kernel k
  Compile.helperUses ik
  |> List.map _.helper
  |> List.filter (fun h -> h.Marks.Contains "Hand" && (Hand.find h.Key).IsSome)
  |> List.distinctBy _.Key

let private sext (bits: int) (v: int64) = (v <<< (64 - bits)) >>> (64 - bits)

/// Arguments: the edge words first (every pair of them for two arguments), then random words.
let private edges = [| 0; 1; -1; 0x7fffff; -0x800000; 0x400000; -0x400000; 0x200000; -0x200000; 0x7ffffe; -0x7fffff; 0x100; -0x100 |]

let private value (v: 'T when 'T : not null) : obj = v :> obj

let private argument (ty: IrTy) (rng: Random) (edge: int option) : obj =
  let word () = match edge with Some e -> e | None -> int (sext 24 (int64 (rng.Next 0x1000000)))
  match ty with
  | TyFrac -> value (Frac.ofRaw (word ()))
  | TyAcc ->
    let hi = int64 (word ())
    let lo = int64 (rng.Next 0x1000000)
    // one in four beyond the words' range, the extension byte in use
    let raw = if rng.Next 4 = 0 then (hi <<< 25) ||| lo else (hi <<< 24) ||| lo
    value (Acc.ofRaw raw)
  | TyLong -> value (Long.ofRaw ((int64 (word ()) <<< 24) ||| int64 (rng.Next 0x1000000)))
  | other -> failwith $"no arguments of {IrTy.name other}"

let private rawOf (v: obj) =
  match v with
  | :? Frac as f -> int64 f.Raw
  | :? Acc as a -> a.Raw
  | :? Long as l -> l.Raw
  | other -> failwith $"not a Frac, Acc or Long: {other}"

let private show (ty: IrTy) (raw: int64) =
  match ty with
  | TyFrac -> $"${raw &&& 0xffffffL:x6}"
  | TyAcc -> $"${(raw >>> 48) &&& 0xffL:x2}:{(raw >>> 24) &&& 0xffffffL:x6}:{raw &&& 0xffffffL:x6}"
  | _ -> $"${(raw >>> 24) &&& 0xffffffL:x6}:{raw &&& 0xffffffL:x6}"

let private code (r: Reg) = (Encode.code6 r).Value

let private setArgs (emu: Emulator) (ps: (IrTy * obj) list) =
  for r in [ X0; X1; Y0; Y1; A0; A1; A2; B0; B1; B2 ] do emu.SetReg(code r, 0)
  let fracs = ps |> List.filter (fun (t, _) -> t = TyFrac) |> List.map snd
  let accs = ps |> List.filter (fun (t, _) -> t <> TyFrac)
  List.iteri (fun i (v: obj) -> emu.SetReg(code Compile.fracArgs[i], int ((v :?> Frac).Word))) fracs
  accs |> List.iteri (fun i (t, v) ->
    let raw = rawOf v
    let e, hi, lo = Compile.parts Compile.accArgs[i]
    let ext = if t = TyLong then (if raw < 0L then 0xff else 0) else int ((raw >>> 48) &&& 0xffL)
    emu.SetReg(code e, ext)
    emu.SetReg(code hi, int ((raw >>> 24) &&& 0xffffffL))
    emu.SetReg(code lo, int (raw &&& 0xffffffL)))

let private result (emu: Emulator) (ty: IrTy) =
  match ty with
  | TyFrac -> sext 24 (int64 (emu.Reg(code X0)))
  | TyAcc -> sext 56 ((int64 (emu.Reg(code A2) &&& 0xff) <<< 48) ||| (int64 (emu.Reg(code A1)) <<< 24) ||| int64 (emu.Reg(code A0)))
  | _ -> sext 48 ((int64 (emu.Reg(code A1)) <<< 24) ||| int64 (emu.Reg(code A0)))

/// Words a program loads at its origin, and its labels' addresses.
let private image (items: Item array) =
  match Assembler.assembleItems items with
  | Error e -> Error(Assembler.errorText e)
  | Ok r ->
    let origin, words = Assembler.image dsp56k.Space.P r
    Ok(int origin, words, r.symbols)

let private workset = 0x800
let private stub = 0x3ff0

/// The routine at `label` of `items` called on each argument set: each result and its cycles.
let private calls (items: Item array) (label: string) (returns: IrTy) (sets: (IrTy * obj) list list) =
  let empty = "K_HAND_CHECK_EMPTY"
  image (Array.append items [| Label empty; Op Rts |])
  |> Result.bind (fun (origin, words, symbols) ->
    match symbols.TryFind label, symbols.TryFind empty with
    | Some at, Some none ->
      use emu = new Emulator(0x4000, 0x4000, 0x4000)
      emu.Load(Space.P, origin, Array.map int words)
      for k in 0 .. 7 do emu.SetReg(code (M k), 0xffffff)
      emu.SetReg(code (R 6), workset)
      let overhead = (emu.Call(int none, stub, 1000)).Value
      let budget = 200000
      Ok
        [ for set in sets ->
            setArgs emu set
            emu.SetReg(code (R 6), workset)
            match emu.Call(int at, stub, budget) with
            | Some cycles -> result emu returns, cycles - overhead
            | None -> failwith $"{label}: no return within {budget} cycles" ]
    | _ -> Error $"{label}: not in the program")

let private callsOf (results: (int64 * int64) list) =
  let cs = results |> List.map snd
  { least = List.min cs; most = List.max cs; mean = List.averageBy float cs }

/// Each hand-written helper of `k` that takes only Frac, Acc and Long values, on `count` argument
/// sets (from `seed`).
let alone (target: Kernel.Target) (count: int) (seed: int) (k: Kernel.Kernel) : Alone list =
  let ik = FromQuotation.kernel k
  [ for h in helpers k do
      let r = (Hand.find h.Key).Value
      let ps, _ = Compile.parametersOf (Compile.reflected h).Value
      let value (t: IrTy) = t = TyFrac || t = TyAcc || t = TyLong
      let fracs = ps |> List.filter (fun v -> v.Type = TyFrac) |> List.length
      let accs = ps |> List.filter (fun v -> v.Type = TyAcc || v.Type = TyLong) |> List.length
      let skip why =
        { helper = h.Name; routine = r.name; arguments = 0; hand = 0; compiled = None; firstWrong = None
          handCycles = None; fsCycles = None; skipped = Some why }
      if ps |> List.exists (fun v -> not (value v.Type)) then
        let others = ps |> List.filter (fun v -> not (value v.Type)) |> List.map (fun v -> IrTy.name v.Type) |> List.distinct |> String.concat ", "
        yield skip $"takes {others}: checked in the kernel"
      elif not (value h.ReturnType) then yield skip "gives no value: checked in the kernel"
      elif fracs > Compile.fracArgs.Length || accs > Compile.accArgs.Length then yield skip "more arguments than registers"
      else
        let rng = Random(seed: int)
        let n = ps.Length
        let corners = pown edges.Length n
        let sets =
          [ for i in 0 .. count - 1 ->
              ps |> List.mapi (fun j v ->
                let edge = if i < corners then Some edges[(i / pown edges.Length j) % edges.Length] else None
                v.Type, argument v.Type rng edge) ]
        let expected = sets |> List.map (fun set -> rawOf (h.Invoke(set |> List.map snd |> Array.ofList)))
        let routine = Hand.relabel (fun l -> l + "_ALONE") (Hand.withoutRts (Hand.items r))
        let handItems = Array.concat [ [| Label "K_HAND_ALONE" |]; routine; [| Op Rts |] ]
        let hand = calls handItems "K_HAND_ALONE" h.ReturnType sets
        // the F# reference compiled as the kernel compiles it, as a subroutine
        let fs =
          Compile.generateIrWith (Set.singleton h.Key) (Set.singleton h.Key) None target ik
          |> Result.bind (fun (c, _) ->
            let items, _ = Optimize.runFrom Optimize.Peephole c.layout.xWorkset c.xWorkset c.items c.lines
            let prefix = "SUB_" + String(h.Name |> Seq.map (fun c -> if Char.IsLetterOrDigit c then Char.ToUpperInvariant c else '_') |> Array.ofSeq) + "_"
            match items |> Array.tryPick (fun it -> match it with Label l when l.StartsWith prefix -> Some l | _ -> None) with
            | Some label -> calls items label h.ReturnType sets
            | None -> Error "not compiled as a subroutine")
        match hand with
        | Error e -> yield skip $"the routine does not assemble alone: {e}"
        | Ok hand ->
          let same (results: (int64 * int64) list) = List.zip expected results |> List.filter (fun (e, (g, _)) -> e = g) |> List.length
          let wrong =
            List.zip3 sets expected hand |> List.tryFind (fun (_, e, (g, _)) -> e <> g) |> Option.map (fun (set, e, (g, _)) ->
              let args = set |> List.map (fun (t, v) -> show t (rawOf v)) |> String.concat ", "
              $"({args}): expected {show h.ReturnType e}, got {show h.ReturnType g}")
          yield
            { helper = h.Name; routine = r.name; arguments = sets.Length; hand = same hand
              compiled = (match fs with Ok f -> Some(same f) | Error _ -> None); firstWrong = wrong
              handCycles = Some(callsOf hand); fsCycles = (match fs with Ok f -> Some(callsOf f) | Error _ -> None)
              skipped = None } ]

/// Each hand-written helper of `k` in the kernel: the grade's scenario with that helper's F#
/// compiled instead, against `all`, the grade with every routine.
let inKernel (grade: Set<string> -> Result<Grade.Report, string>) (all: Grade.Report) (k: Kernel.Kernel) : Result<InKernel list, string> =
  helpers k
  |> List.map (fun h ->
    grade (Set.singleton h.Key) |> Result.map (fun fs ->
      { helper = h.Name; routine = (Hand.find h.Key).Value.name; correct = List.isEmpty fs.correctness
        fsMean = fs.ours.renderMean; fsWorst = fs.ours.renderWorst
        handMean = all.ours.renderMean; handWorst = all.ours.renderWorst }))
  |> List.fold (fun acc r -> match acc, r with Ok xs, Ok x -> Ok(xs @ [ x ]) | Error e, _ | _, Error e -> Error e) (Ok [])

let private cycles (c: Calls) = if c.least = c.most then $"{c.most}" else $"{c.least}..{c.most} ({c.mean:F1})"

/// The tables, as `fsdsp56k grade` prints them.
let text (inside: InKernel list) (alone: Alone list) =
  [| if not inside.IsEmpty then
       yield "  hand-written helpers in the kernel, RENDER cycles with the helper's F# compiled instead:"
       yield sprintf "    %-12s %-28s %10s %10s %8s %8s  %s" "helper" "routine" "F# mean" "hand mean" "saved" "worst" "F# instead"
       for r in inside do
         yield sprintf "    %-12s %-28s %10.1f %10.1f %8.1f %8d  %s" r.helper r.routine r.fsMean r.handMean (r.fsMean - r.handMean) (r.fsWorst - r.handWorst)
                 (if r.correct then "matches .NET" else "DIFFERS from .NET")
     if not alone.IsEmpty then
       yield "  hand-written helpers on their own, each call against the F# run as .NET code:"
       yield sprintf "    %-12s %-28s %13s %16s %16s %7s" "helper" "routine" "hand matches" "F# cycles" "hand cycles" "saved"
       for a in alone do
         match a.skipped with
         | Some why -> yield sprintf "    %-12s %-28s %s" a.helper a.routine why
         | None ->
           let fsText = a.fsCycles |> Option.map cycles |> Option.defaultValue "-"
           let handText = a.handCycles |> Option.map cycles |> Option.defaultValue "-"
           let saved = match a.fsCycles, a.handCycles with Some f, Some h -> sprintf "%.1f" (f.mean - h.mean) | _ -> "-"
           yield sprintf "    %-12s %-28s %13s %16s %16s %7s" a.helper a.routine $"{a.hand}/{a.arguments}" fsText handText saved
           match a.compiled with
           | Some c when c <> a.arguments -> yield $"      the F# compiled alone matches on {c}/{a.arguments}"
           | _ -> ()
           match a.firstWrong with
           | Some w -> yield $"      first difference {w}"
           | None -> () |]
  |> String.concat "\n"

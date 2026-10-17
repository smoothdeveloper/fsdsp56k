/// Passes over the compiled items, each keeping the program's meaning:
///
/// - value numbering inside straight-line code: a move whose destination already holds the
///   value is dropped (a reload just stored, an Acc local's three words reloaded into the
///   unchanged accumulator, a store of what the word holds), and a load whose value a data
///   register holds becomes a register move (which can then be packed);
/// - dead stores to the compiler's temporaries: words of the X workset no instruction reads;
/// - packing moves into the parallel slots of neighbouring instructions (Lower.pack).
///
/// Labels, branches, loops and anything not modelled end what is known.
module dsp56k.kernel.Optimize

open System.Collections.Generic
open dsp56k
open dsp56k.dsl
open dsp56k.dsl.Asm

/// What holds a value: a register part, or a word at (r6+k) in X or Y.
type Slot =
  | SReg of Reg
  | SMem of Space * int

/// The (r6+k) word a memory operand names, when it names one.
let workset (space: Space) (ea: Ea) =
  match ea with
  | Disp(6, { expr = Num k }) when space = Space.X || space = Space.Y -> Some(SMem(space, int k))
  | _ -> None

/// The parts a register write touches.
let parts (r: Reg) =
  match r with
  | A -> [ A0; A1; A2 ]
  | B -> [ B0; B1; B2 ]
  | A10 -> [ A0; A1 ]
  | B10 -> [ B0; B1 ]
  | AB | BA -> [ A0; A1; A2; B0; B1; B2 ]
  | X -> [ X0; X1 ]
  | Y -> [ Y0; Y1 ]
  | r -> [ r ]

let isWord (r: Reg) =
  match r with
  | X0 | X1 | Y0 | Y1 | A0 | A1 | B0 | B1 | R _ | N _ -> true
  | _ -> false

let accOf (r: Reg) =
  match r with
  | A | A0 | A1 | A2 | A10 -> Some A
  | B | B0 | B1 | B2 | B10 -> Some B
  | _ -> None

/// Value numbers of the slots in one straight-line stretch.
type Facts() =
  let ids = Dictionary<Slot, int>()
  let loaded = Dictionary<Reg, int>()     // a whole accumulator loaded from a word (a1 = v, a0 = 0, a2 its sign)
  let mutable next = 0
  let consts = Dictionary<int64, int>()

  member _.Fresh() =
    next <- next + 1
    next

  member this.Id(s: Slot) =
    match ids.TryGetValue s with
    | true, v -> v
    | _ ->
      let v = this.Fresh()
      ids[s] <- v
      v

  member this.Const(v: int64) =
    match consts.TryGetValue v with
    | true, id -> id
    | _ ->
      let id = this.Fresh()
      consts[v] <- id
      id

  member _.Known(s: Slot) = ids.ContainsKey s
  member _.Get(s: Slot) = match ids.TryGetValue s with | true, v -> Some v | _ -> None
  member _.Set(s: Slot, v: int) = ids[s] <- v
  member _.Forget(s: Slot) = ids.Remove s |> ignore

  member this.WriteReg(r: Reg) =
    for p in parts r do ids.Remove(SReg p) |> ignore
    match accOf r with
    | Some acc -> loaded.Remove acc |> ignore
    | None -> ()

  member _.Loaded(acc: Reg) = match loaded.TryGetValue acc with | true, v -> Some v | _ -> None
  member _.SetLoaded(acc: Reg, v: int) = loaded[acc] <- v

  member _.ForgetSpace(space: Space) =
    for k in List.ofSeq ids.Keys do
      match k with
      | SMem(s, _) when s = space -> ids.Remove k |> ignore
      | _ -> ()

  member _.Clear() =
    ids.Clear()
    loaded.Clear()

  /// A data register holding value v, other than `except`.
  member _.Holder(v: int, except: Reg) =
    [ X0; X1; Y0; Y1; A1; B1 ]
    |> List.tryFind (fun r -> r <> except && (match ids.TryGetValue(SReg r) with | true, w -> w = v | _ -> false))

let immValue (op: Operand) = match op.expr with Num v -> Some v | _ -> None

/// The effect of a parallel instruction's moves on the facts (sources read before any write).
let applyMoves (f: Facts) (alu: Alu option) (moves: Move array) =
  let reads =
    moves |> Array.map (fun m ->
      match m with
      | Load(space, ea, _) -> workset space ea |> Option.map f.Id
      | Copy(src, _) when isWord src || src = A2 || src = B2 -> Some(f.Id(SReg src))
      | Store(src, _, _) when isWord src || src = A2 || src = B2 -> Some(f.Id(SReg src))
      // only long immediates and zero: a short one lands in the MSBs of a data register
      | Imm(op, _) when op.force = Long || immValue op = Some 0L -> immValue op |> Option.map f.Const
      | _ -> None)
  alu |> Option.iter (fun a -> for w in Lower.aluWrites a do f.WriteReg w)
  moves |> Array.iteri (fun i m ->
    match m with
    | Load(space, ea, dst) ->
      f.WriteReg dst
      // (rn)+ and friends move the register
      for r in Lower.eaWrites ea do f.WriteReg r
      match dst, reads[i] with
      | (A | B), Some v ->
        f.Set(SReg(if dst = A then A1 else B1), v)
        f.Set(SReg(if dst = A then A0 else B0), f.Const 0L)
        f.SetLoaded(dst, v)
      | d, Some v when isWord d || d = A2 || d = B2 -> f.Set(SReg d, v)
      | _ -> ()
    | Store(src, space, ea) ->
      for r in Lower.eaWrites ea do f.WriteReg r
      match workset space ea with
      | Some slot ->
        match reads[i] with
        | Some v -> f.Set(slot, v)
        | None -> f.Forget slot            // a limited accumulator: unknown word
      | None -> f.ForgetSpace space        // a word elsewhere, maybe a state array element
    | Copy(_, dst) | Imm(_, dst) ->
      f.WriteReg dst
      match dst, reads[i] with
      | (A | B), Some v ->
        f.Set(SReg(if dst = A then A1 else B1), v)
        f.Set(SReg(if dst = A then A0 else B0), f.Const 0L)
        f.SetLoaded(dst, v)
      | d, Some v when isWord d || d = A2 || d = B2 -> f.Set(SReg d, v)
      | _ -> ()
    | Update ea -> for r in Lower.eaWrites ea do f.WriteReg r)

/// The registers a data ALU instruction outside the parallel class writes (None: not one).
let aluWrites (instr: Instr) =
  match instr with
  | AluImm(_, _, d) | ShiftImm(_, _, _, d) | ShiftReg(_, _, _, d) | MulImm(_, _, _, _, d)
  | MulSu(_, _, _, _, _, d) | MulI(_, _, _, _, d) | Div(_, d) | Inc d | Dec d -> Some [ Acc.reg d ]
  | IfCc(alu, _, _) -> Some(List.ofArray (Lower.aluWrites alu))
  | Tcc(_, alu, agu) -> Some([ for _, d in Option.toList alu -> Acc.reg d ] @ [ for _, r in Option.toList agu -> R r ])
  | Norm(r, d) -> Some [ Acc.reg d; R r ]
  | Nop -> Some []
  | _ -> None

/// Value numbering over straight-line stretches: moves that change nothing are dropped, loads
/// of a value a data register holds become register moves.
let forward (items: (Item * int) array) =
  let f = Facts()
  let out = ResizeArray<Item * int>()
  let mutable i = 0
  let single (it: Item) =
    match it with
    | Op(Par(None, [| m |])) -> Some m
    | _ -> None
  while i < items.Length do
    let it, line = items[i]
    // an Acc's three words reloaded into the accumulator they were stored from
    let triple =
      if i + 2 < items.Length then
        match single (fst items[i]), single (fst items[i + 1]), single (fst items[i + 2]) with
        | Some(Load(s1, e1, ((A | B) as acc))), Some(Load(s2, e2, lo)), Some(Load(s3, e3, ext))
            when (lo = (if acc = A then A0 else B0)) && (ext = (if acc = A then A2 else B2)) ->
          match workset s1 e1, workset s2 e2, workset s3 e3 with
          | Some m1, Some m2, Some m3 ->
            let hi = if acc = A then A1 else B1
            let same r m = match f.Get(SReg r), f.Get m with | Some a, Some b -> a = b | _ -> false
            same hi m1 && same lo m2 && same ext m3
          | _ -> false
        | _ -> false
      else false
    if triple then i <- i + 3
    else
      match it with
      | Label _ | Dc _ | Ds _ | Org _ | Equ _ ->
        f.Clear()
        out.Add(it, line)
        i <- i + 1
      | Comment _ ->
        out.Add(it, line)
        i <- i + 1
      | Op(Par(alu, moves)) ->
        let keep =
          match alu, moves with
          | None, [| Load(space, ea, dst) |] when isWord dst ->
            match workset space ea |> Option.bind f.Get, f.Get(SReg dst) with
            | Some v, Some w when v = w -> None                                   // already there
            | Some v, _ ->
              match f.Holder(v, dst) with
              | Some r -> Some(Op(Par(None, [| Copy(r, dst) |])))                  // from a register
              | None -> Some it
            | _ -> Some it
          | None, [| Load(space, ea, ((A | B) as acc)) |] ->
            match workset space ea |> Option.bind f.Get, f.Loaded acc with
            | Some v, Some w when v = w -> None
            | _ -> Some it
          | None, [| Copy(src, dst) |] when isWord src && isWord dst ->
            match f.Get(SReg src), f.Get(SReg dst) with
            | Some v, Some w when v = w -> None
            | _ -> Some it
          | None, [| Store(src, space, ea) |] when isWord src || src = A2 || src = B2 ->
            match workset space ea |> Option.bind f.Get, f.Get(SReg src) with
            | Some v, Some w when v = w -> None
            | _ -> Some it
          | _ -> Some it
        match keep with
        | Some(Op(Par(alu, moves)) as k) ->
          applyMoves f alu moves
          out.Add(k, line)
        | Some k -> out.Add(k, line)
        | None -> ()
        i <- i + 1
      | Op instr ->
        match aluWrites instr with
        | Some ws -> for w in ws do f.WriteReg w
        | None -> f.Clear()           // loops, branches, calls and anything else: nothing is known after
        out.Add(it, line)
        i <- i + 1
  out.ToArray()

/// Successors of each item: the next one, branch targets, a DO loop's last instruction back to
/// the loop's first. None when the program jumps somewhere this cannot follow.
let successors (items: Item array) =
  let labels = Dictionary<string, int>()
  items |> Array.iteri (fun i it -> match it with Label n -> labels[n] <- i | _ -> ())
  let target (op: Operand) = match op.expr with Sym n when labels.ContainsKey n -> Some labels[n] | _ -> None
  let succ = Array.init items.Length (fun i -> ResizeArray<int>())
  let mutable followable = true
  for i in 0 .. items.Length - 1 do
    let next () = if i + 1 < items.Length then succ[i].Add(i + 1)
    match items[i] with
    | Op(Branch(Bra, t)) | Op(Jump(Jmp, t)) ->
      match target t with Some j -> succ[i].Add j | None -> followable <- false
    | Op(Branch((Bcc _ | Bscc _), t)) | Op(Jump((Jcc _ | Jscc _), t)) | Op(BitBranch(_, _, _, t)) ->
      next ()
      match target t with Some j -> succ[i].Add j | None -> followable <- false
    | Op Rts | Op Rti | Op Stop | Dc _ | Ds _ -> ()
    | Op(JumpEa _) | Op(BranchRn _) -> followable <- false
    | Op(Loop(_, _, endLabel)) ->
      next ()
      match target endLabel with
      | Some e ->
        // the body's last instruction goes back to the body's first
        let last = [ i + 1 .. e - 1 ] |> List.rev |> List.tryFind (fun k -> match items[k] with Op _ -> true | _ -> false)
        match last with
        | Some k -> succ[k].Add(i + 1)
        | None -> ()
      | None -> followable <- false
    | _ -> next ()
  if followable then Some(succ |> Array.map (fun l -> l.ToArray())) else None

/// Stores to the compiler's temporaries (X workset words below `temps`) that no path reads
/// before the word is written again.
let deadTemps (temps: int) (items: (Item * int) array) =
  let its = Array.map fst items
  let temp (space: Space) (ea: Ea) =
    match workset space ea with
    | Some(SMem(Space.X, k)) when k < temps -> Some k
    | _ -> None
  let uses i =
    match its[i] with
    | Op(Par(_, moves)) -> moves |> Array.choose (function Load(sp, ea, _) -> temp sp ea | _ -> None) |> Set.ofArray
    | _ -> Set.empty
  let defs i =
    match its[i] with
    | Op(Par(_, moves)) -> moves |> Array.choose (function Store(_, sp, ea) -> temp sp ea | _ -> None) |> Set.ofArray
    | _ -> Set.empty
  match successors its with
  | None -> items
  | Some succ ->
    let n = its.Length
    let liveIn = Array.create n Set.empty
    let liveOut = Array.create n Set.empty
    let u = Array.init n uses
    let d = Array.init n defs
    let mutable changed = true
    while changed do
      changed <- false
      for i in n - 1 .. -1 .. 0 do
        let out = succ[i] |> Array.fold (fun acc j -> Set.union acc liveIn[j]) Set.empty
        let inn = Set.union u[i] (Set.difference out d[i])
        if out <> liveOut[i] || inn <> liveIn[i] then
          liveOut[i] <- out
          liveIn[i] <- inn
          changed <- true
    items |> Array.mapi (fun i x -> i, x) |> Array.filter (fun (i, _) ->
      match its[i] with
      | Op(Par(None, [| Store(_, sp, ea) |])) ->
        match temp sp ea with
        | Some k -> liveOut[i].Contains k
        | None -> true
      | _ -> true) |> Array.map snd

/// Registers an instruction reads, as far as this pass needs (None: not known).
let readsOf (instr: Instr) =
  match instr with
  | Par(alu, moves) ->
    Some(Array.append (alu |> Option.map Lower.aluReads |> Option.defaultValue [||]) (Array.collect Lower.moveReads moves))
  | MulI(_, _, _, src, d) | MulImm(_, _, src, _, d) -> Some [| src; Acc.reg d |]
  | MulSu(_, _, _, s1, s2, d) -> Some [| s1; s2; Acc.reg d |]
  | Div(src, d) -> Some [| src; Acc.reg d |]
  | AluImm(_, _, d) | ShiftImm(_, _, _, d) | Inc d | Dec d -> Some [| Acc.reg d |]
  | Nop -> Some [||]
  | _ -> None

let writesOf (instr: Instr) =
  match instr with
  | Par(alu, moves) ->
    Some(Array.append (alu |> Option.map Lower.aluWrites |> Option.defaultValue [||]) (Array.collect Lower.moveWrites moves))
  | i -> aluWrites i |> Option.map List.toArray

/// Whether `r` is overwritten before anything reads it, from item `from` on, in straight-line code.
let deadAfter (items: (Item * int) array) (from: int) (r: Reg) =
  let rec go k =
    if k >= items.Length then false
    else
      match fst items[k] with
      | Comment _ -> go (k + 1)
      | Op instr ->
        match readsOf instr, writesOf instr with
        | Some rs, Some ws ->
          if rs |> Array.exists (fun x -> List.contains r (parts x) || List.contains x (parts r)) then false
          elif ws |> Array.exists (fun x -> parts x = parts r || x = r) then true
          else go (k + 1)
        | _ -> false
      | _ -> false
  go from

/// `move s,x0` `move x0,d` with x0 dead afterwards: `move s,d`, when that encodes.
let copyForward (items: (Item * int) array) =
  let out = ResizeArray<Item * int>()
  let mutable i = 0
  while i < items.Length do
    let fused =
      if i + 1 < items.Length then
        match fst items[i], fst items[i + 1] with
        | Op(Par(None, [| first |])), Op(Par(None, [| second |])) ->
          let combined =
            match first, second with
            | Load(sp, ea, r), Copy(r', d) when r = r' && (r = X0 || r = Y0) && d <> A2 && d <> B2 -> Some(r, Load(sp, ea, d))
            | Copy(src, r), Copy(r', d) when r = r' && (r = X0 || r = Y0) && not ((src = A || src = B) && (d = A || d = B)) && d <> A2 && d <> B2 -> Some(r, Copy(src, d))
            | Copy(src, r), Store(r', sp, ea) when r = r' && (r = X0 || r = Y0) -> Some(r, Store(src, sp, ea))
            | Imm(op, r), Copy(r', d) when r = r' && (r = X0 || r = Y0) && op.force = Long && d <> A2 && d <> B2 -> Some(r, Imm(op, d))
            | _ -> None
          match combined with
          | Some(r, m) when Lower.encodes (Par(None, [| m |])) && deadAfter items (i + 2) r -> Some(Op(Par(None, [| m |])))
          | _ -> None
        | _ -> None
      else None
    match fused with
    | Some it ->
      out.Add(it, snd items[i])
      i <- i + 2
    | None ->
      out.Add items[i]
      i <- i + 1
  out.ToArray()

/// (Superseded by the liveness version above; kept for reference.)
let deadTempsGlobal (temps: int) (items: (Item * int) array) =
  let read = HashSet<int>()
  for it, _ in items do
    match it with
    | Op(Par(_, moves)) ->
      for m in moves do
        match m with
        | Load(Space.X, ea, _) -> workset Space.X ea |> Option.iter (function SMem(_, k) -> read.Add k |> ignore | _ -> ())
        | _ -> ()
    | _ -> ()
  items |> Array.filter (fun (it, _) ->
    match it with
    | Op(Par(None, [| Store(_, Space.X, ea) |])) ->
      match workset Space.X ea with
      | Some(SMem(_, k)) when k < temps -> read.Contains k
      | _ -> true
    | _ -> true)

/// Lower.pack with each item's source line kept (a merged instruction takes the first's).
let pack (items: (Item * int) array) =
  let mutable cur = items
  let mutable changed = true
  while changed do
    changed <- false
    let out = ResizeArray<Item * int>()
    let mutable i = 0
    while i < cur.Length do
      if i + 1 < cur.Length then
        match Lower.pack [| fst cur[i]; fst cur[i + 1] |] with
        | [| merged |] ->
          out.Add(merged, (if snd cur[i] > 0 then snd cur[i] else snd cur[i + 1]))
          changed <- true
          i <- i + 2
        | _ ->
          out.Add cur[i]
          i <- i + 1
      else
        out.Add cur[i]
        i <- i + 1
    cur <- out.ToArray()
  cur

/// A DO loop's last instruction cannot be a branch target: a NOP where a pass left a label (or
/// nothing) at the end of a loop.
let fixLoops (items: (Item * int) array) =
  let ends =
    items |> Array.choose (fun (it, _) ->
      match it with
      | Op(Loop(_, _, { expr = Sym name })) -> Some name
      | _ -> None) |> Set.ofArray
  let out = ResizeArray<Item * int>()
  for it, line in items do
    match it with
    | Label name when ends.Contains name ->
      let previous = out |> Seq.rev |> Seq.tryFind (fun (x, _) -> match x with Comment _ -> false | _ -> true)
      match previous with
      | Some(Label _, _) | Some(Op(Loop _), _) | None -> out.Add(Op Nop, line)
      | _ -> ()
      out.Add(it, line)
    | _ -> out.Add(it, line)
  out.ToArray()

type Level =
  | Direct            // the compiler's translation as it is
  | Peephole          // value numbering, dead temporaries, packing

let run (level: Level) (temps: int) (items: Item array) (lines: int array) =
  match level with
  | Direct -> items, lines
  | Peephole ->
    let mutable cur = Array.zip items lines
    for _ in 1 .. 3 do
      cur <- cur |> forward |> copyForward |> deadTemps temps
    cur <- pack cur |> fixLoops
    Array.map fst cur, Array.map snd cur

/// Compile, then run the passes of `level` over the items.
let compile (level: Level) (target: Kernel.Target) (k: Kernel.Kernel) =
  Compile.compile target k
  |> Result.map (fun c ->
    let items, lines = run level c.xWorkset c.items c.lines
    { c with items = items; lines = lines })

/// Passes over the compiled items, each keeping the program's meaning:
///
/// - value numbering inside straight-line code: a move whose destination already holds the
///   value is dropped (a reload just stored, an Acc local's three words reloaded into the
///   unchanged accumulator, a store of what the word holds, a tfr between equal accumulators),
///   and a load whose value a data register holds becomes a register move (which can then be
///   packed); a long immediate (or zero) a register already holds is not loaded again, and one
///   another data register holds is copied from it;
/// - dead stores to the compiler's temporaries: words of the X workset past the [<XMemory>]
///   state that no instruction reads;
/// - loads of workset words a DO loop does not write moved before the loop;
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
  let signExts = Dictionary<int, int>()
  let imports = Dictionary<string * int64, int>()

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

  member this.Import(marker: string, off: int64) =
    match imports.TryGetValue((marker, off)) with
    | true, id -> id
    | _ ->
      let id = this.Fresh()
      imports[(marker, off)] <- id
      id

  member this.SignExt(v: int) =
    match signExts.TryGetValue v with
    | true, id -> id
    | _ ->
      let id = this.Fresh()
      signExts[v] <- id
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
    signExts.Clear()

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
      | Load(space, ea, _) when space <> Space.L ->
        Some(match workset space ea with Some s -> f.Id s | None -> f.Fresh())
      | Copy(src, _) when isWord src || src = A2 || src = B2 -> Some(f.Id(SReg src))
      | Store(src, _, _) when isWord src || src = A2 || src = B2 -> Some(f.Id(SReg src))
      // only long immediates and zero: a short one lands in the MSBs of a data register
      | Imm(op, _) when op.force = Long || immValue op = Some 0L -> immValue op |> Option.map f.Const
      | _ -> None)
  // tfr between the accumulators copies all three parts
  let copied =
    match alu with
    | Some(Binary(AluOp.Tfr, ((A | B) as s), d)) ->
      let e, h, l = (if s = A then (A2, A1, A0) else (B2, B1, B0))
      Some(Acc.reg d, f.Id(SReg e), f.Id(SReg h), f.Id(SReg l), f.Loaded s)
    | _ -> None
  alu |> Option.iter (fun a -> for w in Lower.aluWrites a do f.WriteReg w)
  match copied with
  | Some(d, e, h, l, loaded) ->
    let e', h', l' = (if d = A then (A2, A1, A0) else (B2, B1, B0))
    f.Set(SReg e', e)
    f.Set(SReg h', h)
    f.Set(SReg l', l)
    loaded |> Option.iter (fun v -> f.SetLoaded(d, v))
  | None -> ()
  moves |> Array.iteri (fun i m ->
    match m with
    | Load(space, ea, dst) ->
      f.WriteReg dst
      // (rn)+ and friends move the register
      for r in Lower.eaWrites ea do f.WriteReg r
      match dst, reads[i] with
      | (A | B), Some v ->
        f.Set(SReg(if dst = A then A2 else B2), f.SignExt v)
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
      // the output block (r7) is not the workset; any other word may be a state array element
      | None -> match ea with PostInc 7 -> () | _ -> f.ForgetSpace space
    | Copy(_, dst) | Imm(_, dst) ->
      f.WriteReg dst
      match dst, reads[i] with
      | (A | B), Some v ->
        f.Set(SReg(if dst = A then A2 else B2), f.SignExt v)
        f.Set(SReg(if dst = A then A1 else B1), v)
        f.Set(SReg(if dst = A then A0 else B0), f.Const 0L)
        f.SetLoaded(dst, v)
      | d, Some v when isWord d || d = A2 || d = B2 -> f.Set(SReg d, v)
      | _ -> ()
    | Update ea -> for r in Lower.eaWrites ea do f.WriteReg r)

/// The registers a data ALU instruction outside the parallel class writes (None: not one).
let aluWrites (instr: Instr) =
  match instr with
  | AluImm(CmpI, _, _) -> Some []
  | AluImm(_, _, d) | ShiftImm(_, _, _, d) | ShiftReg(_, _, _, d) | MulImm(_, _, _, _, d)
  | MulSu(_, _, _, _, _, d) | Dmac(_, _, _, _, d) | MulI(_, _, _, _, d) | Div(_, d) | Inc d | Dec d -> Some [ Acc.reg d ]
  | IfCc(alu, _, _) -> Some(List.ofArray (Lower.aluWrites alu))
  | Tcc(_, alu, agu) -> Some([ for _, d in Option.toList alu -> Acc.reg d ] @ [ for _, r in Option.toList agu -> R r ])
  | Norm(r, d) -> Some [ Acc.reg d; R r ]
  | Clb(_, d) | Normf(_, d) -> Some [ Acc.reg d ]
  | Lua(_, d) -> Some [ d ]
  | BitOp(Btst, _, _) | CtrlImm(_, _, (Ccr | Mr | Omr)) | Rep(Count _) | Nop -> Some []
  | BitOp(_, _, BitReg r) -> Some(List.ofArray (Lower.storage r))
  | _ -> None

/// Value numbering over straight-line stretches: moves that change nothing are dropped, loads
/// of a value a data register holds become register moves.
/// `accCopies`: an accumulator loaded from a word a data register holds is copied from the
/// register instead (done last: earlier, the register's read would keep copyForward from
/// removing the register's own load).
let forwardWith (accCopies: bool) (keepTriples: bool) (items: (Item * int) array) =
  let items = Array.copy items
  let f = Facts()
  let out = ResizeArray<Item * int>()
  let mutable i = 0
  let targetLabels = HashSet<string>([ "INIT"; "TRIG"; "RENDER" ])
  for it, _ in items do
    match it with
    | Label l when l.StartsWith "SUB_" -> targetLabels.Add l |> ignore
    | Op(Loop(_, _, { expr = Sym t }))
    | Op(Branch(_, { expr = Sym t }))
    | Op(Jump(_, { expr = Sym t }))
    | Op(BitBranch(_, _, _, { expr = Sym t })) -> targetLabels.Add t |> ignore
    | _ -> ()
  // the instruction after an `mds:` marker has a word the loader patches (an import's address,
  // a relocated target): its immediate is not the literal value written
  let mutable patched : string option = None
  let single (it: Item) =
    match it with
    | Op(Par(None, [| m |])) -> Some m
    | _ -> None
  while i < items.Length do
    if accCopies && not keepTriples && patched.IsNone then
      match single (fst items[i]) with
      | Some(Store(((A | B) as acc), space, ea)) when (workset space ea).IsSome ->
        let mutable k = i + 1
        while k < items.Length && (match fst items[k] with Comment _ -> true | _ -> false) do k <- k + 1
        if k < items.Length then
          match single (fst items[k]) with
          | Some(Load(space', ea', ((X0 | X1 | Y0 | Y1) as dst))) when space = space' && ea = ea' ->
            items[i] <- (Op(Par(None, [| Copy(acc, dst) |])), snd items[i])
            items[k] <- (Op(Par(None, [| Store(dst, space, ea) |])), snd items[k])
          | _ -> ()
      | _ -> ()
    let it, line = items[i]
    // an Acc's three words reloaded into the accumulator they were stored from (or the other one)
    let isTload, triple, otherTriple =
      if i + 2 < items.Length then
        match single (fst items[i]), single (fst items[i + 1]), single (fst items[i + 2]) with
        | Some(Load(s1, e1, ((A | B) as acc))), Some(Load(s2, e2, lo)), Some(Load(s3, e3, ext))
            when (lo = (if acc = A then A0 else B0)) && (ext = (if acc = A then A2 else B2)) ->
          match workset s1 e1, workset s2 e2, workset s3 e3 with
          | Some m1, Some m2, Some m3 ->
            let hi = if acc = A then A1 else B1
            let other = if acc = A then B else A
            let ohi = if other = A then A1 else B1
            let olo = if other = A then A0 else B0
            let oext = if other = A then A2 else B2
            let same r m = match f.Get(SReg r), f.Get m with | Some a, Some b -> a = b | _ -> false
            let inOther =
              if not keepTriples && same ohi m1 && same olo m2 && same oext m3 then
                Some(other, if acc = A then Acc.A else Acc.B)
              else None
            true, same hi m1 && same lo m2 && same ext m3, inOther
          | _ -> false, false, None
        | _ -> false, false, None
      else false, false, None
    let immPair =
      if not isTload && i + 1 < items.Length && patched.IsNone then
        match single (fst items[i]), single (fst items[i + 1]) with
        | Some(Imm(opHi, ((A | B) as acc))), Some(Imm(opLo, lo))
            when lo = (if acc = A then A0 else B0)
                 && (opHi.force = Long || immValue opHi = Some 0L)
                 && (opLo.force = Long || immValue opLo = Some 0L) ->
          match immValue opHi, immValue opLo with
          | Some vHi, Some vLo ->
            let ext, hi = if acc = A then A2, A1 else B2, B1
            let cHi, cLo = f.Const vHi, f.Const vLo
            f.Get(SReg ext) = Some(f.SignExt cHi) && f.Get(SReg hi) = Some cHi && f.Get(SReg lo) = Some cLo
          | _ -> false
        | _ -> false
      else false
    if triple then i <- i + 3
    elif immPair then i <- i + 2
    elif otherTriple.IsSome then
      let src, dst = otherTriple.Value
      let alu = Some(Binary(AluOp.Tfr, src, dst))
      applyMoves f alu [||]
      out.Add(Op(Par(alu, [||])), line)
      i <- i + 3
    elif keepTriples && isTload then
      for j in i .. i + 2 do
        match fst items[j] with
        | Op(Par(alu, moves)) -> applyMoves f alu moves
        | _ -> ()
        out.Add items[j]
      i <- i + 3
    else
      match it with
      | Label l ->
        let isPatchLabel =
          not (targetLabels.Contains l)
          && i + 1 < items.Length
          && (match fst items[i + 1] with Comment t -> t.StartsWith machinedrum.Abi.markerPrefix | _ -> false)
        if not isPatchLabel then f.Clear()
        out.Add(it, line)
        i <- i + 1
      | Dc _ | Ds _ | Org _ | Equ _ ->
        f.Clear()
        out.Add(it, line)
        i <- i + 1
      | Comment text ->
        if text.StartsWith machinedrum.Abi.markerPrefix then patched <- Some text
        out.Add(it, line)
        i <- i + 1
      | Op _ when patched.IsSome ->
        let marker = patched.Value
        patched <- None
        let hasPatchHeader () =
          out.Count >= 2
          && (match fst out[out.Count - 2], fst out[out.Count - 1] with
              | Label lbl, Comment c -> c = marker && not (targetLabels.Contains lbl)
              | _ -> false)
        match it with
        | Op(Par(None, [| Imm({ expr = Num k; force = Long }, dst) |]))
            when marker.StartsWith "mds: import " && not (marker.EndsWith "+0") ->
          let impId = f.Import(marker, k)
          let alreadyThere =
            match dst with
            | A | B -> f.Loaded dst = Some impId
            | d when isWord d -> f.Get(SReg d) = Some impId
            | _ -> false
          if alreadyThere && hasPatchHeader () then
            out.RemoveRange(out.Count - 2, 2)
          else
            let holderOpt =
              if isWord dst && hasPatchHeader () then
                match f.Holder(impId, dst) with
                | Some r when Lower.encodes (Par(None, [| Copy(r, dst) |])) -> Some r
                | _ -> None
              else None
            match holderOpt with
            | Some r ->
              out.RemoveRange(out.Count - 2, 2)
              out.Add(Op(Par(None, [| Copy(r, dst) |])), line)
            | None ->
              out.Add(it, line)
            f.WriteReg dst
            match dst with
            | A | B ->
              f.Set(SReg(if dst = A then A2 else B2), f.SignExt impId)
              f.Set(SReg(if dst = A then A1 else B1), impId)
              f.Set(SReg(if dst = A then A0 else B0), f.Const 0L)
              f.SetLoaded(dst, impId)
            | d when isWord d -> f.Set(SReg d, impId)
            | _ -> ()
        | Op(Par(alu, moves)) when not (moves |> Array.exists (function Store _ -> true | _ -> false)) ->
          alu |> Option.iter (fun a -> for w in Lower.aluWrites a do f.WriteReg w)
          for m in moves do
            for w in Lower.moveWrites m do f.WriteReg w
          out.Add(it, line)
        | Op instr ->
          match aluWrites instr with
          | Some ws -> for w in ws do f.WriteReg w
          | None -> f.Clear()
          out.Add(it, line)
        | _ ->
          f.Clear()
          out.Add(it, line)
        i <- i + 1
      | Op(Par(alu, moves)) ->
        let keep =
          match alu, moves with
          | None, [| Load(space, ea, dst) |] when isWord dst || dst = A2 || dst = B2 ->
            match workset space ea |> Option.bind f.Get, f.Get(SReg dst) with
            | Some v, Some w when v = w -> None                                   // already there
            | Some v, _ when isWord dst ->
              match f.Holder(v, dst) with
              | Some r -> Some(Op(Par(None, [| Copy(r, dst) |])))                  // from a register
              | None -> Some it
            | _ -> Some it
          | None, [| Load(space, ea, ((A | B) as acc)) |] when not (keepTriples && isTload) ->
            match workset space ea |> Option.bind f.Get, f.Loaded acc with
            | Some v, Some w when v = w -> None
            | Some v, _ ->
              // the word is in a data register: copied from there (sign-extended, a0 cleared, as loaded)
              match f.Holder(v, acc) with
              | Some r when accCopies && Lower.encodes (Par(None, [| Copy(r, acc) |])) -> Some(Op(Par(None, [| Copy(r, acc) |])))
              | _ -> Some it
            | _ -> Some it
          | Some(Binary(AluOp.Tfr, ((A | B) as src), dst)), _ when Acc.reg dst <> src ->
            let three r = (if r = A then [ A2; A1; A0 ] else [ B2; B1; B0 ]) |> List.map (fun p -> f.Get(SReg p))
            let s, d = three src, three (Acc.reg dst)
            if List.forall Option.isSome s && s = d then
              (if moves.Length = 0 then None else Some(Op(Par(None, moves))))
            else Some it
          | None, [| Copy(src, dst) |] when isWord src && isWord dst ->
            match f.Get(SReg src), f.Get(SReg dst) with
            | Some v, Some w when v = w -> None
            | _ -> Some it
          | None, [| Copy(src, ((A | B) as acc)) |] when isWord src ->
            match f.Get(SReg src), f.Loaded acc with
            | Some v, Some w when v = w -> None
            | _ -> Some it
          | None, [| Store(src, space, ea) |] when isWord src || src = A2 || src = B2 ->
            match workset space ea |> Option.bind f.Get, f.Get(SReg src) with
            | Some v, Some w when v = w -> None
            | _ -> Some it
          // an immediate a register already holds (long immediates and zero only, as applyMoves)
          | None, [| Imm(op, dst) |] when op.force = Long || immValue op = Some 0L ->
            match immValue op with
            | Some v ->
              let c = f.Const v
              match dst with
              | A | B ->
                match f.Loaded dst with
                | Some w when w = c -> None                                         // already loaded
                | _ -> Some it
              | d when isWord d ->
                match f.Get(SReg d) with
                | Some w when w = c -> None                                         // already there
                | _ ->
                  match f.Holder(c, d) with
                  | Some r when Lower.encodes (Par(None, [| Copy(r, d) |])) -> Some(Op(Par(None, [| Copy(r, d) |])))
                  | _ -> Some it
              | _ -> Some it
            | None -> Some it
          | _ -> Some it
        match keep with
        | Some(Op(Par(alu, moves)) as k) ->
          applyMoves f alu moves
          out.Add(k, line)
        | Some k -> out.Add(k, line)
        | None -> ()
        i <- i + 1
      | Op(Branch(Bcc _, _)) when accCopies && not keepTriples ->
        out.Add(it, line)
        i <- i + 1
      | Op instr ->
        match aluWrites instr with
        | Some ws -> for w in ws do f.WriteReg w
        | None -> f.Clear()           // loops, branches, calls and anything else: nothing is known after
        out.Add(it, line)
        i <- i + 1
  out.ToArray()

let forward = forwardWith false true

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
    | Op(Branch((Bcc _ | Bscc _ | Bsr), t)) | Op(Jump((Jcc _ | Jscc _ | Jsr), t)) | Op(BitBranch(_, _, _, t)) ->
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

/// A compiler temporary word: X workset words from `first` up to `temps`, and Y workset words
/// from `yFirst` up to `yTemps` (mapped to 1024 + k so X and Y slots never share an ID or
/// appear contiguous across banks).
let tempSlot (first: int) (temps: int) (yFirst: int) (yTemps: int) (space: Space) (ea: Ea) =
  match workset space ea with
  | Some(SMem(Space.X, k)) when k >= first && k < temps -> Some k
  | Some(SMem(Space.Y, k)) when k >= yFirst && k < yTemps -> Some(1024 + k)
  | _ -> None

/// Registers an instruction reads, as far as this pass needs (None: not known).
let readsOf (instr: Instr) =
  match instr with
  | Par(alu, moves) ->
    Some(Array.append (alu |> Option.map Lower.aluReads |> Option.defaultValue [||]) (Array.collect Lower.moveReads moves))
  | IfCc(alu, _, _) -> Some(Lower.aluReads alu)
  | Tcc(_, accPair, rPair) ->
    let a = match accPair with Some(s, d) -> [| s; Acc.reg d |] | None -> [||]
    let r = match rPair with Some(s, d) -> [| R s; R d |] | None -> [||]
    Some(Array.append a r)
  | Norm(r, d) -> Some [| R r; Acc.reg d |]
  | MulI(kind, _, _, src, d) | MulImm(kind, _, src, _, d) ->
    Some(match kind with Mac | Macr -> [| src; Acc.reg d |] | _ -> [| src |])
  | MulSu(kind, _, _, s1, s2, d) ->
    Some(match kind with Mac | Macr -> [| s1; s2; Acc.reg d |] | _ -> [| s1; s2 |])
  | Dmac(_, _, s1, s2, d) -> Some [| s1; s2; Acc.reg d |]
  | Div(src, d) | Normf(src, d) -> Some [| src; Acc.reg d |]
  | Clb(src, _) -> Some [| Acc.reg src |]
  | AluImm(_, _, d) | Inc d | Dec d -> Some [| Acc.reg d |]
  | ShiftImm((ShLsl | ShLsr), _, s, d) -> Some([| Acc.reg s; Acc.reg d |] |> Array.distinct)
  | ShiftImm(_, _, s, _) -> Some [| Acc.reg s |]
  | ShiftReg((ShLsl | ShLsr), r, s, d) -> Some([| r; Acc.reg s; Acc.reg d |] |> Array.distinct)
  | ShiftReg(_, r, s, _) -> Some [| r; Acc.reg s |]
  | Lua(ea, _) -> Some(Lower.eaRegs ea)
  | BitOp(_, _, BitReg r) -> Some(Lower.storage r)
  | BitOp(_, _, BitMem(_, ea)) -> Some(Lower.eaRegs ea)
  | CtrlImm(_, _, (Ccr | Mr | Omr)) | Rep(Count _) | Nop -> Some [||]
  | Rep(FromReg r) -> Some(Lower.storage r)
  | Rep(FromMem(_, ea)) -> Some(Lower.eaRegs ea)
  | _ -> None

let writesOf (instr: Instr) =
  match instr with
  | Par(alu, moves) ->
    Some(Array.append (alu |> Option.map Lower.aluWrites |> Option.defaultValue [||]) (Array.collect Lower.moveWrites moves))
  | BitOp(_, _, BitMem(_, ea)) -> Some(Lower.eaWrites ea)
  | i -> aluWrites i |> Option.map List.toArray

/// Whether `instr` overwrites every part of `t`.
let writesWhole (t: Reg) (instr: Instr) =
  let covers (r: Reg) = parts t |> List.forall (fun p -> List.contains p (parts r))
  let aluWhole (a: Alu) =
    match a with
    | Binary(AluOp.Tfr, _, d) | Unary(AluUnary.Clr, d) | Mul((Mpy | Mpyr), _, _, _, d) -> covers (Acc.reg d)
    | _ -> false
  match instr with
  | Par(alu, moves) ->
    Option.exists aluWhole alu
    || (moves |> Array.exists (function Load(_, _, r) | Copy(_, r) | Imm(_, r) -> covers r | _ -> false))
  | MulI((Mpy | Mpyr), _, _, _, d) | MulImm((Mpy | Mpyr), _, _, _, d)
  | MulSu((Mpy | Mpyr), _, _, _, _, d) | Clb(_, d)
  | ShiftImm((ShAsl | ShAsr), _, _, d) | ShiftReg((ShAsl | ShAsr), _, _, d) -> covers (Acc.reg d)
  | _ -> false

/// The first item of the subroutines (`SUB_...`), or the end when there are none: an `rts` before
/// it returns from an entry (`INIT`, `TRIG`, `RENDER`), which returns nothing in registers; an
/// `rts` from there on returns a subroutine's result in `a` or `x0`.
let firstSubroutine (its: Item array) =
  its |> Array.tryFindIndex (function Label l -> l.StartsWith "SUB_" | _ -> false) |> Option.defaultValue its.Length

/// Whether `r` is overwritten before anything reads it, from item `from` on.
let deadAfter (items: (Item * int) array) (from: int) (r: Reg) =
  let its = Array.map fst items
  let firstSub = firstSubroutine its
  let control (instr: Instr) =
    match instr with
    | Branch((Bra | Bcc _), _) | Jump((Jmp | Jcc _), _) | Loop(_, Count _, _) | Enddo | Nop -> true
    | _ -> false
  let overlap (a: Reg) (b: Reg) =
    let pa = parts a
    parts b |> List.exists (fun x -> List.contains x pa)
  let rec straight k =
    if k >= items.Length then None
    else
      match fst items[k] with
      | Comment _ -> straight (k + 1)
      | Op Rts -> Some(k < firstSub || not (overlap r A || overlap r X0))
      | Op instr ->
        match readsOf instr, writesOf instr with
        | Some rs, Some ws ->
          if rs |> Array.exists (overlap r) then Some false
          elif ws |> Array.exists (fun x -> parts x = parts r || x = r) then Some true
          else straight (k + 1)
        | _ -> None
      | _ -> None
  match straight from with
  | Some res -> res
  | None ->
    if from < 0 || from >= items.Length then false
    else
      match successors its with
      | None -> false
      | Some succ ->
        let seen = HashSet<int>()
        let todo = Stack<int>()
        if seen.Add from then todo.Push from
        let mutable dead = true
        while todo.Count > 0 && dead do
          let k = todo.Pop()
          let step () =
            if succ[k].Length = 0 then dead <- false
            else for nx in succ[k] do if seen.Add nx then todo.Push nx
          match fst items[k] with
          | Comment _ | Label _ -> step ()
          | Op Rts -> if k >= firstSub && (overlap r A || overlap r X0) then dead <- false
          | Op instr when control instr -> step ()
          | Op instr ->
            match readsOf instr, writesOf instr with
            | Some rs, Some ws ->
              if rs |> Array.exists (overlap r) then dead <- false
              elif ws |> Array.exists (fun x -> parts x = parts r || x = r) then ()
              else step ()
            | _ -> dead <- false
          | _ -> dead <- false
        dead

/// Whole-accumulator reloads whose three raw parts equal their temporary on every incoming
/// path. Value IDs need not agree between paths: a clamp can select a different value while
/// keeping A equal to its home on both. Unknown effects and entry paths contribute no facts.
let joinAccumulatorsY (first: int) (temps: int) (yFirst: int) (yTemps: int) (items: (Item * int) array) =
  let temp = tempSlot first temps yFirst yTemps
  let items =
    let out = ResizeArray<Item * int>(items.Length)
    let nonCommentBack start count =
      let res = ResizeArray<int>()
      let mutable j = start
      while j >= 0 && res.Count < count do
        match fst items[j] with
        | Comment _ -> ()
        | _ -> res.Add j
        j <- j - 1
      res.ToArray()
    let nonCommentFwd start count =
      let res = ResizeArray<int>()
      let mutable j = start
      while j < items.Length && res.Count < count do
        match fst items[j] with
        | Comment _ -> ()
        | _ -> res.Add j
        j <- j + 1
      res.ToArray()
    for i in 0 .. items.Length - 1 do
      match fst items[i] with
      | Label lbl ->
        let back = nonCommentBack (i - 1) 6
        let fwd = nonCommentFwd (i + 1) 3
        let isClampBridge =
          if back.Length >= 4 && fwd.Length = 3 then
            match fst items[back[2]], fst items[back[1]], fst items[back[0]] with
            | Op(Par(None, [| Store(A1, sp1, e1) |])), Op(Par(None, [| Store(A0, sp0, e0) |])), Op(Par(None, [| Store(A2, sp2, e2) |])) ->
              match temp sp1 e1, temp sp0 e0, temp sp2 e2 with
              | Some k, Some k0, Some k2 when k0 = k + 1 && k2 = k + 2 ->
                let hasBle =
                  back[3..] |> Array.exists (fun bi ->
                    match fst items[bi] with
                    | Op(Branch(Bcc Cond.Le, { expr = Sym t })) -> t = lbl
                    | _ -> false)
                match hasBle, fst items[fwd[0]], fst items[fwd[1]], fst items[fwd[2]] with
                | true, Op(Par(Some(Binary(AluOp.Tfr, B, Acc.A)), [||])), Op(AluImm(CmpI, _, Acc.A)), Op(Branch(Bcc Cond.Ge, _)) ->
                  deadAfter items (fwd[0] + 1) B
                | _ -> false
              | _ -> false
            | _ -> false
          else false
        if isClampBridge then
          out.Add(Op(Par(Some(Binary(AluOp.Tfr, A, Acc.B)), [||])), snd items[i])
      | _ -> ()
      out.Add items[i]
    out.ToArray()
  let its = Array.map fst items
  let n = its.Length
  let single i = match its[i] with Op(Par(None, [| m |])) -> Some m | _ -> None
  let reload i =
    if i + 2 >= n then None
    else
      match single i, single (i + 1), single (i + 2) with
      | Some(Load(sp1, e1, ((A | B) as acc))), Some(Load(sp0, e0, lo)), Some(Load(sp2, e2, ext))
          when lo = (if acc = A then A0 else B0) && ext = (if acc = A then A2 else B2) ->
        match temp sp1 e1, temp sp0 e0, temp sp2 e2 with
        | Some k, Some k0, Some k2 when k0 = k + 1 && k2 = k + 2 -> Some(k, acc)
        | _ -> None
      | _ -> None
  match successors its with
  | None -> items
  | Some succ ->
    let preds = Array.init n (fun _ -> ResizeArray<int>())
    for i in 0 .. n - 1 do for j in succ[i] do preds[j].Add i
    let candidates = [| for i in 0 .. n - 1 do match reload i with Some pair -> yield pair | _ -> () |] |> Array.distinct
    let dropped = HashSet<int>()
    let regsA = [| A1; A0; A2 |]
    let regsB = [| B1; B0; B2 |]
    let maskOf (regs: Reg array) (r: Reg) =
      let touched = parts r
      regs |> Array.mapi (fun bit p -> if List.contains p touched then 1 <<< bit else 0) |> Array.fold (|||) 0
    for k, acc in candidates do
      let spSlot = if k >= 1024 then Space.Y else Space.X
      let memoryMask space ea =
        match temp space ea with
        | Some word when word >= k && word < k + 3 -> 1 <<< (word - k)
        | _ ->
          match workset space ea with
          | Some _ -> 0
          | None when space = spSlot -> 7     // an indirect store can alias the temporary
          | _ -> 0
      // 9-bit state: bits 0..2 = (A == slot), bits 3..5 = (B == slot), bits 6..8 = (A == B)
      let close (aSlot: int) (bSlot: int) (abEq: int) =
        let a = aSlot ||| (bSlot &&& abEq)
        let b = bSlot ||| (a &&& abEq)
        let ab = abEq ||| (a &&& b)
        a ||| (b <<< 3) ||| (ab <<< 6)
      let transfer state item =
        match item with
        | Label _ | Comment _ -> state
        | Op(Branch((Bra | Bcc _), _)) | Op(Jump((Jmp | Jcc _), _))
        | Op(Loop _) | Op Enddo | Op Nop -> state
        | Op(Par(alu, moves)) ->
          let s0 = close (state &&& 7) ((state >>> 3) &&& 7) ((state >>> 6) &&& 7)
          let mutable aSlot = s0 &&& 7
          let mutable bSlot = (s0 >>> 3) &&& 7
          let mutable abEq = (s0 >>> 6) &&& 7
          let mutable baseChanged = false
          match alu with
          | None -> ()
          | Some(Binary(AluOp.Tfr, A, Acc.B)) ->
            bSlot <- aSlot
            abEq <- 7
          | Some(Binary(AluOp.Tfr, B, Acc.A)) ->
            aSlot <- bSlot
            abEq <- 7
          | Some a ->
            for r in Lower.aluWrites a do
              if r = R 6 then baseChanged <- true
              let ma, mb = maskOf regsA r, maskOf regsB r
              aSlot <- aSlot &&& (7 ^^^ ma)
              bSlot <- bSlot &&& (7 ^^^ mb)
              abEq <- abEq &&& (7 ^^^ (ma ||| mb))
          let mutable changedA = 0
          let mutable changedB = 0
          let mutable stored = 0
          let write r =
            changedA <- changedA ||| maskOf regsA r
            changedB <- changedB ||| maskOf regsB r
            if r = R 6 then baseChanged <- true
          for move in moves do
            // Lower.storage deliberately merges A0/A1/A2 for interference; equality needs
            // their precise writes so the three-part reload does not erase its earlier parts.
            match move with
            | Load(_, ea, r) -> write r; Lower.eaWrites ea |> Array.iter write
            | Store(_, _, ea) | Update ea -> Lower.eaWrites ea |> Array.iter write
            | Copy(_, r) | Imm(_, r) -> write r
            match move with Store(_, sp, ea) -> stored <- stored ||| memoryMask sp ea | _ -> ()
          if baseChanged then 0
          else
            aSlot <- aSlot &&& (7 ^^^ (changedA ||| stored))
            bSlot <- bSlot &&& (7 ^^^ (changedB ||| stored))
            abEq <- abEq &&& (7 ^^^ (changedA ||| changedB))
            // Generate equality only for a single move without an ALU: a parallel move's
            // sources precede all writes, and overlapping destinations need more modelling.
            for move in (if alu.IsNone && moves.Length = 1 then moves else [||]) do
              match move with
              | Store(src, sp, ea) ->
                match temp sp ea with
                | Some word when word >= k && word < k + 3 ->
                  let bit = 1 <<< (word - k)
                  if src = regsA[word - k] && changedA &&& bit = 0 then
                    aSlot <- aSlot ||| bit
                    if abEq &&& bit <> 0 then bSlot <- bSlot ||| bit
                  if src = regsB[word - k] && changedB &&& bit = 0 then
                    bSlot <- bSlot ||| bit
                    if abEq &&& bit <> 0 then aSlot <- aSlot ||| bit
                | _ -> ()
              | Load(sp, ea, dst) ->
                match temp sp ea with
                | Some word when word >= k && word < k + 3 ->
                  let bit = 1 <<< (word - k)
                  if (dst = regsA[word - k] || (word = k && dst = A)) && stored &&& bit = 0 then
                    aSlot <- aSlot ||| bit
                  if (dst = regsB[word - k] || (word = k && dst = B)) && stored &&& bit = 0 then
                    bSlot <- bSlot ||| bit
                | _ -> ()
              | _ -> ()
            close aSlot bSlot abEq
        | Op instr ->
          match aluWrites instr with
          | Some writes when not (List.contains (R 6) writes) ->
            let ma = writes |> List.map (maskOf regsA) |> List.fold (|||) 0
            let mb = writes |> List.map (maskOf regsB) |> List.fold (|||) 0
            let aSlot = (state &&& 7) &&& (7 ^^^ ma)
            let bSlot = ((state >>> 3) &&& 7) &&& (7 ^^^ mb)
            let abEq = ((state >>> 6) &&& 7) &&& (7 ^^^ (ma ||| mb))
            close aSlot bSlot abEq
          | _ -> 0
        | _ -> 0
      let incoming = Array.zeroCreate<int> n
      let outgoing = Array.zeroCreate<int> n
      let mutable changed = true
      while changed do
        changed <- false
        for i in 0 .. n - 1 do
          let before = if preds[i].Count = 0 then 0 else preds[i] |> Seq.map (fun p -> outgoing[p]) |> Seq.reduce (&&&)
          let after = transfer before its[i]
          if before <> incoming[i] || after <> outgoing[i] then
            incoming[i] <- before
            outgoing[i] <- after
            changed <- true
      let want = if acc = A then 7 else (7 <<< 3)
      for i in 0 .. n - 1 do
        if (incoming[i] &&& want) = want && reload i = Some(k, acc) then
          for j in i .. i + 2 do dropped.Add j |> ignore
    items |> Array.mapi (fun i item -> i, item) |> Array.choose (fun (i, item) -> if dropped.Contains i then None else Some item)

let joinAccumulators (first: int) (temps: int) (items: (Item * int) array) =
  joinAccumulatorsY first temps 0 0 items

/// Stores to the compiler's temporaries (X workset words from `first` up to `temps` and Y
/// workset words from `yFirst` up to `yTemps`; the words below are state) that no path reads
/// before the word is written again.
let deadTempsFromY (first: int) (temps: int) (yFirst: int) (yTemps: int) (items: (Item * int) array) =
  let its = Array.map fst items
  let temp = tempSlot first temps yFirst yTemps
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

let deadTempsFrom (first: int) (temps: int) (items: (Item * int) array) =
  deadTempsFromY first temps 0 0 items

/// The dead temporaries of a kernel without [<XMemory>] state: X words below `temps`.
let deadTemps (temps: int) (items: (Item * int) array) = deadTempsFrom 0 temps items

/// `move s,x0` `move x0,d` with x0 dead afterwards: `move s,d`, when that encodes.
let copyForwardWith (dropDeadAcc: bool) (items: (Item * int) array) =
  let cur = Array.copy items
  let dropped = HashSet<int>()
  let its = Array.map fst cur
  let firstSub = firstSubroutine its
  let succ = successors its
  let control (instr: Instr) =
    match instr with
    | Branch((Bra | Bcc _), _) | Jump((Jmp | Jcc _), _) | Loop(_, Count _, _) | Enddo | Nop -> true
    | _ -> false
  let overlap (a: Reg) (b: Reg) =
    let pa = parts a
    parts b |> List.exists (fun x -> List.contains x pa)
  let deadAfterAt put drop after (rep: Item) (r: Reg) =
    let rec straight k =
      if k >= cur.Length then None
      elif k = drop || dropped.Contains k then straight (k + 1)
      else
        match (if k = put then rep else fst cur[k]) with
        | Comment _ -> straight (k + 1)
        | Op Rts -> Some(k < firstSub || not (overlap r A || overlap r X0))
        | Op instr ->
          match readsOf instr, writesOf instr with
          | Some rs, Some ws ->
            if rs |> Array.exists (overlap r) then Some false
            elif ws |> Array.exists (fun x -> parts x = parts r || x = r) then Some true
            else straight (k + 1)
          | _ -> None
        | _ -> None
    match straight (after + 1) with
    | Some res -> res
    | None ->
      match succ with
      | None -> false
      | Some succ ->
        if succ[after].Length = 0 then false
        else
          let seen = HashSet<int>()
          let todo = Stack<int>()
          for nx in succ[after] do if seen.Add nx then todo.Push nx
          let mutable dead = true
          while todo.Count > 0 && dead do
            let k = todo.Pop()
            let step () =
              if succ[k].Length = 0 then dead <- false
              else for nx in succ[k] do if seen.Add nx then todo.Push nx
            if k = drop || dropped.Contains k then step ()
            else
              match (if k = put then rep else fst cur[k]) with
              | Comment _ | Label _ -> step ()
              | Op Rts -> if k >= firstSub && (overlap r A || overlap r X0) then dead <- false
              | Op instr when control instr -> step ()
              | Op instr ->
                match readsOf instr, writesOf instr with
                | Some rs, Some ws ->
                  if rs |> Array.exists (overlap r) then dead <- false
                  elif ws |> Array.exists (fun x -> parts x = parts r || x = r) then ()
                  else step ()
                | _ -> dead <- false
              | _ -> dead <- false
          dead
  let data (r: Reg) = r = X0 || r = X1 || r = Y0 || r = Y1
  let renameAlu (fromReg: Reg) (toReg: Reg) (instr: Instr) =
    let swap x = if x = fromReg then toReg else x
    match instr with
    | Par(Some(Binary((AluOp.Add | AluOp.Sub | AluOp.Cmp) as op, s, d)), [||]) when s = fromReg ->
      Some(Par(Some(Binary(op, toReg, d)), [||]))
    | Par(Some(Mul(kind, neg, s1, s2, d)), [||]) when s1 = fromReg || s2 = fromReg ->
      let m = Par(Some(Mul(kind, neg, swap s1, swap s2, d)), [||])
      if Lower.encodes m then Some m else None
    | MulI(kind, neg, imm, s, d) when s = fromReg ->
      Some(MulI(kind, neg, imm, toReg, d))
    | _ -> None
  let deadAccParts (i: int) (dst: Reg) =
    let isPatched =
      i > 0 && (match fst cur[i - 1] with Comment t -> t.StartsWith machinedrum.Abi.markerPrefix | _ -> false)
    if isPatched then false
    else
      let acc = if List.contains dst [ A; A0; A1; A2 ] then A else B
      let rec check (k: int) (unwritten: Set<Reg>) =
        if unwritten.IsEmpty then true
        elif k >= cur.Length then false
        elif dropped.Contains k then check (k + 1) unwritten
        else
          match fst cur[k] with
          | Comment _ -> check (k + 1) unwritten
          | Op instr when not (control instr) ->
            let readParts =
              match instr with
              | Par(None, [| Copy(src, _) |]) | Par(None, [| Store(src, _, _) |]) -> Some(parts src)
              | _ -> readsOf instr |> Option.map (fun rs -> rs |> Array.toList |> List.collect parts)
            match readParts with
            | None -> false
            | Some rp when rp |> List.exists unwritten.Contains -> false
            | Some _ ->
              let unwritten' =
                if writesWhole acc instr then Set.empty
                else
                  match instr with
                  | Par(None, [| (Load(_, _, d) | Copy(_, d) | Imm(_, d)) |]) ->
                    Set.difference unwritten (Set.ofList (parts d))
                  | _ -> unwritten
              check (k + 1) unwritten'
          | _ -> false
      check (i + 1) (Set.ofList (parts dst))
  for i in 0 .. cur.Length - 1 do
    if dropDeadAcc && not (dropped.Contains i) then
      match fst cur[i] with
      | Op(Par(None, [| (Load(_, Disp _, dst) | Imm(_, dst)) |]))
      | Op(Par(None, [| Copy((X0 | X1 | Y0 | Y1 | A0 | A1 | A2 | B0 | B1 | B2), dst) |]))
          when List.contains dst [ A; A0; A1; A2; B; B0; B1; B2 ] && deadAccParts i dst ->
        dropped.Add i |> ignore
      | _ -> ()
    if not (dropped.Contains i) then
      let accWrite =
        match fst cur[i] with
        | Op(Par(None, [| Load(sp, ea, ((A | B) as acc)) |])) ->
          Some(acc, fun d -> Par(None, [| Load(sp, ea, Acc.reg d) |]))
        | Op(Par(None, [| Copy(src, ((A | B) as acc)) |])) when src <> A && src <> B && src <> A2 && src <> B2 ->
          Some(acc, fun d -> Par(None, [| Copy(src, Acc.reg d) |]))
        | Op(Par(None, [| Imm(op, ((A | B) as acc)) |])) ->
          Some(acc, fun d -> Par(None, [| Imm(op, Acc.reg d) |]))
        | Op(Par(Some(Unary(AluUnary.Clr, accD)), [||])) ->
          Some(Acc.reg accD, fun d -> Par(Some(Unary(AluUnary.Clr, d)), [||]))
        | _ -> None
      match accWrite with
      | Some(acc, rebuild) ->
        let mutable k = i + 1
        while k < cur.Length && (dropped.Contains k || (match fst cur[k] with Comment _ -> true | _ -> false)) do
          k <- k + 1
        if k < cur.Length then
          match fst cur[k] with
          | Op(Par(Some(Binary(AluOp.Tfr, srcAcc, dstAcc)), [||])) when srcAcc = acc && Acc.reg dstAcc <> acc ->
            let m = rebuild dstAcc
            if Lower.encodes m && deadAfterAt i k k (Op m) acc then
              cur[i] <- (Op m, snd cur[i])
              dropped.Add k |> ignore
          | Op(Par(None, [| Copy(hi, dst) |]))
              when (fst cur[i] = Op(Par(None, [| Copy(A1, A) |])) && hi = A1 && acc = A
                    || fst cur[i] = Op(Par(None, [| Copy(B1, B) |])) && hi = B1 && acc = B)
                   && dst <> acc && dst <> A0 && dst <> A2 && dst <> B0 && dst <> B2
                   && deadAfterAt k i k (fst cur[k]) acc ->
            dropped.Add i |> ignore
          | Op(Par(None, [| Store(hi, _, _) |]))
              when (fst cur[i] = Op(Par(None, [| Copy(A1, A) |])) && hi = A1 && acc = A
                    || fst cur[i] = Op(Par(None, [| Copy(B1, B) |])) && hi = B1 && acc = B)
                   && deadAfterAt k i k (fst cur[k]) acc ->
            dropped.Add i |> ignore
          | _ -> ()
      | None -> ()
      if not (dropped.Contains i) then
        match fst cur[i] with
        | Op(Par(None, [| first |])) ->
          let target =
            match first with
            | Load(_, _, r) | Copy(_, r) | Imm(_, r) when r = X0 || r = Y0 -> Some r
            | _ -> None
          match target with
          | None -> ()
          | Some r ->
            let between = ResizeArray<Reg * Reg>()
            let mutable k = i + 1
            let mutable searching = true
            while searching && k < cur.Length do
              if dropped.Contains k then k <- k + 1
              else
                match fst cur[k] with
                | Comment _ -> k <- k + 1
                | Op(Par(None, [| second |])) ->
                  let combined =
                    match first, second with
                    | Load(sp, ea, r), Copy(r', d) when r = r' && d <> A2 && d <> B2 -> Some(r, Load(sp, ea, d))
                    | Copy(src, r), Copy(r', d) when r = r' && not ((src = A || src = B) && (d = A || d = B)) && d <> A2 && d <> B2 -> Some(r, Copy(src, d))
                    | Copy(src, r), Store(r', sp, ea) when r = r' && between.Count = 0 -> Some(r, Store(src, sp, ea))
                    | Imm(op, r), Copy(r', d) when r = r' && op.force = Long && d <> A2 && d <> B2 -> Some(r, Imm(op, d))
                    | _ -> None
                  let placeAtK = between.Count > 0 && (match first with Copy _ -> true | _ -> false)
                  let clear (m: Move) =
                    let mr = Lower.moveReads m
                    let mw = Lower.moveWrites m
                    between |> Seq.forall (fun (s', d') ->
                      not (mr |> Array.exists (overlap d'))
                      && not (mw |> Array.exists (overlap d'))
                      && (placeAtK || not (mw |> Array.exists (overlap s'))))
                  let put = if placeAtK then k else i
                  let drop = if placeAtK then i else k
                  match combined with
                  | Some(r, m) when clear m && Lower.encodes (Par(None, [| m |])) && deadAfterAt put drop k (Op(Par(None, [| m |]))) r ->
                    cur[put] <- (Op(Par(None, [| m |])), snd cur[put])
                    dropped.Add drop |> ignore
                    searching <- false
                  | _ ->
                    match second with
                    | Copy(s', d') when between.Count = 0 && data s' && data d' && s' <> r && d' <> r ->
                      between.Add(s', d')
                      k <- k + 1
                    | _ -> searching <- false
                | Op instr ->
                  match first with
                  | Copy(src, _) when data src && src <> r && (between |> Seq.forall (fun (_, d') -> not (overlap src d'))) ->
                    match renameAlu r src instr with
                    | Some instr' when deadAfterAt k i k (Op instr') r ->
                      cur[k] <- (Op instr', snd cur[k])
                      dropped.Add i |> ignore
                      searching <- false
                    | _ -> searching <- false
                  | _ -> searching <- false
                | _ -> searching <- false
        | _ -> ()
  cur |> Array.mapi (fun i x -> i, x) |> Array.choose (fun (i, x) -> if dropped.Contains i then None else Some x)

let copyForward (items: (Item * int) array) = copyForwardWith false items

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

/// Loads into a data register of a workset word a DO loop does not write, or of an immediate,
/// where nothing else in the loop writes the register and the straight code before the load does
/// not read it: done once, before the loop (a parameter, a state word only read there, a
/// constant such as the zero a silent block writes).
let hoistFromY (first: int) (temps: int) (yFirst: int) (yTemps: int) (items: (Item * int) array) =
  let control (instr: Instr) =
    match instr with
    | Branch _ | Jump _ | Loop _ | Rep _ | Enddo -> true
    | _ -> false
  let overlaps (r: Reg) (ws: Reg array) = ws |> Array.exists (fun w -> Lower.storage w |> Array.exists (fun x -> Array.contains x (Lower.storage r)))
  let renameUse (fromReg: Reg) (toReg: Reg) (instr: Instr) =
    let swap r = if r = fromReg then toReg else r
    match instr with
    | Par(Some(Binary((AluOp.Add | AluOp.Sub | AluOp.Cmp) as op, s, d)), [||]) when s = fromReg ->
      Some(Par(Some(Binary(op, toReg, d)), [||]))
    | Par(Some(Mul(kind, neg, s1, s2, d)), [||]) when s1 = fromReg || s2 = fromReg ->
      let m = Par(Some(Mul(kind, neg, swap s1, swap s2, d)), [||])
      if Lower.encodes m then Some m else None
    | MulI(kind, neg, imm, s, d) when s = fromReg ->
      Some(MulI(kind, neg, imm, toReg, d))
    | Par(None, [| Store(s, sp, ea) |]) when s = fromReg ->
      let m = Par(None, [| Store(toReg, sp, ea) |])
      if Lower.encodes m then Some m else None
    | Par(None, [| Copy(s, d) |]) when s = fromReg && d <> toReg ->
      let m = Par(None, [| Copy(toReg, d) |])
      if Lower.encodes m then Some m else None
    | _ -> None
  let once (its: (Item * int) array) =
    let derivesWorksetAddr =
      its |> Array.exists (fun (it, _) ->
        match it with
        | Op(Lua(Disp(6, _), _)) -> true
        | Op(Par(_, moves)) -> moves |> Array.exists (function Copy(R 6, _) -> true | _ -> false)
        | _ -> false)
    let candidate (i: int) (e: int) (k: int) =
      let data (r: Reg) = r = X0 || r = X1 || r = Y0 || r = Y1
      // a workset word (its space and index), or an immediate (None): nothing in memory to watch
      let source =
        match fst its[k] with
        | Op(Par(None, [| Load(space, ea, dst) |])) when data dst ->
          match workset space ea with
          | Some(SMem(_, w)) -> Some(dst, Some(space, w), fun d -> Op(Par(None, [| Load(space, ea, d) |])))
          | _ -> None
        // not a word the loader patches: it stays at its label
        | Op(Par(None, [| Imm(op, dst) |])) when data dst && not (k > 0 && (match fst its[k - 1] with Comment t -> t.StartsWith machinedrum.Abi.markerPrefix | _ -> false)) ->
          Some(dst, None, fun d -> Op(Par(None, [| Imm(op, d) |])))
        | _ -> None
      match source with
      | Some(dst, word, rebuild) ->
          // every pass reaches the load: what comes before it branches only forward to it or
          // short of it, and does not read the register
          let inside (t: Operand) =
            match t.expr with
            | Sym l -> its |> Array.tryFindIndex (fun (x, _) -> x = Label l) |> Option.exists (fun at -> at > i && at <= k)
            | _ -> false
          let before (r: Reg) =
            [ i + 1 .. k - 1 ] |> List.forall (fun j ->
              match fst its[j] with
              | Comment _ | Label _ -> true
              | Op(Branch((Bcc _ | Bra), t)) -> inside t
              | Op instr when not (control instr) ->
                match readsOf instr with
                | Some rs -> not (overlaps r rs)
                | None -> false
              | _ -> false)
          let invariantWord =
            [ i + 1 .. e - 1 ] |> List.forall (fun j ->
              j = k ||
              match fst its[j] with
              | Op instr ->
                let stores =
                  match instr with
                  | Par(_, moves) ->
                    match word with
                    | None -> true
                    | Some(space, w) ->
                      let unaliased =
                        (space = Space.X && w >= first && w < temps)
                        || (space = Space.Y && ((w >= 1 && w < 9) || (w >= yFirst && w < yTemps)))
                        || not derivesWorksetAddr
                      moves |> Array.forall (fun m ->
                        match m with
                        | Store(_, sp, ea') when sp = space ->
                          match workset sp ea', ea' with
                          | Some(SMem(_, w')), _ -> w' <> w
                          | None, PostInc 7 -> true
                          | None, _ when unaliased -> true
                          | _ -> false
                        | _ -> true)
                  | _ -> true
                match writesOf instr with
                | Some ws -> stores && not (Array.contains (R 6) ws)
                // a subroutine may write the word
                | None -> control instr && (match instr with Branch((Bsr | Bscc _), _) | Jump((Jsr | Jscc _), _) -> false | _ -> true)
              | _ -> true)
          let noWrites (r: Reg) =
            [ i + 1 .. e - 1 ] |> List.forall (fun j ->
              j = k ||
              match fst its[j] with
              | Op instr ->
                match writesOf instr with
                | Some ws -> not (overlaps r ws)
                | None -> control instr
              | _ -> true)
          if not invariantWord then None
          elif before dst && noWrites dst then Some(fst its[k], None)
          elif dst = X0 || dst = Y0 then
            let alt = if dst = X0 then Y0 else X0
            let altFree =
              before alt && noWrites alt
              && ([ i + 1 .. e - 1 ] |> List.forall (fun j ->
                    match fst its[j] with
                    | Op instr when not (control instr) ->
                      match readsOf instr with Some rs -> not (overlaps alt rs) | None -> false
                    | _ -> true))
              && deadAfter its (e + 1) alt
            if not altFree then None
            else
              let mutable u = k + 1
              while u < e && (match fst its[u] with Comment _ -> true | _ -> false) do u <- u + 1
              if u >= e then None
              else
                match fst its[u] with
                | Op instr ->
                  match renameUse dst alt instr with
                  | Some instr' ->
                    let quietAfterU =
                      [ u + 1 .. e - 1 ] |> List.forall (fun j ->
                        match fst its[j] with
                        | Comment _ -> true
                        | Op ins when not (control ins) ->
                          match readsOf ins, writesOf ins with
                          | Some rs, Some ws -> not (overlaps dst rs) && not (overlaps dst ws)
                          | _ -> false
                        | _ -> false)
                    let rec deadBefore limit j =
                      if j >= limit then false
                      else
                        match fst its[j] with
                        | Comment _ -> deadBefore limit (j + 1)
                        | Op ins when not (control ins) ->
                          match readsOf ins, writesOf ins with
                          | Some rs, Some ws ->
                            if overlaps dst rs then false
                            elif ws |> Array.exists (fun x -> parts x = parts dst || x = dst) then true
                            else deadBefore limit (j + 1)
                          | _ -> false
                        | _ -> false
                    let dstDead =
                      deadBefore e (u + 1)
                      || (quietAfterU && deadBefore k (i + 1) && deadAfter its (e + 1) dst)
                    if dstDead then Some(rebuild alt, Some(u, Op instr')) else None
                  | None -> None
                | _ -> None
          else None
      | None -> None
    its |> Array.indexed |> Array.tryPick (fun (i, (it, _)) ->
      match it with
      | Op(Loop(_, _, { expr = Sym name })) ->
        match its |> Array.tryFindIndex (fun (x, _) -> x = Label name) with
        | Some e when e > i ->
          [ i + 1 .. e - 1 ] |> List.tryPick (fun k ->
            candidate i e k |> Option.map (fun (hoisted, rewrite) ->
              // before the loop's label and relocation marker
              let mutable p = i
              if p > 0 && fst its[p - 1] = machinedrum.Abi.relocateMarker then p <- p - 1
              if p > 0 && (match fst its[p - 1] with Label l -> l.EndsWith "_DO" | _ -> false) then p <- p - 1
              k, p, hoisted, rewrite))
        | _ -> None
      | _ -> None)
  let mutable cur = items
  let mutable go = true
  while go do
    match once cur with
    | Some(k, p, hoisted, rewrite) ->
      let moved = hoisted, snd cur[k]
      let cur' =
        match rewrite with
        | Some(u, it') -> cur |> Array.mapi (fun idx x -> if idx = u then (it', snd x) else x)
        | None -> cur
      let rest = cur' |> Array.removeAt k
      cur <- rest |> Array.insertAt p moved
    | None -> go <- false
  cur

let hoistFrom (first: int) (temps: int) (items: (Item * int) array) =
  hoistFromY first temps 0 0 items

let hoist (items: (Item * int) array) = hoistFrom 0 0 items

/// When two adjacent single-move loads read X memory into Y0/Y1 and Y memory into X0/X1 (in
/// either order), swapping their destination registers across the straight-line stretch until
/// both are dead lets `Lower.pack` merge the two loads into a single 1-word X:Y dual move.
let uncrossDualLoads (items: (Item * int) array) =
  let cur = Array.copy items
  let overlap (a: Reg) (b: Reg) =
    let pa = parts a
    parts b |> List.exists (fun x -> List.contains x pa)
  let rewriteInstr (r1: Reg) (r2: Reg) (instr: Instr) =
    let swap r = if r = r1 then r2 elif r = r2 then r1 else r
    match readsOf instr, writesOf instr with
    | Some rs, Some ws when not (ws |> Array.exists (fun w -> overlap w r1 || overlap w r2)) ->
      if not (rs |> Array.exists (fun r -> overlap r r1 || overlap r r2)) then Some instr
      else
        match instr with
        | Par(alu, moves) ->
          let alu' =
            match alu with
            | None -> Some None
            | Some a ->
              if not (Lower.aluReads a |> Array.exists (fun r -> overlap r r1 || overlap r r2)) then Some(Some a)
              else
                match a with
                | Binary((AluOp.Add | AluOp.Sub | AluOp.Cmp | AluOp.And | AluOp.Or | AluOp.Eor) as op, s, d) ->
                  Some(Some(Binary(op, swap s, d)))
                | Mul(kind, neg, s1, s2, d) ->
                  let a1 = Mul(kind, neg, swap s1, swap s2, d)
                  let a2 = Mul(kind, neg, swap s2, swap s1, d)
                  if Lower.encodes (Par(Some a1, [||])) then Some(Some a1)
                  elif Lower.encodes (Par(Some a2, [||])) then Some(Some a2)
                  else None
                | _ -> None
          let moves' =
            moves |> Array.map (fun m ->
              if not (Lower.moveReads m |> Array.exists (fun r -> overlap r r1 || overlap r r2)) then Some m
              else
                match m with
                | Store(s, sp, ea) -> Some(Store(swap s, sp, ea))
                | Copy(s, d) -> Some(Copy(swap s, d))
                | _ -> None)
          match alu', (if moves' |> Array.forall Option.isSome then Some(moves' |> Array.map Option.get) else None) with
          | Some a', Some ms' ->
            let ins' = Par(a', ms')
            if Lower.encodes ins' && Lower.wordsOf ins' <= Lower.wordsOf instr then Some ins' else None
          | _ -> None
        | MulI(kind, neg, imm, s, d) ->
          let ins' = MulI(kind, neg, imm, swap s, d)
          if Lower.encodes ins' then Some ins' else None
        | _ -> None
    | _ -> None
  let mutable i = 0
  while i < cur.Length do
    let mutable j = i + 1
    while j < cur.Length && (match fst cur[j] with Comment _ -> true | _ -> false) do j <- j + 1
    let swapped =
      if j < cur.Length then
        match fst cur[i], fst cur[j] with
        | Op(Par(None, [| Load(sp1, ea1, r1) |])), Op(Par(None, [| Load(sp2, ea2, r2) |]))
            when (sp1 = Space.X && sp2 = Space.Y && (r1 = Y0 || r1 = Y1) && (r2 = X0 || r2 = X1))
                 || (sp1 = Space.Y && sp2 = Space.X && (r1 = X0 || r1 = X1) && (r2 = Y0 || r2 = Y1)) ->
          let mX = if sp1 = Space.X then Load(Space.X, ea1, r2) else Load(Space.X, ea2, r1)
          let mY = if sp1 = Space.Y then Load(Space.Y, ea1, r2) else Load(Space.Y, ea2, r1)
          let dual = Par(None, [| mX; mY |])
          if Lower.disjoint (Lower.moveWrites mX) (Lower.moveWrites mY)
             && Lower.disjoint (Lower.moveWrites mX) (Lower.moveReads mY)
             && Lower.disjoint (Lower.moveWrites mY) (Lower.moveReads mX)
             && Lower.encodes dual && Lower.wordsOf dual = 1 then
            let rewrites = ResizeArray<int * Item>()
            let mutable k = j + 1
            let mutable scanning = true
            let mutable ok = false
            while scanning do
              if deadAfter cur k r1 && deadAfter cur k r2 then
                ok <- true
                scanning <- false
              elif k >= cur.Length then
                scanning <- false
              else
                match fst cur[k] with
                | Comment _ -> k <- k + 1
                | Op instr ->
                  match rewriteInstr r1 r2 instr with
                  | Some instr' ->
                    if instr' <> instr then rewrites.Add(k, Op instr')
                    k <- k + 1
                  | None -> scanning <- false
                | _ -> scanning <- false
            if ok then
              cur[i] <- (Op(Par(None, [| mX |])), snd cur[i])
              cur[j] <- (Op(Par(None, [| mY |])), snd cur[j])
              for idx, it' in rewrites do cur[idx] <- (it', snd cur[idx])
              true
            else false
          else false
        | _ -> false
      else false
    i <- if swapped then j + 1 else i + 1
  cur

/// Lower.pack with each item's source line kept (a merged instruction takes the first's), across
/// the comments quoting the F# (they follow the merged instruction).
let pack (items: (Item * int) array) =
  let mutable cur = uncrossDualLoads items
  let mutable changed = true
  while changed do
    changed <- false
    let out = ResizeArray<Item * int>()
    let mutable i = 0
    while i < cur.Length do
      let mutable j = i + 1
      while j < cur.Length && (match fst cur[j] with Comment _ -> true | _ -> false) do j <- j + 1
      let merged =
        match fst cur[i] with
        | Op _ when j < cur.Length ->
          match Lower.pack [| fst cur[i]; fst cur[j] |] with
          | [| merged |] -> Some merged
          | _ -> None
        | _ -> None
      match merged with
      | Some m ->
        out.Add(m, Ir.Origin.merge (snd cur[i]) (snd cur[j]))
        for k in i + 1 .. j - 1 do out.Add cur[k]
        changed <- true
        i <- j + 1
      | None ->
        out.Add cur[i]
        i <- i + 1
    cur <- out.ToArray()
  cur

/// Whether nothing reads `r` after item `from` before it is written, following the straight code
/// (an rts ends it; a branch or a loop gives up).
let deadOnExit (items: (Item * int) array) (from: int) (r: Reg) =
  let touches (rs: Reg array) = rs |> Array.exists (fun x -> Lower.storage x |> Array.exists (fun y -> Array.contains y (Lower.storage r)))
  let rec go k =
    if k >= items.Length then true
    else
      match fst items[k] with
      | Comment _ | Label _ -> go (k + 1)
      // an entry's rts; a subroutine's gives its result back in registers
      | Op Rts -> not (items[.. k] |> Array.exists (fun (it, _) -> match it with Label l -> l.StartsWith "SUB_" | _ -> false))
      | Op instr ->
        match readsOf instr, writesOf instr with
        | Some rs, Some ws -> if touches rs then false elif touches ws then true else go (k + 1)
        | _ -> false
      | _ -> false
  go from

/// Software pipelining, one stage: a DO loop whose body starts with a load through a
/// post-incremented address register, and ends with an instruction that load packs into, does the
/// load at the end of each pass, for the next one (nothing runs in between), and once before the
/// loop for the first. The last pass loads one word more, so the loaded register and the address
/// register must be dead after the loop.
let rotate (items: (Item * int) array) =
  let isOp (it: Item) = match it with Op _ -> true | _ -> false
  let once (its: (Item * int) array) =
    its |> Array.indexed |> Array.tryPick (fun (i, (it, _)) ->
      match it with
      | Op(Loop(_, _, { expr = Sym name })) ->
        match its |> Array.tryFindIndex (fun (x, _) -> x = Label name) with
        | Some e when e > i ->
          let first = [ i + 1 .. e - 1 ] |> List.tryFind (fun k -> match fst its[k] with Comment _ -> false | _ -> true)
          let last = [ e - 1 .. -1 .. i + 1 ] |> List.tryFind (fun k -> match fst its[k] with Comment _ -> false | _ -> true)
          match first, last with
          | Some f, Some l when f < l && isOp (fst its[l]) ->
            match fst its[f], fst its[l] with
            | Op(Par(None, [| Load(_, ((PostInc r | PostIncN r)), dst) |])) as load, (Op _ as tail)
                when r <> 6 && r <> 7 && (dst = X0 || dst = X1 || dst = Y0 || dst = Y1)
                     && deadOnExit its (e + 1) dst && deadOnExit its (e + 1) (R r) ->
              // the load joins the tail (an X and a Y move: either order of the two moves)
              match Lower.pack [| tail; load |], tail, load with
              | [| merged |], _, _ -> Some(i, f, l, load, merged)
              | _, Op(Par(None, [| m |])), Op(Par(None, [| m' |])) when Lower.encodes (Par(None, [| m'; m |])) ->
                Some(i, f, l, load, Op(Par(None, [| m'; m |])))
              | _ -> None
            | _ -> None
          | _ -> None
        | _ -> None
      | _ -> None)
  let mutable cur = items
  let mutable go = true
  while go do
    match once cur with
    | Some(i, f, l, load, merged) ->
      let line = snd cur[f]
      let body = cur |> Array.mapi (fun k x -> if k = l then (merged, snd x) else x)
      let body = body |> Array.removeAt f
      // before the loop's label and relocation marker
      let mutable p = i
      if p > 0 && fst body[p - 1] = machinedrum.Abi.relocateMarker then p <- p - 1
      if p > 0 && (match fst body[p - 1] with Label n -> n.EndsWith "_DO" | _ -> false) then p <- p - 1
      cur <- body |> Array.insertAt p (load, line)
    | None -> go <- false
  cur

/// Each item's weight: the trip counts of the DO loops around it (32 for a count known only
/// when running), as staticCost counts them.
let tripWeights (items: Item array) =
  let w = Array.zeroCreate<int64> items.Length
  let mutable around : (string * int64) list = []
  for i in 0 .. items.Length - 1 do
    match items[i] with
    | Label l ->
      while (match around with (e, _) :: _ when e = l -> true | _ -> false) do around <- List.tail around
    | _ -> ()
    w[i] <- around |> List.fold (fun t (_, n) -> t * n) 1L
    match items[i] with
    | Op(Loop(_, count, { expr = Sym e })) ->
      let n = match count with Count { expr = Num n } when n > 0L -> n | _ -> 32L
      around <- (e, n) :: around
    | _ -> ()
  w

/// An accumulator temporary in a free accumulator. The compiler keeps an Acc local it cannot hold
/// in a register as three X temporaries (a1, a0, a2 at k, k+1, k+2), stored and reloaded whole,
/// three moves each way. Where another accumulator is neither read, written nor live anywhere
/// the temporary is live, it holds the value instead: the store becomes `tfr a,b`, the reload
/// `tfr b,a`, a read of the high word `move b1,x0`. Only temporaries every access of which is
/// one of those forms; calls, returns and anything not modelled count as using every register.
let promoteY (first: int) (temps: int) (yFirst: int) (yTemps: int) (items: (Item * int) array) =
  let its = Array.map fst items
  let n = its.Length
  let temp = tempSlot first temps yFirst yTemps
  let single i = match its[i] with Op(Par(None, [| m |])) -> Some m | _ -> None
  let hi acc = if acc = A then A1 else B1
  let lo acc = if acc = A then A0 else B0
  let ext acc = if acc = A then A2 else B2
  // the three moves of a whole accumulator at k, from item i: (k, acc, store?)
  let triple i =
    if i + 2 >= n then None
    else
      match single i, single (i + 1), single (i + 2) with
      | Some(Store(r1, s1, e1)), Some(Store(r0, s0, e0)), Some(Store(r2, s2, e2)) ->
        match accOf r1, temp s1 e1, temp s0 e0, temp s2 e2 with
        | Some acc, Some k, Some k0, Some k2 when r1 = hi acc && r0 = lo acc && r2 = ext acc && k0 = k + 1 && k2 = k + 2 ->
          Some(k, acc, true)
        | _ -> None
      | Some(Load(s1, e1, r1)), Some(Load(s0, e0, r0)), Some(Load(s2, e2, r2)) when r1 = A || r1 = B ->
        match temp s1 e1, temp s0 e0, temp s2 e2 with
        | Some k, Some k0, Some k2 when r0 = lo r1 && r2 = ext r1 && k0 = k + 1 && k2 = k + 2 -> Some(k, r1, false)
        | _ -> None
      | _ -> None
  // every access to the temporaries: whole stores and loads, high-word reads, and anything else
  let accesses = Dictionary<int, ResizeArray<int * string>>()   // k -> (item, kind)
  let bad = HashSet<int>()
  let note k i kind =
    if not (accesses.ContainsKey k) then accesses[k] <- ResizeArray()
    accesses[k].Add(i, kind)
  let wordOf = Dictionary<int, int>()                            // temporary word -> its slot k
  let mutable i = 0
  while i < n do
    match triple i with
    | Some(k, acc, isStore) ->
      note k i (if isStore then "store" else "load")
      for j in 0 .. 2 do wordOf[k + j] <- k
      i <- i + 3
    | None ->
      match its[i] with
      | Op(Par(None, [| Load(sp, ea, r) |])) when (temp sp ea).IsSome && (r = X0 || r = X1 || r = Y0 || r = Y1) ->
        note (temp sp ea).Value i "read"
      | Op(Par(_, moves)) ->
        for m in moves do
          match m with
          | Load(sp, ea, _) | Store(_, sp, ea) -> temp sp ea |> Option.iter (fun k -> bad.Add k |> ignore)
          | _ -> ()
      | _ -> ()
      i <- i + 1
  // a slot is k with a whole store; a read of a word inside another slot, or of k+1/k+2, spoils it
  for KeyValue(k, list) in accesses |> Seq.toArray do
    if not (list |> Seq.exists (fun (_, kind) -> kind = "store")) then bad.Add k |> ignore
  for KeyValue(k, list) in accesses |> Seq.toArray do
    match wordOf.TryGetValue k with
    | true, slot when slot <> k -> bad.Add slot |> ignore; bad.Add k |> ignore
    | _ -> ()
  for b in bad |> Seq.toArray do
    match wordOf.TryGetValue b with
    | true, slot -> bad.Add slot |> ignore
    | _ -> ()
  let slots = accesses.Keys |> Seq.filter (fun k -> not (bad.Contains k)) |> Seq.toArray
  match successors its with
  | None -> items
  | Some succ when slots.Length > 0 ->
    let preds = Array.create n 0
    for i in 0 .. n - 1 do for j in succ[i] do preds[j] <- preds[j] + 1
    // backward liveness of one fact over the items: use/def per item
    let live (uses: int -> bool) (defs: int -> bool) =
      let liveIn = Array.create n false
      let liveOut = Array.create n false
      let mutable changed = true
      while changed do
        changed <- false
        for i in n - 1 .. -1 .. 0 do
          let out = succ[i] |> Array.exists (fun j -> liveIn[j])
          let inn = uses i || (out && not (defs i))
          if out <> liveOut[i] || inn <> liveIn[i] then
            liveOut[i] <- out
            liveIn[i] <- inn
            changed <- true
      liveIn, liveOut
    let control (instr: Instr) =
      match instr with
      | Branch((Bra | Bcc _), _) | Jump((Jmp | Jcc _), _) | Loop(_, Count _, _) | Enddo | Nop -> true
      | _ -> false
    let firstSub = firstSubroutine its
    // what an item does to accumulator t: (reads, writes it whole, touches it at all)
    let effect (t: Reg) i =
      match its[i] with
      | Op Rts -> let r = i >= firstSub && t = A in r, false, r
      | Op instr when control instr -> false, false, false
      | Op instr ->
        match readsOf instr, writesOf instr with
        | Some rs, Some ws ->
          let on (r: Reg) = Array.contains t (Lower.storage r)
          Array.exists on rs, writesWhole t instr, Array.exists on rs || Array.exists on ws
        | _ -> true, false, true
      | _ -> false, false, false
    let weights = tripWeights its
    let accessItems k = accesses[k] |> Seq.collect (fun (i, kind) -> if kind = "read" then [ i ] else [ i; i + 1; i + 2 ]) |> Set.ofSeq
    let chosen = ResizeArray<int * Reg>()
    let taken = Dictionary<Reg, Set<int>>()
    for t in [ B; A ] do
      let tIn, tOut = live (fun i -> let r, _, _ = effect t i in r) (fun i -> let _, w, _ = effect t i in w)
      taken[t] <- Set.empty
      let ranked =
        slots |> Array.sortByDescending (fun k -> accesses[k] |> Seq.sumBy (fun (i, _) -> weights[i]))
      for k in ranked do
        if not (chosen |> Seq.exists (fun (k', _) -> k' = k)) then
          let own = accessItems k
          let uses i = accesses[k] |> Seq.exists (fun (j, kind) -> kind <> "store" && j = i)
          let defs i = accesses[k] |> Seq.exists (fun (j, kind) -> kind = "store" && j = i)
          let sIn, sOut = live uses defs
          let range = [ 0 .. n - 1 ] |> List.filter (fun i -> sIn[i] || sOut[i] || own.Contains i) |> Set.ofList
          let entryLive = [ 0 .. n - 1 ] |> List.exists (fun i -> preds[i] = 0 && sIn[i])
          let sources = accesses[k] |> Seq.forall (fun (j, kind) ->
            match kind with
            | "read" -> true
            | _ -> match triple j with Some(_, acc, _) -> acc <> t | None -> false)
          let free =
            range |> Set.forall (fun i ->
              not tIn[i] && not tOut[i] && (own.Contains i || (let _, _, touches = effect t i in not touches)))
          if sources && not entryLive && free && Set.isEmpty (Set.intersect range taken[t]) then
            chosen.Add(k, t)
            taken[t] <- Set.union taken[t] range
    if chosen.Count = 0 then items
    else
      let rewrite = Dictionary<int, Item option>()       // item -> replacement (None: dropped)
      for k, t in chosen do
        let tAcc = if t = A then Acc.A else Acc.B
        for i, kind in accesses[k] do
          match kind, triple i with
          | "store", Some(_, src, _) ->
            rewrite[i] <- Some(Op(Par(Some(Binary(AluOp.Tfr, src, tAcc)), [||])))
            rewrite[i + 1] <- None
            rewrite[i + 2] <- None
          | "load", Some(_, dst, _) ->
            rewrite[i] <- Some(Op(Par(Some(Binary(AluOp.Tfr, t, (if dst = A then Acc.A else Acc.B))), [||])))
            rewrite[i + 1] <- None
            rewrite[i + 2] <- None
          | "read", _ ->
            match its[i] with
            | Op(Par(None, [| Load(_, _, r) |])) -> rewrite[i] <- Some(Op(Par(None, [| Copy(hi t, r) |])))
            | _ -> ()
          | _ -> ()
      if rewrite.Values |> Seq.exists (function Some(Op instr) -> not (Lower.encodes instr) | _ -> false) then items
      else
        [| for i in 0 .. n - 1 do
             match rewrite.TryGetValue i with
             | true, Some it -> yield it, snd items[i]
             | true, None -> ()
             | _ -> yield items[i] |]
  | _ -> items

let promote (first: int) (temps: int) (items: (Item * int) array) =
  promoteY first temps 0 0 items

/// The compiler's temporaries in registers, web by web. A temporary word is reused for many
/// expressions; each definition with the uses it reaches (reaching definitions, per word) is a
/// web, given a register when one is neither touched nor live anywhere the web is live:
///
/// - a whole accumulator (a1, a0, a2 at k, k+1, k+2, three moves each way; reads of the high or
///   low word alone too) in A or B: `tfr a,b`, `tfr b,a`, `move b1,x0`;
/// - a word (`move x0,x:k` or `move a,x:k`, read by `move x:k,y0` or `move x:k,a`) in x0, x1,
///   y0, y1, b1 or b0: a register move does to the word what the memory move did (a limited store
///   of an accumulator, a sign-extended load into one).
///
/// Heavier webs (trip-weighted accesses) first; webs sharing a register have disjoint ranges. A
/// move between a web's register and itself goes. Calls, returns and anything not modelled use
/// every register, so a web across one keeps its memory.
let assignY (first: int) (temps: int) (yFirst: int) (yTemps: int) (items: (Item * int) array) =
  let its = Array.map fst items
  let n = its.Length
  let temp = tempSlot first temps yFirst yTemps
  let single i = match its[i] with Op(Par(None, [| m |])) -> Some m | _ -> None
  let hi acc = if acc = A then A1 else B1
  let lo acc = if acc = A then A0 else B0
  let ext acc = if acc = A then A2 else B2
  let data (r: Reg) = r = X0 || r = X1 || r = Y0 || r = Y1
  let triple i =
    if i + 2 >= n then None
    else
      match single i, single (i + 1), single (i + 2) with
      | Some(Store(r1, s1, e1)), Some(Store(r0, s0, e0)), Some(Store(r2, s2, e2)) ->
        match accOf r1, temp s1 e1, temp s0 e0, temp s2 e2 with
        | Some acc, Some k, Some k0, Some k2 when r1 = hi acc && r0 = lo acc && r2 = ext acc && k0 = k + 1 && k2 = k + 2 ->
          Some(k, acc, true)
        | _ -> None
      | Some(Load(s1, e1, r1)), Some(Load(s0, e0, r0)), Some(Load(s2, e2, r2)) when r1 = A || r1 = B ->
        match temp s1 e1, temp s0 e0, temp s2 e2 with
        | Some k, Some k0, Some k2 when r0 = lo r1 && r2 = ext r1 && k0 = k + 1 && k2 = k + 2 -> Some(k, r1, false)
        | _ -> None
      | _ -> None
  // the accesses: an access item, its kind, the word(s) it defines and uses
  // kinds: "tstore" acc, "tload" acc, "hiread" reg (word k of a triple, or a word), "wstore" reg, "wload" reg
  let kind = Array.create n ""
  let reg = Array.create n A
  let word = Array.create n -1
  let defs = Array.create n Set.empty<int>
  let uses = Array.create n Set.empty<int>
  let mutable i = 0
  while i < n do
    match triple i with
    | Some(k, acc, isStore) ->
      kind[i] <- (if isStore then "tstore" else "tload"); reg[i] <- acc; word[i] <- k
      if isStore then defs[i] <- set [ k; k + 1; k + 2 ] else uses[i] <- set [ k; k + 1; k + 2 ]
      kind[i + 1] <- "part"; kind[i + 2] <- "part"
      i <- i + 3
    | None ->
      match its[i] with
      | Op(Par(None, [| Load(sp, ea, r) |])) when (temp sp ea).IsSome && (data r || r = A || r = B) ->
        let k = (temp sp ea).Value
        kind[i] <- "wload"; reg[i] <- r; word[i] <- k; uses[i] <- set [ k ]
      | Op(Par(None, [| Store(r, sp, ea) |])) when (temp sp ea).IsSome && (data r || r = A || r = B || r = A1 || r = B1 || r = A0 || r = B0) ->
        let k = (temp sp ea).Value
        kind[i] <- "wstore"; reg[i] <- r; word[i] <- k; defs[i] <- set [ k ]
      | Op(Par(_, moves)) ->
        // any other access to a temporary: a definition and a use nothing can take over
        for m in moves do
          match m with
          | Load(sp, ea, _) -> temp sp ea |> Option.iter (fun k -> kind[i] <- "other"; uses[i] <- uses[i].Add k)
          | Store(_, sp, ea) -> temp sp ea |> Option.iter (fun k -> kind[i] <- "other"; defs[i] <- defs[i].Add k)
          | _ -> ()
      | _ -> ()
      i <- i + 1
  match successors its with
  | None -> items
  | Some succ ->
    let preds = Array.init n (fun _ -> ResizeArray<int>())
    for i in 0 .. n - 1 do for j in succ[i] do preds[j].Add i
    // reaching definitions, per word: the definition items reaching each item's entry
    let words = Array.append (Array.collect Set.toArray defs) (Array.collect Set.toArray uses) |> Array.distinct
    let parent = Dictionary<int, int>()          // union-find over access items
    let rec find x = match parent.TryGetValue x with | true, p when p <> x -> (let r = find p in parent[x] <- r; r) | _ -> x
    let union a b = let ra, rb = find a, find b in if ra <> rb then parent[ra] <- rb
    let unreached = HashSet<int>()               // uses some path reaches with no definition
    for k in words do
      let reach = Array.create n Set.empty<int>  // at entry
      let outOf i = if defs[i].Contains k then Set.singleton i else reach[i]
      let mutable changed = true
      let entry = Set.singleton -1               // the function's entry: no definition yet
      while changed do
        changed <- false
        for i in 0 .. n - 1 do
          let r =
            if preds[i].Count = 0 then entry
            else preds[i] |> Seq.fold (fun acc p -> Set.union acc (outOf p)) Set.empty
          if r <> reach[i] then reach[i] <- r; changed <- true
      for i in 0 .. n - 1 do
        if uses[i].Contains k then
          for d in reach[i] do
            if d < 0 then unreached.Add i |> ignore else union i d
    // webs: the access items of one class
    let members = Dictionary<int, ResizeArray<int>>()
    for i in 0 .. n - 1 do
      if kind[i] <> "" && kind[i] <> "part" then
        let r = find i
        if not (members.ContainsKey r) then members[r] <- ResizeArray()
        members[r].Add i
    let control (instr: Instr) =
      match instr with
      | Branch((Bra | Bcc _), _) | Jump((Jmp | Jcc _), _) | Loop(_, Count _, _) | Enddo | Nop -> true
      | _ -> false
    let firstSub = firstSubroutine its
    // what an item does to register t: (reads, writes it whole, touches it)
    let effect (t: Reg) i =
      match its[i] with
      | Op Rts -> let r = i >= firstSub && (t = A || t = X0) in r, false, r
      | Op instr when control instr -> false, false, false
      | Op instr ->
        match readsOf instr, writesOf instr with
        | Some rs, Some ws ->
          let on (r: Reg) = Lower.storage r |> Array.exists (fun x -> Array.contains x (Lower.storage t))
          Array.exists on rs, writesWhole t instr, Array.exists on rs || Array.exists on ws
        | _ -> true, false, true
      | _ -> false, false, false
    let live (u: int -> bool) (d: int -> bool) =
      let liveIn = Array.create n false
      let liveOut = Array.create n false
      let mutable changed = true
      while changed do
        changed <- false
        for i in n - 1 .. -1 .. 0 do
          let out = succ[i] |> Array.exists (fun j -> liveIn[j])
          let inn = u i || (out && not (d i))
          if out <> liveOut[i] || inn <> liveIn[i] then
            liveOut[i] <- out
            liveIn[i] <- inn
            changed <- true
      liveIn, liveOut
    let weights = tripWeights its
    let span i = if kind[i] = "tstore" || kind[i] = "tload" then [ i; i + 1; i + 2 ] else [ i ]
    let regLive = Dictionary<Reg, bool array * bool array>()
    let liveOf t =
      match regLive.TryGetValue t with
      | true, l -> l
      | _ ->
        let l = live (fun i -> let r, _, _ = effect t i in r) (fun i -> let _, w, _ = effect t i in w)
        regLive[t] <- l
        l
    let webs =
      members.Values
      |> Seq.map (fun ms -> ms.ToArray())
      |> Seq.filter (fun ms ->
        let kinds = ms |> Array.map (fun i -> kind[i]) |> Set.ofArray
        let wholeWeb =
          // stores and reloads of a whole accumulator at k, reads of its high or low word into a data register
          kinds.Contains "tstore" && Set.isSubset kinds (set [ "tstore"; "tload"; "wload" ])
          && (let k = word[ms |> Array.find (fun i -> kind[i] = "tstore")]
              ms |> Array.forall (fun i ->
                match kind[i] with
                | "tstore" | "tload" -> word[i] = k
                | "wload" -> (word[i] = k || word[i] = k + 1) && data reg[i]
                | _ -> false))
        let wordWeb = kinds.Contains "wstore" && Set.isSubset kinds (set [ "wstore"; "wload" ])
        not (ms |> Array.exists unreached.Contains) && (wholeWeb || wordWeb))
      |> Seq.sortByDescending (fun ms -> ms |> Array.sumBy (fun i -> weights[i]))
      |> Array.ofSeq
    let taken = Dictionary<Reg, HashSet<int>>()
    let rewrite = Dictionary<int, Item option>()
    for ms in webs do
      let own = ms |> Array.collect (fun i -> Array.ofList (span i)) |> Set.ofArray
      let isDef i = kind[i] = "tstore" || kind[i] = "wstore"
      let sIn, sOut = live (fun i -> Array.contains i ms && not (isDef i)) (fun i -> Array.contains i ms && isDef i)
      let range = [ 0 .. n - 1 ] |> List.filter (fun i -> sIn[i] || sOut[i] || own.Contains i)
      let rec nextOp k =
        if k >= n then None
        else
          match (match rewrite.TryGetValue k with true, v -> v | _ -> Some its[k]) with
          | None | Some(Comment _) -> nextOp (k + 1)
          | Some it -> Some it
      let isTriple = ms |> Array.exists (fun i -> kind[i] = "tstore")
      let canTfrB =
        not isTriple
        && ms |> Array.forall (fun i -> (kind[i] = "wstore" && reg[i] = A1) || (kind[i] = "wload" && data reg[i]))
        && (let maxStore = ms |> Array.filter (fun i -> kind[i] = "wstore") |> Array.map (fun i -> weights[i]) |> Array.max
            ms |> Array.forall (fun i -> weights[i] <= maxStore))
      let hasSquare =
        not isTriple
        && ms |> Array.exists (fun i ->
          kind[i] = "wload" &&
          match nextOp (i + 1) with
          | Some(Op(Par(Some(Mul(_, _, s1, s2, _)), [||]))) -> s1 = reg[i] && s2 = reg[i]
          | _ -> false)
      let candidates =
        (if isTriple then [ B; A ] elif canTfrB then [ Y1; X1; Y0; X0; B ] else [ Y1; X1; Y0; X0 ])
        |> List.sortByDescending (fun t ->
          if isTriple || (hasSquare && (t = X0 || t = Y0)) then
            1L + (ms |> Array.sumBy (fun i -> if reg[i] = t then weights[i] else 0L))
          elif ms |> Array.forall (fun i -> reg[i] = t) then 1L
          else 0L)
      let fits t =
        let tIn, tOut = liveOf t
        let clear =
          parts t |> List.forall (fun p ->
            taken.TryGetValue p |> function | true, s -> range |> List.forall (fun i -> not (s.Contains i)) | _ -> true)
        clear &&
        range |> List.forall (fun i ->
          if own.Contains i then
            // a store from another register writes t: t must be dead after it
            match kind[i] with
            | "tstore" -> reg[i] = t || not tOut[i + 2]
            | "wstore" -> reg[i] = t || not tOut[i]
            | _ -> true
          else not tIn[i] && not tOut[i] && (let _, _, touches = effect t i in not touches))
      match candidates |> List.tryFind fits with
      | None -> ()
      | Some t ->
        let tAcc = if t = A then Acc.A else Acc.B
        let local = Dictionary<int, Item option>()
        for i in ms do
          match kind[i] with
          | "tstore" ->
            local[i] <- (if reg[i] = t then None else Some(Op(Par(Some(Binary(AluOp.Tfr, reg[i], tAcc)), [||]))))
            local[i + 1] <- None; local[i + 2] <- None
          | "tload" ->
            local[i] <- (if reg[i] = t then None else Some(Op(Par(Some(Binary(AluOp.Tfr, t, (if reg[i] = A then Acc.A else Acc.B))), [||]))))
            local[i + 1] <- None; local[i + 2] <- None
          | "wload" when isTriple ->
            let k = word[ms |> Array.find (fun j -> kind[j] = "tstore")]
            local[i] <- Some(Op(Par(None, [| Copy((if word[i] = k + 1 then lo t else hi t), reg[i]) |])))
          | "wload" when t = B -> local[i] <- Some(Op(Par(None, [| Copy(B1, reg[i]) |])))
          | "wload" -> local[i] <- (if reg[i] = t then None else Some(Op(Par(None, [| Copy(t, reg[i]) |]))))
          | "wstore" when t = B ->
            let useMove =
              match nextOp (i + 1) with
              | Some(Op(Par(Some _, [||]))) -> true
              | Some(Op(Par(None, [| Copy(s, d) |]))) when data s && data d -> true
              | _ -> false
            local[i] <- Some(Op(if useMove then Par(None, [| Copy(A1, B1) |]) else Par(Some(Binary(AluOp.Tfr, A, Acc.B)), [||])))
          | "wstore" -> local[i] <- (if reg[i] = t then None else Some(Op(Par(None, [| Copy(reg[i], t) |]))))
          | _ -> ()
        let encodes = local.Values |> Seq.forall (function Some(Op instr) -> Lower.encodes instr | _ -> true)
        if encodes then
          for KeyValue(k, v) in local do rewrite[k] <- v
          for p in parts t do
            if not (taken.ContainsKey p) then taken[p] <- HashSet()
            for i in range do taken[p].Add i |> ignore
    if rewrite.Count = 0 then items
    else
      [| for i in 0 .. n - 1 do
           match rewrite.TryGetValue i with
           | true, Some it -> yield it, snd items[i]
           | true, None -> ()
           | _ -> yield items[i] |]

let assign (first: int) (temps: int) (items: (Item * int) array) =
  assignY first temps 0 0 items

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
      | Some(Label _, _) | Some(Op(Loop _), _) | Some(Op(Branch _), _) | Some(Op(Jump _), _) | None -> out.Add(Op Nop, line)
      | _ -> ()
      out.Add(it, line)
    | _ -> out.Add(it, line)
  out.ToArray()

/// Walk workset sub-blocks in X: and Y: with a free address register (`r1..r5`), turning 2-cycle
/// `x:(r6+$k)` / `y:(r6+$k)` moves (`Alone`) into 1-cycle `(rN)+` / `(rN)-` / `(rN)` / `(rN)+nN` /
/// `(rN)-nN` moves (`Parallel`) that `pack` can fold into adjacent ALU and dual-move instructions.
let walkWith (yLimit: int) (items: (Item * int) array) =
  let cur = Array.copy items
  let n = cur.Length
  if n = 0 then cur
  else

  let dispOf = function
    | Op(Par(None, [| Load(sp, Disp(6, { expr = Num k }), reg) |])) when sp <> Space.Y || int k < yLimit -> Some(sp, int k, reg, false)
    | Op(Par(None, [| Store(reg, sp, Disp(6, { expr = Num k })) |])) when sp <> Space.Y || int k < yLimit -> Some(sp, int k, reg, true)
    | _ -> None

  let stepEa r delta =
    if delta = 0 then Ind r
    elif delta = 1 then PostInc r
    elif delta = -1 then PostDec r
    elif delta > 1 then PostIncN r
    else PostDecN r

  let rewriteAt (idx: int) (ea: Ea) =
    let it, ln = cur[idx]
    match dispOf it with
    | Some(sp, _, reg, false) -> cur[idx] <- (Op(Par(None, [| Load(sp, ea, reg) |])), ln)
    | Some(sp, _, reg, true)  -> cur[idx] <- (Op(Par(None, [| Store(reg, sp, ea) |])), ln)
    | None -> ()

  let canPackWith (idx: int) (sp: Space) (reg: Reg) (isStore: bool) =
    if idx < 0 || idx >= n then false
    else
      let mv = Op(Par(None, [| (if isStore then Store(reg, sp, Ind 1) else Load(sp, Ind 1, reg)) |]))
      let mutable j = idx - 1
      while j >= 0 && (match fst cur[j] with Comment _ -> true | _ -> false) do j <- j - 1
      let prevOk =
        if j >= 0 then
          match fst cur[j] with
          | Op _ as prev -> (Lower.pack [| prev; mv |]).Length = 1
          | _ -> false
        else false
      let mutable k = idx + 1
      while k < n && (match fst cur[k] with Comment _ -> true | _ -> false) do k <- k + 1
      let nextOk =
        if k < n then
          match fst cur[k] with
          | Op _ as next -> (Lower.pack [| mv; next |]).Length = 1
          | _ -> false
        else false
      prevOk || nextOk

  let canWalkSeq (accs: (int * int * Reg * bool) array) (endOff: int) =
    if accs.Length < 2 then None
    else
      let _, k0, _, _ = accs[0]
      if k0 < -64 || k0 > 63 then None
      else
        let deltas =
          [| for a in 0 .. accs.Length - 1 ->
               let _, ka, _, _ = accs[a]
               let kb =
                 if a + 1 < accs.Length then
                   let _, k, _, _ = accs[a + 1]
                   k
                 else endOff
               kb - ka |]
        let nonUnit = deltas |> Array.map abs |> Array.filter (fun d -> d > 1) |> Array.distinct
        if nonUnit.Length = 0 then Some(k0, None, deltas)
        elif nonUnit.Length = 1 && nonUnit[0] <= 255 then Some(k0, Some nonUnit[0], deltas)
        else None

  // Only walk inside RENDER and subroutines reachable from RENDER: INIT and TRIG do not count
  // toward render cycles, and extra setup words there would grow program size for zero render gain.
  let its0 = Array.map fst cur
  let labels0 =
    its0
    |> Array.mapi (fun i it -> match it with Label l -> Some(l, i) | _ -> None)
    |> Array.choose id
    |> Map.ofArray
  let renderIn = Array.create n false
  match labels0.TryFind "RENDER" with
  | None -> cur
  | Some rStart ->
    let rQueue = Queue<int>()
    rQueue.Enqueue rStart
    while rQueue.Count > 0 do
      let idx = rQueue.Dequeue()
      if idx >= 0 && idx < n && not renderIn[idx] then
        renderIn[idx] <- true
        match its0[idx] with
        | Op Rts | Op Rti -> ()
        | Op(Jump(op, { expr = Sym t })) ->
          match labels0.TryFind t with Some ti -> rQueue.Enqueue ti | None -> ()
          if op <> Jmp then rQueue.Enqueue (idx + 1)
        | Op(Branch(Bsr, { expr = Sym t })) ->
          match labels0.TryFind t with Some ti -> rQueue.Enqueue ti | None -> ()
          rQueue.Enqueue (idx + 1)
        | Op(Branch(_, { expr = Sym t }))
        | Op(BitBranch(_, _, _, { expr = Sym t })) ->
          match labels0.TryFind t with Some ti -> rQueue.Enqueue ti | None -> ()
          rQueue.Enqueue (idx + 1)
        | _ -> rQueue.Enqueue (idx + 1)

    let insertions = ResizeArray<int * (Item * int)>()

    let computeRegInfo () =
      let its = Array.map fst cur
      let labels =
        its
        |> Array.mapi (fun i it -> match it with Label l -> Some(l, i) | _ -> None)
        |> Array.choose id
        |> Map.ofArray
      let succ =
        match successors its with
        | Some s -> s
        | None -> Array.init n (fun i -> if i + 1 < n then [| i + 1 |] else [||])
      let rReads  = Array.init n (fun _ -> Array.create 8 false)
      let rWrites = Array.init n (fun _ -> Array.create 8 false)
      let nReads  = Array.init n (fun _ -> Array.create 8 false)
      let nWrites = Array.init n (fun _ -> Array.create 8 false)
      let mTouch  = Array.init n (fun _ -> Array.create 8 false)
      let control (instr: Instr) =
        match instr with
        | Branch((Bra | Bcc _), _) | Jump((Jmp | Jcc _), _) | Loop(_, Count _, _) | Enddo | Nop | Rts | Rti -> true
        | Branch(Bsr, { expr = Sym s }) when s.StartsWith "SUB_" && not (s.StartsWith "SUB_K_HAND_") -> true
        | _ -> false
      let markRegs i (rs: Reg array) (ws: Reg array) =
        for r in 1 .. 5 do
          if Array.contains (R r) rs then rReads[i][r] <- true
          if Array.contains (R r) ws then rWrites[i][r] <- true
          if Array.contains (N r) rs then nReads[i][r] <- true
          if Array.contains (N r) ws then nWrites[i][r] <- true
          if Array.contains (M r) rs || Array.contains (M r) ws then mTouch[i][r] <- true
      for i in 0 .. n - 1 do
        match its[i] with
        | Op(Lua(ea, dst)) ->
          let rs = Lower.eaRegs ea
          for r in 1 .. 5 do
            if Array.contains (R r) rs then rReads[i][r] <- true
            if Array.contains (N r) rs then nReads[i][r] <- true
            if dst = R r then rWrites[i][r] <- true
            if dst = N r then nWrites[i][r] <- true
        | Op(Loop(_, FromReg r, _)) | Op(BitBranch(_, _, BitReg r, _)) ->
          markRegs i (Lower.storage r) [||]
        | Op(Loop(_, FromMem(_, ea), _)) | Op(BitBranch(_, _, BitMem(_, ea), _)) ->
          markRegs i (Lower.eaRegs ea) (Lower.eaWrites ea)
        | Op instr when control instr -> ()
        | Op instr ->
          match readsOf instr, writesOf instr with
          | Some rs, Some ws -> markRegs i rs ws
          | _ ->
            for r in 1 .. 5 do
              rReads[i][r] <- true; rWrites[i][r] <- true
              nReads[i][r] <- true; nWrites[i][r] <- true
              mTouch[i][r] <- true
        | _ -> ()
      for idx, (it, _) in insertions do
        match it with
        | Op(Lua(_, R r)) | Op(Par(None, [| Copy(_, R r) |])) when r >= 1 && r <= 5 -> rWrites[idx][r] <- true
        | Op(Par(None, [| Imm(_, N r) |])) when r >= 1 && r <= 5 -> nWrites[idx][r] <- true
        | _ -> ()
      let subBounds = Array.init n (fun _ -> None)
      let subList = ResizeArray<string * int * int>()
      for KeyValue(l, sIdx) in labels do
        if l.StartsWith "SUB_" && not (l.StartsWith "SUB_K_HAND_") then
          let mutable eIdx = sIdx
          while eIdx < n && its[eIdx] <> Op Rts do eIdx <- eIdx + 1
          if eIdx < n then
            subList.Add(l, sIdx, eIdx)
            for k in sIdx .. eIdx do subBounds[k] <- Some(l, sIdx, eIdx)
      let rSubTouch = Array.init n (fun _ -> Array.create 8 false)
      let nSubTouch = Array.init n (fun _ -> Array.create 8 false)
      let mutable subProp = true
      while subProp do
        subProp <- false
        for (subName, sIdx, eIdx) in subList do
          for r in 1 .. 5 do
            let mutable rt = false
            let mutable nt = false
            let mutable mt = false
            for k in sIdx .. eIdx do
              if rReads[k][r] || rWrites[k][r] || rSubTouch[k][r] then rt <- true
              if nReads[k][r] || nWrites[k][r] || nSubTouch[k][r] then nt <- true
              if mTouch[k][r] then mt <- true
            if rt || nt || mt then
              for i in 0 .. n - 1 do
                match its[i] with
                | Op(Branch(Bsr, { expr = Sym s })) when s = subName ->
                  if rt && not (rSubTouch[i][r]) then rSubTouch[i][r] <- true; subProp <- true
                  if nt && not (nSubTouch[i][r]) then nSubTouch[i][r] <- true; subProp <- true
                  if mt && not (mTouch[i][r]) then mTouch[i][r] <- true; subProp <- true
                | _ -> ()
      let solveLive (reads: bool array array) (writes: bool array array) =
        let liveIn  = Array.init n (fun _ -> Array.create 8 false)
        let liveOut = Array.init n (fun _ -> Array.create 8 false)
        let mutable changed = true
        while changed do
          changed <- false
          for i = n - 1 downto 0 do
            for r in 1 .. 5 do
              let mutable lo = false
              for s in succ[i] do
                if liveIn[s][r] then lo <- true
              let li = reads[i][r] || (lo && not (writes[i][r]))
              if lo <> liveOut[i][r] || li <> liveIn[i][r] then
                liveOut[i][r] <- lo
                liveIn[i][r] <- li
                changed <- true
        liveIn, liveOut
      let rLiveIn, rLiveOut = solveLive rReads rWrites
      let nLiveIn, nLiveOut = solveLive nReads nWrites
      let mNonLinear = Array.init n (fun _ -> Array.create 8 false)
      let mutable mChanged = true
      while mChanged do
        mChanged <- false
        for i in 0 .. n - 1 do
          for r in 1 .. 5 do
            let isNonLinWrite =
              match its[i] with
              | Op(Par(None, [| Imm({ expr = Num v }, M r') |])) when r' = r -> v <> 0xffffffL
              | _ -> mTouch[i][r]
            if isNonLinWrite && not (mNonLinear[i][r]) then
              mNonLinear[i][r] <- true
              mChanged <- true
            if mNonLinear[i][r] then
              let kills =
                match its[i] with
                | Op(Par(None, [| Imm({ expr = Num 0xffffffL }, M r') |])) when r' = r -> true
                | _ -> false
              if not kills then
                for s in succ[i] do
                  if not (mNonLinear[s][r]) then
                    mNonLinear[s][r] <- true
                    mChanged <- true
      let subRLive = Dictionary<string, bool array>()
      let subNLive = Dictionary<string, bool array>()
      let subMNonLin = Dictionary<string, bool array>()
      for (subName, _, _) in subList do
        subRLive[subName] <- Array.create 8 false
        subNLive[subName] <- Array.create 8 false
        subMNonLin[subName] <- Array.create 8 false
      for i in 0 .. n - 1 do
        match its[i] with
        | Op(Branch(Bsr, { expr = Sym s })) when subRLive.ContainsKey s ->
          for r in 1 .. 5 do
            if rLiveIn[i][r] || rLiveOut[i][r] then subRLive[s][r] <- true
            if nLiveIn[i][r] || nLiveOut[i][r] then subNLive[s][r] <- true
            if mNonLinear[i][r] then subMNonLin[s][r] <- true
        | _ -> ()
      let mutable callProp = true
      while callProp do
        callProp <- false
        for (callerName, sIdx, eIdx) in subList do
          for k in sIdx .. eIdx do
            match its[k] with
            | Op(Branch(Bsr, { expr = Sym calleeName })) when subRLive.ContainsKey calleeName ->
              for r in 1 .. 5 do
                if subRLive[callerName][r] && not (subRLive[calleeName][r]) then
                  subRLive[calleeName][r] <- true; callProp <- true
                if subNLive[callerName][r] && not (subNLive[calleeName][r]) then
                  subNLive[calleeName][r] <- true; callProp <- true
                if subMNonLin[callerName][r] && not (subMNonLin[calleeName][r]) then
                  subMNonLin[calleeName][r] <- true; callProp <- true
            | _ -> ()
      let isFree (r: int) (needN: bool) (startIdx: int) (endIdx: int) =
        let mutable ok = true
        for k in startIdx .. endIdx do
          if rReads[k][r] || rWrites[k][r] || rSubTouch[k][r] || rLiveOut[k][r] || mNonLinear[k][r] then ok <- false
          if needN && (nReads[k][r] || nWrites[k][r] || nSubTouch[k][r] || nLiveOut[k][r]) then ok <- false
        if ok then
          match subBounds[startIdx] with
          | Some(subName, _, _) ->
            if subRLive[subName][r] || (needN && subNLive[subName][r]) || subMNonLin[subName][r] then
              ok <- false
          | None -> ()
        ok
      its, labels, isFree

    let partitionGroups (sp: Space) (allAccs: (int * int * Reg * bool) array) (isLoop: bool) (maxRegs: int) (isUnconditional: int -> bool) =
      let endOff (sub: (int * int * Reg * bool) array) =
        if isLoop then let _, k0, _, _ = sub[0] in k0
        else let _, lastK, _, _ = sub[sub.Length - 1] in lastK
      let valid (sub: (int * int * Reg * bool) array) =
        sub.Length >= 2
        && (sub |> Array.forall (fun (idx, _, _, _) -> isUnconditional idx))
        && (canWalkSeq sub (endOff sub)).IsSome
      let groupSaving (sub: (int * int * Reg * bool) array) =
        if sub.Length < 2 then 0
        else
          let _, k0, _, _ = sub[0]
          let allSameReadOnly = sub |> Array.forall (fun (_, k, _, isStore) -> k = k0 && not isStore)
          if isLoop && allSameReadOnly then 0
          else
            match canWalkSeq sub (endOff sub) with
            | Some(k0, needN, _) ->
              let setupCycles = (if k0 = 0 then 1 else 3) + (if needN.IsSome then 1 else 0)
              let packCount =
                sub
                |> Array.filter (fun (idx, _, reg, isStore) -> canPackWith idx sp reg isStore)
                |> Array.length
              if isLoop then
                max 0 (32 * (sub.Length + packCount) - setupCycles)
              else
                max 0 (sub.Length + packCount - setupCycles)
            | None -> 0
      let groups =
        if valid allAccs then [| allAccs |]
        else
          let ks = allAccs |> Array.map (fun (_, k, _, _) -> k) |> Array.distinct |> Array.sort
          let rawIntervals = ResizeArray<Set<int>>()
          let mutable curSet = Set.singleton ks[0]
          for i in 1 .. ks.Length - 1 do
            if ks[i] = ks[i - 1] + 1 then curSet <- curSet.Add ks[i]
            else rawIntervals.Add curSet; curSet <- Set.singleton ks[i]
          rawIntervals.Add curSet
          let mergeParts (parts: ResizeArray<Set<int>>) =
            let usedPart = Array.create parts.Count false
            let byOffsets = ResizeArray<(int * int * Reg * bool) array>()
            for a in 0 .. parts.Count - 1 do
              if not usedPart[a] then
                let mutable merged = parts[a]
                let mutable curSub = allAccs |> Array.filter (fun (_, k, _, _) -> merged.Contains k)
                let mutable curSave = if valid curSub then groupSaving curSub else 0
                for b in a + 1 .. parts.Count - 1 do
                  if not usedPart[b] then
                    let cand = Set.union merged parts[b]
                    let candSub = allAccs |> Array.filter (fun (_, k, _, _) -> cand.Contains k)
                    if valid candSub then
                      let bSub = allAccs |> Array.filter (fun (_, k, _, _) -> parts[b].Contains k)
                      let bSave = if valid bSub then groupSaving bSub else 0
                      let candSave = groupSaving candSub
                      if candSave >= curSave + bSave then
                        merged <- cand
                        curSub <- candSub
                        curSave <- candSave
                        usedPart[b] <- true
                if valid curSub then
                  usedPart[a] <- true
                  byOffsets.Add curSub
            byOffsets.ToArray()
          let parts1 = ResizeArray<Set<int>>()
          let topCap = max 1 maxRegs
          let mutable globalDp = Array.create (topCap + 1) (0, [])
          for iv in rawIntervals do
            let arr = Set.toArray iv
            let m = arr.Length
            let dp1 = Array.zeroCreate<int> (m + 1)
            let choice1 = Array.create m -1
            let dp2 = Array2D.create (m + 1) (topCap + 1) (0, [])
            for p = m - 1 downto 0 do
              let mutable bestScore1 = dp1[p + 1]
              let mutable bestQ1 = -1
              for k in 1 .. topCap do
                dp2[p, k] <- dp2[p + 1, k]
              for q in p .. m - 1 do
                let slice = Set.ofArray arr[p .. q]
                let sub = allAccs |> Array.filter (fun (_, o, _, _) -> slice.Contains o)
                if valid sub then
                  let sc0 = groupSaving sub
                  let sc1 = sc0 + dp1[q + 1]
                  if sc1 > bestScore1 then
                    bestScore1 <- sc1
                    bestQ1 <- q
                  for k in 1 .. topCap do
                    let scRest, slicesRest = dp2[q + 1, k - 1]
                    let curBest, _ = dp2[p, k]
                    if sc0 + scRest > curBest then
                      dp2[p, k] <- (sc0 + scRest, slice :: slicesRest)
              dp1[p] <- bestScore1
              choice1[p] <- bestQ1
            let mutable p = 0
            while p < m do
              if choice1[p] >= p then
                parts1.Add (Set.ofArray arr[p .. choice1[p]])
                p <- choice1[p] + 1
              else
                parts1.Add (Set.singleton arr[p])
                p <- p + 1
            let nextGlobal = Array.copy globalDp
            for kTot in 1 .. topCap do
              let mutable bestSc, bestLst = nextGlobal[kTot]
              for kIv in 1 .. kTot do
                let scIv, lstIv = dp2[0, kIv]
                if scIv > 0 then
                  let scPrev, lstPrev = globalDp[kTot - kIv]
                  if scPrev + scIv > bestSc then
                    bestSc <- scPrev + scIv
                    bestLst <- lstPrev @ lstIv
              nextGlobal[kTot] <- (bestSc, bestLst)
            globalDp <- nextGlobal
          let chosenSlices = snd globalDp[topCap]
          let covered = chosenSlices |> List.fold Set.union Set.empty
          let parts2 = ResizeArray<Set<int>>()
          for s in chosenSlices do parts2.Add s
          for iv in rawIntervals do
            for o in iv do
              if not (covered.Contains o) then parts2.Add (Set.singleton o)
          let offArr1 = mergeParts parts1
          let offArr2 = mergeParts parts2
          let ordParts = ResizeArray<(int * int * Reg * bool) array>()
          let mutable p = 0
          while p < allAccs.Length do
            let mutable bestEnd = -1
            for q in p + 1 .. allAccs.Length - 1 do
              if valid allAccs[p .. q] then bestEnd <- q
            if bestEnd > p then
              ordParts.Add allAccs[p .. bestEnd]
              p <- bestEnd + 1
            else
              p <- p + 1
          let usedOrd = Array.create ordParts.Count false
          let byOrder = ResizeArray<(int * int * Reg * bool) array>()
          for a in 0 .. ordParts.Count - 1 do
            if not usedOrd[a] then
              usedOrd[a] <- true
              let mutable curSub = ordParts[a]
              let mutable curSave = groupSaving curSub
              for b in a + 1 .. ordParts.Count - 1 do
                if not usedOrd[b] then
                  let candSub = Array.append curSub ordParts[b]
                  if valid candSub then
                    let bSave = groupSaving ordParts[b]
                    let candSave = groupSaving candSub
                    if candSave >= curSave + bSave then
                      curSub <- candSub
                      curSave <- candSave
                      usedOrd[b] <- true
              byOrder.Add curSub
          let scoreTop (arr: (int * int * Reg * bool) array array) =
            arr |> Array.map groupSaving |> Array.sortDescending |> Array.truncate topCap |> Array.sum
          let offArr = if scoreTop offArr2 > scoreTop offArr1 then offArr2 else offArr1
          let ordArr = byOrder.ToArray()
          let offScore = scoreTop offArr
          let ordScore = scoreTop ordArr
          if ordScore > offScore then ordArr else offArr
      groups |> Array.map (fun sub -> sub, groupSaving sub) |> Array.sortByDescending snd

    // Phase 1: inner-most DO loops in RENDER.
    let its1, labels1, isFree1 = computeRegInfo ()
    let inWalkedLoop = Array.create n false
    for loopIdx in 0 .. n - 1 do
      match its1[loopIdx] with
      | Op(Loop(_, _, { expr = Sym endLabel })) when renderIn[loopIdx] ->
        match labels1.TryFind endLabel with
        | Some endIdx when endIdx > loopIdx ->
          let mutable innerOk = true
          let branchSpans = ResizeArray<int * int>()
          for k in loopIdx + 1 .. endIdx - 1 do
            match its1[k] with
            | Op(Loop _ | Forever _ | Enddo | Rts | Rti | Jump _) -> innerOk <- false
            | Op(Branch(Bsr, _)) -> innerOk <- false
            | Op(Branch(_, { expr = Sym t }))
            | Op(BitBranch(_, _, _, { expr = Sym t })) ->
              match labels1.TryFind t with
              | Some tIdx when tIdx > k && tIdx <= endIdx -> branchSpans.Add(k, tIdx)
              | _ -> innerOk <- false
            | Op(Branch _ | BitBranch _) -> innerOk <- false
            | _ -> ()
          if innerOk then
            for i in 0 .. n - 1 do
              if i < loopIdx || i > endIdx then
                let checkTarget t =
                  match labels1.TryFind t with
                  | Some tIdx when tIdx > loopIdx && tIdx < endIdx -> innerOk <- false
                  | _ -> ()
                match its1[i] with
                | Op(Branch(_, { expr = Sym t }))
                | Op(Jump(_, { expr = Sym t }))
                | Op(BitBranch(_, _, _, { expr = Sym t })) -> checkTarget t
                | _ -> ()
          if innerOk then
            let mutable p0 = loopIdx
            if p0 > 0 && its1[p0 - 1] = machinedrum.Abi.relocateMarker then p0 <- p0 - 1
            if p0 > 0 && (match its1[p0 - 1] with Label l -> l.EndsWith "_DO" | _ -> false) then p0 <- p0 - 1
            for k in p0 .. endIdx do inWalkedLoop[k] <- true
            let isUnconditional idx =
              branchSpans |> Seq.forall (fun (bIdx, tIdx) -> not (idx > bIdx && idx < tIdx))
            let maxLoopRegs =
              [ 1 .. 5 ]
              |> List.filter (fun r -> isFree1 r false p0 endIdx)
              |> List.length
            let allGroups =
              [| for sp in [ Space.Y; Space.X ] do
                   let allAccs =
                     [| for idx in loopIdx + 1 .. endIdx - 1 do
                          match dispOf (fst cur[idx]) with
                          | Some(sp', k, reg, isStore) when sp' = sp -> yield (idx, k, reg, isStore)
                          | _ -> () |]
                   if allAccs.Length >= 2 && maxLoopRegs > 0 then
                     for sub, saving in partitionGroups sp allAccs true maxLoopRegs isUnconditional do
                       if saving > 0 then yield (sp, sub, saving) |]
              |> Array.sortByDescending (fun (_, _, saving) -> saving)
            let usedRegs = HashSet<int>()
            for sp, sub, _ in allGroups do
              let _, k0, _, _ = sub[0]
              let prefRegs = if sp = Space.Y then [ 4; 5; 3; 2; 1 ] else [ 1; 2; 3; 4; 5 ]
              match canWalkSeq sub k0 with
              | Some(k0, needN, deltas) ->
                let regOpt =
                  prefRegs
                  |> List.tryFind (fun r ->
                    not (usedRegs.Contains r) && isFree1 r needN.IsSome p0 endIdx)
                match regOpt with
                | Some r ->
                  usedRegs.Add r |> ignore
                  let _, ln = cur[loopIdx]
                  let setupR =
                    if k0 = 0 then Op(Par(None, [| Copy(R 6, R r) |])), ln
                    else Op(Lua(Disp(6, Operand.num k0), R r)), ln
                  insertions.Add(p0, setupR)
                  match needN with
                  | Some d -> insertions.Add(p0, (Op(Par(None, [| Imm(Operand.num d, N r) |])), ln))
                  | None -> ()
                  for a in 0 .. sub.Length - 1 do
                    let idx, _, _, _ = sub[a]
                    rewriteAt idx (stepEa r deltas[a])
                | None -> ()
              | None -> ()
        | _ -> ()
      | _ -> ()

    // Phase 2: straight-line basic blocks in RENDER outside walked DO loops.
    let its2, _, isFree2 = computeRegInfo ()
    let targetLabels = HashSet<string>([ "INIT"; "TRIG"; "RENDER" ])
    for i in 0 .. n - 1 do
      match its2[i] with
      | Label l when l.StartsWith "SUB_" || l.EndsWith "_DO" -> targetLabels.Add l |> ignore
      | Op(Loop(_, _, { expr = Sym t }))
      | Op(Branch(_, { expr = Sym t }))
      | Op(Jump(_, { expr = Sym t }))
      | Op(BitBranch(_, _, _, { expr = Sym t })) -> targetLabels.Add t |> ignore
      | _ -> ()
    let isBlockBoundary i =
      match its2[i] with
      | Label l -> targetLabels.Contains l
      | Op(Loop _ | Forever _ | Enddo | Branch _ | Jump _ | BitBranch _ | Rts | Rti | Rep _) -> true
      | _ -> false
    let mutable bStart = 0
    while bStart < n do
      if not renderIn[bStart] || inWalkedLoop[bStart] || isBlockBoundary bStart then
        bStart <- bStart + 1
      else
        let mutable bEnd = bStart
        while bEnd + 1 < n && renderIn[bEnd + 1] && not inWalkedLoop[bEnd + 1] && not (isBlockBoundary (bEnd + 1)) do
          bEnd <- bEnd + 1
        let allGroups =
          [| for sp in [ Space.Y; Space.X ] do
               let allAccs =
                 [| for idx in bStart .. bEnd do
                      match dispOf (fst cur[idx]) with
                      | Some(sp', k, reg, isStore) when sp' = sp -> yield (idx, k, reg, isStore)
                      | _ -> () |]
               if allAccs.Length >= 2 then
                 for sub, saving in partitionGroups sp allAccs false 5 (fun _ -> true) do
                   if saving > 0 then yield (sp, sub, saving) |]
          |> Array.sortByDescending (fun (_, _, saving) -> saving)
        let usedRegs = HashSet<int>()
        for sp, sub, _ in allGroups do
          if sub.Length >= 2 then
            let firstIdx, _, _, _ = sub[0]
            let lastIdx, lastK, _, _ = sub[sub.Length - 1]
            let prefRegs = if sp = Space.Y then [ 4; 5; 3; 2; 1 ] else [ 1; 2; 3; 4; 5 ]
            match canWalkSeq sub lastK with
            | Some(k0, needN, deltas) ->
              let setupCycles = (if k0 = 0 then 1 else 3) + (if needN.IsSome then 1 else 0)
              let packCount =
                sub
                |> Array.filter (fun (idx, _, reg, isStore) -> canPackWith idx sp reg isStore)
                |> Array.length
              if sub.Length + packCount > setupCycles then
                let regOpt =
                  prefRegs
                  |> List.tryFind (fun r ->
                    not (usedRegs.Contains r) && isFree2 r needN.IsSome firstIdx lastIdx)
                match regOpt with
                | Some r ->
                  usedRegs.Add r |> ignore
                  let _, ln = cur[firstIdx]
                  let setupR =
                    if k0 = 0 then Op(Par(None, [| Copy(R 6, R r) |])), ln
                    else Op(Lua(Disp(6, Operand.num k0), R r)), ln
                  insertions.Add(firstIdx, setupR)
                  match needN with
                  | Some d -> insertions.Add(firstIdx, (Op(Par(None, [| Imm(Operand.num d, N r) |])), ln))
                  | None -> ()
                  for a in 0 .. sub.Length - 1 do
                    let idx, _, _, _ = sub[a]
                    rewriteAt idx (stepEa r deltas[a])
                | None -> ()
            | None -> ()
        bStart <- bEnd + 1

    let walked =
      if insertions.Count = 0 then cur
      else
        let byIdx = Dictionary<int, ResizeArray<Item * int>>()
        for idx, item in insertions do
          match byIdx.TryGetValue idx with
          | true, list -> list.Add item
          | false, _ ->
            let list = ResizeArray<Item * int>()
            list.Add item
            byIdx[idx] <- list
        let out = ResizeArray<Item * int>(n + insertions.Count)
        for i in 0 .. n - 1 do
          match byIdx.TryGetValue i with
          | true, list -> out.AddRange list
          | false, _ -> ()
          out.Add cur[i]
        out.ToArray()

    // Fold trailing `move (rN)+` / `move (rN)-` at the end of a DO loop into the last `Ind rN`
    // access in the loop body when nothing between them touches rN.
    let foldCur = Array.copy walked
    let dropped = HashSet<int>()
    let fn = foldCur.Length
    let flabels = Dictionary<string, int>()
    for i in 0 .. fn - 1 do
      match fst foldCur[i] with
      | Label l -> flabels[l] <- i
      | _ -> ()
    let touchesReg (r: int) (instr: Instr) =
      match readsOf instr, writesOf instr with
      | Some rs, Some ws ->
        let check (x: Reg) = x = R r || x = N r || x = M r
        Array.exists check rs || Array.exists check ws
      | _ -> true
    for loopIdx in 0 .. fn - 1 do
      match fst foldCur[loopIdx] with
      | Op(Loop(_, _, { expr = Sym endLabel })) ->
        match flabels.TryGetValue endLabel with
        | true, endIdx when endIdx > loopIdx ->
          let mutable innerOk = true
          let branchSpans = ResizeArray<int * int>()
          for k in loopIdx + 1 .. endIdx - 1 do
            match fst foldCur[k] with
            | Op(Loop _ | Forever _ | Enddo | Rts | Rti | Jump _) -> innerOk <- false
            | Op(Branch(Bsr, _)) -> innerOk <- false
            | Op(Branch(_, { expr = Sym t }))
            | Op(BitBranch(_, _, _, { expr = Sym t })) ->
              match flabels.TryGetValue t with
              | true, tIdx when tIdx > k && tIdx <= endIdx -> branchSpans.Add(k, tIdx)
              | _ -> innerOk <- false
            | Op(Branch _ | BitBranch _) -> innerOk <- false
            | _ -> ()
          if innerOk then
            let isUncond idx =
              branchSpans |> Seq.forall (fun (bIdx, tIdx) -> not (idx > bIdx && idx < tIdx))
            for uIdx in endIdx - 1 .. -1 .. loopIdx + 1 do
              if not (dropped.Contains uIdx) && isUncond uIdx then
                match fst foldCur[uIdx] with
                | Op(Par(None, [| Update((PostInc r | PostDec r) as stepEa) |])) when r >= 1 && r <= 5 ->
                  let afterClean =
                    [ uIdx + 1 .. endIdx - 1 ]
                    |> List.forall (fun k ->
                      dropped.Contains k
                      || (match fst foldCur[k] with
                          | Comment _ | Label _ -> true
                          | Op ins -> not (touchesReg r ins)
                          | _ -> false))
                  if afterClean then
                    let prevOpt =
                      [ uIdx - 1 .. -1 .. loopIdx + 1 ]
                      |> List.tryFind (fun k ->
                        not (dropped.Contains k)
                        && (match fst foldCur[k] with
                            | Op ins -> touchesReg r ins
                            | _ -> false))
                    match prevOpt with
                    | Some pIdx when isUncond pIdx ->
                      match fst foldCur[pIdx] with
                      | Op(Par(alu, moves)) ->
                        let aluTouches =
                          match alu with
                          | Some a ->
                            let check (x: Reg) = x = R r || x = N r || x = M r
                            Array.exists check (Lower.aluReads a) || Array.exists check (Lower.aluWrites a)
                          | None -> false
                        let moveTouches =
                          moves
                          |> Array.mapi (fun mi m ->
                            let check (x: Reg) = x = R r || x = N r || x = M r
                            mi, Array.exists check (Lower.moveReads m) || Array.exists check (Lower.moveWrites m))
                          |> Array.filter snd
                        if not aluTouches && moveTouches.Length = 1 then
                          let mi, _ = moveTouches[0]
                          let rewrittenMove =
                            match moves[mi] with
                            | Load(sp, Ind r', dst) when r' = r && (sp = Space.X || sp = Space.Y) ->
                              Some(Load(sp, stepEa, dst))
                            | Store(src, sp, Ind r') when r' = r && (sp = Space.X || sp = Space.Y) ->
                              Some(Store(src, sp, stepEa))
                            | _ -> None
                          match rewrittenMove with
                          | Some m' ->
                            let moves' = Array.copy moves
                            moves'[mi] <- m'
                            let lastAfterDrop =
                              [ endIdx - 1 .. -1 .. loopIdx + 1 ]
                              |> List.tryFind (fun k ->
                                k <> uIdx && not (dropped.Contains k)
                                && (match fst foldCur[k] with Comment _ -> false | _ -> true))
                            let endValid =
                              match lastAfterDrop with
                              | Some k ->
                                match (if k = pIdx then Op(Par(alu, moves')) else fst foldCur[k]) with
                                | Op(Branch _ | BitBranch _ | Jump _) | Label _ -> false
                                | Op _ -> true
                                | _ -> false
                              | None -> false
                            if endValid && Lower.encodes (Par(alu, moves')) then
                              foldCur[pIdx] <- (Op(Par(alu, moves')), Ir.Origin.merge (snd foldCur[pIdx]) (snd foldCur[uIdx]))
                              dropped.Add uIdx |> ignore
                          | None -> ()
                      | _ -> ()
                    | _ -> ()
                | _ -> ()
        | _ -> ()
      | _ -> ()
    if dropped.Count = 0 then walked
    else foldCur |> Array.mapi (fun i x -> i, x) |> Array.choose (fun (i, x) -> if dropped.Contains i then None else Some x)

let walk (items: (Item * int) array) = walkWith 64 items

/// The passes, each one switchable (for measuring what each does), and the guard that keeps a
/// pass's rewrite only when it is no worse (`monotone`). fixLoops always runs: it is not an
/// optimisation, it keeps DO loop ends legal.
type Passes =
  { forward     : bool   // value numbering: loads a register already holds dropped, others copied
    accCopies   : bool   // the same, with copies between accumulators
    copyForward : bool   // a copy's source read in place of the copy
    deadTemps   : bool   // stores to X temporaries nothing reads dropped
    hoist       : bool   // loads a DO loop does not change done once, before the loop
    walk        : bool   // workset sub-blocks walked by a pointer register instead of r6+k
    pack        : bool   // moves merged into parallel instructions
    rotate      : bool   // one-stage software pipelining of DO loops
    promote     : bool   // accumulator temporaries kept in a free accumulator
    assign      : bool   // temporaries' webs in free registers
    joins       : bool   // raw accumulator equality retained through CFG joins
    monotone    : bool } // each pass kept only when the static cost is no worse

  static member all =
    { forward = true; accCopies = true; copyForward = true; deadTemps = true; hoist = true; walk = true
      pack = true; rotate = true; promote = true; assign = true; joins = true; monotone = true }

  static member none =
    { forward = false; accCopies = false; copyForward = false; deadTemps = false; hoist = false; walk = false
      pack = false; rotate = false; promote = false; assign = false; joins = false; monotone = false }

/// The switchable passes by name, with how to turn each off.
let passes : (string * (Passes -> Passes)) list =
  [ "forward",     (fun p -> { p with forward = false })
    "accCopies",   (fun p -> { p with accCopies = false })
    "copyForward", (fun p -> { p with copyForward = false })
    "deadTemps",   (fun p -> { p with deadTemps = false })
    "hoist",       (fun p -> { p with hoist = false })
    "walk",        (fun p -> { p with walk = false })
    "pack",        (fun p -> { p with pack = false })
    "rotate",      (fun p -> { p with rotate = false })
    "promote",     (fun p -> { p with promote = false })
    "assign",      (fun p -> { p with assign = false })
    "joins",       (fun p -> { p with joins = false }) ]

type Level =
  | Direct            // the compiler's translation as it is
  | Peephole          // every pass, guarded
  | Chosen of Passes  // the passes chosen

/// The static cost the guard compares: instructions weighted by the trip counts of the DO loops
/// around them (a loop with a count only known when running weighs 32, one block), then the
/// instruction count. Branches inside a loop count as taken on every pass: an upper bound.
/// Indexed `Disp` moves take 2 cycles and `lua` takes 3 cycles on the DSP56300; single-word
/// register/indirect instructions take 1 cycle.
let staticCost (items: (Item * int) array) =
  let mutable weighted = 0L
  let mutable count = 0L
  let mutable around : (string * int64) list = []
  let trips () = around |> List.fold (fun t (_, n) -> t * n) 1L
  for it, _ in items do
    match it with
    | Label l ->
      while (match around with (e, _) :: _ when e = l -> true | _ -> false) do around <- List.tail around
    | Op instr ->
      let cycles =
        match instr with
        | Lua _ -> 3L
        | Par(_, moves) when moves |> Array.exists (function Load(_, Disp _, _) | Store(_, _, Disp _) -> true | _ -> false) -> 2L
        | _ -> 1L
      weighted <- weighted + cycles * trips ()
      count <- count + 1L
      match instr with
      | Loop(_, count', { expr = Sym e }) ->
        let n =
          match count' with
          | Count { expr = Num n } when n > 0L -> n
          | _ -> 32L
        around <- (e, n) :: around
      | _ -> ()
    | _ -> ()
  weighted, count

/// Hand-written code ([<HandWritten>]) is not the passes' to change. Each stretch between its
/// `hand:` comments is taken out before the passes, a `bsr` to a stub (an rts after the program)
/// standing in for it, and put back after them: the passes take that call as they take any, as a
/// routine that may read and write any register.
let handStub (k: int) = $"SUB_K_HAND_{k}"

let freezeHand (items: (Item * int) array) =
  let out = ResizeArray<Item * int>()
  let regions = ResizeArray<(Item * int) array>()
  let mutable i = 0
  while i < items.Length do
    match fst items[i] with
    | Comment t when t.StartsWith "hand: " && t <> "hand: end" ->
      let j = [ i .. items.Length - 1 ] |> List.find (fun j -> fst items[j] = Comment "hand: end")
      out.Add(bsr (handStub regions.Count), snd items[i])
      regions.Add items[i .. j]
      i <- j + 1
    | _ ->
      out.Add items[i]
      i <- i + 1
  for k in 0 .. regions.Count - 1 do
    let orig = if regions[k].Length > 0 && snd (regions[k][0]) > 0 then snd (regions[k][0]) else Ir.Origin.synthetic
    out.Add(Label(handStub k), orig)
    out.Add(Op Rts, orig)
  out.ToArray(), regions.ToArray()

/// The hand-written stretches back where their stubs' calls are, each exactly once, the stubs gone.
let thawHand (regions: (Item * int) array array) (items: (Item * int) array) =
  if regions.Length = 0 then items
  else
    let stubs = Set.ofList [ for k in 0 .. regions.Length - 1 -> handStub k ]
    let seen = HashSet<string>()
    let out = ResizeArray<Item * int>()
    let mutable i = 0
    while i < items.Length do
      match fst items[i] with
      | Label l when stubs.Contains l -> i <- i + 2
      | Op(Branch(Bsr, { expr = Sym s })) when stubs.Contains s ->
        if not (seen.Add s) then Kernel.fail $"an optimizer pass copied hand-written code ({s})"
        out.AddRange regions[int (s.Substring "SUB_K_HAND_".Length)]
        i <- i + 1
      | _ ->
        out.Add items[i]
        i <- i + 1
    if seen.Count <> regions.Length then Kernel.fail "an optimizer pass dropped hand-written code"
    out.ToArray()

/// Verifies that every emitted instruction (`Op _`) carries a positive provenance origin id; raises
/// `KernelError` if a pass dropped one.
let checkOrigins (passName: string) (items: (Item * int) array) =
  for i in 0 .. items.Length - 1 do
    match items[i] with
    | Op _, orig when orig <= 0 ->
      Kernel.fail $"optimizer pass '{passName}' dropped provenance origin at item {i} ({Print.item (fst items[i])})"
    | _ -> ()

/// Runs a pass and verifies that no emitted instruction lost its provenance origin.
let runPassChecked (passName: string) (pass: (Item * int) array -> (Item * int) array) (items: (Item * int) array) =
  checkOrigins $"{passName} (input)" items
  let next = pass items
  checkOrigins passName next
  next

/// The passes of `p`; the compiler's temporaries are X words `first` up to `temps` and Y words
/// `yFirst` up to `yTemps`.
let runWithYSink (sink: Ir.Sink) (p: Passes) (first: int) (temps: int) (yFirst: int) (yTemps: int) (items: Item array) (lines: int array) =
  let zipped = Array.zip items lines
  let enforceOrigins = zipped |> Array.forall (fun (it, o) -> match it with Op _ -> o > 0 | _ -> true)
  let frozen, regions = freezeHand zipped
  let mutable cur = frozen
  let yf, yt = if first < 64 then yFirst, yTemps else 0, 0
  let yLim = if first >= 64 && yTemps > yFirst then yFirst else 64
  // a pass's rewrite, kept when it is no worse (when the passes are guarded)
  let step (name: string) (on: bool) (pass: (Item * int) array -> (Item * int) array) =
    if on then
      let next = pass cur
      if enforceOrigins then checkOrigins name next
      if not p.monotone || staticCost next <= staticCost cur then
        if next.Length <> cur.Length || next <> cur then
          let orig = next |> Array.tryPick (fun (it, o) -> match it with Op _ when o > 0 -> Some o | _ -> None) |> Option.defaultValue Ir.Origin.synthetic
          Ir.Sink.record sink orig Ir.AsmLayer $"opt.{name}" $"{cur.Length}->{next.Length}"
        cur <- next
  for _ in 1 .. 3 do
    step "forward" p.forward forward
    step "copyForward" p.copyForward copyForward
    step "deadTemps" p.deadTemps (deadTempsFromY first temps yf yt)
  for _ in 1 .. 2 do
    step "accCopies" p.accCopies (forwardWith true true)
    step "copyForward" p.copyForward copyForward
    step "deadTemps" p.deadTemps (deadTempsFromY first temps yf yt)
  step "joins" p.joins (joinAccumulatorsY first temps yf yt)
  if p.joins then step "deadTemps" p.deadTemps (deadTempsFromY first temps yf yt)
  step "promote" p.promote (promoteY first temps yf yt)
  step "assign" p.assign (assignY first temps yf yt)
  for _ in 1 .. 2 do
    step "forward" p.forward (forwardWith p.accCopies false)
    step "copyForward" p.copyForward copyForward
    step "deadTemps" p.deadTemps (deadTempsFromY first temps yf yt)
  step "joins" p.joins (joinAccumulatorsY first temps yf yt)
  if p.joins then
    step "deadTemps" p.deadTemps (deadTempsFromY first temps yf yt)
    step "forward" p.forward (forwardWith p.accCopies false)
    step "copyForward" p.copyForward (copyForwardWith true)
    step "deadTemps" p.deadTemps (deadTempsFromY first temps yf yt)
  step "hoist" p.hoist (hoistFromY first temps yf yt)
  step "copyForward" p.copyForward (copyForwardWith true)
  step "walk" p.walk (walkWith yLim)
  step "pack" p.pack pack
  step "rotate" p.rotate (rotate >> (if p.pack then pack else id))
  cur <- thawHand regions cur
  cur <- fixLoops cur
  if enforceOrigins then checkOrigins "fixLoops" cur
  Array.map fst cur, Array.map snd cur

let runWithY (p: Passes) (first: int) (temps: int) (yFirst: int) (yTemps: int) (items: Item array) (lines: int array) =
  runWithYSink Ir.Sink.none p first temps yFirst yTemps items lines

let runWith (p: Passes) (first: int) (temps: int) (items: Item array) (lines: int array) =
  runWithY p first temps 0 0 items lines

/// The passes of `level`; the compiler's temporaries are X words `first` up to `temps` and Y
/// words `yFirst` up to `yTemps`.
let runFromYSink (sink: Ir.Sink) (level: Level) (first: int) (temps: int) (yFirst: int) (yTemps: int) (items: Item array) (lines: int array) =
  match level with
  | Direct -> items, lines
  | Peephole -> runWithYSink sink Passes.all first temps yFirst yTemps items lines
  | Chosen p -> runWithYSink sink p first temps yFirst yTemps items lines

let runFromY (level: Level) (first: int) (temps: int) (yFirst: int) (yTemps: int) (items: Item array) (lines: int array) =
  runFromYSink Ir.Sink.none level first temps yFirst yTemps items lines

/// The passes of `level`; the compiler's temporaries are X words `first` up to `temps`.
let runFrom (level: Level) (first: int) (temps: int) (items: Item array) (lines: int array) =
  runFromY level first temps 0 0 items lines

/// The passes of `level`, for a kernel without [<XMemory>] state: temporaries are X words below `temps`.
let run (level: Level) (temps: int) (items: Item array) (lines: int array) = runFrom level 0 temps items lines

/// Which helpers the compiler makes subroutines by its own choice, besides the [<Subroutine>]
/// ones ([<Inline>] helpers never):
/// - Off: none;
/// - Conservative: a helper called from more than one place, all of them in INIT or TRIG, when
///   that saves at least `minSaving` program words (RENDER's code stays as it was);
/// - Size: also helpers RENDER calls, outside its loops first, then inside them; any saving.
/// Conservative is the default; a kernel too large for its machine tries Size before it is refused.
type Subroutines =
  | Off
  | Conservative
  | Size

/// The words a chosen subroutine must save under Conservative.
let minSaving = 4

/// The policy `compile` uses: FSDSP56K_SUBROUTINES=off, conservative (the default) or size.
let subroutinesPolicy () =
  match System.Environment.GetEnvironmentVariable "FSDSP56K_SUBROUTINES" with
  | null | "" | "conservative" -> Conservative
  | "off" -> Off
  | "size" -> Size
  | other -> failwith $"FSDSP56K_SUBROUTINES: {other} is not off, conservative or size"

/// The helpers `policy` may make subroutines, in the order they are tried: (tier, calls, key)
/// with tier 0 called only from INIT and TRIG, 1 also from RENDER outside its loops, 2 inside
/// them; the most calls first, then by name (deterministic).
let subroutineCandidates (policy: Subroutines) (ik: Ir.IrKernel) =
  // a subroutine gives a Frac, Acc or Long, or nothing (unit, a method's void)
  let returns (t: Ir.IrTy) = t = Ir.TyFrac || t = Ir.TyAcc || t = Ir.TyLong || t = Ir.TyUnit || t = Ir.TyOther "System.Void"
  Compile.helperUses ik
  |> List.groupBy (fun u -> u.helper.Key)
  |> List.choose (fun (key, uses) ->
    let h = uses.Head.helper
    if policy = Off || uses.Length < 2 || h.Marks.Contains "Subroutine" || h.Marks.Contains "Inline" || h.Marks.Contains "Hand" || not (returns h.ReturnType) then None
    else
      let entryOnly = uses |> List.forall (fun u -> u.entry = "INIT" || u.entry = "TRIG")
      let coldRender = uses |> List.forall (fun u -> not (u.entry = "RENDER" && u.inLoop))
      match policy with
      | Conservative when entryOnly -> Some(0, -uses.Length, key)
      | Size when entryOnly -> Some(0, -uses.Length, key)
      | Size when coldRender -> Some(1, -uses.Length, key)
      | Size -> Some(2, -uses.Length, key)
      | _ -> None)
  |> List.sort

/// Compile an `IrKernel` with a diagnostics sink, run the passes of `level`, choose subroutines (`policy`),
/// and keep the program within `limit` words.
let compileIrChoosingWithSink (sink: Ir.Sink) (handOff: Set<string>) (policy: Subroutines) (limit: int option) (level: Level) (target: Kernel.Target) (ik: Ir.IrKernel) =
  let attempt (chosen: Set<string>) (unswitchMax: int option) =
    let attemptSink = if Ir.Sink.isActive sink then Ir.Sink.create () else Ir.Sink.none
    Compile.generateIrWithSink attemptSink handOff chosen unswitchMax target ik
    |> Result.map (fun (c, costs) ->
      let items, origins = runFromYSink attemptSink level c.layout.xWorkset c.xWorkset c.layout.yWorkset c.yWorkset c.items c.origins
      let lines =
        origins |> Array.map (fun o ->
          match Ir.Origin.primary o with
          | Some r when c.source |> Option.forall ((=) r.file) && r.file <> "<synthetic>" -> r.line
          | _ -> 0)
      let facts = if Ir.Sink.isActive attemptSink then Ir.Sink.toList attemptSink else c.facts
      { c with items = items; origins = origins; lines = lines; facts = facts }, costs)
  let fits (c: Compile.Compiled) =
    match limit, Compile.size c with
    | Some l, Ok s -> s.total <= l
    | _ -> true
  let words (c: Compile.Compiled) = match Compile.size c with Ok s -> s.total | Error _ -> System.Int32.MaxValue
  // each candidate tried on top of those kept, kept when it saves enough words; a candidate the
  // compiler refuses (an argument it cannot pass, the system stack) is left inlined
  let choose (p: Subroutines) (unswitchMax: int option) (chosen: Set<string>, best: Compile.Compiled) =
    subroutineCandidates p ik
    |> List.fold (fun (chosen: Set<string>, best: Compile.Compiled) (_, _, key) ->
      if chosen.Contains key then chosen, best
      else
        let need = if p = Conservative then minSaving else 1
        match (try attempt (chosen.Add key) unswitchMax with e -> Error e.Message) with
        | Ok(c, _) when words c <= words best - need -> chosen.Add key, c
        | _ -> chosen, best) (chosen, best)
  let finish (res: Result<Compile.Compiled, string>) =
    res |> Result.map (fun c ->
      Ir.Sink.recordAll sink c.facts
      c)
  match attempt Set.empty None with
  | Error e -> Error e
  | Ok(c0, costs) ->
    let chosen, c = if policy = Off then Set.empty, c0 else choose policy None (Set.empty, c0)
    if fits c then finish (Ok c)
    else
      // each distinct cost, largest first: loops costing more than the next one down kept whole
      let rec smaller (thresholds: int list) (best: Compile.Compiled) =
        match thresholds with
        | [] -> Ok best
        | m :: rest ->
          match attempt chosen (Some m) with
          | Ok(c, _) when fits c -> Ok c
          | Ok(c, _) -> smaller rest c
          | Error e -> Error e
      let shrunk = if costs.IsEmpty then Ok c else smaller ((costs |> List.distinct |> List.sortDescending |> List.tail) @ [ -1 ]) c
      match shrunk with
      | Ok c' when not (fits c') && policy <> Off ->
        // size pressure: helpers RENDER calls as subroutines too
        let unswitchMax = if costs.IsEmpty then None else Some -1
        finish (Ok(snd (choose Size unswitchMax (chosen, c'))))
      | r -> finish r

/// Compile, run the passes of `level`, choose subroutines (`policy`), and keep the program within
/// `limit` words: when it is larger, the loops whose unswitching added the most are kept whole
/// (tested on each pass), the largest first, until it fits or none is unswitched; then, unless the
/// policy is Off, helpers RENDER calls are made subroutines too. None: no limit.
let compileChoosingWith (handOff: Set<string>) (policy: Subroutines) (limit: int option) (level: Level) (target: Kernel.Target) (k: Kernel.Kernel) =
  compileIrChoosingWithSink Ir.Sink.none handOff policy limit level target (FromQuotation.kernel k)

/// `compileChoosingWith`, every hand routine used.
let compileChoosing (policy: Subroutines) (limit: int option) (level: Level) (target: Kernel.Target) (k: Kernel.Kernel) =
  compileChoosingWith Set.empty policy limit level target k

/// `compileChoosing` with the policy of FSDSP56K_SUBROUTINES.
let compileWithin (limit: int option) (level: Level) (target: Kernel.Target) (k: Kernel.Kernel) =
  compileChoosing (subroutinesPolicy ()) limit level target k

/// Compile, then run the passes of `level` over the items; refused when the program is larger
/// than the target's machine holds (Kernel.programLimit), after keeping loops whole if that helps.
let compile (level: Level) (target: Kernel.Target) (k: Kernel.Kernel) =
  compileWithin (Kernel.programLimit target |> Option.map fst) level target k
  |> Result.bind Compile.checkSize

/// `compile` with an explicit diagnostics sink.
let compileWithSink (sink: Ir.Sink) (level: Level) (target: Kernel.Target) (k: Kernel.Kernel) =
  compileIrChoosingWithSink sink Set.empty (subroutinesPolicy ()) (Kernel.programLimit target |> Option.map fst) level target (FromQuotation.kernel k)
  |> Result.bind Compile.checkSize

/// `compile`, the [<HandWritten>] helpers of `handOff` (by key) compiled from their F# reference.
let compileHandOff (handOff: Set<string>) (level: Level) (target: Kernel.Target) (k: Kernel.Kernel) =
  compileChoosingWith handOff (subroutinesPolicy ()) (Kernel.programLimit target |> Option.map fst) level target k
  |> Result.bind Compile.checkSize


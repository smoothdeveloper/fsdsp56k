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
  | MulSu(_, _, _, _, _, d) | Dmac(_, _, _, _, d) | MulI(_, _, _, _, d) | Div(_, d) | Inc d | Dec d -> Some [ Acc.reg d ]
  | IfCc(alu, _, _) -> Some(List.ofArray (Lower.aluWrites alu))
  | Tcc(_, alu, agu) -> Some([ for _, d in Option.toList alu -> Acc.reg d ] @ [ for _, r in Option.toList agu -> R r ])
  | Norm(r, d) -> Some [ Acc.reg d; R r ]
  | Clb(_, d) | Normf(_, d) -> Some [ Acc.reg d ]
  | Nop -> Some []
  | _ -> None

/// Value numbering over straight-line stretches: moves that change nothing are dropped, loads
/// of a value a data register holds become register moves.
/// `accCopies`: an accumulator loaded from a word a data register holds is copied from the
/// register instead (done last: earlier, the register's read would keep copyForward from
/// removing the register's own load).
let forwardWith (accCopies: bool) (items: (Item * int) array) =
  let f = Facts()
  let out = ResizeArray<Item * int>()
  let mutable i = 0
  // the instruction after an `mds:` marker has a word the loader patches (an import's address,
  // a relocated target): its immediate is not the value written, so it ends what is known
  let mutable patched = false
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
      | Comment text ->
        if text.StartsWith machinedrum.Abi.markerPrefix then patched <- true
        out.Add(it, line)
        i <- i + 1
      | Op _ when patched ->
        patched <- false
        f.Clear()
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
      | Op instr ->
        match aluWrites instr with
        | Some ws -> for w in ws do f.WriteReg w
        | None -> f.Clear()           // loops, branches, calls and anything else: nothing is known after
        out.Add(it, line)
        i <- i + 1
  out.ToArray()

let forward = forwardWith false

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

/// Whole-accumulator reloads whose three raw parts equal their temporary on every incoming
/// path. Value IDs need not agree between paths: a clamp can select a different value while
/// keeping A equal to its home on both. Unknown effects and entry paths contribute no facts.
let joinAccumulators (first: int) (temps: int) (items: (Item * int) array) =
  let its = Array.map fst items
  let n = its.Length
  let single i = match its[i] with Op(Par(None, [| m |])) -> Some m | _ -> None
  let reload i =
    if i + 2 >= n then None
    else
      match single i, single (i + 1), single (i + 2) with
      | Some(Load(Space.X, e1, ((A | B) as acc))), Some(Load(Space.X, e0, lo)), Some(Load(Space.X, e2, ext))
          when lo = (if acc = A then A0 else B0) && ext = (if acc = A then A2 else B2) ->
        match workset Space.X e1, workset Space.X e0, workset Space.X e2 with
        | Some(SMem(_, k)), Some(SMem(_, k0)), Some(SMem(_, k2))
            when k >= first && k + 2 < temps && k0 = k + 1 && k2 = k + 2 -> Some(k, acc)
        | _ -> None
      | _ -> None
  match successors its with
  | None -> items
  | Some succ ->
    let preds = Array.init n (fun _ -> ResizeArray<int>())
    for i in 0 .. n - 1 do for j in succ[i] do preds[j].Add i
    let candidates = [| for i in 0 .. n - 1 do match reload i with Some pair -> yield pair | _ -> () |] |> Array.distinct
    let dropped = HashSet<int>()
    for k, acc in candidates do
      let registers = if acc = A then [| A1; A0; A2 |] else [| B1; B0; B2 |]
      let regMask r =
        let touched = parts r
        registers |> Array.mapi (fun bit r -> if List.contains r touched then 1 <<< bit else 0) |> Array.fold (|||) 0
      let memoryMask space ea =
        match workset space ea with
        | Some(SMem(Space.X, word)) when word >= k && word < k + 3 -> 1 <<< (word - k)
        | Some _ -> 0
        | None when space = Space.X -> 7     // an indirect store can alias the temporary
        | _ -> 0
      let transfer state item =
        match item with
        | Label _ -> state
        | Comment text when not (text.StartsWith machinedrum.Abi.markerPrefix) -> state
        | Op(Branch((Bra | Bcc _), _)) | Op(Jump((Jmp | Jcc _), _))
        | Op(Loop _) | Op Enddo | Op Nop -> state
        | Op(Par(alu, moves)) ->
          let mutable changed = 0
          let mutable stored = 0
          let mutable baseChanged = false
          let write r =
            changed <- changed ||| regMask r
            if r = R 6 then baseChanged <- true
          alu |> Option.iter (fun a -> Lower.aluWrites a |> Array.iter write)
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
            let mutable result = state &&& (7 ^^^ (changed ||| stored))
            // Generate equality only for a single move without an ALU: a parallel move's
            // sources precede all writes, and overlapping destinations need more modelling.
            for move in (if alu.IsNone && moves.Length = 1 then moves else [||]) do
              match move with
              | Store(src, Space.X, ea) ->
                match workset Space.X ea with
                | Some(SMem(_, word)) when word >= k && word < k + 3 ->
                  let bit = 1 <<< (word - k)
                  // Parallel stores read the old register, before any ALU/register writes.
                  if src = registers[word - k] && changed &&& bit = 0 then result <- result ||| bit
                | _ -> ()
              | Load(Space.X, ea, dst) ->
                match workset Space.X ea with
                | Some(SMem(_, word)) when word >= k && word < k + 3 ->
                  let bit = 1 <<< (word - k)
                  if (dst = registers[word - k] || (word = k && dst = acc)) && stored &&& bit = 0 then
                    result <- result ||| bit
                | _ -> ()
              | _ -> ()
            result
        | Op instr ->
          match aluWrites instr with
          | Some writes when not (List.contains (R 6) writes) ->
            state &&& (7 ^^^ (writes |> List.map regMask |> List.fold (|||) 0))
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
      for i in 0 .. n - 1 do
        if incoming[i] = 7 && reload i = Some(k, acc) then
          for j in i .. i + 2 do dropped.Add j |> ignore
    items |> Array.mapi (fun i item -> i, item) |> Array.choose (fun (i, item) -> if dropped.Contains i then None else Some item)

/// Stores to the compiler's temporaries (X workset words from `first` up to `temps`; the words
/// below `first` are [<XMemory>] state) that no path reads before the word is written again.
let deadTempsFrom (first: int) (temps: int) (items: (Item * int) array) =
  let its = Array.map fst items
  let temp (space: Space) (ea: Ea) =
    match workset space ea with
    | Some(SMem(Space.X, k)) when k >= first && k < temps -> Some k
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

/// The dead temporaries of a kernel without [<XMemory>] state: X words below `temps`.
let deadTemps (temps: int) (items: (Item * int) array) = deadTempsFrom 0 temps items

/// Registers an instruction reads, as far as this pass needs (None: not known).
let readsOf (instr: Instr) =
  match instr with
  | Par(alu, moves) ->
    Some(Array.append (alu |> Option.map Lower.aluReads |> Option.defaultValue [||]) (Array.collect Lower.moveReads moves))
  | MulI(_, _, _, src, d) | MulImm(_, _, src, _, d) -> Some [| src; Acc.reg d |]
  | MulSu(_, _, _, s1, s2, d) | Dmac(_, _, s1, s2, d) -> Some [| s1; s2; Acc.reg d |]
  | Div(src, d) | Normf(src, d) -> Some [| src; Acc.reg d |]
  | Clb(src, _) -> Some [| Acc.reg src |]
  | AluImm(_, _, d) | ShiftImm(_, _, _, d) | Inc d | Dec d -> Some [| Acc.reg d |]
  | ShiftReg(_, r, s, _) -> Some [| r; Acc.reg s |]
  | Nop -> Some [||]
  | _ -> None

let writesOf (instr: Instr) =
  match instr with
  | Par(alu, moves) ->
    Some(Array.append (alu |> Option.map Lower.aluWrites |> Option.defaultValue [||]) (Array.collect Lower.moveWrites moves))
  | i -> aluWrites i |> Option.map List.toArray

/// Whether `r` is overwritten before anything reads it, from item `from` on.
let deadAfter (items: (Item * int) array) (from: int) (r: Reg) =
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
      match successors (Array.map fst items) with
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

/// `move s,x0` `move x0,d` with x0 dead afterwards: `move s,d`, when that encodes.
let copyForward (items: (Item * int) array) =
  let cur = Array.copy items
  let dropped = HashSet<int>()
  let its = Array.map fst cur
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
  for i in 0 .. cur.Length - 1 do
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
let hoistFrom (first: int) (temps: int) (items: (Item * int) array) =
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
                        || (space = Space.Y && w >= 1 && w < 9)
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

let hoist (items: (Item * int) array) = hoistFrom 0 0 items

/// Lower.pack with each item's source line kept (a merged instruction takes the first's), across
/// the comments quoting the F# (they follow the merged instruction).
let pack (items: (Item * int) array) =
  let mutable cur = items
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
        out.Add(m, (if snd cur[i] > 0 then snd cur[i] else snd cur[j]))
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
let promote (first: int) (temps: int) (items: (Item * int) array) =
  let its = Array.map fst items
  let n = its.Length
  let temp (space: Space) (ea: Ea) =
    match workset space ea with
    | Some(SMem(Space.X, k)) when k >= first && k < temps -> Some k
    | _ -> None
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
    // what an item does to accumulator t: (reads, writes it whole, touches it at all)
    let effect (t: Reg) i =
      match its[i] with
      | Op instr when control instr -> false, false, false
      | Op instr ->
        match readsOf instr, writesOf instr with
        | Some rs, Some ws ->
          let on (r: Reg) = Array.contains t (Lower.storage r)
          let whole =
            match instr with
            | Par(None, moves) -> moves |> Array.exists (function Load(_, _, r) | Copy(_, r) | Imm(_, r) -> r = t | _ -> false)
            | _ -> false
          Array.exists on rs, whole, Array.exists on rs || Array.exists on ws
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

/// The compiler's X temporaries in registers, web by web. A temporary word is reused for many
/// expressions; each definition with the uses it reaches (reaching definitions, per word) is a
/// web, given a register when one is neither touched nor live anywhere the web is live:
///
/// - a whole accumulator (a1, a0, a2 at k, k+1, k+2, three moves each way; reads of the high word
///   alone too) in A or B: `tfr a,b`, `tfr b,a`, `move b1,x0`;
/// - a word (`move x0,x:k` or `move a,x:k`, read by `move x:k,y0` or `move x:k,a`) in x0, x1,
///   y0 or y1: a register move does to the word what the memory move did (a limited store of an
///   accumulator, a sign-extended load into one).
///
/// Heavier webs (trip-weighted accesses) first; webs sharing a register have disjoint ranges. A
/// move between a web's register and itself goes. Calls, returns and anything not modelled use
/// every register, so a web across one keeps its memory.
let assign (first: int) (temps: int) (items: (Item * int) array) =
  let its = Array.map fst items
  let n = its.Length
  let temp (space: Space) (ea: Ea) =
    match workset space ea with
    | Some(SMem(Space.X, k)) when k >= first && k < temps -> Some k
    | _ -> None
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
      | Op(Par(None, [| Store(r, sp, ea) |])) when (temp sp ea).IsSome && (data r || r = A || r = B) ->
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
    // what an item does to register t: (reads, writes it whole, touches it)
    let effect (t: Reg) i =
      match its[i] with
      | Op instr when control instr -> false, false, false
      | Op instr ->
        match readsOf instr, writesOf instr with
        | Some rs, Some ws ->
          let on (r: Reg) = Lower.storage r |> Array.exists (fun x -> Array.contains x (Lower.storage t))
          let whole =
            match instr with
            | Par(None, moves) -> moves |> Array.exists (function Load(_, _, r) | Copy(_, r) | Imm(_, r) -> r = t | _ -> false)
            | _ -> false
          Array.exists on rs, whole, Array.exists on rs || Array.exists on ws
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
          // stores and reloads of a whole accumulator at k, reads of its high word into a data register
          kinds.Contains "tstore" && Set.isSubset kinds (set [ "tstore"; "tload"; "wload" ])
          && (let k = word[ms |> Array.find (fun i -> kind[i] = "tstore")]
              ms |> Array.forall (fun i -> word[i] = k && (kind[i] <> "wload" || data reg[i])))
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
      let isTriple = ms |> Array.exists (fun i -> kind[i] = "tstore")
      let candidates = if isTriple then [ B; A ] else [ Y1; X1; Y0; X0 ]
      let fits t =
        let tIn, tOut = liveOf t
        let clear = taken.TryGetValue t |> function | true, s -> range |> List.forall (fun i -> not (s.Contains i)) | _ -> true
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
          | "wload" when isTriple -> local[i] <- Some(Op(Par(None, [| Copy(hi t, reg[i]) |])))
          | "wload" -> local[i] <- (if reg[i] = t then None else Some(Op(Par(None, [| Copy(t, reg[i]) |]))))
          | "wstore" -> local[i] <- (if reg[i] = t then None else Some(Op(Par(None, [| Copy(reg[i], t) |]))))
          | _ -> ()
        let encodes = local.Values |> Seq.forall (function Some(Op instr) -> Lower.encodes instr | _ -> true)
        if encodes then
          for KeyValue(k, v) in local do rewrite[k] <- v
          if not (taken.ContainsKey t) then taken[t] <- HashSet()
          for i in range do taken[t].Add i |> ignore
    if rewrite.Count = 0 then items
    else
      [| for i in 0 .. n - 1 do
           match rewrite.TryGetValue i with
           | true, Some it -> yield it, snd items[i]
           | true, None -> ()
           | _ -> yield items[i] |]

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

/// The passes, each one switchable (for measuring what each does), and the guard that keeps a
/// pass's rewrite only when it is no worse (`monotone`). fixLoops always runs: it is not an
/// optimisation, it keeps DO loop ends legal.
type Passes =
  { forward     : bool   // value numbering: loads a register already holds dropped, others copied
    accCopies   : bool   // the same, with copies between accumulators
    copyForward : bool   // a copy's source read in place of the copy
    deadTemps   : bool   // stores to X temporaries nothing reads dropped
    hoist       : bool   // loads a DO loop does not change done once, before the loop
    pack        : bool   // moves merged into parallel instructions
    rotate      : bool   // one-stage software pipelining of DO loops
    promote     : bool   // accumulator temporaries kept in a free accumulator
    assign      : bool   // temporaries' webs in free registers
    joins       : bool   // raw accumulator equality retained through CFG joins
    monotone    : bool } // each pass kept only when the static cost is no worse

  static member all =
    { forward = true; accCopies = true; copyForward = true; deadTemps = true; hoist = true; pack = true
      rotate = true; promote = true; assign = true; joins = true; monotone = true }

  static member none =
    { forward = false; accCopies = false; copyForward = false; deadTemps = false; hoist = false; pack = false
      rotate = false; promote = false; assign = false; joins = false; monotone = false }

/// The switchable passes by name, with how to turn each off.
let passes : (string * (Passes -> Passes)) list =
  [ "forward",     (fun p -> { p with forward = false })
    "accCopies",   (fun p -> { p with accCopies = false })
    "copyForward", (fun p -> { p with copyForward = false })
    "deadTemps",   (fun p -> { p with deadTemps = false })
    "hoist",       (fun p -> { p with hoist = false })
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
      weighted <- weighted + trips ()
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
    out.Add(Label(handStub k), 0)
    out.Add(Op Rts, 0)
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

/// The passes of `p`; the compiler's temporaries are X words `first` up to `temps`.
let runWith (p: Passes) (first: int) (temps: int) (items: Item array) (lines: int array) =
  let frozen, regions = freezeHand (Array.zip items lines)
  let mutable cur = frozen
  // a pass's rewrite, kept when it is no worse (when the passes are guarded)
  let step (on: bool) (pass: (Item * int) array -> (Item * int) array) =
    if on then
      let next = pass cur
      if not p.monotone || staticCost next <= staticCost cur then cur <- next
  for _ in 1 .. 3 do
    step p.forward forward
    step p.copyForward copyForward
    step p.deadTemps (deadTempsFrom first temps)
  for _ in 1 .. 2 do
    step p.accCopies (forwardWith true)
    step p.copyForward copyForward
    step p.deadTemps (deadTempsFrom first temps)
  step p.joins (joinAccumulators first temps)
  if p.joins then step p.deadTemps (deadTempsFrom first temps)
  step p.promote (promote first temps)
  step p.assign (assign first temps)
  for _ in 1 .. 2 do
    step p.forward forward
    step p.copyForward copyForward
  step p.hoist (hoistFrom first temps)
  step p.pack pack
  step p.rotate (rotate >> (if p.pack then pack else id))
  cur <- thawHand regions cur
  cur <- fixLoops cur
  Array.map fst cur, Array.map snd cur

/// The passes of `level`; the compiler's temporaries are X words `first` up to `temps`.
let runFrom (level: Level) (first: int) (temps: int) (items: Item array) (lines: int array) =
  match level with
  | Direct -> items, lines
  | Peephole -> runWith Passes.all first temps items lines
  | Chosen p -> runWith p first temps items lines

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

/// Compile, run the passes of `level`, choose subroutines (`policy`), and keep the program within
/// `limit` words: when it is larger, the loops whose unswitching added the most are kept whole
/// (tested on each pass), the largest first, until it fits or none is unswitched; then, unless the
/// policy is Off, helpers RENDER calls are made subroutines too. None: no limit.
let compileChoosingWith (handOff: Set<string>) (policy: Subroutines) (limit: int option) (level: Level) (target: Kernel.Target) (k: Kernel.Kernel) =
  let ik = FromQuotation.kernel k
  let attempt (chosen: Set<string>) (unswitchMax: int option) =
    Compile.generateIrWith handOff chosen unswitchMax target ik
    |> Result.map (fun (c, costs) ->
      let items, lines = runFrom level c.layout.xWorkset c.xWorkset c.items c.lines
      { c with items = items; lines = lines }, costs)
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
  match attempt Set.empty None with
  | Error e -> Error e
  | Ok(c0, costs) ->
    let chosen, c = if policy = Off then Set.empty, c0 else choose policy None (Set.empty, c0)
    if fits c then Ok c
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
        Ok(snd (choose Size unswitchMax (chosen, c')))
      | r -> r

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

/// `compile`, the [<HandWritten>] helpers of `handOff` (by key) compiled from their F# reference.
let compileHandOff (handOff: Set<string>) (level: Level) (target: Kernel.Target) (k: Kernel.Kernel) =
  compileChoosingWith handOff (subroutinesPolicy ()) (Kernel.programLimit target |> Option.map fst) level target k
  |> Result.bind Compile.checkSize

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

/// Loads into a data register of a workset word a DO loop does not write, or of an immediate,
/// where nothing else in the loop writes the register and the straight code before the load does
/// not read it: done once, before the loop (a parameter, a state word only read there, a
/// constant such as the zero a silent block writes).
let hoist (items: (Item * int) array) =
  let control (instr: Instr) =
    match instr with
    | Branch _ | Jump _ | Loop _ | Rep _ | Enddo -> true
    | _ -> false
  let overlaps (r: Reg) (ws: Reg array) = ws |> Array.exists (fun w -> Lower.storage w |> Array.exists (fun x -> Array.contains x (Lower.storage r)))
  let once (its: (Item * int) array) =
    let candidate (i: int) (e: int) (k: int) =
      let data (r: Reg) = r = X0 || r = X1 || r = Y0 || r = Y1
      // a workset word (its space and index), or an immediate (None): nothing in memory to watch
      let source =
        match fst its[k] with
        | Op(Par(None, [| Load(space, ea, dst) |])) when data dst ->
          match workset space ea with
          | Some(SMem(_, w)) -> Some(dst, Some(space, w))
          | _ -> None
        | Op(Par(None, [| Imm(_, dst) |])) when data dst -> Some(dst, None)
        | _ -> None
      match source with
      | Some(dst, word) ->
          // every pass reaches the load: what comes before it branches only forward to it or
          // short of it, and does not read the register
          let inside (t: Operand) =
            match t.expr with
            | Sym l -> its |> Array.tryFindIndex (fun (x, _) -> x = Label l) |> Option.exists (fun at -> at > i && at <= k)
            | _ -> false
          let before =
            [ i + 1 .. k - 1 ] |> List.forall (fun j ->
              match fst its[j] with
              | Comment _ | Label _ -> true
              | Op(Branch((Bcc _ | Bra), t)) -> inside t
              | Op instr when not (control instr) ->
                match readsOf instr with
                | Some rs -> not (overlaps dst rs)
                | None -> false
              | _ -> false)
          let others =
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
                      moves |> Array.forall (fun m ->
                        match m with
                        | Store(_, sp, ea') when sp = space ->
                          match workset sp ea', ea' with
                          | Some(SMem(_, w')), _ -> w' <> w
                          | None, PostInc 7 -> true
                          | _ -> false
                        | _ -> true)
                  | _ -> true
                match writesOf instr with
                | Some ws -> stores && not (overlaps dst ws) && not (Array.contains (R 6) ws)
                // a subroutine may write the word
                | None -> control instr && (match instr with Branch((Bsr | Bscc _), _) | Jump((Jsr | Jscc _), _) -> false | _ -> true)
              | _ -> true)
          before && others
      | None -> false
    its |> Array.indexed |> Array.tryPick (fun (i, (it, _)) ->
      match it with
      | Op(Loop(_, _, { expr = Sym name })) ->
        match its |> Array.tryFindIndex (fun (x, _) -> x = Label name) with
        | Some e when e > i ->
          [ i + 1 .. e - 1 ] |> List.tryFind (candidate i e) |> Option.map (fun k ->
            // before the loop's label and relocation marker
            let mutable p = i
            if p > 0 && fst its[p - 1] = machinedrum.Abi.relocateMarker then p <- p - 1
            if p > 0 && (match fst its[p - 1] with Label l -> l.EndsWith "_DO" | _ -> false) then p <- p - 1
            k, p)
        | _ -> None
      | _ -> None)
  let mutable cur = items
  let mutable go = true
  while go do
    match once cur with
    | Some(k, p) ->
      let moved = cur[k]
      let rest = cur |> Array.removeAt k
      cur <- rest |> Array.insertAt p moved
    | None -> go <- false
  cur

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
    monotone    : bool } // each pass kept only when the static cost is no worse

  static member all =
    { forward = true; accCopies = true; copyForward = true; deadTemps = true; hoist = true; pack = true
      rotate = true; monotone = true }

  static member none =
    { forward = false; accCopies = false; copyForward = false; deadTemps = false; hoist = false; pack = false
      rotate = false; monotone = false }

/// The switchable passes by name, with how to turn each off.
let passes : (string * (Passes -> Passes)) list =
  [ "forward",     (fun p -> { p with forward = false })
    "accCopies",   (fun p -> { p with accCopies = false })
    "copyForward", (fun p -> { p with copyForward = false })
    "deadTemps",   (fun p -> { p with deadTemps = false })
    "hoist",       (fun p -> { p with hoist = false })
    "pack",        (fun p -> { p with pack = false })
    "rotate",      (fun p -> { p with rotate = false }) ]

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

/// The passes of `p`; the compiler's temporaries are X words `first` up to `temps`.
let runWith (p: Passes) (first: int) (temps: int) (items: Item array) (lines: int array) =
  let mutable cur = Array.zip items lines
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
  step p.hoist hoist
  step p.pack pack
  step p.rotate (rotate >> (if p.pack then pack else id))
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

/// Compile, then run the passes of `level` over the items.
/// Compile, run the passes of `level`, and keep the program within `limit` words: when it is
/// larger, the loops whose unswitching added the most are kept whole (tested on each pass), the
/// largest first, until it fits or none is unswitched. None: no limit.
let compileWithin (limit: int option) (level: Level) (target: Kernel.Target) (k: Kernel.Kernel) =
  let attempt (unswitchMax: int option) =
    Compile.generateUnswitching unswitchMax target k
    |> Result.map (fun (c, costs) ->
      let items, lines = runFrom level c.layout.xWorkset c.xWorkset c.items c.lines
      { c with items = items; lines = lines }, costs)
  let fits (c: Compile.Compiled) =
    match limit, Compile.size c with
    | Some l, Ok s -> s.total <= l
    | _ -> true
  match attempt None with
  | Ok(c, costs) when not (fits c) && not costs.IsEmpty ->
    // each distinct cost, largest first: loops costing more than the next one down kept whole
    let rec smaller (thresholds: int list) (best: Compile.Compiled) =
      match thresholds with
      | [] -> Ok best
      | m :: rest ->
        match attempt (Some m) with
        | Ok(c, _) when fits c -> Ok c
        | Ok(c, _) -> smaller rest c
        | Error e -> Error e
    let thresholds = (costs |> List.distinct |> List.sortDescending |> List.tail) @ [ -1 ]
    smaller thresholds c
  | r -> r |> Result.map fst

/// Compile, then run the passes of `level` over the items; refused when the program is larger
/// than the target's machine holds (Kernel.programLimit), after keeping loops whole if that helps.
let compile (level: Level) (target: Kernel.Target) (k: Kernel.Kernel) =
  compileWithin (Kernel.programLimit target |> Option.map fst) level target k
  |> Result.bind Compile.checkSize

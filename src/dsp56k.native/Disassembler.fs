/// Program recovery from machine words: code found by recursive descent from entry points
/// (instructions decoded by the oracle disassembler and read back by the F# parser), labels at
/// every branch, call and loop target, `dc` for what no path reaches. Reassembling the items
/// gives the same words.
module dsp56k.Disassembler

open dsp56k

/// Rewrite every operand of an instruction.
let mapOperands (f: Operand -> Operand) (instr: Instr) =
  let ea e =
    match e with
    | Disp(r, o) -> Disp(r, f o)
    | Abs o -> Abs(f o)
    | e -> e
  let move m =
    match m with
    | Load(s, e, r) -> Load(s, ea e, r)
    | Store(r, s, e) -> Store(r, s, ea e)
    | Imm(v, r) -> Imm(f v, r)
    | Update e -> Update(ea e)
    | m -> m
  let count c =
    match c with
    | Count v -> Count(f v)
    | FromMem(s, e) -> FromMem(s, ea e)
    | c -> c
  let bit t =
    match t with
    | BitMem(s, e) -> BitMem(s, ea e)
    | t -> t
  match instr with
  | Par(a, ms) -> Par(a, Array.map move ms)
  | AluImm(op, v, d) -> AluImm(op, f v, d)
  | MulI(k, n, v, s, d) -> MulI(k, n, f v, s, d)
  | Loop(k, c, t) -> Loop(k, count c, f t)
  | Forever(k, t) -> Forever(k, f t)
  | Rep c -> Rep(count c)
  | Branch(k, t) -> Branch(k, f t)
  | Jump(k, t) -> Jump(k, f t)
  | JumpEa(k, e) -> JumpEa(k, ea e)
  | BitBranch(k, b, t, d) -> BitBranch(k, b, bit t, f d)
  | BitOp(k, b, t) -> BitOp(k, b, bit t)
  | Lua(e, d) -> Lua(ea e, d)
  | Lra(LraTarget t, d) -> Lra(LraTarget(f t), d)
  | PlockR(l, t) -> PlockR(l, f t)
  | Plock(l, e) -> Plock(l, ea e)
  | Extract(u, FieldConst v, s, d) -> Extract(u, FieldConst(f v), s, d)
  | Insert(FieldConst v, s, d) -> Insert(FieldConst(f v), s, d)
  | Vsl(s, i, e) -> Vsl(s, i, ea e)
  | Movep(t, sp, a, MovepMem(s, e)) -> Movep(t, sp, f a, MovepMem(s, ea e))
  | Movep(t, sp, a, MovepImm v) -> Movep(t, sp, f a, MovepImm(f v))
  | Movep(t, sp, a, o) -> Movep(t, sp, f a, o)
  | i -> i

let value (o: Operand) =
  match o.expr with
  | Num v -> Some(int v)
  | _ -> None

/// Program addresses an instruction can transfer control to (besides falling through), and
/// whether it falls through.
let flow (pc: int) (instr: Instr) =
  match instr with
  | Branch((Bra), t) | Jump(Jmp, t) -> (value t |> Option.toArray), false
  | Branch(_, t) | Jump(_, t) -> (value t |> Option.toArray), true
  | BitBranch(_, _, _, d) -> (value d |> Option.toArray), true
  | Loop(_, _, t) | Forever(_, t) -> (value t |> Option.toArray), true
  | Lra(LraTarget t, _) | PlockR(_, t) -> (value t |> Option.toArray), true
  | JumpEa(Jmp, _) | BranchRn(Bra, _) | Rts | Rti | Stop | Illegal | Reset -> [||], false
  | _ -> [||], true

type Decoded =
  { pc    : int
    words : int
    instr : Instr }

/// Decode the instruction at `pc` with the oracle and read it back.
let decode (words: int array) pc =
  let text, n = Native.disassemble pc words[pc] (if pc + 1 < words.Length then words[pc + 1] else 0)
  if text.StartsWith "dc" || n = 0 || pc + n > words.Length then None
  else
    match Parse.parseLine 0 ("        " + text) with
    | Ok { body = Parse.Instruction i } ->
      // The disassembler prints long operands without `>` when a short form would also hold
      // the value: pin the original size.
      let env : Encode.Env = { pc = pc; value = Expr.eval (fun _ -> None); settled = fun _ -> true }
      let sized f = mapOperands (fun o -> if o.force = Auto then { o with force = f } else o) i
      let fits instr = match Encode.encode env instr with Ok ws -> ws.Length = n | Error _ -> false
      [| i; sized (if n = 1 then Short else Long) |]
      |> Array.tryFind fits
      |> Option.map (fun i -> { pc = pc; words = n; instr = i })
    | _ -> None

/// Recursive descent from the entry points (program addresses).
let recover (words: int array) (entries: int array) =
  let code = System.Collections.Generic.Dictionary<int, Decoded>()
  let targets = System.Collections.Generic.HashSet<int>(entries)
  let pending = System.Collections.Generic.Stack<int>(entries)
  while pending.Count > 0 do
    let mutable pc = pending.Pop()
    let mutable go = true
    while go && pc >= 0 && pc < words.Length && not (code.ContainsKey pc) do
      match decode words pc with
      | None -> go <- false
      | Some d ->
        code[pc] <- d
        let jumps, falls = flow pc d.instr
        for t in jumps do
          if t >= 0 && t < words.Length then
            targets.Add t |> ignore
            pending.Push t
        if falls then pc <- pc + d.words else go <- false
  code, targets

let labelName (pc: int) = $"L_{pc:x4}"

/// Items for the words, each with its address: recovered code with labels at targets, `dc` for
/// the rest. `names` gives labels for known addresses (entry points); `relocated` lists the
/// extension words holding a program address (those operands become label references).
let itemsAt (words: int array) (entries: int array) (names: Map<int, string>) (relocated: Set<int>) =
  let code, targets = recover words entries
  let name pc = Map.tryFind pc names |> Option.defaultValue (labelName pc)
  let referenced = System.Collections.Generic.HashSet<int>(targets)
  // relocated words that point into the program get labels too
  for w in relocated do
    if w < words.Length && words[w] < words.Length then referenced.Add words[w] |> ignore
  let toLabel (d: Decoded) (o: Operand) =
    match o.expr with
    | Num v when int v >= 0 && int v < words.Length && referenced.Contains(int v) ->
      let isRelocated = relocated.Contains(d.pc + 1)
      let isFlow = (fst (flow d.pc d.instr)) |> Array.contains (int v)
      // A label can be a forward reference, which the assembler sizes long: keep the original
      // size explicit.
      let force = if o.force <> Auto then o.force elif d.words = 1 then Short else Long
      if isRelocated || isFlow then { o with expr = Sym(name (int v)); force = force } else o
    | _ -> o
  [| let mutable pc = 0
     while pc < words.Length do
       if referenced.Contains pc || names.ContainsKey pc then pc, Label(name pc)
       match code.TryGetValue pc with
       | true, d ->
         pc, Op(mapOperands (toLabel d) d.instr)
         pc <- pc + d.words
       | _ ->
         // a run of data up to the next code or label
         let start = pc
         pc <- pc + 1
         while pc < words.Length && not (code.ContainsKey pc) && not (referenced.Contains pc) && pc - start < 8 do
           pc <- pc + 1
         start, Dc(words[start .. pc - 1] |> Array.map (fun w -> Num(int64 w))) |]

let items words entries names relocated = itemsAt words entries names relocated |> Array.map snd

/// F# source in the DSL for a program: the importer's output, a starting point for porting an
/// existing listing (assemble the result and compare words, then refactor into idioms).
module dsp56k.dsl.Transcribe
open dsp56k.dsl.Asm

open dsp56k

let reg (r: Reg) =
  match r with
  | X -> "xL"
  | Y -> "yL"
  | r -> Print.reg r

let quote (s: string) = "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""

/// An expression where the DSL takes an int, string or Expr.
let rec expr (e: Expr) =
  match e with
  | Num v when v >= 0L && v < 10L -> string v
  | Num v when v < 0L -> $"(-0x{-v:x})"
  | Num v -> $"0x{v:x}"
  | Sym s -> quote s
  | _ -> $"({exprTyped e})"

/// An expression that must be an Expr (inside arithmetic).
and exprTyped (e: Expr) =
  let side x =
    match x with
    | Num v -> $"ex {expr (Num v)}"
    | Sym s -> $"sym {quote s}"
    | _ -> $"({exprTyped x})"
  match e with
  | Num _ | Sym _ -> side e
  | Neg x -> $"-({exprTyped x})"
  | Bin(op, l, Num v) when Array.contains op [| BinOp.Add; BinOp.Sub; BinOp.Mul; BinOp.Div; BinOp.Shl; BinOp.Shr; BinOp.And |] ->
    let o = match op with BinOp.Add -> "+" | BinOp.Sub -> "-" | BinOp.Mul -> "*" | BinOp.Div -> "/" | BinOp.Shl -> "<<<" | BinOp.Shr -> ">>>" | _ -> "&&&"
    $"{side l} {o} {v}"
  | Bin(BinOp.Add, l, r) -> $"{side l} + {side r}"
  | Bin(BinOp.Sub, l, r) -> $"{side l} - {side r}"
  | Bin(BinOp.Mul, l, r) -> $"{side l} * {side r}"
  | Bin(BinOp.Or, l, r) -> $"{side l} ||| {side r}"
  | Bin(op, l, r) -> $"Bin(BinOp.{op}, {exprTyped l}, {exprTyped r})"
  | Not x -> $"Not({exprTyped x})"

let operand (o: Operand) =
  match o.force with
  | Auto -> $"imm {expr o.expr}"
  | Long -> $"immL {expr o.expr}"
  | Short -> $"immS {expr o.expr}"
  | IoShort -> $"{{ imm {expr o.expr} with force = IoShort }}"

let ea (e: Ea) =
  match e with
  | PostDecN r -> $"(decN r{r})"
  | PostIncN r -> $"(incN r{r})"
  | PostDec r -> $"(dec r{r})"
  | PostInc r -> $"(inc r{r})"
  | Ind r -> $"(ind r{r})"
  | IndexN r -> $"(idx r{r})"
  | PreDec r -> $"(pre r{r})"
  | Disp(r, o) ->
    match o.force with
    | Long -> $"(dispL r{r} {expr o.expr})"
    | _ -> $"(disp r{r} {expr o.expr})"
  | Abs o ->
    match o.force with
    | Auto -> $"(abs' {expr o.expr})"
    | Short -> $"(absS {expr o.expr})"
    | Long -> $"(absL {expr o.expr})"
    | IoShort -> $"(absIo {expr o.expr})"

let mem (s: Space) (e: Ea) =
  let f = match s with Space.X -> "xm" | Space.Y -> "ym" | Space.L -> "lm" | Space.P -> "pm"
  $"({f} {ea e})"

let move (m: Move) =
  match m with
  | Copy(s, d) -> $"move {reg s} {reg d}"
  | Load(s, e, d) -> $"move {mem s e} {reg d}"
  | Store(r, s, e) -> $"move {reg r} {mem s e}"
  | Imm(v, d) -> $"move ({operand v}) {reg d}"
  | Update e -> $"update {ea e}"

let aluName (op: AluOp) =
  match op with
  | AluOp.And -> "and'"
  | AluOp.Or -> "or'"
  | o -> (string o).ToLowerInvariant()

let alu (a: Alu) =
  match a with
  | Binary(op, s, d) -> $"{aluName op} {reg s} {reg (Acc.reg d)}"
  | Unary(op, d) ->
    let name =
      match op with
      | AluUnary.Abs -> "abs''"
      | AluUnary.Not -> "not'"
      | o -> (string o).ToLowerInvariant()
    $"{name} {reg (Acc.reg d)}"
  | Mul(kind, neg, s1, s2, d) -> $"""{(string kind).ToLowerInvariant()}{(if neg then "n" else "")} {reg s1} {reg s2} {reg (Acc.reg d)}"""
  | Max -> "max'"
  | Maxm -> "maxm"

let cond (c: Cond) = $"Cond.{c}"

let target (o: Operand) = expr o.expr

let instr (i: Instr) =
  let named prefix (c: Cond) shortcuts t =
    if Array.contains c shortcuts then $"{prefix}{Cond.name c} {t}" else $"{prefix}cc {cond c} {t}"
  match i with
  | Par(None, [||]) -> "par [||]"
  | Par(None, moves) -> moves |> Array.map move |> String.concat " ++ "
  | Par(Some a, moves) -> String.concat " ++ " [| alu a; yield! moves |> Array.map (fun m -> $"({move m})") |]
  | IfCc(a, c, false) -> $"({alu a} |> ifcc {cond c})"
  | IfCc(a, c, true) -> $"({alu a} |> ifccU {cond c})"
  | AluImm(op, v, d) ->
    let name = match op with AddI -> "add" | SubI -> "sub" | CmpI -> "cmp" | AndI -> "and'" | OrI -> "or'" | EorI -> "eor"
    $"{name} ({operand v}) {reg (Acc.reg d)}"
  | ShiftImm(ShAsl, n, s, d) -> $"asli {n} {reg (Acc.reg s)} {reg (Acc.reg d)}"
  | ShiftImm(ShAsr, n, s, d) -> $"asri {n} {reg (Acc.reg s)} {reg (Acc.reg d)}"
  | ShiftImm(ShLsl, n, _, d) -> $"lsli {n} {reg (Acc.reg d)}"
  | ShiftImm(ShLsr, n, _, d) -> $"lsri {n} {reg (Acc.reg d)}"
  | ShiftReg(ShAsl, r, s, d) -> $"aslr {reg r} {reg (Acc.reg s)} {reg (Acc.reg d)}"
  | ShiftReg(ShAsr, r, s, d) -> $"asrr {reg r} {reg (Acc.reg s)} {reg (Acc.reg d)}"
  | ShiftReg(ShLsl, r, _, d) -> $"lslr {reg r} {reg (Acc.reg d)}"
  | ShiftReg(ShLsr, r, _, d) -> $"lsrr {reg r} {reg (Acc.reg d)}"
  | Lua(e, d) -> $"lua {ea e} {reg d}"
  | Loop(Do, c, t) | Loop(Dor, c, t) ->
    let name = match i with Loop(Do, _, _) -> "doTo" | _ -> "dorTo"
    let count =
      match c with
      | Count v -> $"({operand v})"
      | FromReg r -> reg r
      | FromMem(s, e) -> mem s e
    match t.expr with
    | Sym l -> $"{name} {count} {quote l}"
    | _ -> $"asm {quote (Print.instr i)}"
  | Rep(Count v) -> $"rep ({operand v})"
  | Rep(FromReg r) -> $"rep {reg r}"
  | Rep(FromMem(s, e)) -> $"rep {mem s e}"
  | Branch(kind, t) when t.force = Auto ->
    match kind with
    | Bra -> $"bra {target t}"
    | Bsr -> $"bsr {target t}"
    | Bcc c -> named "b" c [| Cond.Eq; Cond.Ne; Cond.Lt; Cond.Le; Cond.Gt; Cond.Ge; Cond.Pl; Cond.Mi |] (target t)
    | Bscc c -> $"branch (Bscc {cond c}) {target t}"
  | Branch(kind, t) ->
    let k = match kind with Bra -> "Bra" | Bsr -> "Bsr" | Bcc c -> $"(Bcc {cond c})" | Bscc c -> $"(Bscc {cond c})"
    let f = if t.force = Long then "branchL" else "branch"
    $"{f} {k} {target t}"
  | Jump(kind, t) when t.force = Auto ->
    match kind with
    | Jmp -> $"jmp {target t}"
    | Jsr -> $"jsr {target t}"
    | Jcc c -> named "j" c [| Cond.Eq; Cond.Ne; Cond.Lt; Cond.Ge |] (target t)
    | Jscc c -> $"jump (Jscc {cond c}) {target t}"
  | Jump(kind, t) ->
    let k = match kind with Jmp -> "Jmp" | Jsr -> "Jsr" | Jcc c -> $"(Jcc {cond c})" | Jscc c -> $"(Jscc {cond c})"
    let f = if t.force = Long then "jumpL" else "jump"
    $"{f} {k} {target t}"
  | JumpEa(Jmp, e) -> $"jmpEa {ea e}"
  | JumpEa(Jsr, e) -> $"jsrEa {ea e}"
  | BitBranch(kind, bit, t, d) when (match kind with Brclr | Brset | Jclr | Jset -> true | _ -> false) ->
    let tt = match t with BitMem(s, e) -> mem s e | BitReg r -> reg r
    $"{(string kind).ToLowerInvariant()} {bit} {tt} {target d}"
  | BitOp(kind, bit, t) ->
    let tt = match t with BitMem(s, e) -> mem s e | BitReg r -> reg r
    $"{(string kind).ToLowerInvariant()} {bit} {tt}"
  | Tcc(c, Some(s, d), None) ->
    let shortcut = [| Cond.Eq; Cond.Ne; Cond.Lt; Cond.Le; Cond.Gt; Cond.Ge |]
    if Array.contains c shortcut then $"t{Cond.name c} {reg s} {reg (Acc.reg d)}"
    else $"tcc {cond c} {reg s} {reg (Acc.reg d)}"
  | CtrlImm(true, v, r) -> $"andi 0x{v:x} {reg r}"
  | CtrlImm(false, v, r) -> $"ori 0x{v:x} {reg r}"
  | MulImm(Mpy, false, s, n, d) -> $"mpyBy {reg s} {n} {reg (Acc.reg d)}"
  | MulImm(Mac, false, s, n, d) -> $"macBy {reg s} {n} {reg (Acc.reg d)}"
  | MulSu(Mpy, false, false, s1, s2, d) -> $"mpysu {reg s1} {reg s2} {reg (Acc.reg d)}"
  | MulSu(Mac, false, false, s1, s2, d) -> $"macsu {reg s1} {reg s2} {reg (Acc.reg d)}"
  | MulSu(Mpy, false, true, s1, s2, d) -> $"mpyuu {reg s1} {reg s2} {reg (Acc.reg d)}"
  | MulSu(Mac, false, true, s1, s2, d) -> $"macuu {reg s1} {reg s2} {reg (Acc.reg d)}"
  | Div(s, d) -> $"div {reg s} {reg (Acc.reg d)}"
  | Norm(r, d) -> $"norm r{r} {reg (Acc.reg d)}"
  | Rts -> "rts"
  | Rti -> "rti"
  | Nop -> "nop"
  | Illegal -> "illegal"
  | Stop -> "stop"
  | Wait -> "wait"
  | Reset -> "reset"
  | Debug -> "debug"
  | Trap -> "trap"
  | Enddo -> "enddo"
  | other -> $"asm {quote (Print.instr other)}"

let item (it: Item) =
  match it with
  | Label name -> $"label {quote name}"
  | Op i -> instr i
  | Dc values when values |> Array.forall (function Num _ -> true | _ -> false) ->
    let words = values |> Array.map (function Num v -> $"0x{v &&& 0xffffffL:x6}" | _ -> "") |> String.concat "; "
    $"dc [| {words} |]"
  | Dc values -> $"""dcE [| {values |> Array.map exprTyped |> String.concat "; "} |]"""
  | Ds count -> $"Ds({exprTyped count})"
  | Equ(name, v) -> $"equ {quote name} {expr v}"
  | Org(s, a) -> $"org Space.{s} {expr a}"
  | Comment text -> $"comment {quote text}"

/// A module holding `program`, one item per line.
let moduleSource (moduleName: string) (header: string array) (items: Item array) =
  [| for h in header do
       $"// {h}"
     $"module {moduleName}"
     ""
     "open dsp56k"
     "open dsp56k.dsl"
     "open dsp56k.dsl.Asm"
     ""
     "let program = [|"
     for it in items do
       "  " + item it
     "|]"
     "" |]
  |> String.concat "\n"

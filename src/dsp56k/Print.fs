/// Motorola DSP56300 assembler syntax for Isa values (what asm56300 and the dsp56300 tools read).
module dsp56k.Print

let reg r =
  match r with
  | R n -> $"r{n}"
  | N n -> $"n{n}"
  | M n -> $"m{n}"
  | r -> (string r).ToLowerInvariant()

let acc a = reg (Acc.reg a)

let space s =
  match s with
  | Space.X -> "x:"
  | Space.Y -> "y:"
  | Space.L -> "l:"
  | Space.P -> "p:"

let force f =
  match f with
  | Auto -> ""
  | Short -> "<"
  | Long -> ">"
  | IoShort -> "<<"

let operand (o: Operand) = force o.force + Expr.print o.expr
let imm (o: Operand) = "#" + operand o

let ea e =
  match e with
  | PostDecN r -> $"(r{r})-n{r}"
  | PostIncN r -> $"(r{r})+n{r}"
  | PostDec r  -> $"(r{r})-"
  | PostInc r  -> $"(r{r})+"
  | Ind r      -> $"(r{r})"
  | IndexN r   -> $"(r{r}+n{r})"
  | PreDec r   -> $"-(r{r})"
  | Disp(r, o) ->
    match o.expr with
    | Num v when v < 0L -> $"(r{r}{force o.force}-${-v:x})"
    | _ -> $"(r{r}+{operand o})"
  | Abs o -> operand o

let move m =
  match m with
  | Load(s, e, r)  -> $"{space s}{ea e},{reg r}"
  | Store(r, s, e) -> $"{reg r},{space s}{ea e}"
  | Copy(s, d)     -> $"{reg s},{reg d}"
  | Imm(v, d)      -> $"{imm v},{reg d}"
  | Update e       -> ea e

let aluOpName op = (string op).ToLowerInvariant()

let mulName kind = (string kind).ToLowerInvariant()

let alu a =
  match a with
  | Binary(op, s, d) -> $"{aluOpName op} {reg s},{acc d}"
  | Unary(op, d) -> $"{(string op).ToLowerInvariant()} {acc d}"
  | Mul(kind, neg, s1, s2, d) -> $"""{mulName kind} {(if neg then "-" else "")}{reg s1},{reg s2},{acc d}"""
  | Max -> "max a,b"
  | Maxm -> "maxm a,b"

let cond c = Cond.name c

let immOpName op =
  match op with
  | AddI -> "add" | SubI -> "sub" | CmpI -> "cmp"
  | AndI -> "and" | OrI  -> "or"  | EorI -> "eor"

let shiftName op =
  match op with
  | ShAsl -> "asl"
  | ShAsr -> "asr"
  | ShLsl -> "lsl"
  | ShLsr -> "lsr"

let loopCount c =
  match c with
  | Count v -> imm v
  | FromReg r -> reg r
  | FromMem(s, e) -> space s + ea e

let branchName kind =
  match kind with
  | Bra -> "bra"
  | Bsr -> "bsr"
  | Bcc c -> "b" + cond c
  | Bscc c -> "bs" + cond c

let jumpName kind =
  match kind with
  | Jmp -> "jmp"
  | Jsr -> "jsr"
  | Jcc c -> "j" + cond c
  | Jscc c -> "js" + cond c

let bitTarget t =
  match t with
  | BitMem(s, e) -> space s + ea e
  | BitReg r -> reg r

/// Mnemonic and operand fields of an instruction.
let fields instr : string * string array =
  match instr with
  | Par(None, [| (Load(_, _, r) | Store(r, _, _) | Imm(_, r)) as m |]) when Reg.isControl r -> "movec", [| move m |]
  | Par(None, [| Copy(s, d) as m |]) when Reg.isControl s || Reg.isControl d -> "movec", [| move m |]
  | Par(None, [| (Load(Space.P, _, _) | Store(_, Space.P, _)) as m |]) -> "movem", [| move m |]
  | Par(None, moves) -> "move", Array.map move moves
  | Par(Some a, moves) ->
    let text = alu a
    let i = text.IndexOf ' '
    text[.. i - 1], [| text[i + 1 ..]; yield! Array.map move moves |]
  | IfCc(a, c, u) ->
    let text = alu a
    let i = text.IndexOf ' '
    text[.. i - 1], [| text[i + 1 ..]; $"""if{cond c}{(if u then ".u" else "")}""" |]
  | AluImm(op, v, d) -> immOpName op, [| $"{imm v},{acc d}" |]
  | ShiftImm(op, n, s, d) when op = ShLsl || op = ShLsr -> shiftName op, [| $"#{n},{acc d}" |]
  | ShiftImm(op, n, s, d) -> shiftName op, [| $"#{n},{acc s},{acc d}" |]
  | ShiftReg(op, r, s, d) when op = ShLsl || op = ShLsr -> shiftName op, [| $"{reg r},{acc d}" |]
  | ShiftReg(op, r, s, d) -> shiftName op, [| $"{reg r},{acc s},{acc d}" |]
  | MulImm(kind, neg, src, n, d) -> mulName kind, [| $"""{(if neg then "-" else "")}{reg src},#{n},{acc d}""" |]
  | MulSu(kind, neg, uu, s1, s2, d) ->
    $"""{mulName kind}{(if uu then "uu" else "su")}""", [| $"""{(if neg then "-" else "")}{reg s1},{reg s2},{acc d}""" |]
  | MulI(kind, neg, v, src, d) -> mulName kind + "i", [| $"""{(if neg then "-" else "")}{imm v},{reg src},{acc d}""" |]
  | Div(s, d) -> "div", [| $"{reg s},{acc d}" |]
  | Norm(r, d) -> "norm", [| $"r{r},{acc d}" |]
  | Tcc(c, alu, agu) ->
    "t" + cond c,
    [| match alu with
       | Some(s, d) -> $"{reg s},{acc d}"
       | None -> ()
       match agu with
       | Some(s, d) -> $"r{s},r{d}"
       | None -> () |]
  | CtrlImm(andi, v, r) -> (if andi then "andi" else "ori"), [| $"#${v:x},{reg r}" |]
  | Loop(kind, count, endLabel) -> (if kind = Do then "do" else "dor"), [| $"{loopCount count},{operand endLabel}" |]
  | Forever(kind, endLabel) -> (if kind = Do then "do" else "dor"), [| $"forever,{operand endLabel}" |]
  | Rep count -> "rep", [| loopCount count |]
  | Enddo -> "enddo", [||]
  | Branch(kind, target) -> branchName kind, [| operand target |]
  | BranchRn(kind, r) -> branchName kind, [| $"r{r}" |]
  | Jump(kind, target) -> jumpName kind, [| operand target |]
  | JumpEa(kind, e) -> jumpName kind, [| ea e |]
  | BitBranch(kind, bit, t, dest) -> (string kind).ToLowerInvariant(), [| $"#{bit},{bitTarget t},{operand dest}" |]
  | BitOp(kind, bit, t) -> (string kind).ToLowerInvariant(), [| $"#{bit},{bitTarget t}" |]
  | Lua(e, d) -> "lua", [| $"{ea e},{reg d}" |]
  | Lra(LraReg r, d) -> "lra", [| $"r{r},{reg d}" |]
  | Lra(LraTarget t, d) -> "lra", [| $"{operand t},{reg d}" |]
  | Inc d -> "inc", [| acc d |]
  | Dec d -> "dec", [| acc d |]
  | CondOp(Trapcc, c) -> "trap" + cond c, [||]
  | CondOp(Debugcc, c) -> "debug" + cond c, [||]
  | CondOp(Brkcc, c) -> "brk" + cond c, [||]
  | Cache op -> (string op).ToLowerInvariant(), [||]
  | Plock(lock, e) -> (if lock then "plock" else "punlock"), [| ea e |]
  | PlockR(lock, t) -> (if lock then "plockr" else "punlockr"), [| operand t |]
  | Clb(s, d) -> "clb", [| $"{acc s},{acc d}" |]
  | Normf(s, d) -> "normf", [| $"{reg s},{acc d}" |]
  | Merge(s, d) -> "merge", [| $"{reg s},{acc d}" |]
  | Extract(u, f, s, d) ->
    let field = match f with FieldReg r -> reg r | FieldConst v -> imm v
    (if u then "extractu" else "extract"), [| $"{field},{acc s},{acc d}" |]
  | Insert(f, s, d) ->
    let field = match f with FieldReg r -> reg r | FieldConst v -> imm v
    "insert", [| $"{field},{reg s},{acc d}" |]
  | Cmpu(s, d) -> "cmpu", [| $"{reg s},{acc d}" |]
  | Dmac(neg, signs, s1, s2, d) ->
    let suffix = match signs with SignedSigned -> "ss" | SignedUnsigned -> "su" | UnsignedUnsigned -> "uu"
    let sign = if neg then "-" else ""
    "dmac" + suffix, [| $"{sign}{reg s1},{reg s2},{acc d}" |]
  | Vsl(s, i, e) -> "vsl", [| $"{acc s},{i},l:{ea e}" |]
  | Movep(toP, ps, address, other) ->
    let periph = space ps + operand address
    let o = match other with MovepMem(sp, e) -> space sp + ea e | MovepReg r -> reg r | MovepImm v -> imm v
    "movep", [| (if toP then $"{o},{periph}" else $"{periph},{o}") |]
  | Rts -> "rts", [||]
  | Rti -> "rti", [||]
  | Nop -> "nop", [||]
  | Illegal -> "illegal", [||]
  | Stop -> "stop", [||]
  | Wait -> "wait", [||]
  | Reset -> "reset", [||]
  | Debug -> "debug", [||]
  | Trap -> "trap", [||]

/// One instruction on one line, fields separated by single spaces (the canonical form tests
/// compare against the disassembler).
let instr i =
  let mnemonic, operands = fields i
  String.concat " " [| mnemonic; yield! operands |]

/// One instruction in the column layout of the Motorola sources.
let instrColumns i =
  let mnemonic, operands = fields i
  match operands with
  | [||] -> $"        {mnemonic}"
  | _ -> $"        {mnemonic,-8}" + (operands |> Array.mapi (fun k o -> if k < operands.Length - 1 then $"{o,-16}" else o) |> String.concat "").TrimEnd()

let item it =
  match it with
  | Label name -> $"{name}:"
  | Op i -> instrColumns i
  | Dc values -> $"""        dc      {values |> Array.map Expr.print |> String.concat ","}"""
  | Ds count -> $"        ds      {Expr.print count}"
  | Equ(name, v) -> $"{name,-8}equ     {Expr.print v}"
  | Org(s, a) -> $"        org     {space s}{Expr.print a}"
  | Comment text -> $"; {text}"

let program (items: Item array) =
  items
  |> Array.map item
  |> String.concat "\n"

/// The authoring surface: an F# value for every DSP56300 instruction, reading like the Motorola
/// listing it stands for. Every function returns an `Item`; `++` adds parallel moves to an
/// instruction (`mac x0 y1 a ++ load (xm (inc r0)) x0 ++ move b y1`), so one line of a listing
/// stays one line of F#.
///
/// Operands that are numbers, symbols or expressions take an int, an int64, a string (a symbol)
/// or an `Expr`. Registers are checked where an instruction needs a particular kind (an
/// accumulator, an address register) when the program is built, with a message naming the
/// instruction.
module dsp56k.dsl.Asm

open dsp56k

// ---------------------------------------------------------------------------------------------
// Registers.

let x0, x1, y0, y1 = X0, X1, Y0, Y1
let a0, a1, a2, b0, b1, b2 = A0, A1, A2, B0, B1, B2
let a, b = A, B
/// x1:x0 and y1:y0 as 48-bit operands (`x` and `y` in the listing).
let xL, yL = X, Y
let a10, b10, ab, ba = A10, B10, AB, BA
let r0, r1, r2, r3, r4, r5, r6, r7 = R 0, R 1, R 2, R 3, R 4, R 5, R 6, R 7
let n0, n1, n2, n3, n4, n5, n6, n7 = N 0, N 1, N 2, N 3, N 4, N 5, N 6, N 7
let m0, m1, m2, m3, m4, m5, m6, m7 = M 0, M 1, M 2, M 3, M 4, M 5, M 6, M 7
let ep, vba, sc, sz, sr, omr, sp, ssh, ssl, la, lc = Ep, Vba, Sc, Sz, Sr, Omr, Sp, Ssh, Ssl, La, Lc
let mr, ccr, eom = Mr, Ccr, Eom

exception DslError of message: string

let fail message = raise (DslError message)

let accOf what (r: Reg) =
  match Acc.ofReg r with
  | Some acc -> acc
  | None -> fail $"{what}: {Print.reg r} is not an accumulator (a or b)"

let rnOf what (r: Reg) =
  match r with
  | R n -> n
  | _ -> fail $"{what}: {Print.reg r} is not an address register (r0..r7)"

// ---------------------------------------------------------------------------------------------
// Expressions and operands.

type ToExpr =
  | ToExpr
  static member ($) (ToExpr, v: int) = Num(int64 v)
  static member ($) (ToExpr, v: int64) = Num v
  static member ($) (ToExpr, v: string) = Sym v
  static member ($) (ToExpr, v: Expr) = v

/// An expression from an int, int64, symbol name or Expr.
let inline ex v : Expr = ToExpr $ v

let sym name = Sym name

/// `#v` (the assembler's choice of short or long), `#>v`, `#<v`.
let inline imm v = Operand.auto (ex v)
let inline immL v = Operand.long (ex v)
let inline immS v = Operand.short (ex v)

// ---------------------------------------------------------------------------------------------
// Effective addresses and memory references.

let ind r = Ind(rnOf "(rn)" r)
let inc r = PostInc(rnOf "(rn)+" r)
let dec r = PostDec(rnOf "(rn)-" r)
let incN r = PostIncN(rnOf "(rn)+nn" r)
let decN r = PostDecN(rnOf "(rn)-nn" r)
let idx r = IndexN(rnOf "(rn+nn)" r)
let pre r = PreDec(rnOf "-(rn)" r)
let inline disp r v = Disp(rnOf "(rn+d)" r, imm v)
let inline dispL r v = Disp(rnOf "(rn+d)" r, immL v)
/// Absolute addresses: assembler's choice, `<` short, `>` long, `<<` I/O short.
let inline abs' v = Abs(imm v)
let inline absS v = Abs(immS v)
let inline absL v = Abs(immL v)
let inline absIo v = Abs(Operand.auto (ex v) |> fun o -> { o with force = IoShort })

type Mem =
  { space : Space
    ea    : Ea }

let xm ea = { space = Space.X; ea = ea }
let ym ea = { space = Space.Y; ea = ea }
let lm ea = { space = Space.L; ea = ea }
let pm ea = { space = Space.P; ea = ea }

// ---------------------------------------------------------------------------------------------
// Moves. `move` takes register/register, memory/register, register/memory or #value/register;
// `movec` and `movem` are the same function (the encoder picks MOVEC/MOVEM from the operands).

type MoveOf =
  | MoveOf
  static member Of(_: MoveOf, s: Reg, d: Reg) = Copy(s, d)
  static member Of(_: MoveOf, s: Mem, d: Reg) = Load(s.space, s.ea, d)
  static member Of(_: MoveOf, s: Reg, d: Mem) = Store(s, d.space, d.ea)
  static member Of(_: MoveOf, s: Operand, d: Reg) = Imm(s, d)

let inline moveOf (w: ^W) (s: ^S) (d: ^D) : Move =
  ((^W or ^S or ^D) : (static member Of: ^W * ^S * ^D -> Move) (w, s, d))

let par moves = Op(Par(None, moves))

let inline move src dst = par [| moveOf MoveOf src dst |]
let inline movec src dst = move src dst
let inline movem src dst = move src dst
/// move (rn)+nn: an address register update.
let update ea = par [| Update ea |]

/// Add the moves of `rhs` to the instruction `lhs` (one of them may carry the ALU operation).
let (++) (lhs: Item) (rhs: Item) =
  match lhs, rhs with
  | Op(Par(a1, m1)), Op(Par(a2, m2)) ->
    match a1, a2 with
    | Some _, Some _ -> fail "++: two ALU operations in one instruction"
    | _ -> Op(Par(Option.orElse a2 a1, Array.append m1 m2))
  | _ -> fail $"++: {Print.item lhs} and {Print.item rhs} do not combine into one parallel instruction"

// ---------------------------------------------------------------------------------------------
// Data ALU operations (parallel instructions).

let alu op = Op(Par(Some op, [||]))

type AluSrc =
  | AluSrc
  static member Of(_: AluSrc, op: AluOp, s: Reg, d: Reg) = alu (Binary(op, s, accOf (string op) d))
  static member Of(_: AluSrc, op: AluOp, s: Operand, d: Reg) =
    let iop =
      match op with
      | AluOp.Add -> AddI | AluOp.Sub -> SubI | AluOp.Cmp -> CmpI
      | AluOp.And -> AndI | AluOp.Or  -> OrI  | AluOp.Eor -> EorI
      | o -> fail $"{o} takes no immediate operand"
    Op(AluImm(iop, s, accOf (string op) d))

let inline aluOf (w: ^W) (op: AluOp) (s: ^S) (d: Reg) : Item =
  ((^W or ^S) : (static member Of: ^W * AluOp * ^S * Reg -> Item) (w, op, s, d))

let inline add s d = aluOf AluSrc AluOp.Add s d
let inline sub s d = aluOf AluSrc AluOp.Sub s d
let inline cmp s d = aluOf AluSrc AluOp.Cmp s d
let inline and' s d = aluOf AluSrc AluOp.And s d
let inline or' s d = aluOf AluSrc AluOp.Or s d
let inline eor s d = aluOf AluSrc AluOp.Eor s d
let cmpm s d = alu (Binary(AluOp.Cmpm, s, accOf "cmpm" d))
let tfr s d = alu (Binary(AluOp.Tfr, s, accOf "tfr" d))
let adc s d = alu (Binary(AluOp.Adc, s, accOf "adc" d))
let sbc s d = alu (Binary(AluOp.Sbc, s, accOf "sbc" d))
let addl s d = alu (Binary(AluOp.Addl, s, accOf "addl" d))
let addr s d = alu (Binary(AluOp.Addr, s, accOf "addr" d))
let subl s d = alu (Binary(AluOp.Subl, s, accOf "subl" d))
let subr s d = alu (Binary(AluOp.Subr, s, accOf "subr" d))

let unary op name d = alu (Unary(op, accOf name d))
let clr d = unary AluUnary.Clr "clr" d
let tst d = unary AluUnary.Tst "tst" d
let neg d = unary AluUnary.Neg "neg" d
let abs'' d = unary AluUnary.Abs "abs" d
let not' d = unary AluUnary.Not "not" d
let rnd d = unary AluUnary.Rnd "rnd" d
let asl d = unary AluUnary.Asl "asl" d
let asr d = unary AluUnary.Asr "asr" d
let lsl d = unary AluUnary.Lsl "lsl" d
let lsr d = unary AluUnary.Lsr "lsr" d
let rol d = unary AluUnary.Rol "rol" d
let ror d = unary AluUnary.Ror "ror" d
let max' = alu Max
let maxm = alu Maxm

let mul kind neg s1 s2 d = alu (Mul(kind, neg, s1, s2, accOf (string kind) d))
let mpy s1 s2 d = mul Mpy false s1 s2 d
let mpyr s1 s2 d = mul Mpyr false s1 s2 d
let mac s1 s2 d = mul Mac false s1 s2 d
let macr s1 s2 d = mul Macr false s1 s2 d
/// mpy -s1,s2,d and friends.
let mpyn s1 s2 d = mul Mpy true s1 s2 d
let mpyrn s1 s2 d = mul Mpyr true s1 s2 d
let macn s1 s2 d = mul Mac true s1 s2 d
let macrn s1 s2 d = mul Macr true s1 s2 d

/// `alu ifcc`: execute the ALU operation only when the condition holds (`.u`: update the CCR).
let ifcc cond (i: Item) =
  match i with
  | Op(Par(Some op, [||])) -> Op(IfCc(op, cond, false))
  | _ -> fail "ifcc applies to a lone ALU operation"

let ifccU cond (i: Item) =
  match i with
  | Op(Par(Some op, [||])) -> Op(IfCc(op, cond, true))
  | _ -> fail "ifcc.u applies to a lone ALU operation"

// ---------------------------------------------------------------------------------------------
// Other arithmetic.

let shiftImm op n s d = Op(ShiftImm(op, n, accOf "shift" s, accOf "shift" d))
let shiftReg op r s d = Op(ShiftReg(op, r, accOf "shift" s, accOf "shift" d))
/// asl #n,s,d / asr #n,s,d / lsl #n,d / lsr #n,d
let asli n s d = shiftImm ShAsl n s d
let asri n s d = shiftImm ShAsr n s d
let lsli n d = shiftImm ShLsl n d d
let lsri n d = shiftImm ShLsr n d d
/// asl r,s,d / asr r,s,d / lsl r,d / lsr r,d (shift count in a register)
let aslr r s d = shiftReg ShAsl r s d
let asrr r s d = shiftReg ShAsr r s d
let lslr r d = shiftReg ShLsl r d d
let lsrr r d = shiftReg ShLsr r d d

let mpyBy s n d = Op(MulImm(Mpy, false, s, n, accOf "mpy" d))
let macBy s n d = Op(MulImm(Mac, false, s, n, accOf "mac" d))
let mpysu s1 s2 d = Op(MulSu(Mpy, false, false, s1, s2, accOf "mpysu" d))
let macsu s1 s2 d = Op(MulSu(Mac, false, false, s1, s2, accOf "macsu" d))
let mpyuu s1 s2 d = Op(MulSu(Mpy, false, true, s1, s2, accOf "mpyuu" d))
let macuu s1 s2 d = Op(MulSu(Mac, false, true, s1, s2, accOf "macuu" d))
let inline mpyi v s d = Op(MulI(Mpy, false, immL v, s, accOf "mpyi" d))
let inline maci v s d = Op(MulI(Mac, false, immL v, s, accOf "maci" d))
let div s d = Op(Div(s, accOf "div" d))
let norm r d = Op(Norm(rnOf "norm" r, accOf "norm" d))
let andi v r = Op(CtrlImm(true, v, r))
let ori v r = Op(CtrlImm(false, v, r))

/// tcc s,d / tcc s,d r,r (e.g. `tcc Cond.Lt a b` is `tlt a,b`).
let tcc cond s d = Op(Tcc(cond, Some(s, accOf "tcc" d), None))
let tccR cond s d rs rd = Op(Tcc(cond, Some(s, accOf "tcc" d), Some(rnOf "tcc" rs, rnOf "tcc" rd)))
let teq s d = tcc Cond.Eq s d
let tne s d = tcc Cond.Ne s d
let tlt s d = tcc Cond.Lt s d
let tle s d = tcc Cond.Le s d
let tgt s d = tcc Cond.Gt s d
let tge s d = tcc Cond.Ge s d

let lua ea d = Op(Lua(ea, d))

// ---------------------------------------------------------------------------------------------
// Control flow. Targets are label names, numbers or expressions.

let inline branch kind t = Op(Branch(kind, imm t))
let inline branchL kind t = Op(Branch(kind, immL t))
let inline bra t = branch Bra t
let inline bsr t = branch Bsr t
let inline bcc cond t = branch (Bcc cond) t
let inline beq t = bcc Cond.Eq t
let inline bne t = bcc Cond.Ne t
let inline blt t = bcc Cond.Lt t
let inline ble t = bcc Cond.Le t
let inline bgt t = bcc Cond.Gt t
let inline bge t = bcc Cond.Ge t
let inline bpl t = bcc Cond.Pl t
let inline bmi t = bcc Cond.Mi t
let inline jump kind t = Op(Jump(kind, imm t))
let inline jumpL kind t = Op(Jump(kind, immL t))
let inline jmp t = jump Jmp t
let inline jsr t = jump Jsr t
let inline jcc cond t = jump (Jcc cond) t
let inline jeq t = jcc Cond.Eq t
let inline jne t = jcc Cond.Ne t
let inline jlt t = jcc Cond.Lt t
let inline jge t = jcc Cond.Ge t
let jmpEa ea = Op(JumpEa(Jmp, ea))
let jsrEa ea = Op(JumpEa(Jsr, ea))
let rts = Op Rts
let rti = Op Rti
let nop = Op Nop
let illegal = Op Illegal
let stop = Op Stop
let wait = Op Wait
let reset = Op Reset
let debug = Op Debug
let trap = Op Trap
let enddo = Op Enddo

type LoopCountOf =
  | LoopCountOf
  static member ($) (LoopCountOf, c: Operand) = Count c
  static member ($) (LoopCountOf, r: Reg) = FromReg r
  static member ($) (LoopCountOf, m: Mem) = FromMem(m.space, m.ea)

/// do count,endLabel / dor count,endLabel with an explicit end label (`label endLabel` after the
/// loop body), and rep.
let inline doTo count endLabel = Op(Loop(Do, LoopCountOf $ count, imm (endLabel: string)))
let inline dorTo count endLabel = Op(Loop(Dor, LoopCountOf $ count, imm (endLabel: string)))
let inline rep count = Op(Rep(LoopCountOf $ count))

type BitTargetOf =
  | BitTargetOf
  static member ($) (BitTargetOf, m: Mem) = BitMem(m.space, m.ea)
  static member ($) (BitTargetOf, r: Reg) = BitReg r

let inline brclr bit target dest = Op(BitBranch(Brclr, bit, BitTargetOf $ target, imm dest))
let inline brset bit target dest = Op(BitBranch(Brset, bit, BitTargetOf $ target, imm dest))
let inline jclr bit target dest = Op(BitBranch(Jclr, bit, BitTargetOf $ target, imm dest))
let inline jset bit target dest = Op(BitBranch(Jset, bit, BitTargetOf $ target, imm dest))
let inline bset bit target = Op(BitOp(Bset, bit, BitTargetOf $ target))
let inline bclr bit target = Op(BitOp(Bclr, bit, BitTargetOf $ target))
let inline bchg bit target = Op(BitOp(Bchg, bit, BitTargetOf $ target))
let inline btst bit target = Op(BitOp(Btst, bit, BitTargetOf $ target))

// ---------------------------------------------------------------------------------------------
// Program structure.

let label name = Label name
let inline equ name v = Equ(name, ex v)
let comment text = Comment text
let org space address = Org(space, Num(int64 address))
let dc (words: int array) = Dc(words |> Array.map (fun w -> Num(int64 w)))
let dcE (values: Expr array) = Dc values

/// One instruction in Motorola syntax, read when the program is built: the escape hatch for a
/// form the functions above do not spell.
let asm (text: string) =
  match Parse.parseLine 0 ("        " + text) with
  | Ok { body = Parse.Instruction i } -> Op i
  | Ok _ -> fail $"asm \"{text}\": not an instruction"
  | Error e -> fail $"asm \"{text}\": {Parse.errorText e}"

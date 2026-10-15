namespace dsp56k

/// The 56-bit accumulators, where an operation needs exactly one of them.
[<RequireQualifiedAccess>]
type Acc =
  | A
  | B

/// Every register an instruction can name. Order and grouping follow the DSP56300's six-bit
/// register field (DDDDDD); `X`/`Y` are x1:x0 and y1:y0 as 48-bit ALU sources and L: operands,
/// `A10`..`BA` the other L: move pairs, `Mr`..`Eom` the targets of ANDI/ORI.
type Reg =
  | X0 | X1 | Y0 | Y1
  | A0 | B0 | A2 | B2 | A1 | B1 | A | B
  | R of n: int
  | N of n: int
  | M of n: int
  | Ep | Vba | Sc | Sz | Sr | Omr | Sp | Ssh | Ssl | La | Lc
  | X | Y
  | A10 | B10 | AB | BA
  | Mr | Ccr | Eom

[<RequireQualifiedAccess>]
type Space =
  | X
  | Y
  | L
  | P

/// Effective addresses. The seven register modes are the MMM field; `Disp` is (rn+xxx), which
/// only MOVE and LUA take; `Abs` is an absolute address (short `aa`, I/O short, or long).
type Ea =
  | PostDecN of r: int   // (rn)-nn
  | PostIncN of r: int   // (rn)+nn
  | PostDec  of r: int   // (rn)-
  | PostInc  of r: int   // (rn)+
  | Ind      of r: int   // (rn)
  | IndexN   of r: int   // (rn+nn)
  | PreDec   of r: int   // -(rn)
  | Disp     of r: int * offset: Operand
  | Abs      of address: Operand

[<RequireQualifiedAccess>]
type Cond =
  | Cc | Ge | Ne | Pl | Nn | Ec | Lc | Gt
  | Cs | Lt | Eq | Mi | Nr | Es | Ls | Le

/// Two-operand ALU operations of the parallel instruction byte.
[<RequireQualifiedAccess>]
type AluOp =
  | Add | Sub | Cmp | Cmpm | Tfr | Adc | Sbc
  | And | Or  | Eor | Addl | Addr | Subl | Subr

/// One-operand ALU operations of the parallel instruction byte.
[<RequireQualifiedAccess>]
type AluUnary =
  | Clr | Tst | Neg | Abs | Not | Rnd
  | Asl | Asr | Lsl | Lsr | Rol | Ror

type MulKind =
  | Mpy
  | Mpyr
  | Mac
  | Macr

/// The operation byte of a parallel instruction.
type Alu =
  | Binary of op: AluOp * src: Reg * dst: Acc
  | Unary  of op: AluUnary * dst: Acc
  | Mul    of kind: MulKind * neg: bool * s1: Reg * s2: Reg * dst: Acc
  | Max                    // max a,b
  | Maxm                   // maxm a,b

/// One data move. A parallel instruction carries zero, one or two; a single move the parallel
/// formats cannot hold (a control register, P: memory, (rn+xxx)) becomes MOVEC, MOVEM or the
/// long-displacement MOVE.
type Move =
  | Load   of space: Space * ea: Ea * dst: Reg
  | Store  of src: Reg * space: Space * ea: Ea
  | Copy   of src: Reg * dst: Reg
  | Imm    of value: Operand * dst: Reg
  | Update of ea: Ea                    // move (rn)+nn

type ImmOp =
  | AddI | SubI | CmpI
  | AndI | OrI  | EorI

type ShiftOp =
  | ShAsl
  | ShAsr
  | ShLsl
  | ShLsr

type LoopKind =
  | Do
  | Dor

type LoopCount =
  | Count   of value: Operand
  | FromReg of reg: Reg
  | FromMem of space: Space * ea: Ea

type BranchKind =
  | Bra
  | Bsr
  | Bcc  of cond: Cond
  | Bscc of cond: Cond

type JumpKind =
  | Jmp
  | Jsr
  | Jcc  of cond: Cond
  | Jscc of cond: Cond

type BitTest =
  | Brclr | Brset | Bsclr | Bsset
  | Jclr  | Jset  | Jsclr | Jsset

type BitOp =
  | Bset
  | Bclr
  | Bchg
  | Btst

type BitTarget =
  | BitMem of space: Space * ea: Ea
  | BitReg of reg: Reg

/// The conditional software-interrupt and debug forms (TRAPcc, DEBUGcc, BRKcc).
type CondOp =
  | Trapcc
  | Debugcc
  | Brkcc

type CacheOp =
  | Pflush
  | Pflushun
  | Pfree

/// Signed/unsigned operand pairs of DMAC.
type Signs =
  | SignedSigned
  | SignedUnsigned
  | UnsignedUnsigned

/// The non-peripheral side of a MOVEP: memory (X, Y or P) or a register.
type MovepOther =
  | MovepMem of space: Space * ea: Ea
  | MovepReg of reg: Reg
  | MovepImm of value: Operand

type LraSource =
  | LraReg    of r: int
  | LraTarget of target: Operand

/// Width/offset of EXTRACT and INSERT: a register or a #constant (extension word).
type BitField =
  | FieldReg   of reg: Reg
  | FieldConst of value: Operand

type Instr =
  | Par       of alu: Alu option * moves: Move array
  | IfCc      of alu: Alu * cond: Cond * update: bool
  | AluImm    of op: ImmOp * value: Operand * dst: Acc
  | ShiftImm  of op: ShiftOp * amount: int * src: Acc * dst: Acc
  | ShiftReg  of op: ShiftOp * amount: Reg * src: Acc * dst: Acc
  | MulImm    of kind: MulKind * neg: bool * src: Reg * shift: int * dst: Acc
  | MulSu     of kind: MulKind * neg: bool * unsignedBoth: bool * s1: Reg * s2: Reg * dst: Acc
  | MulI      of kind: MulKind * neg: bool * value: Operand * src: Reg * dst: Acc
  | Div       of src: Reg * dst: Acc
  | Norm      of r: int * dst: Acc
  | Tcc       of cond: Cond * alu: (Reg * Acc) option * agu: (int * int) option
  | CtrlImm   of andi: bool * value: int * reg: Reg
  | Loop      of kind: LoopKind * count: LoopCount * endLabel: Operand
  | Forever   of kind: LoopKind * endLabel: Operand
  | Rep       of count: LoopCount
  | Enddo
  | Branch    of kind: BranchKind * target: Operand
  | BranchRn  of kind: BranchKind * r: int
  | Jump      of kind: JumpKind * target: Operand
  | JumpEa    of kind: JumpKind * ea: Ea
  | BitBranch of kind: BitTest * bit: int * target: BitTarget * dest: Operand
  | BitOp     of kind: BitOp * bit: int * target: BitTarget
  | Lua       of ea: Ea * dst: Reg
  | Lra       of source: LraSource * dst: Reg
  | Inc       of dst: Acc
  | Dec       of dst: Acc
  | CondOp    of kind: CondOp * cond: Cond
  | Cache     of op: CacheOp
  | Plock     of lock: bool * ea: Ea                         // plock ea / punlock ea
  | PlockR    of lock: bool * target: Operand                // plockr / punlockr (PC-relative)
  | Clb       of src: Acc * dst: Acc
  | Normf     of src: Reg * dst: Acc
  | Merge     of src: Reg * dst: Acc
  | Extract   of unsignedField: bool * field: BitField * src: Acc * dst: Acc
  | Insert    of field: BitField * src: Reg * dst: Acc
  | Cmpu      of src: Reg * dst: Acc
  | Dmac      of neg: bool * signs: Signs * s1: Reg * s2: Reg * dst: Acc
  | Vsl       of src: Acc * bit: int * ea: Ea
  | Movep     of toPeripheral: bool * peripheral: Space * address: Operand * other: MovepOther
  | Rts
  | Rti
  | Nop
  | Illegal
  | Stop
  | Wait
  | Reset
  | Debug
  | Trap

/// A program: instructions, labels, data and the few directives the DSL also needs.
type Item =
  | Label   of name: string
  | Op      of instr: Instr
  | Dc      of values: Expr array
  | Ds      of count: Expr
  | Equ     of name: string * value: Expr
  | Org     of space: Space * address: Expr
  | Comment of text: string

module Acc =
  let reg acc =
    match acc with
    | Acc.A -> A
    | Acc.B -> B

  let other acc =
    match acc with
    | Acc.A -> Acc.B
    | Acc.B -> Acc.A

  let ofReg reg =
    match reg with
    | A -> Some Acc.A
    | B -> Some Acc.B
    | _ -> None

module Reg =
  /// The 24-bit data ALU input registers (the multiplier's inputs).
  let isInput reg =
    match reg with
    | X0 | X1 | Y0 | Y1 -> true
    | _ -> false

  let isAddress reg =
    match reg with
    | R _ | N _ -> true
    | _ -> false

  let isDataAlu reg =
    match reg with
    | X0 | X1 | Y0 | Y1 | A0 | B0 | A2 | B2 | A1 | B1 | A | B -> true
    | _ -> false

  /// Registers only MOVEC reaches.
  let isControl reg =
    match reg with
    | M _ | Ep | Vba | Sc | Sz | Sr | Omr | Sp | Ssh | Ssl | La | Lc -> true
    | _ -> false

module Ea =
  let register ea =
    match ea with
    | PostDecN r | PostIncN r | PostDec r | PostInc r | Ind r | IndexN r | PreDec r | Disp(r, _) -> Some r
    | Abs _ -> None

  /// Modes a parallel move can use (the MMM register modes and absolute addresses).
  let isParallel ea =
    match ea with
    | Disp _ -> false
    | _ -> true

module Cond =
  let all =
    [| Cond.Cc; Cond.Ge; Cond.Ne; Cond.Pl; Cond.Nn; Cond.Ec; Cond.Lc; Cond.Gt
       Cond.Cs; Cond.Lt; Cond.Eq; Cond.Mi; Cond.Nr; Cond.Es; Cond.Ls; Cond.Le |]

  let code cond = Array.findIndex ((=) cond) all

  let name cond = (string cond).ToLowerInvariant()

  let tryParse (text: string) =
    match text.ToLowerInvariant() with
    | "hs" -> Some Cond.Cc
    | "lo" -> Some Cond.Cs
    | t -> all |> Array.tryFind (fun c -> name c = t)

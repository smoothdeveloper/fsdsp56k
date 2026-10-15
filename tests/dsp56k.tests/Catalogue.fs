/// A broad set of instruction values, generated across operand forms, for the oracle tests.
module dsp56k.tests.Catalogue

open dsp56k

let num v = Operand.num v
let shortOp v = Operand.short (Expr.num v)
let longOp v = Operand.long (Expr.num v)

let inputs = [| X0; X1; Y0; Y1 |]
let accs = [| Acc.A; Acc.B |]
let dataRegs = [| X0; X1; Y0; Y1; A0; B0; A2; B2; A1; B1; A; B |]
let moveRegs = [| yield! dataRegs; for i in 0 .. 7 do R i; for i in 0 .. 7 do N i |]
let ctrlRegs = [| for i in 0 .. 7 do M i; Ep; Vba; Sc; Sz; Sr; Omr; Sp; Ssh; Ssl; La; Lc |]
let regEas r = [| PostDecN r; PostIncN r; PostDec r; PostInc r; Ind r; IndexN r; PreDec r |]
let conds = Cond.all

let alu =
  [| for d in accs do
       for op in [| AluOp.Add; AluOp.Tfr; AluOp.Or; AluOp.Eor; AluOp.Sub; AluOp.Cmp; AluOp.And; AluOp.Cmpm |] do
         for s in inputs do
           Binary(op, s, d)
       for op in [| AluOp.Add; AluOp.Adc; AluOp.Sub; AluOp.Sbc |] do
         for s in [| X; Y |] do
           Binary(op, s, d)
       for op in [| AluOp.Tfr; AluOp.Addr; AluOp.Cmp; AluOp.Subr; AluOp.Cmpm; AluOp.Add; AluOp.Addl; AluOp.Sub; AluOp.Subl |] do
         Binary(op, Acc.reg (Acc.other d), d)
       for op in [| AluUnary.Clr; AluUnary.Tst; AluUnary.Neg; AluUnary.Abs; AluUnary.Not; AluUnary.Rnd
                    AluUnary.Asl; AluUnary.Asr; AluUnary.Lsl; AluUnary.Lsr; AluUnary.Rol; AluUnary.Ror |] do
         Unary(op, d)
       for kind in [| Mpy; Mpyr; Mac; Macr |] do
         for neg in [| false; true |] do
           for s1, s2 in [| X0, X0; Y0, Y0; X1, X0; Y1, Y0; X0, Y1; Y0, X0; X1, Y0; Y1, X1 |] do
             Mul(kind, neg, s1, s2, d)
     Max
     Maxm |]

let singleMoves =
  [| for r in 0 .. 7 do
       for ea in regEas r do
         for reg in [| X0; A; R 3; N 5 |] do
           Load(Space.X, ea, reg)
           Store(reg, Space.Y, ea)
     for reg in moveRegs do
       Load(Space.X, Ind 0, reg)
       Store(reg, Space.Y, PostInc 4)
       Load(Space.Y, Abs(shortOp 0x21), reg)
       Store(reg, Space.X, Abs(longOp 0x1234))
       Imm(longOp 0x123456, reg)
     for reg in [| R 0; N 6; R 7 |] do
       Imm(num 0x28, reg)
     Imm(num 0x400000, X0)
     Imm(num 0, Y1)
     for s in [| X0; Y1; A; B1; R 2; N 3 |] do
       for d in [| X1; A0; B; R 5; N 0 |] do
         Copy(s, d)
     for r in 0 .. 7 do
       for ea in [| PostDecN r; PostIncN r; PostDec r; PostInc r |] do
         Update ea
     for reg in [| A10; B10; X; Y; A; B; AB; BA |] do
       Load(Space.L, PostInc 1, reg)
       Store(reg, Space.L, Abs(shortOp 0x10)) |]

let pairs =
  [| for mx in [| Ind 0; PostIncN 1; PostDec 2; PostInc 3 |] do
       for my in [| Ind 4; PostIncN 5; PostDec 6; PostInc 7 |] do
         for xr in [| X0; X1; A; B |] do
           for yr in [| Y0; Y1; A; B |] do
             [| Load(Space.X, mx, xr); Load(Space.Y, my, yr) |]
       [| Store(A, Space.X, mx); Load(Space.Y, PostInc 6, Y0) |]
     [| Load(Space.X, PostInc 4, X0); Load(Space.Y, PostInc 0, Y1) |]
     for xr in [| X0; X1; A; B |] do
       for s2 in [| A; B |] do
         for d2 in [| Y0; Y1 |] do
           [| Load(Space.X, PostInc 2, xr); Copy(s2, d2) |]
           [| Store(xr, Space.X, Ind 5); Copy(s2, d2) |]
     for s1 in [| A; B |] do
       for d1 in [| X0; X1 |] do
         for yr in [| Y0; Y1; A; B |] do
           [| Copy(s1, d1); Load(Space.Y, PostDec 3, yr) |]
           [| Copy(s1, d1); Store(yr, Space.Y, Abs(longOp 0x2000)) |]
     [| Store(A, Space.X, PostInc 1); Copy(X0, A) |]
     [| Store(B, Space.X, Ind 2); Copy(X0, B) |]
     [| Store(A, Space.Y, PostInc 5); Copy(Y0, A) |]
     [| Store(B, Space.Y, Ind 6); Copy(Y0, B) |] |]

let alone =
  [| for reg in ctrlRegs do
       Par(None, [| Imm(longOp 0xffffff, reg) |])
       Par(None, [| Imm(num 0x1f, reg) |])
       Par(None, [| Copy(X0, reg) |])
       Par(None, [| Copy(reg, A1) |])
       Par(None, [| Load(Space.X, PostInc 3, reg) |])
       Par(None, [| Store(reg, Space.Y, Abs(shortOp 5)) |])
     for reg in [| Y0; A; R 1 |] do
       Par(None, [| Load(Space.P, Ind 0, reg) |])
       Par(None, [| Store(reg, Space.P, Abs(shortOp 3)) |])
       Par(None, [| Load(Space.P, Abs(longOp 0x400), reg) |])
     for reg in dataRegs do
       for d in [| 0; 1; 0x0b; 63; -1; -64 |] do
         Par(None, [| Load(Space.Y, Disp(6, num d), reg) |])
         Par(None, [| Store(reg, Space.X, Disp(2, num d)) |])
     for reg in [| A; R 3 |] do
       Par(None, [| Load(Space.Y, Disp(6, num 200), reg) |])
       Par(None, [| Store(reg, Space.Y, Disp(1, num -300)) |]) |]

let others pc =
  [| for op in [| AddI; SubI; CmpI; AndI; OrI; EorI |] do
       for d in accs do
         AluImm(op, num 5, d)
         AluImm(op, longOp 0x123456, d)
     for s in accs do
       for d in accs do
         for n in [| 0; 1; 9; 23 |] do
           ShiftImm(ShAsl, n, s, d)
           ShiftImm(ShAsr, n, s, d)
         for r in [| A1; B1; X0; Y0; X1; Y1 |] do
           ShiftReg(ShAsl, r, s, d)
           ShiftReg(ShAsr, r, s, d)
     for d in accs do
       ShiftImm(ShLsl, 3, d, d)
       ShiftImm(ShLsr, 7, d, d)
       ShiftReg(ShLsl, X0, d, d)
       ShiftReg(ShLsr, B1, d, d)
       for kind in [| Mpy; Mpyr; Mac; Macr |] do
         for s in inputs do
           MulImm(kind, false, s, 4, d)
           MulI(kind, true, longOp 0x200000, s, d)
       for s1, s2 in [| X0, X0; Y1, X0; X0, Y0; X1, Y1 |] do
         MulSu(Mpy, false, false, s1, s2, d)
         MulSu(Mac, true, true, s1, s2, d)
       for s in inputs do
         Div(s, d)
       Norm(3, d)
     for c in conds do
       Tcc(c, Some(X0, Acc.A), None)
       Tcc(c, Some(B, Acc.A), Some(1, 2))
       Tcc(c, None, Some(4, 5))
     for r in [| Mr; Ccr; Omr; Eom |] do
       CtrlImm(true, 0xfe, r)
       CtrlImm(false, 0x03, r)
     for kind in [| Do; Dor |] do
       Loop(kind, Count(num 32), num (pc + 9))
       Loop(kind, FromReg(N 2), num (pc + 5))
       Loop(kind, FromMem(Space.Y, PostInc 3), num (pc + 4))
       Loop(kind, FromMem(Space.X, Abs(shortOp 7)), num (pc + 4))
       Forever(kind, num (pc + 6))
     Rep(Count(num 16))
     Rep(FromReg X0)
     Rep(FromMem(Space.Y, Ind 1))
     Rep(FromMem(Space.X, Abs(shortOp 2)))
     Enddo
     for kind in [| Bra; Bsr; for c in conds do Bcc c; Bscc c |] do
       Branch(kind, num (pc - 20))
       Branch(kind, longOp (pc + 3))
       BranchRn(kind, 4)
     for kind in [| Jmp; Jsr; for c in conds do Jcc c; Jscc c |] do
       Jump(kind, num 0x0042)
       Jump(kind, longOp 0x12345)
       JumpEa(kind, Ind 2)
       JumpEa(kind, PostInc 5)
     for kind in [| Brclr; Brset; Bsclr; Bsset; Jclr; Jset; Jsclr; Jsset |] do
       let dest = match kind with Brclr | Brset | Bsclr | Bsset -> num (pc + 10) | _ -> num 0x300
       BitBranch(kind, 23, BitMem(Space.X, Ind 5), dest)
       BitBranch(kind, 1, BitReg(N 3), dest)
       BitBranch(kind, 4, BitMem(Space.Y, Abs(shortOp 6)), dest)
       BitBranch(kind, 0, BitMem(Space.X, Abs(Operand.auto (Expr.num 0xffffc5))), dest)
       BitBranch(kind, 2, BitMem(Space.Y, Abs(Operand.auto (Expr.num 0xffff85))), dest)
     for kind in [| Bset; Bclr; Bchg; Btst |] do
       BitOp(kind, 7, BitMem(Space.Y, PostDec 2))
       BitOp(kind, 3, BitReg A1)
       BitOp(kind, 12, BitMem(Space.X, Abs(shortOp 9)))
       BitOp(kind, 0, BitMem(Space.X, Abs(Operand.auto (Expr.num 0xfffff8))))
       BitOp(kind, 5, BitMem(Space.Y, Abs(longOp 0x1000)))
     for r in 0 .. 7 do
       Lua(PostIncN r, R((r + 1) % 8))
       Lua(PostDec r, N 2)
       Lua(Disp(r, num 5), R 0)
       Lua(Disp(r, num -7), N 6)
     for d in accs do
       Inc d
       Dec d
       Clb(Acc.other d, d)
       for r in [| A1; B1; X0; Y0; X1; Y1 |] do
         Normf(r, d)
         Merge(r, d)
         Extract(false, FieldReg r, Acc.other d, d)
         Extract(true, FieldReg r, d, d)
       Extract(false, FieldConst(num 0x123), Acc.A, d)
       Extract(true, FieldConst(num 0x456), Acc.B, d)
       for src in [| A0; B0; X0; Y0; X1; Y1 |] do
         Insert(FieldReg X0, src, d)
         Insert(FieldConst(num 0x123), src, d)
       for s in [| X0; Y0; X1; Y1; Acc.reg (Acc.other d) |] do
         Cmpu(s, d)
       for signs in [| SignedSigned; SignedUnsigned; UnsignedUnsigned |] do
         Dmac(false, signs, X0, Y0, d)
         Dmac(true, signs, Y1, X1, d)
       Vsl(d, 1, PostInc 3)
     for c in conds do
       CondOp(Trapcc, c)
       CondOp(Debugcc, c)
       CondOp(Brkcc, c)
     Cache Pflush
     Cache Pflushun
     Cache Pfree
     Plock(true, Ind 5)
     Plock(false, PostInc 1)
     Plock(true, Abs(num 0x123))
     PlockR(true, num (pc + 0x40))
     PlockR(false, num (pc - 3))
     Lra(LraReg 7, R 5)
     Lra(LraTarget(num (pc + 9)), A1)
     for toP in [| false; true |] do
       Movep(toP, Space.Y, num 0xffffc5, MovepMem(Space.X, PostInc 2))
       Movep(toP, Space.X, num 0xffff83, MovepMem(Space.Y, Ind 1))
       Movep(toP, Space.Y, num 0xffff83, MovepMem(Space.X, Ind 1))
       Movep(toP, Space.X, num 0xffffc9, MovepMem(Space.P, Ind 6))
       Movep(toP, Space.Y, num 0xffff89, MovepMem(Space.P, Ind 6))
       Movep(toP, Space.Y, num 0xffffc2, MovepReg A)
       Movep(toP, Space.X, num 0xffffa1, MovepReg X1)
       Movep(toP, Space.Y, num 0xffff82, MovepReg(R 4))
     Rts
     Rti
     Nop
     Illegal
     Stop
     Wait
     Reset
     Debug
     Trap |]

/// Every catalogue instruction, as the oracle tests use them.
let all pc =
  [| for a in alu do
       Par(Some a, [||])
       Par(Some a, [| Load(Space.X, PostInc 0, Y1) |])
       IfCc(a, Cond.Eq, false)
       IfCc(a, Cond.Lt, true)
     for m in singleMoves do
       Par(None, [| m |])
       Par(Some(Unary(AluUnary.Clr, Acc.B)), [| m |])
     for ms in pairs do
       Par(None, ms)
       Par(Some(Mul(Mac, false, X0, Y0, Acc.A)), ms)
     yield! alone
     yield! others pc |]

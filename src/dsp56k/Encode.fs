/// Instruction words from Isa values. Each form fills a 24-bit pattern written as in the DSP56300
/// Family Manual's instruction encodings: '0'/'1' fixed bits, a letter per field, filled MSB first
/// (a field may be split, e.g. the branch displacement `aaaa0aaaaa`).
module dsp56k.Encode

type EncodeError =
  | NoForm      of what: string
  | OutOfRange  of what: string * value: int64
  | BadRegister of what: string * reg: Reg
  | BadExpr     of error: ExprError

/// What the encoder needs from its caller: where the instruction is, the value of an expression,
/// and whether that value was known when the instruction was sized (a forward reference was not,
/// and gets the long form even when the final value would fit a short one).
type Env =
  { pc      : int
    value   : Expr -> Result<int64, ExprError>
    settled : Expr -> bool }

let fill (pattern: string) (fields: (char * int) array) =
  let mutable word = 0
  for i in 0 .. 23 do
    match pattern[i] with
    | '1' -> word <- word ||| (1 <<< (23 - i))
    | _ -> ()
  for ch, value in fields do
    let positions = [| for i in 0 .. 23 do if pattern[i] = ch then 23 - i |]
    let n = positions.Length
    positions |> Array.iteri (fun k bit ->
      if (value >>> (n - 1 - k)) &&& 1 = 1 then word <- word ||| (1 <<< bit))
  word

type ResultBuilder() =
  member _.Bind(r, f) = Result.bind f r
  member _.Return x = Ok x
  member _.ReturnFrom r = r

/// `res { }`: Result sequencing for the encoders (no early return: every path ends in a value).
let res = ResultBuilder()
let mask24 (v: int64) = int (v &&& 0xffffffL)

// ---------------------------------------------------------------------------------------------
// Register fields.

/// DDDDDD: the six-bit code of every on-chip register.
let code6 reg =
  match reg with
  | X0 -> Some 4  | X1 -> Some 5  | Y0 -> Some 6  | Y1 -> Some 7
  | A0 -> Some 8  | B0 -> Some 9  | A2 -> Some 10 | B2 -> Some 11
  | A1 -> Some 12 | B1 -> Some 13 | A  -> Some 14 | B  -> Some 15
  | R n when n >= 0 && n < 8 -> Some(16 + n)
  | N n when n >= 0 && n < 8 -> Some(24 + n)
  | M n when n >= 0 && n < 8 -> Some(32 + n)
  | Ep  -> Some 42 | Vba -> Some 48 | Sc  -> Some 49 | Sz  -> Some 56 | Sr -> Some 57
  | Omr -> Some 58 | Sp  -> Some 59 | Ssh -> Some 60 | Ssl -> Some 61 | La -> Some 62
  | Lc  -> Some 63
  | _ -> None

let need what (reg: Reg) (code: int option) =
  match code with
  | Some c -> Ok c
  | None -> Error(BadRegister(what, reg))

/// ddddd: data ALU, address and offset registers (the parallel move field).
let code5 reg = code6 reg |> Option.filter (fun c -> c >= 4 && c < 32)

/// DDDD of the short-displacement MOVE: data ALU registers only.
let code4 reg = code6 reg |> Option.filter (fun c -> c >= 4 && c < 16) |> Option.map (fun c -> c &&& 15)

/// DDDDD of MOVEC: the program controller and modifier registers.
let codeCtrl reg = code6 reg |> Option.filter (fun c -> c >= 32) |> Option.map (fun c -> c - 32)

let codeL reg =
  match reg with
  | A10 -> Some 0 | B10 -> Some 1 | X -> Some 2 | Y -> Some 3
  | A -> Some 4   | B -> Some 5   | AB -> Some 6 | BA -> Some 7
  | _ -> None

let accBit acc = if acc = Acc.B then 1 else 0

/// The multiplier's eight input pairs (QQQ), either order.
let qqq s1 s2 =
  let table =
    [| (X0, X0); (Y0, Y0); (X1, X0); (Y1, Y0); (X0, Y1); (Y0, X0); (X1, Y0); (Y1, X1) |]
  match Array.tryFindIndex ((=) (s1, s2)) table with
  | Some i -> Some i
  | None -> Array.tryFindIndex ((=) (s2, s1)) table

/// The sixteen ordered pairs of MPYSU/MACSU (QQQQ).
let qqqq s1 s2 =
  [| (X0, X0); (Y0, Y0); (X1, X0); (Y1, Y0); (X0, Y1); (Y0, X0); (X1, Y0); (Y1, X1)
     (X1, X1); (Y1, Y1); (X0, X1); (Y0, Y1); (Y1, X0); (X0, Y0); (Y0, X1); (X1, Y1) |]
  |> Array.tryFindIndex ((=) (s1, s2))

// ---------------------------------------------------------------------------------------------
// The ALU byte of parallel instructions: rows by source (other accumulator, X, Y, x0, y0, x1,
// y1), columns by operation; bit 3 is the destination; 1QQQdkKK for the multiplies.

let aluByte alu =
  let byte row col d = Ok((row <<< 4) ||| (accBit d <<< 3) ||| col)
  match alu with
  | Mul(kind, neg, s1, s2, d) ->
    match qqq s1 s2 with
    | None -> Error(NoForm $"multiplier pair {s1},{s2}")
    | Some q ->
      let k = match kind with Mpy -> 0 | Mpyr -> 1 | Mac -> 2 | Macr -> 3
      Ok(0x80 ||| (q <<< 4) ||| (accBit d <<< 3) ||| ((if neg then 1 else 0) <<< 2) ||| k)
  | Max -> Ok 0x1d
  | Maxm -> Ok 0x15
  | Unary(op, d) ->
    match op with
    | AluUnary.Tst -> byte 0 3 d
    | AluUnary.Rnd -> byte 1 1 d
    | AluUnary.Clr -> byte 1 3 d
    | AluUnary.Not -> byte 1 7 d
    | AluUnary.Asr -> byte 2 2 d
    | AluUnary.Lsr -> byte 2 3 d
    | AluUnary.Abs -> byte 2 6 d
    | AluUnary.Ror -> byte 2 7 d
    | AluUnary.Asl -> byte 3 2 d
    | AluUnary.Lsl -> byte 3 3 d
    | AluUnary.Neg -> byte 3 6 d
    | AluUnary.Rol -> byte 3 7 d
  | Binary(op, src, d) ->
    let other = Acc.reg (Acc.other d)
    let input row =
      match op with
      | AluOp.Add  -> byte row 0 d
      | AluOp.Tfr  -> byte row 1 d
      | AluOp.Or   -> byte row 2 d
      | AluOp.Eor  -> byte row 3 d
      | AluOp.Sub  -> byte row 4 d
      | AluOp.Cmp  -> byte row 5 d
      | AluOp.And  -> byte row 6 d
      | AluOp.Cmpm -> byte row 7 d
      | _ -> Error(NoForm $"{op} {src}")
    match src with
    | X0 -> input 4
    | Y0 -> input 5
    | X1 -> input 6
    | Y1 -> input 7
    | X | Y ->
      let row = if src = X then 2 else 3
      match op with
      | AluOp.Add -> byte row 0 d
      | AluOp.Adc -> byte row 1 d
      | AluOp.Sub -> byte row 4 d
      | AluOp.Sbc -> byte row 5 d
      | _ -> Error(NoForm $"{op} {src}")
    | s when s = other ->
      match op with
      | AluOp.Tfr  -> byte 0 1 d
      | AluOp.Addr -> byte 0 2 d
      | AluOp.Cmp  -> byte 0 5 d
      | AluOp.Subr -> byte 0 6 d
      | AluOp.Cmpm -> byte 0 7 d
      | AluOp.Add  -> byte 1 0 d
      | AluOp.Addl -> byte 1 2 d
      | AluOp.Sub  -> byte 1 4 d
      | AluOp.Subl -> byte 1 6 d
      | _ -> Error(NoForm $"{op} {src}")
    | s -> Error(BadRegister($"{op} source", s))

// ---------------------------------------------------------------------------------------------
// Operands.

let value env (op: Operand) = env.value op.expr |> Result.mapError BadExpr

/// A value known when sizing, inside lo..hi, and not forced long: the short form applies.
let fitsShort env (op: Operand) lo hi =
  match op.force with
  | Long -> false
  | Short | IoShort -> true
  | Auto ->
    env.settled op.expr
    && (match env.value op.expr with
        | Ok v -> v >= lo && v <= hi
        | Error _ -> false)

let checkRange what lo hi (v: int64) =
  if v >= lo && v <= hi then Ok(int v) else Error(OutOfRange(what, v))

/// MMMRRR for the register modes; absolute long and immediate take an extension word.
let mmmrrr ea =
  match ea with
  | PostDecN r -> Ok(0b000 <<< 3 ||| r)
  | PostIncN r -> Ok(0b001 <<< 3 ||| r)
  | PostDec r  -> Ok(0b010 <<< 3 ||| r)
  | PostInc r  -> Ok(0b011 <<< 3 ||| r)
  | Ind r      -> Ok(0b100 <<< 3 ||| r)
  | IndexN r   -> Ok(0b101 <<< 3 ||| r)
  | PreDec r   -> Ok(0b111 <<< 3 ||| r)
  | Abs _      -> Ok 0b110000
  | Disp _     -> Error(NoForm "(rn+xxx) in this instruction")

let immMode = 0b110100

/// The extension word an ea brings (an absolute long address), if any.
let eaExt env ea =
  match ea with
  | Abs a -> value env a |> Result.map (fun v -> [| mask24 v |])
  | _ -> Ok [||]

/// MM of the two-bit update modes (MOVE ea, LUA ea, X:Y moves use their own order).
let mm2 ea =
  match ea with
  | PostDecN r -> Ok(0, r)
  | PostIncN r -> Ok(1, r)
  | PostDec r  -> Ok(2, r)
  | PostInc r  -> Ok(3, r)
  | _ -> Error(NoForm "address update mode")

let mmXy ea =
  match ea with
  | Ind r      -> Ok(0, r)
  | PostIncN r -> Ok(1, r)
  | PostDec r  -> Ok(2, r)
  | PostInc r  -> Ok(3, r)
  | _ -> Error(NoForm "X:Y move address mode")

let spaceBit space =
  match space with
  | Space.Y -> Ok 1
  | Space.X -> Ok 0
  | s -> Error(NoForm $"{s}: memory here")

// ---------------------------------------------------------------------------------------------
// Moves.

type MoveWord =
  | Parallel of word: int * ext: int array        // the ALU byte goes in bits 7..0
  | Alone    of words: int array                  // a non-parallel MOVE/MOVEC/MOVEM

/// A short immediate is the low byte of rn/nn but the top byte of a data ALU register:
/// `move #$08,x1` leaves x1 = $080000 (the Motorola semantics; write `#>8` for $000008). A
/// value of 0..255 is that byte; a value whose low 16 bits are clear is short too, its top byte.
let shortImmByte (x: int64) =
  if x >= 0L && x <= 255L then Some(int x)
  elif x &&& 0xffffL = 0L && (x >>> 16) >= -128L && (x >>> 16) <= 255L then Some(int ((x >>> 16) &&& 0xffL))
  else None

let shortImm env (v: Operand) dst =
  if Reg.isDataAlu dst then
    match v.force with
    | Short -> true
    | Long | IoShort -> false
    | Auto ->
      env.settled v.expr
      && (match env.value v.expr with
          | Ok x -> (shortImmByte x).IsSome
          | Error _ -> false)
  else fitsShort env v 0L 255L

let shortImmValue env (v: Operand) dst =
  value env v |> Result.map (fun x ->
    if Reg.isDataAlu dst then defaultArg (shortImmByte x) (int (x &&& 0xffL)) else int (x &&& 0xffL))

let rec singleMove env move =
  match move with
  | Imm(v, dst) when Reg.isControl dst ->
    res {
      let! d = need "movec" dst (codeCtrl dst)
      if fitsShort env v 0L 255L then
        let! x = value env v
        return Alone [| fill "00000101iiiiiiii101DDDDD" [| 'i', int x; 'D', d |] |]
      else
        let! x = value env v
        return Alone [| fill "00000101W1MMMRRR0S1DDDDD" [| 'W', 1; 'M', immMode >>> 3; 'R', immMode &&& 7; 'D', d |]; mask24 x |]
    }
  | Imm(v, dst) ->
    res {
      let! d = need "move #" dst (code5 dst)
      if shortImm env v dst then
        let! x = shortImmValue env v dst
        return Parallel(fill "001dddddiiiiiiii00000000" [| 'd', d; 'i', x |], [||])
      else
        let! x = value env v
        return Parallel(fill "01dd0dddW1MMMRRR00000000" [| 'd', d; 'W', 1; 'M', 6; 'R', 4 |], [| mask24 x |])
    }
  | Copy(s, d) when Reg.isControl s || Reg.isControl d ->
    res {
      // Between two control registers the source takes the DDDDD field (W=0).
      let toCtrl = Reg.isControl d && not (Reg.isControl s)
      let ctrl, other = if toCtrl then d, s else s, d
      let! c = need "movec" ctrl (codeCtrl ctrl)
      let! o = need "movec" other (code6 other)
      return Alone [| fill "00000100W1eeeeee101DDDDD" [| 'W', (if toCtrl then 1 else 0); 'e', o; 'D', c |] |]
    }
  | Copy(s, d) ->
    res {
      let! e = need "move source" s (code5 s)
      let! dd = need "move destination" d (code5 d)
      return Parallel(fill "001000eeeeeddddd00000000" [| 'e', e; 'd', dd |], [||])
    }
  | Update ea ->
    res {
      let! m, r = mm2 ea
      return Parallel(fill "00100000010MMRRR00000000" [| 'M', m; 'R', r |], [||])
    }
  | Load(Space.P, ea, reg) | Store(reg, Space.P, ea) ->
    let w = if move.IsLoad then 1 else 0
    res {
      let! d = need "movem" reg (code6 reg)
      match ea with
      | Abs a when fitsShort env a 0L 63L ->
        let! x = value env a
        return Alone [| fill "00000111W0aaaaaa00dddddd" [| 'W', w; 'a', int x; 'd', d |] |]
      | _ ->
        let! m = mmmrrr ea
        let! ext = eaExt env ea
        return Alone [| fill "00000111W1MMMRRR10dddddd" [| 'W', w; 'M', m >>> 3; 'R', m &&& 7; 'd', d |]; yield! ext |]
    }
  | Load(space, ea, reg) | Store(reg, space, ea) when Reg.isControl reg && not (match ea with Disp _ -> true | _ -> false) ->
    let w = if move.IsLoad then 1 else 0
    res {
      let! d = need "movec" reg (codeCtrl reg)
      let! s = spaceBit space
      match ea with
      | Abs a when fitsShort env a 0L 63L ->
        let! x = value env a
        return Alone [| fill "00000101W0aaaaaa0S1DDDDD" [| 'W', w; 'a', int x; 'S', s; 'D', d |] |]
      | _ ->
        let! m = mmmrrr ea
        let! ext = eaExt env ea
        return Alone [| fill "00000101W1MMMRRR0S1DDDDD" [| 'W', w; 'M', m >>> 3; 'R', m &&& 7; 'S', s; 'D', d |]; yield! ext |]
    }
  | Load(space, Disp(r, off), reg) | Store(reg, space, Disp(r, off)) ->
    let w = if move.IsLoad then 1 else 0
    res {
      let! s = spaceBit space
      let! x = value env off
      match code4 reg with
      | Some d when fitsShort env off -64L 63L ->
        let! disp = checkRange "displacement" -64L 63L x
        let pattern = if s = 1 then "0000001aaaaaaRRR1a1WDDDD" else "0000001aaaaaaRRR1a0WDDDD"
        return Alone [| fill pattern [| 'a', disp &&& 0x7f; 'R', r; 'W', w; 'D', d |] |]
      | _ ->
        let! d = need "move (rn+xxxx)" reg (code6 reg)
        let pattern = if s = 1 then "0000101101110RRR1WDDDDDD" else "0000101001110RRR1WDDDDDD"
        return Alone [| fill pattern [| 'R', r; 'W', w; 'D', d |]; mask24 x |]
    }
  | Load(Space.L, ea, reg) | Store(reg, Space.L, ea) ->
    let w = if move.IsLoad then 1 else 0
    res {
      let! l = need "move l:" reg (codeL reg)
      match ea with
      | Abs a when fitsShort env a 0L 63L ->
        let! x = value env a
        return Parallel(fill "0100L0LLW0aaaaaa00000000" [| 'L', l; 'W', w; 'a', int x |], [||])
      | _ ->
        let! m = mmmrrr ea
        let! ext = eaExt env ea
        return Parallel(fill "0100L0LLW1MMMRRR00000000" [| 'L', l; 'W', w; 'M', m >>> 3; 'R', m &&& 7 |], ext)
    }
  | Load(space, ea, reg) | Store(reg, space, ea) ->
    let w = if move.IsLoad then 1 else 0
    res {
      let! s = spaceBit space
      let! d = need "move" reg (code5 reg)
      let pattern, short =
        if s = 1 then "01dd1dddW1MMMRRR00000000", "01dd1dddW0aaaaaa00000000"
        else "01dd0dddW1MMMRRR00000000", "01dd0dddW0aaaaaa00000000"
      match ea with
      | Abs a when fitsShort env a 0L 63L ->
        let! x = value env a
        return Parallel(fill short [| 'd', d; 'W', w; 'a', int x |], [||])
      | _ ->
        let! m = mmmrrr ea
        let! ext = eaExt env ea
        return Parallel(fill pattern [| 'd', d; 'W', w; 'M', m >>> 3; 'R', m &&& 7 |], ext)
    }

/// Two moves in one word: X:Y, X memory + register, register + Y memory, and the two
/// accumulator forms `a,x:ea x0,a` and `y0,a a,y:ea`.
and pairMove env m1 m2 =
  let memSide move =
    match move with
    | Load(Space.X, ea, r) -> Some(Space.X, 1, ea, r)
    | Store(r, Space.X, ea) -> Some(Space.X, 0, ea, r)
    | Load(Space.Y, ea, r) -> Some(Space.Y, 1, ea, r)
    | Store(r, Space.Y, ea) -> Some(Space.Y, 0, ea, r)
    | _ -> None
  let ee r = match r with X0 -> Some 0 | X1 -> Some 1 | A -> Some 2 | B -> Some 3 | _ -> None
  let ff r = match r with Y0 -> Some 0 | Y1 -> Some 1 | A -> Some 2 | B -> Some 3 | _ -> None
  match memSide m1, memSide m2, m1, m2 with
  | Some(Space.X, wx, ex, rx), Some(Space.Y, wy, ey, ry), _, _ ->
    res {
      let! mx, nx = mmXy ex
      let! my, ny = mmXy ey
      let! _ = if nx / 4 = ny / 4 then Error(NoForm "X:Y move through one register bank") else Ok()
      let! e = need "X:Y move X register" rx (ee rx)
      let! f = need "X:Y move Y register" ry (ff ry)
      return Parallel(fill "1wmmeeffWrrMMRRR00000000" [| 'w', wy; 'm', my; 'e', e; 'f', f; 'W', wx; 'r', ny &&& 3; 'M', mx; 'R', nx |], [||])
    }
  // a,x:ea x0,a  and  b,x:ea x0,b
  | _, _, Store(acc1, Space.X, ea), Copy(X0, acc2) when acc1 = acc2 && (acc1 = A || acc1 = B) ->
    res {
      let! m = mmmrrr ea
      let! ext = eaExt env ea
      return Parallel(fill "0000100d00MMMRRR00000000" [| 'd', (if acc1 = B then 1 else 0); 'M', m >>> 3; 'R', m &&& 7 |], ext)
    }
  // a,y:ea y0,a  and  b,y:ea y0,b (either order)
  | _, _, Copy(Y0, acc1), Store(acc2, Space.Y, ea)
  | _, _, Store(acc2, Space.Y, ea), Copy(Y0, acc1) when acc1 = acc2 && (acc1 = A || acc1 = B) ->
    res {
      let! m = mmmrrr ea
      let! ext = eaExt env ea
      return Parallel(fill "0000100d10MMMRRR00000000" [| 'd', (if acc1 = B then 1 else 0); 'M', m >>> 3; 'R', m &&& 7 |], ext)
    }
  // X memory (or #xxxx) + accumulator to y0/y1
  | _, _, (Load(Space.X, _, _) | Store(_, Space.X, _) | Imm _), Copy(s2, d2) ->
    res {
      let! d = need "X:R move source" s2 (match s2 with A -> Some 0 | B -> Some 1 | _ -> None)
      let! f = need "X:R move destination" d2 (match d2 with Y0 -> Some 0 | Y1 -> Some 1 | _ -> None)
      match m1 with
      | Imm(v, d1) ->
        let! e = need "X:R move register" d1 (ee d1)
        let! x = value env v
        return Parallel(fill "0001ffdFW0MMMRRR00000000" [| 'f', e; 'd', d; 'F', f; 'W', 1; 'M', 6; 'R', 4 |], [| mask24 x |])
      | _ ->
        let wx, ea, rx = match m1 with Load(_, ea, r) -> 1, ea, r | Store(r, _, ea) -> 0, ea, r | _ -> 0, Ind 0, X0
        let! e = need "X:R move register" rx (ee rx)
        let! m = mmmrrr ea
        let! ext = eaExt env ea
        return Parallel(fill "0001ffdFW0MMMRRR00000000" [| 'f', e; 'd', d; 'F', f; 'W', wx; 'M', m >>> 3; 'R', m &&& 7 |], ext)
    }
  // accumulator to x0/x1 + Y memory (or #xxxx)
  | _, _, Copy(s1, d1), (Load(Space.Y, _, _) | Store(_, Space.Y, _) | Imm _) ->
    res {
      let! d = need "R:Y move source" s1 (match s1 with A -> Some 0 | B -> Some 1 | _ -> None)
      let! e = need "R:Y move destination" d1 (match d1 with X0 -> Some 0 | X1 -> Some 1 | _ -> None)
      match m2 with
      | Imm(v, d2) ->
        let! f = need "R:Y move register" d2 (ff d2)
        let! x = value env v
        return Parallel(fill "0001deffW1MMMRRR00000000" [| 'd', d; 'e', e; 'f', f; 'W', 1; 'M', 6; 'R', 4 |], [| mask24 x |])
      | _ ->
        let wy, ea, ry = match m2 with Load(_, ea, r) -> 1, ea, r | Store(r, _, ea) -> 0, ea, r | _ -> 0, Ind 0, Y0
        let! f = need "R:Y move register" ry (ff ry)
        let! m = mmmrrr ea
        let! ext = eaExt env ea
        return Parallel(fill "0001deffW1MMMRRR00000000" [| 'd', d; 'e', e; 'f', f; 'W', wy; 'M', m >>> 3; 'R', m &&& 7 |], ext)
    }
  | _ -> Error(NoForm "this pair of moves")

// ---------------------------------------------------------------------------------------------
// Instructions.

let condCode c = Cond.code c

/// sss / SSS: a1, b1, x0, y0, x1, y1.
let sss r = match r with A1 -> Some 2 | B1 -> Some 3 | X0 -> Some 4 | Y0 -> Some 5 | X1 -> Some 6 | Y1 -> Some 7 | _ -> None

/// qqq of INSERT's source: a0, b0, x0, y0, x1, y1.
let qqqInsert r = match r with A0 -> Some 2 | B0 -> Some 3 | X0 -> Some 4 | Y0 -> Some 5 | X1 -> Some 6 | Y1 -> Some 7 | _ -> None

let relative env (target: Operand) =
  value env target |> Result.map (fun t -> t - int64 env.pc)

let loopCountForm env count =
  // DO/DOR/REP share their count forms: #xxx (12 bits), a register, X:/Y: memory.
  match count with
  | Count v ->
    res {
      let! x = value env v
      let! n = checkRange "loop count" 0L 4095L x
      return "imm", [| 'i', n &&& 0xff; 'h', n >>> 8 |], [||]
    }
  | FromReg r ->
    res {
      let! d = need "loop count register" r (code6 r)
      return "reg", [| 'd', d; 'D', d |], [||]
    }
  | FromMem(space, Abs a) when fitsShort env a 0L 63L ->
    res {
      let! s = spaceBit space
      let! x = value env a
      return "aa", [| 'a', int x; 'S', s |], [||]
    }
  | FromMem(space, ea) ->
    res {
      let! s = spaceBit space
      let! m = mmmrrr ea
      let! ext = eaExt env ea
      let! _ = if ext.Length > 0 then Error(NoForm "loop count at a long address") else Ok()
      return "ea", [| 'M', m >>> 3; 'R', m &&& 7; 'S', s |], ext
    }

let bitTarget env (target: BitTarget) =
  // ea, aa (0..63), pp ($ffffc0..), qq ($ffff80..$ffffbf) or a register.
  match target with
  | BitReg r ->
    res {
      let! d = need "bit register" r (code6 r)
      return "S", [| 'D', d |], [||]
    }
  | BitMem(space, Abs a) ->
    res {
      let! s = spaceBit space
      let! x = value env a
      let v = x &&& 0xffffffL
      if a.force <> Long && env.settled a.expr && v >= 0xffffc0L then
        return "pp", [| 'p', int (v - 0xffffc0L); 'S', s |], [||]
      elif a.force <> Long && env.settled a.expr && v >= 0xffff80L && v < 0xffffc0L then
        return "qq", [| 'q', int (v - 0xffff80L); 'S', s |], [||]
      elif fitsShort env a 0L 63L then
        return "aa", [| 'a', int x; 'S', s |], [||]
      else
        return "ea", [| 'M', 6; 'R', 0; 'S', s |], [| mask24 x |]
    }
  | BitMem(space, ea) ->
    res {
      let! s = spaceBit space
      let! m = mmmrrr ea
      return "ea", [| 'M', m >>> 3; 'R', m &&& 7; 'S', s |], [||]
    }

let encode (env: Env) (instr: Instr) : Result<int array, EncodeError> =
  let word pattern fields = Ok [| fill pattern fields |]
  match instr with
  | Par(alu, moves) ->
    res {
      let! byte =
        match alu with
        | Some a -> aluByte a
        | None -> Ok 0
      match moves with
      | [||] ->
        return [| fill "001000000000000000000000" [||] ||| byte |]
      | [| m |] ->
        let! mw = singleMove env m
        match mw with
        | Parallel(w, ext) -> return [| w ||| byte; yield! ext |]
        | Alone words ->
          let! _ = if alu.IsSome then Error(NoForm "this move alongside an ALU operation") else Ok()
          return words
      | [| m1; m2 |] ->
        let! mw = pairMove env m1 m2
        match mw with
        | Parallel(w, ext) -> return [| w ||| byte; yield! ext |]
        | Alone words -> return words
      | _ -> return! Error(NoForm "more than two moves")
    }
  | IfCc(alu, cond, update) ->
    res {
      let! byte = aluByte alu
      let pattern = if update then "001000000011CCCC00000000" else "001000000010CCCC00000000"
      return [| fill pattern [| 'C', condCode cond |] ||| byte |]
    }
  | AluImm(op, v, d) ->
    let c = match op with AddI -> 0 | SubI -> 4 | CmpI -> 5 | AndI -> 6 | OrI -> 2 | EorI -> 3
    res {
      let! x = value env v
      if fitsShort env v 0L 63L then
        return [| fill "0000000101iiiiii1000dccc" [| 'i', int x; 'd', accBit d; 'c', c |] |]
      else
        return [| fill "00000001010000001100dccc" [| 'd', accBit d; 'c', c |]; mask24 x |]
    }
  | ShiftImm(op, n, s, d) ->
    res {
      match op with
      | ShAsl | ShAsr ->
        let! n = checkRange "shift" 0L 63L (int64 n)
        let pattern = if op = ShAsl then "0000110000011101SiiiiiiD" else "0000110000011100SiiiiiiD"
        return [| fill pattern [| 'S', accBit s; 'i', n; 'D', accBit d |] |]
      | ShLsl | ShLsr ->
        let! n = checkRange "shift" 0L 31L (int64 n)
        let! _ = if s <> d then Error(NoForm "LSL/LSR #n with two accumulators") else Ok()
        let pattern = if op = ShLsl then "000011000001111010iiiiiD" else "000011000001111011iiiiiD"
        return [| fill pattern [| 'i', n; 'D', accBit d |] |]
    }
  | ShiftReg(op, amount, s, d) ->
    res {
      let! a = need "shift amount" amount (sss amount)
      match op with
      | ShAsl -> return [| fill "0000110000011110010SsssD" [| 'S', accBit s; 's', a; 'D', accBit d |] |]
      | ShAsr -> return [| fill "0000110000011110011SsssD" [| 'S', accBit s; 's', a; 'D', accBit d |] |]
      | ShLsl -> return [| fill "00001100000111100001sssD" [| 's', a; 'D', accBit d |] |]
      | ShLsr -> return [| fill "00001100000111100011sssD" [| 's', a; 'D', accBit d |] |]
    }
  | MulImm(kind, neg, src, shift, d) ->
    let qq r = match r with Y1 -> Some 0 | X0 -> Some 1 | Y0 -> Some 2 | X1 -> Some 3 | _ -> None
    res {
      let! q = need "multiply source" src (qq src)
      let! n = checkRange "multiply shift" 0L 31L (int64 shift)
      let k = match kind with Mpy -> 0 | Mpyr -> 1 | Mac -> 2 | Macr -> 3
      return [| fill "00000001000sssss11QQdkKK" [| 's', n; 'Q', q; 'd', accBit d; 'k', (if neg then 1 else 0); 'K', k |] |]
    }
  | MulSu(kind, neg, uu, s1, s2, d) ->
    res {
      let! q =
        match qqqq s1 s2 with
        | Some q -> Ok q
        | None -> Error(NoForm $"su/uu pair {s1},{s2}")
      let! pattern =
        match kind with
        | Mpy -> Ok "00000001001001111sdkQQQQ"
        | Mac -> Ok "00000001001001101sdkQQQQ"
        | _ -> Error(NoForm "rounding su/uu multiply")
      return [| fill pattern [| 's', (if uu then 1 else 0); 'd', accBit d; 'k', (if neg then 1 else 0); 'Q', q |] |]
    }
  | MulI(kind, neg, v, src, d) ->
    let qq r = match r with X0 -> Some 0 | Y0 -> Some 1 | X1 -> Some 2 | Y1 -> Some 3 | _ -> None
    res {
      let! q = need "multiply source" src (qq src)
      let! x = value env v
      let k = match kind with Mpy -> 0 | Mpyr -> 1 | Mac -> 2 | Macr -> 3
      return [| fill "000000010100000111qqdkKK" [| 'q', q; 'd', accBit d; 'k', (if neg then 1 else 0); 'K', k |]; mask24 x |]
    }
  | Div(src, d) ->
    let jj r = match r with X0 -> Some 0 | Y0 -> Some 1 | X1 -> Some 2 | Y1 -> Some 3 | _ -> None
    res {
      let! j = need "div source" src (jj src)
      return [| fill "000000011000000001JJd000" [| 'J', j; 'd', accBit d |] |]
    }
  | Norm(r, d) -> word "0000000111011RRR0001d101" [| 'R', r; 'd', accBit d |]
  | Tcc(cond, alu, agu) ->
    let jjj (s, d) =
      match s with
      | X0 -> Some 4 | Y0 -> Some 5 | X1 -> Some 6 | Y1 -> Some 7
      | s when Some s = Some(Acc.reg (Acc.other d)) -> Some 0
      | _ -> None
    res {
      let c = condCode cond
      match alu, agu with
      | Some(s, d), None ->
        let! j = need "tcc source" s (jjj (s, d))
        return [| fill "00000010CCCC00000JJJd000" [| 'C', c; 'J', j; 'd', accBit d |] |]
      | Some(s, d), Some(t, tt) ->
        let! j = need "tcc source" s (jjj (s, d))
        return [| fill "00000011CCCC0ttt0JJJdTTT" [| 'C', c; 't', t; 'J', j; 'd', accBit d; 'T', tt |] |]
      | None, Some(t, tt) -> return [| fill "00000010CCCC1ttt00000TTT" [| 'C', c; 't', t; 'T', tt |] |]
      | None, None -> return! Error(NoForm "tcc without transfer")
    }
  | CtrlImm(andi, v, reg) ->
    let ee = match reg with Mr -> Some 0 | Ccr -> Some 1 | Omr -> Some 2 | Eom -> Some 3 | _ -> None
    res {
      let! e = need "andi/ori register" reg ee
      let! v = checkRange "andi/ori value" 0L 255L (int64 v)
      let pattern = if andi then "00000000iiiiiiii101110EE" else "00000000iiiiiiii111110EE"
      return [| fill pattern [| 'i', v; 'E', e |] |]
    }
  | Loop(kind, count, endLabel) ->
    res {
      let! form, fields, ext = loopCountForm env count
      let! target = value env endLabel
      // DO holds the absolute address of the loop's last word, DOR its distance from the DOR.
      let la = if kind = Do then mask24 (target - 1L) else mask24 (target - 1L - int64 env.pc)
      let pattern =
        match kind, form with
        | Do , "imm" -> "00000110iiiiiiii1000hhhh"
        | Do , "reg" -> "0000011011DDDDDD00000000"
        | Do , "aa"  -> "0000011000aaaaaa0S000000"
        | Do , _     -> "0000011001MMMRRR0S000000"
        | Dor, "imm" -> "00000110iiiiiiii1001hhhh"
        | Dor, "reg" -> "0000011011DDDDDD00010000"
        | Dor, "aa"  -> "0000011000aaaaaa0S010000"
        | Dor, _     -> "0000011001MMMRRR0S010000"
      return [| fill pattern fields; yield! ext; la |]
    }
  | Forever(kind, endLabel) ->
    res {
      let! target = value env endLabel
      let la = if kind = Do then mask24 (target - 1L) else mask24 (target - 1L - int64 env.pc)
      let pattern = if kind = Do then "000000000000001000000011" else "000000000000001000000010"
      return [| fill pattern [||]; la |]
    }
  | Rep count ->
    res {
      let! form, fields, ext = loopCountForm env count
      let pattern =
        match form with
        | "imm" -> "00000110iiiiiiii1010hhhh"
        | "reg" -> "0000011011dddddd00100000"
        | "aa"  -> "0000011000aaaaaa0S100000"
        | _     -> "0000011001MMMRRR0S100000"
      return [| fill pattern fields; yield! ext |]
    }
  | Enddo -> word "000000000000000010001100" [||]
  | Branch(kind, target) ->
    res {
      let! disp = relative env target
      let shortOk = fitsShort env { target with expr = Bin(BinOp.Sub, target.expr, Expr.num env.pc) } -256L 255L
      let d9 = int disp &&& 0x1ff
      match kind, shortOk with
      | Bra, true     -> return [| fill "00000101000011aaaa0aaaaa" [| 'a', d9 |] |]
      | Bsr, true     -> return [| fill "00000101000010aaaa0aaaaa" [| 'a', d9 |] |]
      | Bcc c, true   -> return [| fill "00000101CCCC01aaaa0aaaaa" [| 'C', condCode c; 'a', d9 |] |]
      | Bscc c, true  -> return [| fill "00000101CCCC00aaaa0aaaaa" [| 'C', condCode c; 'a', d9 |] |]
      | Bra, false    -> return [| fill "000011010001000011000000" [||]; mask24 disp |]
      | Bsr, false    -> return [| fill "000011010001000010000000" [||]; mask24 disp |]
      | Bcc c, false  -> return [| fill "00001101000100000100CCCC" [| 'C', condCode c |]; mask24 disp |]
      | Bscc c, false -> return [| fill "00001101000100000000CCCC" [| 'C', condCode c |]; mask24 disp |]
    }
  | BranchRn(kind, r) ->
    match kind with
    | Bra    -> word "0000110100011RRR11000000" [| 'R', r |]
    | Bsr    -> word "0000110100011RRR10000000" [| 'R', r |]
    | Bcc c  -> word "0000110100011RRR0100CCCC" [| 'R', r; 'C', condCode c |]
    | Bscc c -> word "0000110100011RRR0000CCCC" [| 'R', r; 'C', condCode c |]
  | Jump(kind, target) ->
    res {
      let! t = value env target
      if fitsShort env target 0L 4095L then
        let a = int t
        match kind with
        | Jmp    -> return [| fill "000011000000aaaaaaaaaaaa" [| 'a', a |] |]
        | Jsr    -> return [| fill "000011010000aaaaaaaaaaaa" [| 'a', a |] |]
        | Jcc c  -> return [| fill "00001110CCCCaaaaaaaaaaaa" [| 'C', condCode c; 'a', a |] |]
        | Jscc c -> return [| fill "00001111CCCCaaaaaaaaaaaa" [| 'C', condCode c; 'a', a |] |]
      else
        let m = 0b110000
        match kind with
        | Jmp    -> return [| fill "0000101011MMMRRR10000000" [| 'M', m >>> 3; 'R', 0 |]; mask24 t |]
        | Jsr    -> return [| fill "0000101111MMMRRR10000000" [| 'M', m >>> 3; 'R', 0 |]; mask24 t |]
        | Jcc c  -> return [| fill "0000101011MMMRRR1010CCCC" [| 'M', m >>> 3; 'R', 0; 'C', condCode c |]; mask24 t |]
        | Jscc c -> return [| fill "0000101111MMMRRR1010CCCC" [| 'M', m >>> 3; 'R', 0; 'C', condCode c |]; mask24 t |]
    }
  | JumpEa(kind, ea) ->
    res {
      let! m = mmmrrr ea
      let! ext = eaExt env ea
      let fields = [| 'M', m >>> 3; 'R', m &&& 7 |]
      match kind with
      | Jmp    -> return [| fill "0000101011MMMRRR10000000" fields; yield! ext |]
      | Jsr    -> return [| fill "0000101111MMMRRR10000000" fields; yield! ext |]
      | Jcc c  -> return [| fill "0000101011MMMRRR1010CCCC" [| yield! fields; 'C', condCode c |]; yield! ext |]
      | Jscc c -> return [| fill "0000101111MMMRRR1010CCCC" [| yield! fields; 'C', condCode c |]; yield! ext |]
    }
  | BitBranch(kind, bit, target, dest) ->
    res {
      let! form, fields, ext = bitTarget env target
      let! b = checkRange "bit number" 0L 31L (int64 bit)
      let! d = value env dest
      let pc = int64 env.pc
      let pattern =
        match kind, form with
        | Brclr, "ea" -> "0000110010MMMRRR0S0bbbbb" | Brclr, "aa" -> "0000110010aaaaaa1S0bbbbb"
        | Brclr, "pp" -> "0000110011pppppp0S0bbbbb" | Brclr, "qq" -> "0000010010qqqqqq0S0bbbbb"
        | Brclr, _    -> "0000110011DDDDDD100bbbbb"
        | Brset, "ea" -> "0000110010MMMRRR0S1bbbbb" | Brset, "aa" -> "0000110010aaaaaa1S1bbbbb"
        | Brset, "pp" -> "0000110011pppppp0S1bbbbb" | Brset, "qq" -> "0000010010qqqqqq0S1bbbbb"
        | Brset, _    -> "0000110011DDDDDD101bbbbb"
        | Bsclr, "ea" -> "0000110110MMMRRR0S0bbbbb" | Bsclr, "aa" -> "0000110110aaaaaa1S0bbbbb"
        | Bsclr, "pp" -> "0000110111pppppp0S0bbbbb" | Bsclr, "qq" -> "0000010010qqqqqq1S0bbbbb"
        | Bsclr, _    -> "0000110111DDDDDD100bbbbb"
        | Bsset, "ea" -> "0000110110MMMRRR0S1bbbbb" | Bsset, "aa" -> "0000110110aaaaaa1S1bbbbb"
        | Bsset, "pp" -> "0000110111pppppp0S1bbbbb" | Bsset, "qq" -> "0000010010qqqqqq1S1bbbbb"
        | Bsset, _    -> "0000110111DDDDDD101bbbbb"
        | Jclr , "ea" -> "0000101001MMMRRR1S0bbbbb" | Jclr , "aa" -> "0000101000aaaaaa1S0bbbbb"
        | Jclr , "pp" -> "0000101010pppppp1S0bbbbb" | Jclr , "qq" -> "0000000110qqqqqq1S0bbbbb"
        | Jclr , _    -> "0000101011DDDDDD000bbbbb"
        | Jset , "ea" -> "0000101001MMMRRR1S1bbbbb" | Jset , "aa" -> "0000101000aaaaaa1S1bbbbb"
        | Jset , "pp" -> "0000101010pppppp1S1bbbbb" | Jset , "qq" -> "0000000110qqqqqq1S1bbbbb"
        | Jset , _    -> "0000101011DDDDDD001bbbbb"
        | Jsclr, "ea" -> "0000101101MMMRRR1S0bbbbb" | Jsclr, "aa" -> "0000101100aaaaaa1S0bbbbb"
        | Jsclr, "pp" -> "0000101110pppppp1S0bbbbb" | Jsclr, "qq" -> "0000000111qqqqqq1S0bbbbb"
        | Jsclr, _    -> "0000101111DDDDDD000bbbbb"
        | Jsset, "ea" -> "0000101101MMMRRR1S1bbbbb" | Jsset, "aa" -> "0000101100aaaaaa1S1bbbbb"
        | Jsset, "pp" -> "0000101110pppppp1S1bbbbb" | Jsset, "qq" -> "0000000111qqqqqq1S1bbbbb"
        | Jsset, _    -> "0000101111DDDDDD001bbbbb"
      let relativeDest =
        match kind with
        | Brclr | Brset | Bsclr | Bsset -> true
        | _ -> false
      let destWord = if relativeDest then mask24 (d - pc) else mask24 d
      return [| fill pattern [| yield! fields; 'b', b |]; yield! ext; destWord |]
    }
  | BitOp(kind, bit, target) ->
    res {
      let! form, fields, ext = bitTarget env target
      let! b = checkRange "bit number" 0L 31L (int64 bit)
      let pattern =
        match kind, form with
        | Bset, "ea" -> "0000101001MMMRRR0S1bbbbb" | Bset, "aa" -> "0000101000aaaaaa0S1bbbbb"
        | Bset, "pp" -> "0000101010pppppp0S1bbbbb" | Bset, "qq" -> "0000000100qqqqqq0S1bbbbb"
        | Bset, _    -> "0000101011DDDDDD011bbbbb"
        | Bclr, "ea" -> "0000101001MMMRRR0S0bbbbb" | Bclr, "aa" -> "0000101000aaaaaa0S0bbbbb"
        | Bclr, "pp" -> "0000101010pppppp0S0bbbbb" | Bclr, "qq" -> "0000000100qqqqqq0S0bbbbb"
        | Bclr, _    -> "0000101011DDDDDD010bbbbb"
        | Bchg, "ea" -> "0000101101MMMRRR0S0bbbbb" | Bchg, "aa" -> "0000101100aaaaaa0S0bbbbb"
        | Bchg, "pp" -> "0000101110pppppp0S0bbbbb" | Bchg, "qq" -> "0000000101qqqqqq0S0bbbbb"
        | Bchg, _    -> "0000101111DDDDDD010bbbbb"
        | Btst, "ea" -> "0000101101MMMRRR0S1bbbbb" | Btst, "aa" -> "0000101100aaaaaa0S1bbbbb"
        | Btst, "pp" -> "0000101110pppppp0S1bbbbb" | Btst, "qq" -> "0000000101qqqqqq0S1bbbbb"
        | Btst, _    -> "0000101111DDDDDD011bbbbb"
      return [| fill pattern [| yield! fields; 'b', b |]; yield! ext |]
    }
  | Lua(ea, dst) ->
    res {
      match ea with
      | Disp(r, off) ->
        let! dd =
          match dst with
          | R n -> Ok n
          | N n -> Ok(8 + n)
          | d -> Error(BadRegister("lua destination", d))
        let! x = value env off
        let! disp = checkRange "lua displacement" -64L 63L x
        return [| fill "0000010000aaaRRRaaaadddd" [| 'a', disp &&& 0x7f; 'R', r; 'd', dd |] |]
      | _ ->
        let! m, r = mm2 ea
        let! d = need "lua destination" dst (code5 dst)
        return [| fill "00000100010MMRRR000ddddd" [| 'M', m; 'R', r; 'd', d |] |]
    }
  | Lra(LraReg r, d) ->
    res {
      let! dd = need "lra destination" d (code5 d)
      return [| fill "0000010011000RRR000ddddd" [| 'R', r; 'd', dd |] |]
    }
  | Lra(LraTarget t, d) ->
    res {
      let! dd = need "lra destination" d (code5 d)
      let! disp = relative env t
      return [| fill "0000010001000000010ddddd" [| 'd', dd |]; mask24 disp |]
    }
  | Inc d -> word "00000000000000000000100d" [| 'd', accBit d |]
  | Dec d -> word "00000000000000000000101d" [| 'd', accBit d |]
  | CondOp(Trapcc, c) -> word "00000000000000000001CCCC" [| 'C', condCode c |]
  | CondOp(Debugcc, c) -> word "00000000000000110000CCCC" [| 'C', condCode c |]
  | CondOp(Brkcc, c) -> word "00000000000000100001CCCC" [| 'C', condCode c |]
  | Cache Pflush -> word "000000000000000000000011" [||]
  | Cache Pflushun -> word "000000000000000000000001" [||]
  | Cache Pfree -> word "000000000000000000000010" [||]
  | Plock(lock, ea) ->
    res {
      let! m = mmmrrr ea
      let! ext = eaExt env ea
      let pattern = if lock then "0000101111MMMRRR10000001" else "0000101011MMMRRR10000001"
      return [| fill pattern [| 'M', m >>> 3; 'R', m &&& 7 |]; yield! ext |]
    }
  | PlockR(lock, t) ->
    res {
      let! disp = relative env t
      return [| (if lock then 0x00000f else 0x00000e); mask24 disp |]
    }
  | Clb(s, d) -> word "0000110000011110000000SD" [| 'S', accBit s; 'D', accBit d |]
  | Normf(s, d) ->
    res {
      let! c = need "normf source" s (sss s)
      return [| fill "00001100000111100010sssD" [| 's', c; 'D', accBit d |] |]
    }
  | Merge(s, d) ->
    res {
      let! c = need "merge source" s (sss s)
      return [| fill "00001100000110111000SSSD" [| 'S', c; 'D', accBit d |] |]
    }
  | Extract(u, FieldReg r, s, d) ->
    res {
      let! c = need "extract field" r (sss r)
      let pattern = if u then "0000110000011010100sSSSD" else "0000110000011010000sSSSD"
      return [| fill pattern [| 's', accBit s; 'S', c; 'D', accBit d |] |]
    }
  | Extract(u, FieldConst v, s, d) ->
    res {
      let! x = value env v
      let pattern = if u then "0000110000011000100s000D" else "0000110000011000000s000D"
      return [| fill pattern [| 's', accBit s; 'D', accBit d |]; mask24 x |]
    }
  | Insert(FieldReg r, s, d) ->
    res {
      let! c = need "insert field" r (sss r)
      let! q = need "insert source" s (qqqInsert s)
      return [| fill "00001100000110110qqqSSSD" [| 'q', q; 'S', c; 'D', accBit d |] |]
    }
  | Insert(FieldConst v, s, d) ->
    res {
      let! q = need "insert source" s (qqqInsert s)
      let! x = value env v
      return [| fill "00001100000110010qqq000D" [| 'q', q; 'D', accBit d |]; mask24 x |]
    }
  | Cmpu(s, d) ->
    let ggg =
      match s with
      | X0 -> Some 4 | Y0 -> Some 5 | X1 -> Some 6 | Y1 -> Some 7
      | s when s = Acc.reg (Acc.other d) -> Some 0
      | _ -> None
    res {
      let! g = need "cmpu source" s ggg
      return [| fill "00001100000111111111gggd" [| 'g', g; 'd', accBit d |] |]
    }
  | Dmac(neg, signs, s1, s2, d) ->
    res {
      let! q =
        match qqqq s1 s2 with
        | Some q -> Ok q
        | None -> Error(NoForm $"dmac pair {s1},{s2}")
      let s, S = match signs with SignedSigned -> 0, 0 | SignedUnsigned -> 1, 0 | UnsignedUnsigned -> 1, 1
      return [| fill "000000010010010s1SdkQQQQ" [| 's', s; 'S', S; 'd', accBit d; 'k', (if neg then 1 else 0); 'Q', q |] |]
    }
  | Vsl(s, i, ea) ->
    res {
      let! m = mmmrrr ea
      return [| fill "0000101S11MMMRRR110i0000" [| 'S', accBit s; 'M', m >>> 3; 'R', m &&& 7; 'i', i &&& 1 |] |]
    }
  | Movep(toP, pspace, address, other) ->
    res {
      let! a = value env address
      let a = a &&& 0xffffffL
      let! ps = spaceBit pspace
      let w = if toP then 1 else 0
      let! isPp =
        if a >= 0xffffc0L then Ok true
        elif a >= 0xffff80L then Ok false
        else Error(OutOfRange("peripheral address", a))
      let pp = int (a - 0xffffc0L)
      let qq = int (a - 0xffff80L)
      match other with
      | MovepImm v ->
        let! x = value env v
        let m = immMode
        if isPp then
          return [| fill "0000100sW1MMMRRR1Spppppp" [| 's', ps; 'W', 1; 'M', m >>> 3; 'R', m &&& 7; 'S', 0; 'p', pp |]; mask24 x |]
        elif ps = 0 then
          return [| fill "00000111W1MMMRRR0Sqqqqqq" [| 'W', 1; 'M', m >>> 3; 'R', m &&& 7; 'S', 0; 'q', qq |]; mask24 x |]
        else
          return [| fill "00000111W0MMMRRR1Sqqqqqq" [| 'W', 1; 'M', m >>> 3; 'R', m &&& 7; 'S', 0; 'q', qq |]; mask24 x |]
      | MovepMem(Space.P, ea) ->
        let! m = mmmrrr ea
        let! ext = eaExt env ea
        if isPp then
          return [| fill "0000100sW1MMMRRR01pppppp" [| 's', ps; 'W', w; 'M', m >>> 3; 'R', m &&& 7; 'p', pp |]; yield! ext |]
        else
          return [| fill "000000001WMMMRRR0Sqqqqqq" [| 'W', w; 'M', m >>> 3; 'R', m &&& 7; 'S', ps; 'q', qq |]; yield! ext |]
      | MovepMem(space, ea) ->
        let! m = mmmrrr ea
        let! ext = eaExt env ea
        let! s = spaceBit space
        if isPp then
          return [| fill "0000100sW1MMMRRR1Spppppp" [| 's', ps; 'W', w; 'M', m >>> 3; 'R', m &&& 7; 'S', s; 'p', pp |]; yield! ext |]
        elif ps = 0 then
          return [| fill "00000111W1MMMRRR0Sqqqqqq" [| 'W', w; 'M', m >>> 3; 'R', m &&& 7; 'S', s; 'q', qq |]; yield! ext |]
        else
          return [| fill "00000111W0MMMRRR1Sqqqqqq" [| 'W', w; 'M', m >>> 3; 'R', m &&& 7; 'S', s; 'q', qq |]; yield! ext |]
      | MovepReg r ->
        let! d = need "movep register" r (code6 r)
        if isPp then
          return [| fill "0000100sW1dddddd00pppppp" [| 's', ps; 'W', w; 'd', d; 'p', pp |] |]
        elif ps = 0 then
          return [| fill "00000100W1dddddd1q0qqqqq" [| 'W', w; 'd', d; 'q', qq |] |]
        else
          return [| fill "00000100W1dddddd0q1qqqqq" [| 'W', w; 'd', d; 'q', qq |] |]
    }
  | Rts     -> word "000000000000000000001100" [||]
  | Rti     -> word "000000000000000000000100" [||]
  | Nop     -> word "000000000000000000000000" [||]
  | Illegal -> word "000000000000000000000101" [||]
  | Stop    -> word "000000000000000010000111" [||]
  | Wait    -> word "000000000000000010000110" [||]
  | Reset   -> word "000000000000000010000100" [||]
  | Debug   -> word "000000000000001000000000" [||]
  | Trap    -> word "000000000000000000000110" [||]

/// Words an instruction takes, from the same choices `encode` makes.
let size env instr =
  encode env instr |> Result.map _.Length

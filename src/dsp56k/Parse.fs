/// Motorola DSP56300 assembler source (asm56300 dialect, plus the `.dc`/`.equ` spellings
/// kitbasher's sources use) to Isa values, one line at a time.
module dsp56k.Parse

open System

type ParseError =
  | UnknownMnemonic of mnemonic: string
  | BadOperand      of mnemonic: string * operand: string
  | BadExpression   of text: string

type Directive =
  | DEqu     of value: Expr
  | DDc      of values: Expr array
  | DDs      of count: Expr
  | DOrg     of space: Space * address: Expr option
  | DSection of name: string
  | DEndsec
  | DInclude of path: string
  | DIf      of condition: Expr
  | DElse
  | DEndif
  | DEnd
  | DIgnored of name: string

type Body =
  | Empty
  | Instruction of instr: Instr
  | Directive   of directive: Directive

type Line =
  { number  : int
    text    : string
    label   : string option
    body    : Body
    comment : string option }

// ---------------------------------------------------------------------------------------------
// Expressions.

let isIdentStart (c: char) = Char.IsLetter c || c = '_' || c = '.' || c = '@'
let isIdent (c: char) = Char.IsLetterOrDigit c || c = '_' || c = '.' || c = '@'

type Cursor(text: string) =
  let mutable pos = 0
  member _.Text = text
  member _.Pos with get () = pos and set v = pos <- v
  member _.Peek = if pos < text.Length then text[pos] else '\000'
  member _.PeekAt k = if pos + k < text.Length then text[pos + k] else '\000'
  member _.AtEnd = pos >= text.Length
  member _.Advance(n) = pos <- pos + n
  member _.Rest = text[pos..]

let rec parseExprAt (c: Cursor) : Result<Expr, ParseError> =
  // Lowest precedence first: relational, |/^, &, shifts, +/-, *,/,%, unary.
  let fail () = Error(BadExpression c.Text)
  let rec binaryLevel (ops: (string * BinOp) array) (next: unit -> Result<Expr, ParseError>) () =
    match next () with
    | Error e -> Error e
    | Ok left ->
      let rec loop left =
        let matched =
          ops |> Array.tryFind (fun (sym, _) -> c.Rest.StartsWith sym && not (sym = "<" && c.PeekAt 1 = '<') && not (sym = ">" && c.PeekAt 1 = '>'))
        match matched with
        | Some(sym, op) ->
          c.Advance sym.Length
          match next () with
          | Ok right -> loop (Bin(op, left, right))
          | Error e -> Error e
        | None -> Ok left
      loop left
  let rec unary () =
    match c.Peek with
    | '-' -> c.Advance 1; unary () |> Result.map (fun e -> match e with Num v -> Num(-v) | e -> Neg e)
    | '~' -> c.Advance 1; unary () |> Result.map Not
    | '+' -> c.Advance 1; unary ()
    | _ -> atom ()
  and atom () =
    match c.Peek with
    | '(' ->
      c.Advance 1
      match parseExprAt c with
      | Ok e when c.Peek = ')' -> c.Advance 1; Ok e
      | Ok _ -> fail ()
      | Error e -> Error e
    | '$' ->
      c.Advance 1
      let start = c.Pos
      while Uri.IsHexDigit c.Peek do c.Advance 1
      if c.Pos = start then fail () else Ok(Num(Convert.ToInt64(c.Text[start .. c.Pos - 1], 16)))
    | '%' ->
      c.Advance 1
      let start = c.Pos
      while c.Peek = '0' || c.Peek = '1' do c.Advance 1
      if c.Pos = start then fail () else Ok(Num(Convert.ToInt64(c.Text[start .. c.Pos - 1], 2)))
    | '\'' ->
      // 'c' character constants
      let close = c.Text.IndexOf('\'', c.Pos + 1)
      if close < 0 then fail ()
      else
        let chars = c.Text[c.Pos + 1 .. close - 1]
        c.Pos <- close + 1
        Ok(Num(chars |> Seq.fold (fun acc ch -> (acc <<< 8) ||| int64 ch) 0L))
    | d when Char.IsDigit d ->
      let start = c.Pos
      while Char.IsDigit c.Peek do c.Advance 1
      if c.Peek = '.' && Char.IsDigit(c.PeekAt 1) then
        // A fraction: Q23, the way the Motorola assembler reads 0.5.
        c.Advance 1
        while Char.IsDigit c.Peek do c.Advance 1
        let f = Double.Parse(c.Text[start .. c.Pos - 1], Globalization.CultureInfo.InvariantCulture)
        Ok(Num(int64 (Math.Min(Math.Round(f * 8388608.0), 8388607.0))))
      else Ok(Num(Int64.Parse(c.Text[start .. c.Pos - 1])))
    | i when isIdentStart i ->
      let start = c.Pos
      while isIdent c.Peek do c.Advance 1
      Ok(Sym(c.Text[start .. c.Pos - 1]))
    | '*' -> c.Advance 1; Ok(Sym "*")
    | _ -> fail ()
  let mult = binaryLevel [| "*", BinOp.Mul; "/", BinOp.Div; "%", BinOp.Mod |] unary
  let add = binaryLevel [| "+", BinOp.Add; "-", BinOp.Sub |] mult
  let shift = binaryLevel [| "<<", BinOp.Shl; ">>", BinOp.Shr |] add
  let band = binaryLevel [| "&", BinOp.And |] shift
  let bor = binaryLevel [| "|", BinOp.Or; "^", BinOp.Xor |] band
  let rel = binaryLevel [| "<=", BinOp.Le; ">=", BinOp.Ge; "==", BinOp.Eq; "!=", BinOp.Ne; "<>", BinOp.Ne; "=", BinOp.Eq; "<", BinOp.Lt; ">", BinOp.Gt |] bor
  rel ()

let parseExpr (text: string) =
  let c = Cursor(text.Trim())
  match parseExprAt c with
  | Ok e when c.AtEnd -> Ok e
  | Ok _ -> Error(BadExpression text)
  | Error e -> Error e

/// An operand with its forcing prefix: `<<` I/O short, `<` short, `>` long.
let parseOperand (text: string) =
  let t = text.Trim()
  let force, rest =
    if t.StartsWith "<<" then IoShort, t[2..]
    elif t.StartsWith "<" then Short, t[1..]
    elif t.StartsWith ">" then Long, t[1..]
    else Auto, t
  parseExpr rest |> Result.map (fun e -> { expr = e; force = force })

// ---------------------------------------------------------------------------------------------
// Registers, effective addresses, memory references.

let regNames =
  [| "x0", X0; "x1", X1; "y0", Y0; "y1", Y1
     "a0", A0; "b0", B0; "a2", A2; "b2", B2; "a1", A1; "b1", B1; "a", A; "b", B
     "ep", Ep; "vba", Vba; "sc", Sc; "sz", Sz; "sr", Sr; "omr", Omr; "sp", Sp
     "ssh", Ssh; "ssl", Ssl; "la", La; "lc", Lc
     "x", X; "y", Y; "a10", A10; "b10", B10; "ab", AB; "ba", BA
     "mr", Mr; "ccr", Ccr; "eom", Eom; "com", Omr
     for i in 0 .. 7 do
       $"r{i}", R i
       $"n{i}", N i
       $"m{i}", M i |]
  |> dict

let tryReg (text: string) =
  match regNames.TryGetValue(text.Trim().ToLowerInvariant()) with
  | true, r -> Some r
  | _ -> None

let tryAcc text = tryReg text |> Option.bind Acc.ofReg

let tryRn (text: string) =
  match tryReg text with
  | Some(R n) -> Some n
  | _ -> None

/// (rn)-nn (rn)+nn (rn)- (rn)+ (rn) (rn+nn) -(rn) (rn+expr) (rn-expr), or an absolute address.
let rec parseEa (text: string) : Result<Ea, ParseError> =
  let t = text.Trim()
  if t.StartsWith ">(" || t.StartsWith "<(" then
    // y:>(r4+1): the forcing written before the parenthesis
    parseEa t[1..] |> Result.map (fun ea ->
      match ea with
      | Disp(r, o) -> Disp(r, { o with force = (if t[0] = '>' then Long else Short) })
      | other -> other)
  else
    let lower = t.ToLowerInvariant()
    let bad () = Error(BadOperand("ea", t))
    let inner (s: string) = s.Substring(1, s.IndexOf ')' - 1)
    if lower.StartsWith "-(" && lower.EndsWith ")" then
      match tryRn (inner t[1..]) with
      | Some r -> Ok(PreDec r)
      | None -> bad ()
    elif lower.StartsWith "(" then
      let close = lower.IndexOf ')'
      if close < 0 then bad ()
      else
        let body = t.Substring(1, close - 1)
        let post = lower[close + 1 ..]
        match tryRn body with
        | Some r ->
          match post with
          | "" -> Ok(Ind r)
          | "+" -> Ok(PostInc r)
          | "-" -> Ok(PostDec r)
          | p when p = $"+n{r}" -> Ok(PostIncN r)
          | p when p = $"-n{r}" -> Ok(PostDecN r)
          | _ -> bad ()
        | None ->
          // (rn+nn), (rn+expr), (rn-expr)
          let k = body.IndexOfAny [| '+'; '-' |]
          if k < 2 || post <> "" then bad ()
          else
            match tryRn body[.. k - 1] with
            | None -> bad ()
            | Some r ->
              let rest = body[k + 1 ..].Trim()
              if body[k] = '+' && tryReg rest = Some(N r) then Ok(IndexN r)
              else
                let opText = if body[k] = '-' then "-" + rest else rest
                let forced, exprText =
                  if rest.StartsWith ">" then Long, (if body[k] = '-' then "-" + rest[1..] else rest[1..])
                  elif rest.StartsWith "<" then Short, (if body[k] = '-' then "-" + rest[1..] else rest[1..])
                  else Auto, opText
                parseExpr exprText |> Result.map (fun e -> Disp(r, { expr = e; force = forced }))
    else parseOperand t |> Result.map Abs

let parseSpace (text: string) =
  match text.ToLowerInvariant() with
  | "x" -> Some Space.X
  | "y" -> Some Space.Y
  | "l" -> Some Space.L
  | "p" -> Some Space.P
  | _ -> None

/// `x:ea`, `y:ea`, `l:ea`, `p:ea` (the space letter and colon, then an effective address).
let tryMem (text: string) =
  let t = text.Trim()
  if t.Length > 2 && t[1] = ':' then
    parseSpace (string t[0]) |> Option.map (fun s -> s, t[2..])
  else None

/// Split at top-level commas (not inside parentheses or quotes).
let splitTop (text: string) =
  let parts = Collections.Generic.List<string>()
  let mutable depth = 0
  let mutable start = 0
  let mutable quote = false
  for i in 0 .. text.Length - 1 do
    match text[i] with
    | '\'' -> quote <- not quote
    | '(' when not quote -> depth <- depth + 1
    | ')' when not quote -> depth <- depth - 1
    | ',' when depth = 0 && not quote ->
      parts.Add(text[start .. i - 1])
      start <- i + 1
    | _ -> ()
  parts.Add(text[start..])
  parts.ToArray() |> Array.map _.Trim()

let collect (results: Result<'a, 'e> seq) =
  let items = Collections.Generic.List<'a>()
  let mutable error = None
  for r in results do
    match r, error with
    | Ok v, None -> items.Add v
    | Error e, None -> error <- Some e
    | _ -> ()
  match error with
  | Some e -> Error e
  | None -> Ok(items.ToArray())

/// One move field: `x:ea,reg`, `reg,x:ea`, `#imm,reg`, `reg,reg`, or an update `(rn)+nn`.
let parseMove (field: string) : Result<Move, ParseError> =
  let bad () = Error(BadOperand("move", field))
  match splitTop field with
  | [| single |] ->
    match parseEa single with
    | Ok ea when (match ea with Abs _ -> false | _ -> true) -> Ok(Update ea)
    | _ -> bad ()
  | [| src; dst |] ->
    match tryMem src, tryMem dst with
    | Some(space, eaText), None ->
      match tryReg dst, parseEa eaText with
      | Some r, Ok ea -> Ok(Load(space, ea, r))
      | _ -> bad ()
    | None, Some(space, eaText) ->
      match tryReg src, parseEa eaText with
      | Some r, Ok ea -> Ok(Store(r, space, ea))
      | _ -> bad ()
    | None, None when (match parseEa src, tryReg dst with
                       | Ok(PostDecN r | PostIncN r | PostDec r | PostInc r), Some(R d) -> r = d
                       | _ -> false) ->
      // (rn)+nn,rn: the oracle's spelling of an address update
      parseEa src |> Result.map Update
    | None, None ->
      if src.StartsWith "#" then
        match tryReg dst, parseOperand src[1..] with
        | Some r, Ok v -> Ok(Imm(v, r))
        | _ -> bad ()
      else
        match tryReg src, tryReg dst with
        | Some s, Some d -> Ok(Copy(s, d))
        | _ -> bad ()
    | Some _, Some _ -> bad ()
  | _ -> bad ()

// ---------------------------------------------------------------------------------------------
// Instructions.

let binaryOps =
  [| "add", AluOp.Add; "sub", AluOp.Sub; "cmp", AluOp.Cmp; "cmpm", AluOp.Cmpm; "tfr", AluOp.Tfr
     "adc", AluOp.Adc; "sbc", AluOp.Sbc; "and", AluOp.And; "or", AluOp.Or; "eor", AluOp.Eor
     "addl", AluOp.Addl; "addr", AluOp.Addr; "subl", AluOp.Subl; "subr", AluOp.Subr |] |> dict

let unaryOps =
  [| "clr", AluUnary.Clr; "tst", AluUnary.Tst; "neg", AluUnary.Neg; "abs", AluUnary.Abs
     "not", AluUnary.Not; "rnd", AluUnary.Rnd; "asl", AluUnary.Asl; "asr", AluUnary.Asr
     "lsl", AluUnary.Lsl; "lsr", AluUnary.Lsr; "rol", AluUnary.Rol; "ror", AluUnary.Ror |] |> dict

let mulKinds = [| "mpy", Mpy; "mpyr", Mpyr; "mac", Mac; "macr", Macr |] |> dict

let immOps = [| "add", AddI; "sub", SubI; "cmp", CmpI; "and", AndI; "or", OrI; "eor", EorI |] |> dict

/// The ALU part of a parallel instruction, from its mnemonic and first field.
let parseAlu (mnemonic: string) (operands: string) : Result<Alu, ParseError> option =
  let bad () = Some(Error(BadOperand(mnemonic, operands)))
  let ops = splitTop operands
  match binaryOps.TryGetValue mnemonic, unaryOps.TryGetValue mnemonic, mulKinds.TryGetValue mnemonic with
  | (true, op), _, _ when ops.Length = 2 && not (ops[0].StartsWith "#") ->
    match tryReg ops[0], tryAcc ops[1] with
    | Some s, Some d -> Some(Ok(Binary(op, s, d)))
    | _ -> bad ()
  | _, (true, op), _ when ops.Length = 1 ->
    match tryAcc ops[0] with
    | Some d -> Some(Ok(Unary(op, d)))
    | None -> bad ()
  | _, _, (true, kind) when ops.Length = 3 && not (ops[1].StartsWith "#") ->
    let neg = ops[0].StartsWith "-"
    let s1 = if neg then (ops[0].Substring 1) else ops[0].TrimStart '+'
    match tryReg s1, tryReg ops[1], tryAcc ops[2] with
    | Some a, Some b, Some d -> Some(Ok(Mul(kind, neg, a, b, d)))
    | _ -> bad ()
  | _ ->
    match mnemonic, operands.ToLowerInvariant().Replace(" ", "") with
    | "max", "a,b" -> Some(Ok Max)
    | "maxm", "a,b" -> Some(Ok Maxm)
    | _ -> None

let tryCondSuffix (prefix: string) (mnemonic: string) =
  if mnemonic.StartsWith prefix && mnemonic.Length > prefix.Length then
    Cond.tryParse mnemonic[prefix.Length ..]
  else None

let parseLoopCount (text: string) =
  if text.StartsWith "#" then parseOperand text[1..] |> Result.map Count
  else
    match tryReg text, tryMem text with
    | Some r, _ -> Ok(FromReg r)
    | None, Some(space, eaText) -> parseEa eaText |> Result.map (fun ea -> FromMem(space, ea))
    | None, None -> Error(BadOperand("loop count", text))

let parseBitNumber (text: string) =
  if text.StartsWith "#" then
    parseExpr text[1..]
    |> Result.bind (fun e ->
      match Expr.eval (fun _ -> None) e with
      | Ok v -> Ok(int v)
      | Error _ -> Error(BadOperand("bit number", text)))
  else Error(BadOperand("bit number", text))

let parseBitTarget (text: string) =
  match tryMem text, tryReg text with
  | Some(space, eaText), _ -> parseEa eaText |> Result.map (fun ea -> BitMem(space, ea))
  | None, Some r -> Ok(BitReg r)
  | None, None -> Error(BadOperand("bit target", text))

/// An instruction from its mnemonic and whitespace-separated fields.
let rec parseInstr (mnemonic: string) (fields: string array) : Result<Instr, ParseError> =
  let m = mnemonic.ToLowerInvariant()
  let bad () = Error(BadOperand(m, String.concat " " fields))
  let first = if fields.Length > 0 then fields[0] else ""
  let ops = splitTop first
  let moves (fs: string array) = fs |> Seq.map parseMove |> collect
  let noOperands instr = if fields.Length = 0 then Ok instr else bad ()
  match m with
  | "nop" -> noOperands Nop
  | "rts" -> noOperands Rts
  | "rti" -> noOperands Rti
  | "illegal" -> noOperands Illegal
  | "stop" -> noOperands Stop
  | "wait" -> noOperands Wait
  | "reset" -> noOperands Reset
  | "debug" -> noOperands Debug
  | "trap" -> noOperands Trap
  | "enddo" -> noOperands Enddo
  | "move" | "movec" | "movem" ->
    moves fields |> Result.map (fun ms -> Par(None, ms))
  | "andi" | "ori" ->
    match ops with
    | [| v; r |] when v.StartsWith "#" ->
      match parseExpr v[1..], tryReg r with
      | Ok e, Some reg ->
        match Expr.eval (fun _ -> None) e with
        | Ok x -> Ok(CtrlImm((m = "andi"), int x, reg))
        | Error _ -> bad ()
      | _ -> bad ()
    | _ -> bad ()
  | ("and" | "or") when ops.Length = 2 && ops[0].StartsWith "#" && (match tryReg ops[1] with Some(Mr | Ccr | Omr | Eom) -> true | _ -> false) ->
    parseInstr (m + "i") fields
  | ("add" | "sub" | "cmp" | "and" | "or" | "eor") when ops.Length = 2 && ops[0].StartsWith "#" ->
    match parseOperand (ops[0].Substring 1), tryAcc ops[1] with
    | Ok v, Some d when fields.Length = 1 -> Ok(AluImm(immOps[m], v, d))
    | _ -> bad ()
  | ("asl" | "asr") when ops.Length = 3 ->
    let op = if m = "asl" then ShAsl else ShAsr
    match tryAcc ops[1], tryAcc ops[2] with
    | Some s, Some d when ops[0].StartsWith "#" ->
      parseExpr (ops[0].Substring 1)
      |> Result.bind (fun e ->
        match Expr.eval (fun _ -> None) e with
        | Ok n -> Ok(ShiftImm(op, int n, s, d))
        | Error _ -> bad ())
    | Some s, Some d ->
      match tryReg ops[0] with
      | Some r -> Ok(ShiftReg(op, r, s, d))
      | None -> bad ()
    | _ -> bad ()
  | ("lsl" | "lsr") when ops.Length = 2 ->
    let op = if m = "lsl" then ShLsl else ShLsr
    match tryAcc ops[1] with
    | Some d when ops[0].StartsWith "#" ->
      parseExpr (ops[0].Substring 1)
      |> Result.bind (fun e ->
        match Expr.eval (fun _ -> None) e with
        | Ok n -> Ok(ShiftImm(op, int n, d, d))
        | Error _ -> bad ())
    | Some d ->
      match tryReg ops[0] with
      | Some r -> Ok(ShiftReg(op, r, d, d))
      | None -> bad ()
    | None -> bad ()
  | ("mpy" | "mpyr" | "mac" | "macr") when ops.Length = 3 && ops[1].StartsWith "#" ->
    let neg = ops[0].StartsWith "-"
    let s = if neg then (ops[0].Substring 1) else ops[0].TrimStart '+'
    match tryReg s, parseExpr (ops[1].Substring 1), tryAcc ops[2] with
    | Some src, Ok e, Some d ->
      match Expr.eval (fun _ -> None) e with
      | Ok n -> Ok(MulImm(mulKinds[m], neg, src, int n, d))
      | Error _ -> bad ()
    | _ -> bad ()
  | "mpysu" | "mpyuu" | "macsu" | "macuu" ->
    let neg = ops.Length = 3 && ops[0].StartsWith "-"
    match ops with
    | [| s1; s2; d |] ->
      match tryReg (s1.TrimStart('-', '+')), tryReg s2, tryAcc d with
      | Some a, Some b, Some acc -> Ok(MulSu((if m.StartsWith "mpy" then Mpy else Mac), neg, m.EndsWith "uu", a, b, acc))
      | _ -> bad ()
    | _ -> bad ()
  | "mpyi" | "mpyri" | "maci" | "macri" ->
    match ops with
    | [| v; s; d |] ->
      let neg = v.StartsWith "-"
      let v = v.TrimStart('-', '+')
      match parseOperand (v.TrimStart '#'), tryReg s, tryAcc d with
      | Ok value, Some src, Some acc ->
        let kind = match m with "mpyi" -> Mpy | "mpyri" -> Mpyr | "maci" -> Mac | _ -> Macr
        Ok(MulI(kind, neg, value, src, acc))
      | _ -> bad ()
    | _ -> bad ()
  | "div" ->
    match ops with
    | [| s; d |] ->
      match tryReg s, tryAcc d with
      | Some src, Some acc -> Ok(Div(src, acc))
      | _ -> bad ()
    | _ -> bad ()
  | "norm" ->
    match ops with
    | [| r; d |] ->
      match tryRn r, tryAcc d with
      | Some n, Some acc -> Ok(Norm(n, acc))
      | _ -> bad ()
    | _ -> bad ()
  | "do" | "dor" ->
    let kind = if m = "do" then Do else Dor
    match ops with
    | [| c; target |] when c.ToLowerInvariant() = "forever" ->
      parseOperand target |> Result.map (fun t -> Forever(kind, t))
    | [| c; target |] ->
      match parseLoopCount c, parseOperand target with
      | Ok count, Ok t -> Ok(Loop(kind, count, t))
      | _ -> bad ()
    | _ -> bad ()
  | "rep" ->
    match ops with
    | [| c |] -> parseLoopCount c |> Result.map Rep
    | _ -> bad ()
  | "lua" | "lea" ->
    match ops with
    | [| e; d |] ->
      match parseEa e, tryReg d with
      | Ok ea, Some r -> Ok(Lua(ea, r))
      | _ -> bad ()
    | _ -> bad ()
  | "brclr" | "brset" | "bsclr" | "bsset" | "jclr" | "jset" | "jsclr" | "jsset" ->
    let kind =
      match m with
      | "brclr" -> Brclr | "brset" -> Brset | "bsclr" -> Bsclr | "bsset" -> Bsset
      | "jclr" -> Jclr   | "jset" -> Jset   | "jsclr" -> Jsclr | _ -> Jsset
    match ops with
    | [| bit; target; dest |] ->
      match parseBitNumber bit, parseBitTarget target, parseOperand dest with
      | Ok b, Ok t, Ok d -> Ok(BitBranch(kind, b, t, d))
      | _ -> bad ()
    | _ -> bad ()
  | "bset" | "bclr" | "bchg" | "btst" ->
    let kind = match m with "bset" -> Bset | "bclr" -> Bclr | "bchg" -> Bchg | _ -> Btst
    match ops with
    | [| bit; target |] ->
      match parseBitNumber bit, parseBitTarget target with
      | Ok b, Ok t -> Ok(BitOp(kind, b, t))
      | _ -> bad ()
    | _ -> bad ()
  | "inc" | "dec" ->
    match ops with
    | [| d |] ->
      match tryAcc d with
      | Some acc -> Ok(if m = "inc" then Inc acc else Dec acc)
      | None -> bad ()
    | _ -> bad ()
  | "pflush" -> noOperands (Cache Pflush)
  | "pflushun" -> noOperands (Cache Pflushun)
  | "pfree" -> noOperands (Cache Pfree)
  | "plock" | "punlock" ->
    match ops with
    | [| e |] -> parseEa e |> Result.map (fun ea -> Plock((m = "plock"), ea))
    | _ -> bad ()
  | "plockr" | "punlockr" ->
    match ops with
    | [| t |] -> parseOperand t |> Result.map (fun o -> PlockR((m = "plockr"), o))
    | _ -> bad ()
  | "lra" ->
    match ops with
    | [| s; d |] ->
      match tryReg d with
      | Some dst ->
        match tryRn s with
        | Some r -> Ok(Lra(LraReg r, dst))
        | None -> parseOperand s |> Result.map (fun o -> Lra(LraTarget o, dst))
      | None -> bad ()
    | _ -> bad ()
  | "clb" ->
    match ops with
    | [| s; d |] ->
      match tryAcc s, tryAcc d with
      | Some x, Some y -> Ok(Clb(x, y))
      | _ -> bad ()
    | _ -> bad ()
  | "normf" | "merge" | "cmpu" ->
    match ops with
    | [| s; d |] ->
      match tryReg s, tryAcc d with
      | Some src, Some acc -> Ok(match m with "normf" -> Normf(src, acc) | "merge" -> Merge(src, acc) | _ -> Cmpu(src, acc))
      | _ -> bad ()
    | _ -> bad ()
  | "extract" | "extractu" | "insert" ->
    match ops with
    | [| f; s; d |] ->
      let field =
        if f.StartsWith "#" then parseOperand (f.Substring 1) |> Result.map FieldConst
        else
          match tryReg f with
          | Some r -> Ok(FieldReg r)
          | None -> Error(BadOperand(m, f))
      match field, tryReg s, tryAcc d with
      | Ok fld, Some src, Some acc ->
        if m = "insert" then Ok(Insert(fld, src, acc))
        else
          match Acc.ofReg src with
          | Some sa -> Ok(Extract((m = "extractu"), fld, sa, acc))
          | None -> bad ()
      | _ -> bad ()
    | _ -> bad ()
  | "dmacss" | "dmacsu" | "dmacuu" ->
    match ops with
    | [| s1; s2; d |] ->
      let neg = s1.StartsWith "-"
      match tryReg (s1.TrimStart('-', '+')), tryReg s2, tryAcc d with
      | Some x, Some y, Some acc ->
        let signs = match m with "dmacss" -> SignedSigned | "dmacsu" -> SignedUnsigned | _ -> UnsignedUnsigned
        Ok(Dmac(neg, signs, x, y, acc))
      | _ -> bad ()
    | _ -> bad ()
  | "vsl" ->
    match ops with
    | [| s; i; e |] ->
      match tryAcc s, Int32.TryParse i, tryMem e with
      | Some acc, (true, bit), Some(Space.L, eaText) -> parseEa eaText |> Result.map (fun ea -> Vsl(acc, bit, ea))
      | _ -> bad ()
    | _ -> bad ()
  | "movep" ->
    match ops with
    | [| src; dst |] ->
      // The peripheral side is the X:/Y: absolute address at or above $ffff80.
      let peripheral (text: string) =
        match tryMem text with
        | Some((Space.X | Space.Y) as sp, eaText) ->
          match parseEa eaText with
          | Ok(Abs o) ->
            match Expr.eval (fun _ -> None) o.expr with
            | Ok v when (v &&& 0xffffffL) >= 0xffff80L -> Some(sp, o)
            | _ -> None
          | _ -> None
        | _ -> None
      let other (text: string) =
        match tryMem text, tryReg text with
        | Some(sp, eaText), _ -> parseEa eaText |> Result.map (fun ea -> MovepMem(sp, ea))
        | None, Some r -> Ok(MovepReg r)
        | None, None when text.StartsWith "#" -> parseOperand (text.Substring 1) |> Result.map MovepImm
        | None, None -> Error(BadOperand(m, text))
      match peripheral dst, peripheral src with
      | Some(sp, o), _ -> other src |> Result.map (fun x -> Movep(true, sp, o, x))
      | None, Some(sp, o) -> other dst |> Result.map (fun x -> Movep(false, sp, o, x))
      | None, None -> bad ()
    | _ -> bad ()
  | _ when m.Length > 4 && m.StartsWith "trap" && (Cond.tryParse m[4..]).IsSome ->
    noOperands (CondOp(Trapcc, (Cond.tryParse m[4..]).Value))
  | _ when m.Length > 5 && m.StartsWith "debug" && (Cond.tryParse m[5..]).IsSome ->
    noOperands (CondOp(Debugcc, (Cond.tryParse m[5..]).Value))
  | _ when m.Length > 3 && m.StartsWith "brk" && (Cond.tryParse m[3..]).IsSome ->
    noOperands (CondOp(Brkcc, (Cond.tryParse m[3..]).Value))
  | _ ->
    // Branches and jumps: bra bsr b<cc> bs<cc> jmp jsr j<cc> js<cc>; t<cc>; then the ALU ops.
    let branchKind =
      match m with
      | "bra" -> Some Bra
      | "bsr" -> Some Bsr
      | _ ->
        match tryCondSuffix "bs" m with
        | Some c -> Some(Bscc c)
        | None -> tryCondSuffix "b" m |> Option.map Bcc
    let jumpKind =
      match m with
      | "jmp" -> Some Jmp
      | "jsr" -> Some Jsr
      | _ ->
        match tryCondSuffix "js" m with
        | Some c -> Some(Jscc c)
        | None -> tryCondSuffix "j" m |> Option.map Jcc
    let tcc = if m.Length = 3 then tryCondSuffix "t" m else None
    match branchKind, jumpKind, tcc with
    | Some kind, _, _ when fields.Length = 1 ->
      match tryRn first with
      | Some r -> Ok(BranchRn(kind, r))
      | None -> parseOperand first |> Result.map (fun t -> Branch(kind, t))
    | _, Some kind, _ when fields.Length = 1 ->
      if first.StartsWith "(" || first.StartsWith "-(" then parseEa first |> Result.map (fun ea -> JumpEa(kind, ea))
      else parseOperand first |> Result.map (fun t -> Jump(kind, t))
    | _, _, Some c when m <> "tst" && m <> "tfr" ->
      let parsePair (text: string) = splitTop text
      let alu, agu =
        fields |> Array.fold (fun (alu, agu) f ->
          match parsePair f with
          | [| s; d |] ->
            match tryRn s, tryRn d, tryReg s, tryAcc d with
            | Some a, Some b, _, _ -> alu, Some(a, b)
            | _, _, Some src, Some acc -> Some(src, acc), agu
            | _ -> alu, agu
          | _ -> alu, agu) (None, None)
      if alu.IsNone && agu.IsNone then bad () else Ok(Tcc(c, alu, agu))
    | _ ->
      match parseAlu m first with
      | Some(Ok alu) ->
        let rest = fields[1..]
        match rest |> Array.tryFindIndex (fun f -> f.ToLowerInvariant().StartsWith "if") with
        | Some k ->
          let f = rest[k].ToLowerInvariant()
          let update = f.EndsWith ".u"
          let condText = if update then f[2 .. f.Length - 3] else f[2..]
          match Cond.tryParse condText with
          | Some c when rest.Length = 1 -> Ok(IfCc(alu, c, update))
          | _ -> bad ()
        | None -> moves rest |> Result.map (fun ms -> Par(Some alu, ms))
      | Some(Error e) -> Error e
      | None -> Error(UnknownMnemonic m)

// ---------------------------------------------------------------------------------------------
// Lines.

/// Split the statement into whitespace-separated fields, keeping quoted strings whole.
let fields (text: string) =
  let parts = Collections.Generic.List<string>()
  let sb = Text.StringBuilder()
  let mutable quote = false
  for ch in text do
    if ch = '\'' then quote <- not quote
    if Char.IsWhiteSpace ch && not quote then
      if sb.Length > 0 then
        parts.Add(sb.ToString())
        sb.Clear() |> ignore
    else sb.Append ch |> ignore
  if sb.Length > 0 then parts.Add(sb.ToString())
  parts.ToArray()

let stripComment (text: string) =
  let mutable quote = false
  let mutable cut = -1
  for i in 0 .. text.Length - 1 do
    if cut < 0 then
      match text[i] with
      | '\'' -> quote <- not quote
      | ';' when not quote -> cut <- i
      | _ -> ()
  if cut < 0 then text, None else text[.. cut - 1], Some(text[cut + 1 ..].Trim())

let directiveNames =
  set [ "equ"; ".equ"; "dc"; ".dc"; "ds"; "org"; "section"; "endsec"; "include"; "if"; "else"; "endif"
        "end"; "xdef"; "xref"; "global"; "opt"; "page"; "nolist"; "list"; "title"; "ttl"; "align" ]

let parseDirective (name: string) (args: string) : Result<Directive, ParseError> =
  match name with
  | "equ" | ".equ" -> parseExpr args |> Result.map DEqu
  | "dc" | ".dc" -> splitTop args |> Seq.map parseExpr |> collect |> Result.map DDc
  | "ds" -> parseExpr args |> Result.map DDs
  | "org" ->
    let t = args.Trim()
    match tryMem (t + (if t.EndsWith ":" then "0" else "")) with
    | Some(space, rest) when t.EndsWith ":" -> Ok(DOrg(space, None))
    | Some(space, rest) -> parseExpr rest |> Result.map (fun e -> DOrg(space, Some e))
    | None -> Error(BadOperand("org", args))
  | "section" -> Ok(DSection(args.Trim()))
  | "endsec" -> Ok DEndsec
  | "include" -> Ok(DInclude(args.Trim().Trim('\'', '"')))
  | "if" -> parseExpr args |> Result.map DIf
  | "else" -> Ok DElse
  | "endif" -> Ok DEndif
  | "end" -> Ok DEnd
  | other -> Ok(DIgnored other)

/// One source line. A label starts in column 1 (its colon is optional); `NAME equ value` may
/// also be written indented.
let parseLine (number: int) (text: string) : Result<Line, ParseError> =
  let code, comment = stripComment text
  let line body label = Ok { number = number; text = text; label = label; body = body; comment = comment }
  let fs = fields code
  if fs.Length = 0 then line Empty None
  else
    let startsInColumn1 = code.Length > 0 && not (Char.IsWhiteSpace code[0])
    let label, rest =
      if startsInColumn1 then Some(fs[0].TrimEnd ':'), fs[1..]
      elif fs[0].EndsWith ":" then Some(fs[0].TrimEnd ':'), fs[1..]
      elif fs.Length >= 2 && (fs[1].ToLowerInvariant() = "equ" || fs[1].ToLowerInvariant() = ".equ") then Some fs[0], fs[1..]
      else None, fs
    match rest with
    | [||] -> line Empty label
    | _ ->
      let name = rest[0].ToLowerInvariant()
      if directiveNames.Contains name then
        let args = String.concat " " rest[1..]
        parseDirective name args |> Result.bind (fun d -> line (Directive d) label)
      else parseInstr rest[0] rest[1..] |> Result.bind (fun i -> line (Instruction i) label)

let errorText error =
  match error with
  | UnknownMnemonic m -> $"unknown mnemonic '{m}'"
  | BadOperand(m, o) -> $"cannot read '{o}' for {m}"
  | BadExpression t -> $"cannot read the expression '{t}'"

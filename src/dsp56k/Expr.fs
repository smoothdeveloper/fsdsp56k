namespace dsp56k

/// Assembler expressions: what can stand where the DSP takes a number (immediates, addresses,
/// displacements, loop counts). Symbols resolve late, so the same instruction value serves the
/// assembler (labels known after pass 1) and the DSL (labels it generates).
type Expr =
  | Num of value: int64
  | Sym of name: string
  | Neg of Expr
  | Not of Expr
  | Bin of op: BinOp * left: Expr * right: Expr

  // Arithmetic on expressions, so the DSL can write `Sym "TB_FREE" - 64`.
  static member (+) (a: Expr, b: Expr) = Bin(BinOp.Add, a, b)
  static member (+) (a: Expr, b: int) = Bin(BinOp.Add, a, Num(int64 b))
  static member (-) (a: Expr, b: Expr) = Bin(BinOp.Sub, a, b)
  static member (-) (a: Expr, b: int) = Bin(BinOp.Sub, a, Num(int64 b))
  static member (*) (a: Expr, b: Expr) = Bin(BinOp.Mul, a, b)
  static member (*) (a: Expr, b: int) = Bin(BinOp.Mul, a, Num(int64 b))
  static member (/) (a: Expr, b: int) = Bin(BinOp.Div, a, Num(int64 b))
  static member (<<<) (a: Expr, b: int) = Bin(BinOp.Shl, a, Num(int64 b))
  static member (>>>) (a: Expr, b: int) = Bin(BinOp.Shr, a, Num(int64 b))
  static member (&&&) (a: Expr, b: int) = Bin(BinOp.And, a, Num(int64 b))
  static member (|||) (a: Expr, b: Expr) = Bin(BinOp.Or, a, b)
  static member (~-) (a: Expr) = match a with Num v -> Num(-v) | e -> Neg e

and [<RequireQualifiedAccess>] BinOp =
  | Add | Sub | Mul | Div | Mod
  | Shl | Shr | And | Or  | Xor
  | Lt  | Gt  | Le  | Ge  | Eq | Ne

/// How the source asked for an operand to be encoded: `<` short, `>` long, `<<` I/O short,
/// nothing for the assembler's choice (the shortest form that fits a value known when the
/// instruction is sized; a forward reference takes the long form).
type Force =
  | Auto
  | Short
  | Long
  | IoShort

type Operand =
  { expr  : Expr
    force : Force }

type ExprError =
  | Undefined of name: string
  | DivideByZero

module Expr =
  let num (v: int) = Num(int64 v)

  let rec eval (lookup: string -> int64 option) expr =
    let both l r f =
      match eval lookup l, eval lookup r with
      | Ok a, Ok b -> f a b
      | Error e, _ | _, Error e -> Error e
    let flag b = Ok(if b then 1L else 0L)
    match expr with
    | Num v -> Ok v
    | Sym s ->
      match lookup s with
      | Some v -> Ok v
      | None -> Error(Undefined s)
    | Neg e -> eval lookup e |> Result.map (~-)
    | Not e -> eval lookup e |> Result.map (~~~)
    | Bin(op, l, r) ->
      both l r (fun a b ->
        match op with
        | BinOp.Add -> Ok(a + b)
        | BinOp.Sub -> Ok(a - b)
        | BinOp.Mul -> Ok(a * b)
        | BinOp.Div -> if b = 0L then Error DivideByZero else Ok(a / b)
        | BinOp.Mod -> if b = 0L then Error DivideByZero else Ok(a % b)
        | BinOp.Shl -> Ok(a <<< int b)
        | BinOp.Shr -> Ok(a >>> int b)
        | BinOp.And -> Ok(a &&& b)
        | BinOp.Or  -> Ok(a ||| b)
        | BinOp.Xor -> Ok(a ^^^ b)
        | BinOp.Lt  -> flag (a < b)
        | BinOp.Gt  -> flag (a > b)
        | BinOp.Le  -> flag (a <= b)
        | BinOp.Ge  -> flag (a >= b)
        | BinOp.Eq  -> flag (a = b)
        | BinOp.Ne  -> flag (a <> b))

  /// Symbols an expression mentions.
  let rec symbols expr =
    seq {
      match expr with
      | Num _ -> ()
      | Sym s -> s
      | Neg e | Not e -> yield! symbols e
      | Bin(_, l, r) ->
        yield! symbols l
        yield! symbols r
    }

  let opText op =
    match op with
    | BinOp.Add -> "+"  | BinOp.Sub -> "-"  | BinOp.Mul -> "*"  | BinOp.Div -> "/"  | BinOp.Mod -> "%"
    | BinOp.Shl -> "<<" | BinOp.Shr -> ">>" | BinOp.And -> "&"  | BinOp.Or  -> "|"  | BinOp.Xor -> "^"
    | BinOp.Lt  -> "<"  | BinOp.Gt  -> ">"  | BinOp.Le  -> "<=" | BinOp.Ge  -> ">=" | BinOp.Eq  -> "==" | BinOp.Ne -> "!="

  let rec print expr =
    match expr with
    | Num v when v < 0L -> $"-${-v:x}"
    | Num v -> $"${v:x}"
    | Sym s -> s
    | Neg e -> $"-{atom e}"
    | Not e -> $"~{atom e}"
    | Bin(op, l, r) -> $"{atom l}{opText op}{atom r}"

  and atom expr =
    match expr with
    | Bin _ -> $"({print expr})"
    | _ -> print expr

module Operand =
  let auto expr = { expr = expr; force = Auto }
  let short expr = { expr = expr; force = Short }
  let long expr = { expr = expr; force = Long }
  let num (v: int) = auto (Expr.num v)
  let sym name = auto (Sym name)

/// Two-pass assembly of Motorola-syntax sources and of DSL item lists, with asm56300's sizing
/// rule: an operand whose symbols are all defined above the instruction gets the shortest form
/// that fits; a forward reference gets the long form (the size pass 1 had to assume).
module dsp56k.Assembler

open System
open System.IO
open dsp56k.Parse

type AsmError =
  | Syntax     of file: string * line: int * error: ParseError
  | Encoding   of file: string * line: int * text: string * error: Encode.EncodeError
  | Undefined  of file: string * line: int * name: string
  | Unbalanced of file: string * line: int * what: string
  | Missing    of file: string * name: string

type Segment =
  { space : Space
    org   : int
    words : int array }

type ListingLine =
  { file    : string
    line    : int
    space   : Space
    address : int
    words   : int array
    text    : string }

type Assembled =
  { segments : Segment array
    symbols  : Map<string, int64>
    listing  : ListingLine array }

/// A source line after includes and conditionals, tagged with where it came from.
type Source =
  { file : string
    line : Line }

let errorText error =
  match error with
  | Syntax(f, n, e) -> $"{f}:{n}: {Parse.errorText e}"
  | Encoding(f, n, t, e) -> $"{f}:{n}: cannot encode '{t.Trim()}': %A{e}"
  | Undefined(f, n, s) -> $"{f}:{n}: undefined symbol '{s}'"
  | Unbalanced(f, n, w) -> $"{f}:{n}: unbalanced {w}"
  | Missing(f, i) -> $"{f}: cannot find include '{i}'"

/// Expand includes and conditional blocks, evaluating `if` against the equates defined so far.
let rec preprocess (resolveInclude: string -> string -> string option) (predefined: Map<string, int64>) (file: string) (text: string) =
  let lines = text.Replace("\r\n", "\n").Split '\n'
  let out = ResizeArray<Source>()
  let mutable equs = predefined
  let mutable error = None
  // Each open `if`: (this branch taken, an earlier branch was taken, enclosing branch active).
  let stack = System.Collections.Generic.Stack<bool * bool * bool>()
  let active () = stack.Count = 0 || (let taken, _, outer = stack.Peek() in taken && outer)
  // `define NAME 'text'`: textual substitution of whole identifiers in later lines.
  let defines = System.Collections.Generic.Dictionary<string, string>()
  let substitute (line: string) =
    if defines.Count = 0 then line
    else
      System.Text.RegularExpressions.Regex.Replace(line, @"[A-Za-z_][A-Za-z0-9_]*", fun m ->
        match defines.TryGetValue m.Value with
        | true, v -> v
        | _ -> m.Value)
  // `NAME macro [args]` ... `endm`: bodies kept as text, expanded where NAME is used as a
  // mnemonic (arguments replace `\arg` and whole-word `arg` in the body).
  let macros = System.Collections.Generic.Dictionary<string, string array * string array>()
  let mutable recording : (string * string array * ResizeArray<string>) option = None
  let rec go (lines: string array) (file: string) =
    for i in 0 .. lines.Length - 1 do
      let code, _ = stripComment lines[i]
      let fs = fields code
      let lower k = if fs.Length > k then fs[k].ToLowerInvariant() else ""
      match recording with
      | Some(name, args, body) ->
        if lower 0 = "endm" || lower 1 = "endm" then
          macros[name] <- (args, body.ToArray())
          recording <- None
        else body.Add lines[i]
      | None ->
        if error.IsNone && active () && lower 1 = "macro" then
          let args = if fs.Length > 2 then splitTop fs[2] else [||]
          recording <- Some(fs[0].TrimEnd ':', args, ResizeArray())
        elif error.IsNone && active () && fs.Length >= 1 && code.Length > 0 && Char.IsWhiteSpace code[0] && macros.ContainsKey fs[0] then
          let args, body = macros[fs[0]]
          let values = if fs.Length > 1 then splitTop fs[1] else [||]
          let expand (line: string) =
            Array.zip args (Array.truncate args.Length (Array.append values (Array.create args.Length "")))
            |> Array.fold (fun (l: string) (a, v) ->
              System.Text.RegularExpressions.Regex.Replace(l.Replace("\\" + a, v), $@"\b{a}\b", v)) line
          go (body |> Array.map expand) file
        elif error.IsNone && fs.Length >= 3 && fs[0].ToLowerInvariant() = "define" && active () then
          defines[fs[1]] <- (String.concat " " fs[2..]).Trim('\'', '"')
        elif error.IsNone then
          match parseLine (i + 1) (substitute lines[i]) with
          | Error e when active () -> error <- Some(Syntax(file, i + 1, e))
          | Error _ -> ()
          | Ok line ->
            match line.body with
            | Directive(DIf cond) ->
              let outer = active ()
              let taken =
                outer && (match Expr.eval (fun s -> Map.tryFind s equs) cond with
                          | Ok v -> v <> 0L
                          | Error _ -> false)
              stack.Push(taken, taken, outer)
            | Directive DElse ->
              if stack.Count = 0 then error <- Some(Unbalanced(file, i + 1, "else"))
              else
                let _, any, outer = stack.Pop()
                stack.Push(not any, true, outer)
            | Directive DEndif ->
              if stack.Count = 0 then error <- Some(Unbalanced(file, i + 1, "endif")) else stack.Pop() |> ignore
            | _ when not (active ()) -> ()
            | Directive(DInclude path) ->
              match resolveInclude file path with
              | Some full -> go ((File.ReadAllText full).Replace("\r\n", "\n").Split '\n') full
              | None -> error <- Some(Missing(file, path))
            | Directive(DEqu v) ->
              match line.label with
              | Some name ->
                match Expr.eval (fun s -> Map.tryFind s equs) v with
                | Ok x -> equs <- Map.add name x equs
                | Error _ -> ()
                out.Add { file = file; line = line }
              | None -> error <- Some(Syntax(file, i + 1, BadOperand("equ", "missing name")))
            | _ -> out.Add { file = file; line = line }
  go lines file
  match error with
  | Some e -> Error e
  | None when stack.Count > 0 -> Error(Unbalanced(file, lines.Length, "if"))
  | None -> Ok(out.ToArray())

/// Assemble preprocessed lines.
let assembleSources (predefined: Map<string, int64>) (sources: Source array) : Result<Assembled, AsmError> =
  // Where each symbol is defined (line index), so pass 2 can tell forward references apart.
  let definedAt = System.Collections.Generic.Dictionary<string, int>()
  for i in 0 .. sources.Length - 1 do
    match sources[i].line.label with
    | Some name when not (definedAt.ContainsKey name) -> definedAt[name] <- i
    | _ -> ()
  let settledAt index (e: Expr) =
    Expr.symbols e |> Seq.forall (fun s ->
      s = "*" || predefined.ContainsKey s || (match definedAt.TryGetValue s with
                                              | true, d -> d < index
                                              | _ -> false))
  let run (pass2: bool) (symbols: Map<string, int64>) =
    let mutable table = symbols
    let mutable space = Space.P
    let mutable pc = 0
    let segments = ResizeArray<Segment>()
    let current = ResizeArray<int>()
    let mutable segOrg = 0
    let mutable segSpace = Space.P
    let listing = ResizeArray<ListingLine>()
    let mutable error = None
    let flush () =
      if current.Count > 0 then segments.Add { space = segSpace; org = segOrg; words = current.ToArray() }
      current.Clear()
      segOrg <- pc
      segSpace <- space
    let lookup index s =
      if s = "*" then Some(int64 pc)
      else
        match Map.tryFind s table with
        | Some v -> Some v
        | None when not pass2 -> Some 0L        // pass 1: only sizes matter
        | None -> None
    for i in 0 .. sources.Length - 1 do
      if error.IsNone then
        let src = sources[i]
        let line = src.line
        let defineLabel () =
          match line.label, line.body with
          | Some name, Directive(DEqu _) -> ()
          | Some name, _ -> table <- Map.add name (int64 pc) table
          | None, _ -> ()
        let emit words =
          listing.Add { file = src.file; line = line.number; space = space; address = pc; words = words; text = line.text }
          current.AddRange words
          pc <- pc + words.Length
        match line.body with
        | Directive(DOrg(s, addr)) ->
          let target =
            match addr with
            | Some e -> Expr.eval (lookup i) e |> Result.toOption |> Option.map int
            | None -> Some 0
          space <- s
          pc <- defaultArg target 0
          flush ()
          defineLabel ()
        | Directive(DEqu v) ->
          match line.label with
          | Some name ->
            match Expr.eval (lookup i) v with
            | Ok x -> table <- Map.add name x table
            | Error(ExprError.Undefined s) -> error <- Some(Undefined(src.file, line.number, s))
            | Error _ -> ()
          | None -> ()
        | Directive(DDc values) ->
          defineLabel ()
          let words =
            values |> Array.map (fun e ->
              match Expr.eval (lookup i) e with
              | Ok v -> int (v &&& 0xffffffL)
              | Error(ExprError.Undefined s) ->
                error <- Some(Undefined(src.file, line.number, s))
                0
              | Error _ -> 0)
          emit words
        | Directive(DDs count) ->
          defineLabel ()
          match Expr.eval (lookup i) count with
          | Ok n -> emit (Array.zeroCreate (int n))
          | Error _ -> error <- Some(Syntax(src.file, line.number, BadExpression(line.text)))
        | Directive _ | Empty -> defineLabel ()
        | Instruction instr ->
          defineLabel ()
          let env : Encode.Env =
            { pc = pc
              value = fun e -> Expr.eval (lookup i) e
              settled = settledAt i }
          match Encode.encode env instr with
          | Ok words -> emit words
          | Error(Encode.BadExpr(ExprError.Undefined s)) -> error <- Some(Undefined(src.file, line.number, s))
          | Error e -> error <- Some(Encoding(src.file, line.number, line.text, e))
    flush ()
    match error with
    | Some e -> Error e
    | None -> Ok({ segments = segments.ToArray(); symbols = table; listing = listing.ToArray() })
  match run false predefined with
  | Error e -> Error e
  | Ok first -> run true first.symbols

/// Assemble a source file; includes resolve against the including file's folder, then `includeDirs`.
let assembleFile (includeDirs: string array) (predefined: Map<string, int64>) (path: string) =
  let resolve (from: string) (name: string) =
    let here = string (Path.GetDirectoryName(Path.GetFullPath from))
    let candidates =
      [| Path.Combine(here, name)
         yield! includeDirs |> Array.map (fun d -> Path.Combine(d, name)) |]
    candidates |> Array.tryFind File.Exists
  preprocess resolve predefined path (File.ReadAllText path)
  |> Result.bind (assembleSources predefined)

let assembleText (predefined: Map<string, int64>) (name: string) (text: string) =
  preprocess (fun _ _ -> None) predefined name text
  |> Result.bind (assembleSources predefined)

/// Assemble DSL items directly (no text round trip).
let assembleItems (items: Item array) =
  let toLine (n: int) item =
    let mk label body = { number = n; text = Print.item item; label = label; body = body; comment = None }
    match item with
    | Item.Label name -> mk (Some name) Empty
    | Op i -> mk None (Instruction i)
    | Item.Dc values -> mk None (Directive(DDc values))
    | Item.Ds count -> mk None (Directive(DDs count))
    | Item.Equ(name, v) -> mk (Some name) (Directive(DEqu v))
    | Item.Org(s, a) -> mk None (Directive(DOrg(s, Some a)))
    | Item.Comment _ -> mk None Empty
  items
  |> Array.mapi (fun i it -> { file = "<dsl>"; line = toLine (i + 1) it })
  |> assembleSources Map.empty

/// The words of one memory space, laid out from `org` (gaps zero-filled).
let image (space: Space) (result: Assembled) =
  let segs = result.segments |> Array.filter (fun s -> s.space = space && s.words.Length > 0)
  if segs.Length = 0 then 0, [||]
  else
    let lo = segs |> Array.map _.org |> Array.min
    let hi = segs |> Array.map (fun s -> s.org + s.words.Length) |> Array.max
    let img = Array.zeroCreate (hi - lo)
    for s in segs do Array.blit s.words 0 img (s.org - lo) s.words.Length
    lo, img

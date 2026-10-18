/// A small JSON tree, its writer and its reader, with nothing but the core libraries (no System.Text.Json, no reflection) so
/// it compiles on .NET and under Fable. The typed tree is written with it (TypedTreeJson.fs) and read back with it (FromJson.fs).
module dsp56k.kernel.Json

open System

type J =
  | Str of string
  | Int of int
  | Bool of bool
  | Null
  | Arr of J list
  | Obj of (string * J) list

let private escape (s: string) =
  let sb = System.Text.StringBuilder()
  sb.Append('"') |> ignore
  for c in s do
    if c = '"' then sb.Append("\\\"") |> ignore
    elif c = '\\' then sb.Append("\\\\") |> ignore
    elif c = '\n' then sb.Append("\\n") |> ignore
    elif c = '\r' then sb.Append("\\r") |> ignore
    elif c = '\t' then sb.Append("\\t") |> ignore
    elif c < ' ' then sb.Append(' ') |> ignore
    else sb.Append(c) |> ignore
  sb.Append('"') |> ignore
  sb.ToString()

let rec private write (sb: System.Text.StringBuilder) (j: J) =
  match j with
  | Str s -> sb.Append(escape s) |> ignore
  | Int n -> sb.Append(string n) |> ignore
  | Bool b -> sb.Append(if b then "true" else "false") |> ignore
  | Null -> sb.Append("null") |> ignore
  | Arr items ->
    sb.Append('[') |> ignore
    items |> List.iteri (fun i x -> (if i > 0 then sb.Append(',') |> ignore); write sb x)
    sb.Append(']') |> ignore
  | Obj fields ->
    sb.Append('{') |> ignore
    fields |> List.iteri (fun i (k, v) -> (if i > 0 then sb.Append(',') |> ignore); sb.Append(escape k).Append(':') |> ignore; write sb v)
    sb.Append('}') |> ignore

let toText (j: J) =
  let sb = System.Text.StringBuilder()
  write sb j
  sb.ToString()

/// Reads JSON text (numbers are whole: the typed tree has no others).
let parse (text: string) : J =
  let mutable i = 0
  let fail (what: string) : 'a = failwith (what + " at " + string i)
  let skip () = while i < text.Length && (text[i] = ' ' || text[i] = '\n' || text[i] = '\r' || text[i] = '\t') do i <- i + 1
  let expect (c: char) = if i < text.Length && text[i] = c then i <- i + 1 else fail ("expected " + string c)
  let hex (c: char) =
    if c >= '0' && c <= '9' then int c - int '0'
    elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
    elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
    else fail "bad hex digit"
  let str () =
    expect '"'
    let sb = System.Text.StringBuilder()
    while i < text.Length && text[i] <> '"' do
      let c = text[i]
      if c = '\\' then
        i <- i + 1
        let e = text[i]
        if e = 'n' then sb.Append('\n') |> ignore
        elif e = 'r' then sb.Append('\r') |> ignore
        elif e = 't' then sb.Append('\t') |> ignore
        elif e = 'u' then
          sb.Append(char (hex text[i + 1] * 4096 + hex text[i + 2] * 256 + hex text[i + 3] * 16 + hex text[i + 4])) |> ignore
          i <- i + 4
        else sb.Append(e) |> ignore
      else sb.Append(c) |> ignore
      i <- i + 1
    expect '"'
    sb.ToString()
  let rec value () : J =
    skip ()
    if i >= text.Length then fail "unexpected end"
    let c = text[i]
    if c = '"' then Str(str ())
    elif c = '[' then
      i <- i + 1
      let items = ResizeArray<J>()
      skip ()
      if text[i] = ']' then i <- i + 1
      else
        let mutable go = true
        while go do
          items.Add(value ())
          skip ()
          if text[i] = ',' then i <- i + 1 else (expect ']'; go <- false)
      Arr(List.ofSeq items)
    elif c = '{' then
      i <- i + 1
      let fields = ResizeArray<string * J>()
      skip ()
      if text[i] = '}' then i <- i + 1
      else
        let mutable go = true
        while go do
          skip ()
          let k = str ()
          skip ()
          expect ':'
          fields.Add((k, value ()))
          skip ()
          if text[i] = ',' then i <- i + 1 else (expect '}'; go <- false)
      Obj(List.ofSeq fields)
    elif c = 't' then (i <- i + 4; Bool true)
    elif c = 'f' then (i <- i + 5; Bool false)
    elif c = 'n' then (i <- i + 4; Null)
    else
      let start = i
      if c = '-' then i <- i + 1
      while i < text.Length && text[i] >= '0' && text[i] <= '9' do i <- i + 1
      if i = start then fail "unexpected character"
      Int(int (text.Substring(start, i - start)))
  value ()

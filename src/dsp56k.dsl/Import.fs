/// Motorola listings to DSL source: includes expanded and conditionals resolved for one variant,
/// every instruction, label, equate, data line and comment kept in order.
module dsp56k.dsl.Import
open dsp56k.dsl.Asm

open dsp56k
open dsp56k.Parse

/// The items of one preprocessed line (a label, then what follows it), and its comment.
let lineItems (line: Line) =
  [| match line.label, line.body with
     | Some name, Directive(DEqu v) -> Equ(name, v)
     | Some name, _ -> Label name
     | None, _ -> ()
     match line.body with
     | Instruction i -> Op i
     | Directive(DDc values) -> Dc values
     | Directive(DDs count) -> Ds count
     | Directive(DOrg(s, a)) -> Org(s, defaultArg a (Num 0L))
     | _ -> () |],
  line.comment

/// The DSL items of preprocessed sources (comments dropped).
let items (sources: Assembler.Source array) =
  sources |> Array.collect (fun s -> fst (lineItems s.line))

/// DSL source for preprocessed sources: one item per line, comments carried as F# comments.
let source (moduleName: string) (header: string array) (sources: Assembler.Source array) =
  let body =
    [| for s in sources do
         let its, comment = lineItems s.line
         let c = comment |> Option.filter (fun t -> t <> "") |> Option.map (fun t -> "// " + t)
         match its, c with
         | [||], Some c -> "  " + c
         | [||], None -> ""
         | _ ->
           for k in 0 .. its.Length - 1 do
             let code = "  " + Transcribe.item its[k]
             match c with
             | Some c when k = its.Length - 1 -> $"{code,-56} {c}"
             | _ -> code |]
  [| for h in header do
       $"// {h}"
     $"module {moduleName}"
     ""
     "open dsp56k"
     "open dsp56k.dsl"
     "open dsp56k.dsl.Asm"
     ""
     "let program = [|"
     yield! body
     "|]"
     "" |]
  |> String.concat "\n"

/// Read a listing (includes resolved against its folder and `includeDirs`) for one set of
/// predefined symbols.
let file (includeDirs: string array) (predefined: Map<string, int64>) (path: string) =
  let resolve (from: string) (name: string) =
    let here = string (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath from))
    [| System.IO.Path.Combine(here, name); yield! includeDirs |> Array.map (fun d -> System.IO.Path.Combine(d, name)) |]
    |> Array.tryFind System.IO.File.Exists
  Assembler.preprocess resolve predefined path (System.IO.File.ReadAllText path)

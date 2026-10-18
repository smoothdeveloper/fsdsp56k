/// A front end that reads the kernel's source through the F# compiler's typed tree (FSharp.Compiler.Service), not through
/// quotations: the file is checked, its functions converted into the compiler's tree, and the generator's program is
/// compared with the one the quotation front end gives for the same kernel.
module dsp56k.tests.TypedTreeFrontEndTests

open System
open System.IO
open Expecto
open dsp56k
open dsp56k.dsl
open dsp56k.dsl.Asm
open dsp56k.kernel

let samples = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "Kernels"))

/// The items without the comments that quote the F# source.
let code (items: Item array) = items |> Array.filter (fun it -> match it with Comment _ -> false | _ -> true)

/// Checks the files, reads the three functions as a kernel, and compares what the generator emits with the quotation kernel's.
let compare (files: string list) (name: string) (quoted: Kernel.Kernel) (target: Kernel.Target) =
  match FromTypedTree.check (files |> List.map (fun f -> Path.Combine(samples, f))) [] with
  | Error e -> failtest $"checking {files}: {e}"
  | Ok checked' ->
    match FromTypedTree.kernel checked' name ("init", "trig", "render") with
    | Error e -> failtest e
    | Ok ir ->
      match Compile.generateIr target ir, Compile.generate target quoted with
      | Ok a, Ok b ->
        Expect.equal (Program.source (code a.items)) (Program.source (code b.items)) "the same program"
        Expect.equal a.layout b.layout "the same layout"
      | Error e, _ -> failtest $"typed tree: {e}"
      | _, Error e -> failtest $"quotations: {e}"

let sineTests =
  testList "kernel IR, the typed tree front end" [
    for target in Kernel.hostTargets do
      test $"Sine.fs through the typed tree compiles as through quotations ({target.name})" {
        compare [ "Sine.fs" ] "sine" global.samples.kernels.Sine.kernel target
      }
  ]

/// Every kernel of tests/dsp56k.tests/Kernels read from its source file (and the tables file next to it, if there is one) through the
/// typed tree, against the same kernel read through quotations: the outcome of each, one line per kernel and target.
let sweep (target: Kernel.Target) =
  [ for k in KernelIrTests.kernels () do
      let t = k.init.DeclaringType
      if not (isNull t) && t.FullName.StartsWith "samples.kernels." && not (t.FullName.EndsWith "Tables") then
        let file = t.FullName.Substring("samples.kernels.".Length)
        let files = [ file + "Tables"; file ] |> List.filter (fun f -> File.Exists(Path.Combine(samples, f + ".fs")))
        let names = (k.init.Name, k.trig.Name, k.render.Name)
        let outcome =
          match FromTypedTree.check (files |> List.map (fun f -> Path.Combine(samples, f + ".fs"))) [] with
          | Error e -> $"check: {e}"
          | Ok checked' ->
            match FromTypedTree.kernel checked' k.name names with
            | Error e -> $"read: {e}"
            | Ok ir ->
              match (try Compile.generateIr target ir with e -> Error $"raised {e.GetType().Name}: {e.Message}"), Compile.generate target k with
              | Ok a, Ok b -> if Program.source (code a.items) = Program.source (code b.items) then "same" else "DIFFERENT"
              | Error e, Ok _ -> $"typed tree refused: {e}"
              | Ok _, Error _ -> "typed tree compiled, quotations refused"
              | Error _, Error _ -> "both refuse"
        yield $"{k.name}\t{target.name}\t{files}\t{outcome.Replace('\n', ' ')}" ]

let sweepTest =
  test "the fixture kernels, kernel by kernel" {
    let lines = [ for target in Kernel.hostTargets do yield! sweep target ]
    for l in lines do printfn "SWEEP %s" (if l.Length > 400 then l.Substring(0, 400) else l)
    Expect.isGreaterThan lines.Length 10 "the fixture kernels were found"
    Expect.equal (lines |> List.filter (fun l -> not (l.EndsWith "same") && not (l.Contains "\t[]\t"))) [] "every kernel compiles to the program of the quotation front end"
  }

/// The typed tree of a file as JSON text (TypedTreeJson, the code that also runs inside Fable.Standalone): valid JSON, with the
/// members the kernel calls listed once. DUMP_TYPED_TREE_JSON=dir writes it, to compare with what the standalone compiler gives.
let jsonTest =
  test "Sine.fs as JSON" {
    match FromTypedTree.check [ Path.Combine(samples, "Sine.fs") ] [] with
    | Error e -> failtest e
    | Ok c ->
      let text = TypedTreeJson.ofFiles c.contents
      match Environment.GetEnvironmentVariable "DUMP_TYPED_TREE_JSON" with
      | null | "" -> ()
      | dir -> File.WriteAllText(Path.Combine(dir, "Sine.dotnet.json"), text)
      use doc = System.Text.Json.JsonDocument.Parse text
      let members = [ for m in doc.RootElement.GetProperty("members").EnumerateArray() -> m.GetProperty("owner").GetString() + "." + m.GetProperty("name").GetString() ]
      Expect.contains members "dsp56k.kernel.Long.zero" "the call of Long.zero is listed"
      let texts = [ for m in doc.RootElement.GetProperty("members").EnumerateArray() -> m.GetRawText() ]
      Expect.equal (texts |> List.distinct |> List.length) texts.Length "each member once"
  }

/// The same kernels once more, now through the JSON: the source checked, its typed tree written as JSON text, the text read
/// back into the kernel IR (FromJson, which holds nothing of the F# compiler) and compiled; the program must be the one the
/// quotations give.
let jsonSweepFrom (jsonOf: string list -> string -> Result<string, string>) (target: Kernel.Target) =
  [ for k in KernelIrTests.kernels () do
      let t = k.init.DeclaringType
      if not (isNull t) && t.FullName.StartsWith "samples.kernels." && not (t.FullName.EndsWith "Tables") then
        let file = t.FullName.Substring("samples.kernels.".Length)
        let files = [ file + "Tables"; file ] |> List.filter (fun f -> File.Exists(Path.Combine(samples, f + ".fs")))
        let outcome =
          match jsonOf files file with
          | Error e -> $"json: {e}"
          | Ok text ->
            match ReflectionHost.kernel text t.FullName k.name (k.init.Name, k.trig.Name, k.render.Name) with
            | Error e -> "read: " + e
            | Ok ir ->
              match (try Compile.generateIr target ir with e -> Error $"raised {e.GetType().Name}: {e.Message}"), Compile.generate target k with
              | Ok a, Ok b -> if Program.source (code a.items) = Program.source (code b.items) then "same" else "DIFFERENT"
              | Error e, Ok _ -> $"json reader refused: {e}"
              | Ok _, Error _ -> "json reader compiled, quotations refused"
              | Error _, Error _ -> "both refuse"
        yield $"{k.name}\t{target.name}\t{files}\t{outcome.Replace('\n', ' ')}" ]

/// The JSON of .NET's F# compiler.
let jsonFromDotnet (files: string list) (_: string) =
  match FromTypedTree.check (files |> List.map (fun f -> Path.Combine(samples, f + ".fs"))) [] with
  | Error e -> Error e
  | Ok checked' -> Ok(TypedTreeJson.ofFiles checked'.contents)

let jsonSweepTest =
  test "the fixture kernels through the typed tree JSON" {
    let lines = [ for target in Kernel.hostTargets do yield! jsonSweepFrom jsonFromDotnet target ]
    for l in lines do printfn "JSONSWEEP %s" (if l.Length > 400 then l.Substring(0, 400) else l)
    Expect.isGreaterThan lines.Length 10 "the fixture kernels were found"
    Expect.equal (lines |> List.filter (fun l -> not (l.EndsWith "same") && not (l.Contains "\t[]\t"))) [] "every kernel compiles to the program of the quotation front end"
  }

/// The JSON the browser's compiler gave (Fable.Standalone, tools/typed-tree-standalone, run in node), one file per kernel source
/// named like it (`Sine.json`, `Tx6Bd.json`, the tables first in the project), in the folder TYPED_TREE_JSON_DIR names: the
/// kernels read from it must compile to the program of the quotations. Not run when the variable is unset.
let standaloneJsonTest =
  test "the fixture kernels through the JSON of the browser's compiler" {
    match Environment.GetEnvironmentVariable "TYPED_TREE_JSON_DIR" with
    | null | "" -> printfn "STANDALONE skipped (TYPED_TREE_JSON_DIR is not set)"
    | dir ->
      let fromStandalone (_: string list) (file: string) =
        let path = Path.Combine(dir, file + ".json")
        if File.Exists path then Ok(File.ReadAllText path) else Error($"no {path}")
      let lines = [ for target in Kernel.hostTargets do yield! jsonSweepFrom fromStandalone target ]
      for l in lines do printfn "STANDALONE %s" (if l.Length > 400 then l.Substring(0, 400) else l)
      Expect.isGreaterThan lines.Length 10 "the fixture kernels were found"
      Expect.equal (lines |> List.filter (fun l -> not (l.EndsWith "same") && not (l.Contains "\t[]\t"))) [] "every kernel compiles to the program of the quotation front end"
  }

let tests = testList "kernel IR, the typed tree front end" [ sineTests; sweepTest; jsonTest; jsonSweepTest; standaloneJsonTest ]

/// What the kernel compiler generates, kept as a golden file: every `Kernel.define` value of the test and sample
/// assemblies, compiled for every host by the code generator alone (`Compile.generate`, before any optimisation pass),
/// as its item count and a hash of its listing, or the error it is refused with. A change to how kernels are read (a new
/// front end, a different intermediate form) must leave this file as it is; a change to the generator itself regenerates it
/// (`UPDATE_GOLDEN=1 dotnet run --project tests/dsp56k.tests`) and the diff is what the change did.
module dsp56k.tests.KernelIrTests

open System
open System.IO
open System.Reflection
open System.Security.Cryptography
open System.Text
open Expecto
open dsp56k
open dsp56k.dsl
open dsp56k.dsl.Asm
open dsp56k.kernel

let goldenFile = Path.Combine(__SOURCE_DIRECTORY__, "golden", "kernel-compile.tsv")

/// Every Kernel value (or list of them) held by a static property of the assemblies' types, by name.
let kernels () : Kernel.Kernel list =
  let assemblies =
    [ typeof<dsp56k.tests.KernelTests.Probes.P>.Assembly
      typeof<samples.kernels.Sine.Params>.Assembly ]
    |> List.distinct
  let flags = BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic
  let found =
    [ for asm in assemblies do
        for t in (try asm.GetTypes() with :? ReflectionTypeLoadException as e -> e.Types |> Array.filter (fun t -> not (isNull t))) do
          for p in t.GetProperties flags do
            if p.GetIndexParameters().Length = 0 && p.CanRead then
              if p.PropertyType = typeof<Kernel.Kernel> then
                match (try p.GetValue null with _ -> null) with
                | :? Kernel.Kernel as k -> yield k
                | _ -> ()
              elif p.PropertyType = typeof<Kernel.Kernel list> then
                match (try p.GetValue null with _ -> null) with
                | :? (Kernel.Kernel list) as ks -> yield! ks
                | _ -> () ]
  found |> List.distinctBy _.name |> List.sortBy _.name

let sha (text: string) =
  Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes text)).Substring(0, 16).ToLowerInvariant()

/// A refusal as it is kept: the expression some messages print ("not Call (Some (out), Write, ...)") is cut, its printed form
/// belongs to the expression type, not to what the compiler decides.
let refusal (m: string) =
  let m = m.Replace('\n', ' ').Replace('\t', ' ')
  System.Text.RegularExpressions.Regex.Replace(m, @"\b(not|got) (Call|Value|Let|Var|Sequential|IfThenElse|PropertyGet|PropertySet|Lambda|Application|ForIntegerRangeLoop|VarSet|WhileLoop)\b.*$", "$1 <expression>")

/// One golden line: kernel, host, then `ok items listingHash` or `error messageHash message`.
let line (host: Kernel.KernelHost) (k: Kernel.Kernel) =
  let target = host.target
  let outcome =
    try
      match Compile.generate target k with
      | Ok c -> $"ok\t{c.items.Length}\t{sha (Program.source c.items)}"
      | Error m -> let m = refusal m in $"error\t-\t{sha m}\t{m}"
    with e -> $"raised\t-\t{sha (string e.Message)}\t{e.GetType().Name}"
  $"{k.name}\t{host.key}\t{outcome}"

/// DUMP_LISTINGS=dir writes each listing there (to see what a change did to a kernel).
let dump (dir: string) =
  Directory.CreateDirectory dir |> ignore
  for k in kernels () do
    for target in Kernel.hostTargets do
      let text =
        try
          match Compile.generate target k with
          | Ok c -> Program.source c.items
          | Error m -> "error: " + m
        with e -> "raised: " + e.Message
      File.WriteAllText(Path.Combine(dir, $"{k.name}.{target.name.Replace(' ', '-')}.txt"), text)

let current () =
  match Environment.GetEnvironmentVariable "DUMP_LISTINGS" with
  | null | "" -> ()
  | dir -> dump dir
  [ for k in kernels () do
      for host in Kernel.hosts do
        yield line host k ]

let tests =
  testList "kernel compile golden" [
    test "every kernel compiles as it did" {
      let lines = current ()
      if Environment.GetEnvironmentVariable "UPDATE_GOLDEN" = "1" then
        File.WriteAllLines(goldenFile, lines)
      let expected = File.ReadAllLines goldenFile |> Array.toList
      let differing = List.zip (List.truncate expected.Length lines) (List.truncate lines.Length expected) |> List.filter (fun (a, b) -> a <> b)
      Expect.isEmpty differing $"lines that differ (now, golden), first: {List.tryHead differing}"
      Expect.equal lines.Length expected.Length "the kernels found, against the golden file's"
    }
  ]

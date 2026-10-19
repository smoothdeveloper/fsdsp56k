/// Stage existing sample sources without their quotation-only registration, never edit originals.
open System
open System.IO
open System.Text.RegularExpressions
open System.Security.Cryptography

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let output = Path.Combine(root, "out", "portable", "sources")
Directory.CreateDirectory output |> ignore
let sources = [| "Sine"; "Pluck"; "InputThru" |]
let manifest = ResizeArray<string>()
manifest.Add "source\tsha256"
for index, name in Array.indexed sources do
  let relative = $"tests/dsp56k.tests/Kernels/{name}.fs"
  let path = Path.Combine(root, relative)
  let source = File.ReadAllText path
  let registration = @"(?m)^let kernel = Kernel\.define [^\r\n]+<@[^\r\n]+$"
  if Regex.Matches(source, registration).Count <> 1 then
    failwith $"{relative}: expected one single-line kernel registration; update staging explicitly"
  let staged = Regex.Replace(source, registration, "// Quotation registration belongs only to the DSP compiler host.")
  if staged.Contains "<@" then failwith $"{relative}: unstaged quotation"
  File.WriteAllText(Path.Combine(output, name + ".fs"), staged)
  manifest.Add $"{relative}\t{SHA256.HashData(File.ReadAllBytes path) |> Convert.ToHexString}"
  printfn "[%d/%d] Staged %s" (index + 1) sources.Length relative
File.WriteAllLines(Path.Combine(root, "out", "portable", "sources.tsv"), manifest)

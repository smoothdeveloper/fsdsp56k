/// Stage existing sample sources without their quotation-only registration, never edit originals.
open System
open System.IO
open System.Text.RegularExpressions
open System.Security.Cryptography

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let output = Path.Combine(root, "out", "portable", "sources")
Directory.CreateDirectory output |> ignore
let sources = [| "Sine"; "Pluck"; "InputThru"; "Tx6Bd"; "Tx6Tom"; "Tx6Metal"; "Tx6Sd"; "Tx6Cp" |]
// TX6's tables are plain data, staged as they are, ahead of the kernel that reads them.
let tables  = sources |> Array.filter (fun name -> name.StartsWith "Tx6") |> Array.map (fun name -> name + "Tables")
let manifest = ResizeArray<string>()
manifest.Add "source\tsha256"
for index, name in Array.indexed sources do
  let relative = $"tests/dsp56k.tests/Kernels/{name}.fs"
  let path = Path.Combine(root, relative)
  let source = File.ReadAllText path
  // one registration per kernel: Tx6Tom and Tx6Metal register a kernel per voice (ltKernel, chKernel, ...)
  let registration = @"(?m)^let [a-z]*[kK]ernel = Kernel\.define [^\r\n]+<@[^\r\n]+$"
  if Regex.Matches(source, registration).Count < 1 then
    failwith $"{relative}: expected single-line kernel registrations; update staging explicitly"
  // Unit annotations and their identity cast are compile-time metadata only.
  let numerical = Regex.Replace(source, @"Frac<(Hz|ratio)>", "Frac")
  let numerical = numerical.Replace("open dsp56k.kernel.Units", "").Replace("plain ", "")
  let staged = Regex.Replace(numerical, registration, "// Quotation registration belongs only to the DSP compiler host.")
  if staged.Contains "<@" then failwith $"{relative}: unstaged quotation"
  File.WriteAllText(Path.Combine(output, name + ".fs"), staged)
  manifest.Add $"{relative}\t{SHA256.HashData(File.ReadAllBytes path) |> Convert.ToHexString}"
  printfn "[%d/%d] Staged %s" (index + 1) sources.Length relative
for name in tables do
  let relative = $"tests/dsp56k.tests/Kernels/{name}.fs"
  let path     = Path.Combine(root, relative)
  File.Copy(path, Path.Combine(output, name + ".fs"), true)
  manifest.Add $"{relative}\t{SHA256.HashData(File.ReadAllBytes path) |> Convert.ToHexString}"
File.WriteAllLines(Path.Combine(root, "out", "portable", "sources.tsv"), manifest)

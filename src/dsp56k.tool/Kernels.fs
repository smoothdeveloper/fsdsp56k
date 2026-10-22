/// Kernels from F# source for run, stress, audition and grade: a project (.fsproj), a script (.fsx) or one
/// file (.fs) is built, its assembly loaded, and its kernels (module values of type Kernel.Kernel)
/// found by reflection; the quotation front end then compiles the chosen one for its host.
///
/// TODO: the typed-tree front end (dsp56k.kernel.typedtree) reads source without building it; a
/// project goes through the build and the quotations here, like the other two.
module dsp56k.tool.Kernels

open System
open System.Diagnostics
open System.IO
open System.Reflection
open Microsoft.FSharp.Reflection
open dsp56k.kernel
open machinedrum

let isSource (path: string) =
  match (string (Path.GetExtension path)).ToLowerInvariant() with
  | ".fsproj" | ".fsx" | ".fs" -> true
  | _ -> false

/// The host named on the command line: a key of `Kernel.hosts`, or md.
let host (name: string) =
  match name.ToLowerInvariant() with
  | "md" -> Ok Kernel.KernelHost.Machinedrum
  | n ->
    match Kernel.hosts |> List.tryFind (fun h -> h.key = n) with
    | Some h -> Ok h
    | None -> Error $"""unknown host '{name}': {Kernel.hosts |> List.map (fun h -> h.key) |> String.concat ", "} (or md)"""

/// `dotnet` with arguments, in the project's folder, its output kept; Ok on exit code 0.
let private dotnet (dir: string) (args: string list) =
  let info = ProcessStartInfo("dotnet", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = dir)
  for a in args do info.ArgumentList.Add a
  use p = match Process.Start info with null -> failwith "dotnet did not start" | p -> p
  let out = p.StandardOutput.ReadToEndAsync()
  let err = p.StandardError.ReadToEnd()
  p.WaitForExit()
  if p.ExitCode = 0 then Ok out.Result
  else Error $"""dotnet {String.concat " " args} failed:{Environment.NewLine}{out.Result}{err}"""

/// The toolchain's own assemblies beside the tool, which a kernel compiles against (and binds to
/// when loaded): Dsp56k.*, Machinedrum.* and the FSharp.Core they were built with.
let private references () =
  [ for f in Directory.GetFiles(AppContext.BaseDirectory, "*.dll") do
      let n = string (Path.GetFileName f)
      if n.StartsWith "Dsp56k." || n.StartsWith "Machinedrum." || n = "FSharp.Core.dll" then yield f ]

/// A project around one source file, in a scratch folder per file and its `defines` (conditional
/// compilation symbols, as `#if HAND_SHAPE` reads them), referencing the toolchain.
let private wrap (source: string) (defines: string list) =
  let full = Path.GetFullPath source
  let symbols = String.concat ";" defines
  let dir = Path.Combine(Path.GetTempPath(), "fsdsp56k-temp-builds", (GradeCache.shaText (full + "|" + symbols)).Substring(0, 16))
  Directory.CreateDirectory dir |> ignore
  let text = File.ReadAllText full
  let compiled =
    if full.EndsWith(".fsx", StringComparison.OrdinalIgnoreCase) then
      // a script: its #r and #load name the toolchain the project references already
      let lines = text.Split('\n') |> Array.filter (fun l -> not (l.TrimStart().StartsWith "#r" || l.TrimStart().StartsWith "#load" || l.TrimStart().StartsWith "#I"))
      let body = String.Join("\n", lines)
      let declared = lines |> Array.exists (fun l -> l.StartsWith "module " || l.StartsWith "namespace " || l.StartsWith "[<ReflectedDefinition>] module" || l.StartsWith "[<ReflectedDefinition>]")
      let file = Path.Combine(dir, "Script.fs")
      let stem = string (Path.GetFileNameWithoutExtension full)
      let name = String(stem |> Seq.filter Char.IsLetterOrDigit |> Array.ofSeq)
      File.WriteAllText(file, (if declared then body else $"[<ReflectedDefinition>]\nmodule script.{name}\n\n{body}"))
      file
    else full
  let refs = references () |> List.map (fun f -> $"""    <Reference Include="{Path.GetFileNameWithoutExtension f}"><HintPath>{f}</HintPath><Private>false</Private></Reference>""")
  let project =
    String.concat "\n"
      [ """<Project Sdk="Microsoft.NET.Sdk">"""
        "  <PropertyGroup>"
        "    <TargetFramework>net10.0</TargetFramework>"
        "    <DisableImplicitFSharpCoreReference>true</DisableImplicitFSharpCoreReference>"
        "    <AssemblyName>fsdsp56k.tempbuild</AssemblyName>"
        "    <NoWarn>FS0988;FS3370</NoWarn>"
        if symbols <> "" then $"    <DefineConstants>$(DefineConstants);{symbols}</DefineConstants>"
        "  </PropertyGroup>"
        "  <ItemGroup>"
        $"""    <Compile Include="{compiled}" />"""
        "  </ItemGroup>"
        "  <ItemGroup>"
        yield! refs
        "  </ItemGroup>"
        "</Project>" ]
  let path = Path.Combine(dir, "kernels.fsproj")
  if not (File.Exists path) || File.ReadAllText path <> project then File.WriteAllText(path, project)
  path

/// Build the project (or the one wrapped around a file, with `defines`) and return the assembly it makes.
let build (source: string) (defines: string list) =
  let isProject = source.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
  if isProject && not defines.IsEmpty then Error "--define is for a .fs or .fsx kernel: a project sets its own DefineConstants"
  else
    let project = if isProject then Path.GetFullPath source else wrap source defines
    let dir = string (Path.GetDirectoryName project)
    dotnet dir [ "build"; project; "-c"; "Debug"; "-nologo"; "-v:q" ]
    |> Result.bind (fun _ -> dotnet dir [ "msbuild"; project; "-getProperty:TargetPath"; "-nologo" ])
    |> Result.map (fun out -> out.Trim())

/// The kernels an assembly defines: its static properties of type Kernel.Kernel.
let kernelsIn (assembly: Assembly) =
  let types = try assembly.GetTypes() with :? ReflectionTypeLoadException as e -> e.Types |> Array.choose Option.ofObj
  [ for t in types do
      for p in t.GetProperties(BindingFlags.Public ||| BindingFlags.Static) do
        if p.PropertyType = typeof<Kernel.Kernel> then
          match p.GetValue null with
          | :? Kernel.Kernel as k -> yield k
          | _ -> () ]

/// The kernel to play: the one named, or the only one.
let choose (name: string option) (kernels: Kernel.Kernel list) =
  match name, kernels with
  | _, [] -> Error "no kernel found (a module value made with Kernel.define)"
  | None, [ k ] -> Ok k
  | None, ks -> Error $"""several kernels: {ks |> List.map (fun k -> k.name) |> String.concat ", "}; pick one with --kernel"""
  | Some n, ks ->
    match ks |> List.tryFind (fun k -> k.name = n) with
    | Some k -> Ok k
    | None -> Error $"""no kernel '{n}' among {ks |> List.map (fun k -> k.name) |> String.concat ", "}"""

/// A machine for the kernel when no definition is given: one control per parameter field, unipolar,
/// at mid range, the track limit as the declared cycles.
/// TODO: controls from attributes on the fields (transform, range, default); the measured worst case
/// as the declared cycles.
let machineFor (k: Kernel.Kernel) =
  let short = String(k.name.ToUpperInvariant() |> Seq.filter Char.IsLetterOrDigit |> Array.ofSeq)
  let short = if short.Length = 0 then "K" else short
  { Mds.machine $"org.fsdsp56k.kernel.{k.name}" (short.Substring(0, min 12 short.Length)) "USR" (short.Substring(0, min 2 short.Length)) with
      parameters =
        [| for f in FSharpType.GetRecordFields k.paramsType ->
             let n = f.Name.ToUpperInvariant()
             { Mds.param (n.Substring(0, min 4 n.Length)) Mds.UnipolarQ23 with defaultValue = 64 } |]
      maxTrackCycles = Abi.trackCycleLimit }

/// The kernel in `source` (built with `defines`), and its machine: the definition in `json`, or
/// one made from its parameter fields.
let kernelAndMachine (source: string) (defines: string list) (name: string option) (json: string option) =
  build source defines
  |> Result.bind (fun path ->
    let assembly = Assembly.LoadFrom path
    choose name (kernelsIn assembly))
  |> Result.bind (fun k ->
    match json with
    | Some j -> Mds.fromJson (File.ReadAllText j) |> Result.mapError (sprintf "%A") |> Result.map (fun m -> k, m)
    | None -> Ok(k, machineFor k))

/// The kernel in `source` compiled for `host`, as a package and its machine.
let load (host: Kernel.KernelHost) (source: string) (defines: string list) (name: string option) (json: string option) =
  kernelAndMachine source defines name json
  |> Result.bind (fun (k, m) ->
    Optimize.compile Optimize.Peephole host.target k
    |> Result.mapError (sprintf "%A")
    |> Result.bind (Grade.build m)
    |> Result.map (fun built -> k.name, built.package, built.machine))

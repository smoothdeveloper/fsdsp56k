open System
open System.IO
open System.Diagnostics

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let scratch = Path.Combine(root, ".bench/wasm")
let upstream = Path.Combine(scratch, "upstream")
if String.IsNullOrEmpty(Environment.GetEnvironmentVariable "EM_CACHE") then
  let cache = Path.Combine(scratch, "emscripten-cache")
  Directory.CreateDirectory cache |> ignore
  Environment.SetEnvironmentVariable("EM_CACHE", cache)
let arguments = fsi.CommandLineArgs |> Array.skip 1
let checkout =
  match arguments with
  | [| path |] -> Path.GetFullPath path
  | [||] -> Path.GetFullPath(Path.Combine(root, "../../dsp56300/dsp56300"))
  | _ -> failwith "Usage: dotnet fsi wasm/build.fsx [dsp56300 checkout (default ../../dsp56300/dsp56300)]"

let run directory command arguments =
  let start = ProcessStartInfo(command, WorkingDirectory = directory)
  arguments |> List.iter start.ArgumentList.Add
  use child = Process.Start start
  child.WaitForExit()
  if child.ExitCode <> 0 then failwith $"{command} failed ({child.ExitCode})"

let output directory command arguments =
  let start = ProcessStartInfo(command, WorkingDirectory = directory, RedirectStandardOutput = true)
  arguments |> List.iter start.ArgumentList.Add
  use child = Process.Start start
  let text = child.StandardOutput.ReadToEnd()
  child.WaitForExit()
  if child.ExitCode <> 0 then failwith $"{command} failed ({child.ExitCode})"
  text.Trim()

/// The DSP56300 sources: a dsp56300 checkout (github.com/dsp56300/dsp56300) at the commit checked out, with its
/// asmjit submodule.
if not (File.Exists(Path.Combine(checkout, "source/dsp56kEmu/dsp.cpp"))) then
  failwith $"{checkout} is not a dsp56300 checkout (no source/dsp56kEmu/dsp.cpp). Usage: dotnet fsi wasm/build.fsx [dsp56300 checkout]"
let revision = output checkout "git" [ "rev-parse"; "HEAD" ]
let origin = $"https://github.com/dsp56300/dsp56300 {revision}"

if Directory.Exists upstream then Directory.Delete(upstream, true)
Directory.CreateDirectory upstream |> ignore
Directory.CreateDirectory(Path.Combine(root, "out/wasm")) |> ignore
printfn "[1/3] Extract the dsp56300 interpreter: %s" origin
let archive = Path.Combine(scratch, "upstream.tar")
run checkout "git" [ "archive"; revision; "-o"; archive ]
run upstream "tar" [ "-xf"; archive ]
// its submodules (asmjit) are not in its archive: each comes from its own checkout
for line in (output checkout "git" [ "submodule"; "status" ]).Split('\n', StringSplitOptions.RemoveEmptyEntries) do
  match line.Trim().Split(' ') with
  | [| _; path |] | [| _; path; _ |] ->
    let sub = Path.Combine(checkout, path)
    if not (File.Exists(Path.Combine(sub, ".git")) || Directory.Exists(Path.Combine(sub, ".git"))) then
      failwith $"{sub} is not checked out: git -C {checkout} submodule update --init {path}"
    let subArchive = Path.Combine(scratch, "submodule.tar")
    Directory.CreateDirectory(Path.Combine(upstream, path)) |> ignore
    run sub "git" [ "archive"; "HEAD"; "-o"; subArchive ]
    run (Path.Combine(upstream, path)) "tar" [ "-xf"; subArchive ]
  | _ -> ()

// The upstream DSP owns a JIT even in interpreter mode. Skip executable-memory setup.
let jit = Path.Combine(upstream, "source/dsp56kEmu/jit.cpp")
let original = File.ReadAllText jit
let anchor = "m_rt(new JitRuntime())\n\t{"
if not (original.Contains anchor) then failwith "Upstream JIT constructor changed; review interpreter patch"
File.WriteAllText(jit,
  original.Replace(anchor,
    "m_rt(g_jitSupported ? new JitRuntime() : nullptr)\n\t{\n\t\tif constexpr (!g_jitSupported) return;")
    .Replace("void Jit::resetHW()\n\t{", "void Jit::resetHW()\n\t{\n\t\tif constexpr (!g_jitSupported) return;"))
let virtualMemory = Path.Combine(upstream, "source/asmjit/src/asmjit/core/virtmem.cpp")
let virtualSource = File.ReadAllText virtualMemory
let cacheFlush = "__builtin___clear_cache"
if not (virtualSource.Contains cacheFlush) then failwith "Upstream instruction-cache flush changed"
File.WriteAllText(virtualMemory,
  virtualSource.Replace(cacheFlush, "(void)"))
let assertions = Path.Combine(upstream, "source/dsp56kBase/dspassert.cpp")
File.WriteAllText(assertions,
  (File.ReadAllText assertions).Replace("#ifndef ANDROID", "#if !defined(ANDROID) && !defined(__EMSCRIPTEN__)"))
let helpers = Path.Combine(upstream, "source/dsp56kEmu/jitops_helper.cpp")
File.WriteAllText(helpers,
  (File.ReadAllText helpers).Replace(
    "static_assert(sizeof(m_block.dsp().m_interruptFunc) == 8);",
    "static_assert(sizeof(m_block.dsp().m_interruptFunc) == sizeof(void*));"))
// Pipeline lookahead can read unmapped memory. Preserve upstream's zero return, but bound
// diagnostics so repeated speculative reads cannot flood an AudioWorklet's console.
let memoryFile = Path.Combine(upstream, "source/dsp56kEmu/memory.cpp")
let memorySource = File.ReadAllText memoryFile
if not (memorySource.Contains "LOG_ERR_MEM_READ( _offset );") then
  failwith "Upstream unmapped-read diagnostics changed"
File.WriteAllText(memoryFile,
  memorySource.Replace("LOG_ERR_MEM_READ( _offset );",
    "static unsigned diagnostics = 0; if (diagnostics++ < 4) { LOG_ERR_MEM_READ( _offset ); }"))
// MACRI is MACI followed by convergent rounding; the interpreter must implement it.
let aluSource = File.ReadAllText(Path.Combine(upstream, "source/dsp56kEmu/dsp_ops_alu.inl"))
if aluSource.Contains "errNotImplemented(\"MACRI\");" then
  failwith "This dsp56300 revision leaves MACRI unimplemented: update the dsp56300 checkout"
printfn "[2/3] Configure WebAssembly interpreter"
let build = Path.Combine(scratch, "build")
run root "emcmake"
  [ "cmake"; "-S"; __SOURCE_DIRECTORY__; "-B"; build
    "-DUPSTREAM=" + upstream; "-DCMAKE_BUILD_TYPE=Release"
    "-DCMAKE_RUNTIME_OUTPUT_DIRECTORY=" + Path.Combine(scratch, "dist") ]
printfn "[3/3] Compile WebAssembly interpreter"
run root "cmake" [ "--build"; build; "-j"; "4" ]
for name in [ "dsp56300.mjs"; "dsp56300.wasm" ] do
  File.Copy(Path.Combine(scratch, "dist", name), Path.Combine(root, "out/wasm", name), true)
File.Copy(Path.Combine(upstream, "LICENSE.md"), Path.Combine(root, "out/wasm/LICENSE.dsp56300.md"), true)
// GPL-3.0: the corresponding source is that revision with the adaptations above (this script)
File.WriteAllText(Path.Combine(root, "out/wasm/SOURCE.txt"),
  $"dsp56300.wasm and dsp56300.mjs are built from {origin},\nwith the adaptations of wasm/build.fsx and the wrapper wasm/runtime.cpp of fsdsp56k. Licence: GPL-3.0 (LICENSE.dsp56300.md).\n")

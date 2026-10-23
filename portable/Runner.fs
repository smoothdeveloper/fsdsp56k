/// Same runner under .NET and Fable: establish fixtures, then replay and measure direct functions.
module dsp56k.PortableRunner

open dsp56k.Portable

#if FABLE_COMPILER
open Fable.Core
open Fable.Core.JsInterop
[<Import("readFileSync", "node:fs")>]
let read (path: string) (encoding: string) : string = jsNative
[<Import("writeFileSync", "node:fs")>]
let write (path: string) (text: string) : unit = jsNative
[<Emit("JSON.parse($0)")>]
let parse<'a> (text: string) : 'a = jsNative
[<Emit("JSON.stringify($0)")>]
let serialize (value: 'a) : string = jsNative
[<Emit("performance.now()")>]
let now () : float = jsNative
[<Emit("process.argv.slice(2)")>]
let arguments () : string array = jsNative
[<Emit("process.version + ' / ' + process.platform + ' / ' + process.arch")>]
let runtime () : string = jsNative
let target = "fable-js"
let parseFixture text : Fixture = parse text
let writeFixture value = serialize value
let writeMeasurement value = serialize value
#else
open System
open System.IO
let read path (_: string) = File.ReadAllText path
let write (path: string) (text: string) = File.WriteAllText(path, text)
let parseFixture = PortableJson.fixture
let writeFixture = PortableJson.writeFixture
let writeMeasurement = PortableJson.writeMeasurement
let now () = float (Diagnostics.Stopwatch.GetTimestamp()) * 1000. / float Diagnostics.Stopwatch.Frequency
let arguments () = Environment.GetCommandLineArgs()[1..]
let runtime () =
  let info = Runtime.InteropServices.RuntimeInformation.FrameworkDescription
  $"{info} / {Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}"
let target = if Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then "dotnet" else "dotnet-aot"
#endif

let private measure selectedTarget create path result warmup trials =
  let fixture = read path "utf8" |> parseFixture
  let measured = grade selectedTarget (runtime()) now (create fixture.kernel) (int warmup) (int trials) fixture
  write result (writeMeasurement measured)
  let milliseconds = measured.medianRenderMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
  let speed = measured.realTimeFactor.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
  System.Console.WriteLine
    $"{selectedTarget} {fixture.kernel}: exact output/state/buffers; {milliseconds} ms render, {speed}x real time"

/// Each block's output and state words, one block a line: what a sweep grader compares itself.
let private captureTo create path result =
  let fixture = read path "utf8" |> parseFixture
  let blocks, _ = capture (create fixture.kernel) fixture.steps
  let line (block: Block) =
    String.concat " " (block.output |> Array.map string) + "\t" + String.concat " " (block.state |> Array.map string)
  write result (String.concat "\n" (blocks |> Array.map line) + "\n")
  System.Console.WriteLine $"Capture: {fixture.kernel}, {blocks.Length} blocks"

#if FABLE_COMPILER
[<Import("readFileSync", "node:fs")>]
let readBinary (path: string) : obj = jsNative
[<Emit("$0.then($1).catch(e => { console.error(e); process.exitCode = 1; })")>]
let finish (pending: JS.Promise<'a>) (success: 'a -> unit) : unit = jsNative
#endif

let run () =
  match arguments() with
#if FABLE_COMPILER
  | [| "--wasm"; wasm; path; result; warmup; trials |] ->
    finish (PortableRustWasm.instantiate (readBinary wasm)) (fun exports ->
      measure "fable-rust-wasm" (PortableRustWasm.create exports) path result warmup trials)
  | [| "--wasm"; wasm; "--capture"; path; result |] ->
    finish (PortableRustWasm.instantiate (readBinary wasm)) (fun exports ->
      captureTo (PortableRustWasm.create exports) path result)
#endif
#if !FABLE_COMPILER
  | [| "--native"; library; path; result; warmup; trials |] ->
    let create, lifetime = PortableRustNative.load library
    use _keepAlive = lifetime
    measure "fable-rust-native" create path result warmup trials
#endif
  | [| "--reference"; path |] ->
    let fixture = read path "utf8" |> parseFixture
    let expected, buffers = capture (fun () -> PortableAdapters.create fixture.kernel) fixture.steps
    write path (writeFixture { fixture with expected = expected; buffers = buffers })
    System.Console.WriteLine $"Reference: {fixture.kernel}, {expected.Length} blocks"
  | [| "--capture"; path; result |] ->
    captureTo (fun name () -> PortableAdapters.create name) path result
  | [| "--kernels" |] ->
    for name in PortableAdapters.names do System.Console.WriteLine name
  | [| path; result; warmup; trials |] ->
    measure target (fun name () -> PortableAdapters.create name) path result warmup trials
  | _ -> failwith "Usage: runner [--wasm module.wasm | --native library] fixture.json result.json warmup trials | runner --reference fixture.json | runner --kernels | runner [--wasm module.wasm] --capture fixture.json blocks.tsv"

#if FABLE_COMPILER
run()
#else
[<EntryPoint>]
let main _ = run(); 0
#endif

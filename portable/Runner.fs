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
#else
open System
open System.IO
open System.Text.Json
let read path (_: string) = File.ReadAllText path
let write (path: string) (text: string) = File.WriteAllText(path, text)
let parse<'a> text = JsonSerializer.Deserialize<'a>(text: string)
let serialize value = JsonSerializer.Serialize value
let now () = float (Diagnostics.Stopwatch.GetTimestamp()) * 1000. / float Diagnostics.Stopwatch.Frequency
let arguments () = Environment.GetCommandLineArgs()[1..]
let runtime () = Runtime.InteropServices.RuntimeInformation.FrameworkDescription
let target = "dotnet"
#endif

let private measure selectedTarget create path result warmup trials =
  let fixture: Fixture = read path "utf8" |> parse
  let measured = grade selectedTarget (runtime()) now (create fixture.kernel) (int warmup) (int trials) fixture
  write result (serialize measured)
  printfn "%s %s: exact output/state/buffers; %.3f ms render, %.1fx real time"
    selectedTarget fixture.kernel measured.medianRenderMs measured.realTimeFactor

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
#endif
  | [| "--reference"; path |] ->
    let fixture: Fixture = read path "utf8" |> parse
    let expected, buffers = capture (fun () -> PortableAdapters.create fixture.kernel) fixture.steps
    write path (serialize { fixture with expected = expected; buffers = buffers })
    printfn "Reference: %s, %d blocks" fixture.kernel expected.Length
  | [| path; result; warmup; trials |] ->
    measure target (fun name () -> PortableAdapters.create name) path result warmup trials
  | _ -> failwith "Usage: runner [--wasm module.wasm] fixture.json result.json warmup trials | runner --reference fixture.json"

#if FABLE_COMPILER
run()
#else
[<EntryPoint>]
let main _ = run(); 0
#endif

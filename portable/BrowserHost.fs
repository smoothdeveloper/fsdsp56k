/// Node host for the browser-wasm runtime; the kernel loop and checks stay inside .NET.
module dsp56k.PortableBrowserHost

open Fable.Core
open Fable.Core.JsInterop

[<Import("readFileSync", "node:fs")>]
let private read (path: string) (encoding: string) : string = jsNative
[<Import("writeFileSync", "node:fs")>]
let private write (path: string) (text: string) : unit = jsNative
[<Import("pathToFileURL", "node:url")>]
let private fileUrl (path: string) : obj = jsNative
[<Emit("import($0)")>]
let private importRuntime (url: string) : JS.Promise<obj> = jsNative
[<Emit("globalThis.portableGrade = $0")>]
let private configure (request: obj) : unit = jsNative
[<Emit("process.argv.slice(2)")>]
let private arguments () : string array = jsNative
[<Emit("process.version + ' / ' + process.platform + ' / ' + process.arch")>]
let private runtime () : string = jsNative
[<Emit("JSON.parse($0)")>]
let private measurement (json: string) : dsp56k.Portable.Measurement = jsNative
[<Emit("$0.catch(e => {console.error(e);process.exitCode=1})")>]
let private finish (pending: JS.Promise<unit>) : unit = jsNative

[<Emit("$0.then($1)")>]
let private bind (pending: JS.Promise<'a>) (next: 'a -> JS.Promise<'b>) : JS.Promise<'b> = jsNative
[<Emit("Promise.resolve($0)")>]
let private resolved (value: 'a) : JS.Promise<'a> = jsNative

let private run () =
  match arguments() with
  | [| modulePath; target; fixture; result; warmup; trials |] ->
    let request = createObj [
      "fixture" ==> read fixture "utf8"; "warmup" ==> int warmup; "trials" ==> int trials
      "target" ==> target; "runtime" ==> runtime(); "result" ==> ""; "error" ==> "" ]
    configure request
    bind (importRuntime (string (fileUrl modulePath)?href)) (fun moduleExports ->
      bind (moduleExports?dotnet?create()) (fun (engine: obj) ->
        bind (engine?runMain()) (fun (exitCode: int) ->
          if exitCode <> 0 || string request?error <> "" then failwith (string request?error)
          let json: string = request?result
          if json = "" then failwith ".NET WASM runner returned no measurement"
          write result json
          let measured = measurement json
          printfn "%s %s: exact output/state/buffers; %.3f ms render, %.1fx real time"
            target measured.kernel measured.medianRenderMs measured.realTimeFactor
          resolved ())))
  | _ -> failwith "Usage: dotnet-wasm-grade dotnet.js target fixture.json result.json warmup trials"
finish (run())

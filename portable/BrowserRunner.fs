/// Run the same fixture entirely inside the .NET WebAssembly runtime.
module dsp56k.PortableBrowserRunner

open System
open System.Runtime.InteropServices.JavaScript
open dsp56k.Portable

[<EntryPoint>]
let main _ =
  use request = JSHost.GlobalThis.GetPropertyAsJSObject "portableGrade"
  try
    let fixture = PortableJson.fixture (request.GetPropertyAsString "fixture")
    let warmup = request.GetPropertyAsInt32 "warmup"
    let trials = request.GetPropertyAsInt32 "trials"
#if PORTABLE_WASM_AOT
    let target = "dotnet-wasm-aot"
#else
    let target = "dotnet-wasm"
#endif
    if request.GetPropertyAsString "target" <> target then failwith "Incorrect .NET WASM build mode"
    let hostRuntime = request.GetPropertyAsString "runtime"
    let framework = Runtime.InteropServices.RuntimeInformation.FrameworkDescription
    let runtime = $"{framework} / {target} / {hostRuntime}"
    let now () = float (Diagnostics.Stopwatch.GetTimestamp()) * 1000. / float Diagnostics.Stopwatch.Frequency
    let create () = PortableAdapters.create fixture.kernel
    let result = grade target runtime now create warmup trials fixture
    request.SetProperty("result", PortableJson.writeMeasurement result)
    0
  with error ->
    request.SetProperty("error", error.ToString())
    1

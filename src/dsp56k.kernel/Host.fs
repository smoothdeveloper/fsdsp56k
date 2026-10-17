/// A kernel run as .NET code: the meaning the compiled program is checked against. Parameters
/// arrive as the words the DSP finds at Y:(r6+1..), the state is read back as the workset words
/// it occupies on the DSP.
module dsp56k.kernel.Host

open System
open System.Reflection
open Microsoft.FSharp.Reflection
open dsp56k.kernel.Kernel

let private call (mi: MethodInfo) (args: obj array) =
  try mi.Invoke(null, args) |> ignore
  with :? TargetInvocationException as e when not (isNull e.InnerException) -> raise (nonNull e.InnerException)

type Instance(target: Target, kernel: Kernel) =
  let layout = Kernel.layout target kernel
  let zero (t: Type) = Activator.CreateInstance t
  let state = FSharpValue.MakeRecord(kernel.stateType, FSharpType.GetRecordFields kernel.stateType |> Array.map (fun p -> zero p.PropertyType))
  let mutable parameters = FSharpValue.MakeRecord(kernel.paramsType, FSharpType.GetRecordFields kernel.paramsType |> Array.map (fun p -> zero p.PropertyType))
  let output = Output()

  member _.Layout = layout

  /// The parameter words, parameter 0 first (as at Y:(r6+1)).
  member _.SetParameters(words: int array) =
    let fields = FSharpType.GetRecordFields kernel.paramsType
    parameters <- FSharpValue.MakeRecord(kernel.paramsType, fields |> Array.mapi (fun i _ -> box (Frac.ofRaw (if i < words.Length then words[i] else 0))))

  member _.Init() = call kernel.init [| parameters; state |]
  member _.Trig() = call kernel.trig [| parameters; state |]

  /// One RENDER: the words written, one per frame.
  member _.Render() =
    output.Clear()
    call kernel.render [| parameters; state; output |]
    output.Words

  /// The state as workset words, from Y:(r6+stateBase).
  member _.StateWords() =
    [| for f, p in Array.zip layout.state (FSharpType.GetRecordFields kernel.stateType) do
         match p.GetValue state with
         | :? Frac as v -> v.Word
         | :? Long as v ->
           int ((v.Raw >>> 24) &&& 0xffffffL)
           int (v.Raw &&& 0xffffffL)
         | v -> failwith $"state field {f.name}: {v}" |]

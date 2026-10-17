/// A kernel run as .NET code: the meaning the compiled program is checked against. Parameters
/// arrive as the words the DSP finds at Y:(r6+1..), the state is read back as the workset words
/// it occupies on the DSP.
module dsp56k.kernel.Host

open System
open System.Reflection
open Microsoft.FSharp.Reflection
open dsp56k.kernel.Kernel

let private call (mi: MethodInfo) (args: (obj | null) array) =
  try mi.Invoke(null, args) |> ignore
  with :? TargetInvocationException as e when not (isNull e.InnerException) -> raise (nonNull e.InnerException)

type Instance(target: Target, kernel: Kernel) =
  let layout = Kernel.layout target kernel
  let zero (t: Type) = Activator.CreateInstance t
  let state : obj | null =
    if kernel.stateType = typeof<unit> then null
    else
      let initial (f: Field) (p: PropertyInfo) =
        match f.ty with
        | TArray n -> box (Array.create n Frac.zero)
        | _ -> zero p.PropertyType
      FSharpValue.MakeRecord(kernel.stateType, Array.map2 initial layout.state (FSharpType.GetRecordFields kernel.stateType))
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
    [| for f, p in Array.zip layout.state (if isNull state then [||] else FSharpType.GetRecordFields kernel.stateType) do
         match p.GetValue(nonNull state) with
         | :? Frac as v -> v.Word
         | :? Long as v ->
           int ((v.Raw >>> 24) &&& 0xffffffL)
           int (v.Raw &&& 0xffffffL)
         | :? Acc as v ->                           // a1, a0, then a2 sign-extended
           int ((v.Raw >>> 24) &&& 0xffffffL)
           int (v.Raw &&& 0xffffffL)
           int ((v.Raw >>> 48) &&& 0xffffffL)
         | :? (Frac array) as v -> yield! v |> Array.map _.Word
         | v -> failwith $"state field {f.name}: {v}" |]

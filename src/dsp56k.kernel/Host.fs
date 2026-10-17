/// A kernel run as .NET code: the meaning the compiled program is checked against. Parameters
/// arrive as the words the DSP finds at Y:(r6+1..), the state is read back as the workset words
/// it occupies on the DSP: the Y state, then the [<XMemory>] state.
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
      let initial (p: PropertyInfo) =
        match (layout.field p.Name).ty with
        | TArray n -> box (Array.create n Frac.zero)
        | TLongArray n -> box (Array.create n Long.zero)
        | _ -> zero p.PropertyType
      FSharpValue.MakeRecord(kernel.stateType, FSharpType.GetRecordFields kernel.stateType |> Array.map initial)
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

  /// The words of some state fields, in their workset order.
  member private _.Words(fields: Field array) =
    [| for f in fields do
         match (nonNull (kernel.stateType.GetProperty f.name)).GetValue(nonNull state) with
         | :? Frac as v -> v.Word
         | :? Long as v ->
           int ((v.Raw >>> 24) &&& 0xffffffL)
           int (v.Raw &&& 0xffffffL)
         | :? Acc as v ->                           // a1, a0, then a2 sign-extended
           int ((v.Raw >>> 24) &&& 0xffffffL)
           int (v.Raw &&& 0xffffffL)
           int ((v.Raw >>> 48) &&& 0xffffffL)
         | :? (Frac array) as v -> yield! v |> Array.map _.Word
         | :? (Long array) as v ->
           for l in v do
             int ((l.Raw >>> 24) &&& 0xffffffL)
             int (l.Raw &&& 0xffffffL)
         | v -> failwith $"state field {f.name}: {v}" |]

  /// The Y state as workset words, from Y:(r6+stateBase).
  member this.YStateWords() = this.Words layout.state

  /// The [<XMemory>] state as workset words, from X:(r6+0).
  member this.XStateWords() = this.Words layout.xState

  /// The state as workset words: the Y state from Y:(r6+stateBase), then the X state from X:(r6+0).
  member this.StateWords() = Array.append (this.YStateWords()) (this.XStateWords())

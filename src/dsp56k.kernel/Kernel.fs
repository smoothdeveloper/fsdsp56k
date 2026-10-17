/// A machine written in kernel F#: three functions of a [<ReflectedDefinition>] module, over a
/// parameter record (Frac fields, Y:(r6+1) on) and a state record (Frac and Long fields, the
/// persistent workset), the render also writing frames to an Output.
///
///   let init   (p: Params) (s: State) = ...
///   let trig   (p: Params) (s: State) = ...
///   let render (p: Params) (s: State) (out: Output) = ...
///
/// The same functions run as .NET code (`Host`) and compile to DSP56300 items (`Compile`).
module dsp56k.kernel.Kernel

open System
open System.Reflection
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns
open Microsoft.FSharp.Reflection

exception KernelError of message: string

let fail message = raise (KernelError message)

type Kernel =
  { name       : string
    paramsType : Type
    stateType  : Type
    init       : MethodInfo
    trig       : MethodInfo
    render     : MethodInfo }

let methodOf what (e: Expr) =
  let rec go e =
    match e with
    | Lambda(_, body) -> go body
    | Call(None, mi, _) -> mi
    | _ -> fail $"{what}: expected a module function, as in <@ render @>"
  go e

/// The kernel of three module functions, given as quotations naming them.
let define name (init: Expr<'P -> 'S -> unit>) (trig: Expr<'P -> 'S -> unit>) (render: Expr<'P -> 'S -> Output -> unit>) =
  if not (FSharpType.IsRecord typeof<'P>) then fail $"{name}: the parameters must be a record"
  if not (FSharpType.IsRecord typeof<'S>) then fail $"{name}: the state must be a record"
  { name = name; paramsType = typeof<'P>; stateType = typeof<'S>
    init = methodOf "init" init; trig = methodOf "trig" trig; render = methodOf "render" render }

/// Where things are on the target.
type Target =
  { name        : string
    frames      : int        // frames per RENDER
    paramBase   : int        // Y:(r6+paramBase) parameter 0
    maxParams   : int
    stateBase   : int        // Y:(r6+stateBase) the first state word
    yLimit      : int        // Y workset words
    xLimit      : int }      // X workset words (the compiler's locals and spills)

/// The MDS machine ABI: 32 frames, Y+1..8 parameters, Y+9..63 state, X+0..63 free.
let mds =
  { name = "Machinedrum MDS"; frames = 32; paramBase = 1; maxParams = 8; stateBase = 9; yLimit = 64; xLimit = 64 }

type Ty =
  | TFrac
  | TLong
  | TAcc
  | TOther of Type

let tyOf (t: Type) =
  if t = typeof<Frac> then TFrac
  elif t = typeof<Long> then TLong
  elif t = typeof<Acc> then TAcc
  else TOther t

/// Words per value in memory: Frac one, Long hi:lo, Acc hi:lo:ext (spills are exact).
let words ty =
  match ty with
  | TFrac -> 1
  | TLong -> 2
  | TAcc -> 3
  | TOther t -> fail $"{t.Name} values have no place in DSP memory"

/// The record fields and their workset words.
type Field =
  { name   : string
    ty     : Ty
    offset : int }      // from r6

type Layout =
  { parameters : Field array
    state      : Field array
    yWorkset   : int }  // Y words declared: Y+0 up to the last state word

let layout (target: Target) (k: Kernel) =
  let ps = FSharpType.GetRecordFields k.paramsType
  if ps.Length > target.maxParams then fail $"{k.name}: {ps.Length} parameters, the target has {target.maxParams}"
  let parameters =
    ps |> Array.mapi (fun i p ->
      if tyOf p.PropertyType <> TFrac then fail $"{k.name}: parameter {p.Name} must be a Frac"
      { name = p.Name; ty = TFrac; offset = target.paramBase + i })
  let mutable at = target.stateBase
  let state =
    [| for p in FSharpType.GetRecordFields k.stateType do
         let ty = tyOf p.PropertyType
         match ty with
         | TFrac | TLong -> ()
         | _ -> fail $"{k.name}: state field {p.Name} must be a Frac or a Long"
         { name = p.Name; ty = ty; offset = at }
         at <- at + words ty |]
  if at > target.yLimit then fail $"{k.name}: the state needs Y+{at - 1}, the target has {target.yLimit} words"
  { parameters = parameters; state = state; yWorkset = at }

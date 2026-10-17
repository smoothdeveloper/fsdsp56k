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
  if not (FSharpType.IsRecord typeof<'S>) && typeof<'S> <> typeof<unit> then fail $"{name}: the state must be a record (or unit)"
  { name = name; paramsType = typeof<'P>; stateType = typeof<'S>
    init = methodOf "init" init; trig = methodOf "trig" trig; render = methodOf "render" render }

/// Where things are on the target.
type Target =
  { name        : string
    frames      : int        // frames per RENDER
    channels    : int        // words per frame at Y:(r7)+: 1 mono, 2 interleaved left, right
    paramBase   : int        // Y:(r6+paramBase) parameter 0
    maxParams   : int
    stateBase   : int        // Y:(r6+stateBase) the first state word
    yLimit      : int        // Y workset words
    xLimit      : int }      // X workset words (the compiler's locals and spills)

  /// The words one RENDER writes.
  member t.blockWords = t.frames * t.channels

  /// "32 frames (one word each)", for messages.
  member t.blockText =
    let each = match t.channels with 1 -> "one word" | 2 -> "two words" | n -> $"{n} words"
    $"{t.frames} frames ({each} each)"

/// The MDS machine ABI: 32 mono frames, Y+1..8 parameters, Y+9..63 state, X+0..63 free.
let mds =
  { name = "Machinedrum MDS"; frames = 32; channels = 1; paramBase = 1; maxParams = 8; stateBase = 9; yLimit = 64; xLimit = 64 }

/// The host a kernel's generated code runs under: how the host enters the kernel's routines (INIT, TRIG,
/// RENDER), where the parameters, the state and the output frames sit (its `Target`), and how the frames go
/// out. The compiler, the tests and the golden file go through `hosts`: a new host is one more case here,
/// and its rows in the golden file.
[<RequireQualifiedAccess>]
type KernelHost =
  /// The Machinedrum: MDS machines, entered per track with r6 on the track's workset.
  | Machinedrum

  /// The name the golden file keys the host's rows by.
  member h.key = match h with KernelHost.Machinedrum -> "machinedrum"

  /// Where things are under the host.
  member h.target = match h with KernelHost.Machinedrum -> mds

/// Every host the compiler knows.
let hosts = [ KernelHost.Machinedrum ]

/// The targets of the hosts, in order.
let hostTargets = hosts |> List.map (fun h -> h.target)

/// The host whose target this is, if any.
let hostOf (t: Target) = hosts |> List.tryFind (fun h -> h.target.name = t.name)

type Ty =
  | TFrac
  | TLong
  | TAcc
  | TArray of length: int       // a state field of Frac words
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
  | TArray n -> n
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
    [| for p in (if k.stateType = typeof<unit> then [||] else FSharpType.GetRecordFields k.stateType) do
         let ty =
           if p.PropertyType = typeof<Frac array> then
             match p.GetCustomAttributes(typeof<LengthAttribute>, true) with
             | [| :? LengthAttribute as l |] when l.Words > 0 -> TArray l.Words
             | _ -> fail $"{k.name}: state field {p.Name} is an array: give its length, [<Length n>]"
           else tyOf p.PropertyType
         match ty with
         | TFrac | TLong | TAcc | TArray _ -> ()
         | _ -> fail $"{k.name}: state field {p.Name} must be a Frac, a Long, an Acc or a [<Length n>] Frac array"
         { name = p.Name; ty = ty; offset = at }
         at <- at + words ty |]
  if at > target.yLimit then fail $"{k.name}: the state needs Y+{at - 1}, the target has {target.yLimit} words"
  { parameters = parameters; state = state; yWorkset = at }

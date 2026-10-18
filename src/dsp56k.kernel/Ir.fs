/// The kernel expressions the code generator reads, as plain data: a tree of the same shapes as an F# quotation (the
/// shapes the generator already matches: Let, Call, PropertyGet, IfThenElse, ForIntegerRangeLoop and the rest), but with
/// no .NET reflection in it. Types are `IrTy`, members are `Member` records that carry what the generator asks of a
/// method or a property (its name, its owner, its intrinsic name, its attributes, its inlinable body, a function that
/// evaluates it on constants), and a front end builds them from whatever it reads: quotations (FromQuotation.fs), or any
/// other source that can produce the same shapes (a typed tree from Fable, a hand-written or serialised form).
///
/// The names mirror Microsoft.FSharp.Quotations on purpose: `Let`, `Call`, `PropertyGet`... as patterns, `Expr.Let`,
/// `Expr.Call`... as constructors, `ExprShape` for generic traversals, `Substitute` and `GetFreeVars`, so code written
/// against quotations reads the same against this tree.
module dsp56k.kernel.Ir

open System
open System.Collections.Generic

/// The types the generator tells apart.
type IrTy =
  | TyUnit
  | TyBool
  | TyInt
  | TyInt64
  | TyFloat
  /// Another primitive (a byte, a char...): a constant when compiled, the type's name.
  | TyPrim of string
  | TyFrac
  | TyAcc
  | TyLong
  | TyTable
  | TyRegion
  | TyCell
  | TyCursor
  | TyOutput
  /// A record: the kernel's parameters or state (named by the type's full name).
  | TyRecord of string
  /// A union (a constant when compiled: a voice's configuration).
  | TyUnion of string
  | TyArray of IrTy
  | TyFunc of IrTy * IrTy
  | TyOther of string

module IrTy =
  /// Values of these types are known when the kernel is compiled (constants, tables, a voice's configuration).
  let foldable (t: IrTy) =
    match t with
    | TyFrac | TyAcc | TyLong | TyTable | TyBool | TyInt | TyInt64 | TyFloat | TyPrim _ | TyUnion _ -> true
    | _ -> false

  /// The type's short name, for messages.
  let name (t: IrTy) =
    match t with
    | TyUnit -> "Unit"
    | TyBool -> "Boolean"
    | TyInt -> "Int32"
    | TyInt64 -> "Int64"
    | TyFloat -> "Double"
    | TyPrim n | TyRecord n | TyUnion n | TyOther n -> n.Substring(n.LastIndexOfAny [| '.'; '+' |] + 1)
    | TyFrac -> "Frac"
    | TyAcc -> "Acc"
    | TyLong -> "Long"
    | TyTable -> "Table"
    | TyRegion -> "Region"
    | TyCell -> "Cell"
    | TyCursor -> "Cursor"
    | TyOutput -> "Output"
    | TyArray t -> "FSharpArray`1"
    | TyFunc _ -> "FSharpFunc`2"

let counter = ref 0

/// What orders variables of the same name: the .NET type's metadata token, as F# orders quotation variables (a front end
/// that has no such thing leaves it 0). Set by the front end before kernels are compiled.
let mutable typeKeyOf: IrTy -> int = fun _ -> 0

/// A variable. Identity is the variable's own: two with the same name are two variables. They are ordered as F# orders
/// quotation variables (by name, then type, then creation): the generator keeps some of them in maps, and the order of
/// those decides which local gets a register.
[<Sealed; AllowNullLiteral>]
type Var(name: string, ty: IrTy, isMutable: bool, typeKey: int) =
  let id = System.Threading.Interlocked.Increment counter
  new(name: string, ty: IrTy, isMutable: bool) = Var(name, ty, isMutable, typeKeyOf ty)
  new(name: string, ty: IrTy) = Var(name, ty, false, typeKeyOf ty)
  member _.Name = name
  member _.Type = ty
  member _.IsMutable = isMutable
  member _.Id = id
  member _.TypeKey = typeKey
  /// Another variable like this one.
  member _.Fresh() = Var(name, ty, isMutable, typeKey)
  override _.Equals(o: obj) =
    match o with
    | :? Var as v -> v.Id = id
    | _ -> false
  override _.GetHashCode() = id
  override _.ToString() = name
  interface IComparable with
    member this.CompareTo(o: obj) =
      let v = o :?> Var
      if obj.ReferenceEquals(this, v) then 0
      else
        let c = String.CompareOrdinal(name, v.Name)
        if c <> 0 then c
        else
          let c = compare typeKey v.TypeKey
          if c <> 0 then c else compare id v.Id

/// Where an expression came from in the F# source (the debug range of a quotation), for the comments in the listing.
type Range = { file: string; line: int }

/// What the generator asks of a method or a property it meets in a call. Equality is the record's own identity: a front end
/// makes one `Member` for each method and uses it everywhere.
[<ReferenceEquality>]
type Member =
  { /// The method's or property's name ("op_Addition", "ofRaw", "GetArray", "phase").
    Name: string
    /// The full name of its declaring type ("dsp56k.kernel.Acc", "Microsoft.FSharp.Core.Operators", a module's).
    Owner: string
    /// The declaring type when it is one the generator knows (Frac, Acc, Long...).
    Declaring: IrTy
    /// What a method returns, or the type of a property.
    ReturnType: IrTy
    /// The number of parameters of a method.
    ParameterCount: int
    /// The compiler's name for the operation ("acc.add"), when it is an intrinsic.
    Intrinsic: string option
    /// A constant-only operation: evaluated when the kernel is compiled.
    IsConst: bool
    /// A generic method definition (never evaluated or inlined as it is).
    IsGeneric: bool
    /// A property that can be assigned.
    CanWrite: bool
    /// Marks that apply to the member or, for NoUnswitch and Unroll, to the module that declares it: "Subroutine",
    /// "NoUnswitch", "Unroll".
    Marks: Set<string>
    /// A name that tells this member from any other across compilations (a subroutine's label).
    Key: string
    /// The body of a helper the generator inlines (a lambda over its parameters), when it has one.
    Definition: Lazy<Expr option>
    /// Calls a method on constant arguments.
    Invoke: obj[] -> obj
    /// Reads a property of a constant (the instance, or null for a module value).
    Get: obj -> obj }

/// A union case, for `match x with Case ...` on a constant.
and [<ReferenceEquality>] Case =
  { Name: string
    Tag: int
    /// Whether the (constant) value is this case.
    Test: obj -> bool }

and Node =
  | NVar of Var
  | NValue of (obj | null) * IrTy
  | NLambda of Var * Expr
  | NApplication of Expr * Expr
  | NCall of Expr option * Member * Expr list
  | NPropertyGet of Expr option * Member * Expr list
  | NPropertySet of Expr option * Member * Expr list * Expr
  | NLet of Var * Expr * Expr
  | NSequential of Expr * Expr
  | NIfThenElse of Expr * Expr * Expr
  | NVarSet of Var * Expr
  | NFor of Var * Expr * Expr * Expr
  | NWhile of Expr * Expr
  | NUnionCaseTest of Expr * Case
  | NTuple of Expr list
  /// A node the front end has no counterpart for: its name and sub-expressions (the generator refuses it where it meets it).
  | NOther of string * Expr list

/// An expression: a node, its type and, when known, where it was written. Equality is structural (the range is not part of
/// it, as a quotation's custom attributes are not).
and [<Sealed>] Expr(node: Node, ty: IrTy, range: Range option) =
  member _.Node = node
  member _.Type = ty
  member _.Range = range
  override this.Equals(o: obj) =
    match o with
    | :? Expr as e -> obj.ReferenceEquals(this, e) || node = e.Node
    | _ -> false
  override _.GetHashCode() = hash node
  override this.ToString() = Expr.Show this

  /// The same node with another range.
  member this.WithRange(r: Range option) = Expr(node, ty, r)

  // ---- construction: the type of each node follows from its parts, as a quotation's does ----

  static member Make(node: Node, ty: IrTy, range: Range option) = Expr(node, ty, range)
  static member Var(v: Var) = Expr(NVar v, v.Type, None)
  static member Value(v: (obj | null), ty: IrTy) = Expr(NValue(v, ty), ty, None)
  static member Value(v: int) = Expr(NValue(box v, TyInt), TyInt, None)
  static member Value(v: bool) = Expr(NValue(box v, TyBool), TyBool, None)
  static member Value(_: unit) = Expr(NValue(null, TyUnit), TyUnit, None)
  static member Lambda(v: Var, body: Expr) = Expr(NLambda(v, body), TyFunc(v.Type, body.Type), None)
  static member Application(f: Expr, a: Expr) =
    let r = match f.Type with TyFunc(_, r) -> r | _ -> TyOther "obj"
    Expr(NApplication(f, a), r, None)
  static member Call(m: Member, args: Expr list) = Expr(NCall(None, m, args), m.ReturnType, None)
  static member Call(o: Expr, m: Member, args: Expr list) = Expr(NCall(Some o, m, args), m.ReturnType, None)
  static member PropertyGet(m: Member) = Expr(NPropertyGet(None, m, []), m.ReturnType, None)
  static member PropertyGet(o: Expr, m: Member) = Expr(NPropertyGet(Some o, m, []), m.ReturnType, None)
  static member PropertySet(o: Expr, m: Member, index: Expr list, value: Expr) =
    Expr(NPropertySet(Some o, m, index, value), TyUnit, None)
  static member Let(v: Var, value: Expr, body: Expr) = Expr(NLet(v, value, body), body.Type, None)
  static member Sequential(a: Expr, b: Expr) = Expr(NSequential(a, b), b.Type, None)
  static member IfThenElse(c: Expr, t: Expr, f: Expr) = Expr(NIfThenElse(c, t, f), t.Type, None)
  static member VarSet(v: Var, value: Expr) = Expr(NVarSet(v, value), TyUnit, None)
  static member ForIntegerRangeLoop(v: Var, lo: Expr, hi: Expr, body: Expr) = Expr(NFor(v, lo, hi, body), TyUnit, None)
  static member WhileLoop(c: Expr, body: Expr) = Expr(NWhile(c, body), TyUnit, None)
  static member NewTuple(es: Expr list) = Expr(NTuple es, TyOther "tuple", None)

  // ---- reading ----

  /// The variables used and not bound inside.
  member this.GetFreeVars() : Var seq =
    let seen = HashSet<Var>()
    let order = ResizeArray<Var>()
    let rec go (bound: Set<Var>) (e: Expr) =
      match e.Node with
      | NVar v -> if not (bound.Contains v) && seen.Add v then order.Add v
      | NValue _ -> ()
      | NLambda(v, b) -> go (bound.Add v) b
      | NApplication(f, a) -> go bound f; go bound a
      | NCall(o, _, args) | NPropertyGet(o, _, args) -> Option.iter (go bound) o; List.iter (go bound) args
      | NPropertySet(o, _, idx, x) -> Option.iter (go bound) o; List.iter (go bound) idx; go bound x
      | NLet(v, x, b) -> go bound x; go (bound.Add v) b
      | NSequential(a, b) -> go bound a; go bound b
      | NIfThenElse(c, t, f) -> go bound c; go bound t; go bound f
      | NVarSet(v, x) -> (if not (bound.Contains v) && seen.Add v then order.Add v); go bound x
      | NFor(v, lo, hi, b) -> go bound lo; go bound hi; go (bound.Add v) b
      | NWhile(c, b) -> go bound c; go bound b
      | NUnionCaseTest(o, _) -> go bound o
      | NTuple es | NOther(_, es) -> List.iter (go bound) es
    go Set.empty this
    order :> Var seq

  /// The expression with each free variable the function answers for replaced. A variable bound inside the expression that a
  /// replacement would capture is renamed first.
  member this.Substitute(f: Var -> Expr option) : Expr =
    let replacements = Dictionary<Var, Expr>()
    for v in this.GetFreeVars() do
      match f v with
      | Some r -> replacements[v] <- r
      | None -> ()
    if replacements.Count = 0 then this
    else
      let captured = HashSet<Var>(replacements.Values |> Seq.collect (fun r -> r.GetFreeVars()))
      let rec go (renames: Map<Var, Var>) (e: Expr) : Expr =
        let range = e.Range
        let bind (v: Var) =
          if captured.Contains v then
            let v' = v.Fresh()
            v', renames.Add(v, v')
          else v, renames.Remove v
        let rebuild node = Expr(node, e.Type, range)
        match e.Node with
        | NVar v ->
          match Map.tryFind v renames with
          | Some v' -> Expr(NVar v', e.Type, range)
          | None ->
            match replacements.TryGetValue v with
            | true, r -> r
            | _ -> e
        | NValue _ -> e
        | NLambda(v, b) ->
          let v', renames' = bind v
          rebuild (NLambda(v', go renames' b))
        | NApplication(a, b) -> rebuild (NApplication(go renames a, go renames b))
        | NCall(o, m, args) -> rebuild (NCall(Option.map (go renames) o, m, List.map (go renames) args))
        | NPropertyGet(o, m, args) -> rebuild (NPropertyGet(Option.map (go renames) o, m, List.map (go renames) args))
        | NPropertySet(o, m, idx, x) -> rebuild (NPropertySet(Option.map (go renames) o, m, List.map (go renames) idx, go renames x))
        | NLet(v, x, b) ->
          let x' = go renames x
          let v', renames' = bind v
          rebuild (NLet(v', x', go renames' b))
        | NSequential(a, b) -> rebuild (NSequential(go renames a, go renames b))
        | NIfThenElse(c, t, f) -> rebuild (NIfThenElse(go renames c, go renames t, go renames f))
        | NVarSet(v, x) ->
          let v' = match Map.tryFind v renames with Some v' -> v' | None -> v
          rebuild (NVarSet(v', go renames x))
        | NFor(v, lo, hi, b) ->
          let lo', hi' = go renames lo, go renames hi
          let v', renames' = bind v
          rebuild (NFor(v', lo', hi', go renames' b))
        | NWhile(c, b) -> rebuild (NWhile(go renames c, go renames b))
        | NUnionCaseTest(o, c) -> rebuild (NUnionCaseTest(go renames o, c))
        | NTuple es -> rebuild (NTuple(List.map (go renames) es))
        | NOther(n, es) -> rebuild (NOther(n, List.map (go renames) es))
      go Map.empty this

  /// A readable form, for messages.
  static member Show(e: Expr) : string =
    let rec show (e: Expr) =
      let all (es: Expr list) = String.concat ", " (List.map show es)
      let instance (o: Expr option) = match o with Some o -> "Some (" + show o + ")" | None -> "None"
      match e.Node with
      | NVar v -> v.Name
      | NValue(null, _) -> "Value (<null>)"
      | NValue(v, _) -> "Value (" + string v + ")"
      | NLambda(v, b) -> "Lambda (" + v.Name + ", " + show b + ")"
      | NApplication(f, a) -> "Application (" + show f + ", " + show a + ")"
      | NCall(o, m, args) -> "Call (" + instance o + ", " + m.Name + ", [" + all args + "])"
      | NPropertyGet(o, m, _) -> "PropertyGet (" + instance o + ", " + m.Name + ", [])"
      | NPropertySet(o, m, _, x) -> "PropertySet (" + instance o + ", " + m.Name + ", " + show x + ")"
      | NLet(v, x, b) -> "Let (" + v.Name + ", " + show x + ", " + show b + ")"
      | NSequential(a, b) -> "Sequential (" + show a + ", " + show b + ")"
      | NIfThenElse(c, t, f) -> "IfThenElse (" + show c + ", " + show t + ", " + show f + ")"
      | NVarSet(v, x) -> "VarSet (" + v.Name + ", " + show x + ")"
      | NFor(v, lo, hi, b) -> "ForIntegerRangeLoop (" + v.Name + ", " + show lo + ", " + show hi + ", " + show b + ")"
      | NWhile(c, b) -> "WhileLoop (" + show c + ", " + show b + ")"
      | NUnionCaseTest(o, c) -> "UnionCaseTest (" + show o + ", " + c.Name + ")"
      | NTuple es -> "NewTuple (" + all es + ")"
      | NOther(n, es) -> n + " (" + all es + ")"
    show e

/// The patterns of Microsoft.FSharp.Quotations.Patterns the generator uses, over this tree.
module Patterns =
  let (|Var|_|) (e: Expr) = match e.Node with NVar v -> Some v | _ -> None
  let (|Value|_|) (e: Expr) = match e.Node with NValue(v, t) -> Some(v, t) | _ -> None
  let (|Lambda|_|) (e: Expr) = match e.Node with NLambda(v, b) -> Some(v, b) | _ -> None
  let (|Application|_|) (e: Expr) = match e.Node with NApplication(f, a) -> Some(f, a) | _ -> None
  let (|Call|_|) (e: Expr) = match e.Node with NCall(o, m, args) -> Some(o, m, args) | _ -> None
  let (|PropertyGet|_|) (e: Expr) = match e.Node with NPropertyGet(o, m, idx) -> Some(o, m, idx) | _ -> None
  let (|PropertySet|_|) (e: Expr) = match e.Node with NPropertySet(o, m, idx, x) -> Some(o, m, idx, x) | _ -> None
  let (|Let|_|) (e: Expr) = match e.Node with NLet(v, x, b) -> Some(v, x, b) | _ -> None
  let (|Sequential|_|) (e: Expr) = match e.Node with NSequential(a, b) -> Some(a, b) | _ -> None
  let (|IfThenElse|_|) (e: Expr) = match e.Node with NIfThenElse(c, t, f) -> Some(c, t, f) | _ -> None
  let (|VarSet|_|) (e: Expr) = match e.Node with NVarSet(v, x) -> Some(v, x) | _ -> None
  let (|ForIntegerRangeLoop|_|) (e: Expr) = match e.Node with NFor(v, lo, hi, b) -> Some(v, lo, hi, b) | _ -> None
  let (|WhileLoop|_|) (e: Expr) = match e.Node with NWhile(c, b) -> Some(c, b) | _ -> None
  let (|UnionCaseTest|_|) (e: Expr) = match e.Node with NUnionCaseTest(o, c) -> Some(o, c) | _ -> None
  let (|NewTuple|_|) (e: Expr) = match e.Node with NTuple es -> Some es | _ -> None

open Patterns
/// Generic traversals, as Microsoft.FSharp.Quotations.ExprShape: a variable, a lambda, or any other node as its
/// operator and its sub-expressions in a fixed order (a `Let` is its value and a lambda over its body, a loop its bounds
/// and a lambda over its body), rebuilt with new sub-expressions by `RebuildShapeCombination`.
module ExprShape =
  /// How a node is rebuilt from new sub-expressions.
  type Shape = { rebuild: Expr list -> Expr }

  let (|ShapeVar|ShapeLambda|ShapeCombination|) (e: Expr) =
    let range = e.Range
    let ty = e.Type
    let make node = Expr(node, ty, range)
    match e.Node with
    | NVar v -> ShapeVar v
    | NLambda(v, b) -> ShapeLambda(v, b)
    | NValue _ -> ShapeCombination(box { rebuild = fun _ -> e }, [])
    | NApplication(f, a) ->
      ShapeCombination(box { rebuild = (function [ f; a ] -> make (NApplication(f, a)) | _ -> failwith "Application") }, [ f; a ])
    | NCall(o, m, args) ->
      let items = Option.toList o @ args
      ShapeCombination(box { rebuild = fun xs -> (match o with Some _ -> make (NCall(Some xs.Head, m, xs.Tail)) | None -> make (NCall(None, m, xs))) }, items)
    | NPropertyGet(o, m, idx) ->
      let items = Option.toList o @ idx
      ShapeCombination(box { rebuild = fun xs -> (match o with Some _ -> make (NPropertyGet(Some xs.Head, m, xs.Tail)) | None -> make (NPropertyGet(None, m, xs))) }, items)
    | NPropertySet(o, m, idx, x) ->
      let items = Option.toList o @ idx @ [ x ]
      let rebuild (xs: Expr list) =
        let xs, last = List.take (xs.Length - 1) xs, List.last xs
        match o with
        | Some _ -> make (NPropertySet(Some xs.Head, m, xs.Tail, last))
        | None -> make (NPropertySet(None, m, xs, last))
      ShapeCombination(box { rebuild = rebuild }, items)
    | NLet(v, x, b) ->
      let body = Expr(NLambda(v, b), TyFunc(v.Type, b.Type), None)
      let rebuild =
        function
        | [ x; Lambda(v, b) ] -> make (NLet(v, x, b))
        | _ -> failwith "Let"
      ShapeCombination(box { rebuild = rebuild }, [ x; body ])
    | NSequential(a, b) ->
      ShapeCombination(box { rebuild = (function [ a; b ] -> make (NSequential(a, b)) | _ -> failwith "Sequential") }, [ a; b ])
    | NIfThenElse(c, t, f) ->
      ShapeCombination(box { rebuild = (function [ c; t; f ] -> make (NIfThenElse(c, t, f)) | _ -> failwith "IfThenElse") }, [ c; t; f ])
    | NVarSet(v, x) ->
      ShapeCombination(box { rebuild = (function [ x ] -> make (NVarSet(v, x)) | _ -> failwith "VarSet") }, [ x ])
    | NFor(v, lo, hi, b) ->
      let body = Expr(NLambda(v, b), TyFunc(v.Type, b.Type), None)
      let rebuild =
        function
        | [ lo; hi; Lambda(v, b) ] -> make (NFor(v, lo, hi, b))
        | _ -> failwith "ForIntegerRangeLoop"
      ShapeCombination(box { rebuild = rebuild }, [ lo; hi; body ])
    | NWhile(c, b) ->
      ShapeCombination(box { rebuild = (function [ c; b ] -> make (NWhile(c, b)) | _ -> failwith "WhileLoop") }, [ c; b ])
    | NUnionCaseTest(o, c) ->
      ShapeCombination(box { rebuild = (function [ o ] -> make (NUnionCaseTest(o, c)) | _ -> failwith "UnionCaseTest") }, [ o ])
    | NTuple es -> ShapeCombination(box { rebuild = fun xs -> make (NTuple xs) }, es)
    | NOther(n, es) -> ShapeCombination(box { rebuild = fun xs -> make (NOther(n, xs)) }, es)

  let RebuildShapeCombination (shape: obj, args: Expr list) : Expr = (unbox<Shape> shape).rebuild args

/// A field of the parameter or state record.
type IrField =
  { name: string
    ty: IrTy
    /// [<XMemory>]: kept in X.
    xMemory: bool
    /// [<Length n>] of an array field.
    length: int option }

/// A machine as the generator reads it: the layout of its parameter and state records and the three functions.
type IrKernel =
  { name: string
    paramsType: IrTy
    stateType: IrTy
    parameters: IrField array
    /// None for a kernel without state (the unit type).
    state: IrField array option
    init: Member
    trig: Member
    render: Member }

/// The operators of the kernel types (`Acc.op_Addition(Acc, Acc)`), by declaring type, name and parameter types: what `a + b` on
/// kernel values means (the front end registers them; a quotation says `Operators.op_Addition` and leaves the choice to the type).
let typeOperators = Dictionary<IrTy * string * IrTy list, Member>()

/// A call of one of F#'s generic operators (`op_Inequality`...), which the generator evaluates itself on constants.
let fsharpOperator (name: string) (returns: IrTy) : Member =
  { Name = name; Owner = "Microsoft.FSharp.Core.Operators"; Declaring = TyOther "Microsoft.FSharp.Core.Operators"; ReturnType = returns
    ParameterCount = 2; Intrinsic = None; IsConst = false; IsGeneric = false; CanWrite = false; Marks = Set.empty
    Key = "Microsoft.FSharp.Core.Operators." + name; Definition = lazy None
    Invoke = (fun _ -> failwith (name + " is evaluated by the generator")); Get = (fun _ -> failwith (name + " is a method")) }

/// The intrinsics a front end has registered, by name: the generator builds a few calls itself ("acc.ofFrac" around a Frac).
let intrinsics = Dictionary<string, Member>()

/// A registered intrinsic. Front ends register them all before anything is compiled.
let intrinsic (name: string) : Member =
  match intrinsics.TryGetValue name with
  | true, m -> m
  | _ -> failwith $"intrinsic {name} is not registered by the front end"

/// The constant zero of a Frac, Acc or Long.
let mutable zero: IrTy -> Expr = fun t -> failwith $"no zero registered for {t}"

/// Making members and kernels by hand, for a front end that does not read quotations (or a test): no reflection, only
/// the names and types the generator asks for.
module Build =
  let private none (what: string) : obj -> obj = fun _ -> failwith (what + " cannot be evaluated here")

  /// A field of the parameter or state record, or any property.
  let property (owner: string) (name: string) (ty: IrTy) (canWrite: bool) : Member =
    { Name = name; Owner = owner; Declaring = TyOther owner; ReturnType = ty; ParameterCount = 0; Intrinsic = None
      IsConst = false; IsGeneric = false; CanWrite = canWrite; Marks = Set.empty; Key = owner + "." + name
      Definition = lazy None; Invoke = (fun _ -> failwith (name + " is a property")); Get = none name }

  /// A function of the kernel's module whose body is `definition` (a lambda over its parameters), inlined where called, or
  /// an entry point (init, trig, render).
  let func (owner: string) (name: string) (returns: IrTy) (parameterCount: int) (marks: string list) (definition: Expr) : Member =
    { Name = name; Owner = owner; Declaring = TyOther owner; ReturnType = returns; ParameterCount = parameterCount; Intrinsic = None
      IsConst = false; IsGeneric = false; CanWrite = false; Marks = Set.ofList marks; Key = owner + "." + name
      Definition = lazy Some definition; Invoke = (fun _ -> failwith (name + " cannot be evaluated here")); Get = none name }

  let field (name: string) (ty: IrTy) : IrField = { name = name; ty = ty; xMemory = false; length = None }

  /// A call of a registered intrinsic: `call "acc.add" [ a; b ]`.
  let call (name: string) (args: Expr list) : Expr = Expr.Call(intrinsic name, args)

  /// A call of an instance intrinsic: `callOn out "out.write" [ x ]`.
  let callOn (instance: Expr) (name: string) (args: Expr list) : Expr = Expr.Call(instance, intrinsic name, args)

  /// An F# operator on kernel values (`+`, `>>>`, `*`...), resolved to the type's own at compile time.
  let operator (name: string) (returns: IrTy) (args: Expr list) : Expr = Expr.Call(fsharpOperator name returns, args)

  let read (o: Var) (m: Member) : Expr = Expr.PropertyGet(Expr.Var o, m)
  let write (o: Var) (m: Member) (value: Expr) : Expr = Expr.PropertySet(Expr.Var o, m, [], value)
  let constant (value: obj) (ty: IrTy) : Expr = Expr.Value(value, ty)

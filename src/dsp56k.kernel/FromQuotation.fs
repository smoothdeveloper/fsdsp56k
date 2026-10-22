/// The quotation front end: a kernel written as [<ReflectedDefinition>] F# (`Kernel.define "name" <@ init @> <@ trig @> <@ render @>`)
/// read into the compiler's own tree (Ir.fs). Every node of the quotation becomes the node of the same shape, every .NET
/// method or property becomes a `Member` carrying what the generator asks of it (name, owner, intrinsic name, marks, inlinable
/// body, how to evaluate it on constants), every .NET type an `IrTy`, every quotation variable one `Ir.Var`, and the debug
/// range of a node (with --quotations-debug+) its `Range`. Nothing else changes: the generator reads what the quotation said.
module dsp56k.kernel.FromQuotation

open System
open System.Collections.Concurrent
open System.Reflection
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns
open Microsoft.FSharp.Reflection
open dsp56k.kernel.Kernel

/// A .NET type as the generator tells types apart.
let rec tyOf (t: Type) : Ir.IrTy =
  if t = typeof<Frac> then Ir.TyFrac
  elif t = typeof<Acc> then Ir.TyAcc
  elif t = typeof<Long> then Ir.TyLong
  elif t = typeof<Table> then Ir.TyTable
  elif t = typeof<Region> then Ir.TyRegion
  elif t = typeof<Cell> then Ir.TyCell
  elif t = typeof<Cursor> then Ir.TyCursor
  elif t = typeof<Output> then Ir.TyOutput
  elif t = typeof<unit> then Ir.TyUnit
  elif t = typeof<bool> then Ir.TyBool
  elif t = typeof<int> then Ir.TyInt
  elif t = typeof<int64> then Ir.TyInt64
  elif t = typeof<float> then Ir.TyFloat
  elif t.IsArray then Ir.TyArray(tyOf (t.GetElementType()))
  elif FSharpType.IsFunction t then
    let a, r = FSharpType.GetFunctionElements t
    Ir.TyFunc(tyOf a, tyOf r)
  elif t.IsPrimitive then Ir.TyPrim t.Name
  elif FSharpType.IsRecord t then Ir.TyRecord(string t.FullName)
  elif FSharpType.IsUnion t then Ir.TyUnion(string t.FullName)
  else Ir.TyOther(string t.FullName)

let ownerOf (m: MemberInfo) = match m.DeclaringType with null -> "" | t -> string t.FullName

let private operators = "Microsoft.FSharp.Core.Operators"

/// The debug range a quotation node carries when compiled with --quotations-debug+.
let rangeOf (e: Expr) : Ir.Range option =
  e.CustomAttributes |> List.tryPick (fun a ->
    match a with
    | NewTuple [ Value(:? string as tag, _); NewTuple [ Value(:? string as file, _); Value(:? int as line, _); _; _; _ ] ] when tag = "DebugRange" ->
      Some { Ir.file = file; Ir.line = line }
    | _ -> None)

let private vars = ConcurrentDictionary<Var, Ir.Var>()
let private members = ConcurrentDictionary<MemberInfo, Ir.Member>()

let varOf (v: Var) : Ir.Var = vars.GetOrAdd(v, fun v -> Ir.Var(v.Name, tyOf v.Type, v.IsMutable, v.Type.MetadataToken))

/// The marks the generator reads: [<Subroutine>], [<Inline>] and [<HandWritten>] on the method, [<NoUnswitch>] and [<Unroll>] on the method or its module.
let private marksOf (m: MemberInfo) =
  let on (attr: Type) = not (isNull (m.GetCustomAttribute attr)) || (match m.DeclaringType with null -> false | t -> not (isNull (t.GetCustomAttribute attr)))
  Set.ofList [ if not (isNull (m.GetCustomAttribute typeof<SubroutineAttribute>)) then "Subroutine"
               if not (isNull (m.GetCustomAttribute typeof<InlineAttribute>)) then "Inline"
               if not (isNull (m.GetCustomAttribute typeof<HandWrittenAttribute>)) then "Hand"
               if on typeof<NoUnswitchAttribute> then "NoUnswitch"
               if on typeof<UnrollAttribute> then "Unroll" ]

let private intrinsicOf (m: MemberInfo) =
  match m.GetCustomAttribute<IntrinsicAttribute>() with
  | null -> None
  | a -> Some a.Name

let private unsupported (what: string) : obj = fail what

let rec convert (e: Expr) : Ir.Expr =
  let range = rangeOf e
  let ty = tyOf e.Type
  let make node = Ir.Expr.Make(node, ty, range)
  match e with
  | Value(v, t) -> make (Ir.NValue(v, tyOf t))
  | Var v -> make (Ir.NVar(varOf v))
  | Lambda(v, body) -> make (Ir.NLambda(varOf v, convert body))
  | Application(f, a) -> make (Ir.NApplication(convert f, convert a))
  // `Units.plain x` only retypes a unit's Frac: it is `x`
  | Call(None, mi, [ x ]) when intrinsicOf mi = Some "unit.plain" -> convert x
  | Call(o, mi, args) -> make (Ir.NCall(Option.map convert o, memberOf mi, List.map convert args))
  | PropertyGet(o, p, idx) -> make (Ir.NPropertyGet(Option.map convert o, memberOf p, List.map convert idx))
  | PropertySet(o, p, idx, x) -> make (Ir.NPropertySet(Option.map convert o, memberOf p, List.map convert idx, convert x))
  | Let(v, x, b) -> make (Ir.NLet(varOf v, convert x, convert b))
  | Sequential(a, b) -> make (Ir.NSequential(convert a, convert b))
  | IfThenElse(c, t, f) -> make (Ir.NIfThenElse(convert c, convert t, convert f))
  | VarSet(v, x) -> make (Ir.NVarSet(varOf v, convert x))
  | ForIntegerRangeLoop(v, lo, hi, body) -> make (Ir.NFor(varOf v, convert lo, convert hi, convert body))
  | WhileLoop(c, body) -> make (Ir.NWhile(convert c, convert body))
  | UnionCaseTest(o, case) ->
    let test (x: obj) = (fst (FSharpValue.GetUnionFields(x, case.DeclaringType))).Tag = case.Tag
    let c: Ir.Case = { Name = case.Name; Tag = case.Tag; Test = test }
    make (Ir.NUnionCaseTest(convert o, c))
  | NewTuple es -> make (Ir.NTuple(List.map convert es))
  | ExprShape.ShapeVar v -> make (Ir.NVar(varOf v))
  | ExprShape.ShapeLambda(v, b) -> make (Ir.NLambda(varOf v, convert b))
  | ExprShape.ShapeCombination(_, xs) -> make (Ir.NOther(string (e.GetType().Name), List.map convert xs))

/// A method or property as the generator sees it, made once.
and memberOf (m: MemberInfo) : Ir.Member =
  members.GetOrAdd(m, fun m ->
    let owner = ownerOf m
    let declaring = match m.DeclaringType with null -> Ir.TyOther "" | t -> tyOf t
    match m with
    | :? MethodInfo as mi ->
      let invoke (args: obj[]) =
        try mi.Invoke(null, args)
        with :? TargetInvocationException as e -> fail $"{mi.Name}: {e.InnerException}"
      let r: Ir.Member =
        { Ir.Name = mi.Name; Ir.Owner = owner; Ir.Declaring = declaring; Ir.ReturnType = tyOf mi.ReturnType
          Ir.ParameterCount = mi.GetParameters().Length; Ir.Intrinsic = intrinsicOf mi
          Ir.IsConst = not (isNull (mi.GetCustomAttribute<ConstAttribute>())); Ir.IsGeneric = mi.IsGenericMethodDefinition
          Ir.CanWrite = false; Ir.Marks = marksOf mi
          Ir.Key = $"{owner}.{mi.Name}:{mi.MetadataToken}"
          Ir.Definition = lazy (Expr.TryGetReflectedDefinition mi |> Option.map convert)
          Ir.Invoke = invoke
          Ir.Get = fun _ -> fail $"{mi.Name} is a method" }
      // a helper written by hand too: its routine, read when first compiled
      match mi.GetCustomAttribute<HandWrittenAttribute>() with
      | null -> ()
      | a ->
        let home = match mi.DeclaringType with null -> typeof<HandWrittenAttribute> | t -> t
        Hand.register r.Key mi.Name a.Routine (fun () -> Hand.resolve home mi.Name a.Routine)
      r
    | :? PropertyInfo as p ->
      let r: Ir.Member =
        { Ir.Name = p.Name; Ir.Owner = owner; Ir.Declaring = declaring; Ir.ReturnType = tyOf p.PropertyType
          Ir.ParameterCount = 0; Ir.Intrinsic = intrinsicOf p
          Ir.IsConst = not (isNull (p.GetCustomAttribute<ConstAttribute>())); Ir.IsGeneric = false
          Ir.CanWrite = p.CanWrite; Ir.Marks = Set.empty
          Ir.Key = $"{owner}.{p.Name}"
          Ir.Definition = lazy None
          Ir.Invoke = (fun _ -> fail $"{p.Name} is a property")
          Ir.Get = (fun instance -> p.GetValue instance) }
      r
    | _ -> fail $"{m.Name}: neither a method nor a property")

let private fields (t: Type) = fieldsOf t

/// What the generator builds calls and constants of itself from: every intrinsic by name, and the zeros.
let private registered =
  lazy
    (let flags = BindingFlags.Static ||| BindingFlags.Instance ||| BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.DeclaredOnly
     for t in typeof<Frac>.Assembly.GetTypes() do
       for m in t.GetMembers flags do
         match m with
         | :? MethodInfo | :? PropertyInfo ->
           match intrinsicOf m with
           | Some name when not (Ir.intrinsics.ContainsKey name) && not (m :? MethodInfo && (m :?> MethodInfo).IsGenericMethodDefinition) ->
             Ir.intrinsics[name] <- memberOf m
           | _ -> ()
         | _ -> ()
     // `a + b` on kernel values: the operators of the kernel types, by parameter types
     for t in [ typeof<Frac>; typeof<Acc>; typeof<Long> ] do
       for mi in t.GetMethods(BindingFlags.Static ||| BindingFlags.Public) do
         if mi.Name.StartsWith "op_" then
           let types = mi.GetParameters() |> Array.map (fun p -> tyOf p.ParameterType) |> List.ofArray
           Ir.typeOperators[(tyOf t, mi.Name, types)] <- memberOf mi
     // the order of variables of one name: by their .NET type's token, as F# orders quotation variables
     Ir.typeKeyOf <-
       fun t ->
         match t with
         | Ir.TyFrac -> typeof<Frac>.MetadataToken
         | Ir.TyAcc -> typeof<Acc>.MetadataToken
         | Ir.TyLong -> typeof<Long>.MetadataToken
         | Ir.TyTable -> typeof<Table>.MetadataToken
         | Ir.TyRegion -> typeof<Region>.MetadataToken
         | Ir.TyCell -> typeof<Cell>.MetadataToken
         | Ir.TyCursor -> typeof<Cursor>.MetadataToken
         | Ir.TyOutput -> typeof<Output>.MetadataToken
         | Ir.TyUnit -> typeof<unit>.MetadataToken
         | Ir.TyBool -> typeof<bool>.MetadataToken
         | Ir.TyInt -> typeof<int>.MetadataToken
         | Ir.TyInt64 -> typeof<int64>.MetadataToken
         | Ir.TyFloat -> typeof<float>.MetadataToken
         | _ -> 0
     Ir.zero <-
       fun t ->
         match t with
         | Ir.TyFrac -> Ir.Expr.Value(box Frac.zero, Ir.TyFrac)
         | Ir.TyAcc -> Ir.Expr.Value(box Acc.zero, Ir.TyAcc)
         | Ir.TyLong -> Ir.Expr.Value(box Long.zero, Ir.TyLong)
         | other -> fail $"no zero of {other}")

/// Fills the registries the generator reads (intrinsics, operators, zeros): any front end that builds trees needs them.
let ensureRegistered () = registered.Force()

/// The kernel as the generator reads it.
let kernel (k: Kernel) : Ir.IrKernel =
  registered.Force()
  { name = k.name; paramsType = tyOf k.paramsType; stateType = tyOf k.stateType
    parameters = fields k.paramsType
    state = (if k.stateType = typeof<unit> then None else Some(fields k.stateType))
    init = memberOf k.init; trig = memberOf k.trig; render = memberOf k.render }

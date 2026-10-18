/// A front end that reads kernel source through the F# compiler's typed tree (FSharp.Compiler.Service: `FSharpExpr`), not through
/// quotations: the source text of the kernel is checked, each function's typed body is turned into the compiler's own tree
/// (Ir.fs) and handed to `Compile.generateIr`. The `FSharpExpr` API is the one Fable.Standalone's compiler has in the
/// browser, so this is the shape a "paste F# and compile it" front end takes there; here it runs on .NET, where what it
/// cannot get from the tree (the value of a module-level constant, the call of an intrinsic on constants) it gets by
/// reflection on the assemblies already built (`Member.Get`, `Member.Invoke`). A page would get them by running the
/// compiled JavaScript instead.
module dsp56k.kernel.FromTypedTree

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Symbols.FSharpExprPatterns
open dsp56k.kernel
open dsp56k.kernel.Kernel

/// Source files checked together, in order, with the assemblies they refer to.
type Checked =
  { contents: FSharpImplementationFileContents list
    /// What the compiler said about the source (errors included).
    messages: string list }

let private runtimeReferences () =
  let dir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()
  [ for f in Directory.GetFiles(dir, "*.dll") do
      let isManaged = try AssemblyName.GetAssemblyName f |> ignore; true with _ -> false
      if isManaged then yield f ]

/// Checks source files (paths, in compilation order) against the kernel libraries and any further assemblies.
let private sharedChecker = lazy (FSharpChecker.Create(keepAssemblyContents = true))

let check (files: string list) (extraReferences: string list) : Result<Checked, string> =
  let checker = sharedChecker.Value
  let references =
    [ yield! runtimeReferences ()
      typeof<unit>.Assembly.Location
      typeof<Frac>.Assembly.Location
      typeof<Kernel.Kernel>.Assembly.Location
      yield! extraReferences ]
    |> List.distinct
  let options: FSharpProjectOptions =
    { ProjectFileName = "kernel.fsproj"
      ProjectId = None
      SourceFiles = Array.ofList files
      OtherOptions = [| yield "--noframework"; yield "--targetprofile:netcore"; yield "--quotations-debug+"; for r in references -> "-r:" + r |]
      ReferencedProjects = [||]
      IsIncompleteTypeCheckEnvironment = false
      UseScriptResolutionRules = false
      LoadTime = DateTime.UtcNow
      UnresolvedReferences = None
      OriginalLoadReferences = []
      Stamp = None }
  let results = checker.ParseAndCheckProject options |> Async.RunSynchronously
  let errors = results.Diagnostics |> Array.filter (fun d -> d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)
  if errors.Length > 0 then
    Error(errors |> Array.truncate 5 |> Array.map (fun d -> $"{d.FileName}({d.StartLine}): {d.Message}") |> String.concat "\n")
  else
    Ok { contents = results.AssemblyContents.ImplementationFiles; messages = [] }

/// The compiled library of source files, loaded into this process, so that what the kernel asks of the running program (a module
/// value such as a table, a call on constants) can be answered by reflection as for any kernel built with the project. The
/// source's own code runs when its values are read: for a trusted source (the user's own, on loopback) only.
let compileAndLoad (files: string list) (extraReferences: string list) (outPath: string) : Result<unit, string> =
  let checker = sharedChecker.Value
  let references =
    [ yield! runtimeReferences ()
      typeof<unit>.Assembly.Location
      typeof<Frac>.Assembly.Location
      typeof<Kernel.Kernel>.Assembly.Location
      yield! extraReferences ]
    |> List.distinct
  let argv =
    [| yield "fsc.exe"; yield "--noframework"; yield "--targetprofile:netcore"; yield "--target:library"; yield "--nologo"
       yield "--out:" + outPath
       for r in references -> "-r:" + r
       yield! files |]
  let diagnostics, exn = checker.Compile argv |> Async.RunSynchronously
  let errors = diagnostics |> Array.filter (fun d -> d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)
  match exn with
  | Some e -> Error e.Message
  | None when errors.Length > 0 -> Error(errors |> Array.truncate 5 |> Array.map (fun d -> $"{d.FileName}({d.StartLine}): {d.Message}") |> String.concat "\n")
  | None ->
    Reflection.Assembly.LoadFrom outPath |> ignore
    Ok()

// ---------------------------------------------------------------------------------------------
// Types and members.

let rec irTy (t: FSharpType) : Ir.IrTy =
  let t = t.StripAbbreviations()
  if t.IsFunctionType then Ir.TyFunc(irTy t.GenericArguments[0], irTy t.GenericArguments[1])
  elif t.IsTupleType || t.IsStructTupleType then Ir.TyOther "tuple"
  elif t.HasTypeDefinition then
    let e = t.TypeDefinition
    if e.IsArrayType then Ir.TyArray(irTy t.GenericArguments[0])
    else
      match e.TryFullName with
      | Some "dsp56k.kernel.Frac" -> Ir.TyFrac
      | Some "dsp56k.kernel.Acc" -> Ir.TyAcc
      | Some "dsp56k.kernel.Long" -> Ir.TyLong
      | Some "dsp56k.kernel.Table" -> Ir.TyTable
      | Some "dsp56k.kernel.Region" -> Ir.TyRegion
      | Some "dsp56k.kernel.Cell" -> Ir.TyCell
      | Some "dsp56k.kernel.Cursor" -> Ir.TyCursor
      | Some "dsp56k.kernel.Output" -> Ir.TyOutput
      | Some "Microsoft.FSharp.Core.Unit" -> Ir.TyUnit
      | Some "System.Boolean" -> Ir.TyBool
      | Some "System.Int32" -> Ir.TyInt
      | Some "System.Int64" -> Ir.TyInt64
      | Some "System.Double" -> Ir.TyFloat
      | Some name when e.IsFSharpRecord -> Ir.TyRecord name
      | Some name when e.IsFSharpUnion -> Ir.TyUnion name
      | Some name when e.IsValueType && name.StartsWith "System." -> Ir.TyPrim name
      | Some name -> Ir.TyOther name
      | None -> Ir.TyOther(e.DisplayName)
  else Ir.TyOther "?"

let private attributeNamed (name: string) (attrs: seq<FSharpAttribute>) =
  attrs |> Seq.tryFind (fun a -> a.AttributeType.TryFullName = Some name)

let private intrinsicName (attrs: seq<FSharpAttribute>) =
  attributeNamed "dsp56k.kernel.IntrinsicAttribute" attrs
  |> Option.bind (fun a -> match Seq.toList a.ConstructorArguments with [ (_, (:? string as n)) ] -> Some n | _ -> None)

/// The .NET type of an F# entity from the assemblies already loaded (the kernel libraries, the samples).
let private clrType (e: FSharpEntity) : Type option =
  let named =
    match e.TryFullName with
    | Some n -> n
    | None -> e.DisplayName
  // the newest assembly first: a source compiled again (the explorer's) shadows its earlier version
  AppDomain.CurrentDomain.GetAssemblies()
  |> Array.rev
  |> Array.tryPick (fun a ->
    match a.GetType(named.Replace(".", ".")) with
    | null ->
      // a nested module or type: Outer+Inner
      let rec nested (parts: string list) (prefix: string) =
        match parts with
        | [] -> None
        | p :: rest ->
          let candidate = if prefix = "" then p else prefix + "." + p
          match a.GetType candidate with
          | null -> nested rest candidate
          | t ->
            let rec dig (t: Type) (rest: string list) =
              match rest with
              | [] -> Some t
              | n :: more -> match t.GetNestedType(n, BindingFlags.Public ||| BindingFlags.NonPublic) with null -> None | nt -> dig nt more
            dig t rest
      nested (named.Split('.') |> List.ofArray) ""
    | t -> Some t)

/// What the generator asked of .NET reflection while reading a kernel (a module value read, a function called on constants),
/// by member: what a build without .NET would have to supply another way.
let reflected = System.Collections.Concurrent.ConcurrentDictionary<string, int>()
let private touch (what: string) = reflected.AddOrUpdate(what, 1, fun _ n -> n + 1) |> ignore

/// What is known of the source: the typed bodies of the functions defined there, by member.
type Source(contents: FSharpImplementationFileContents list) =
  /// By where the function is declared: a call in another file holds a reference to it that is not the object (or hash) the
  /// declaration holds.
  let keyOf (m: FSharpMemberOrFunctionOrValue) =
    let l = m.DeclarationLocation
    $"{l.FileName}:{l.StartLine}:{l.StartColumn}:{m.CompiledName}"
  let definitions = Dictionary<string, FSharpMemberOrFunctionOrValue list list * FSharpExpr>()
  do
    let rec walk (d: FSharpImplementationFileDeclaration) =
      match d with
      | FSharpImplementationFileDeclaration.Entity(_, subs) -> List.iter walk subs
      | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue(m, args, body) -> definitions[keyOf m] <- (args, body)
      | FSharpImplementationFileDeclaration.InitAction _ -> ()
    for f in contents do List.iter walk f.Declarations
  member _.Definition(m: FSharpMemberOrFunctionOrValue) =
    match definitions.TryGetValue(keyOf m) with
    | true, d -> Some d
    | _ -> None

type Converter(source: Source) =
  let vars = Dictionary<FSharpMemberOrFunctionOrValue, Ir.Var>(HashIdentity.Structural)
  let members = Dictionary<string, Ir.Member>()
  let instances = Dictionary<string, Ir.Member>()
  /// The targets of the decision tree being converted (a `match`): their bodies are placed where the tree reaches them.
  let mutable targets: (FSharpMemberOrFunctionOrValue list * FSharpExpr) list = []

  member this.Var(v: FSharpMemberOrFunctionOrValue) : Ir.Var =
    match vars.TryGetValue v with
    | true, x -> x
    | _ ->
      let x = Ir.Var(v.DisplayName, irTy v.FullType, v.IsMutable)
      vars[v] <- x
      x

  /// A method, a property or a module value, as the generator sees it, made once.
  member this.Member(m: FSharpMemberOrFunctionOrValue) : Ir.Member =
    let owner = match m.DeclaringEntity with Some e -> (defaultArg e.TryFullName e.DisplayName) | None -> ""
    let key = $"{owner}.{m.CompiledName}:{m.DeclarationLocation.StartLine}:{m.DeclarationLocation.StartColumn}"
    match members.TryGetValue key with
    | true, r -> r
    | _ ->
      let attrs = Seq.toList m.Attributes
      let moduleAttrs = match m.DeclaringEntity with Some e -> Seq.toList e.Attributes | None -> []
      let has (name: string) = attributeNamed name attrs |> Option.isSome
      let hasOnModule (name: string) = attributeNamed name moduleAttrs |> Option.isSome
      let marks =
        Set.ofList [ if has "dsp56k.kernel.SubroutineAttribute" then "Subroutine"
                     if has "dsp56k.kernel.NoUnswitchAttribute" || hasOnModule "dsp56k.kernel.NoUnswitchAttribute" then "NoUnswitch"
                     if has "dsp56k.kernel.UnrollAttribute" || hasOnModule "dsp56k.kernel.UnrollAttribute" then "Unroll" ]
      let declaring = match m.DeclaringEntity with Some e -> irTy (e.AsType()) | None -> Ir.TyOther ""
      let parameters = m.CurriedParameterGroups |> Seq.sumBy Seq.length
      let clr = m.DeclaringEntity |> Option.bind clrType
      let flags = BindingFlags.Static ||| BindingFlags.Instance ||| BindingFlags.Public ||| BindingFlags.NonPublic
      let invoke (args: obj[]) : obj =
        touch $"call {owner}.{m.DisplayName}"
        match clr with
        | None -> fail $"{m.DisplayName}: its .NET code is not loaded"
        | Some t ->
          let candidates = t.GetMethods flags |> Array.filter (fun mi -> mi.Name = m.CompiledName && mi.GetParameters().Length = args.Length)
          let types = m.CurriedParameterGroups |> Seq.collect id |> Seq.map (fun p -> (p.Type.StripAbbreviations()).TypeDefinition.TryFullName |> Option.defaultValue "") |> Seq.toArray
          let fits (mi: MethodInfo) =
            let ps = mi.GetParameters()
            types.Length = ps.Length && Array.forall2 (fun (t: string) (p: ParameterInfo) -> t = "" || t = p.ParameterType.FullName || p.ParameterType.IsGenericParameter) types ps
          let chosen = match candidates |> Array.filter fits with [||] -> candidates | c -> c
          match chosen with
          | [||] -> fail $"{m.DisplayName}: no .NET method to call"
          | cs ->
            try (Array.head cs).Invoke(null, args)
            with :? TargetInvocationException as e -> fail $"{m.DisplayName}: {e.InnerException}"
      let get (instance: obj) : obj =
        touch $"read {owner}.{m.DisplayName}"
        match clr with
        | Some t when m.IsModuleValueOrMember && not m.IsInstanceMember ->
          match t.GetProperty(m.CompiledName, flags) with
          | null ->
            match t.GetProperty(m.DisplayName, flags) with
            | null -> fail $"{m.DisplayName}: no .NET property to read"
            | p -> p.GetValue null
          | p -> p.GetValue null
        | _ ->
          match instance with
          | null -> fail $"{m.DisplayName}: no value to read"
          | x ->
            match x.GetType().GetProperty(m.DisplayName, flags) with
            | null -> fail $"{m.DisplayName}: not a property of {x.GetType().Name}"
            | p -> p.GetValue x
      let definition =
        lazy
          (match source.Definition m with
           | Some(args, body) ->
             let rec lambdas (vs: FSharpMemberOrFunctionOrValue list) (inner: Ir.Expr) =
               List.foldBack (fun (v: FSharpMemberOrFunctionOrValue) acc -> Ir.Expr.Lambda(this.Var v, acc)) vs inner
             let converted = this.Expr body
             Some(List.foldBack (fun group acc -> lambdas group acc) args converted)
           | None -> None)
      let r: Ir.Member =
        { Name = (if m.CompiledName.StartsWith "op_" then m.CompiledName else m.DisplayName); Owner = owner; Declaring = declaring; ReturnType = irTy m.ReturnParameter.Type
          ParameterCount = parameters; Intrinsic = intrinsicName attrs; IsConst = has "dsp56k.kernel.ConstAttribute"
          IsGeneric = m.GenericParameters.Count > 0; CanWrite = m.HasSetterMethod; Marks = marks
          Key = key; Definition = definition; Invoke = invoke; Get = get }
      members[key] <- r
      r

  /// A record field, as the property the generator reads and assigns.
  member this.Field(record: FSharpType, f: FSharpField, ?case: string) : Ir.Member =
    let owner = match record.StripAbbreviations().TypeDefinition.TryFullName with Some n -> n | None -> ""
    let key = match case with Some c -> $"{owner}.{c}.{f.Name}" | None -> $"{owner}.{f.Name}"
    match members.TryGetValue key with
    | true, r -> r
    | _ ->
      let get (instance: obj) : obj =
        touch $"field {owner}.{f.Name}"
        match instance with
        | null -> fail $"{f.Name}: no record to read"
        | x ->
          match x.GetType().GetProperty(f.Name, BindingFlags.Instance ||| BindingFlags.Public ||| BindingFlags.NonPublic) with
          | null -> fail $"{f.Name}: not a field of {x.GetType().Name}"
          | p -> p.GetValue x
      let r: Ir.Member =
        { Name = f.Name; Owner = owner; Declaring = irTy record; ReturnType = irTy f.FieldType; ParameterCount = 0; Intrinsic = None
          IsConst = false; IsGeneric = false; CanWrite = f.IsMutable; Marks = Set.empty; Key = key; Definition = lazy None
          Invoke = (fun _ -> fail $"{f.Name} is a field"); Get = get }
      members[key] <- r
      r

  member this.Expr(e: FSharpExpr) : Ir.Expr =
    let range: Ir.Range option = Some { file = e.Range.FileName; line = e.Range.StartLine }
    let ty = irTy e.Type
    let make node = Ir.Expr.Make(node, ty, range)
    let isLocal (v: FSharpMemberOrFunctionOrValue) = not v.IsModuleValueOrMember
    match e with
    | Const(v, t) ->
      let value: obj | null = if ty = Ir.TyUnit then null else v
      make (Ir.NValue(value, irTy t))
    | Value v when isLocal v -> make (Ir.NVar(this.Var v))
    | Value v -> make (Ir.NPropertyGet(None, this.Member v, []))
    | Lambda(v, body) -> make (Ir.NLambda(this.Var v, this.Expr body))
    | Application(f, _, args) ->
      args |> List.fold (fun (acc: Ir.Expr) a -> Ir.Expr.Make(Ir.NApplication(acc, this.Expr a), ty, range)) (this.Expr f)
    | Call(instance, m, _, _, args) ->
      // `input.Next()` is a call with a unit argument in the typed tree and a call with none in a quotation (a function
      // of a module keeps its unit argument in both)
      let args' = if m.IsMember && (match args with [ a ] -> irTy a.Type = Ir.TyUnit | _ -> false) then [] else List.map this.Expr args
      // `acc >>> 6` is the generic operator of the F# library in the typed tree and the kernel type's own operator in a
      // quotation: the one the type declares is looked up by the operand types, as the quotation front end finds it
      let member' =
        let own =
          if m.CompiledName.StartsWith "op_" && (match m.DeclaringEntity with Some e -> e.TryFullName = Some "Microsoft.FSharp.Core.Operators" | None -> false) then
            match args' with
            | first :: _ -> Ir.typeOperators.TryGetValue((first.Type, m.CompiledName, args' |> List.map (fun a -> a.Type)))
            | [] -> false, Unchecked.defaultof<Ir.Member>
          else false, Unchecked.defaultof<Ir.Member>
        match own with
        | true, found -> found
        | _ -> this.Member m
      // a generic function (`(-)`, `max`) is a different member at each instantiation, as in a quotation: the type it returns here
      let member' =
        if m.GenericParameters.Count = 0 then member'
        else
          let key = member'.Key + "<" + String.Join(",", args' |> List.map (fun a -> string a.Type)) + ">" + string ty
          match instances.TryGetValue key with
          | true, i -> i
          | _ ->
            let i = { member' with ReturnType = ty; Key = key }
            instances[key] <- i
            i
      // a static property is a call with a unit argument in the typed tree
      let unitArgs = args |> List.forall (fun a -> irTy a.Type = Ir.TyUnit)
      if (m.IsProperty || m.IsPropertyGetterMethod || member'.ParameterCount = 0) && unitArgs then make (Ir.NPropertyGet(Option.map this.Expr instance, member', []))
      else make (Ir.NCall(Option.map this.Expr instance, member', args'))
    | FSharpFieldGet(instance, record, f) -> make (Ir.NPropertyGet(Option.map this.Expr instance, this.Field(record, f), []))
    | FSharpFieldSet(instance, record, f, value) ->
      make (Ir.NPropertySet(Option.map this.Expr instance, this.Field(record, f), [], this.Expr value))
    | Let((v, value, _), body) -> make (Ir.NLet(this.Var v, this.Expr value, this.Expr body))
    | Sequential(a, b) -> make (Ir.NSequential(this.Expr a, this.Expr b))
    | IfThenElse(c, t, f) -> make (Ir.NIfThenElse(this.Expr c, this.Expr t, this.Expr f))
    | ValueSet(v, value) when isLocal v -> make (Ir.NVarSet(this.Var v, this.Expr value))
    | FastIntegerForLoop(start, limit, body, isUp, _, _) when isUp ->
      match body with
      | Lambda(v, inner) -> make (Ir.NFor(this.Var v, this.Expr start, this.Expr limit, this.Expr inner))
      | _ -> fail "a for loop whose body is not a lambda over its index"
    | WhileLoop(c, body, _) -> make (Ir.NWhile(this.Expr c, this.Expr body))
    | UnionCaseTest(o, unionType, case) ->
      let tag = unionType.StripAbbreviations().TypeDefinition.UnionCases |> Seq.findIndex (fun c -> c.Name = case.Name)
      let clr = unionType.StripAbbreviations().TypeDefinition |> clrType
      let test (x: obj) =
        match clr with
        | Some t -> (fst (Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(x, t))).Tag = tag
        | None -> fail "a union whose .NET type is not loaded"
      let c: Ir.Case = { Name = case.Name; Tag = tag; Test = test }
      make (Ir.NUnionCaseTest(this.Expr o, c))
    // `Bell(table, _, _, click)` reads the fields of the case: the property of the case's class
    | UnionCaseGet(o, unionType, case, f) -> make (Ir.NPropertyGet(Some(this.Expr o), this.Field(unionType, f, case.Name), []))
    // a `match` is a decision tree: tests on the value, and at each leaf the number of the target (the arm) to run, with what
    // its pattern bound; each leaf becomes its arm's body under lets of those variables
    | DecisionTree(decision, arms) ->
      let saved = targets
      targets <- arms
      let converted = this.Expr decision
      targets <- saved
      converted
    | DecisionTreeSuccess(index, values) ->
      let vs, body = targets[index]
      List.foldBack2 (fun (v: FSharpMemberOrFunctionOrValue) (value: FSharpExpr) (acc: Ir.Expr) -> Ir.Expr.Make(Ir.NLet(this.Var v, this.Expr value, acc), ty, range)) vs values (this.Expr body)
    | Coerce(_, inner) -> this.Expr inner
    | _ -> make (Ir.NOther("typed tree node", e.ImmediateSubExpressions |> List.map this.Expr))

/// The kernel `name` made of three functions of the module `moduleName` (its full name; "" for any) of the checked source:
/// `init`, `trig` and `render` (their names), the parameter and state records read off the render function's arguments.
let kernelIn (checked': Checked) (moduleName: string) (name: string) (init: string, trig: string, render: string) : Result<Ir.IrKernel, string> =
  FromQuotation.ensureRegistered ()
  let source = Source(checked'.contents)
  let converter = Converter(source)
  let functions =
    [ for f in checked'.contents do
        let rec walk (d: FSharpImplementationFileDeclaration) =
          seq {
            match d with
            | FSharpImplementationFileDeclaration.Entity(_, subs) -> for s in subs do yield! walk s
            | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue(m, args, _) -> yield m, args
            | _ -> () }
        for d in f.Declarations do yield! walk d ]
  let inModule (m: FSharpMemberOrFunctionOrValue) =
    moduleName = "" || (match m.DeclaringEntity with Some e -> e.TryFullName = Some moduleName | None -> false)
  let find (n: string) = functions |> List.tryFind (fun (m, _) -> m.DisplayName = n && m.IsModuleValueOrMember && inModule m)
  match find init, find trig, find render with
  | Some(i, _), Some(t, _), Some(r, rArgs) ->
    let flat = rArgs |> List.concat
    match flat with
    | p :: s :: _ ->
      let fields (t: FSharpType) =
        let e = t.StripAbbreviations().TypeDefinition
        [| for f in e.FSharpFields ->
             let attrs = Seq.append f.FieldAttributes f.PropertyAttributes |> Seq.toList
             let length =
               attributeNamed "dsp56k.kernel.LengthAttribute" attrs
               |> Option.bind (fun a -> match Seq.toList a.ConstructorArguments with [ (_, (:? int as n)) ] -> Some n | _ -> None)
             ({ name = f.Name; ty = irTy f.FieldType; xMemory = (attributeNamed "dsp56k.kernel.XMemoryAttribute" attrs).IsSome; length = length } : Ir.IrField) |]
      let stateType = irTy s.FullType
      Ok { name = name; paramsType = irTy p.FullType; stateType = stateType
           parameters = fields p.FullType
           state = (if stateType = Ir.TyUnit then None else Some(fields s.FullType))
           init = converter.Member i; trig = converter.Member t; render = converter.Member r }
    | _ -> Error "render must take the parameters and the state"
  | _ -> Error $"the source has no {init}, {trig} and {render}"

/// The kernel `name` made of three functions of the checked source (the first of each name).
let kernel (checked': Checked) (name: string) (names: string * string * string) = kernelIn checked' "" name names

/// A function (or value) of a module of the source, and the lines it covers.
type FunctionSpan = { name: string; startLine: int; endLine: int }

/// The last line any part of an expression is on: the range of a `let` or a sequence covers its first part only.
let rec private lastLine (e: FSharpExpr) : int =
  e.ImmediateSubExpressions |> List.fold (fun last sub -> max last (lastLine sub)) e.Range.EndLine

/// The module-level functions and values the file `file` declares, with their line extents: from the line of the name to the
/// last line of the body. The members the compiler writes are left out.
let functionsOf (checked': Checked) (file: string) : FunctionSpan array =
  [| for f in checked'.contents do
       if f.FileName = file then
         let rec walk (d: FSharpImplementationFileDeclaration) =
           seq {
             match d with
             | FSharpImplementationFileDeclaration.Entity(e, subs) when e.IsFSharpModule || e.IsNamespace -> for s in subs do yield! walk s
             | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue(m, _, body) when m.IsModuleValueOrMember && not m.IsCompilerGenerated ->
               let start = m.DeclarationLocation.StartLine
               yield { name = m.DisplayName; startLine = start; endLine = max start (lastLine body) }
             | _ -> () }
         for d in f.Declarations do yield! walk d |]

/// The typed tree front end's reader: the JSON TypedTreeJson writes (on .NET from FSharp.Compiler.Service, in a page from
/// Fable.Standalone's compiler) in, the kernel IR the code generator reads out. It does what FromTypedTree.fs does with the
/// compiler's symbols, on the JSON instead, so it needs no F# compiler, and nothing here is .NET specific: what has to be
/// asked of the running program (a module value such as a table, a function called on constants, a union case test) goes
/// through a `Host`, which the .NET reflection one (ReflectionHost.fs) or a JavaScript one supplies. Intrinsics, operators and
/// zeros are registered in `Ir` by the host side before a kernel is read.
module dsp56k.kernel.FromJson

open System
open System.Collections.Generic
open dsp56k.kernel
open dsp56k.kernel.Json

/// What the reader cannot know from the tree: the values of the running program.
type Host =
  { /// A module value or a static property: the owner's full name, the property's compiled name, then its display name.
    getStatic: string -> string -> string -> obj
    /// A property of a value (a record field, a union case's field) by name.
    getInstance: obj -> string -> obj
    /// A function called on constants: owner, compiled name, the full names of its parameter types ("" unknown), the arguments.
    invoke: string -> string -> string[] -> obj[] -> obj
    /// Whether a value is the union case: the union's full name, the case's name.
    isCase: string -> string -> obj -> bool }

// ---- reading the JSON ----

let private field (j: J) (k: string) : J =
  match j with
  | Obj fs ->
    match List.tryFind (fun (n: string, _) -> n = k) fs with
    | Some(_, v) -> v
    | None -> Null
  | _ -> Null

let private str (j: J) = match j with Str s -> s | _ -> ""
let private int' (j: J) = match j with Int n -> n | _ -> 0
let private bool' (j: J) = match j with Bool b -> b | _ -> false
let private items (j: J) = match j with Arr xs -> xs | _ -> []
let private isNull' (j: J) = match j with Null -> true | _ -> false

let private ownerOfUnion (j: J) = str (field j "t")

/// A type as the generator tells types apart (FromTypedTree.irTy on the JSON).
let rec tyOf (j: J) : Ir.IrTy =
  match field j "fn" with
  | Arr [ a; r ] -> Ir.TyFunc(tyOf a, tyOf r)
  | _ ->
    if not (isNull' (field j "tuple")) then Ir.TyOther "tuple"
    elif not (isNull' (field j "gp")) || not (isNull' (field j "other")) then Ir.TyOther "?"
    else
      let name = str (field j "t")
      let kind = str (field j "kind")
      if kind = "array" then Ir.TyArray(tyOf (List.head (items (field j "args"))))
      else
        match name with
        | "dsp56k.kernel.Frac" | "dsp56k.kernel.Frac`1" -> Ir.TyFrac  // `Frac<Hz>` too
        | "dsp56k.kernel.Acc" -> Ir.TyAcc
        | "dsp56k.kernel.Long" -> Ir.TyLong
        | "dsp56k.kernel.Table" -> Ir.TyTable
        | "dsp56k.kernel.Region" -> Ir.TyRegion
        | "dsp56k.kernel.Cell" -> Ir.TyCell
        | "dsp56k.kernel.Cursor" -> Ir.TyCursor
        | "dsp56k.kernel.Output" -> Ir.TyOutput
        | "Microsoft.FSharp.Core.Unit" -> Ir.TyUnit
        | "System.Boolean" -> Ir.TyBool
        | "System.Int32" -> Ir.TyInt
        | "System.Int64" -> Ir.TyInt64
        | "System.Double" -> Ir.TyFloat
        | _ ->
          if kind = "record" then Ir.TyRecord name
          elif kind = "union" then Ir.TyUnion name
          elif (kind = "struct" || kind = "enum") && name.StartsWith "System." then Ir.TyPrim name
          else Ir.TyOther name

/// A type known by its full name only (a member's owner).
let private tyOfName (name: string) : Ir.IrTy =
  match name with
  | "dsp56k.kernel.Frac" -> Ir.TyFrac
  | "dsp56k.kernel.Acc" -> Ir.TyAcc
  | "dsp56k.kernel.Long" -> Ir.TyLong
  | "dsp56k.kernel.Table" -> Ir.TyTable
  | "dsp56k.kernel.Region" -> Ir.TyRegion
  | "dsp56k.kernel.Cell" -> Ir.TyCell
  | "dsp56k.kernel.Cursor" -> Ir.TyCursor
  | "dsp56k.kernel.Output" -> Ir.TyOutput
  | "" -> Ir.TyOther ""
  | other -> Ir.TyOther other

/// The names Fable's compiler gives a keyword-named member carry the backticks; .NET's do not.
let private cleanName (n: string) = if n.StartsWith "``" && n.EndsWith "``" && n.Length > 4 then n.Substring(2, n.Length - 4) else n

/// A constant of the type it has in the source.
let private constant (ty: Ir.IrTy) (j: J) : obj =
  match ty, j with
  | Ir.TyUnit, _ -> null
  | _, Null -> null
  | Ir.TyInt64, Str s -> box (Int64.Parse s)
  | Ir.TyInt64, Int n -> box (int64 n)
  | Ir.TyFloat, Str s -> box (Double.Parse(s, Globalization.CultureInfo.InvariantCulture))
  | Ir.TyFloat, Int n -> box (float n)
  | _, Int n -> box n
  | _, Bool b -> box b
  | _, Str s -> box s
  | _ -> failwith "a constant of a kind the reader does not know"

/// The typed tree of a project, read: its declarations by member, its entities by name.
type Tree(root: J, host: Host) =
  let membersJ = Array.ofList (items (field root "members"))
  let varsJ = Array.ofList (items (field root "vars"))
  /// The declared members: parameters (variables, by group), body and the file of the declaration.
  let definitions = Dictionary<int, int list list * J * string>()
  let entities = Dictionary<string, J>()
  do
    let rec walk (file: string) (d: J) =
      match str (field d "k") with
      | "entity" ->
        entities[str (field d "fullName")] <- d
        for s in items (field d "subs") do walk file s
      | "member" ->
        let groups = [ for g in items (field d "args") -> [ for v in items g -> int' v ] ]
        definitions[int' (field d "m")] <- (groups, field d "body", file)
      | _ -> ()
    for f in items (field root "files") do
      let file = str (field f "file")
      for d in items (field f "decls") do walk file d
  let vars = Dictionary<int, Ir.Var>()
  let members = Dictionary<int, Ir.Member>()
  let fields = Dictionary<string, Ir.Member>()
  let instances = Dictionary<string, Ir.Member>()
  /// The targets of the decision tree being read (a `match`): their bodies are placed where the tree reaches them.
  let mutable targets: (int list * J) list = []

  member _.Entities = entities
  member _.MemberCount = membersJ.Length

  member _.MemberJson(id: int) = membersJ[id]
  member _.VarJson(id: int) = varsJ[id]
  member _.Definitions = definitions

  member this.Var(id: int) : Ir.Var =
    match vars.TryGetValue id with
    | true, x -> x
    | _ ->
      let v = varsJ[id]
      let x = Ir.Var(cleanName (str (field v "name")), tyOf (field v "type"), bool' (field v "mutable"))
      vars[id] <- x
      x

  /// A method, a property or a module value, as the generator sees it, made once.
  member this.Member(id: int) : Ir.Member =
    match members.TryGetValue id with
    | true, r -> r
    | _ ->
      let m = membersJ[id]
      let owner = str (field m "owner")
      let displayName = cleanName (str (field m "name"))
      let compiledName = str (field m "compiledName")
      let attrs = items (field m "attrs")
      let ownerAttrs = items (field m "ownerAttrs")
      let has (name: string) (xs: J list) = xs |> List.exists (fun a -> str (field a "t") = name)
      let marks =
        Set.ofList [ if has "dsp56k.kernel.SubroutineAttribute" attrs then "Subroutine"
                     if has "dsp56k.kernel.NoUnswitchAttribute" attrs || has "dsp56k.kernel.NoUnswitchAttribute" ownerAttrs then "NoUnswitch"
                     if has "dsp56k.kernel.UnrollAttribute" attrs || has "dsp56k.kernel.UnrollAttribute" ownerAttrs then "Unroll" ]
      let intrinsic =
        attrs |> List.tryPick (fun a ->
          if str (field a "t") = "dsp56k.kernel.IntrinsicAttribute" then
            match items (field a "args") with
            | [ Str n ] -> Some n
            | _ -> None
          else None)
      let groups = [ for g in items (field m "params") -> items g ]
      let parameterCount = groups |> List.sumBy List.length
      let parameterTypes = [| for g in groups do for p in g -> str (field p "t") |]
      let isStatic = bool' (field m "moduleValueOrMember") && not (bool' (field m "instance"))
      let invoke (args: obj[]) : obj = host.invoke owner compiledName parameterTypes args
      let get (instance: obj) : obj =
        if isStatic then host.getStatic owner compiledName displayName
        else
          match instance with
          | null -> failwith (displayName + ": no value to read")
          | x -> host.getInstance x displayName
      let definition =
        lazy
          (match definitions.TryGetValue id with
           | true, (groups, body, file) ->
             let rec lambdas (vs: int list) (inner: Ir.Expr) =
               List.foldBack (fun (v: int) acc -> Ir.Expr.Lambda(this.Var v, acc)) vs inner
             let converted = this.Expr file body
             Some(List.foldBack (fun group acc -> lambdas group acc) groups converted)
           | _ -> None)
      let r: Ir.Member =
        { Name = (if compiledName.StartsWith "op_" then compiledName else displayName); Owner = owner; Declaring = tyOfName owner
          ReturnType = tyOf (field m "returns"); ParameterCount = parameterCount; Intrinsic = intrinsic
          IsConst = has "dsp56k.kernel.ConstAttribute" attrs; IsGeneric = int' (field m "generics") > 0
          CanWrite = bool' (field m "setter"); Marks = marks
          Key = owner + "." + compiledName + ":" + string (int' (field m "line")) + ":" + string (int' (field m "col")) + ":" + string id
          Definition = definition; Invoke = invoke; Get = get }
      members[id] <- r
      r

  /// Registers what the generator builds calls of itself from, out of the members the tree lists: every intrinsic by name, and
  /// the operators of the kernel types by operand types. A page gets them from the source of the kernel libraries (checked
  /// along with the kernel, so their members are in the tree); .NET has them by reflection (FromQuotation.register).
  member this.Register() =
    for id in 0 .. membersJ.Length - 1 do
      let m = membersJ[id]
      let owner = str (field m "owner")
      let compiledName = str (field m "compiledName")
      let named =
        items (field m "attrs") |> List.tryPick (fun a ->
          if str (field a "t") = "dsp56k.kernel.IntrinsicAttribute" then (match items (field a "args") with [ Str n ] -> Some n | _ -> None) else None)
      match named with
      | Some n when not (Ir.intrinsics.ContainsKey n) && int' (field m "generics") = 0 -> Ir.intrinsics[n] <- this.Member id
      | _ -> ()
      let kernelType = owner = "dsp56k.kernel.Frac" || owner = "dsp56k.kernel.Acc" || owner = "dsp56k.kernel.Long"
      if kernelType && compiledName.StartsWith "op_" && not (bool' (field m "instance")) then
        let types = [ for g in items (field m "params") do for p in items g -> tyOf p ]
        Ir.typeOperators[(tyOfName owner, compiledName, types)] <- this.Member id

  /// A record field (or a union case's), as the property the generator reads and assigns.
  member this.Field(owner: string, case: string, name: string, fieldType: J, isMutable: bool) : Ir.Member =
    let key = if case = "" then owner + "." + name else owner + "." + case + "." + name
    match fields.TryGetValue key with
    | true, r -> r
    | _ ->
      let r: Ir.Member =
        { Name = name; Owner = owner; Declaring = tyOfName owner; ReturnType = tyOf fieldType; ParameterCount = 0; Intrinsic = None
          IsConst = false; IsGeneric = false; CanWrite = isMutable; Marks = Set.empty; Key = key; Definition = lazy None
          Invoke = (fun _ -> failwith (name + " is a field"))
          Get = (fun instance -> (match instance with null -> failwith (name + ": no record to read") | x -> host.getInstance x name)) }
      fields[key] <- r
      r

  /// An expression; `file` is the file of the declaration it is in, which the lines are of unless it says otherwise.
  member this.Expr (file: string) (j: J) : Ir.Expr =
    let ty = tyOf (field j "t")
    let file' = match field j "rf" with Str f -> f | _ -> file
    let range: Ir.Range option = Some { file = file'; line = int' (field j "r") }
    let make node = Ir.Expr.Make(node, ty, range)
    let expr (x: J) = this.Expr file x
    let optional (x: J) = if isNull' x then None else Some(expr x)
    let subs (xs: J) = List.map expr (items xs)
    let kind = str (field j "k")
    let other (children: Ir.Expr list) = make (Ir.NOther("typed tree node", children))
    match kind with
    | "Const" -> make (Ir.NValue(constant ty (field j "v"), ty))
    | "Var" -> make (Ir.NVar(this.Var(int' (field j "v"))))
    | "Member" -> make (Ir.NPropertyGet(None, this.Member(int' (field j "m")), []))
    | "Lambda" -> make (Ir.NLambda(this.Var(int' (field j "v")), expr (field j "b")))
    | "App" ->
      let f = expr (field j "f")
      items (field j "args") |> List.fold (fun (acc: Ir.Expr) a -> Ir.Expr.Make(Ir.NApplication(acc, expr a), ty, range)) f
    | "Call" ->
      let m = this.MemberJson(int' (field j "m"))
      let argsJ = items (field j "args")
      let isUnit (a: J) = tyOf (field a "t") = Ir.TyUnit
      let isMember = bool' (field m "member")
      // `input.Next()` is a call with a unit argument in the typed tree and a call with none in a quotation (a function
      // of a module keeps its unit argument in both)
      let args' = if isMember && (match argsJ with [ a ] -> isUnit a | _ -> false) then [] else List.map expr argsJ
      // `acc >>> 6` is the generic operator of the F# library in the typed tree and the kernel type's own operator in a
      // quotation: the one the type declares is looked up by the operand types
      let compiledName = str (field m "compiledName")
      let member' =
        let own =
          if compiledName.StartsWith "op_" && str (field m "owner") = "Microsoft.FSharp.Core.Operators" then
            match args' with
            | first :: _ -> Ir.typeOperators.TryGetValue((first.Type, compiledName, args' |> List.map (fun a -> a.Type)))
            | [] -> false, Unchecked.defaultof<Ir.Member>
          else false, Unchecked.defaultof<Ir.Member>
        match own with
        | true, found -> found
        | _ -> this.Member(int' (field j "m"))
      // a generic function (`(-)`, `max`) is a different member at each instantiation, as in a quotation: the type it returns here
      let member' =
        if int' (field m "generics") = 0 then member'
        else
          let key = member'.Key + "<" + String.Join(",", args' |> List.map (fun a -> string a.Type)) + ">" + string ty
          match instances.TryGetValue key with
          | true, i -> i
          | _ ->
            let i = { member' with ReturnType = ty; Key = key }
            instances[key] <- i
            i
      // a static property is a call with a unit argument in the typed tree
      let unitArgs = argsJ |> List.forall isUnit
      // `Units.plain x` only retypes a unit's Frac: it is `x`
      if member'.Intrinsic = Some "unit.plain" && args'.Length = 1 then args'.Head
      elif (bool' (field m "property") || bool' (field m "getter") || member'.ParameterCount = 0) && unitArgs then
        make (Ir.NPropertyGet(optional (field j "o"), member', []))
      else make (Ir.NCall(optional (field j "o"), member', args'))
    | "FieldGet" ->
      let o = optional (field j "o")
      make (Ir.NPropertyGet(o, this.Field(ownerOfUnion (field j "type"), "", str (field j "field"), field j "fieldType", bool' (field j "mutable")), []))
    | "FieldSet" ->
      let o = optional (field j "o")
      let f = this.Field(ownerOfUnion (field j "type"), "", str (field j "field"), field j "fieldType", true)
      make (Ir.NPropertySet(o, f, [], expr (field j "value")))
    | "Let" ->
      let v = this.Var(int' (field j "v"))
      let value = expr (field j "value")
      make (Ir.NLet(v, value, expr (field j "b")))
    | "Seq" ->
      let a = expr (field j "a")
      make (Ir.NSequential(a, expr (field j "b")))
    | "If" ->
      let c = expr (field j "c")
      let t = expr (field j "then")
      make (Ir.NIfThenElse(c, t, expr (field j "else")))
    | "VarSet" ->
      let v = this.Var(int' (field j "v"))
      make (Ir.NVarSet(v, expr (field j "value")))
    | "For" when bool' (field j "up") ->
      let body = field j "body"
      if str (field body "k") = "Lambda" then
        let v = this.Var(int' (field body "v"))
        let start = expr (field j "start")
        let limit = expr (field j "limit")
        make (Ir.NFor(v, start, limit, expr (field body "b")))
      else failwith "a for loop whose body is not a lambda over its index"
    | "While" ->
      let c = expr (field j "c")
      make (Ir.NWhile(c, expr (field j "body")))
    | "CaseTest" ->
      let unionName = ownerOfUnion (field j "type")
      let case = str (field j "case")
      let tag =
        match entities.TryGetValue unionName with
        | true, e -> items (field e "cases") |> List.findIndex (fun c -> str c = case)
        | _ -> -1
      let c: Ir.Case = { Name = case; Tag = tag; Test = host.isCase unionName case }
      make (Ir.NUnionCaseTest(expr (field j "o"), c))
    | "CaseGet" ->
      let o = expr (field j "o")
      make (Ir.NPropertyGet(Some o, this.Field(ownerOfUnion (field j "type"), str (field j "case"), str (field j "field"), field j "fieldType", false), []))
    // a `match` is a decision tree: tests on the value, and at each leaf the number of the target (the arm) to run, with what
    // its pattern bound; each leaf becomes its arm's body under lets of those variables
    | "Decision" ->
      let saved = targets
      targets <- [ for t in items (field j "targets") -> [ for v in items (field t "vars") -> int' v ], field t "b" ]
      let converted = expr (field j "decision")
      targets <- saved
      converted
    | "Success" ->
      let vs, body = targets[int' (field j "target")]
      let values = items (field j "values")
      List.foldBack2 (fun (v: int) (value: J) (acc: Ir.Expr) -> Ir.Expr.Make(Ir.NLet(this.Var v, expr value, acc), ty, range)) vs values (expr body)
    | "Coerce" -> expr (field j "o")
    | "TupleGet" -> other [ expr (field j "o") ]
    | "Tuple" | "NewRecord" | "NewCase" | "NewArray" -> other (subs (field j "args"))
    | "Other" -> other (subs (field j "children"))
    | "For" -> other [ expr (field j "start"); expr (field j "limit"); expr (field j "body") ]
    | "MemberSet" -> other [ expr (field j "value") ]
    | _ -> other []

/// The kernel `name` made of three functions of the module `moduleName`, read from a typed tree.
let kernelOf (tree: Tree) (moduleName: string) (name: string) (init: string, trig: string, render: string) : Result<Ir.IrKernel, string> =
  let find (n: string) =
    tree.Definitions
    |> Seq.tryPick (fun kv ->
      let m = tree.MemberJson kv.Key
      if cleanName (str (field m "name")) = n && bool' (field m "moduleValueOrMember") && str (field m "owner") = moduleName then Some kv.Key else None)
  match find init, find trig, find render with
  | Some i, Some t, Some r ->
    let rArgs, _, _ = tree.Definitions[r]
    match List.concat rArgs with
    | p :: s :: _ ->
      let fields (typeJson: J) =
        match tree.Entities.TryGetValue(str (field typeJson "t")) with
        | true, e ->
          [| for f in items (field e "fields") ->
               let attrs = items (field f "attrs")
               let attr (n: string) = attrs |> List.tryFind (fun a -> str (field a "t") = n)
               let length =
                 match attr "dsp56k.kernel.LengthAttribute" with
                 | Some a -> (match items (field a "args") with [ Int n ] -> Some n | _ -> None)
                 | None -> None
               ({ name = str (field f "name"); ty = tyOf (field f "type"); xMemory = (attr "dsp56k.kernel.XMemoryAttribute").IsSome; length = length } : Ir.IrField) |]
        | _ -> [||]
      let pType = field (tree.VarJson p) "type"
      let sType = field (tree.VarJson s) "type"
      let stateType = tyOf sType
      Ok { name = name; paramsType = tyOf pType; stateType = stateType
           parameters = fields pType
           state = (if stateType = Ir.TyUnit then None else Some(fields sType))
           init = tree.Member i; trig = tree.Member t; render = tree.Member r }
    | _ -> Error "render must take the parameters and the state"
  | _ -> Error("the source has no " + init + ", " + trig + " and " + render)

/// The same, from the JSON of a typed tree: the intrinsics and operators are registered already (by the host).
let kernelIn (host: Host) (json: J) (moduleName: string) (name: string) (names: string * string * string) =
  kernelOf (Tree(json, host)) moduleName name names

/// The same, registering first the intrinsics and operators the tree lists (it holds the source of the kernel libraries).
let kernelRegistering (host: Host) (json: J) (moduleName: string) (name: string) (names: string * string * string) =
  let tree = Tree(json, host)
  tree.Register()
  kernelOf tree moduleName name names

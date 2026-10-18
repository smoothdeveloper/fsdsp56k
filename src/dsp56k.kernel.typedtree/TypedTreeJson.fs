/// The F# compiler's typed tree (`FSharpImplementationFileContents`) as JSON text: every declaration, every expression with its
/// type and line, the members and variables it refers to listed once and referred to by number. It uses only what the
/// F# compiler's symbol API gives (no reflection, no System.Text.Json), so the same file compiles on .NET against
/// FSharp.Compiler.Service and under Fable inside Fable.Standalone (Fable's own copy of the compiler), where the page gets the
/// tree of pasted source as this text. A reader (JSON in, the kernel IR out) can then run anywhere.
module dsp56k.kernel.TypedTreeJson

open System
open System.Collections.Generic
open FSharp.Compiler.Symbols
open FSharp.Compiler.Symbols.FSharpExprPatterns
open dsp56k.kernel.Json

/// A type: the entity's full name and kind with its type arguments, a function, a tuple, a generic parameter.
let rec ty (t: FSharpType) : J =
  let t = t.StripAbbreviations()
  if t.IsFunctionType then Obj [ "fn", Arr [ ty t.GenericArguments[0]; ty t.GenericArguments[1] ] ]
  elif t.IsTupleType || t.IsStructTupleType then Obj [ "tuple", Arr [ for a in t.GenericArguments -> ty a ] ]
  elif t.IsGenericParameter then Obj [ "gp", Str t.GenericParameter.Name ]
  elif t.HasTypeDefinition then
    let e = t.TypeDefinition
    let kind =
      if e.IsArrayType then "array"
      elif e.IsFSharpRecord then "record"
      elif e.IsFSharpUnion then "union"
      elif e.IsEnum then "enum"
      elif e.IsValueType then "struct"
      else "class"
    Obj [ "t", Str(match e.TryFullName with Some n -> n | None -> e.DisplayName)
          "kind", Str kind
          "args", Arr [ for a in t.GenericArguments -> ty a ] ]
  else Obj [ "other", Str "?" ]

/// A constant by the type the source gives it, not by what it is at run time: in JavaScript every number is an `int` to a type test.
let private constantJson (t: FSharpType) (v: obj) : J =
  if isNull (box v) then Null
  else
    let t = t.StripAbbreviations()
    match (if t.HasTypeDefinition then t.TypeDefinition.TryFullName else None) with
    | Some "System.Int32" -> Int(unbox<int> v)
    | Some "System.Boolean" -> Bool(unbox<bool> v)
    | Some "System.String" -> Str(unbox<string> v)
    | _ -> Str(string v)

let private attributes (xs: seq<FSharpAttribute>) : J =
  Arr [ for a in xs ->
          Obj [ "t", Str(match a.AttributeType.TryFullName with Some n -> n | None -> a.AttributeType.DisplayName)
                "args", Arr [ for (t, v) in a.ConstructorArguments -> constantJson t v ] ] ]

let private ownerName (m: FSharpMemberOrFunctionOrValue) =
  match m.DeclaringEntity with
  | Some e -> (match e.TryFullName with Some n -> n | None -> e.DisplayName)
  | None -> ""

/// Where a symbol is declared (file, line, column); a symbol the compiler made up has none, and Fable's compiler throws for it.
let private locationOf (m: FSharpMemberOrFunctionOrValue) : string * int * int =
  try
    let l = m.DeclarationLocation
    l.FileName, l.StartLine, l.StartColumn
  with _ -> "", 0, 0

/// Writes a tree: the members and variables met are numbered as they come, and listed after the files.
type Writer() =
  let members = List<J>()
  let memberIds = Dictionary<string, int>()
  let vars = List<J>()
  let varIds = Dictionary<string, int>()
  /// The file of the declaration being written: a line is given with its file only where it differs.
  let mutable file = ""

  member _.Members = members
  member _.Vars = vars

  member this.Member(m: FSharpMemberOrFunctionOrValue) : int =
    let file', line, col = locationOf m
    let parameters =
      try [ for g in m.CurriedParameterGroups -> [ for p in g -> ty p.Type ] ]
      with _ -> []
    let key =
      String.Join("|", [ file'; string line; string col; m.CompiledName; ownerName m
                         String.Join(",", [ for g in parameters -> String.Join(";", [ for p in g -> toText p ]) ]) ])
    match memberIds.TryGetValue key with
    | true, id -> id
    | _ ->
      let id = members.Count
      memberIds[key] <- id
      members.Add Null
      let returns = try ty m.ReturnParameter.Type with _ -> Obj [ "other", Str "?" ]
      let ownerAttrs = match m.DeclaringEntity with Some e -> attributes e.Attributes | None -> Arr []
      members[id] <-
        Obj [ "name", Str m.DisplayName
              "compiledName", Str m.CompiledName
              "owner", Str(ownerName m)
              "file", Str file'
              "line", Int line
              "col", Int col
              "property", Bool m.IsProperty
              "getter", Bool m.IsPropertyGetterMethod
              "member", Bool m.IsMember
              "instance", Bool m.IsInstanceMember
              "moduleValueOrMember", Bool m.IsModuleValueOrMember
              "mutable", Bool m.IsMutable
              "setter", Bool m.HasSetterMethod
              "generics", Int m.GenericParameters.Count
              "params", Arr [ for g in parameters -> Arr g ]
              "returns", returns
              "attrs", attributes m.Attributes
              "ownerAttrs", ownerAttrs ]
      id

  member this.Var(v: FSharpMemberOrFunctionOrValue) : int =
    let file', line, col = locationOf v
    let t = ty v.FullType
    let key = String.Join("|", [ v.DisplayName; file'; string line; string col; toText t ])
    match varIds.TryGetValue key with
    | true, id -> id
    | _ ->
      let id = vars.Count
      varIds[key] <- id
      vars.Add(Obj [ "name", Str v.DisplayName; "type", t; "mutable", Bool v.IsMutable ])
      id

  member this.Expr(e: FSharpExpr) : J =
    let node (kind: string) (fields: (string * J) list) =
      let range =
        [ "r", Int e.Range.StartLine
          if e.Range.FileName <> file then "rf", Str e.Range.FileName ]
      Obj([ "k", Str kind; "t", ty e.Type ] @ range @ fields)
    let sub (x: FSharpExpr) = this.Expr x
    let opt (x: FSharpExpr option) = match x with Some x -> sub x | None -> Null
    let isLocal (v: FSharpMemberOrFunctionOrValue) = not v.IsModuleValueOrMember
    match e with
    | Const(v, t) -> node "Const" [ "v", constantJson t (box v) ]
    | Value v when isLocal v -> node "Var" [ "v", Int(this.Var v) ]
    | Value v -> node "Member" [ "m", Int(this.Member v) ]
    | Lambda(v, body) -> node "Lambda" [ "v", Int(this.Var v); "b", sub body ]
    | Application(f, _, args) -> node "App" [ "f", sub f; "args", Arr(List.map sub args) ]
    | Call(instance, m, _, _, args) -> node "Call" [ "o", opt instance; "m", Int(this.Member m); "args", Arr(List.map sub args) ]
    | FSharpFieldGet(instance, record, f) ->
      node "FieldGet" [ "o", opt instance; "type", ty record; "field", Str f.Name; "fieldType", ty f.FieldType; "mutable", Bool f.IsMutable ]
    | FSharpFieldSet(instance, record, f, value) ->
      node "FieldSet" [ "o", opt instance; "type", ty record; "field", Str f.Name; "fieldType", ty f.FieldType; "value", sub value ]
    | Let((v, value, _), body) -> node "Let" [ "v", Int(this.Var v); "value", sub value; "b", sub body ]
    | Sequential(a, b) -> node "Seq" [ "a", sub a; "b", sub b ]
    | IfThenElse(c, t, f) -> node "If" [ "c", sub c; "then", sub t; "else", sub f ]
    | ValueSet(v, value) when isLocal v -> node "VarSet" [ "v", Int(this.Var v); "value", sub value ]
    | ValueSet(v, value) -> node "MemberSet" [ "m", Int(this.Member v); "value", sub value ]
    | FastIntegerForLoop(start, limit, body, isUp, _, _) ->
      node "For" [ "start", sub start; "limit", sub limit; "body", sub body; "up", Bool isUp ]
    | WhileLoop(c, body, _) -> node "While" [ "c", sub c; "body", sub body ]
    | UnionCaseTest(o, unionType, case) -> node "CaseTest" [ "o", sub o; "type", ty unionType; "case", Str case.Name ]
    | UnionCaseGet(o, unionType, case, f) ->
      node "CaseGet" [ "o", sub o; "type", ty unionType; "case", Str case.Name; "field", Str f.Name; "fieldType", ty f.FieldType ]
    | DecisionTree(decision, targets) ->
      node "Decision"
        [ "decision", sub decision
          "targets", Arr [ for (vs, body) in targets -> Obj [ "vars", Arr [ for v in vs -> Int(this.Var v) ]; "b", sub body ] ] ]
    | DecisionTreeSuccess(index, values) -> node "Success" [ "target", Int index; "values", Arr(List.map sub values) ]
    | Coerce(_, inner) -> node "Coerce" [ "o", sub inner ]
    | TupleGet(_, index, tuple) -> node "TupleGet" [ "o", sub tuple; "index", Int index ]
    | NewTuple(_, args) -> node "Tuple" [ "args", Arr(List.map sub args) ]
    | NewRecord(recordType, args) -> node "NewRecord" [ "type", ty recordType; "args", Arr(List.map sub args) ]
    | NewUnionCase(unionType, case, args) -> node "NewCase" [ "type", ty unionType; "case", Str case.Name; "args", Arr(List.map sub args) ]
    | NewArray(_, args) -> node "NewArray" [ "args", Arr(List.map sub args) ]
    | DefaultValue _ -> node "Default" []
    | _ -> node "Other" [ "children", Arr(e.ImmediateSubExpressions |> List.map sub) ]

  /// The declarations that are in the source: the members the compiler writes for a record or a union (equality, comparison,
  /// hashing) are left out.
  member this.Declaration(d: FSharpImplementationFileDeclaration) : J =
    match d with
    | FSharpImplementationFileDeclaration.Entity(entity, subs) ->
      Obj [ "k", Str "entity"
            "name", Str entity.DisplayName
            "fullName", Str(match entity.TryFullName with Some n -> n | None -> entity.DisplayName)
            "attrs", attributes entity.Attributes
            "fields",
            Arr [ if entity.IsFSharpRecord then
                    for f in entity.FSharpFields ->
                      Obj [ "name", Str f.Name
                            "type", ty f.FieldType
                            "mutable", Bool f.IsMutable
                            "attrs", attributes (Seq.append f.FieldAttributes f.PropertyAttributes) ] ]
            "cases", Arr [ if entity.IsFSharpUnion then for c in entity.UnionCases -> Str c.Name ]
            "subs", Arr [ for s in subs do
                            match s with
                            | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue(m, _, _) when m.IsCompilerGenerated -> ()
                            | _ -> yield this.Declaration s ] ]
    | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue(m, args, body) ->
      Obj [ "k", Str "member"
            "m", Int(this.Member m)
            "args", Arr [ for g in args -> Arr [ for v in g -> Int(this.Var v) ] ]
            "body", this.Expr body ]
    | FSharpImplementationFileDeclaration.InitAction body -> Obj [ "k", Str "init"; "body", this.Expr body ]

  member this.File(f: FSharpImplementationFileContents) : J =
    file <- f.FileName
    Obj [ "file", Str f.FileName; "name", Str f.QualifiedName; "decls", Arr(List.map this.Declaration f.Declarations) ]

/// The whole tree: `{"version":1,"files":[...],"members":[...],"vars":[...]}`.
let ofFiles (files: FSharpImplementationFileContents list) : string =
  let w = Writer()
  let written = files |> List.map w.File
  toText (Obj [ "version", Int 1; "files", Arr written; "members", Arr(List.ofSeq w.Members); "vars", Arr(List.ofSeq w.Vars) ])

/// Kernel F# to DSL items, by direct translation: every expression is evaluated into A (an
/// accumulator value) or X0 (a word), operands go through B, X0 and Y0, and anything that does
/// not fit is spilled to X:(r6+n). Nothing is kept in registers across statements and no move is
/// packed into a parallel slot: the output is correct and slow, and the grade says how slow.
///
/// What a kernel may contain: let / let mutable / <- on locals and state fields; `for _ in a .. b`
/// with constant bounds (DO); `if` on comparisons of Frac or Acc values (CMP, Bcc); intrinsic
/// calls (Intrinsics.fs); calls to [<ReflectedDefinition>] functions, inlined; constants, folded
/// by running the .NET side of the intrinsics.
module dsp56k.kernel.Compile

open System
open System.Reflection
open dsp56k
open dsp56k.dsl
open dsp56k.dsl.Asm
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns
open dsp56k.kernel.Kernel
open System.Collections.Generic

type Acc = dsp56k.kernel.Acc

let owner (mi: MemberInfo) = match mi.DeclaringType with null -> "" | t -> string t.FullName

let someValue (v: obj | null) : obj option = match v with null -> None | v -> Some v

// ---------------------------------------------------------------------------------------------
// Members: intrinsic names, constant evaluation.

let operators = "Microsoft.FSharp.Core.Operators"

/// `a + b` on kernel types is Operators.op_Addition<Acc, Acc, Acc>: the type's own operator.
let resolve (mi: MethodInfo) (args: Expr list) =
  if owner mi = operators && mi.Name.StartsWith "op_" then
    let types = args |> List.map _.Type |> List.toArray
    types
    |> Array.tryPick (fun t ->
      match t.GetMethod(mi.Name, BindingFlags.Static ||| BindingFlags.Public, null, types, null) with
      | null -> None
      | m -> Some m)
    |> Option.defaultValue mi
  else mi

/// Reflected definitions, read once: inlining and the use counts must see the same variables.
let definitions = Collections.Concurrent.ConcurrentDictionary<MethodInfo, Expr option>()
let reflected (mi: MethodInfo) = definitions.GetOrAdd(mi, fun mi -> Expr.TryGetReflectedDefinition mi)

let intrinsic (m: MemberInfo) =
  match m.GetCustomAttribute<IntrinsicAttribute>() with
  | null -> None
  | a -> Some a.Name

let isConst (m: MemberInfo) = not (isNull (m.GetCustomAttribute<ConstAttribute>()))

let foldable (t: Type) =
  t = typeof<Frac> || t = typeof<Acc> || t = typeof<Long> || t = typeof<Table>
  || t.IsPrimitive || Microsoft.FSharp.Reflection.FSharpType.IsUnion t

/// FSharp.Core's generic operators cannot be invoked by reflection: evaluated here on ints,
/// int64s, floats and bools.
let operator (name: string) (args: obj array) : obj option =
  let o (v: 'T) = Some(v :> obj)
  let arith fi fl ff =
    match args with
    | [| :? int as a; :? int as b |] -> o (fi a b)
    | [| :? int64 as a; :? int64 as b |] -> o (fl a b)
    | [| :? float as a; :? float as b |] -> o (ff a b)
    | _ -> None
  let bits fi fl =
    match args with
    | [| :? int as a; :? int as b |] -> o (fi a b)
    | [| :? int64 as a; :? int64 as b |] -> o (fl a b)
    | _ -> None
  let cmp (f: int -> bool) =
    match args with
    | [| :? IComparable as a; b |] when a.GetType() = b.GetType() -> o (f (a.CompareTo b))
    | _ -> None
  match name, args with
  | "op_Addition", _ -> arith (+) (+) (+)
  | "op_Subtraction", _ -> arith (-) (-) (-)
  | "op_Multiply", _ -> arith (*) (*) (*)
  | "op_Division", _ -> arith (/) (/) (/)
  | "op_Modulus", _ -> bits (%) (%)
  | "op_BitwiseAnd", _ -> bits (&&&) (&&&)
  | "op_BitwiseOr", _ -> bits (|||) (|||)
  | "op_ExclusiveOr", _ -> bits (^^^) (^^^)
  | "op_LeftShift", [| :? int as a; :? int as b |] -> o (a <<< b)
  | "op_LeftShift", [| :? int64 as a; :? int as b |] -> o (a <<< b)
  | "op_RightShift", [| :? int as a; :? int as b |] -> o (a >>> b)
  | "op_RightShift", [| :? int64 as a; :? int as b |] -> o (a >>> b)
  | "op_UnaryNegation", [| :? int as a |] -> o -a
  | "op_UnaryNegation", [| :? int64 as a |] -> o -a
  | "op_UnaryNegation", [| :? float as a |] -> o -a
  | "Not", [| :? bool as a |] -> o (not a)
  | "op_Equality", _ -> cmp (fun c -> c = 0)
  | "op_Inequality", _ -> cmp (fun c -> c <> 0)
  | "op_LessThan", _ -> cmp (fun c -> c < 0)
  | "op_LessThanOrEqual", _ -> cmp (fun c -> c <= 0)
  | "op_GreaterThan", _ -> cmp (fun c -> c > 0)
  | "op_GreaterThanOrEqual", _ -> cmp (fun c -> c >= 0)
  | _ -> None

let invoke (mi: MethodInfo) (args: obj array) : obj | null =
  match (if owner mi = operators then operator mi.Name args else None) with
  | Some v -> v
  | None ->
  try mi.Invoke(null, args)
  with :? TargetInvocationException as e -> fail $"{mi.Name}: {e.InnerException}"

// ---------------------------------------------------------------------------------------------
// Compilation state.

type Home =
  { space  : Space
    offset : int
    ty     : Ty
    reg    : Reg option }   // a Frac local kept in X1 or Y1 instead of memory

type Ctx =
  { target   : Target
    kernel   : Kernel
    layout   : Layout
    items    : ResizeArray<Item>
    lines    : ResizeArray<int>     // the F# line each item came from (0: none)
    mutable line   : int
    mutable vars   : Map<Var, Home>
    mutable consts : Map<Var, obj>
    mutable next   : int        // first free X word
    mutable high   : int        // X words used
    mutable labels : int
    mutable cursors : Map<Var, CursorReg>
    mutable freeRegs : int list       // address registers for cursors (r0 is the table index)
    mutable conts : Expr list         // code still to run after the current expression (loop bodies again)
    released : Collections.Generic.HashSet<Var>   // cursors whose register was freed before their scope ended
    mutable trackHome : Home option   // where the entry saved R1, the track number
    mutable indexed : Map<Var * string, int>  // (loop index, state array) -> its address register
    mutable counters : Map<Var, Home>         // loop index -> the word counting it, when read
    mutable freeData : Reg list               // X1, Y1 when no local holds them
    scores   : Dictionary<Var, int>           // uses of each local, weighted by loop trip counts
    deadLocals : HashSet<Var>                 // register locals given back before their scope ended
    tables   : ResizeArray<Table> }   // program tables, emitted after the code

/// A cursor's address register.
and CursorReg =
  { reg    : int
    region : Region
    step   : int
    ring   : bool }

let emit (ctx: Ctx) (it: Item) =
  ctx.items.Add it
  ctx.lines.Add ctx.line

let emitAll (ctx: Ctx) (its: Item seq) = for it in its do emit ctx it

/// The source range quotations carry when compiled with --quotations-debug+.
let debugRange (e: Expr) =
  e.CustomAttributes |> List.tryPick (fun a ->
    match a with
    | NewTuple [ Value(:? string as tag, _); NewTuple [ Value(:? string as file, _); Value(:? int as line, _); _; _; _ ] ] when tag = "DebugRange" ->
      Some(file, line)
    | _ -> None)

let sourceLines = Collections.Concurrent.ConcurrentDictionary<string, string array>()

/// Statements are introduced by a comment quoting their F# line, when it is known.
let at' (ctx: Ctx) (e: Expr) =
  match debugRange e with
  | Some(file, line) when line <> ctx.line ->
    ctx.line <- line
    let text =
      let lines = sourceLines.GetOrAdd(file, fun f -> if IO.File.Exists f then IO.File.ReadAllLines f else [||])
      if line >= 1 && line <= lines.Length then lines[line - 1].Trim() else ""
    if text <> "" then emit ctx (Comment $"{IO.Path.GetFileName file}:{line}  {text}")
  | _ -> ()

let fresh (ctx: Ctx) what =
  ctx.labels <- ctx.labels + 1
  $"K_{what}_{ctx.labels}"

let alloc (ctx: Ctx) ty =
  let h = { space = Space.X; offset = ctx.next; ty = ty; reg = None }
  ctx.next <- ctx.next + words ty
  if ctx.next > ctx.target.xLimit then fail $"{ctx.kernel.name}: locals and spills need more than {ctx.target.xLimit} X words"
  ctx.high <- max ctx.high ctx.next
  h

/// Word k of a home: (r6+offset+k) in its space.
let at (h: Home) k : Mem = { space = h.space; ea = disp r6 (h.offset + k) }

let param (f: Field) : Mem = ym (disp r6 f.offset)

let field (ctx: Ctx) (name: string) =
  ctx.layout.state |> Array.find (fun f -> f.name = name)

let stateHome (f: Field) = { space = Space.Y; offset = f.offset; ty = f.ty; reg = None }

/// Intrinsics that read firmware memory or have effects: never evaluated at compile time (a
/// resource's .NET words are a stand-in for the firmware's).
let effects =
  set [ "out.write"; "out.pair"; "table.read"; "table.row"; "region.address"; "cell.read"
        "cursor.next"; "cursor.peek"; "cursor.put"; "cursor.linear"; "cursor.ring"; "cursor.modulo"
        "cursor.position"; "track.number" ]

let rec tryConst (ctx: Ctx) (e: Expr) : obj option =
  match e with
  | Value(v, t) when foldable t -> someValue v
  | Var v when ctx.consts.ContainsKey v -> Some ctx.consts[v]
  | PropertyGet(None, p, []) when foldable p.PropertyType -> someValue (p.GetValue null)
  | PropertyGet(Some o, p, []) when foldable p.PropertyType ->
    constObject ctx o |> Option.bind (fun x -> someValue (p.GetValue x))
  // `match voice.layer with Metal t -> ...` on a constant: decided when compiling
  | UnionCaseTest(o, case) ->
    constObject ctx o |> Option.map (fun x ->
      let tag, _ = Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(x, case.DeclaringType)
      (tag.Tag = case.Tag) :> obj)
  | Call(None, mi, args) ->
    let mi = resolve mi args
    let pure' = isConst mi || (match intrinsic mi with Some n -> not (Set.contains n effects) | None -> owner mi = operators)
    if pure' && foldable mi.ReturnType then
      let values = args |> List.map (tryConst ctx)
      if values |> List.forall Option.isSome then
        someValue (invoke mi (values |> List.map Option.get |> List.toArray))
      else None
    else None
  | _ -> None

/// Any value known when the kernel is compiled: module values (a voice's configuration, tables,
/// regions), their fields, constant-bound locals, folded expressions.
and constObject (ctx: Ctx) (e: Expr) : obj option =
  match e with
  | PropertyGet(None, p, []) -> someValue (p.GetValue null)
  | PropertyGet(Some o, p, []) -> constObject ctx o |> Option.bind (fun x -> someValue (p.GetValue x))
  | Var v when ctx.consts.ContainsKey v -> Some ctx.consts[v]
  | _ -> tryConst ctx e

let constInt (ctx: Ctx) what (e: Expr) =
  match tryConst ctx e with
  | Some(:? int as n) -> n
  | _ -> fail $"{what} must be a constant int, got {e}"

/// Applications of lambdas reduced: `f a b` on a curried member is quoted as the lambdas F#
/// wraps around the call, which become the call itself; other lambdas bind their arguments.
let rec normalize (e: Expr) : Expr =
  let rec spine (e: Expr) (args: Expr list) =
    match e with
    | Application(f, a) -> spine f (a :: args)
    | f -> f, args
  match e with
  // x |> f, f <| x, (a, b) ||> f: plain applications
  | Call(None, mi, [ x; f ]) when mi.Name = "op_PipeRight" && owner mi = operators -> normalize (Expr.Application(f, x))
  | Call(None, mi, [ f; x ]) when mi.Name = "op_PipeLeft" && owner mi = operators -> normalize (Expr.Application(f, x))
  | Call(None, mi, [ x; y; f ]) when mi.Name = "op_PipeRight2" && owner mi = operators ->
    normalize (Expr.Application(Expr.Application(f, x), y))
  | Application _ ->
    let f, args = spine e []
    let args = args |> List.map normalize
    let rec lambdas (e: Expr) acc =
      match e with
      | Lambda(v, body) -> lambdas body (v :: acc)
      | body -> List.rev acc, body
    let vs, body = lambdas f []
    match body with
    | Call(o, mi, callArgs) when vs.Length = args.Length && callArgs = (vs |> List.map Expr.Var) ->
      match o with
      | Some o -> Expr.Call(normalize o, mi, args)
      | None -> Expr.Call(mi, args)
    | _ ->
      let rec bind (f: Expr) (args: Expr list) =
        match f, args with
        | Lambda(v, rest), (Var _ as a) :: more -> (bind rest more : Expr).Substitute(fun x -> if x = v then Some a else None)
        | Lambda(v, rest), a :: more -> Expr.Let(v, a, bind rest more)
        | f, [] -> normalize f
        | _ -> fail $"cannot reduce the application {e}"
      bind f args
  | ExprShape.ShapeVar _ -> e
  | ExprShape.ShapeLambda(v, body) -> Expr.Lambda(v, normalize body)
  | ExprShape.ShapeCombination(shape, xs) -> ExprShape.RebuildShapeCombination(shape, xs |> List.map normalize)

/// A call to a kernel helper: its parameters bound to the arguments.
let inlined (mi: MethodInfo) (args: Expr list) =
  match reflected mi with
  | Some def ->
    let rec bind (body: Expr) (args: Expr list) =
      match body, args with
      // a variable argument is substituted (the body cannot assign the caller's variable);
      // anything else is bound, evaluated once
      | Lambda(v, rest), (Var _ as a) :: more -> (bind rest more : Expr).Substitute(fun x -> if x = v then Some a else None)
      | Lambda(v, rest), a :: more -> Expr.Let(v, a, bind rest more)
      | body, [] -> body
      | _ -> fail $"{mi.Name}: partial application is not supported"
    normalize (bind def args)
  | None -> fail $"{owner mi}.{mi.Name} is neither an intrinsic nor a [<ReflectedDefinition>] function"

// ---------------------------------------------------------------------------------------------
// Operands that load with single moves.

type Word =
  | WMem of Mem
  | WImm of int
  | WReg of Reg

let loadWord (w: Word) (r: Reg) =
  match w with
  | WMem m -> [ move m r ]
  | WImm 0 -> [ move (imm 0) r ]
  | WImm v -> [ move (immL v) r ]
  | WReg s when s = r -> []
  | WReg s -> [ move s r ]

/// The multiplier takes its two inputs from these pairs only.
let multiplierPairs =
  set [ X0, X0; Y0, Y0; X1, X0; Y1, Y0; X0, Y1; Y0, X0; X1, Y0; Y1, X1 ]

/// Where the two factors go: the registers already holding them where the multiplier allows,
/// X0/Y0 otherwise. The loads, then the pair.
let placeFactors (w1: Word) (w2: Word) =
  let ok (s, t) = multiplierPairs.Contains((s, t)) || multiplierPairs.Contains((t, s))
  let ordered (s, t) = if multiplierPairs.Contains((s, t)) then (s, t) else (t, s)
  match w1, w2 with
  | WReg r1, WReg r2 when r1 <> r2 && ok (r1, r2) -> [], ordered (r1, r2)
  | WReg r, other | other, WReg r when other <> WReg r ->
    let partner = if ok (r, Y0) && other <> WReg Y0 then Y0 else X0
    loadWord other partner, ordered (r, partner)
  | _ -> loadWord w1 X0 @ loadWord w2 Y0, (X0, Y0)

type AccLeaf =
  | AHome of Home              // a spilled or local Acc, hi:lo:ext
  | AConst of int64
  | AOfWord of Word            // Acc.ofFrac
  | AOfLong of Mem * Mem       // Acc.ofLong of a Long in memory

let parts (acc: Reg) =
  match acc with
  | A -> A2, A1, A0
  | B -> B2, B1, B0
  | r -> fail $"{r} is not an accumulator"

let loadAccConst (c: int64) (acc: Reg) =
  let _, _, lo = parts acc
  let e, _, _ = parts acc
  let hi = int ((c >>> 24) &&& 0xffffffL)
  let low = int (c &&& 0xffffffL)
  let ext = int ((c >>> 48) &&& 0xffL)
  [| if c = 0L then clr acc
     else
       move (immL hi) acc
       if low <> 0 then move (immL low) lo
       if ext <> (if hi &&& 0x800000 <> 0 then 0xff else 0) then move (immL ext) e |]

let loadAccLeaf (l: AccLeaf) (acc: Reg) =
  let e, _, lo = parts acc
  match l with
  | AHome h -> [| move (at h 0) acc; move (at h 1) lo; move (at h 2) e |]
  | AConst c -> loadAccConst c acc
  | AOfWord w -> loadWord w acc |> List.toArray
  | AOfLong(hi, l) -> [| move hi acc; move l lo |]

let storeA (h: Home) =
  match h.ty with
  | TAcc -> [| move a1 (at h 0); move a0 (at h 1); move a2 (at h 2) |]
  | TLong -> [| move a1 (at h 0); move a0 (at h 1) |]
  | TFrac -> fail "a Frac home takes a word, not an accumulator"
  | TArray _ -> fail "an array is written element by element"
  | TOther t -> fail $"{t.Name} has no home"

/// The arguments of a call to intrinsic `name`.
let (|Intr|_|) (name: string) (e: Expr) =
  match e with
  | Call(None, mi, args) when intrinsic (resolve mi args) = Some name -> Some args
  | _ -> None

let callName (ctx: Ctx) (e: Expr) =
  match e with
  | Call(_, mi, args) -> intrinsic (resolve mi args)
  | _ -> None

/// `i`, `i + c` or `i - c` for a loop index i: (i, c).
let loopOffset (ctx: Ctx) (isIndex: Var -> bool) (e: Expr) =
  match e with
  | Var v when isIndex v -> Some(v, 0)
  | Call(None, mi, [ Var v; c ]) when isIndex v && mi.Name = "op_Addition" -> Some(v, constInt ctx "an index offset" c)
  | Call(None, mi, [ c; Var v ]) when isIndex v && mi.Name = "op_Addition" -> Some(v, constInt ctx "an index offset" c)
  | Call(None, mi, [ Var v; c ]) when isIndex v && mi.Name = "op_Subtraction" -> Some(v, -(constInt ctx "an index offset" c))
  | _ -> None

/// `s.field[k]` on a [<Length n>] state array: its workset word for a constant k, or the loop's
/// address register for k = i + c.
let (|StateElement|_|) (ctx: Ctx) (e: Expr) =
  match e with
  | Call(None, mi, PropertyGet(Some(Var v), p, []) :: index :: rest)
      when (mi.Name = "GetArray" || mi.Name = "SetArray") && v.Type = ctx.kernel.stateType ->
    let f = field ctx p.Name
    match f.ty with
    | TArray n ->
      match loopOffset ctx (fun i -> ctx.indexed.ContainsKey(i, p.Name)) index with
      | Some(i, c) ->
        let reg = ctx.indexed[i, p.Name]
        Some(({ space = Space.Y; ea = (if c = 0 then Ind reg else Disp(reg, Operand.num c)) } : Mem), rest)
      | None ->
        let k = constInt ctx $"the index into {p.Name} (a constant, or the loop's index plus or minus a constant)" index
        if k < 0 || k >= n then fail $"{p.Name}[{k}] is outside its {n} words"
        Some(({ space = Space.Y; ea = disp r6 (f.offset + k) } : Mem), rest)
    | _ -> None
  | _ -> None

let rec wordLeaf (ctx: Ctx) (e: Expr) : Word option =
  match tryConst ctx e with
  | Some(:? Frac as f) -> Some(WImm f.Word)
  | _ ->
    match e with
    | Var v when ctx.vars.ContainsKey v && ctx.vars[v].ty = TFrac ->
      match ctx.vars[v].reg with
      | Some r -> Some(WReg r)
      | None -> Some(WMem(at ctx.vars[v] 0))
    | StateElement ctx (m, []) -> Some(WMem m)
    // a1 or a0 of an accumulator kept in memory: the word itself
    | Call(None, mi, [ x ]) when (intrinsic mi = Some "acc.hi" || intrinsic mi = Some "acc.lo") && (accLeaf ctx x).IsSome ->
      let word = if intrinsic mi = Some "acc.hi" then 0 else 1
      match (accLeaf ctx x).Value with
      | AHome h -> Some(WMem(at h word))
      | AOfLong(hi, lo) -> Some(WMem(if word = 0 then hi else lo))
      | AOfWord w when word = 0 -> Some w
      | _ -> None
    | Call(None, mi, [ Var i ]) when mi.Name = "ofRaw" && mi.DeclaringType = typeof<Frac> && ctx.counters.ContainsKey i ->
      Some(WMem(at ctx.counters[i] 0))
    | Call(None, mi, _) when intrinsic mi = Some "track.number" ->
      match ctx.trackHome with
      | Some h -> Some(WMem(at h 0))
      | None -> fail "the track number is only known in an entry point that reads it"
    | PropertyGet(Some(Var v), p, []) when v.Type = ctx.kernel.paramsType ->
      Some(WMem(param (ctx.layout.parameters |> Array.find (fun f -> f.name = p.Name))))
    | PropertyGet(Some(Var v), p, []) when v.Type = ctx.kernel.stateType && (field ctx p.Name).ty = TFrac ->
      Some(WMem(at (stateHome (field ctx p.Name)) 0))
    | _ -> None

and longLeaf (ctx: Ctx) (e: Expr) : AccLeaf option =
  match tryConst ctx e with
  | Some(:? Long as l) -> Some(AConst l.Raw)
  | _ ->
    match e with
    | Var v when ctx.vars.ContainsKey v && ctx.vars[v].ty = TLong ->
      let h = ctx.vars[v]
      Some(AOfLong(at h 0, at h 1))
    | PropertyGet(Some(Var v), p, []) when v.Type = ctx.kernel.stateType && (field ctx p.Name).ty = TLong ->
      let h = stateHome (field ctx p.Name)
      Some(AOfLong(at h 0, at h 1))
    | _ -> None

and accLeaf (ctx: Ctx) (e: Expr) : AccLeaf option =
  match tryConst ctx e with
  | Some(:? Acc as a) -> Some(AConst a.Raw)
  | _ ->
    match e with
    | Var v when ctx.vars.ContainsKey v && ctx.vars[v].ty = TAcc -> Some(AHome ctx.vars[v])
    | PropertyGet(Some(Var v), p, []) when v.Type = ctx.kernel.stateType && (field ctx p.Name).ty = TAcc ->
      Some(AHome(stateHome (field ctx p.Name)))
    | Call(None, mi, [x]) ->
      match intrinsic (resolve mi [x]) with
      | Some "acc.ofFrac" -> wordLeaf ctx x |> Option.map AOfWord
      | Some "acc.ofLong" -> longLeaf ctx x
      | _ -> None
    | _ -> None

/// Generate `first` knowing that `rest` runs after it.
let thenRun (ctx: Ctx) (rest: Expr) (first: unit -> unit) =
  let saved = ctx.conts
  ctx.conts <- rest :: saved
  first ()
  ctx.conts <- saved

let tableLabel (t: Table) = "K_TBL_" + t.name.ToUpperInvariant().Replace("-", "_")

// ---------------------------------------------------------------------------------------------
// Expressions.

/// A local whose value can be read in place of the variable: a constant, a parameter, an
/// immutable local.
let substitutable (ctx: Ctx) (v: Var) (value: Expr) =
  not v.IsMutable
  && (match value with
      | Var w -> not w.IsMutable && ctx.vars.ContainsKey w
      | PropertyGet(Some(Var p), _, []) -> p.Type = ctx.kernel.paramsType
      | _ -> (tryConst ctx value).IsSome)

let rec genAcc (ctx: Ctx) (e: Expr) : unit =
  match accLeaf ctx e with
  | Some l -> emitAll ctx (loadAccLeaf l A)
  | None ->
    match e with
    | Let(v, value, body) -> withLet ctx v value body (fun () -> genAcc ctx body)
    | IfThenElse(c, t, f) when (match tryConst ctx c with Some(:? bool) -> true | _ -> false) ->
      genAcc ctx (if (tryConst ctx c).Value :?> bool then t else f)
    | Sequential(s, rest) -> thenRun ctx rest (fun () -> genStmt ctx s); genAcc ctx rest
    | Call(None, mi, args) ->
      let mi = resolve mi args
      match intrinsic mi, args with
      | Some "acc.ofFrac", [f] -> genWord ctx f; emit ctx (move x0 a)
      | Some "acc.ofLong", [l] -> genLong ctx l
      | Some "acc.add", [l; Intr "frac.mul" [x; y]] -> mulInto ctx (Some l) x y (MulKind.Mac, false)
      | Some "acc.add", [Intr "frac.mul" [x; y]; r] -> mulInto ctx (Some r) x y (MulKind.Mac, false)
      | Some "acc.sub", [l; Intr "frac.mul" [x; y]] -> mulInto ctx (Some l) x y (MulKind.Mac, true)
      | Some "acc.neg", [Intr "frac.mul" [x; y]] -> mulInto ctx None x y (MulKind.Mpy, true)
      | Some "acc.add", [l; r] -> binAcc ctx l r (add b a)
      | Some "acc.sub", [l; r] -> binAcc ctx l r (sub b a)
      | Some "acc.rnd", [Intr "frac.mul" [x; y]] -> mulInto ctx None x y (MulKind.Mpyr, false)
      | Some "acc.rnd", [Intr "acc.mac" [l; x; y]] -> mulInto ctx (Some l) x y (MulKind.Macr, false)
      | Some "acc.rnd", [Intr "acc.add" [l; Intr "frac.mul" [x; y]]] -> mulInto ctx (Some l) x y (MulKind.Macr, false)
      | Some "acc.rnd", [Intr "acc.add" [Intr "frac.mul" [x; y]; r]] -> mulInto ctx (Some r) x y (MulKind.Macr, false)
      | Some "acc.rnd", [Intr "acc.sub" [l; Intr "frac.mul" [x; y]]] -> mulInto ctx (Some l) x y (MulKind.Macr, true)
      | Some "acc.rnd", [x] -> genAcc ctx x; emit ctx (rnd a)
      | Some "acc.divide", [x; d] ->
        let w = (operands ctx [ d ])[0]
        genAcc ctx x
        emitAll ctx (loadWord w x0)
        emit ctx (asl a)
        emit ctx (rep (imm 24))
        emit ctx (div x0 a)
      | Some "acc.addFrac", [l; f] -> binAccWord ctx l f (fun r -> add r a)
      | Some "acc.subFrac", [l; f] -> binAccWord ctx l f (fun r -> sub r a)
      | Some "acc.neg", [x] -> genAcc ctx x; emit ctx (neg a)
      | Some "acc.asr", [x; n] ->
        let n = constInt ctx "a shift" n
        genAcc ctx x
        if n = 1 then emit ctx (asr a) elif n > 1 then emit ctx (asri n a a)
      | Some "acc.asl", [x; n] ->
        let n = constInt ctx "a shift" n
        genAcc ctx x
        if n = 1 then emit ctx (asl a) elif n > 1 then emit ctx (asli n a a)
      | Some "acc.andHi", [m; x] ->
        let m = constInt ctx "andHi's mask" m
        genAcc ctx x
        emit ctx (and' (immL (m &&& 0xffffff)) a)
      | Some "frac.mul", [x; y] -> mulInto ctx None x y (MulKind.Mpy, false)
      | Some "acc.mac", [acc; x; y] -> mulInto ctx (Some acc) x y (MulKind.Mac, false)
      | Some n, _ -> fail $"{n} does not give an Acc"
      | None, _ -> genAcc ctx (inlined mi args)
    | _ -> fail $"not a kernel Acc expression: {e}"

/// A multiplication: the factors in X0 and Y0, the accumulated value (if any) in A.
and mulInto (ctx: Ctx) (acc: Expr option) (x: Expr) (y: Expr) (kind: MulKind, negate: bool) =
  let xs = operands ctx [ x; y ]
  acc |> Option.iter (genAcc ctx)
  match xs with
  | [| WImm v; w |] | [| w; WImm v |] when (match w with WImm _ -> false | _ -> true) ->
    // a constant factor rides in the instruction: mpyi/maci #c,s,a
    let r, loads = match w with WReg r -> r, [] | _ -> X0, loadWord w X0
    emitAll ctx loads
    emit ctx (Op(MulI(kind, negate, immL v, r, dsp56k.Acc.A)))
  | _ ->
    let loads, (s, t) = placeFactors xs[0] xs[1]
    emitAll ctx loads
    emit ctx (alu (Mul(kind, negate, s, t, dsp56k.Acc.A)))

/// l op r with l in A and r in B.
and binAcc (ctx: Ctx) (l: Expr) (r: Expr) (op: Item) =
  match accLeaf ctx r with
  | Some rl ->
    genAcc ctx l
    emitAll ctx (loadAccLeaf rl B)
  | None ->
    genAcc ctx r
    let t = alloc ctx TAcc
    emitAll ctx (storeA t)
    genAcc ctx l
    emitAll ctx (loadAccLeaf (AHome t) B)
  emit ctx op

/// l op f with l in A and f in X0.
and binAccWord (ctx: Ctx) (l: Expr) (f: Expr) (op: Reg -> Item) =
  let w = (operands ctx [ f ])[0]
  genAcc ctx l
  match w with
  | WReg r -> emit ctx (op r)
  | _ ->
    emitAll ctx (loadWord w x0)
    emit ctx (op x0)

/// Each Frac as something one move loads: computed and spilled first when it is not.
and operands (ctx: Ctx) (es: Expr list) : Word array =
  [| for e in es do
       match wordLeaf ctx e with
       | Some w -> w
       | None ->
         genWord ctx e
         let t = alloc ctx TFrac
         emit ctx (move x0 (at t 0))
         WMem(at t 0) |]

/// A Long expression into A (a1:a0, a2 its sign).
and genLong (ctx: Ctx) (e: Expr) =
  match longLeaf ctx e with
  | Some l -> emitAll ctx (loadAccLeaf l A)
  | None ->
    match e with
    | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "long.ofAcc" ->
      genAcc ctx x
      let t = alloc ctx TLong
      emitAll ctx (storeA t)
      emitAll ctx (loadAccLeaf (AOfLong(at t 0, at t 1)) A)
    | Let(v, value, body) -> withLet ctx v value body (fun () -> genLong ctx body)
    | Call(None, mi, args) when (intrinsic (resolve mi args)).IsNone -> genLong ctx (inlined mi args)
    | _ -> fail $"not a kernel Long expression: {e}"

/// A Frac expression into X0.
and genWord (ctx: Ctx) (e: Expr) : unit =
  match wordLeaf ctx e with
  | Some w -> emitAll ctx (loadWord w x0)
  | None ->
    match e with
    | Let(v, value, body) -> withLet ctx v value body (fun () -> genWord ctx body)
    | IfThenElse(c, t, f) when (match tryConst ctx c with Some(:? bool) -> true | _ -> false) ->
      genWord ctx (if (tryConst ctx c).Value :?> bool then t else f)
    | Sequential(s, rest) -> thenRun ctx rest (fun () -> genStmt ctx s); genWord ctx rest
    | Call(None, mi, args) ->
      let mi = resolve mi args
      match intrinsic mi, args with
      | Some "acc.hi", [x] -> genAcc ctx x; emit ctx (move a1 x0)
      | Some "acc.lo", [x] -> genAcc ctx x; emit ctx (move a0 x0)
      | Some "acc.sat", [x] -> genAcc ctx x; emit ctx (move a x0)
      | Some "table.read", [t; i] -> tableRead ctx t i 1 0 x0
      | Some "table.row", [t; w; i; c] -> tableRead ctx t i (constInt ctx "a table width" w) (constInt ctx "a table column" c) x0
      | Some "region.address", [r] ->
        let region : Region = constRegion ctx r
        importHere ctx region.name region.abiSymbol (move (immL 0) x0)
      | Some "cell.read", [c] ->
        let cell : Cell =
          match tryConst' ctx c with
          | Some(:? Cell as c) -> c
          | _ -> fail $"a cell must be a constant, got {c}"
        importHere ctx cell.name cell.abiSymbol (move (xm (absL 0)) x0)
      | Some "frac.lfsr", [taps; st] ->
        let taps = constInt ctx "LFSR taps" taps
        genAcc ctx (Expr.Call(nonNull (typeof<Acc>.GetMethod "ofFrac"), [ st ]))
        let skip = fresh ctx "LFSR"
        emit ctx (lsl a)
        emit ctx (bcc Cond.Cc skip)
        emit ctx (eor (immL (taps &&& 0xffffff)) a)
        emit ctx (Label skip)
        emit ctx (move a1 x0)
      | Some n, _ -> fail $"{n} does not give a Frac"
      | None, _ -> genWord ctx (inlined mi args)
    | Call(Some(Var c), mi, []) when ctx.cursors.ContainsKey c ->
      match intrinsic mi with
      | Some "cursor.next" -> emit ctx (move (cursorMem ctx.cursors[c] true) x0)
      | Some "cursor.peek" -> emit ctx (move (cursorMem ctx.cursors[c] false) x0)
      | Some "cursor.position" ->
        let cr = ctx.cursors[c]
        emit ctx (move (R cr.reg) a)
        importHere ctx cr.region.name cr.region.abiSymbol (move (immL 0) x0)
        emit ctx (sub x0 a)
        emit ctx (move a1 x0)
      | _ -> fail $"{mi.Name} on a cursor does not give a Frac"
    | _ -> fail $"not a kernel Frac expression: {e}"

/// Table word `index * width + col`: a resource as `move x:(r0+table),dst` (the address patched
/// by the loader), program words as `move p:(r0),dst` (the address relocated).
and tableRead (ctx: Ctx) (t: Expr) (index: Expr) (width: int) (col: int) (dst: Reg) =
  let table =
    match tryConst' ctx t with
    | Some(:? Table as t) -> t
    | _ -> fail $"a table must be a constant, got {t}"
  let simple = width = 1 && (table.source <> Program)
  if simple && col = 0 then
    match index with
    | _ when (wordLeaf ctx index).IsSome -> emitAll ctx (loadWord (wordLeaf ctx index).Value r0)
    | Intr "acc.hi" [x] ->
      genAcc ctx x
      emit ctx (move a1 r0)
    | _ ->
      match wordLeaf ctx index with
      | Some w -> emitAll ctx (loadWord w r0)
      | None ->
        genWord ctx index
        emit ctx (move x0 r0)
  else
    // a1 = index * width, by shifts and adds
    genAcc ctx (Expr.Call(nonNull (typeof<Acc>.GetMethod "ofFrac"), [ index ]))
    if width > 1 then
      let bits = Convert.ToString(width, 2)
      emit ctx (tfr a b)
      for c in bits.Substring 1 do
        emit ctx (asl a)
        if c = '1' then emit ctx (add b a)
    match table.source with
    | Program ->
      if not (ctx.tables |> Seq.exists (fun x -> x.name = table.name)) then ctx.tables.Add table
      let l = fresh ctx "TABLE_ADDR"
      emit ctx (Label l)
      emit ctx machinedrum.Abi.relocateMarker
      emit ctx (add (immL (Sym(tableLabel table) + col)) a)
    | Resource _ ->
      if col <> 0 then emit ctx (add (immL col) a)
    emit ctx (move a1 r0)
  match table.source with
  | Resource symbol -> importHere ctx table.name symbol (move (xm (dispL r0 0)) dst)
  | Program -> emit ctx (move (pm (ind r0)) dst)

/// `name: ; mds: import ...` before an instruction whose second word the loader patches.
and importHere (ctx: Ctx) (name: string) (symbol: int) (instr: Item) =
  emit ctx (Label(fresh ctx (name.ToUpperInvariant())))
  emit ctx (machinedrum.Abi.importMarker (machinedrum.Mds.importKind symbol) symbol)
  emit ctx instr

and constRegion (ctx: Ctx) (r: Expr) : Region =
  match tryConst' ctx r with
  | Some(:? Region as r) -> r
  | _ -> fail $"a region must be a constant, got {r}"

/// Constants including firmware descriptions (tables, regions, cells), which are values the
/// compiler reads but never folds into reads.
and tryConst' (ctx: Ctx) (e: Expr) : obj option = constObject ctx e

/// A cursor's next word or its current word.
and cursorMem (c: CursorReg) (advance: bool) : Mem =
  let space = match c.region.bank with XMemory -> Space.X | YMemory -> Space.Y
  let ea = if not advance then Ind c.reg elif c.step = 1 then PostInc c.reg else PostIncN c.reg
  { space = space; ea = ea }

/// A cursor bound for the scope of `body`: an address register at region + start, modulo the
/// region's size for a ring, its step in nn.
and withCursor (ctx: Ctx) (v: Var) (value: Expr) (bodyExpr: Expr) (body: unit -> unit) =
  let (region: Region), start, step, ring, modulus =
    match value with
    | Intr "cursor.linear" [r; start] -> constRegion ctx r, start, 1, false, None
    | Intr "cursor.ring" [r; start; step] -> constRegion ctx r, start, constInt ctx "a cursor step" step, true, None
    | Intr "cursor.modulo" [r; start; last] -> constRegion ctx r, start, 1, true, Some last
    | _ -> fail $"a cursor is bound to Cursor.linear or Cursor.ring, not {value}"
  // cursors no code still to run reads give their registers back
  let ahead = value :: bodyExpr :: ctx.conts
  for KeyValue(w, c) in ctx.cursors do
    let live = ahead |> List.exists (fun e -> e.GetFreeVars() |> Seq.contains w)
    if not live && ctx.released.Add w then
      if c.ring then emit ctx (movec (immL 0xffffff) (M c.reg))
      ctx.freeRegs <- c.reg :: ctx.freeRegs
  let reg =
    match ctx.freeRegs with
    | r :: rest -> ctx.freeRegs <- rest; r
    | [] -> fail "too many live cursors at once (r1..r5)"
  match tryConst ctx start with
  | Some(:? Frac as f) when f.Raw = 0 -> importHere ctx region.name region.abiSymbol (move (immL 0) (R reg))
  | _ ->
    genWord ctx start
    importHere ctx region.name region.abiSymbol (move (immL 0) a)
    emit ctx (add x0 a)
    emit ctx (move a1 (R reg))
  match modulus with
  | Some last ->
    genWord ctx last
    emit ctx (movec x0 (M reg))
  | None -> if ring then emit ctx (movec (immL (region.words.Length - 1)) (M reg))
  if step <> 1 then emit ctx (move (immL step) (N reg))
  let saved = ctx.cursors
  ctx.cursors <- ctx.cursors.Add(v, { reg = reg; region = region; step = step; ring = ring })
  body ()
  ctx.cursors <- saved
  if not (ctx.released.Remove v) then
    if ring then emit ctx (movec (immL 0xffffff) (M reg))
    ctx.freeRegs <- reg :: ctx.freeRegs

/// The value of e into a home.
and store (ctx: Ctx) (h: Home) (e: Expr) =
  match h.ty with
  | TFrac when h.reg.IsSome -> storeWordReg ctx e h.reg.Value
  | TFrac -> storeWord ctx e (at h 0)
  | TAcc -> genAcc ctx e; emitAll ctx (storeA h)
  | TLong ->
    match e with
    | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "long.ofAcc" -> genAcc ctx x
    | _ -> genLong ctx e
    emitAll ctx (storeA h)
  | TArray _ -> fail "an array state field is written element by element, s.field[k] <- v"
  | TOther t -> fail $"{t.Name} values cannot be stored"

/// A Frac into a memory word: an accumulator stored directly when it is one.
and storeWord (ctx: Ctx) (e: Expr) (dst: Mem) =
  match e with
  | _ when (match wordLeaf ctx e with Some(WMem _) | Some(WReg _) -> true | _ -> false) ->
    match wordLeaf ctx e with
    | Some(WReg r) -> emit ctx (move r dst)
    | Some w ->
      emitAll ctx (loadWord w x0)
      emit ctx (move x0 dst)
    | None -> ()
  | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "acc.sat" -> genAcc ctx x; emit ctx (move a dst)
  | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "acc.hi" -> genAcc ctx x; emit ctx (move a1 dst)
  | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "acc.lo" -> genAcc ctx x; emit ctx (move a0 dst)
  | _ ->
    match wordLeaf ctx e with
    | Some(WReg r) -> emit ctx (move r dst)
    | _ ->
      genWord ctx e
      emit ctx (move x0 dst)

/// A Frac into a data register.
and storeWordReg (ctx: Ctx) (e: Expr) (dst: Reg) =
  match e with
  | _ when (wordLeaf ctx e).IsSome -> emitAll ctx (loadWord (wordLeaf ctx e).Value dst)
  | Intr "acc.sat" [x] -> genAcc ctx x; emit ctx (move a dst)
  | Intr "acc.hi" [x] -> genAcc ctx x; emit ctx (move a1 dst)
  | Intr "acc.lo" [x] -> genAcc ctx x; emit ctx (move a0 dst)
  | _ ->
    match wordLeaf ctx e with
    | Some w -> emitAll ctx (loadWord w dst)
    | None ->
      genWord ctx e
      emit ctx (move x0 dst)

and withLet (ctx: Ctx) (v: Var) (value: Expr) (bodyExpr: Expr) (body: unit -> unit) =
  if v.Type = typeof<Cursor> then withCursor ctx v value bodyExpr body
  elif (match tyOf v.Type with TOther _ -> true | _ -> false) then
    // not a DSP value: a table, a region, a configuration record... known at compile time
    match constObject ctx value with
    | Some c when not v.IsMutable ->
      let saved = ctx.consts
      ctx.consts <- ctx.consts.Add(v, c)
      body ()
      ctx.consts <- saved             // an inlined helper's variables are shared by its instances
    | _ -> fail $"{v.Name}: a {v.Type.Name} must be an immutable constant (only Frac, Acc and Long live on the DSP)"
  else
  match tryConst ctx value with
  | Some c when not v.IsMutable ->
    let saved = ctx.consts
    ctx.consts <- ctx.consts.Add(v, c)
    body ()
    ctx.consts <- saved
  | _ when substitutable ctx v value ->
    let saved = ctx.vars
    match value with
    | Var w -> ctx.vars <- ctx.vars.Add(v, ctx.vars[w]); body (); ctx.vars <- saved
    | PropertyGet(Some(Var _), p, []) ->
      let f = ctx.layout.parameters |> Array.find (fun f -> f.name = p.Name)
      ctx.vars <- ctx.vars.Add(v, { space = Space.Y; offset = f.offset; ty = TFrac; reg = None })
      body ()
      ctx.vars <- saved
    | _ -> fail "unreachable"
  | _ ->
    let mark = ctx.next
    let score w = match ctx.scores.TryGetValue w with | true, n -> n | _ -> 0
    // a Frac local takes X1 or Y1 when it is used enough, and the locals bound inside its scope
    // that are used more still find one free
    // registers of locals no code still to run reads come back (a let's scope runs to the end
    // of its block, its life often does not); a register shared by aliases waits for all
    if tyOf v.Type = TFrac && score v >= 2 then
      let ahead = value :: bodyExpr :: ctx.conts
      let live (w: Var) = ahead |> List.exists (fun e -> e.GetFreeVars() |> Seq.contains w)
      let holders =
        [ for KeyValue(w, h) in ctx.vars do
            match h.reg with
            | Some r when not (ctx.deadLocals.Contains w) -> yield r, w
            | _ -> () ]
      for r, vars in holders |> List.groupBy fst do
        if vars |> List.forall (fun (_, w) -> not (live w)) then
          for _, w in vars do ctx.deadLocals.Add w |> ignore
          if not (List.contains r ctx.freeData) then ctx.freeData <- r :: ctx.freeData
    let register =
      if tyOf v.Type <> TFrac || score v < 2 then None
      else
        let rivals = rivalsOf v bodyExpr |> List.filter (fun w -> score w > score v) |> List.length
        match ctx.freeData with
        | r :: rest when ctx.freeData.Length > rivals ->
          ctx.freeData <- rest
          Some r
        | _ -> None
    if tyOf v.Type = TFrac && not (isNull (System.Environment.GetEnvironmentVariable "FSDSP_KERNEL_DEBUG")) then
      let holders = [ for KeyValue(w, h) in ctx.vars do if h.reg.IsSome && not (ctx.deadLocals.Contains w) then $"{w.Name}:{h.reg.Value}" ]
      eprintfn $"let {v.Name} score {score v} rivals {rivalsOf v bodyExpr |> List.filter (fun w -> score w > score v) |> List.map (fun w -> w.Name)} free {ctx.freeData} held {holders} -> {register}"
    let h =
      match register with
      | Some r -> { space = Space.X; offset = -1; ty = TFrac; reg = Some r }
      | None -> alloc ctx (tyOf v.Type)
    let afterHome = ctx.next
    store ctx h value
    ctx.next <- afterHome
    let saved = ctx.vars
    ctx.vars <- ctx.vars.Add(v, h)
    body ()
    ctx.vars <- saved
    ctx.next <- mark
    match register with
    | Some r when not (ctx.deadLocals.Remove v) -> ctx.freeData <- r :: ctx.freeData
    | _ -> ()

/// The Frac locals bound inside an expression while `v` is still read: those whose scope uses it.
and rivalsOf (v: Var) (e: Expr) =
  match e with
  | Let(w, value, body) when w.Type = typeof<Frac> ->
    (if body.GetFreeVars() |> Seq.contains v then [ w ] else []) @ rivalsOf v value @ rivalsOf v body
  | ExprShape.ShapeVar _ -> []
  | ExprShape.ShapeLambda(_, b) -> rivalsOf v b
  | ExprShape.ShapeCombination(_, xs) -> List.collect (rivalsOf v) xs

/// The Frac locals bound inside an expression.
and innerLocals (e: Expr) =
  match e with
  | Let(w, value, body) when w.Type = typeof<Frac> -> w :: innerLocals value @ innerLocals body
  | ExprShape.ShapeVar _ -> []
  | ExprShape.ShapeLambda(_, b) -> innerLocals b
  | ExprShape.ShapeCombination(_, xs) -> List.collect innerLocals xs

// ---------------------------------------------------------------------------------------------
// Statements.

and genStmt (ctx: Ctx) (e: Expr) : unit =
  let mark = ctx.next
  match e with
  | Sequential _ -> ()
  | _ -> at' ctx e
  match e with
  | Sequential(s1, s2) -> thenRun ctx s2 (fun () -> genStmt ctx s1); genStmt ctx s2
  | Let(v, value, body) -> withLet ctx v value body (fun () -> genStmt ctx body)
  | VarSet(v, value) ->
    match Map.tryFind v ctx.vars with
    | Some h -> store ctx h value
    | None -> fail $"{v.Name} is not a kernel local"
  | PropertySet(Some(Var s), p, [], value) when s.Type = ctx.kernel.stateType ->
    store ctx (stateHome (field ctx p.Name)) value
  | StateElement ctx (m, [ value ]) -> storeWord ctx value m
  | PropertySet(Some(Var p), f, _, _) when p.Type = ctx.kernel.paramsType ->
    fail $"parameter {f.Name} is read-only"
  | ForIntegerRangeLoop(v, lo, hi, body) ->
    let lo = constInt ctx "a loop bound" lo
    let hi = constInt ctx "a loop bound" hi
    let count = hi - lo + 1
    // what reads the index: state arrays (an address register each, stepping once a pass) and
    // Frac.ofRaw i (a counter word); anything else is refused
    let arrays = Collections.Generic.Dictionary<string, int * int>()   // field -> lowest, highest offset
    let mutable counted = false
    let rec scan (e: Expr) =
      match e with
      | Call(None, mi, PropertyGet(Some(Var s), p, []) :: index :: rest)
          when (mi.Name = "GetArray" || mi.Name = "SetArray") && s.Type = ctx.kernel.stateType
               && (loopOffset ctx (fun i -> i = v) index).IsSome ->
        let _, c = (loopOffset ctx (fun i -> i = v) index).Value
        match (field ctx p.Name).ty with
        | TArray n ->
          if lo + c < 0 || hi + c >= n then fail $"{p.Name}[{v.Name} + {c}] reaches outside its {n} words for {v.Name} in {lo} .. {hi}"
          let a, b = match arrays.TryGetValue p.Name with | true, r -> r | _ -> (c, c)
          arrays[p.Name] <- (min a c, max b c)
        | _ -> fail $"{p.Name} is not a [<Length n>] array"
        List.iter scan rest
      | Call(None, mi, [ Var i ]) when i = v && mi.Name = "ofRaw" && mi.DeclaringType = typeof<Frac> -> counted <- true
      | Var i when i = v ->
        fail $"the loop index {v.Name} can index state arrays (s.field[{v.Name} + c]) or be read as Frac.ofRaw {v.Name}; not {body}"
      | ExprShape.ShapeVar _ -> ()
      | ExprShape.ShapeLambda(_, b) -> scan b
      | ExprShape.ShapeCombination(_, xs) -> List.iter scan xs
    scan body
    if count > 0 then
      let savedIndexed, savedCounters = ctx.indexed, ctx.counters
      let regs =
        [ for KeyValue(name, _) in arrays do
            let reg =
              match ctx.freeRegs with
              | r :: rest -> ctx.freeRegs <- rest; r
              | [] -> fail "too many live cursors and indexed arrays at once (r1..r5)"
            let f = field ctx name
            emit ctx (move r6 a)
            emit ctx (add (immL (f.offset + lo)) a)
            emit ctx (move a1 (R reg))
            ctx.indexed <- ctx.indexed.Add((v, name), reg)
            reg ]
      let counter =
        if counted then
          let h = alloc ctx TFrac
          emitAll ctx (loadWord (WImm (lo &&& 0xffffff)) x0)
          emit ctx (move x0 (at h 0))
          ctx.counters <- ctx.counters.Add(v, h)
          Some h
        else None
      let endLabel = fresh ctx "LOOP"
      // the loop's end address is absolute: the loader relocates it
      emit ctx (Label(endLabel + "_DO"))
      emit ctx machinedrum.Abi.relocateMarker
      emit ctx (doTo (imm count) endLabel)
      let first = ctx.items.Count
      thenRun ctx body (fun () -> genStmt ctx body)     // the body runs again
      let stepped = not regs.IsEmpty || counter.IsSome
      // DO needs an instruction to end on: the body's end cannot be a branch target
      let last = if ctx.items.Count > first then Some ctx.items[ctx.items.Count - 1] else None
      match last with
      | (None | Some(Label _) | Some(Comment _)) when not stepped -> emit ctx nop
      | _ -> ()
      for reg in regs do emit ctx (update (inc (R reg)))
      match counter with
      | Some h ->
        emit ctx (move (at h 0) a)
        emit ctx (add (immL 1) a)
        emit ctx (move a1 (at h 0))
      | None -> ()
      emit ctx (Label endLabel)
      ctx.freeRegs <- regs @ ctx.freeRegs
      ctx.indexed <- savedIndexed
      ctx.counters <- savedCounters
  | IfThenElse(cond, t, f) when (match tryConst ctx cond with Some(:? bool) -> true | _ -> false) ->
    // decided when compiling: only the branch taken is generated
    if (tryConst ctx cond).Value :?> bool then genStmt ctx t else genStmt ctx f
  | IfThenElse(cond, t, f) ->
    let elseLabel = fresh ctx "ELSE"
    branchUnless ctx cond elseLabel
    genStmt ctx t
    match f with
    | Value(null, _) -> emit ctx (Label elseLabel)
    | _ ->
      let endLabel = fresh ctx "ENDIF"
      emit ctx (bra endLabel)
      emit ctx (Label elseLabel)
      genStmt ctx f
      emit ctx (Label endLabel)
  | Call(Some(Var o), mi, [x]) when o.Type = typeof<Output> && intrinsic mi = Some "out.write" ->
    storeWord ctx x (ym (inc r7))
  | Call(Some(Var o), mi, [l; r]) when o.Type = typeof<Output> && intrinsic mi = Some "out.pair" ->
    if ctx.target.channels <> 2 then fail $"{ctx.kernel.name}: WritePair needs a stereo target, {ctx.target.name} has {ctx.target.channels} channel(s)"
    storeWord ctx l (ym (inc r7))
    storeWord ctx r (ym (inc r7))
  | Call(Some(Var c), mi, [x]) when ctx.cursors.ContainsKey c && intrinsic mi = Some "cursor.put" ->
    storeWord ctx x (cursorMem ctx.cursors[c] true)
  | Call(None, mi, args) when (intrinsic (resolve mi args)).IsNone -> genStmt ctx (inlined mi args)
  | Value(null, _) -> ()
  | _ -> fail $"not a kernel statement: {e}"
  ctx.next <- mark

/// Branch to `target` when the comparison is false.
and branchUnless (ctx: Ctx) (cond: Expr) (target: string) =
  let asAcc (x: Expr) =
    if x.Type = typeof<Frac> then Expr.Call(nonNull (typeof<Acc>.GetMethod "ofFrac"), [ x ])
    elif x.Type = typeof<Acc> then x
    else fail $"comparisons are on Frac or Acc values, not {x.Type.Name}"
  match cond with
  | Call(None, mi, [l; r]) when owner mi = operators ->
    let unless =
      match mi.Name with
      | "op_LessThan" -> Cond.Ge
      | "op_LessThanOrEqual" -> Cond.Gt
      | "op_GreaterThan" -> Cond.Le
      | "op_GreaterThanOrEqual" -> Cond.Lt
      | "op_Equality" -> Cond.Ne
      | "op_Inequality" -> Cond.Eq
      | n -> fail $"conditions are comparisons, not {n}"
    binAcc ctx (asAcc l) (asAcc r) (cmp b a)
    emit ctx (bcc unless target)
  | _ -> fail $"conditions are comparisons of two values (no &&, || or not yet): {cond}"

// ---------------------------------------------------------------------------------------------
// Entry points.

type Compiled =
  { kernel   : Kernel
    target   : Target
    layout   : Layout
    items    : Item array
    lines    : int array        // per item, the F# line it came from (0: none)
    source   : string option    // the kernel's F# file, from the quotations' debug ranges
    xWorkset : int }

let entry (ctx: Ctx) (name: string) (mi: MethodInfo) =
  let def =
    match reflected mi with
    | Some d -> d
    | None -> fail $"{mi.Name}: mark its module [<ReflectedDefinition>]"
  let rec body (e: Expr) =
    match e with
    | Lambda(v, rest) ->
      if v.Type <> ctx.kernel.paramsType && v.Type <> ctx.kernel.stateType && v.Type <> typeof<Output> then
        fail $"{mi.Name}: argument {v.Name} is a {v.Type.Name}, expected the parameters, the state or the output"
      body rest
    | e -> e
  ctx.next <- 0
  ctx.line <- 0
  ctx.trackHome <- None
  let rec reads (e: Expr) =
    match e with
    | Call(None, mi, _) when intrinsic mi = Some "track.number" -> true
    | Call(None, mi, args) when (intrinsic (resolve mi args)).IsNone ->
      // a helper reads it if its body does
      List.exists reads args || (reflected mi |> Option.exists reads)
    | ExprShape.ShapeVar _ -> false
    | ExprShape.ShapeLambda(_, b) -> reads b
    | ExprShape.ShapeCombination(_, xs) -> List.exists reads xs
  let savesTrack = reads def
  match debugRange def with
  | Some(file, line) -> ctx.line <- line
  | None -> ()
  // how much each local is used, loops weighted by their trip counts
  ctx.scores.Clear()
  let rec weigh (w: int) (e: Expr) =
    match e with
    | ForIntegerRangeLoop(_, lo, hi, b) ->
      let n = match tryConst ctx lo, tryConst ctx hi with | Some(:? int as l), Some(:? int as h) -> max 1 (h - l + 1) | _ -> 1
      weigh (w * n) b
    | Var v | VarSet(v, _) when (match e with VarSet _ -> true | _ -> true) ->
      ctx.scores[v] <- (match ctx.scores.TryGetValue v with | true, n -> n | _ -> 0) + w
      match e with VarSet(_, x) -> weigh w x | _ -> ()
    // a helper's locals are the kernel's once inlined
    | Call(None, mi, args) when (intrinsic (resolve mi args)).IsNone && (reflected mi).IsSome ->
      List.iter (weigh w) args
      weigh w (reflected mi).Value
    | ExprShape.ShapeVar _ -> ()
    | ExprShape.ShapeLambda(_, b) -> weigh w b
    | ExprShape.ShapeCombination(_, xs) -> List.iter (weigh w) xs
  weigh 1 (normalize (body def))
  emit ctx (Label name)
  if savesTrack then
    // R1 holds the track number only on entry
    let h = alloc ctx TFrac
    ctx.trackHome <- Some h
    emit ctx (move r1 (at h 0))
  ctx.line <- 0
  genStmt ctx (normalize (body def))
  emit ctx rts

/// The words a RENDER body writes, when the count does not depend on the parameters: loops
/// with constant bounds multiply, branches must agree, helpers are counted through.
let rec writes (e: Expr) : int option =
  let sum (xs: Expr list) =
    xs |> List.fold (fun acc x -> match acc, writes x with Some a, Some b -> Some(a + b) | _ -> None) (Some 0)
  match e with
  | Call(Some(Var o), mi, _) when o.Type = typeof<Output> ->
    match intrinsic mi with
    | Some "out.write" -> Some 1
    | Some "out.pair" -> Some 2
    | _ -> Some 0
  | ForIntegerRangeLoop(_, Value(:? int as lo, _), Value(:? int as hi, _), body) ->
    writes body |> Option.map (fun n -> n * max 0 (hi - lo + 1))
  | ForIntegerRangeLoop(_, lo, hi, body) ->
    match sum [ lo; hi ], writes body with
    | Some 0, Some 0 -> Some 0
    | _ -> None
  | WhileLoop(c, body) -> match sum [ c; body ] with Some 0 -> Some 0 | _ -> None
  | IfThenElse(c, t, f) ->
    match writes c, writes t, writes f with
    | Some n, Some a, Some b when a = b -> Some(n + a)
    | _ -> None
  | Call(None, mi, args) when (intrinsic (resolve mi args)).IsNone && (reflected mi).IsSome ->
    sum (args @ [ (reflected mi).Value ])
  | ExprShape.ShapeVar _ -> Some 0
  | ExprShape.ShapeLambda(_, b) -> writes b
  | ExprShape.ShapeCombination(_, xs) -> sum xs

let compile (target: Target) (k: Kernel) : Result<Compiled, string> =
  try
    let ctx =
      { target = target; kernel = k; layout = layout target k; items = ResizeArray(); lines = ResizeArray(); line = 0
        vars = Map.empty; consts = Map.empty; next = 0; high = 0; labels = 0
        cursors = Map.empty; freeRegs = [ 1; 2; 3; 4; 5 ]; tables = ResizeArray(); conts = []; released = Collections.Generic.HashSet(); trackHome = None
        indexed = Map.empty; counters = Map.empty; freeData = [ X1; Y1 ]; scores = Dictionary(); deadLocals = HashSet() }
    entry ctx "INIT" k.init
    entry ctx "TRIG" k.trig
    entry ctx "RENDER" k.render
    // the block size, once the entries compile
    match reflected k.render |> Option.bind writes with
    | Some n when n <> target.blockWords ->
      fail $"{k.name}: RENDER writes {n} words, the block is {target.blockText}"
    | _ -> ()
    ctx.line <- 0
    for t in ctx.tables do
      emit ctx (Label(tableLabel t))
      for row in Array.chunkBySize 8 t.words do
        emit ctx (Dc(row |> Array.map (fun w -> Num(int64 w))))
    let source = Expr.TryGetReflectedDefinition k.render |> Option.bind debugRange |> Option.map fst
    Ok { kernel = k; target = target; layout = ctx.layout; items = ctx.items.ToArray(); lines = ctx.lines.ToArray()
         source = source; xWorkset = ctx.high }
  with KernelError m -> Error m

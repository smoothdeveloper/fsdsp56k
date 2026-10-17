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

let intrinsic (m: MemberInfo) =
  match m.GetCustomAttribute<IntrinsicAttribute>() with
  | null -> None
  | a -> Some a.Name

let isConst (m: MemberInfo) = not (isNull (m.GetCustomAttribute<ConstAttribute>()))

let foldable (t: Type) =
  t = typeof<Frac> || t = typeof<Acc> || t = typeof<Long> || t = typeof<Table>
  || t.IsPrimitive

let invoke (mi: MethodInfo) (args: obj array) : obj | null =
  try mi.Invoke(null, args)
  with :? TargetInvocationException as e -> fail $"{mi.Name}: {e.InnerException}"

// ---------------------------------------------------------------------------------------------
// Compilation state.

type Home =
  { space  : Space
    offset : int
    ty     : Ty }

type Ctx =
  { target   : Target
    kernel   : Kernel
    layout   : Layout
    items    : ResizeArray<Item>
    mutable vars   : Map<Var, Home>
    mutable consts : Map<Var, obj>
    mutable next   : int        // first free X word
    mutable high   : int        // X words used
    mutable labels : int }

let emit (ctx: Ctx) (it: Item) = ctx.items.Add it
let emitAll (ctx: Ctx) (its: Item seq) = ctx.items.AddRange its

let fresh (ctx: Ctx) what =
  ctx.labels <- ctx.labels + 1
  $"K_{what}_{ctx.labels}"

let alloc (ctx: Ctx) ty =
  let h = { space = Space.X; offset = ctx.next; ty = ty }
  ctx.next <- ctx.next + words ty
  if ctx.next > ctx.target.xLimit then fail $"{ctx.kernel.name}: locals and spills need more than {ctx.target.xLimit} X words"
  ctx.high <- max ctx.high ctx.next
  h

/// Word k of a home: (r6+offset+k) in its space.
let at (h: Home) k : Mem = { space = h.space; ea = disp r6 (h.offset + k) }

let param (f: Field) : Mem = ym (disp r6 f.offset)

let field (ctx: Ctx) (name: string) =
  ctx.layout.state |> Array.find (fun f -> f.name = name)

let stateHome (f: Field) = { space = Space.Y; offset = f.offset; ty = f.ty }

let rec tryConst (ctx: Ctx) (e: Expr) : obj option =
  match e with
  | Value(v, t) when foldable t -> someValue v
  | Var v when ctx.consts.ContainsKey v -> Some ctx.consts[v]
  | PropertyGet(None, p, []) when foldable p.PropertyType -> someValue (p.GetValue null)
  | Call(None, mi, args) ->
    let mi = resolve mi args
    let pure' = isConst mi || (match intrinsic mi with Some n -> n <> "out.write" | None -> owner mi = operators)
    if pure' && foldable mi.ReturnType then
      let values = args |> List.map (tryConst ctx)
      if values |> List.forall Option.isSome then
        someValue (invoke mi (values |> List.map Option.get |> List.toArray))
      else None
    else None
  | _ -> None

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
        | Lambda(v, rest), a :: more -> Expr.Let(v, a, bind rest more)
        | f, [] -> normalize f
        | _ -> fail $"cannot reduce the application {e}"
      bind f args
  | ExprShape.ShapeVar _ -> e
  | ExprShape.ShapeLambda(v, body) -> Expr.Lambda(v, normalize body)
  | ExprShape.ShapeCombination(shape, xs) -> ExprShape.RebuildShapeCombination(shape, xs |> List.map normalize)

/// A call to a kernel helper: its parameters bound to the arguments.
let inlined (mi: MethodInfo) (args: Expr list) =
  match Expr.TryGetReflectedDefinition mi with
  | Some def ->
    let rec bind (body: Expr) (args: Expr list) =
      match body, args with
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

let loadWord (w: Word) (r: Reg) =
  match w with
  | WMem m -> move m r
  | WImm 0 -> move (imm 0) r
  | WImm v -> move (immL v) r

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
  | AOfWord w -> [| loadWord w acc |]
  | AOfLong(hi, l) -> [| move hi acc; move l lo |]

let storeA (h: Home) =
  match h.ty with
  | TAcc -> [| move a1 (at h 0); move a0 (at h 1); move a2 (at h 2) |]
  | TLong -> [| move a1 (at h 0); move a0 (at h 1) |]
  | TFrac -> fail "a Frac home takes a word, not an accumulator"
  | TOther t -> fail $"{t.Name} has no home"

let callName (ctx: Ctx) (e: Expr) =
  match e with
  | Call(_, mi, args) -> intrinsic (resolve mi args)
  | _ -> None

let rec wordLeaf (ctx: Ctx) (e: Expr) : Word option =
  match tryConst ctx e with
  | Some(:? Frac as f) -> Some(WImm f.Word)
  | _ ->
    match e with
    | Var v when ctx.vars.ContainsKey v && ctx.vars[v].ty = TFrac -> Some(WMem(at ctx.vars[v] 0))
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
    | Call(None, mi, [x]) ->
      match intrinsic (resolve mi [x]) with
      | Some "acc.ofFrac" -> wordLeaf ctx x |> Option.map AOfWord
      | Some "acc.ofLong" -> longLeaf ctx x
      | _ -> None
    | _ -> None

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
    | Let(v, value, body) -> withLet ctx v value (fun () -> genAcc ctx body)
    | Sequential(s, rest) -> genStmt ctx s; genAcc ctx rest
    | Call(None, mi, args) ->
      let mi = resolve mi args
      match intrinsic mi, args with
      | Some "acc.ofFrac", [f] -> genWord ctx f; emit ctx (move x0 a)
      | Some "acc.ofLong", [l] -> genLong ctx l
      | Some "acc.add", [l; r] -> binAcc ctx l r (add b a)
      | Some "acc.sub", [l; r] -> binAcc ctx l r (sub b a)
      | Some "acc.addFrac", [l; f] -> binAccWord ctx l f (add x0 a)
      | Some "acc.subFrac", [l; f] -> binAccWord ctx l f (sub x0 a)
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
      | Some "frac.mul", [x; y] ->
        let xs = operands ctx [ x; y ]
        emit ctx (loadWord xs[0] x0)
        emit ctx (loadWord xs[1] y0)
        emit ctx (mpy x0 y0 a)
      | Some "acc.mac", [acc; x; y] ->
        let xs = operands ctx [ x; y ]
        genAcc ctx acc
        emit ctx (loadWord xs[0] x0)
        emit ctx (loadWord xs[1] y0)
        emit ctx (mac x0 y0 a)
      | Some n, _ -> fail $"{n} does not give an Acc"
      | None, _ -> genAcc ctx (inlined mi args)
    | _ -> fail $"not a kernel Acc expression: {e}"

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
and binAccWord (ctx: Ctx) (l: Expr) (f: Expr) (op: Item) =
  let w = (operands ctx [ f ])[0]
  genAcc ctx l
  emit ctx (loadWord w x0)
  emit ctx op

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
    | Let(v, value, body) -> withLet ctx v value (fun () -> genLong ctx body)
    | Call(None, mi, args) when (intrinsic (resolve mi args)).IsNone -> genLong ctx (inlined mi args)
    | _ -> fail $"not a kernel Long expression: {e}"

/// A Frac expression into X0.
and genWord (ctx: Ctx) (e: Expr) : unit =
  match wordLeaf ctx e with
  | Some w -> emit ctx (loadWord w x0)
  | None ->
    match e with
    | Let(v, value, body) -> withLet ctx v value (fun () -> genWord ctx body)
    | Sequential(s, rest) -> genStmt ctx s; genWord ctx rest
    | Call(None, mi, args) ->
      let mi = resolve mi args
      match intrinsic mi, args with
      | Some "acc.hi", [x] -> genAcc ctx x; emit ctx (move a1 x0)
      | Some "acc.lo", [x] -> genAcc ctx x; emit ctx (move a0 x0)
      | Some "acc.sat", [x] -> genAcc ctx x; emit ctx (move a x0)
      | Some "table.read", [t; i] -> tableRead ctx t i x0
      | Some n, _ -> fail $"{n} does not give a Frac"
      | None, _ -> genWord ctx (inlined mi args)
    | _ -> fail $"not a kernel Frac expression: {e}"

/// `move x:(r0+table),dst`, the table's address patched by the loader.
and tableRead (ctx: Ctx) (t: Expr) (index: Expr) (dst: Reg) =
  let table =
    match tryConst ctx t with
    | Some(:? Table as t) -> t
    | _ -> fail $"a table must be a constant, got {t}"
  match index with
  | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "acc.hi" ->
    genAcc ctx x
    emit ctx (move a1 r0)
  | _ ->
    match wordLeaf ctx index with
    | Some w -> emit ctx (loadWord w r0)
    | None ->
      genWord ctx index
      emit ctx (move x0 r0)
  match table.source with
  | Resource symbol ->
    let name = fresh ctx ("TABLE_" + table.name.ToUpperInvariant())
    emit ctx (Label name)
    emit ctx (machinedrum.Abi.importMarker (machinedrum.Mds.importKind symbol) symbol)
    emit ctx (move (xm (dispL r0 0)) dst)

/// The value of e into a home.
and store (ctx: Ctx) (h: Home) (e: Expr) =
  match h.ty with
  | TFrac -> storeWord ctx e (at h 0)
  | TAcc -> genAcc ctx e; emitAll ctx (storeA h)
  | TLong ->
    match e with
    | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "long.ofAcc" -> genAcc ctx x
    | _ -> genLong ctx e
    emitAll ctx (storeA h)
  | TOther t -> fail $"{t.Name} values cannot be stored"

/// A Frac into a memory word: an accumulator stored directly when it is one.
and storeWord (ctx: Ctx) (e: Expr) (dst: Mem) =
  match e with
  | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "acc.sat" -> genAcc ctx x; emit ctx (move a dst)
  | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "acc.hi" -> genAcc ctx x; emit ctx (move a1 dst)
  | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "acc.lo" -> genAcc ctx x; emit ctx (move a0 dst)
  | _ ->
    genWord ctx e
    emit ctx (move x0 dst)

and withLet (ctx: Ctx) (v: Var) (value: Expr) (body: unit -> unit) =
  match tryConst ctx value with
  | Some c when not v.IsMutable ->
    ctx.consts <- ctx.consts.Add(v, c)
    body ()
  | _ when substitutable ctx v value ->
    let saved = ctx.vars
    match value with
    | Var w -> ctx.vars <- ctx.vars.Add(v, ctx.vars[w]); body (); ctx.vars <- saved
    | PropertyGet(Some(Var _), p, []) ->
      let f = ctx.layout.parameters |> Array.find (fun f -> f.name = p.Name)
      ctx.vars <- ctx.vars.Add(v, { space = Space.Y; offset = f.offset; ty = TFrac })
      body ()
      ctx.vars <- saved
    | _ -> fail "unreachable"
  | _ ->
    let mark = ctx.next
    let h = alloc ctx (tyOf v.Type)
    let afterHome = ctx.next
    store ctx h value
    ctx.next <- afterHome
    let saved = ctx.vars
    ctx.vars <- ctx.vars.Add(v, h)
    body ()
    ctx.vars <- saved
    ctx.next <- mark

// ---------------------------------------------------------------------------------------------
// Statements.

and genStmt (ctx: Ctx) (e: Expr) : unit =
  let mark = ctx.next
  match e with
  | Sequential(s1, s2) -> genStmt ctx s1; genStmt ctx s2
  | Let(v, value, body) -> withLet ctx v value (fun () -> genStmt ctx body)
  | VarSet(v, value) ->
    match Map.tryFind v ctx.vars with
    | Some h -> store ctx h value
    | None -> fail $"{v.Name} is not a kernel local"
  | PropertySet(Some(Var s), p, [], value) when s.Type = ctx.kernel.stateType ->
    store ctx (stateHome (field ctx p.Name)) value
  | PropertySet(Some(Var p), f, _, _) when p.Type = ctx.kernel.paramsType ->
    fail $"parameter {f.Name} is read-only"
  | ForIntegerRangeLoop(v, lo, hi, body) ->
    let count = constInt ctx "a loop bound" hi - constInt ctx "a loop bound" lo + 1
    if body.GetFreeVars() |> Seq.contains v then fail $"the loop index {v.Name} cannot be read (DO keeps no index)"
    if count > 0 then
      let endLabel = fresh ctx "LOOP"
      // the loop's end address is absolute: the loader relocates it
      emit ctx (Label(endLabel + "_DO"))
      emit ctx machinedrum.Abi.relocateMarker
      emit ctx (doTo (imm count) endLabel)
      let first = ctx.items.Count
      genStmt ctx body
      // DO needs an instruction to end on: the body's end cannot be a branch target
      let last = if ctx.items.Count > first then Some ctx.items[ctx.items.Count - 1] else None
      match last with
      | None | Some(Label _) | Some(Comment _) -> emit ctx nop
      | _ -> ()
      emit ctx (Label endLabel)
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
    xWorkset : int }

let entry (ctx: Ctx) (name: string) (mi: MethodInfo) =
  let def =
    match Expr.TryGetReflectedDefinition mi with
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
  emit ctx (Label name)
  genStmt ctx (normalize (body def))
  emit ctx rts

let compile (target: Target) (k: Kernel) : Result<Compiled, string> =
  try
    let ctx =
      { target = target; kernel = k; layout = layout target k; items = ResizeArray()
        vars = Map.empty; consts = Map.empty; next = 0; high = 0; labels = 0 }
    entry ctx "INIT" k.init
    entry ctx "TRIG" k.trig
    entry ctx "RENDER" k.render
    Ok { kernel = k; target = target; layout = ctx.layout; items = ctx.items.ToArray(); xWorkset = ctx.high }
  with KernelError m -> Error m

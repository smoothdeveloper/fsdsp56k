/// Kernel F# to DSL items, by direct translation: every expression is evaluated into A (an
/// accumulator value) or X0 (a word), operands go through B, X0 and Y0, and anything that does
/// not fit is spilled to X:(r6+n), after the [<XMemory>] state words (and, once the X workset is
/// full, to the Y words after the state). Across statements only locals stay in registers: Frac locals
/// in X1/Y1, one Acc local read in a loop or a branch in B (when nothing else in its scope needs
/// B). A loop splits in two on a condition it does not change. No move is packed into a parallel
/// slot here: Optimize does that.
///
/// What a kernel may contain: let / let mutable / <- on locals and state fields; `for _ in a .. b`
/// with constant bounds (DO); `if` on comparisons of Frac or Acc values (CMP, Bcc); intrinsic
/// calls (Intrinsics.fs); calls to [<ReflectedDefinition>] functions, inlined; constants, folded
/// by running the .NET side of the intrinsics.
module dsp56k.kernel.Compile

open System
open dsp56k
open dsp56k.dsl
open dsp56k.dsl.Asm
open dsp56k.kernel.Ir
open dsp56k.kernel.Ir.Patterns
open dsp56k.kernel.Kernel
open System.Collections.Generic

type Acc = dsp56k.kernel.Acc

let owner (m: Member) = m.Owner

let someValue (v: obj | null) : obj option = match v with null -> None | v -> Some v

// ---------------------------------------------------------------------------------------------
// Members: intrinsic names, constant evaluation.

let operators = "Microsoft.FSharp.Core.Operators"

/// `a + b` on kernel types is Operators.op_Addition<Acc, Acc, Acc>: the type's own operator.
let resolve (m: Member) (args: Expr list) =
  if owner m = operators && m.Name.StartsWith "op_" then
    let types = args |> List.map _.Type
    types
    |> List.tryPick (fun t ->
      match Ir.typeOperators.TryGetValue((t, m.Name, types)) with
      | true, found -> Some found
      | _ -> None)
    |> Option.defaultValue m
  else m

/// A call of one of F#'s generic operators, which `resolve` turns into the kernel type's own.
let operatorCall (name: string) (returns: IrTy) (args: Expr list) = Expr.Call(Ir.fsharpOperator name returns, args)

/// A helper's body, read once (the member keeps it): inlining and the use counts must see the same variables.
let reflected (m: Member) = m.Definition.Value

let intrinsic (m: Member) = m.Intrinsic

let isConst (m: Member) = m.IsConst

let foldable (t: IrTy) = IrTy.foldable t

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

let invoke (mi: Member) (args: obj array) : obj | null =
  match (if owner mi = operators then operator mi.Name args else None) with
  | Some v -> v
  | None -> mi.Invoke args

// ---------------------------------------------------------------------------------------------
// Compilation state.

type Home =
  { space  : Space
    offset : int
    ty     : Ty
    reg    : Reg option     // a Frac local kept in X1 or Y1 instead of memory, an Acc local in B
    bound  : float }        // an Acc local's largest magnitude (< 1: a1:a0 hold it, sign-extended)

/// An F# local as the code keeps it, for a debugger: where it lives (or the constant it folded to) and the lines of the
/// entry's own file its scope covers. A home's offset is from r6, in its space; a register home has `reg`.
type Binding =
  { name     : string
    ty       : string
    home     : Home option
    constant : string option
    line     : int
    first    : int
    last     : int }

type Ctx =
  { target   : Target
    kernel   : IrKernel
    layout   : Layout
    items    : ResizeArray<Item>
    lines    : ResizeArray<int>     // the F# line each item came from (0: none)
    mutable line   : int        // the latest debug range's line, in any file (source comments)
    mutable sourceLine : int    // the latest line in the entry's own file: what `lines` records
    mutable sourceFile : string option   // the entry's file (its definition's debug range)
    mutable vars   : Map<Var, Home>
    mutable consts : Map<Var, obj>
    mutable next   : int        // first free local word: X+0 .. are the [<XMemory>] state, words
                                // from xLimit on are Y words after the Y state (alloc)
    mutable high   : int        // local words used, the X state's included
    mutable labels : int
    mutable cursors : Map<Var, CursorReg>
    mutable freeRegs : int list       // address registers for cursors (r0 is the table index)
    mutable conts : Expr list         // code still to run after the current expression (loop bodies again)
    released : Collections.Generic.HashSet<Var>   // cursors whose register was freed before their scope ended
    mutable trackHome : Home option   // where the entry saved R1, the track number
    mutable indexed : Map<Var * string, int * int>  // (loop index, state array) -> its address register,
                                                    // the array's distance from what that register steps
    mutable counters : Map<Var, Home>         // loop index -> the word counting it, when read
    mutable inductions : Map<Var * string, int> // (a local stepped by a constant, a region) -> the
                                              // address register at region + local, in a DO loop
    affineAt : Dictionary<Expr, Mem>          // region read/write calls (by reference) affine in a
                                              // DO loop's index -> the word through its register
    taps : Dictionary<Var, Home>              // lets of a ring tap group, read already into their homes
    mutable freeData : Reg list               // X1, Y1 when no local holds them
    scores   : Dictionary<Var, int>           // uses of each local, weighted by loop trip counts
    deadLocals : HashSet<Var>                 // register locals given back before their scope ended
    tables   : ResizeArray<Table>     // program tables, emitted after the code
    unswitched : HashSet<Var>         // loops (by index) already split on a condition
    mutable bHeld  : bool             // an Acc local lives in B: nothing else may write it
    mutable bWrite : bool             // ...except the local's own updates
    subs     : Dictionary<Member, Sub>   // [<Subroutine>] helpers called, compiled after the entries
    chosen   : Set<string>               // helpers (by key) made subroutines by Optimize's choice
    floors   : Map<string, int>          // the least first word of a subroutine's (Refloor)
    kept     : HashSet<Var>                  // loops not unswitched: in [<NoUnswitch>] functions
    unrolled : HashSet<Var>                  // loops written out: in [<Unroll>] functions
    mutable pending : Expr list              // code being generated that conts may not hold (a
                                             // subroutine's body, a loop count, a cursor's start):
                                             // what a call's frame must not overwrite either
    unswitchMax : int option                 // unswitch a loop only when its copy takes at most these words
    unswitchCosts : ResizeArray<int>         // the words each unswitched loop's copy took
    bindings : ResizeArray<Binding>          // the locals and where they live, for the step view
    handOff  : Set<string>                   // hand-written helpers (by key) compiled from their F# instead
    mutable inSub  : Sub option       // the subroutine being compiled
    mutable depth  : int              // system stack levels taken here: two a DO loop
    mutable deepest : int }           // the most, in the entry or subroutine being compiled

/// A [<Subroutine>] helper: where its calls are, what they leave it.
and Sub =
  { label    : string
    mi       : Member
    records  : Dictionary<IrTy, Var>              // the records (and output) its callers pass
    sites    : ResizeArray<CallSite>
    mutable compiled : bool
    mutable start    : int            // its first word, once compiled
    mutable deepest  : int }          // stack levels its own DO loops take

/// A call: the caller's first free local word (the subroutine's words start above every call's),
/// its free address registers (the subroutine may take those free at every call), the stack
/// levels taken there, and the subroutine making it (None: an entry).
and CallSite =
  { next     : int
    freeRegs : int list
    depth    : int
    caller   : Sub option }

/// A cursor's address register.
and CursorReg =
  { reg    : int
    region : Region       // a state array's: its name and bank
    step   : int
    ring   : bool
    state  : int option } // over a state array: the array's offset from r6

/// An Acc local lives in B for its scope only if nothing else in the scope writes B: the scope
/// is compiled with the local in B and compiled again with it in memory when something does.
exception BTaken

let writesB (it: Item) =
  let isB (r: Reg) = Array.contains B (Lower.storage r)
  match it with
  | Op(Par(alu, moves)) ->
    (alu |> Option.exists (fun a -> Lower.aluWrites a |> Array.exists isB))
    || (moves |> Array.exists (fun m -> Lower.moveWrites m |> Array.exists isB))
  | Op(IfCc(alu, _, _)) -> Lower.aluWrites alu |> Array.exists isB
  | Op(AluImm(_, _, d)) | Op(ShiftImm(_, _, _, d)) | Op(ShiftReg(_, _, _, d)) | Op(MulImm(_, _, _, _, d))
  | Op(MulSu(_, _, _, _, _, d)) | Op(MulI(_, _, _, _, d)) | Op(Div(_, d)) | Op(Norm(_, d)) | Op(Inc d) | Op(Dec d)
  | Op(Clb(_, d)) | Op(Normf(_, d)) | Op(Merge(_, d)) | Op(Extract(_, _, _, d)) | Op(Insert(_, _, d))
  | Op(Dmac(_, _, _, _, d)) -> d = dsp56k.Acc.B
  | Op(Tcc(_, Some(_, d), _)) -> d = dsp56k.Acc.B
  | _ -> false

let emit (ctx: Ctx) (it: Item) =
  if ctx.bHeld && not ctx.bWrite && writesB it then raise BTaken
  ctx.items.Add it
  ctx.lines.Add ctx.sourceLine

let emitAll (ctx: Ctx) (its: Item seq) = for it in its do emit ctx it

/// The source range quotations carry when compiled with --quotations-debug+.
let debugRange (e: Expr) = e.Range |> Option.map (fun r -> r.file, r.line)

let sourceLines = Collections.Concurrent.ConcurrentDictionary<string, string array>()

/// Whether statements are introduced by a comment quoting their F# line (with debug ranges).
/// Off, the code is the same as without debug ranges (comments can stop a peephole pattern or
/// end a loop body), and each item still records its line in Compiled.lines.
let mutable sourceComments = true

/// Statements are introduced by a comment quoting their F# line, when it is known (and
/// sourceComments is on). Items record the line of the entry's own file: code inlined from a
/// helper in another file keeps the line of its call.
let at' (ctx: Ctx) (e: Expr) =
  match debugRange e with
  | Some(file, line) ->
    if ctx.sourceFile |> Option.forall ((=) file) then ctx.sourceLine <- line
    if line <> ctx.line then
      ctx.line <- line
      let text =
        if not sourceComments then ""
        else
          let lines = sourceLines.GetOrAdd(file, fun f -> if IO.File.Exists f then IO.File.ReadAllLines f else [||])
          if line >= 1 && line <= lines.Length then lines[line - 1].Trim() else ""
      if text <> "" then emit ctx (Comment $"{IO.Path.GetFileName file}:{line}  {text}")
  | None -> ()

/// Code generated for `e` is attributed to e's own line (an item records the line of the entry's own file) and to the
/// enclosing line again afterwards: an inlined helper's body takes its own lines in Compiled.lines, not those of its call.
/// No comment is emitted, so the code is the same as without it.
let lined (ctx: Ctx) (e: Expr) (gen: unit -> unit) =
  match debugRange e with
  | Some(file, line) when ctx.sourceFile |> Option.forall ((=) file) ->
    let saved = ctx.sourceLine
    ctx.sourceLine <- line
    try gen () finally ctx.sourceLine <- saved
  | _ -> gen ()

let fresh (ctx: Ctx) what =
  ctx.labels <- ctx.labels + 1
  $"K_{what}_{ctx.labels}"

/// `body` run in the scope of the local `v`, which lives at `home` (None: folded to `constant`): recorded for the
/// step view with the lines its code took. The compiler's own temporaries (`_arg1`, `matchValue`, names with @) are left out.
let bound (ctx: Ctx) (v: Var) (home: Home option) (constant: obj option) (body: unit -> unit) =
  let start = ctx.lines.Count
  let line = ctx.sourceLine
  body ()
  let own = not (v.Name.StartsWith "_" || v.Name.Contains "@" || v.Name = "matchValue" || v.Name.StartsWith "patternInput")
  if own then
    let lines = [ for i in start .. ctx.lines.Count - 1 do if ctx.lines[i] > 0 then ctx.lines[i] ]
    let lines = if line > 0 then line :: lines else lines
    ctx.bindings.Add
      { name     = v.Name
        ty       = IrTy.name v.Type
        home     = home
        constant = constant |> Option.map (fun c -> match c with null -> "null" | c -> sprintf "%A" c)
        line     = line
        first    = (if lines.IsEmpty then 0 else List.min lines)
        last     = (if lines.IsEmpty then 0 else List.max lines) }

/// A local's words. Locals are a stack of words in X after the [<XMemory>] state; past the X
/// workset (a kernel whose X state leaves too little), they go on in Y after the Y state, the
/// words from xLimit on standing for Y:(r6+yWorkset) on. A home does not straddle the two.
let alloc (ctx: Ctx) ty =
  let n = words ty
  let x = ctx.target.xLimit
  if ctx.next < x && ctx.next + n > x then ctx.next <- x
  let h =
    if ctx.next < x then { space = Space.X; offset = ctx.next; ty = ty; reg = None; bound = infinity }
    else { space = Space.Y; offset = ctx.layout.yWorkset + ctx.next - x; ty = ty; reg = None; bound = infinity }
  ctx.next <- ctx.next + n
  if ctx.next > x && ctx.layout.yWorkset + ctx.next - x > ctx.target.yLimit then
    let state = if ctx.layout.xWorkset = 0 then "" else $"the [<XMemory>] state ({ctx.layout.xWorkset} words), "
    fail $"{ctx.kernel.name}: {state}locals and spills need more than {x} X words and the Y words after the state (Y+{ctx.layout.yWorkset} to Y+{ctx.target.yLimit - 1})"
  ctx.high <- max ctx.high ctx.next
  h

/// Word k of a home: (r6+offset+k) in its space.
let at (h: Home) k : Mem = { space = h.space; ea = disp r6 (h.offset + k) }

let param (f: Field) : Mem = ym (disp r6 f.offset)

let field (ctx: Ctx) (name: string) = ctx.layout.field name

/// The memory space of a field's bank.
let spaceOf (f: Field) = match f.bank with XMemory -> Space.X | YMemory -> Space.Y

let stateHome (f: Field) = { space = spaceOf f; offset = f.offset; ty = f.ty; reg = None; bound = infinity }

/// Intrinsics that read firmware memory or have effects: never evaluated at compile time (a
/// resource's .NET words are a stand-in for the firmware's).
let effects =
  set [ "out.write"; "out.pair"; "table.read"; "table.row"; "region.address"; "cell.read"
        "cursor.next"; "cursor.peek"; "cursor.put"; "cursor.linear"; "cursor.ring"; "cursor.modulo"
        "cursor.position"; "track.number"; "region.read"; "region.write"; "cursor.up"; "cursor.down"
        "cursor.putUp"; "cursor.putDown"; "cursor.linearIn"; "cursor.ringIn" ]

/// A helper the compiler inlines whose body only computes: no state written, no output, nothing
/// but pure calls (it can run when the kernel is compiled, on constant arguments).
let rec pureHelper (depth: int) (mi: Member) =
  depth < 8
  && (match reflected mi with
      | Some body ->
        let rec pureExpr (e: Expr) =
          match e with
          | PropertySet _ -> false
          | Call(_, m, args) ->
            let pureCall =
              isConst m
              || (match intrinsic m with Some n -> not (Set.contains n effects) | None -> owner m = operators)
              || (not m.IsGeneric && pureHelper (depth + 1) m)
            pureCall && List.forall pureExpr args
          | ExprShape.ShapeVar _ -> true
          | ExprShape.ShapeLambda(_, b) -> pureExpr b
          | ExprShape.ShapeCombination(_, xs) -> List.forall pureExpr xs
        pureExpr body
      | None -> false)

let rec tryConst (ctx: Ctx) (e: Expr) : obj option =
  match e with
  | Value(v, t) when foldable t -> someValue v
  | Var v when ctx.consts.ContainsKey v -> Some ctx.consts[v]
  | PropertyGet(None, p, []) when foldable p.ReturnType -> someValue (p.Get null)
  | PropertyGet(Some o, p, []) when foldable p.ReturnType ->
    constObject ctx o |> Option.bind (fun x -> someValue (p.Get x))
  | IfThenElse(c, t, f) ->
    match tryConst ctx c with
    | Some(:? bool as b) -> tryConst ctx (if b then t else f)
    | _ -> None
  // `match voice.layer with Metal t -> ...` on a constant: decided when compiling
  | UnionCaseTest(o, case) ->
    constObject ctx o |> Option.map (fun x -> case.Test x :> obj)
  | Call(None, mi, args) ->
    let mi = resolve mi args
    let pure' = isConst mi || (match intrinsic mi with Some n -> not (Set.contains n effects) | None -> owner mi = operators)
    // an inlined helper on constants (`at 415`): folded too
    let pure' = pure' || (not args.IsEmpty && foldable mi.ReturnType && pureHelper 0 mi)
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
  | PropertyGet(None, p, []) -> someValue (p.Get null)
  | PropertyGet(Some o, p, []) -> constObject ctx o |> Option.bind (fun x -> someValue (p.Get x))
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
  // `let b = condition`: a word, Frac.ofBool, its uses tests of the word (a constant condition
  // stays a constant, the tests folded with it)
  | Let(v, value, body) when v.Type = TyBool && not v.IsMutable ->
    let w = Var(v.Name, TyFrac)
    let test = Expr.Call(Ir.fsharpOperator "op_Inequality" TyBool, [ Expr.Var w; Ir.zero TyFrac ])
    Expr.Let(w, Expr.Call(Ir.intrinsic "frac.ofBool", [ normalize value ]),
             normalize (body.Substitute(fun x -> if x = v then Some test else None)))
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

/// Whether `e` (the helpers it calls included) may assign a record field named `name`.
let writesField (name: string) (e: Expr) =
  let seen = HashSet<Member>()
  let rec go (e: Expr) =
    match e with
    | PropertySet(_, p, _, _) when p.Name = name -> true
    | Call(None, mi, args) when (reflected mi).IsSome && intrinsic mi = None ->
      List.exists go args || (seen.Add mi && go (reflected mi).Value)
    | ExprShape.ShapeVar _ -> false
    | ExprShape.ShapeLambda(_, b) -> go b
    | ExprShape.ShapeCombination(_, xs) -> List.exists go xs
  go e

/// What evaluating an expression reads and what it changes, as keys: "F:name" a record field or
/// state array, "M" region memory (what cursors and Region.read/write reach), "C:v" cursor v's
/// position ("C:*" any cursor's, in a helper), "O" the output.
type Touches = { reads : Set<string>; writes : Set<string> }

let noTouch = { reads = Set.empty; writes = Set.empty }
let both (a: Touches) (b: Touches) = { reads = a.reads + b.reads; writes = a.writes + b.writes }

/// Helpers' touches, their parameters and locals as any cursor, read once.
let helperTouches = Collections.Concurrent.ConcurrentDictionary<Member, Touches>()

let rec touches (inHelper: bool) (e: Expr) : Touches =
  let all xs = xs |> List.map (touches inHelper) |> List.fold both noTouch
  let cursor (c: Var) = if inHelper then "C:*" else "C:" + c.Name
  match e with
  | PropertySet(Some o, p, _, x) -> both (all [ o; x ]) { reads = Set.empty; writes = set [ "F:" + p.Name ] }
  | PropertyGet(Some(Var _), p, []) when p.CanWrite -> { reads = set [ "F:" + p.Name ]; writes = Set.empty }
  | Call(None, mi, (PropertyGet(_, p, []) :: rest)) when mi.Name = "SetArray" ->
    both (all rest) { reads = Set.empty; writes = set [ "F:" + p.Name ] }
  | Call(None, mi, (PropertyGet(_, p, []) :: rest)) when mi.Name = "GetArray" ->
    both (all rest) { reads = set [ "F:" + p.Name ]; writes = Set.empty }
  | Call(Some(Var c), mi, args) when c.Type = TyCursor ->
    let k = cursor c
    let own =
      match intrinsic mi with
      | Some "cursor.peek" -> { reads = set [ k; "M" ]; writes = Set.empty }
      | Some "cursor.position" -> { reads = set [ k ]; writes = Set.empty }
      | Some("cursor.put" | "cursor.putUp" | "cursor.putDown") -> { reads = set [ k ]; writes = set [ k; "M" ] }
      | _ -> { reads = set [ k; "M" ]; writes = set [ k ] }
    both (all args) own
  | Call(Some o, _, args) when o.Type = TyOutput -> both (all args) { reads = set [ "O" ]; writes = set [ "O" ] }
  | Call(None, mi, args) ->
    match intrinsic (resolve mi args) with
    | Some "region.write" -> both (all args) { reads = Set.empty; writes = set [ "M" ] }
    | Some "region.read" -> both (all args) { reads = set [ "M" ]; writes = Set.empty }
    | Some _ -> all args
    | None ->
      match reflected mi with
      | Some def -> both (all args) (helperTouches.GetOrAdd(mi, fun _ -> touches true def))
      | None -> all args
  | ExprShape.ShapeVar _ -> noTouch
  | ExprShape.ShapeLambda(_, b) -> touches inHelper b
  | ExprShape.ShapeCombination(_, xs) -> all xs

/// Whether evaluating `later` after `earlier` can give another result than the other way round.
let conflict (earlier: Touches) (later: Touches) =
  let meets (a: Set<string>) (b: Set<string>) =
    not (Set.intersect a b).IsEmpty
    || (a.Contains "C:*" && b |> Set.exists (fun k -> k.StartsWith "C:"))
    || (b.Contains "C:*" && a |> Set.exists (fun k -> k.StartsWith "C:"))
  meets earlier.writes (later.reads + later.writes) || meets earlier.reads later.writes

/// The expression with the arguments of each call evaluated in .NET's order, left to right,
/// where it matters: an argument whose evaluation conflicts with a later one's (one changes
/// what the other reads or changes) is bound to a let first, in order (the code generator
/// otherwise places operands as the instructions want them, products before what they are
/// added to). An expression with nothing to order is returned as it is, the same object.
let rec ordered (e: Expr) : Expr =
  let dsp (t: IrTy) = t = TyFrac || t = TyAcc || t = TyLong
  match e with
  | Call(o, mi, args) ->
    let o' = o |> Option.map ordered
    let args' = args |> List.map ordered
    let ts = args' |> List.map (touches false) |> List.toArray
    let first =
      set [ for i in 0 .. ts.Length - 1 do
              if dsp args'[i].Type && Seq.exists (fun j -> conflict ts[i] ts[j]) { i + 1 .. ts.Length - 1 } then yield i ]
    if not first.IsEmpty then
      let bound = args' |> List.mapi (fun i a -> if first.Contains i then Some(Var($"arg{i}", a.Type), a) else None)
      let callArgs = List.map2 (fun b (a: Expr) -> match b with Some(v, _) -> Expr.Var v | None -> a) bound args'
      let call = match o' with Some o' -> Expr.Call(o', mi, callArgs) | None -> Expr.Call(mi, callArgs)
      List.foldBack (fun b body -> match b with Some(v, a) -> Expr.Let(v, a, body) | None -> body) bound call
    else
      let same = match o, o' with Some x, Some y -> obj.ReferenceEquals(x, y) | _ -> true
      if same && List.forall2 (fun (x: Expr) y -> obj.ReferenceEquals(x, y)) args args' then e
      else match o' with Some o' -> Expr.Call(o', mi, args') | None -> Expr.Call(mi, args')
  | ExprShape.ShapeVar _ -> e
  | ExprShape.ShapeLambda(v, b) ->
    let b' = ordered b
    if obj.ReferenceEquals(b, b') then e else Expr.Lambda(v, b')
  | ExprShape.ShapeCombination(shape, xs) ->
    let xs' = List.map ordered xs
    if List.forall2 (fun (x: Expr) y -> obj.ReferenceEquals(x, y)) xs xs' then e
    else ExprShape.RebuildShapeCombination(shape, xs')

/// An int computed from variables and constants by + and -.
let rec intOfVars (e: Expr) =
  match e with
  | Var _ | Value(_, _) -> true
  | Call(None, mi, [ x; y ]) when (mi.Name = "op_Addition" || mi.Name = "op_Subtraction") && owner mi = operators ->
    intOfVars x && intOfVars y
  | _ -> false

/// A call to a kernel helper: its parameters bound to the arguments.
let inlined (mi: Member) (args: Expr list) =
  match reflected mi with
  | Some def ->
    let rec bind (body: Expr) (args: Expr list) =
      match body, args with
      // a variable argument is substituted (the body cannot assign the caller's variable), and
      // so is a record field the body does not assign (read where it is used, as the caller's
      // own reads are); anything else is bound, evaluated once
      | Lambda(v, rest), (Var _ as a) :: more -> (bind rest more : Expr).Substitute(fun x -> if x = v then Some a else None)
      // and an int of variables and constants (`i - 1`, a loop index): substituted, so the
      // body's s.field[k + 1] is the caller's s.field[i - 1 + 1]
      | Lambda(v, rest), a :: more when a.Type = TyInt && intOfVars a ->
        (bind rest more : Expr).Substitute(fun x -> if x = v then Some a else None)
      | Lambda(v, rest), (PropertyGet(Some(Var _), p, []) as a) :: more when not (writesField p.Name rest) ->
        (bind rest more : Expr).Substitute(fun x -> if x = v then Some a else None)
      | Lambda(v, rest), a :: more -> Expr.Let(v, a, bind rest more)
      // `let f () = ...` is a method of no parameters called with none: its unit argument
      // stands for nothing
      | Lambda(v, rest), [] when v.Type = TyUnit && mi.ParameterCount = 0 -> rest
      | body, [] -> body
      | _ -> fail $"{mi.Name}: partial application is not supported"
    ordered (normalize (bind def args))
  | None -> fail $"{owner mi}.{mi.Name} is neither an intrinsic nor a [<ReflectedDefinition>] function"

/// A helper marked [<Subroutine>]: called, not inlined.
let isSubroutine (mi: Member) =
  mi.Marks.Contains "Subroutine" && (reflected mi).IsSome

/// The routine a [<HandWritten>] helper runs here, unless its F# reference is compiled instead
/// (`handOff`, for measuring one against the other).
let handOf (ctx: Ctx) (mi: Member) =
  if mi.Marks.Contains "Hand" && not (ctx.handOff.Contains mi.Key) && (reflected mi).IsSome then Hand.find mi.Key
  else None

/// A helper called as a subroutine here: marked [<Subroutine>], or chosen (Optimize) and not
/// marked [<Inline>]; or written by hand, its routine put at the call under the same convention.
let isSub (ctx: Ctx) (mi: Member) =
  ((isSubroutine mi || (ctx.chosen.Contains mi.Key && not (mi.Marks.Contains "Inline"))) && (reflected mi).IsSome)
  || (handOf ctx mi).IsSome

/// A helper's parameters and body.
let parametersOf (def: Expr) =
  let rec go (e: Expr) acc =
    match e with
    | Lambda(v, body) -> go body (v :: acc)
    | body -> List.rev acc, body
  go def []

/// The system stack levels a machine may take (the DSP56300 has 16; the Machinedrum's firmware
/// and interrupts keep the others).
let stackLevels = 12

/// A subroutine's name across compilations.
let subKey (mi: Member) = mi.Key

/// The registers a subroutine's arguments arrive in.
let fracArgs = [ X0; Y0; X1; Y1 ]
let accArgs = [ A; B ]

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
  // a square: one register, both inputs
  | WReg r, WReg r' when r = r' && (r = X0 || r = Y0) -> [], (r, r)
  | _ when w1 = w2 -> loadWord w1 X0, (X0, X0)
  | WReg r1, WReg r2 when r1 <> r2 && ok (r1, r2) -> [], ordered (r1, r2)
  // one factor already in a data register (one in B or A1 is loaded like a word in memory)
  | WReg r, other | other, WReg r when other <> WReg r && (r = X0 || r = X1 || r = Y0 || r = Y1) ->
    let partner = if ok (r, Y0) && other <> WReg Y0 then Y0 else X0
    loadWord other partner, ordered (r, partner)
  | _ -> loadWord w1 X0 @ loadWord w2 Y0, (X0, Y0)

/// The two factors of MPYSU/DMAC: any ordered pair of X0, X1, Y0, Y1 (QQQQ), the registers
/// already holding them kept.
let suFactors (xs: Word array) =
  let data r = r = X0 || r = X1 || r = Y0 || r = Y1
  match xs[0], xs[1] with
  | WReg r1, WReg r2 when data r1 && data r2 -> [], (r1, r2)
  | WReg r1, w when data r1 -> (let t = (if r1 = Y0 then X0 else Y0) in loadWord w t, (r1, t))
  | w, WReg r2 when data r2 -> (let t = (if r2 = X0 then Y0 else X0) in loadWord w t, (t, r2))
  | w1, w2 -> loadWord w1 X0 @ loadWord w2 Y0, (X0, Y0)

type AccLeaf =
  | AHome of Home              // a spilled or local Acc, hi:lo:ext
  | AConst of int64
  | AOfWord of Word            // Acc.ofFrac
  | AOfLong of Mem * Mem       // Acc.ofLong of a Long in memory

/// The two-operand Acc operations: A op B into A, or the flags of A - B.
type BinOp =
  | BAdd
  | BSub
  | BCmp

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
  | AHome { reg = Some r } -> if r = acc then [||] else [| tfr r acc |]
  | AHome h -> [| move (at h 0) acc; move (at h 1) lo; move (at h 2) e |]
  | AConst c -> loadAccConst c acc
  | AOfWord w -> loadWord w acc |> List.toArray
  | AOfLong(hi, l) -> [| move hi acc; move l lo |]

let storeA (h: Home) =
  match h.ty with
  | TAcc when h.reg.IsSome -> [| tfr a h.reg.Value |]
  | TAcc -> [| move a1 (at h 0); move a0 (at h 1); move a2 (at h 2) |]
  | TLong -> [| move a1 (at h 0); move a0 (at h 1) |]
  | TFrac -> fail "a Frac home takes a word, not an accumulator"
  | TArray _ | TLongArray _ -> fail "an array is written element by element"
  | TOther t -> fail $"{t} has no home"

/// The arguments of a call to intrinsic `name`.
let (|Intr|_|) (name: string) (e: Expr) =
  match e with
  | Call(None, mi, args) when intrinsic (resolve mi args) = Some name -> Some args
  | _ -> None

let callName (ctx: Ctx) (e: Expr) =
  match e with
  | Call(_, mi, args) -> intrinsic (resolve mi args)
  | _ -> None

/// `i`, `i + c` or `i - c` for a loop index i (nested, as a helper given `i - 1` adds its own
/// offset): (i, c).
let rec loopOffset (ctx: Ctx) (isIndex: Var -> bool) (e: Expr) =
  let offset (c: Expr) = match tryConst ctx c with Some(:? int as n) -> Some n | _ -> None
  match e with
  | Var v when isIndex v -> Some(v, 0)
  | Call(None, mi, [ x; c ]) when mi.Name = "op_Addition" && (offset c).IsSome ->
    loopOffset ctx isIndex x |> Option.map (fun (v, k) -> v, k + (offset c).Value)
  | Call(None, mi, [ c; x ]) when mi.Name = "op_Addition" && (offset c).IsSome ->
    loopOffset ctx isIndex x |> Option.map (fun (v, k) -> v, k + (offset c).Value)
  | Call(None, mi, [ x; c ]) when mi.Name = "op_Subtraction" && (offset c).IsSome ->
    loopOffset ctx isIndex x |> Option.map (fun (v, k) -> v, k - (offset c).Value)
  | _ -> None

/// A region index `v` or `Acc.hi (Acc.ofFrac v + d)` / `- d`, d a constant word: (v, d signed).
let stepIndex (ctx: Ctx) (e: Expr) =
  let word (x: Expr) =
    match tryConst ctx x with
    | Some(:? Frac as f) -> Some((f.Word <<< 8) >>> 8)
    | _ -> None
  match e with
  | Var v -> Some(v, 0)
  | Intr "acc.hi" [ Intr "acc.addFrac" [ Intr "acc.ofFrac" [ Var v ]; d ] ] when (word d).IsSome -> Some(v, (word d).Value)
  | Intr "acc.hi" [ Intr "acc.add" [ Intr "acc.ofFrac" [ Var v ]; d ] ] when (word d).IsSome -> Some(v, (word d).Value)
  | Intr "acc.hi" [ Intr "acc.add" [ d; Intr "acc.ofFrac" [ Var v ] ] ] when (word d).IsSome -> Some(v, (word d).Value)
  | Intr "acc.hi" [ Intr "acc.subFrac" [ Intr "acc.ofFrac" [ Var v ]; d ] ] when (word d).IsSome -> Some(v, -(word d).Value)
  | Intr "acc.hi" [ Intr "acc.sub" [ Intr "acc.ofFrac" [ Var v ]; d ] ] when (word d).IsSome -> Some(v, -(word d).Value)
  | _ -> None

/// `Frac.index w`, `Frac.index w + c` or `Frac.index w - c`, not known when compiling: (w, c).
let computedIndex (ctx: Ctx) (e: Expr) =
  if (tryConst ctx e).IsSome then None
  else
    match e with
    | Intr "frac.index" [ w ] -> Some(w, 0)
    | Call(None, mi, [ Intr "frac.index" [ w ]; c ]) when mi.Name = "op_Addition" -> Some(w, constInt ctx "an index offset" c)
    | Call(None, mi, [ c; Intr "frac.index" [ w ] ]) when mi.Name = "op_Addition" -> Some(w, constInt ctx "an index offset" c)
    | Call(None, mi, [ Intr "frac.index" [ w ]; c ]) when mi.Name = "op_Subtraction" -> Some(w, -(constInt ctx "an index offset" c))
    | _ -> None

/// `s.field[Frac.index w + c]` on a [<Length n>] state array: (the field, w, c, the value written).
let (|ComputedElement|_|) (ctx: Ctx) (e: Expr) =
  match e with
  | Call(None, mi, PropertyGet(Some(Var v), p, []) :: index :: rest)
      when (mi.Name = "GetArray" || mi.Name = "SetArray") && v.Type = ctx.kernel.stateType ->
    match (field ctx p.Name).ty, computedIndex ctx index with
    | TArray _, Some(w, c) -> Some(field ctx p.Name, w, c, rest)
    | _ -> None
  | _ -> None

/// `s.field[k]` on a [<Length n>] state array: its workset word for a constant k, or the loop's
/// address register for k = i + c. (A computed k is ComputedElement.)
let (|StateElement|_|) (ctx: Ctx) (e: Expr) =
  match e with
  | Call(None, mi, PropertyGet(Some(Var v), p, []) :: index :: rest)
      when (mi.Name = "GetArray" || mi.Name = "SetArray") && v.Type = ctx.kernel.stateType ->
    let f = field ctx p.Name
    match f.ty with
    | TArray n ->
      match loopOffset ctx (fun i -> ctx.indexed.ContainsKey(i, p.Name)) index with
      | Some(i, c) ->
        let reg, bias = ctx.indexed[i, p.Name]
        let d = bias + c
        Some(({ space = spaceOf f; ea = (if d = 0 then Ind reg else Disp(reg, Operand.num d)) } : Mem), rest)
      | None when (computedIndex ctx index).IsSome -> None
      | None ->
        let k = constInt ctx $"the index into {p.Name} (a constant, or the loop's index plus or minus a constant)" index
        if k < 0 || k >= n then fail $"{p.Name}[{k}] is outside its {n} words"
        Some(({ space = spaceOf f; ea = disp r6 (f.offset + k) } : Mem), rest)
    | _ -> None
  | _ -> None

/// `s.field[k]` on a [<Length n>] Long array: the element's hi and lo words, for a constant k or
/// k = i + c on the loop's address register (stepping two words a pass).
let (|LongElement|_|) (ctx: Ctx) (e: Expr) =
  match e with
  | Call(None, mi, PropertyGet(Some(Var v), p, []) :: index :: rest)
      when (mi.Name = "GetArray" || mi.Name = "SetArray") && v.Type = ctx.kernel.stateType ->
    let f = field ctx p.Name
    match f.ty with
    | TLongArray n ->
      let mem ea : Mem = { space = spaceOf f; ea = ea }
      match loopOffset ctx (fun i -> ctx.indexed.ContainsKey(i, p.Name)) index with
      | Some(i, c) ->
        let reg, bias = ctx.indexed[i, p.Name]
        let d = bias + 2 * c
        Some(mem (if d = 0 then Ind reg else Disp(reg, Operand.num d)), mem (Disp(reg, Operand.num (d + 1))), rest)
      | None ->
        if (computedIndex ctx index).IsSome then
          fail $"{p.Name}: a Long array is indexed by a constant or the loop's index plus or minus a constant, not a Frac.index"
        let k = constInt ctx $"the index into {p.Name} (a constant, or the loop's index plus or minus a constant)" index
        if k < 0 || k >= n then fail $"{p.Name}[{k}] is outside its {n} Longs"
        Some(mem (disp r6 (f.offset + 2 * k)), mem (disp r6 (f.offset + 2 * k + 1)), rest)
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
    // a1 or a0 of an accumulator kept in memory or B: the word itself
    | Call(None, mi, [ x ]) when (let n = intrinsic (resolve mi [ x ]) in n = Some "acc.hi" || n = Some "acc.lo") && (accLeaf ctx x).IsSome ->
      let word = if intrinsic (resolve mi [ x ]) = Some "acc.hi" then 0 else 1
      match (accLeaf ctx x).Value with
      | AHome { reg = Some B } -> Some(WReg(if word = 0 then B1 else B0))
      | AHome h when h.reg.IsNone -> Some(WMem(at h word))
      | AOfLong(hi, lo) -> Some(WMem(if word = 0 then hi else lo))
      | AOfWord w when word = 0 -> Some w
      | _ -> None
    | Call(None, mi, [ Var i ]) when mi.Name = "ofRaw" && mi.Declaring = TyFrac && ctx.counters.ContainsKey i ->
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
    | LongElement ctx (hi, lo, []) -> Some(AOfLong(hi, lo))
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

/// Generate `first` with `es` read until it is done, for the frames of the subroutines it calls
/// only (register choices do not see them).
let pend (ctx: Ctx) (es: Expr list) (first: unit -> unit) =
  let saved = ctx.pending
  ctx.pending <- es @ saved
  first ()
  ctx.pending <- saved

/// Generate a let's value knowing the value itself and the let's body still read what they read:
/// the lets an inlined helper binds inside the value must not take the registers of the caller's
/// locals the rest of the value or the body use.
let valueOf (ctx: Ctx) (value: Expr) (body: Expr) (gen: unit -> unit) =
  thenRun ctx body (fun () -> thenRun ctx value gen)

let tableLabel (t: Table) = "K_TBL_" + t.name.ToUpperInvariant().Replace("-", "_")

// ---------------------------------------------------------------------------------------------
// Expressions.

/// A local whose value can be read in place of the variable: a constant, a parameter, an
/// immutable local.
/// How much a local is used, loops weighted by their trip counts.
let score (ctx: Ctx) (w: Var) = match ctx.scores.TryGetValue w with | true, n -> n | _ -> 0

/// Whether a loop or a branch in `e` reads `v`: where a value in memory is reloaded, as straight
/// code does not.
let rec acrossControl (v: Var) (e: Expr) =
  match e with
  | ForIntegerRangeLoop _ | WhileLoop _ | IfThenElse _ when e.GetFreeVars() |> Seq.contains v -> true
  | ExprShape.ShapeVar _ -> false
  | ExprShape.ShapeLambda(_, b) -> acrossControl v b
  | ExprShape.ShapeCombination(_, xs) -> List.exists (acrossControl v) xs

/// Everything compiling a scope changes but the label counter, to compile it again.
/// Every node of an expression, outermost first; `enter e` says whether to look inside e. With an
/// explicit stack: a long kernel's bodies nest lets thousands deep.
let visit (enter: Expr -> bool) (root: Expr) =
  let todo = Stack<Expr>()
  todo.Push root
  while todo.Count > 0 do
    let e = todo.Pop()
    if enter e then
      match e with
      | ExprShape.ShapeVar _ -> ()
      | ExprShape.ShapeLambda(_, b) -> todo.Push b
      | ExprShape.ShapeCombination(_, xs) -> for x in List.rev xs do todo.Push x

/// The locals a DO loop's body steps by a constant (one `v <- v + c` at its top level, c a constant
/// word) and otherwise reads only as the index of constant regions (`Region.read r v`,
/// `Region.write r (v + d) x`): (v, c, the regions).
let inductionsOf (ctx: Ctx) (body: Expr) =
  let steps = Dictionary<Var, int>()
  // the body's top level: its statements and the bodies of its lets
  let spine = Stack<Expr>()
  spine.Push body
  while spine.Count > 0 do
    match spine.Pop() with
    | Sequential(a, b) -> spine.Push b; spine.Push a
    | Let(_, _, b) -> spine.Push b
    | VarSet(w, x) ->
      match stepIndex ctx x with
      | Some(v, c) when v = w && ctx.vars.ContainsKey w && not (steps.ContainsKey w) -> steps[w] <- c
      | Some(v, _) when v = w -> steps[w] <- System.Int32.MinValue        // stepped twice
      | _ -> ()
    | _ -> ()
  let bad = HashSet<Var>()
  let regions = Dictionary<Var, ResizeArray<Region>>()
  let assigned = Dictionary<Var, int>()
  let index (r: Expr) (i: Expr) =
    match stepIndex ctx i, constObject ctx r with
    | Some(v, _), Some(:? Region as region) when steps.ContainsKey v ->
      if not (regions.ContainsKey v) then regions[v] <- ResizeArray()
      if not (regions[v] |> Seq.exists (fun x -> x.name = region.name)) then regions[v].Add region
      true
    | _ -> false
  let skipIndex = HashSet<Expr>(HashIdentity.Reference)
  body |> visit (fun e ->
    match e with
    | _ when skipIndex.Contains e -> false
    | Intr "region.read" [ r; i ] when index r i -> false
    | Intr "region.write" [ r; i; _ ] when index r i -> skipIndex.Add i |> ignore; skipIndex.Add r |> ignore; true
    | VarSet(w, x) when steps.ContainsKey w ->
      assigned[w] <- (match assigned.TryGetValue w with | true, n -> n + 1 | _ -> 1)
      match stepIndex ctx x with
      | Some(v, _) when v = w -> false
      | _ -> bad.Add w |> ignore; true
    | Var w -> bad.Add w |> ignore; false
    | _ -> true)
  [ for KeyValue(w, c) in steps do
      if c <> System.Int32.MinValue && not (bad.Contains w) && regions.ContainsKey w
         && assigned.TryGetValue w = (true, 1) && ctx.vars[w].ty = TFrac then
        yield w, c, List.ofSeq regions[w] ]

/// A read of a ring: `Region.read r (base + ((p + c) & mask))` (or `((p + c) & mask) + base`),
/// seen through inlined helpers and their arguments' lets. p is kept symbolic (a sum of words:
/// locals, state and parameter fields, state array elements, with their signs), c a constant.
type RingTap =
  { region   : Region
    mask     : int
    baseKey  : string           // base, symbolically
    posKey   : string           // p without its constant
    offset   : int              // c
    baseExpr : Expr             // base, a Frac (the inlined lets around it)
    wrapped  : Expr }           // (p + c) & mask's operand, an Acc (the inlined lets around it)

let ringTapOf (ctx: Ctx) (value: Expr) : RingTap option =
  let sext (w: int) = (w <<< 8) >>> 8
  // a Frac expression as a sum of signed words and a constant
  let rec lin (env: Map<Var, Expr>) (e: Expr) : (Map<string, int> * int) option =
    match tryConst ctx e with
    | Some(:? Frac as f) -> Some(Map.empty, sext f.Word)
    | _ ->
      match e with
      | Var x when env.ContainsKey x -> lin env env[x]
      | Var x when ctx.vars.ContainsKey x -> Some(Map [ $"v:{x.Name}:{x.GetHashCode()}", 1 ], 0)
      | PropertyGet(Some(Var o), p, []) when o.Type = ctx.kernel.stateType || o.Type = ctx.kernel.paramsType ->
        Some(Map [ $"f:{p.Name}", 1 ], 0)
      | Call(None, mi, [ PropertyGet(Some(Var o), p, []); ix ]) when mi.Name = "GetArray" && o.Type = ctx.kernel.stateType ->
        let ixKey = match tryConst ctx ix with Some c -> $"{c}" | None -> (match ix with Var i -> $"{i.Name}:{i.GetHashCode()}" | _ -> "?")
        if ixKey = "?" then None else Some(Map [ $"a:{p.Name}:{ixKey}", 1 ], 0)
      | Intr "acc.hi" [ x ] -> linAcc env x
      | _ -> None
  and linAcc (env: Map<Var, Expr>) (e: Expr) =
    let add (a: Map<string, int>, k) (b: Map<string, int>, l) sign =
      let m = b |> Map.fold (fun (m: Map<string, int>) key c -> m.Add(key, (defaultArg (m.TryFind key) 0) + sign * c)) a
      Some(m |> Map.filter (fun _ c -> c <> 0), k + sign * l)
    match e with
    | Intr "acc.ofFrac" [ x ] -> lin env x
    | Intr "acc.addFrac" [ a; b ] -> (match linAcc env a, lin env b with Some x, Some y -> add x y 1 | _ -> None)
    | Intr "acc.subFrac" [ a; b ] -> (match linAcc env a, lin env b with Some x, Some y -> add x y -1 | _ -> None)
    | Intr "acc.add" [ a; b ] -> (match linAcc env a, linAcc env b with Some x, Some y -> add x y 1 | _ -> None)
    | Intr "acc.sub" [ a; b ] -> (match linAcc env a, linAcc env b with Some x, Some y -> add x y -1 | _ -> None)
    | Let(v, x, b) when not v.IsMutable -> linAcc (env.Add(v, x)) b
    | _ -> None
  let key (m: Map<string, int>) = m |> Map.toList |> List.map (fun (k, c) -> $"{c}*{k}") |> String.concat "+"
  // the lets an expression is read under, around it
  let under (env: (Var * Expr) list) (e: Expr) = List.foldBack (fun (v, x) b -> Expr.Let(v, x, b)) env e
  let rec find (env: (Var * Expr) list) (e: Expr) =
    match e with
    | Let(v, x, b) when not v.IsMutable && not (v.Type = TyCursor) -> find (env @ [ v, x ]) b
    | Call(None, mi, args) when intrinsic (resolve mi args) = None && not (isSub ctx mi) && (reflected mi).IsSome ->
      find env (inlined mi args)
    | Intr "region.read" [ r; i ] ->
      // an index computed by a helper: inlined, its arguments' lets kept with the others
      let rec open' (env: (Var * Expr) list) (i: Expr) =
        match i with
        | Let(v, x, b) when not v.IsMutable -> open' (env @ [ v, x ]) b
        | Call(None, mi, args) when intrinsic (resolve mi args) = None && not (isSub ctx mi) && (reflected mi).IsSome ->
          open' env (inlined mi args)
        | _ -> env, i
      let env, i = open' env i
      let map = Map.ofList env
      let ring (m: Expr) (x: Expr) (b: Expr) =
        match constObject ctx r, tryConst ctx m, linAcc map x, lin map b with
        | Some(:? Region as region), Some(:? int as mask), Some(pm, c), Some(bm, bk)
            when mask > 1 && mask < 0x8000 && (mask &&& (mask + 1)) = 0 && not pm.IsEmpty ->
          Some { region = region; mask = mask; baseKey = $"{key bm}+{bk}"; posKey = key pm; offset = c
                 baseExpr = under env b; wrapped = under env x }
        | _ -> None
      match i with
      | Intr "acc.hi" [ Intr "acc.addFrac" [ Intr "acc.ofFrac" [ b ]; Intr "acc.hi" [ Intr "acc.andHi" [ m; x ] ] ] ] -> ring m x b
      | Intr "acc.hi" [ Intr "acc.addFrac" [ Intr "acc.andHi" [ m; x ]; b ] ] -> ring m x b
      | _ -> None
    | _ -> None
  if value.Type <> TyFrac then None else find [] value

let snapshot (ctx: Ctx) =
  let count = ctx.items.Count
  let bindings = ctx.bindings.Count
  let line, vars, consts, next, high, cursors, freeRegs, conts = ctx.line, ctx.vars, ctx.consts, ctx.next, ctx.high, ctx.cursors, ctx.freeRegs, ctx.conts
  let sourceLine = ctx.sourceLine
  let released, deadLocals = List.ofSeq ctx.released, List.ofSeq ctx.deadLocals
  let trackHome, indexed, counters, freeData, tables = ctx.trackHome, ctx.indexed, ctx.counters, ctx.freeData, ctx.tables.Count
  let inductions = ctx.inductions
  let bHeld, bWrite = ctx.bHeld, ctx.bWrite
  let unswitched = List.ofSeq ctx.unswitched
  fun () ->
    ctx.items.RemoveRange(count, ctx.items.Count - count)
    ctx.bindings.RemoveRange(bindings, ctx.bindings.Count - bindings)
    ctx.lines.RemoveRange(count, ctx.lines.Count - count)
    ctx.tables.RemoveRange(tables, ctx.tables.Count - tables)
    ctx.line <- line; ctx.sourceLine <- sourceLine; ctx.vars <- vars; ctx.consts <- consts; ctx.next <- next; ctx.high <- high
    ctx.cursors <- cursors; ctx.freeRegs <- freeRegs; ctx.conts <- conts
    ctx.released.Clear(); ctx.released.UnionWith released
    ctx.deadLocals.Clear(); ctx.deadLocals.UnionWith deadLocals
    ctx.trackHome <- trackHome; ctx.indexed <- indexed; ctx.counters <- counters; ctx.freeData <- freeData
    ctx.inductions <- inductions
    ctx.bHeld <- bHeld; ctx.bWrite <- bWrite
    // a loop split in the attempt given up is split again (it is not left as seen)
    ctx.unswitched.Clear(); ctx.unswitched.UnionWith unswitched

/// A condition in a loop's body (at its top level, not in a branch or an inner loop) that the
/// loop does not change: it reads parameters, constants, locals bound outside the loop and not
/// assigned in it, and state the loop does not write.
let invariantIf (ctx: Ctx) (index: Var) (body: Expr) =
  let assigned = HashSet<Var>()
  let rec sets (e: Expr) =
    match e with
    | VarSet(w, x) -> assigned.Add w |> ignore; sets x
    | ExprShape.ShapeVar _ -> ()
    | ExprShape.ShapeLambda(_, b) -> sets b
    | ExprShape.ShapeCombination(_, xs) -> List.iter sets xs
  sets body
  let rec pure' (c: Expr) =
    match c with
    | Var w -> w <> index && not (assigned.Contains w) && (ctx.vars.ContainsKey w || ctx.consts.ContainsKey w)
    | Value _ -> true
    | PropertyGet(Some(Var p), _, []) when p.Type = ctx.kernel.paramsType -> true
    | PropertyGet(Some(Var s), f, []) when s.Type = ctx.kernel.stateType -> not (writesField f.Name body)
    | PropertyGet(None, _, []) -> true
    | Call(None, mi, args) ->
      let mi' = resolve mi args
      let ok =
        match intrinsic mi' with
        | Some n ->
          not (n.StartsWith "cursor.") && n <> "out.write" && n <> "out.pair" && n <> "region.read" && n <> "region.write"
          && n <> "loop.everyPass"
        | None -> owner mi' = operators || isConst mi' || mi'.Declaring = TyFrac || mi'.Declaring = TyAcc
      ok && List.forall pure' args
    | _ -> false
  let rec find (e: Expr) =
    match e with
    | IfThenElse(c, _, _) when (tryConst ctx c).IsNone && pure' c -> Some c
    | Sequential(a, b) -> find a |> Option.orElse (find b)
    | Let(_, _, b) -> find b
    | _ -> None
  find body

/// The body with the first invariant condition (as found above) decided.
let rec decide (ctx: Ctx) (cond: Expr) (taken: bool) (e: Expr) =
  match e with
  | IfThenElse(c, t, f) when obj.ReferenceEquals(c, cond) -> if taken then t else f
  | Sequential(a, b) ->
    let a' = decide ctx cond taken a
    if obj.ReferenceEquals(a', a) then Expr.Sequential(a, decide ctx cond taken b) else Expr.Sequential(a', b)
  | Let(v, x, b) -> Expr.Let(v, x, decide ctx cond taken b)
  | _ -> e

let substitutable (ctx: Ctx) (v: Var) (value: Expr) (body: Expr) =
  not v.IsMutable
  && (match value with
      | Var w -> not w.IsMutable && ctx.vars.ContainsKey w
      | PropertyGet(Some(Var p), _, []) when p.Type = ctx.kernel.paramsType -> true
      // a state word the scope does not assign
      | PropertyGet(Some(Var s), f, []) when s.Type = ctx.kernel.stateType ->
        (field ctx f.Name).ty = TFrac && not (writesField f.Name body)
      | _ -> (tryConst ctx value).IsSome)

/// A multiplication the multiplier does in one instruction: the accumulated value (if any), the
/// factors, the kind and whether the product is negated.
let mulForm (e: Expr) =
  match e with
  | Intr "acc.add" [l; Intr "frac.mul" [x; y]] -> Some(Some l, x, y, (MulKind.Mac, false))
  | Intr "acc.add" [Intr "frac.mul" [x; y]; r] -> Some(Some r, x, y, (MulKind.Mac, false))
  | Intr "acc.sub" [l; Intr "frac.mul" [x; y]] -> Some(Some l, x, y, (MulKind.Mac, true))
  | Intr "acc.neg" [Intr "frac.mul" [x; y]] -> Some(None, x, y, (MulKind.Mpy, true))
  | Intr "acc.rnd" [Intr "frac.mul" [x; y]] -> Some(None, x, y, (MulKind.Mpyr, false))
  | Intr "acc.rnd" [Intr "acc.mac" [l; x; y]] -> Some(Some l, x, y, (MulKind.Macr, false))
  | Intr "acc.rnd" [Intr "acc.add" [l; Intr "frac.mul" [x; y]]] -> Some(Some l, x, y, (MulKind.Macr, false))
  | Intr "acc.rnd" [Intr "acc.add" [Intr "frac.mul" [x; y]; r]] -> Some(Some r, x, y, (MulKind.Macr, false))
  | Intr "acc.rnd" [Intr "acc.sub" [l; Intr "frac.mul" [x; y]]] -> Some(Some l, x, y, (MulKind.Macr, true))
  | Intr "frac.mul" [x; y] -> Some(None, x, y, (MulKind.Mpy, false))
  | Intr "acc.mac" [acc; x; y] -> Some(Some acc, x, y, (MulKind.Mac, false))
  | _ -> None

/// The largest magnitude an Acc expression can have, from what it is made of (infinity: not
/// known). Below 1, the value is a sign-extended 48-bit a1:a0 and can be added as an X or Y pair.
let rec accBound (ctx: Ctx) (e: Expr) : float =
  match tryConst ctx e with
  | Some(:? Acc as c) -> abs (float c.Raw) / 140737488355328.0
  | _ ->
    match e with
    | Var v when ctx.vars.ContainsKey v -> ctx.vars[v].bound
    | Intr "acc.ofFrac" [_] | Intr "acc.ofLong" [_] | Intr "frac.mul" [_; _] -> 1.0
    | Intr "frac.mulsu" [_; _] -> 2.0
    | Intr "acc.macsu" [l; _; _] -> accBound ctx l + 2.0
    | Intr "acc.add" [l; r] | Intr "acc.sub" [l; r] -> accBound ctx l + accBound ctx r
    | Intr "acc.addFrac" [l; _] | Intr "acc.subFrac" [l; _] | Intr "acc.mac" [l; _; _] -> accBound ctx l + 1.0
    | Intr "acc.neg" [x] -> accBound ctx x
    | Intr "acc.clb" [_] -> 1.0
    | Intr "acc.asr" [x; n] ->
      match tryConst ctx n with
      | Some(:? int as n) -> accBound ctx x / Math.Pow(2.0, float n)
      | _ -> infinity
    | _ -> infinity

/// The X or Y pair no local holds: (high, low, pair).
let freePair (ctx: Ctx) =
  if List.contains X1 ctx.freeData then Some(X1, X0, X)
  elif List.contains Y1 ctx.freeData then Some(Y1, Y0, Y)
  else None

/// A fixed Acc that fits 48 bits (added as a register pair) leaves B to a local its scope
/// carries around a loop or across a branch.
let yields (ctx: Ctx) (v: Var) (value: Expr) (body: Expr) =
  let rec carried (e: Expr) =
    match e with
    | Let(w, _, b) when w.IsMutable && w.Type = TyAcc && acrossControl w b -> true
    | ExprShape.ShapeVar _ -> false
    | ExprShape.ShapeLambda(_, b) -> carried b
    | ExprShape.ShapeCombination(_, xs) -> List.exists carried xs
  not v.IsMutable && accBound ctx value < 1.0 && carried body

/// `if c then t else f` as a value: a mutable local, set in statement position, then read. It
/// starts as `f` when that is a plain word or accumulator (a variable, a field, a constant) and
/// the condition and `t` change nothing, so only `t` needs a branch; otherwise as zero.
let selection (ctx: Ctx) (e: Expr) =
  match e with
  | IfThenElse(c, t, f) ->
    let v = Var("select", e.Type, true)
    let leaf =
      if e.Type = TyFrac then (wordLeaf ctx f).IsSome
      elif e.Type = TyLong then (longLeaf ctx f).IsSome
      else (accLeaf ctx f).IsSome
    let quiet = (touches false c).writes.IsEmpty && (touches false t).writes.IsEmpty
    if leaf && quiet then
      Expr.Let(v, f, Expr.Sequential(Expr.IfThenElse(c, Expr.VarSet(v, t), Expr.Value(())), Expr.Var v))
    else
      let zero = Ir.zero e.Type
      Expr.Let(v, zero, Expr.Sequential(Expr.IfThenElse(c, Expr.VarSet(v, t), Expr.VarSet(v, f)), Expr.Var v))
  | _ -> e

let rec genAcc (ctx: Ctx) (e: Expr) : unit =
  match accLeaf ctx e with
  | Some l -> emitAll ctx (loadAccLeaf l A)
  | None ->
    match e with
    | Let(v, value, body) -> withLet ctx v value body (fun () -> genAcc ctx body)
    | IfThenElse(c, t, f) when (match tryConst ctx c with Some(:? bool) -> true | _ -> false) ->
      genAcc ctx (if (tryConst ctx c).Value :?> bool then t else f)
    | IfThenElse _ -> genAcc ctx (selection ctx e)
    | Sequential(s, rest) -> thenRun ctx rest (fun () -> genStmt ctx s); genAcc ctx rest
    | Call(None, mi, args) ->
      let mi = resolve mi args
      match intrinsic mi, args with
      | Some "acc.ofFrac", [Intr "acc.hi" [x]] -> emit ctx (move (accPart ctx x 1) a)
      | Some "acc.ofFrac", [Intr "acc.lo" [x]] -> emit ctx (move (accPart ctx x 0) a)
      | Some "acc.ofFrac", [f] -> genWord ctx f; emit ctx (move x0 a)
      | Some "acc.ofLong", [l] -> genLong ctx l
      | _ when (mulForm e).IsSome ->
        let acc, x, y, kind = (mulForm e).Value
        mulInto ctx acc x y kind dsp56k.Acc.A
      | Some "acc.add", [l; r] -> binAcc ctx l r BAdd
      | Some "acc.sub", [l; r] -> binAcc ctx l r BSub
      | Some "acc.rnd", [x] -> genAcc ctx x; emit ctx (rnd a)
      | Some "acc.divide", [x; d] ->
        let w = (operands ctx [ d ])[0]
        genAcc ctx x
        emitAll ctx (loadWord w x0)
        emit ctx (asl a)
        emit ctx (rep (imm 24))
        emit ctx (div x0 a)
      | Some "frac.mulsu", [x; y] ->
        let loads, (s, t) = suFactors (operands ctx [ x; y ])
        emitAll ctx loads
        emit ctx (mpysu s t a)
      | Some "acc.macsu", [l; x; y] ->
        let xs = operands ctx [ x; y ]
        genAcc ctx l
        let loads, (s, t) = suFactors xs
        emitAll ctx loads
        emit ctx (macsu s t a)
      | Some "acc.dmac", [l; x; y] ->
        let xs = operands ctx [ x; y ]
        genAcc ctx l
        let loads, (s, t) = suFactors xs
        emitAll ctx loads
        emit ctx (Op(Dmac(false, SignedSigned, s, t, dsp56k.Acc.A)))
      | Some "acc.clb", [x] ->
        genAcc ctx x
        emit ctx (Op(Clb(dsp56k.Acc.A, dsp56k.Acc.A)))
      // `clb a,b` `normf b1,a`: an accumulator normalised by its own leading bits
      | Some "acc.normf", [Intr "acc.hi" [Intr "acc.clb" [Var y]]; Var x] when x = y && not ctx.bHeld ->
        genAcc ctx (Expr.Var x)
        emit ctx (Op(Clb(dsp56k.Acc.A, dsp56k.Acc.B)))
        emit ctx (Op(Normf(B1, dsp56k.Acc.A)))
      | Some "acc.normf", [k; x] ->
        let w = (operands ctx [ k ])[0]
        genAcc ctx x
        let r, loads = match w with WReg r when r = X0 || r = X1 || r = Y0 || r = Y1 -> r, [] | _ -> X0, loadWord w X0
        emitAll ctx loads
        emit ctx (Op(Normf(r, dsp56k.Acc.A)))
      | Some "acc.divideCarry", [c; x; d] ->
        let carry = constInt ctx "the division's carry" c
        if carry <> 0 && carry <> 1 then fail $"the division's carry is 0 or 1, not {carry}"
        let w = (operands ctx [ d ])[0]
        genAcc ctx x
        emitAll ctx (loadWord w x0)
        emit ctx (if carry = 0 then andi 0xfe ccr else ori 0x01 ccr)
        emit ctx (rep (imm 24))
        emit ctx (div x0 a)
      | Some "acc.addFrac", [l; f] -> binAccWord ctx l f BAdd
      | Some "acc.subFrac", [l; f] -> binAccWord ctx l f BSub
      | Some "acc.neg", [x] -> genAcc ctx x; emit ctx (neg a)
      | Some "acc.asr", [x; n] ->
        let n = constInt ctx "a shift" n
        genAcc ctx x
        if n = 1 then emit ctx (asr a) elif n > 1 then emit ctx (asri n a a)
      | Some "acc.asl", [x; n] ->
        let n = constInt ctx "a shift" n
        genAcc ctx x
        if n = 1 then emit ctx (asl a) elif n > 1 then emit ctx (asli n a a)
      | Some "acc.lsr", [n; x] ->
        let n = constInt ctx "a shift" n
        if n < 0 || n > 31 then fail $"a logical shift is 0 to 31 bits, not {n}"
        genAcc ctx x
        if n = 1 then emit ctx (lsr a) elif n > 1 then emit ctx (lsri n a)
      // by a count when running: `asr s,a,a` `asl s,a,a` `lsr s,a`, s a data register (the
      // immediate form for a constant count)
      | Some("acc.asrBy" | "acc.aslBy" | "acc.lsrBy" as op), [k; x] ->
        let shift = match op with "acc.asrBy" -> ShAsr | "acc.aslBy" -> ShAsl | _ -> ShLsr
        match tryConst ctx k with
        | Some(:? Frac as c) when shift <> ShLsr || c.Raw &&& 0x3f < 32 ->
          let n = c.Raw &&& 0x3f
          genAcc ctx x
          if n > 0 then emit ctx (Op(ShiftImm(shift, n, dsp56k.Acc.A, dsp56k.Acc.A)))
        | _ ->
          let w = (operands ctx [ k ])[0]
          genAcc ctx x
          let r, loads = match w with WReg r when r = X0 || r = X1 || r = Y0 || r = Y1 -> r, [] | _ -> X0, loadWord w X0
          emitAll ctx loads
          emit ctx (Op(ShiftReg(shift, r, dsp56k.Acc.A, dsp56k.Acc.A)))
      | Some "acc.andHi", [m; x] ->
        let m = constInt ctx "andHi's mask" m
        genAcc ctx x
        emit ctx (and' (immL (m &&& 0xffffff)) a)
      | Some n, _ -> fail $"{n} does not give an Acc"
      | None, _ when isSub ctx mi -> callSub ctx mi args
      | None, _ ->
        let body = inlined mi args
        lined ctx body (fun () -> genAcc ctx body)
    | _ -> fail $"not a kernel Acc expression: {e}"

/// A multiplication: the factors in X0 and Y0, the accumulated value (if any) in A. Into B only
/// for the local B holds, accumulating onto itself (`acc` None or that local).
and mulInto (ctx: Ctx) (acc: Expr option) (x: Expr) (y: Expr) (kind: MulKind, negate: bool) (dst: dsp56k.Acc) =
  let xs = operands ctx [ x; y ]
  if dst = dsp56k.Acc.A then acc |> Option.iter (genAcc ctx)
  let product (it: Item) =
    ctx.bWrite <- dst = dsp56k.Acc.B
    emit ctx it
    ctx.bWrite <- false
  match xs with
  | [| WImm v; w |] | [| w; WImm v |] when (match w with WImm _ -> false | _ -> true) ->
    // a constant factor rides in the instruction: mpyi/maci #c,s,a
    let r, loads = match w with WReg r when r = X0 || r = X1 || r = Y0 || r = Y1 -> r, [] | _ -> X0, loadWord w X0
    emitAll ctx loads
    product (Op(MulI(kind, negate, immL v, r, dst)))
  | _ ->
    let loads, (s, t) = placeFactors xs[0] xs[1]
    emitAll ctx loads
    product (alu (Mul(kind, negate, s, t, dst)))

/// l op r with l in A and r in B. While B holds a local, a word operand is used as it is
/// (sign-extended, as in B) and an operation on the local itself is turned around.
and binAcc (ctx: Ctx) (l: Expr) (r: Expr) (op: BinOp) =
  let item (s: Reg) = match op with BAdd -> add s a | BSub -> sub s a | BCmp -> cmp s a
  let held (x: Expr) = match accLeaf ctx x with Some(AHome { reg = Some _ }) -> true | _ -> false
  // a constant that is a word in a1 (a0 zero, a2 its sign) rides in the instruction: `cmp #k,a`
  let word (c: int64) =
    let hi = (c >>> 24) &&& 0xffffffL
    let signed = if hi &&& 0x800000L <> 0L then hi - 0x1000000L else hi
    if c &&& 0xffffffL = 0L && (c >>> 24) = signed then Some(int hi) else None
  let constant =
    match accLeaf ctx r with
    | Some(AConst c) -> word c
    | Some(AOfWord(WImm v)) -> Some v
    | _ -> None
  match accLeaf ctx r with
  | _ when constant.IsSome ->
    genAcc ctx l
    let k = imm constant.Value
    emit ctx (match op with BAdd -> add k a | BSub -> sub k a | BCmp -> cmp k a)
  | Some(AOfWord w) when not (conflict (touches false l) (touches false r)) ->
    genAcc ctx l
    match w with
    | WReg s when s = X0 || s = X1 || s = Y0 || s = Y1 -> emit ctx (item s)
    | _ ->
      emitAll ctx (loadWord w x0)
      emit ctx (item x0)
  // B holds a local: an Acc that fits 48 bits added as an X or Y pair
  | Some(AHome h) when ctx.bHeld && h.reg.IsNone && h.bound < 1.0 && op <> BCmp && (freePair ctx).IsSome ->
    genAcc ctx l
    let hi, lo, pair = (freePair ctx).Value
    emit ctx (move (at h 0) hi)
    emit ctx (move (at h 1) lo)
    emit ctx (match op with BSub -> sub pair a | _ -> add pair a)
  | Some rl when not (held l) || held r ->
    genAcc ctx l
    emitAll ctx (loadAccLeaf rl B)
    emit ctx (item b)
  | _ when held l ->
    genAcc ctx r
    match op with
    | BAdd -> emit ctx (add b a)
    | BSub -> emit ctx (sub b a); emit ctx (neg a)
    | BCmp -> emit ctx (cmp a b)
  | _ ->
    match accLeaf ctx l with
    | Some _ when op = BAdd && not (conflict (touches false l) (touches false r)) ->
      binAcc ctx r l BAdd
    | Some _ when not ctx.bHeld && not (held l) && not (conflict (touches false l) (touches false r)) ->
      thenRun ctx l (fun () -> genAcc ctx r)
      emit ctx (tfr a b)
      emitAll ctx (loadAccLeaf (accLeaf ctx l).Value A)
      emit ctx (item b)
    | _ ->
      genAcc ctx r
      let t = alloc ctx TAcc
      emitAll ctx (storeA t)
      genAcc ctx l
      emitAll ctx (loadAccLeaf (AHome t) B)
      emit ctx (item b)

/// A word operand is aligned with a1. Constants use the same immediate form as binAcc.
and binAccWord (ctx: Ctx) (l: Expr) (f: Expr) (op: BinOp) =
  let w = (operands ctx [ f ])[0]
  genAcc ctx l
  let register r = if op = BSub then sub r a else add r a
  match w with
  | WImm k -> emit ctx (if op = BSub then sub (imm k) a else add (imm k) a)
  | WReg r when r = X0 || r = X1 || r = Y0 || r = Y1 -> emit ctx (register r)
  | _ ->
    emitAll ctx (loadWord w x0)
    emit ctx (register x0)

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
    | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "long.ofAccSat" ->
      // the E bit clear: the value fits in a1:a0 (a2 its sign) as it is
      genAcc ctx x
      let fits, low = fresh ctx "LFITS", fresh ctx "LLOW"
      emit ctx (tst a)
      emit ctx (bcc Cond.Ec fits)
      emit ctx (bcc Cond.Mi low)
      emit ctx (move (immL 0x7fffff) a)
      emit ctx (move (immL 0xffffff) a0)
      emit ctx (bra fits)
      emit ctx (Label low)
      emit ctx (move (immL 0x800000) a)
      emit ctx (Label fits)
    | Let(v, value, body) -> withLet ctx v value body (fun () -> genLong ctx body)
    | Call(None, mi, args) when (intrinsic (resolve mi args)).IsNone && isSub ctx mi -> callSub ctx mi args
    | Call(None, mi, args) when (intrinsic (resolve mi args)).IsNone ->
      let body = inlined mi args
      lined ctx body (fun () -> genLong ctx body)
    | IfThenElse(c, t, f) when (match tryConst ctx c with Some(:? bool) -> true | _ -> false) ->
      genLong ctx (if (tryConst ctx c).Value :?> bool then t else f)
    | IfThenElse _ -> genLong ctx (selection ctx e)
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
    | IfThenElse _ -> genWord ctx (selection ctx e)
    | Sequential(s, rest) -> thenRun ctx rest (fun () -> genStmt ctx s); genWord ctx rest
    | ComputedElement ctx (f, w, c, []) ->
      indexInN0 ctx w (f.offset + c)
      emit ctx (move r6 r0)
      emit ctx (move (computedMem f.bank) x0)
    | Call(None, mi, args) ->
      let mi = resolve mi args
      match intrinsic mi, args with
      | Some "acc.hi", [x] -> emit ctx (move (accPart ctx x 1) x0)
      | Some "acc.lo", [x] -> emit ctx (move (accPart ctx x 0) x0)
      | Some "acc.sat", [x] -> emit ctx (move (accPart ctx x 2) x0)
      | Some "frac.ofBool", [c] -> genWord ctx (selection ctx (Expr.IfThenElse(c, Expr.Value(box (Frac.ofRaw 1), TyFrac), Ir.zero TyFrac)))
      | Some "table.read", [t; i] -> tableRead ctx t i 1 0 x0
      | Some "region.read", [_; _] when ctx.affineAt.ContainsKey e ->
        emit ctx (move ctx.affineAt[e] x0)
      | Some "region.read", [r; i] when (inducted ctx r i).IsSome ->
        emit ctx (move (inducted ctx r i).Value x0)
      | Some "region.read", [r; i] ->
        let region = constRegion ctx r
        indexInN0 ctx i 0
        regionInR0 ctx region
        emit ctx (move (computedMem region.bank) x0)
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
      // the word in a1: `lsr #n,a` or `lsr s,a`, then a1
      | Some("frac.lsr" | "frac.lsrBy" as op), [n; f] ->
        let ofFrac = Expr.Call(Ir.intrinsic "acc.ofFrac", [ f ])
        let shifted = Expr.Call(Ir.intrinsic (if op = "frac.lsr" then "acc.lsr" else "acc.lsrBy"), [ n; ofFrac ])
        genWord ctx (Expr.Call(Ir.intrinsic "acc.hi", [ shifted ]))
      | Some "frac.lfsr", [taps; st] ->
        let taps = constInt ctx "LFSR taps" taps
        genAcc ctx (Expr.Call(Ir.intrinsic "acc.ofFrac", [ st ]))
        let skip = fresh ctx "LFSR"
        emit ctx (lsl a)
        emit ctx (bcc Cond.Cc skip)
        emit ctx (eor (immL (taps &&& 0xffffff)) a)
        emit ctx (Label skip)
        emit ctx (move a1 x0)
      | Some n, _ -> fail $"{n} does not give a Frac"
      | None, _ when isSub ctx mi -> callSub ctx mi args
      | None, _ ->
        let body = inlined mi args
        lined ctx body (fun () -> genWord ctx body)
    | Call(Some(Var c), mi, []) when ctx.cursors.ContainsKey c ->
      match intrinsic mi with
      | Some "cursor.next" -> emit ctx (move (cursorMem ctx.cursors[c] true) x0)
      | Some "cursor.peek" -> emit ctx (move (cursorMem ctx.cursors[c] false) x0)
      | Some("cursor.up" | "cursor.down" as n) -> emit ctx (move (cursorOne ctx.cursors[c] n) x0)
      | Some "cursor.position" when ctx.cursors[c].state.IsSome ->
        let cr = ctx.cursors[c]
        emit ctx (move (R cr.reg) a)
        emit ctx (move r6 x0)
        emit ctx (sub x0 a)
        if cr.state.Value <> 0 then emit ctx (sub (immL cr.state.Value) a)
        emit ctx (move a1 x0)
      | Some "cursor.position" ->
        let cr = ctx.cursors[c]
        emit ctx (move (R cr.reg) a)
        importHere ctx cr.region.name cr.region.abiSymbol (move (immL 0) x0)
        emit ctx (sub x0 a)
        emit ctx (move a1 x0)
      | _ -> fail $"{mi.Name} on a cursor does not give a Frac"
    | _ -> fail $"not a kernel Frac expression: {e}"

/// Table word `index * width + col`: a resource as `move x:(r0+table),dst` (the address patched
/// by the loader), program words as `move p:(r0),dst` (the address relocated). At a constant
/// index the address is known when the program is built: `move x:>k,dst` (patched) or
/// `movem p:>TABLE+k,dst` (relocated), with no address arithmetic.
and tableRead (ctx: Ctx) (t: Expr) (index: Expr) (width: int) (col: int) (dst: Reg) =
  let table =
    match tryConst' ctx t with
    | Some(:? Table as t) -> t
    | _ -> fail $"a table must be a constant, got {t}"
  let constant =
    match tryConst ctx index with
    | Some(:? Frac as f) ->
      let k = f.Raw * width + col
      if k >= 0 && k < table.words.Length then Some k else None
    | _ -> None
  match constant, table.source with
  | Some k, Program ->
    if not (ctx.tables |> Seq.exists (fun x -> x.name = table.name)) then ctx.tables.Add table
    emit ctx (Label(fresh ctx "TABLE_ADDR"))
    emit ctx machinedrum.Abi.relocateMarker
    emit ctx (move (pm (absL (Sym(tableLabel table) + k))) dst)
  | Some k, Resource symbol -> importHere ctx table.name symbol (move (xm (absL k)) dst)
  | None, _ ->
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
      genAcc ctx (Expr.Call(Ir.intrinsic "acc.ofFrac", [ index ]))
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

/// n0 = the index word w plus `extra` (a state array's offset in the workset and the index's
/// constant), for an (r0+n0) access.
and indexInN0 (ctx: Ctx) (w: Expr) (extra: int) =
  match wordLeaf ctx w with
  | Some leaf when extra = 0 -> emitAll ctx (loadWord leaf (N 0))
  | _ ->
    genAcc ctx (Expr.Call(Ir.intrinsic "acc.ofFrac", [ w ]))
    if extra <> 0 then emit ctx (add (immL (extra &&& 0xffffff)) a)
    emit ctx (move a1 (N 0))

/// The word a region access reads or writes through a loop's induction register, when its index
/// is a local the loop steps (`v` or `v + d`): x:(rk) or x:(rk+d).
and inducted (ctx: Ctx) (r: Expr) (i: Expr) : Mem option =
  if ctx.inductions.IsEmpty then None
  else
    match stepIndex ctx i with
    | Some(v, d) ->
      let region = constRegion ctx r
      ctx.inductions.TryFind((v, region.name)) |> Option.map (fun reg ->
        { space = (match region.bank with XMemory -> Space.X | YMemory -> Space.Y)
          ea = if d = 0 then Ind reg else Disp(reg, Operand.num d) })
    | None -> None

/// r0 at a region's first word (the loader patches the address in).
and regionInR0 (ctx: Ctx) (region: Region) =
  importHere ctx region.name region.abiSymbol (move (immL 0) r0)

/// The (r0+n0) word in a bank.
and computedMem (bank: Bank) : Mem =
  { space = (match bank with XMemory -> Space.X | YMemory -> Space.Y); ea = IndexN 0 }

/// Whether computing `e` may write r0 or n0: table reads, region and computed array accesses,
/// in e or the helpers it calls.
and usesIndexRegs (ctx: Ctx) (e: Expr) =
  let seen = HashSet<Member>()
  let rec go (e: Expr) =
    match e with
    | Call(None, mi, args) ->
      match intrinsic (resolve mi args) with
      | Some("table.read" | "table.row" | "region.read" | "region.write") -> true
      | Some "frac.index" -> (tryConst ctx e).IsNone
      | Some _ -> List.exists go args
      | None -> List.exists go args || (match reflected mi with Some d when seen.Add mi -> go d | _ -> false)
    | ExprShape.ShapeVar _ -> false
    | ExprShape.ShapeLambda(_, b) -> go b
    | ExprShape.ShapeCombination(_, xs) -> List.exists go xs
  go e

/// `value` into the (r0+n0) word: n0 the index, `setBase` r0. A value that needs r0 or n0 itself
/// is computed first and kept in an X word meanwhile.
and computedStore (ctx: Ctx) (w: Expr) (extra: int) (setBase: unit -> unit) (dst: Mem) (value: Expr) =
  if usesIndexRegs ctx value then
    let mark = ctx.next
    genWord ctx value
    let t = alloc ctx TFrac
    emit ctx (move x0 (at t 0))
    indexInN0 ctx w extra
    setBase ()
    emit ctx (move (at t 0) x0)
    emit ctx (move x0 dst)
    ctx.next <- mark
  else
    indexInN0 ctx w extra
    setBase ()
    storeWord ctx value dst

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

/// A cursor's word one up or one down (Up, Down, PutUp, PutDown), whatever its step.
and cursorOne (c: CursorReg) (how: string) : Mem =
  let space = match c.region.bank with XMemory -> Space.X | YMemory -> Space.Y
  { space = space; ea = (if how = "cursor.up" || how = "cursor.putUp" then PostInc c.reg else PostDec c.reg) }

/// A cursor bound for the scope of `body`: an address register at region + start (or at the
/// state array's word start), modulo the region's or the array's size for a ring, its step in nn.
and withCursor (ctx: Ctx) (v: Var) (value: Expr) (bodyExpr: Expr) (body: unit -> unit) =
  let stateArray (arr: Expr) =
    match arr with
    | PropertyGet(Some(Var s), p, []) when s.Type = ctx.kernel.stateType ->
      let f = field ctx p.Name
      match f.ty with
      | TArray n -> f, n
      | _ -> fail $"{p.Name}: a cursor walks a [<Length n>] state array"
    | _ -> fail $"a cursor walks a region or a state array field (s.name), not {arr}"
  let inState arr start step ring =
    let f, n = stateArray arr
    let region = { name = f.name; bank = f.bank; abiSymbol = -1; address = 0; words = Array.zeroCreate n }
    region, start, constInt ctx "a cursor step" step, ring, None, Some(f, n)
  let (region: Region), start, step, ring, modulus, (over: (Field * int) option) =
    match value with
    | Intr "cursor.linear" [r; start] -> constRegion ctx r, start, 1, false, None, None
    | Intr "cursor.ring" [r; start; step] -> constRegion ctx r, start, constInt ctx "a cursor step" step, true, None, None
    | Intr "cursor.modulo" [r; start; last] -> constRegion ctx r, start, 1, true, Some last, None
    | Intr "cursor.linearIn" [arr; start; step] -> inState arr start step false
    | Intr "cursor.ringIn" [arr; start; step] -> inState arr start step true
    | _ -> fail $"a cursor is bound to Cursor.linear, Cursor.ring, Cursor.modulo, Cursor.linearIn or Cursor.ringIn, not {value}"
  match over with
  | Some(f, n) when ring ->
    // the DSP's ring is the aligned block of the power of two at or above n holding the address
    let mutable size = 1
    while size < n do size <- size * 2
    let bankText = match f.bank with XMemory -> "X" | YMemory -> "Y"
    if n < 2 then fail $"{f.name}: a ring needs two words or more"
    if size > worksetAlignment ctx.target then
      fail $"{f.name}: a {n}-word ring needs r6 on a multiple of {size}; on the {ctx.target.name} it is on a multiple of {worksetAlignment ctx.target}"
    if f.offset % size <> 0 then
      fail $"{f.name} at {bankText}+${f.offset:x}: a {n}-word ring starts on a multiple of {size} in the workset (move the field)"
    if abs step > n then fail $"{f.name}: a ring's step is at most its {n} words"
  | _ -> ()
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
    | [] ->
      let held = [ for KeyValue(c, cr) in ctx.cursors do if not (ctx.released.Contains c) then $"{c.Name} (r{cr.reg})" ]
      let others = [ for KeyValue((i, n), (r, _)) in ctx.indexed -> $"{n}[{i.Name}] (r{r})" ]
      let holders = String.Join(", ", held @ others |> List.distinct)
      fail ($"{v.Name}: too many live cursors at once (r1..r5 hold {holders}). " +
            "A cursor gives its register back once no code still to run reads it: read the others first, or index the array with s.field[Frac.index k]")
  match over, tryConst ctx start with
  | None, Some(:? Frac as f) when f.Raw = 0 -> importHere ctx region.name region.abiSymbol (move (immL 0) (R reg))
  | None, _ ->
    pend ctx [ value; bodyExpr ] (fun () -> genWord ctx start)
    importHere ctx region.name region.abiSymbol (move (immL 0) a)
    emit ctx (add x0 a)
    emit ctx (move a1 (R reg))
  | Some(f, n), Some(:? Frac as k) ->
    if k.Raw < 0 || k.Raw >= n then fail $"{f.name}: a cursor starting at word {k.Raw} is outside its {n} words"
    let d = f.offset + k.Raw
    if d <= 63 then emit ctx (lua (disp r6 d) (R reg))
    else
      emit ctx (move r6 a)
      emit ctx (add (immL d) a)
      emit ctx (move a1 (R reg))
  | Some(f, _), _ ->
    pend ctx [ value; bodyExpr ] (fun () -> genWord ctx start)
    emit ctx (move r6 a)
    emit ctx (add x0 a)
    if f.offset <> 0 then emit ctx (add (immL f.offset) a)
    emit ctx (move a1 (R reg))
  match modulus with
  | Some last ->
    pend ctx [ value; bodyExpr ] (fun () -> genWord ctx last)
    emit ctx (movec x0 (M reg))
  | None -> if ring then emit ctx (movec (immL (region.words.Length - 1)) (M reg))
  if step <> 1 then emit ctx (move (immL (step &&& 0xffffff)) (N reg))
  let saved = ctx.cursors
  ctx.cursors <- ctx.cursors.Add(v, { reg = reg; region = region; step = step; ring = ring; state = over |> Option.map (fun (f, _) -> f.offset) })
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
  | TAcc when h.reg.IsSome -> storeHeld ctx h.reg.Value e
  | TAcc -> genAcc ctx e; emitAll ctx (storeA h)
  | TLong ->
    let rec storeLong (e: Expr) =
      match e with
      | Let(v, value, body) -> withLet ctx v value body (fun () -> storeLong body)
      | Sequential(s, rest) -> thenRun ctx rest (fun () -> genStmt ctx s); storeLong rest
      | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "long.ofAcc" ->
        genAcc ctx x
        emitAll ctx (storeA h)
      | Call(None, mi, args) when (intrinsic (resolve mi args)).IsNone && not (isSub ctx mi) && (reflected mi).IsSome ->
        let body = inlined mi args
        lined ctx body (fun () -> storeLong body)
      | _ ->
        genLong ctx e
        emitAll ctx (storeA h)
    storeLong e
  | TArray _ | TLongArray _ -> fail "an array state field is written element by element, s.field[k] <- v"
  | TOther t -> fail $"{t} values cannot be stored"

/// A value into the accumulator holding a local: multiplications onto the local and words added
/// to it done there, anything else computed in A and copied.
and storeHeld (ctx: Ctx) (r: Reg) (e: Expr) =
  let self (x: Expr) = match accLeaf ctx x with Some(AHome { reg = Some q }) -> q = r | _ -> false
  let dst = if r = B then dsp56k.Acc.B else dsp56k.Acc.A
  let onto (it: Item) =
    ctx.bWrite <- true
    emit ctx it
    ctx.bWrite <- false
  match mulForm e with
  | Some(acc, x, y, kind) when (match acc with None -> true | Some l -> self l) -> mulInto ctx acc x y kind dst
  | _ ->
    let word subtract l f =
      let register s = if subtract then sub s r else add s r
      let immediate k = if subtract then sub (imm k) r else add (imm k) r
      let operand =
        match wordLeaf ctx f, accLeaf ctx f with
        | Some w, _ | _, Some(AOfWord w) -> Some w
        | _, Some(AConst c) when c &&& 0xffffffL = 0L && c >= -0x800000000000L && c <= 0x7fffff000000L ->
          Some(WImm(int ((c >>> 24) &&& 0xffffffL)))
        | _ -> None
      match operand with
      | Some w when self l ->
        match w with
        | WImm k -> onto (immediate k)
        | WReg s when s = X0 || s = X1 || s = Y0 || s = Y1 -> onto (register s)
        | _ ->
          emitAll ctx (loadWord w x0)
          onto (register x0)
        true
      | _ -> false
    // an Acc added to the local: a fitting one as a pair, anything else computed in A
    let acc op l f =
      self l && not (self f) &&
      match accLeaf ctx f, freePair ctx with
      | Some(AHome h), Some(hi, lo, pair) when h.reg.IsNone && h.bound < 1.0 ->
        emit ctx (move (at h 0) hi)
        emit ctx (move (at h 1) lo)
        onto (op pair r)
        true
      | _ ->
        genAcc ctx f
        onto (op a r)
        true
    let done' =
      match e with
      | Intr "acc.add" [l; f] -> word false l f || word false f l || acc add l f || acc add f l
      | Intr "acc.sub" [l; f] -> word true l f || acc sub l f
      | Intr "acc.addFrac" [l; f] -> word false l f
      | Intr "acc.subFrac" [l; f] -> word true l f
      | Call(None, mi, args) when (intrinsic (resolve mi args)).IsNone && not (isSub ctx mi) && (reflected mi).IsSome ->
        let body = inlined mi args
        lined ctx body (fun () -> storeHeld ctx r body)
        true
      | _ -> false
    if not done' then
      genAcc ctx e
      onto (tfr a r)

/// A Frac into a memory word: an accumulator stored directly when it is one.
and storeWord (ctx: Ctx) (e: Expr) (dst: Mem) =
  match e with
  | _ when (wordLeaf ctx e).IsSome ->
    match (wordLeaf ctx e).Value with
    | WReg r -> emit ctx (move r dst)
    | w ->
      emitAll ctx (loadWord w x0)
      emit ctx (move x0 dst)
  | Let(v, value, body) -> withLet ctx v value body (fun () -> storeWord ctx body dst)
  | Sequential(s, rest) -> thenRun ctx rest (fun () -> genStmt ctx s); storeWord ctx rest dst
  | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "acc.sat" -> emit ctx (move (accPart ctx x 2) dst)
  | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "acc.hi" -> emit ctx (move (accPart ctx x 1) dst)
  | Call(None, mi, [x]) when intrinsic (resolve mi [x]) = Some "acc.lo" -> emit ctx (move (accPart ctx x 0) dst)
  | Call(None, mi, args) when (intrinsic (resolve mi args)).IsNone && not (isSub ctx mi) && (reflected mi).IsSome ->
    let body = inlined mi args
    lined ctx body (fun () -> storeWord ctx body dst)
  | _ ->
    match wordLeaf ctx e with
    | Some(WReg r) -> emit ctx (move r dst)
    | _ ->
      genWord ctx e
      emit ctx (move x0 dst)

/// Where an Acc's word is read from once it is computed: a0 (0), a1 (1) or a limited (2), of A
/// or of the accumulator holding a local.
and accPart (ctx: Ctx) (x: Expr) (part: int) =
  let acc =
    match accLeaf ctx x with
    | Some(AHome { reg = Some r }) -> r
    | _ ->
      genAcc ctx x
      A
  let e, hi, lo = parts acc
  ignore e
  match part with
  | 0 -> lo
  | 1 -> hi
  | _ -> acc

/// A Frac into a data register.
and storeWordReg (ctx: Ctx) (e: Expr) (dst: Reg) =
  match e with
  | _ when (wordLeaf ctx e).IsSome -> emitAll ctx (loadWord (wordLeaf ctx e).Value dst)
  | Let(v, value, body) -> withLet ctx v value body (fun () -> storeWordReg ctx body dst)
  | Sequential(s, rest) -> thenRun ctx rest (fun () -> genStmt ctx s); storeWordReg ctx rest dst
  | ComputedElement ctx (f, w, c, []) ->
    indexInN0 ctx w (f.offset + c)
    emit ctx (move r6 r0)
    emit ctx (move (computedMem f.bank) dst)
  | Call(Some(Var c), mi, []) when ctx.cursors.ContainsKey c && (intrinsic mi = Some "cursor.next" || intrinsic mi = Some "cursor.peek") ->
    emit ctx (move (cursorMem ctx.cursors[c] (intrinsic mi = Some "cursor.next")) dst)
  | Call(Some(Var c), mi, []) when ctx.cursors.ContainsKey c && (intrinsic mi = Some "cursor.up" || intrinsic mi = Some "cursor.down") ->
    emit ctx (move (cursorOne ctx.cursors[c] (intrinsic mi).Value) dst)
  | Intr "acc.sat" [x] -> emit ctx (move (accPart ctx x 2) dst)
  | Intr "acc.hi" [x] -> emit ctx (move (accPart ctx x 1) dst)
  | Intr "acc.lo" [x] -> emit ctx (move (accPart ctx x 0) dst)
  | Intr "table.read" [t; i] -> tableRead ctx t i 1 0 dst
  | Intr "table.row" [t; w; i; c] -> tableRead ctx t i (constInt ctx "a table width" w) (constInt ctx "a table column" c) dst
  | Intr "region.read" [_; _] when ctx.affineAt.ContainsKey e -> emit ctx (move ctx.affineAt[e] dst)
  | Intr "region.read" [r; i] when (inducted ctx r i).IsSome -> emit ctx (move (inducted ctx r i).Value dst)
  | Intr "region.read" [r; i] ->
    let region = constRegion ctx r
    indexInN0 ctx i 0
    regionInR0 ctx region
    emit ctx (move (computedMem region.bank) dst)
  | Intr "region.address" [r] ->
    let region : Region = constRegion ctx r
    importHere ctx region.name region.abiSymbol (move (immL 0) dst)
  | Intr "cell.read" [c] ->
    let cell : Cell =
      match tryConst' ctx c with
      | Some(:? Cell as c) -> c
      | _ -> fail $"a cell must be a constant, got {c}"
    importHere ctx cell.name cell.abiSymbol (move (xm (absL 0)) dst)
  | Call(None, mi, args) when (intrinsic (resolve mi args)).IsNone && not (isSub ctx mi) && (reflected mi).IsSome ->
    let body = inlined mi args
    lined ctx body (fun () -> storeWordReg ctx body dst)
  | _ ->
    match wordLeaf ctx e with
    | Some w -> emitAll ctx (loadWord w dst)
    | None ->
      genWord ctx e
      emit ctx (move x0 dst)

/// The value's code takes the value's line, and the body's the body's own (see `lined`).
and withLet (ctx: Ctx) (v: Var) (value: Expr) (bodyExpr: Expr) (body: unit -> unit) =
  lined ctx value (fun () -> withLetAt ctx v value bodyExpr (fun () -> lined ctx bodyExpr body))

and withLetAt (ctx: Ctx) (v: Var) (value: Expr) (bodyExpr: Expr) (body: unit -> unit) =
  if ctx.taps.ContainsKey v then
    // read with its tap group: bound to its home
    let saved = ctx.vars
    ctx.vars <- ctx.vars.Add(v, ctx.taps[v])
    bound ctx v (Some ctx.taps[v]) None body
    ctx.vars <- saved
  elif not v.IsMutable && ringGroup ctx v value bodyExpr body then ()
  elif v.Type = TyCursor then withCursor ctx v value bodyExpr body
  elif (match tyOf v.Type with TOther _ -> true | _ -> false) then
    // not a DSP value: a table, a region, a configuration record... known at compile time
    match constObject ctx value with
    | Some c when not v.IsMutable ->
      let saved = ctx.consts
      ctx.consts <- ctx.consts.Add(v, c)
      bound ctx v None (Some c) body
      ctx.consts <- saved             // an inlined helper's variables are shared by its instances
    | _ -> fail $"{v.Name}: a {IrTy.name v.Type} must be an immutable constant (only Frac, Acc and Long live on the DSP)"
  else
  match tryConst ctx value with
  | Some c when not v.IsMutable ->
    let saved = ctx.consts
    ctx.consts <- ctx.consts.Add(v, c)
    bound ctx v None (Some c) body
    ctx.consts <- saved
  | _ when substitutable ctx v value bodyExpr ->
    let saved = ctx.vars
    match value with
    | Var w -> ctx.vars <- ctx.vars.Add(v, ctx.vars[w]); bound ctx v (Some ctx.vars[w]) None body; ctx.vars <- saved
    | PropertyGet(Some(Var s), p, []) ->
      let f =
        if s.Type = ctx.kernel.stateType then field ctx p.Name
        else ctx.layout.parameters |> Array.find (fun f -> f.name = p.Name)
      let h = { space = spaceOf f; offset = f.offset; ty = TFrac; reg = None; bound = infinity }
      ctx.vars <- ctx.vars.Add(v, h)
      bound ctx v (Some h) None body
      ctx.vars <- saved
    | _ -> fail "unreachable"
  | _ when tyOf v.Type = TAcc && not ctx.bHeld && score ctx v >= 2 && acrossControl v bodyExpr
         && not (yields ctx v value bodyExpr) ->
    // in B for its scope, unless something there needs B
    let restore = snapshot ctx
    try
      let h = { space = Space.X; offset = -1; ty = TAcc; reg = Some B; bound = infinity }
      valueOf ctx value bodyExpr (fun () -> store ctx h value)
      ctx.bHeld <- true
      let saved = ctx.vars
      ctx.vars <- ctx.vars.Add(v, h)
      bound ctx v (Some h) None body
      ctx.vars <- saved
      ctx.bHeld <- false
    with BTaken ->
      restore ()
      inMemory ctx v value bodyExpr body
  | _ -> inMemory ctx v value bodyExpr body

/// Lets in a row reading one ring (ringTapOf: same region, base, mask and position, constant
/// offsets apart): when the ring's block is aligned (region + base a multiple of mask + 1, tested
/// when running), one address register at region + base + ((p + c0) & mask) with modulo
/// mask + 1, each read at (rk+nk), nk the tap's offset from the first; otherwise each read as
/// written. Both paths leave the values in the same homes; the code after them is generated once.
and ringGroup (ctx: Ctx) (v: Var) (value: Expr) (bodyExpr: Expr) (body: unit -> unit) =
  match ringTapOf ctx value with
  | None -> false
  | Some first ->
    let same (t: RingTap) =
      t.region.name = first.region.name && t.mask = first.mask && t.baseKey = first.baseKey && t.posKey = first.posKey
    let rec more (e: Expr) acc =
      match e with
      | Let(w, x, b) when not w.IsMutable && w.Type = TyFrac ->
        match ringTapOf ctx x with
        | Some t when same t -> more b ((w, x, t) :: acc)
        | _ -> List.rev acc
      | _ -> List.rev acc
    let group = (v, value, first) :: more bodyExpr []
    if group.Length < 2 || ctx.freeRegs.IsEmpty then false
    else
      let reg = ctx.freeRegs |> List.tryFind (fun r -> (r >= 4) = (first.region.bank = YMemory)) |> Option.defaultValue ctx.freeRegs.Head
      ctx.freeRegs <- ctx.freeRegs |> List.filter ((<>) reg)
      let mark = ctx.next
      let homes = [ for _ in group -> alloc ctx TFrac ]
      let fallback = fresh ctx "RING_FALLBACK"
      let finish = fresh ctx "RING_DONE"
      let space = match first.region.bank with XMemory -> Space.X | YMemory -> Space.Y
      // n = (p + c0) & mask; r = region + base; the block aligned?
      genAcc ctx first.wrapped
      emit ctx (and' (immL first.mask) a)
      emit ctx (move a1 (N reg))
      genAcc ctx (Expr.Call(Ir.intrinsic "acc.ofFrac", [ first.baseExpr ]))
      importHere ctx first.region.name first.region.abiSymbol (move (immL 0) x0)
      emit ctx (add x0 a)
      emit ctx (move a1 (R reg))
      emit ctx (and' (immL first.mask) a)
      emit ctx (bne fallback)
      emit ctx (movec (immL first.mask) (M reg))
      emit ctx (update (incN (R reg)))
      for (_, _, t), h in List.zip group homes do
        let d = t.offset - first.offset
        if d = 0 then emit ctx (move { space = space; ea = Ind reg } x0)
        else
          emit ctx (move (immL (d &&& 0xffffff)) (N reg))
          emit ctx (move { space = space; ea = IndexN reg } x0)
        emit ctx (move x0 (at h 0))
      emit ctx (movec (immL 0xffffff) (M reg))
      emit ctx (bra finish)
      emit ctx (Label fallback)
      for (_, x, _), h in List.zip group homes do store ctx h x
      emit ctx (Label finish)
      ctx.freeRegs <- reg :: ctx.freeRegs
      for (w, _, _), h in List.tail (List.zip group homes) do ctx.taps[w] <- h
      let saved = ctx.vars
      ctx.vars <- ctx.vars.Add(v, homes.Head)
      try bound ctx v (Some homes.Head) None body
      finally
        // also when the code is given up and generated again (BTaken)
        ctx.vars <- saved
        for (w, _, _) in List.tail group do ctx.taps.Remove w |> ignore
      ctx.next <- mark
      true

/// A local in a register (X1 or Y1 for a Frac used enough) or in X words.
and inMemory (ctx: Ctx) (v: Var) (value: Expr) (bodyExpr: Expr) (body: unit -> unit) =
    let mark = ctx.next
    let score = score ctx
    // a Frac local takes X1 or Y1 when it is used enough, and the locals bound inside its scope
    // that are used more still find one free
    // registers of locals no code still to run reads come back (a let's scope runs to the end
    // of its block, its life often does not); a register shared by aliases waits for all
    if tyOf v.Type = TFrac && score v >= 2 then
      let ahead = value :: bodyExpr :: ctx.conts
      let live (w: Var) = ahead |> List.exists (fun e -> e.GetFreeVars() |> Seq.contains w)
      // Only data-register locals return to this pool. B has its own scoped ownership;
      // recycling it here lets a Frac outlive that scope and be overwritten by the next helper.
      let holders =
        [ for KeyValue(w, h) in ctx.vars do
            match h.reg with
            | Some ((X1 | Y1) as r) when not (ctx.deadLocals.Contains w) -> yield r, w
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
      | Some r -> { space = Space.X; offset = -1; ty = TFrac; reg = Some r; bound = infinity }
      | None when tyOf v.Type = TAcc && not v.IsMutable -> { alloc ctx TAcc with bound = accBound ctx value }
      | None -> alloc ctx (tyOf v.Type)
    let afterHome = ctx.next
    valueOf ctx value bodyExpr (fun () -> store ctx h value)
    ctx.next <- afterHome
    let saved = ctx.vars
    ctx.vars <- ctx.vars.Add(v, h)
    bound ctx v (Some h) None body
    ctx.vars <- saved
    ctx.next <- mark
    match register with
    | Some r when not (ctx.deadLocals.Remove v) -> ctx.freeData <- r :: ctx.freeData
    | _ -> ()

/// The Frac locals bound inside an expression while `v` is still read: those whose scope uses it.
and rivalsOf (v: Var) (e: Expr) =
  match e with
  | Let(w, value, body) when w.Type = TyFrac ->
    (if body.GetFreeVars() |> Seq.contains v then [ w ] else []) @ rivalsOf v value @ rivalsOf v body
  | ExprShape.ShapeVar _ -> []
  | ExprShape.ShapeLambda(_, b) -> rivalsOf v b
  | ExprShape.ShapeCombination(_, xs) -> List.collect (rivalsOf v) xs

/// The Frac locals bound inside an expression.
and innerLocals (e: Expr) =
  match e with
  | Let(w, value, body) when w.Type = TyFrac -> w :: innerLocals value @ innerLocals body
  | ExprShape.ShapeVar _ -> []
  | ExprShape.ShapeLambda(_, b) -> innerLocals b
  | ExprShape.ShapeCombination(_, xs) -> List.collect innerLocals xs

// ---------------------------------------------------------------------------------------------
// Statements.

and genStmt (ctx: Ctx) (e: Expr) : unit =
  match e with
  | Sequential _ | Let _ | ForIntegerRangeLoop _ | WhileLoop _ | IfThenElse _ -> genStmtIn ctx e
  // a statement reads all it reads until it is done (lets inlined helpers bind inside it)
  | _ -> thenRun ctx e (fun () -> genStmtIn ctx e)

and genStmtIn (ctx: Ctx) (e: Expr) : unit =
  let mark = ctx.next
  match e with
  | Sequential _ -> ()
  | _ -> at' ctx e
  match e with
  | Sequential(s1, s2) -> thenRun ctx s2 (fun () -> genStmt ctx s1); genStmt ctx s2
  | Let(v, value, body) -> withLet ctx v value body (fun () -> genStmt ctx body)
  | VarSet(v, value) when ctx.inductions |> Map.exists (fun (w, _) _ -> w = v) ->
    // a stepped local: its address registers step instead (the local is set after the loop)
    let _, c = (stepIndex ctx value).Value
    for KeyValue((w, _), reg) in ctx.inductions do
      if w = v && c <> 0 then
        if c = 1 then emit ctx (update (inc (R reg))) else emit ctx (lua (Disp(reg, Operand.num c)) (R reg))
  | VarSet(v, value) ->
    match Map.tryFind v ctx.vars with
    | Some h -> store ctx h value
    | None -> fail $"{v.Name} is not a kernel local"
  | PropertySet(Some(Var s), p, [], value) when s.Type = ctx.kernel.stateType ->
    store ctx (stateHome (field ctx p.Name)) value
  | ComputedElement ctx (f, w, c, [ value ]) ->
    computedStore ctx w (f.offset + c) (fun () -> emit ctx (move r6 r0)) (computedMem f.bank) value
  | Intr "region.write" [ _; _; value ] when ctx.affineAt.ContainsKey e ->
    storeWord ctx value ctx.affineAt[e]
  | Intr "region.write" [ r; i; value ] when (inducted ctx r i).IsSome ->
    storeWord ctx value (inducted ctx r i).Value
  | Intr "region.write" [ r; i; value ] ->
    let region = constRegion ctx r
    computedStore ctx i 0 (fun () -> regionInR0 ctx region) (computedMem region.bank) value
  | StateElement ctx (m, [ value ]) -> storeWord ctx value m
  | LongElement ctx (hi, lo, [ value ]) ->
    genLong ctx value
    emit ctx (move a1 hi)
    emit ctx (move a0 lo)
  | PropertySet(Some(Var p), f, _, _) when p.Type = ctx.kernel.paramsType ->
    fail $"parameter {f.Name} is read-only"
  | ForIntegerRangeLoop(v, lo, hi, body) when ctx.unrolled.Contains v && (tryConst ctx lo).IsSome && (tryConst ctx hi).IsSome ->
    // [<Unroll>]: the body once for each pass, the index a constant in each copy (no DO)
    let lo = constInt ctx "a loop bound" lo
    let hi = constInt ctx "a loop bound" hi
    let copies = [ for k in lo .. hi -> body.Substitute(fun x -> if x = v then Some(Expr.Value k) else None) ]
    if not copies.IsEmpty then genStmt ctx (copies |> List.reduceBack (fun a b -> Expr.Sequential(a, b)))
  | ForIntegerRangeLoop(v, lo, hi, body) when not (ctx.unswitched.Contains v) && not (ctx.kept.Contains v) && (invariantIf ctx v body).IsSome ->
    // a condition the loop does not change, tested once: a copy of the loop for each outcome,
    // unless the copy takes more words than unswitchMax allows
    let restore = snapshot ctx
    let labels = ctx.labels
    let unswitched = List.ofSeq ctx.unswitched
    let costs = ctx.unswitchCosts.Count
    ctx.unswitched.Add v |> ignore
    let c = (invariantIf ctx v body).Value
    let loop taken = Expr.ForIntegerRangeLoop(v, lo, hi, decide ctx c taken body)
    let first = ctx.items.Count
    genStmt ctx (Expr.IfThenElse(c, loop true, loop false))
    let cost = (ctx.items |> Seq.skip first |> Seq.filter (fun it -> match it with Op _ -> true | _ -> false) |> Seq.length) / 2
    match ctx.unswitchMax with
    | Some m when cost > m ->
      restore ()
      ctx.labels <- labels      // the labels numbered as if the loop had not been split
      ctx.unswitched.Clear(); ctx.unswitched.UnionWith unswitched
      ctx.unswitchCosts.RemoveRange(costs, ctx.unswitchCosts.Count - costs)
      ctx.kept.Add v |> ignore
      genStmt ctx e
    | _ -> ctx.unswitchCosts.Add cost
  | ForIntegerRangeLoop(v, lo, hi, body) when (tryConst ctx lo).IsSome && (computedIndex ctx hi).IsSome ->
    // `for _ in lo .. Frac.index w + c`: the count in A, none when it is 0 or less (as .NET's
    // loop), then DO from a1
    let lo = constInt ctx "a loop bound" lo
    let w, c = (computedIndex ctx hi).Value
    if body.GetFreeVars() |> Seq.contains v then
      fail $"{v.Name}: a loop counted when running (up to Frac.index ...) cannot read its index"
    let skip = fresh ctx "NONE"
    thenRun ctx body (fun () -> pend ctx [ hi ] (fun () -> genWord ctx w))
    emit ctx (move x0 a)
    let k = c - lo + 1
    if k <> 0 then emit ctx (add (immL (k &&& 0xffffff)) a) else emit ctx (tst a)
    emit ctx (bcc Cond.Le skip)
    let endLabel = fresh ctx "LOOP"
    emit ctx (Label(endLabel + "_DO"))
    emit ctx machinedrum.Abi.relocateMarker
    emit ctx (doTo a1 endLabel)
    let first = ctx.items.Count
    ctx.depth <- ctx.depth + 2
    ctx.deepest <- max ctx.deepest ctx.depth
    thenRun ctx body (fun () -> genStmt ctx body)     // the body runs again
    ctx.depth <- ctx.depth - 2
    let last = if ctx.items.Count > first then Some ctx.items[ctx.items.Count - 1] else None
    match last with
    | None | Some(Label _) | Some(Comment _) | Some(Op(Branch _)) -> emit ctx nop
    | _ -> ()
    emit ctx (Label endLabel)
    emit ctx (Label skip)
  | ForIntegerRangeLoop(v, lo, hi, body) ->
    let lo = constInt ctx "a loop bound" lo
    let hi = constInt ctx "a loop bound" hi
    let count = hi - lo + 1
    // what reads the index: state arrays (an address register each, stepping once a pass) and
    // Frac.ofRaw i (a counter word); anything else is refused
    // region indexes affine in the index (inv + c·v + d, through lets, helpers, sums and shifts):
    // grouped by region, invariant part and stride, an address register each
    let assignedIn = HashSet<Var>()
    let boundIn = HashSet<Var>()
    let lets = Dictionary<Var, Expr>()
    body |> visit (fun e ->
      match e with
      | VarSet(w, _) -> assignedIn.Add w |> ignore
      | Let(w, x, _) -> boundIn.Add w |> ignore; (if not w.IsMutable then lets[w] <- x)
      | ForIntegerRangeLoop(w, _, _, _) -> boundIn.Add w |> ignore
      | _ -> ()
      true)
    let sext (w: int) = (w <<< 8) >>> 8
    let plus (a: Expr option) (b: Expr option) =
      match a, b with
      | None, x | x, None -> x
      | Some x, Some y -> Some(Expr.Call(Ir.intrinsic "acc.hi", [ operatorCall "op_Addition" TyAcc [ Expr.Call(Ir.intrinsic "acc.ofFrac", [ x ]); y ] ]))
    let rec affine (env: Map<Var, Expr>) (e: Expr) : (Expr option * int * int) option =
      match tryConst ctx e with
      | Some(:? Frac as f) -> Some(None, 0, sext f.Word)
      | _ ->
        match e with
        | Call(None, mi, [ Var i ]) when i = v && mi.Name = "ofRaw" && mi.Declaring = TyFrac -> Some(None, 1, 0)
        | Var x when env.ContainsKey x -> affine env env[x]
        | Var x when lets.ContainsKey x -> affine env lets[x]
        | Var x when x.Type = TyFrac && not (assignedIn.Contains x) && not (boundIn.Contains x) && ctx.vars.ContainsKey x ->
          Some(Some e, 0, 0)
        | Let(w, x, b) when not w.IsMutable -> affine (env.Add(w, x)) b
        | Intr "acc.hi" [ Intr "acc.addFrac" [ Intr "acc.ofFrac" [ a ]; b ] ] ->
          match affine env a, affine env b with
          | Some(ia, ca, ka), Some(ib, cb, kb) -> Some(plus ia ib, ca + cb, ka + kb)
          | _ -> None
        | Intr "acc.hi" [ Intr "acc.subFrac" [ Intr "acc.ofFrac" [ a ]; b ] ] ->
          match affine env a, affine env b with
          | Some(ia, ca, ka), Some(None, cb, kb) -> Some(ia, ca - cb, ka - kb)
          | _ -> None
        | Intr "acc.hi" [ Intr "acc.asl" [ Intr "acc.ofFrac" [ a ]; n ] ] ->
          match affine env a, tryConst ctx n with
          | Some(None, c, k), Some(:? int as n) when n >= 0 && n < 16 -> Some(None, c <<< n, k <<< n)
          | _ -> None
        | Call(None, mi, args) when intrinsic (resolve mi args) = None && not (isSub ctx mi) && (reflected mi).IsSome ->
          affine env (inlined mi args)
        | _ -> None
    let consumed = HashSet<Expr>(HashIdentity.Reference)
    let groups = Dictionary<string, Region * Expr option * int * int>()   // key -> region, inv, stride, register (-1: none yet)
    let accessAt = Dictionary<Expr, string * int>(HashIdentity.Reference) // access -> group key, displacement
    let take (e: Expr) (r: Expr) (i: Expr) =
      match constObject ctx r, affine Map.empty i with
      | Some(:? Region as region), Some(inv, c, d) when c <> 0 ->
        let key = $"{region.name}|{c}|%A{inv}"
        if not (groups.ContainsKey key) then groups[key] <- (region, inv, c, -1)
        accessAt[e] <- (key, d)
        consumed.Add i |> ignore
        true
      | _ -> false
    // a taken index is not looked into (the written value is)
    body |> visit (fun e ->
      match e with
      | Intr "region.read" [ r; i ] -> not (take e r i)
      | Intr "region.write" [ r; i; _ ] -> take e r i |> ignore; true
      | _ when consumed.Contains e -> false
      | _ -> true)
    // lets read only by those indexes (or by other such lets) are not computed
    let mutable skipped =
      if groups.Count = 0 then Set.empty
      else lets.Keys |> Seq.filter (fun w -> (affine Map.empty (Expr.Var w)).IsSome) |> Set.ofSeq
    let mutable settled = false
    while not settled do
      let others = HashSet<Var>()
      let skippedValues = HashSet<Expr>(HashIdentity.Reference)
      body |> visit (fun e ->
        match e with
        | _ when consumed.Contains e || skippedValues.Contains e -> false
        | Let(w, x, _) when skipped.Contains w -> skippedValues.Add x |> ignore; true
        | Var x -> others.Add x |> ignore; false
        | _ -> true)
      let next = skipped |> Set.filter (fun w -> not (others.Contains w))
      settled <- next = skipped
      skipped <- next
    // the lets only those indexes read go: their values written into the indexes, so the body is
    // generated without them, and a copy of a part of it (an inner loop unswitched) still reads
    // whole indexes; the accesses are found again in the body as it now is
    let body =
      if skipped.IsEmpty then body
      else
        let rec drop (e: Expr) : Expr =
          match e with
          | Let(w, x, b) when skipped.Contains w -> (drop b).Substitute(fun u -> if u = w then Some(drop x) else None)
          | ExprShape.ShapeVar _ -> e
          | ExprShape.ShapeLambda(u, b) -> Expr.Lambda(u, drop b)
          | ExprShape.ShapeCombination(o, xs) -> ExprShape.RebuildShapeCombination(o, List.map drop xs)
        let rewritten = drop body
        consumed.Clear(); accessAt.Clear(); groups.Clear()
        rewritten |> visit (fun e ->
          match e with
          | Intr "region.read" [ r; i ] -> not (take e r i)
          | Intr "region.write" [ r; i; _ ] -> take e r i |> ignore; true
          | _ when consumed.Contains e -> false
          | _ -> true)
        rewritten
    let arrays = Collections.Generic.Dictionary<string, int * int>()   // field -> lowest, highest offset
    let mutable counted = false
    let rec scan (e: Expr) =
      match e with
      | _ when consumed.Contains e -> ()
      | Let(w, _, b) when skipped.Contains w -> scan b
      | Call(None, mi, PropertyGet(Some(Var s), p, []) :: index :: rest)
          when (mi.Name = "GetArray" || mi.Name = "SetArray") && s.Type = ctx.kernel.stateType
               && (loopOffset ctx (fun i -> i = v) index).IsSome ->
        let _, c = (loopOffset ctx (fun i -> i = v) index).Value
        match (field ctx p.Name).ty with
        | TArray n | TLongArray n ->
          if lo + c < 0 || hi + c >= n then fail $"{p.Name}[{v.Name} + {c}] reaches outside its {n} elements for {v.Name} in {lo} .. {hi}"
          let a, b = match arrays.TryGetValue p.Name with | true, r -> r | _ -> (c, c)
          arrays[p.Name] <- (min a c, max b c)
        | _ -> fail $"{p.Name} is not a [<Length n>] array"
        List.iter scan rest
      | Call(None, mi, [ Var i ]) when i = v && mi.Name = "ofRaw" && mi.Declaring = TyFrac -> counted <- true
      // a helper given the index: what its body does with it once inlined
      | Call(None, mi, args) when intrinsic mi = None && not (isSub ctx mi) && (reflected mi).IsSome
                                  && args |> List.exists (fun a -> a.GetFreeVars() |> Seq.contains v) ->
        scan (inlined mi args)
      | Var i when i = v ->
        fail $"the loop index {v.Name} can index state arrays (s.field[{v.Name} + c]) or be read as Frac.ofRaw {v.Name}; not {body}"
      | ExprShape.ShapeVar _ -> ()
      | ExprShape.ShapeLambda(_, b) -> scan b
      | ExprShape.ShapeCombination(_, xs) -> List.iter scan xs
    scan body
    if count > 0 then
      let savedIndexed, savedCounters = ctx.indexed, ctx.counters
      // X and Y arrays stepped together: the X ones from r1..r3, the Y ones from r4, r5, so an
      // X load and a Y load can share one instruction (an XY move takes a register of each bank)
      let mixed = arrays.Keys |> Seq.map (fun name -> (field ctx name).bank) |> Seq.distinct |> Seq.length > 1
      let owners = ResizeArray<int * Field>()    // the registers this loop steps, the array each starts on
      let size (f: Field) = match f.ty with TLongArray _ -> 2 | _ -> 1     // words an element, a pass
      let longRegs = ResizeArray<int>()
      let regs =
        [ for KeyValue(name, _) in arrays do
            let f = field ctx name
            let preferred =
              if not mixed then None
              else ctx.freeRegs |> List.tryFind (fun r -> (r >= 4) = (f.bank = YMemory))
            let fresh =
              match preferred, ctx.freeRegs with
              | Some r, _ -> ctx.freeRegs <- ctx.freeRegs |> List.filter ((<>) r); Some r
              | None, r :: rest -> ctx.freeRegs <- rest; Some r
              | None, [] -> None
            match fresh with
            | Some reg ->
              emit ctx (move r6 a)
              emit ctx (add (immL (f.offset + size f * lo)) a)
              emit ctx (move a1 (R reg))
              ctx.indexed <- ctx.indexed.Add((v, name), (reg, 0))
              owners.Add((reg, f))
              if size f = 2 then longRegs.Add reg
              yield reg
            | None when owners |> Seq.exists (fun (_, o) -> size o = size f) ->
              // no register left: every array here steps by one word a pass, so this one rides on
              // a register the loop already steps, at its distance from that one's array
              // (`x:(r1+d)`: a word more, and no parallel move)
              let same = owners |> Seq.filter (fun (_, o) -> size o = size f) |> List.ofSeq
              let reg, o =
                same |> List.tryFind (fun (_, o) -> o.bank = f.bank) |> Option.defaultValue same.Head
              ctx.indexed <- ctx.indexed.Add((v, name), (reg, f.offset - o.offset))
            | None ->
              let held = [ for KeyValue(c, cr) in ctx.cursors do if not (ctx.released.Contains c) then $"{c.Name} (r{cr.reg})" ]
              let others = [ for KeyValue((i, n), (r, _)) in ctx.indexed do if i <> v then $"{n}[{i.Name}] (r{r})" ]
              let holders = String.Join(", ", held @ others |> List.distinct)
              fail ($"{name}[{v.Name}]: no address register is free for the loop over {v.Name} (r1..r5 hold {holders}). " +
                    $"End a cursor before the loop, or index with a word counted when running: s.{name}[Frac.index (Frac.ofRaw {v.Name})] (r0 and n0, no register held)") ]
      // the affine groups' registers, after the arrays' (which must have one): all or none, and
      // with none the indexes and lets are compiled as they are (scanned again for a counter)
      let affineRegs =
        if groups.Count > 0 && ctx.freeRegs.Length >= groups.Count then
          [ for KeyValue(key, (region, inv, c, _)) in Array.ofSeq groups do
              let reg =
                match ctx.freeRegs |> List.tryFind (fun r -> (r >= 4) = (region.bank = YMemory)) with
                | Some r -> r
                | None -> ctx.freeRegs.Head
              ctx.freeRegs <- ctx.freeRegs |> List.filter ((<>) reg)
              match inv with
              | Some e -> genAcc ctx (Expr.Call(Ir.intrinsic "acc.ofFrac", [ e ]))
              | None -> emit ctx (move (immL 0) a)
              if c * lo <> 0 then emit ctx (add (immL ((c * lo) &&& 0xffffff)) a)
              importHere ctx region.name region.abiSymbol (move (immL 0) x0)
              emit ctx (add x0 a)
              emit ctx (move a1 (R reg))
              groups[key] <- (region, inv, c, reg)
              yield reg, c ]
        else
          if groups.Count > 0 then
            consumed.Clear(); accessAt.Clear()
            skipped <- Set.empty
            scan body
          []
      for KeyValue(e, (key, d)) in accessAt do
        let region, _, _, reg = groups[key]
        ctx.affineAt[e] <- { space = (match region.bank with XMemory -> Space.X | YMemory -> Space.Y)
                             ea = if d = 0 then Ind reg else Disp(reg, Operand.num d) }
      let counter =
        if counted then
          let h = alloc ctx TFrac
          emitAll ctx (loadWord (WImm (lo &&& 0xffffff)) x0)
          emit ctx (move x0 (at h 0))
          ctx.counters <- ctx.counters.Add(v, h)
          Some h
        else None
      // locals the body steps by a constant and reads only as region indexes: an address
      // register at region + local each, stepped where the local was (the local set after)
      let savedInductions = ctx.inductions
      let stepLocals =
        [ for w, c, regions in inductionsOf ctx body do
            let banks = regions |> List.map (fun (r: Region) -> r.bank)
            let pick (bank: Bank) =
              match ctx.freeRegs |> List.tryFind (fun r -> (r >= 4) = (bank = YMemory)), ctx.freeRegs with
              | Some r, _ -> Some r
              | None, r :: _ -> Some r
              | None, [] -> None
            let taken = ResizeArray<int>()
            for b in banks do
              match pick b with
              | Some r -> ctx.freeRegs <- ctx.freeRegs |> List.filter ((<>) r); taken.Add r
              | None -> ()
            if taken.Count = regions.Length then
              for region, reg in List.zip regions (List.ofSeq taken) do
                genAcc ctx (Expr.Call(Ir.intrinsic "acc.ofFrac", [ Expr.Var w ]))
                importHere ctx region.name region.abiSymbol (move (immL 0) x0)
                emit ctx (add x0 a)
                emit ctx (move a1 (R reg))
                ctx.inductions <- ctx.inductions.Add((w, region.name), reg)
              yield w, c, List.ofSeq taken
            else ctx.freeRegs <- List.ofSeq taken @ ctx.freeRegs ]
      let endLabel = fresh ctx "LOOP"
      // the loop's end address is absolute: the loader relocates it
      emit ctx (Label(endLabel + "_DO"))
      emit ctx machinedrum.Abi.relocateMarker
      emit ctx (doTo (imm count) endLabel)
      let first = ctx.items.Count
      ctx.depth <- ctx.depth + 2
      ctx.deepest <- max ctx.deepest ctx.depth
      thenRun ctx body (fun () -> genStmt ctx body)     // the body runs again
      ctx.depth <- ctx.depth - 2
      let stepped = not regs.IsEmpty || counter.IsSome || not affineRegs.IsEmpty
      // DO needs an instruction to end on: the body's end cannot be a branch target
      let last = if ctx.items.Count > first then Some ctx.items[ctx.items.Count - 1] else None
      match last with
      | (None | Some(Label _) | Some(Comment _) | Some(Op(Branch _))) when not stepped -> emit ctx nop
      | _ -> ()
      for reg in regs do
        emit ctx (update (inc (R reg)))
        if longRegs.Contains reg then emit ctx (update (inc (R reg)))
      for reg, c in affineRegs do
        if c = 1 then emit ctx (update (inc (R reg))) else emit ctx (lua (Disp(reg, Operand.num c)) (R reg))
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
      ctx.inductions <- savedInductions
      for e in accessAt.Keys do ctx.affineAt.Remove e |> ignore
      ctx.freeRegs <- (affineRegs |> List.map fst) @ ctx.freeRegs
      for w, c, taken in stepLocals do
        ctx.freeRegs <- taken @ ctx.freeRegs
        let k = c * count
        if k <> 0 then
          genStmt ctx (Expr.VarSet(w, Expr.Call(Ir.intrinsic "acc.hi", [ operatorCall "op_Addition" TyAcc [ Expr.Call(Ir.intrinsic "acc.ofFrac", [ Expr.Var w ]); Expr.Value(box (Frac.ofRaw k), TyFrac) ] ])))
  | WhileLoop(cond, body) ->
    // the condition at the top, tested before each pass (none when it is false at once); the
    // condition and the body are compiled knowing the whole loop runs again after them
    let top = fresh ctx "WHILE"
    let endLabel = fresh ctx "WEND"
    emit ctx (Label top)
    thenRun ctx e (fun () -> thenRun ctx body (fun () -> branchUnless ctx cond endLabel))
    thenRun ctx e (fun () -> genStmt ctx body)
    emit ctx (bra top)
    emit ctx (Label endLabel)
  | IfThenElse(cond, t, f) when (match tryConst ctx cond with Some(:? bool) -> true | _ -> false) ->
    // decided when compiling: only the branch taken is generated
    if (tryConst ctx cond).Value :?> bool then genStmt ctx t else genStmt ctx f
  | IfThenElse(cond, t, f) ->
    let elseLabel = fresh ctx "ELSE"
    // the condition is compiled knowing either branch runs after it
    thenRun ctx f (fun () -> thenRun ctx t (fun () -> thenRun ctx cond (fun () -> branchUnless ctx cond elseLabel)))
    // The then branch gives back the registers of locals and cursors the code after the if no
    // longer reads, but the else branch may still read them: it starts from the registers as
    // they were before the then branch. After the if, the else branch's state holds (what it gave
    // back is dead after the if too, and the then branch's own locals ended inside it).
    let freeData, deadLocals = ctx.freeData, List.ofSeq ctx.deadLocals
    let freeRegs, released = ctx.freeRegs, List.ofSeq ctx.released
    genStmt ctx t
    match f with
    | Value(null, _) -> emit ctx (Label elseLabel)
    | _ ->
      let endLabel = fresh ctx "ENDIF"
      emit ctx (bra endLabel)
      emit ctx (Label elseLabel)
      ctx.freeData <- freeData; ctx.deadLocals.Clear(); ctx.deadLocals.UnionWith deadLocals
      ctx.freeRegs <- freeRegs; ctx.released.Clear(); ctx.released.UnionWith released
      genStmt ctx f
      emit ctx (Label endLabel)
  | Call(Some(Var o), mi, [x]) when o.Type = TyOutput && intrinsic mi = Some "out.write" ->
    storeWord ctx x (ym (inc r7))
  | Call(Some(Var o), mi, [l; r]) when o.Type = TyOutput && intrinsic mi = Some "out.pair" ->
    if ctx.target.channels <> 2 then fail $"{ctx.kernel.name}: WritePair needs a stereo target, {ctx.target.name} has {ctx.target.channels} channel(s)"
    storeWord ctx l (ym (inc r7))
    storeWord ctx r (ym (inc r7))
  | Call(Some(Var c), mi, [x]) when ctx.cursors.ContainsKey c && intrinsic mi = Some "cursor.put" ->
    storeWord ctx x (cursorMem ctx.cursors[c] true)
  | Call(Some(Var c), mi, [x]) when ctx.cursors.ContainsKey c && (intrinsic mi = Some "cursor.putUp" || intrinsic mi = Some "cursor.putDown") ->
    storeWord ctx x (cursorOne ctx.cursors[c] (intrinsic mi).Value)
  | Call(None, mi, args) when (intrinsic (resolve mi args)).IsNone && isSub ctx mi -> callSub ctx mi args
  | Call(None, mi, args) when (intrinsic (resolve mi args)).IsNone -> genStmt ctx (inlined mi args)
  | Value(null, _) -> ()
  | _ -> fail $"not a kernel statement: {e}"
  ctx.next <- mark

/// A call to a [<Subroutine>] helper: the value arguments computed (into words, none left in a
/// register), the caller's X1 and Y1 locals saved, the arguments loaded into their registers,
/// bsr, the locals restored. The result is in X0 or A, where the expression generators leave
/// theirs.
/// The local word `i` (X words up to xLimit, then the Y words after the Y state).
and homeAt (ctx: Ctx) (i: int) : Home =
  let x = ctx.target.xLimit
  if i < x then { space = Space.X; offset = i; ty = TFrac; reg = None; bound = infinity }
  else { space = Space.Y; offset = ctx.layout.yWorkset + i - x; ty = TFrac; reg = None; bound = infinity }

/// One past the last local word in use that code still to run may read: words of locals no
/// code ahead reads are free, any other word in use (a spill, a counter, a word not known) is
/// not. Below the entry's or the subroutine's first word, everything is the callers'.
and liveTop (ctx: Ctx) =
  let x = ctx.target.xLimit
  let ahead = HashSet<Var>()
  for e in ctx.pending @ ctx.conts do ahead.UnionWith(e.GetFreeVars())
  let owners = Dictionary<int, bool>()        // a local's word -> one of the locals there is live
  for KeyValue(v, h) in ctx.vars do
    let local = h.reg.IsNone && ((h.space = Space.X && h.offset >= ctx.layout.xWorkset && h.offset < x)
                                 || (h.space = Space.Y && h.offset >= ctx.layout.yWorkset))
    if local then
      let i = if h.space = Space.X then h.offset else h.offset - ctx.layout.yWorkset + x
      for k in i .. i + words h.ty - 1 do
        owners[k] <- (match owners.TryGetValue k with | true, l -> l | _ -> false) || ahead.Contains v
  let first = match ctx.inSub with Some s -> s.start | None -> ctx.layout.xWorkset
  let mutable top = first
  for i in first .. ctx.next - 1 do
    match owners.TryGetValue i with
    | true, false -> ()
    | _ -> top <- i + 1
  top

and callSub (ctx: Ctx) (mi: Member) (args: Expr list) =
  // B holds a caller's Acc local: that local goes to memory instead (the subroutine may use B)
  if ctx.bHeld then raise BTaken
  let def = (reflected mi).Value
  let ps, _ = parametersOf def
  // a hand-written helper not marked [<Subroutine>]: its routine written here, after the same
  // argument moves a call makes
  let hand = handOf ctx mi
  let atCall = hand.IsSome && not (mi.Marks.Contains "Subroutine")
  let sub =
    match ctx.subs.TryGetValue mi with
    | true, s -> s
    | _ ->
      let name = String(mi.Name |> Seq.map (fun c -> if Char.IsLetterOrDigit c then Char.ToUpperInvariant c else '_') |> Array.ofSeq)
      let s = { label = $"SUB_{name}_{ctx.subs.Count + 1}"; mi = mi; records = Dictionary(); sites = ResizeArray()
                compiled = false; start = 0; deepest = 0 }
      if not atCall then ctx.subs[mi] <- s
      s
  let rec calling (s: Sub option) = match s with Some s when s.mi = mi -> true | Some s -> false | None -> false
  if calling ctx.inSub then fail $"{mi.Name}: a [<Subroutine>] cannot call itself"
  let isRecord (t: IrTy) = t = ctx.kernel.paramsType || t = ctx.kernel.stateType || t = TyOutput
  let pairs =
    match ps, args with
    | [ u ], [] when u.Type = TyUnit -> []
    | _ when ps.Length = args.Length -> List.zip ps args
    | _ -> fail $"{mi.Name}: a [<Subroutine>] takes all its arguments"
  let values =
    [ for v, a in pairs do
        if isRecord v.Type then
          match a with
          | Var w -> if not (sub.records.ContainsKey v.Type) then sub.records[v.Type] <- w
          | _ -> fail $"{mi.Name}: pass the {IrTy.name v.Type} record itself"
        elif v.Type = TyUnit then ()
        else
          match tyOf v.Type with
          | TFrac | TAcc | TLong -> yield v, a
          | _ -> fail $"{mi.Name}: a [<Subroutine>] takes Frac, Acc and Long values and the records, not {IrTy.name v.Type}" ]
  let fracs = values |> List.filter (fun (v, _) -> tyOf v.Type = TFrac)
  let accs = values |> List.filter (fun (v, _) -> tyOf v.Type <> TFrac)
  // the caller's words the code after the call still reads: the subroutine's frame starts right
  // above them, over the locals nothing reads again and the argument words (in registers by the
  // bsr). The caller's X1 and Y1 locals are saved in the first words above, when they fit below
  // the words in use (the arguments are computed above those)
  let top = liveTop ctx
  let kept = [ X1; Y1 ] |> List.filter (fun r -> not (List.contains r ctx.freeData))
  let savedHomes =
    if top + kept.Length <= ctx.next then [ for i in 0 .. kept.Length - 1 -> homeAt ctx (top + i) ]
    else [ for _ in kept -> alloc ctx TFrac ]
  let frame = if top + kept.Length <= ctx.next then top + kept.Length else ctx.next
  if fracs.Length > fracArgs.Length || accs.Length > accArgs.Length then
    fail $"{mi.Name}: a [<Subroutine>] takes up to {fracArgs.Length} Frac and {accArgs.Length} Acc or Long values"
  // the arguments in .NET's order, each knowing the later ones still read what they read
  let staged =
    values |> List.mapi (fun i (v, a) ->
      let later = values |> List.skip (i + 1) |> List.map snd
      let run (f: unit -> unit) =
        match later with
        | [] -> f ()
        | [ x ] -> thenRun ctx x f
        | xs -> thenRun ctx (Expr.NewTuple xs) f
      match tyOf v.Type with
      | TFrac ->
        let mutable w = WImm 0
        run (fun () ->
          w <-
            match wordLeaf ctx a with
            | Some(WReg r) ->
              let t = alloc ctx TFrac
              emit ctx (move r (at t 0))
              WMem(at t 0)
            | Some w -> w
            | None ->
              genWord ctx a
              let t = alloc ctx TFrac
              emit ctx (move x0 (at t 0))
              WMem(at t 0))
        v, Choice1Of2 w
      | ty ->
        let t = alloc ctx ty
        run (fun () ->
          if ty = TAcc then genAcc ctx a else genLong ctx a
          emitAll ctx (storeA t))
        v, Choice2Of2 t)
  // the caller's register locals: the subroutine may use X1 and Y1
  let saved = List.zip kept savedHomes
  for r, t in saved do emit ctx (move r (at t 0))
  let mutable nf, na = 0, 0
  for _, s in staged do
    match s with
    | Choice2Of2 t ->
      let leaf = if t.ty = TLong then AOfLong(at t 0, at t 1) else AHome t
      emitAll ctx (loadAccLeaf leaf accArgs[na])
      na <- na + 1
    | Choice1Of2 _ -> ()
  for _, s in staged do
    match s with
    | Choice1Of2 w ->
      emitAll ctx (loadWord w fracArgs[nf])
      nf <- nf + 1
    | Choice2Of2 _ -> ()
  match hand with
  | Some r when atCall -> handAtCall ctx mi r
  | _ ->
    sub.sites.Add { next = frame; freeRegs = ctx.freeRegs; depth = ctx.depth; caller = ctx.inSub }
    emit ctx (bsr sub.label)
  for r, t in saved do emit ctx (move (at t 0) r)

/// A hand routine written at a call, its arguments in their registers: checked against what the
/// code around it keeps, its labels made unique, between the `hand:` comments the optimizer leaves
/// alone.
and handAtCall (ctx: Ctx) (mi: Member) (r: Hand.Routine) =
  let items = Hand.items r
  Hand.check mi.Name r items ctx.freeRegs ctx.layout true
  let levels = Hand.depth items
  if ctx.inSub.IsNone && ctx.depth + levels > stackLevels then
    fail $"{mi.Name} (hand-written, {r.name}) takes {ctx.depth + levels} system stack levels here (a DO loop two, a call one), a machine may take {stackLevels}"
  ctx.deepest <- max ctx.deepest (ctx.depth + levels)
  ctx.labels <- ctx.labels + 1
  let n = ctx.labels
  emit ctx (Comment $"hand: {r.name}")
  emitAll ctx (Hand.relocated $"HAND_H{n}" (Hand.relabel (fun l -> $"{l}_H{n}") (Hand.withoutRts items)))
  emit ctx (Comment "hand: end")

/// A subroutine's code, after the entries: its words above every call's locals, its arguments
/// stored from their registers into them, its body, rts.
and compileSub (ctx: Ctx) (sub: Sub) =
  match handOf ctx sub.mi with
  | Some r -> handSub ctx sub r
  | None -> compileFSharpSub ctx sub

/// A subroutine compiled from its F#.
and compileFSharpSub (ctx: Ctx) (sub: Sub) =
  sub.compiled <- true
  let ps, body = parametersOf (reflected sub.mi).Value
  ctx.vars <- Map.empty; ctx.consts <- Map.empty; ctx.cursors <- Map.empty
  ctx.indexed <- Map.empty; ctx.counters <- Map.empty; ctx.conts <- []; ctx.pending <- []
  ctx.released.Clear(); ctx.deadLocals.Clear()
  ctx.freeData <- [ X1; Y1 ]; ctx.bHeld <- false; ctx.bWrite <- false; ctx.trackHome <- None
  ctx.freeRegs <- sub.sites |> Seq.map (fun s -> Set.ofList s.freeRegs) |> Set.intersectMany |> Set.toList
  ctx.next <- max (sub.sites |> Seq.map _.next |> Seq.max) (defaultArg (ctx.floors.TryFind (subKey sub.mi)) 0)
  sub.start <- ctx.next
  ctx.depth <- 0; ctx.deepest <- 0
  ctx.inSub <- Some sub
  ctx.line <- 0; ctx.sourceLine <- 0
  emit ctx (Label sub.label)
  let mutable nf, na = 0, 0
  let mutable body = body
  for v in ps do
    if sub.records.ContainsKey v.Type then
      let w = sub.records[v.Type]
      body <- body.Substitute(fun x -> if x = v then Some(Expr.Var w) else None)
    elif v.Type = TyUnit || v.Type = ctx.kernel.paramsType || v.Type = ctx.kernel.stateType || v.Type = TyOutput then ()
    else
      let ty = tyOf v.Type
      let h = alloc ctx ty
      match ty with
      | TFrac ->
        emit ctx (move fracArgs[nf] (at h 0))
        nf <- nf + 1
      | _ ->
        let e, hi, lo = parts accArgs[na]
        emit ctx (move hi (at h 0))
        emit ctx (move lo (at h 1))
        if ty = TAcc then emit ctx (move e (at h 2))
        na <- na + 1
      ctx.vars <- ctx.vars.Add(v, h)
  let body = ordered (normalize body)
  let r = sub.mi.ReturnType
  pend ctx [ body ] (fun () ->
    if r = TyFrac then genWord ctx body
    elif r = TyAcc then genAcc ctx body
    elif r = TyLong then genLong ctx body
    else genStmt ctx body)
  emit ctx rts
  sub.deepest <- ctx.deepest
  ctx.inSub <- None

/// A hand-written [<Subroutine>]: its routine after the label, checked against what every call
/// keeps, then rts.
and handSub (ctx: Ctx) (sub: Sub) (r: Hand.Routine) =
  sub.compiled <- true
  let items = Hand.items r
  let free = sub.sites |> Seq.map (fun s -> Set.ofList s.freeRegs) |> Set.intersectMany |> Set.toList
  Hand.check sub.mi.Name r items free ctx.layout false
  sub.start <- max (sub.sites |> Seq.map _.next |> Seq.max) (defaultArg (ctx.floors.TryFind (subKey sub.mi)) 0)
  sub.deepest <- Hand.depth items
  ctx.line <- 0; ctx.sourceLine <- 0
  emit ctx (Label sub.label)
  emit ctx (Comment $"hand: {r.name}")
  emitAll ctx (Hand.relocated $"HAND_{sub.label}" (Hand.relabel (fun l -> $"{l}_{sub.label}") (Hand.withoutRts items)))
  emit ctx (Comment "hand: end")
  emit ctx rts

/// Branch to `target` when the comparison is false.
and bitTest (ctx: Ctx) (n: Expr) (f: Expr) =
  let n = constInt ctx "a bit number" n
  if n < 0 || n > 23 then fail $"a word's bits are 0 to 23, not {n}"
  let w = (operands ctx [ f ])[0]
  let r, loads = match w with WReg r when r = X0 || r = X1 || r = Y0 || r = Y1 -> r, [] | _ -> X0, loadWord w X0
  emitAll ctx loads
  emit ctx (Op(BitOp(Btst, n, BitReg r)))

and branchUnless (ctx: Ctx) (cond: Expr) (target: string) =
  let asAcc (x: Expr) =
    if x.Type = TyFrac then Expr.Call(Ir.intrinsic "acc.ofFrac", [ x ])
    elif x.Type = TyAcc then x
    else fail $"comparisons are on Frac or Acc values, not {IrTy.name x.Type}"
  match cond with
  | Intr "loop.everyPass" [ c ] -> branchUnless ctx c target
  // a bit of a word: `btst #n,d`, the bit in the carry
  | Intr "frac.bit" [ n; f ] -> bitTest ctx n f; emit ctx (bcc Cond.Cc target)
  | Call(None, mi, [ Intr "frac.bit" [ n; f ] ]) when mi.Name = "Not" -> bitTest ctx n f; emit ctx (bcc Cond.Cs target)
  // known when compiling: no test (a let of a constant condition, `wide || x > y` on wide)
  | _ when (match tryConst ctx cond with Some(:? bool) -> true | _ -> false) ->
    if not ((tryConst ctx cond).Value :?> bool) then emit ctx (bra target)
  // a && b: either false skips
  | IfThenElse(l, r, Value(:? bool as f, _)) when not f -> branchUnless ctx l target; branchUnless ctx r target
  // a || b: a true skips the test of b
  | IfThenElse(l, Value(:? bool as t, _), r) when t ->
    let taken, next = fresh ctx "OR", fresh ctx "ORNEXT"
    branchUnless ctx l next
    emit ctx (bra taken)
    emit ctx (Label next)
    branchUnless ctx r target
    emit ctx (Label taken)
  | Call(None, mi, [ c ]) when mi.Name = "Not" ->
    let taken = fresh ctx "NOT"
    branchUnless ctx c taken
    emit ctx (bra target)
    emit ctx (Label taken)
  | Call(None, mi, [l; r]) when owner mi = operators ->
    let unless name =
      match name with
      | "op_LessThan" -> Cond.Ge
      | "op_LessThanOrEqual" -> Cond.Gt
      | "op_GreaterThan" -> Cond.Le
      | "op_GreaterThanOrEqual" -> Cond.Lt
      | "op_Equality" -> Cond.Ne
      | "op_Inequality" -> Cond.Eq
      | n -> fail $"conditions are comparisons, not {n}"
    let swapped name =
      match name with
      | "op_LessThan" -> "op_GreaterThan"
      | "op_LessThanOrEqual" -> "op_GreaterThanOrEqual"
      | "op_GreaterThan" -> "op_LessThan"
      | "op_GreaterThanOrEqual" -> "op_LessThanOrEqual"
      | n -> n
    let zero (x: Expr) = match tryConst ctx x with Some(:? Acc as v) -> v.Raw = 0L | _ -> false
    // against zero: tst, on the local in B where it is one
    let test (x: Expr) =
      match accLeaf ctx x with
      | Some(AHome { reg = Some r }) -> emit ctx (tst r)
      | _ ->
        genAcc ctx x
        emit ctx (tst a)
    let l, r = asAcc l, asAcc r
    if zero r then
      test l
      emit ctx (bcc (unless mi.Name) target)
    elif zero l then
      test r
      emit ctx (bcc (unless (swapped mi.Name)) target)
    else
      binAcc ctx l r BCmp
      emit ctx (bcc (unless mi.Name) target)
  | _ -> fail $"conditions are comparisons of two values, Frac.bit, a let of one, and &&, || and not of those: {cond}"

// ---------------------------------------------------------------------------------------------
// Entry points.

type Compiled =
  { kernel   : IrKernel
    target   : Target
    layout   : Layout
    items    : Item array
    lines    : int array        // per item, the F# line it came from (0: none)
    source   : string option    // the kernel's F# file, from the quotations' debug ranges
    xWorkset : int              // X words used: the [<XMemory>] state (layout.xWorkset), then locals and spills
    yWorkset : int              // Y words used: layout.yWorkset, then locals spilled past the X workset
    /// The F# locals, where each lives (r6-relative, or a register) or the constant it folded to, and the lines of
    /// the entry's file its scope covers: the register and memory map of a step view.
    bindings : Binding array }

let entry (ctx: Ctx) (name: string) (mi: Member) =
  let def =
    match reflected mi with
    | Some d -> d
    | None -> fail $"{mi.Name}: mark its module [<ReflectedDefinition>]"
  let rec body (e: Expr) =
    match e with
    | Lambda(v, rest) ->
      if v.Type <> ctx.kernel.paramsType && v.Type <> ctx.kernel.stateType && v.Type <> TyOutput then
        fail $"{mi.Name}: argument {v.Name} is a {IrTy.name v.Type}, expected the parameters, the state or the output"
      body rest
    | e -> e
  ctx.next <- ctx.layout.xWorkset
  ctx.line <- 0; ctx.sourceLine <- 0
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
  | Some(file, line) -> ctx.line <- line; ctx.sourceLine <- line; ctx.sourceFile <- Some file
  | None -> ()
  // how much each local is used, loops weighted by their trip counts
  ctx.scores.Clear()
  let rec weigh (w: int) (e: Expr) =
    match e with
    | ForIntegerRangeLoop(_, lo, hi, b) ->
      let n = match tryConst ctx lo, tryConst ctx hi with | Some(:? int as l), Some(:? int as h) -> max 1 (h - l + 1) | _ -> 1
      weigh (w * n) b
    // passes not known: weighed as a short loop
    | WhileLoop(c, b) -> weigh (w * 8) c; weigh (w * 8) b
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
  ctx.line <- 0; ctx.sourceLine <- 0
  genStmt ctx (ordered (normalize (body def)))
  emit ctx rts

/// The words a RENDER body writes, when the count does not depend on the parameters: loops
/// with constant bounds multiply, branches must agree, helpers are counted through.
let rec writes (e: Expr) : int option =
  let sum (xs: Expr list) =
    xs |> List.fold (fun acc x -> match acc, writes x with Some a, Some b -> Some(a + b) | _ -> None) (Some 0)
  match e with
  | Call(Some(Var o), mi, _) when o.Type = TyOutput ->
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

/// A program's P words: its code (the entry points) and its tables (emitted after the code).
type Size =
  { code   : int
    tables : int }
  member s.total = s.code + s.tables

  /// "3299 words (code 3011, tables 288)", for messages.
  member s.text = $"{s.total} words (code {s.code}, tables {s.tables})"

/// The compiled program's size, as assembled.
let size (c: Compiled) : Result<Size, string> =
  match Assembler.assembleItems c.items with
  | Error e -> Error(Assembler.errorText e)
  | Ok r ->
    let _, words = Assembler.image Space.P r
    // the tables follow the last entry's rts
    let last = c.items |> Array.tryFindIndexBack (fun it -> match it with Op Rts -> true | _ -> false)
    let tables =
      match last with
      | Some i -> c.items[i + 1 ..] |> Array.sumBy (fun it -> match it with Dc vs -> vs.Length | _ -> 0)
      | None -> 0
    Ok { code = words.Length - tables; tables = tables }

/// The program refused when it is larger than the target's machine can hold (Kernel.programLimit).
let checkSize (c: Compiled) : Result<Compiled, string> =
  match programLimit c.target, size c with
  | Some(limit, why), Ok s when s.total > limit ->
    Error $"{c.kernel.name}: the program is {s.text}, over the {c.target.name} limit by {s.total - limit}: {why}"
  | _ -> Ok c

/// The loops of functions marked with `attr` (or functions of modules marked with it) a kernel's
/// entries reach, themselves or through the helpers they call.
let markedLoops (mark: string) (k: IrKernel) =
  let kept = HashSet<Var>()
  let seen = HashSet<Member>()
  let marked (mi: Member) = mi.Marks.Contains mark
  let rec walk (keep: bool) (e: Expr) =
    match e with
    | ForIntegerRangeLoop(v, lo, hi, b) ->
      if keep then kept.Add v |> ignore
      walk keep lo; walk keep hi; walk keep b
    | Call(o, mi, args) ->
      Option.iter (walk keep) o
      List.iter (walk keep) args
      if seen.Add mi then reflected mi |> Option.iter (walk (marked mi))
    | ExprShape.ShapeVar _ -> ()
    | ExprShape.ShapeLambda(_, b) -> walk keep b
    | ExprShape.ShapeCombination(_, xs) -> List.iter (walk keep) xs
  for mi in [ k.init; k.trig; k.render ] do
    if seen.Add mi then reflected mi |> Option.iter (walk (marked mi))
  kept

/// The loops of [<NoUnswitch>] functions (or functions of [<NoUnswitch>] modules).
let keptLoops (k: IrKernel) = markedLoops "NoUnswitch" k

/// The loops of [<Unroll>] functions (or functions of [<Unroll>] modules).
let unrolledLoops (k: IrKernel) = markedLoops "Unroll" k

/// A call of a kernel helper (inlined unless it is a subroutine): the entry it is reached from
/// ("INIT", "TRIG", "RENDER", or "SUB" inside a [<Subroutine>]), and whether a loop is around it.
type HelperUse =
  { helper : Member
    entry  : string
    inLoop : bool }

/// Every call of a helper the entries make, once for each place inlining puts it (a call in a
/// helper inlined twice counts twice); a [<Subroutine>]'s body once.
let helperUses (k: IrKernel) =
  let uses = ResizeArray<HelperUse>()
  let seenSubs = HashSet<Member>()
  let rec walk (entry: string) (inLoop: bool) (depth: int) (e: Expr) =
    match e with
    | ForIntegerRangeLoop(_, lo, hi, b) ->
      walk entry inLoop depth lo
      walk entry inLoop depth hi
      walk entry true depth b
    | WhileLoop(c, b) ->
      walk entry true depth c
      walk entry true depth b
    | Call(o, mi, args) ->
      Option.iter (walk entry inLoop depth) o
      List.iter (walk entry inLoop depth) args
      let mi = resolve mi args
      match intrinsic mi, reflected mi with
      | None, Some def ->
        uses.Add { helper = mi; entry = entry; inLoop = inLoop }
        if isSubroutine mi then (if seenSubs.Add mi then walk "SUB" false 0 def)
        elif depth < 32 then walk entry inLoop (depth + 1) def
      | _ -> ()
    | ExprShape.ShapeVar _ -> ()
    | ExprShape.ShapeLambda(_, b) -> walk entry inLoop depth b
    | ExprShape.ShapeCombination(_, xs) -> List.iter (walk entry inLoop depth) xs
  for entry, mi in [ "INIT", k.init; "TRIG", k.trig; "RENDER", k.render ] do
    reflected mi |> Option.iter (walk entry false 0)
  List.ofSeq uses

/// A subroutine compiled below a call found later: compiled again from the start, each one's
/// words from at least the given word.
exception Refloor of Map<string, int>

let rec generateWith (handOff: Set<string>) (chosen: Set<string>) (floors: Map<string, int>) (unswitchMax: int option) (target: Target) (ik: IrKernel) : Result<Compiled * int list, string> =
  try
    let layout = layoutOf target ik.name ik.parameters (defaultArg ik.state [||])
    let ctx =
      { target = target; kernel = ik; layout = layout; items = ResizeArray(); lines = ResizeArray(); line = 0; sourceLine = 0; sourceFile = None
        vars = Map.empty; consts = Map.empty; next = layout.xWorkset; high = layout.xWorkset; labels = 0
        cursors = Map.empty; freeRegs = [ 1; 2; 3; 4; 5 ]; tables = ResizeArray(); conts = []; released = Collections.Generic.HashSet(); trackHome = None
        indexed = Map.empty; counters = Map.empty; inductions = Map.empty; affineAt = Dictionary(HashIdentity.Reference); taps = Dictionary(); freeData = [ X1; Y1 ]; scores = Dictionary(); deadLocals = HashSet()
        unswitched = HashSet(); bHeld = false; bWrite = false
        subs = Dictionary(); chosen = chosen; handOff = handOff; inSub = None; depth = 0; deepest = 0; floors = floors
        kept = keptLoops ik; unrolled = unrolledLoops ik; pending = []; unswitchMax = unswitchMax; unswitchCosts = ResizeArray()
        bindings = ResizeArray() }
    entry ctx "INIT" ik.init
    entry ctx "TRIG" ik.trig
    entry ctx "RENDER" ik.render
    // the subroutines called, each once, those they call included
    let mutable pending = ctx.subs.Values |> Seq.filter (fun s -> not s.compiled) |> Seq.tryHead
    while pending.IsSome do
      compileSub ctx pending.Value
      pending <- ctx.subs.Values |> Seq.filter (fun s -> not s.compiled) |> Seq.tryHead
    // a subroutine's words start above the locals of every call to it: a call found after it
    // was compiled (from a subroutine compiled later) must not reach them
    let low =
      [ for s in ctx.subs.Values do
          let highest = s.sites |> Seq.map _.next |> Seq.max
          if highest > s.start then yield subKey s.mi, highest ]
    if not low.IsEmpty then raise (Refloor(low |> List.fold (fun m (mi, n) -> Map.add mi n m) floors))
    // system stack levels: a call one, a DO loop two, what the subroutine itself takes
    let rec need (s: Sub) : int =
      let inner =
        [ for t in ctx.subs.Values do
            for site in t.sites do
              match site.caller with
              | Some c when obj.ReferenceEquals(c, s) -> yield site.depth + need t
              | _ -> () ]
      1 + List.max (s.deepest :: inner)
    for s in ctx.subs.Values do
      for site in s.sites do
        if site.caller.IsNone && site.depth + need s > stackLevels then
          fail $"{ik.name}: the call to {s.mi.Name} takes {site.depth + need s} system stack levels (a call one, a DO loop two), a machine may take {stackLevels}"
    // the block size, once the entries compile
    match reflected ik.render |> Option.bind writes with
    | Some n when n <> target.blockWords ->
      fail $"{ik.name}: RENDER writes {n} words, the block is {target.blockText}"
    | _ -> ()
    ctx.line <- 0; ctx.sourceLine <- 0
    for t in ctx.tables do
      emit ctx (Label(tableLabel t))
      for row in Array.chunkBySize 8 t.words do
        emit ctx (Dc(row |> Array.map (fun w -> Num(int64 w))))
    let source = reflected ik.render |> Option.bind debugRange |> Option.map fst
    Ok({ kernel = ik; target = target; layout = ctx.layout; items = ctx.items.ToArray(); lines = ctx.lines.ToArray()
         source = source; xWorkset = min ctx.high target.xLimit
         yWorkset = ctx.layout.yWorkset + max 0 (ctx.high - target.xLimit)
         bindings = ctx.bindings.ToArray() }, List.ofSeq ctx.unswitchCosts)
  with
  | KernelError m -> Error m
  | Refloor floors' when floors'.Count <= 64 && floors' <> floors -> generateWith handOff chosen floors' unswitchMax target ik

/// The compiled kernel, its size not checked, each loop unswitched only when its copy takes at
/// most `unswitchMax` words (None: any); and what each unswitched loop's copy took.
/// Code generation recurses once per nested let and statement: a long kernel nests thousands, more
/// than a thread pool thread's stack holds. It runs on its own thread with a 256 MB stack.
/// `chosen`: helpers (by key) called as subroutines besides the [<Subroutine>] ones; `handOff`:
/// [<HandWritten>] helpers (by key) compiled from their F# reference instead of their routine.
let generateIrWith (handOff: Set<string>) (chosen: Set<string>) (unswitchMax: int option) (target: Target) (k: IrKernel) =
  let mutable result = Unchecked.defaultof<Result<Compiled * int list, string>>
  let mutable failure : System.Runtime.ExceptionServices.ExceptionDispatchInfo option = None
  let t =
    System.Threading.Thread(
      (fun () ->
        try result <- generateWith handOff chosen Map.empty unswitchMax target k
        with e -> failure <- Some(System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture e)),
      256 * 1024 * 1024)
  t.Start()
  t.Join()
  // what it raised, as raised (callers catch KernelError)
  failure |> Option.iter (fun f -> f.Throw())
  result

/// As generateIrWith, every hand routine used.
let generateIrChoosing (chosen: Set<string>) (unswitchMax: int option) (target: Target) (k: IrKernel) =
  generateIrWith Set.empty chosen unswitchMax target k

/// The compiled kernel, its size not checked, only the [<Subroutine>] helpers called.
let generateIrUnswitching (unswitchMax: int option) (target: Target) (k: IrKernel) = generateIrChoosing Set.empty unswitchMax target k

/// The same for a kernel written as quotations.
let generateUnswitching (unswitchMax: int option) (target: Target) (k: Kernel) =
  generateIrUnswitching unswitchMax target (FromQuotation.kernel k)

/// The compiled kernel, its size not checked (Optimize.compile checks it after its passes).
let generateIr (target: Target) (k: IrKernel) = generateIrUnswitching None target k |> Result.map fst

/// The compiled kernel, as the compiler generates it, refused when the target's machine
/// cannot hold it.
let compileIr (target: Target) (k: IrKernel) : Result<Compiled, string> = generateIr target k |> Result.bind checkSize

/// A kernel written as quotations: read into the tree, then as above.
let generate (target: Target) (k: Kernel) = generateIr target (FromQuotation.kernel k)

let compile (target: Target) (k: Kernel) : Result<Compiled, string> = compileIr target (FromQuotation.kernel k)

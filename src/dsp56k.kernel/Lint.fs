/// Compiler and backend lints (`dsp-xxxx`) and verified-outcome kudos (`dsp-kxxxx`) queried over
/// the plain diagnostics sink populated from IR and assembly provenance facts.
module dsp56k.kernel.Lint

open System
open System.Collections.Generic
open System.Reflection
open Microsoft.FSharp.Reflection
open dsp56k
open dsp56k.dsl
open dsp56k.dsl.Asm
open dsp56k.kernel.Ir
open dsp56k.kernel.Ir.Patterns
open dsp56k.kernel.Kernel

/// Metadata of one lint rule and its mirror kudos rule.
type Rule =
  { id           : string
    kudosId      : string
    family       : string
    anchor       : string
    title        : string
    summary      : string
    kudosSummary : string }

/// Stable lint and kudos catalogue. Anchor is always `#<id>`, shared by the lint and its mirror kudos.
let rules : Rule array =
  [| { id           = "dsp-0001"
       kudosId      = "dsp-k0001"
       family       = "infrastructure"
       anchor       = "docs/lints.md#dsp-0001"
       title        = "Provenance completeness and optimiser-off cycle budget"
       summary      = "Every emitted instruction must carry a source origin; tracks whether the kernel meets its cycle budget without the optimiser."
       kudosSummary = "Kernel meets its declared cycle budget with the optimiser turned off (Direct)." }
     { id           = "dsp-1001"
       kudosId      = "dsp-k1001"
       family       = "intrinsic"
       anchor       = "docs/lints.md#dsp-1001"
       title        = "Comparison select diamond lowered to conditional branch"
       summary      = "Comparison branch diamonds (`if a < b then a else b`) emit conditional branches instead of branchless `Tcc` transfers (planned: `Min`/`Max`/`Clamp` intrinsics and `select -> Tcc` lowering)."
       kudosSummary = "Conditional value selection lowered to branchless `Tcc` transfer." }
     { id           = "dsp-1002"
       kudosId      = "dsp-k1002"
       family       = "intrinsic"
       anchor       = "docs/lints.md#dsp-1002"
       title        = "Use Long.ofAcc / Acc.ofLong / Acc.hi / Acc.lo instead of 24-bit shifts and bit clears"
       summary      = "Shifting by 24 bits (`<<< 24`, `>>> 24`) and clearing bit 23 to pack or split words emits multi-instruction sequences in direct codegen instead of direct `a1`/`a0` sub-register moves (`Long.ofAcc`/`Acc.ofLong`/`Acc.hi`/`Acc.lo` compile today; `Long.pack`/`Long.hi`/`Long.lo` planned)."
       kudosSummary = "48-bit word pack/unpack uses direct accumulator sub-register moves (`a1`/`a0`) without 24-bit shifts." }
     { id           = "dsp-1003"
       kudosId      = "dsp-k1003"
       family       = "intrinsic"
       anchor       = "docs/lints.md#dsp-1003"
       title        = "Sign-branch negation diamond instead of hardware abs"
       summary      = "Sign-branch absolute value (`if x < 0 then -x else x`) emits a test and branch diamond instead of the 1-cycle `abs` instruction (planned: `Acc.abs` / `Frac.abs` intrinsic)."
       kudosSummary = "Absolute value lowered to single-word 1-cycle hardware `abs` instruction." }
     { id           = "dsp-1004"
       kudosId      = "dsp-k1004"
       family       = "intrinsic"
       anchor       = "docs/lints.md#dsp-1004"
       title        = "Use hardware bitwise intrinsics (Acc.andHi / Frac.lfsr) instead of bit-by-bit loops"
       summary      = "Looping over bits with `Frac.bit` and shifts takes dozens of cycles instead of hardware bitwise instructions (`Acc.andHi` and `Frac.lfsr` compile today; general `eor`/`or` intrinsics planned)."
       kudosSummary = "Hardware bitwise ALU instruction (`eor`, `or`, `and`, `not`) emitted in 1 cycle." }
     { id           = "dsp-2001"
       kudosId      = "dsp-k2001"
       family       = "resources"
       anchor       = "docs/lints.md#dsp-2001"
       title        = "Temporary workset spill in loop or hot path"
       summary      = "Compound accumulator expressions or high live-variable pressure inside a loop spill temporaries to `X:(r6+k)` memory."
       kudosSummary = "Hardware `DO` loop executes with zero temporary workset spills in final assembly." }
     { id           = "dsp-3001"
       kudosId      = "dsp-k3001"
       family       = "control-flow"
       anchor       = "docs/lints.md#dsp-3001"
       title        = "Short branch diamond could not become conditional transfer Tcc"
       summary      = "A short conditional branch diamond remained a `Bcc` jump instead of a branchless `Tcc` conditional transfer."
       kudosSummary = "Conditional select lowered to branchless `Tcc` instruction." }
     { id           = "dsp-3002"
       kudosId      = "dsp-k3002"
       family       = "control-flow"
       anchor       = "docs/lints.md#dsp-3002"
       title        = "Software while loop instead of hardware DO loop"
       summary      = "Software `while` loops use compare-and-branch back-edges unless canonicalised into zero-overhead hardware `DO` loops."
       kudosSummary = "All kernel loops lowered to zero-overhead hardware `DO` loops." }
     { id           = "dsp-4001"
       kudosId      = "dsp-k4001"
       family       = "memory"
       anchor       = "docs/lints.md#dsp-4001"
       title        = "Repeated 2-word indexed Disp addressing in loop instead of pointer walking"
       summary      = "Three or more `(rN+offset)` indexed memory moves inside a loop cost 2 words and 2 cycles each instead of 1-word `(rN)+` pointer walking."
       kudosSummary = "Loop memory accesses use single-word 1-cycle indirect pointer addressing `(rN)+` with zero `Disp` offsets." }
     { id           = "dsp-5001"
       kudosId      = "dsp-k5001"
       family       = "host"
       anchor       = "docs/lints.md#dsp-5001"
       title        = "Generic F# comparison on Frac/Acc/Long structs boxes on .NET (48 B/call)"
       summary      = "Calling generic `<`, `>`, `<=`, `>=`, `=`, `min`, or `max` on `Frac`/`Acc`/`Long` structs boxes via `GenericCompare` on .NET (48 B/call); planned `Min`/`Max`/`Clamp` intrinsics compare `.Raw` with 0 B allocation."
       kudosSummary = "Host .NET `RENDER` executes with 0 B managed allocation per block." } |]

let private ruleById = rules |> Array.map (fun r -> r.id, r) |> Map.ofArray
let private ruleByKudosId = rules |> Array.map (fun r -> r.kudosId, r) |> Map.ofArray

let tryRule (id: string) = Map.tryFind id ruleById
let tryKudosRule (id: string) = Map.tryFind id ruleByKudosId

/// One lint warning queried from the diagnostics sink.
type Warning =
  { id               : string
    family           : string
    layer            : Layer
    origin           : int
    ranges           : Range list
    anchor           : string
    optimiserCleaned : bool
    detail           : string
    message          : string }

/// One verified-outcome kudos queried from the diagnostics sink.
type Kudos =
  { id       : string
    mirrorId : string
    family   : string
    layer    : Layer
    origin   : int
    ranges   : Range list
    anchor   : string
    outcome  : string
    message  : string }

/// Per-kernel authoring score.
type Score =
  { kudosCount             : int
    warningCount           : int
    optimiserRelianceCount : int
    directToOptimisedRatio : float
    bitExact               : bool }

/// Options controlling lint and kudos evaluation.
type Options =
  { layer             : Layer option
    kudos             : bool
    bitExact          : bool
    directCycles      : float option
    optimisedCycles   : float option
    declaredBudget    : int option
    netAllocatedBytes : int64 option }

  static member defaults =
    { layer             = None
      kudos             = false
      bitExact          = true
      directCycles      = None
      optimisedCycles   = None
      declaredBudget    = None
      netAllocatedBytes = None }

/// Complete lint and kudos result for a kernel.
type ResultReport =
  { kernel   : string
    warnings : Warning array
    kudos    : Kudos array
    score    : Score
    facts    : Fact list }

// ---------------------------------------------------------------------------------------------
// Fact collection: IR layer.

let private isKernelValueTy (t: IrTy) =
  match t with
  | TyFrac | TyAcc | TyLong -> true
  | _ -> false

let private isComparisonName (name: string) =
  match name with
  | "op_LessThan" | "op_LessThanOrEqual" | "op_GreaterThan" | "op_GreaterThanOrEqual"
  | "op_Equality" | "op_Inequality" | "Min" | "Max" | "compare" -> true
  | _ -> false

let private isOrderingName (name: string) =
  match name with
  | "op_LessThan" | "op_LessThanOrEqual" | "op_GreaterThan" | "op_GreaterThanOrEqual" -> true
  | _ -> false

let rec private unwrapConv (e: Expr) =
  match e with
  | Call(None, mi, [ x ]) when mi.Intrinsic = Some "acc.ofFrac" || mi.Intrinsic = Some "long.ofAcc" || mi.Intrinsic = Some "acc.ofLong" || mi.Intrinsic = Some "frac.ofAcc" ->
    unwrapConv x
  | _ -> e

let private sameExpr (a: Expr) (b: Expr) =
  let a', b' = unwrapConv a, unwrapConv b
  a' = b'

let private isZeroExpr (e: Expr) =
  match unwrapConv e with
  | Value(:? int as n, _) -> n = 0
  | Value(:? int64 as n, _) -> n = 0L
  | Value(:? Frac as f, _) -> f.Word = 0
  | Value(:? dsp56k.kernel.Acc as a, _) -> a.Raw = 0L
  | Value(:? Long as l, _) -> l.Raw = 0L
  | Call(None, mi, []) when mi.Name = "zero" || mi.Name = "get_zero" -> true
  | PropertyGet(None, mi, []) when mi.Name = "zero" -> true
  | _ -> false

let private isNegOf (target: Expr) (e: Expr) =
  match unwrapConv e with
  | Call(None, mi, [ x ]) when (mi.Name = "op_UnaryNegation" || mi.Intrinsic = Some "acc.neg") && sameExpr x target -> true
  | Call(None, mi, [ z; x ]) when (mi.Name = "op_Subtraction" || mi.Intrinsic = Some "acc.sub") && isZeroExpr z && sameExpr x target -> true
  | _ -> false

let private isCompoundAcc (e: Expr) =
  match e with
  | Call(None, mi, args) when e.Type = TyAcc && args.Length >= 2 ->
    match mi.Intrinsic with
    | Some "acc.ofFrac" | Some "acc.ofLong" -> false
    | _ -> true
  | IfThenElse _ when e.Type = TyAcc -> true
  | _ -> false

let rec private countRegionAccesses (e: Expr) =
  match e with
  | Call(None, mi, _) when mi.Intrinsic = Some "region.read" || mi.Intrinsic = Some "region.write" -> 1
  | ExprShape.ShapeVar _ -> 0
  | ExprShape.ShapeLambda(_, b) -> countRegionAccesses b
  | ExprShape.ShapeCombination(_, xs) -> xs |> List.sumBy countRegionAccesses

let rec private hasBitTestAndShift (e: Expr) =
  let mutable hasBit = false
  let mutable hasShift = false
  let rec go (x: Expr) =
    match x with
    | Call(_, mi, args) ->
      match mi.Intrinsic, mi.Name with
      | Some("frac.bit" | "acc.bchg" | "acc.bset" | "acc.bclr"), _ -> hasBit <- true
      | Some("acc.shl" | "acc.shr" | "frac.shl" | "frac.shr"), _
      | _, ("op_LeftShift" | "op_RightShift") -> hasShift <- true
      | _ -> ()
      List.iter go args
    | ExprShape.ShapeVar _ -> ()
    | ExprShape.ShapeLambda(_, b) -> go b
    | ExprShape.ShapeCombination(_, xs) -> List.iter go xs
  go e
  hasBit && hasShift

/// Collects IR-layer facts from an `IrKernel` and its reachable helpers into `sink`.
let collectIrFacts (sink: Sink) (ik: IrKernel) =
  let seenMembers = HashSet<string>()
  let rec walk (inLoop: bool) (e: Expr) =
    let orig = e.Origin
    match e with
    | WhileLoop(cond, body) ->
      Sink.record sink orig IrLayer "dsp-3002" "while loop in kernel IR"
      walk true cond
      walk true body
    | ForIntegerRangeLoop(_, lo, hi, body) ->
      match lo, hi with
      | Value(:? int as l, _), Value(:? int as h, _) when h - l + 1 >= 8 && h - l + 1 <= 24 && hasBitTestAndShift body ->
        Sink.record sink orig IrLayer "dsp-1004" $"bit-by-bit loop ({h - l + 1} iterations) with bit test and shift"
      | _ -> ()
      let regOps = countRegionAccesses body
      if regOps >= 3 then
        Sink.record sink orig IrLayer "dsp-4001" $"{regOps} indexed Region.read/write calls in loop body"
      walk inLoop lo
      walk inLoop hi
      walk true body
    | IfThenElse(cond, t, f) ->
      match cond with
      | Call(None, cmp, [ l; r ]) when isOrderingName cmp.Name && (isKernelValueTy l.Type || isKernelValueTy r.Type) ->
        if (isZeroExpr r && ((sameExpr t l && isNegOf l f) || (sameExpr f l && isNegOf l t)))
           || (isZeroExpr l && ((sameExpr t r && isNegOf r f) || (sameExpr f r && isNegOf r t))) then
          Sink.record sink orig IrLayer "dsp-1003" "sign-branch negation diamond (`if x < 0 then -x else x`)"
        elif (sameExpr t l && sameExpr f r) || (sameExpr t r && sameExpr f l)
             || sameExpr t l || sameExpr f l || sameExpr t r || sameExpr f r then
          Sink.record sink orig IrLayer "dsp-1001" $"comparison diamond on `{cmp.Name}` selecting clamp/min/max bound"
        elif isKernelValueTy e.Type then
          Sink.record sink orig IrLayer "dsp-3001" "conditional value diamond in IR"
      | _ when isKernelValueTy e.Type && inLoop ->
        Sink.record sink orig IrLayer "dsp-3001" "conditional value selection inside loop"
      | _ -> ()
      walk inLoop cond
      walk inLoop t
      walk inLoop f
    | Call(o, mi, args) ->
      // Generic FSharp.Core comparison on Frac/Acc/Long structs (boxes on .NET)
      if (mi.Owner = "Microsoft.FSharp.Core.Operators" || mi.Owner.StartsWith "Microsoft.FSharp.Collections.")
         && isComparisonName mi.Name
         && (args |> List.exists (fun a -> isKernelValueTy a.Type)) then
        Sink.record sink orig IrLayer "dsp-5001" $"generic `{mi.Name}` on `{IrTy.name (args |> List.find (fun a -> isKernelValueTy a.Type)).Type}` boxes on .NET (48 B/call)"
      let resolved = Compile.resolve mi args
      match resolved.Intrinsic, resolved.Name, args with
      | Some("acc.shl" | "acc.shr"), _, [ _; Value(:? int as n, _) ] when n = 24 ->
        Sink.record sink orig IrLayer "dsp-1002" $"24-bit shift (`{resolved.Name} 24`) used for word packing/unpacking"
      | _, ("op_LeftShift" | "op_RightShift"), [ x; Value(:? int as n, _) ] when n = 24 && isKernelValueTy x.Type ->
        Sink.record sink orig IrLayer "dsp-1002" $"24-bit shift (`{resolved.Name} 24`) used for word packing/unpacking"
      | Some "acc.bclr", _, [ Value(:? int as n, _); _ ] when n = 23 ->
        Sink.record sink orig IrLayer "dsp-1002" "bit 23 clear (`Acc.bclr 23`) used for manual unsigned low-word packing"
      | Some("acc.add" | "acc.sub"), _, [ a; b ] when inLoop && isCompoundAcc a && isCompoundAcc b ->
        Sink.record sink orig IrLayer "dsp-2001" "nested binary Acc expression in loop requires a temporary spill in direct codegen"
      | _ -> ()
      Option.iter (walk inLoop) o
      List.iter (walk inLoop) args
      if resolved.Intrinsic.IsNone then
        match Compile.reflected resolved with
        | Some def when seenMembers.Add resolved.Key -> walk inLoop def
        | _ -> ()
    | ExprShape.ShapeVar _ -> ()
    | ExprShape.ShapeLambda(_, b) -> walk inLoop b
    | ExprShape.ShapeCombination(_, xs) -> List.iter (walk inLoop) xs
  for mi in [ ik.init; ik.trig; ik.render ] do
    if seenMembers.Add mi.Key then
      Compile.reflected mi |> Option.iter (walk false)

// ---------------------------------------------------------------------------------------------
// Fact collection: ASM layer (comparing Direct vs Peephole items).

let private isTempWorksetMove (layout: Layout) (m: Move) =
  let isTemp (sp: Space) (ea: Ea) =
    match ea with
    | Disp(6, { expr = Num k }) when sp = Space.X && int k >= layout.xWorkset -> true
    | Disp(6, { expr = Num k }) when sp = Space.Y && int k >= layout.yWorkset -> true
    | Abs { expr = Num 0L } when sp = Space.X -> true
    | _ -> false
  match m with
  | Load(sp, ea, _) | Store(_, sp, ea) -> isTemp sp ea
  | _ -> false

let private isDispMove (m: Move) =
  match m with
  | Load(_, Disp(_, { expr = Num _ }), _) | Store(_, _, Disp(_, { expr = Num _ })) -> true
  | _ -> false

let private isPointerWalkMove (m: Move) =
  match m with
  | Load(_, (PostInc _ | PostDec _ | Ind _), _) | Store(_, _, (PostInc _ | PostDec _ | Ind _)) -> true
  | _ -> false

let private isOrderingCond (cc: Cond) =
  match cc with
  | Cond.Ge | Cond.Gt | Cond.Le | Cond.Lt | Cond.Pl | Cond.Mi -> true
  | _ -> false

type private AsmScan =
  { missingOrigins : (int * string) list
    diamonds1001   : (int * string) list
    shifts1002     : (int * string) list
    absDiamonds1003: (int * string) list
    bitLoops1004   : (int * string) list
    loopSpills2001 : (int * string) list
    diamonds3001   : (int * string) list
    backEdges3002  : (int * string) list
    loopDisps4001  : (int * string) list
    kudos1001      : (int * string) list
    kudos1002      : (int * string) list
    kudos1003      : (int * string) list
    kudos1004      : (int * string) list
    kudos2001      : (int * string) list
    kudos3001      : (int * string) list
    kudos3002      : (int * string) list
    kudos4001      : (int * string) list }

let private scanAsm (layout: Layout) (items: (Item * int) array) : AsmScan =
  let missingOrigins = ResizeArray<int * string>()
  let diamonds1001 = ResizeArray<int * string>()
  let shifts1002 = ResizeArray<int * string>()
  let absDiamonds1003 = ResizeArray<int * string>()
  let bitLoops1004 = ResizeArray<int * string>()
  let loopSpills2001 = ResizeArray<int * string>()
  let diamonds3001 = ResizeArray<int * string>()
  let backEdges3002 = ResizeArray<int * string>()
  let loopDisps4001 = ResizeArray<int * string>()
  let kudos1001 = ResizeArray<int * string>()
  let kudos1002 = ResizeArray<int * string>()
  let kudos1003 = ResizeArray<int * string>()
  let kudos1004 = ResizeArray<int * string>()
  let kudos2001 = ResizeArray<int * string>()
  let kudos3001 = ResizeArray<int * string>()
  let kudos3002 = ResizeArray<int * string>()
  let kudos4001 = ResizeArray<int * string>()

  let labelIdx = Dictionary<string, int>()
  let branchOrigins = HashSet<int>()
  for i in 0 .. items.Length - 1 do
    match items[i] with
    | Label l, _ -> labelIdx[l] <- i
    | Op(Branch _), orig ->
      branchOrigins.Add orig |> ignore
      if orig <= 0 then missingOrigins.Add(orig, Print.item (fst items[i]))
    | Op _, orig when orig <= 0 ->
      missingOrigins.Add(orig, Print.item (fst items[i]))
    | _ -> ()

  let mutable hasDoLoop = false
  let mutable firstDoOrigin = Origin.synthetic
  let mutable hasRep24Shift = false
  let mutable subwordMoveOrigin = 0
  let mutable sawA1 = false
  let mutable sawA0 = false

  // Non-comment operational items between two indices
  let opsBetween (lo: int) (hi: int) =
    [ for k in lo .. min (items.Length - 1) hi do
        match fst items[k] with
        | Comment _ -> ()
        | it -> yield k, it, snd items[k] ]

  for i in 0 .. items.Length - 1 do
    let it, orig = items[i]
    let orig = if orig > 0 then orig else Origin.synthetic
    match it with
    | Op(Rep(Count { expr = Num 24L })) when i + 1 < items.Length ->
      match fst items[i + 1] with
      | Op(ShiftImm((ShAsl | ShAsr | ShLsl | ShLsr), _, _, _))
      | Op(Par(Some(Unary((AluUnary.Asl | AluUnary.Asr | AluUnary.Lsl | AluUnary.Lsr), _)), _)) ->
        hasRep24Shift <- true
        shifts1002.Add(orig, "rep #24 followed by shift for word packing/unpacking")
      | _ -> ()
    | Op(BitOp(Bclr, 23, _)) ->
      // bclr #23 used right after loading or shifting a word into an accumulator
      let nearbyShift =
        [ max 0 (i - 4) .. min (items.Length - 1) (i + 4) ]
        |> List.exists (fun k ->
          match fst items[k] with
          | Op(Rep(Count { expr = Num 24L })) | Op(ShiftImm(_, 24, _, _)) -> true
          | _ -> false)
      if nearbyShift then
        shifts1002.Add(orig, "bclr #23 with 24-bit shift for manual word packing")
    | Op(Par(alu, moves)) ->
      match alu with
      | Some(Unary(AluUnary.Abs, d)) ->
        kudos1003.Add(orig, $"hardware `abs {Print.reg (if d = dsp56k.Acc.A then A else B)}` instruction")
      | Some(Binary((AluOp.Eor | AluOp.Or | AluOp.And as op), _, _)) ->
        let opName = (sprintf "%A" op).ToLowerInvariant()
        kudos1004.Add(orig, $"hardware bitwise `{opName}` instruction")
      | Some(Unary(AluUnary.Not, _)) ->
        kudos1004.Add(orig, "hardware bitwise `not` instruction")
      | _ -> ()
      for m in moves do
        match m with
        | Copy(A1, _) | Copy(_, A1) | Load(_, _, A1) | Store(A1, _, _) -> sawA1 <- true; if subwordMoveOrigin = 0 then subwordMoveOrigin <- orig
        | Copy(A0, _) | Copy(_, A0) | Load(_, _, A0) | Store(A0, _, _) -> sawA0 <- true; if subwordMoveOrigin = 0 then subwordMoveOrigin <- orig
        | _ -> ()
    | Op(AluImm((AndI | OrI | EorI as op), _, _)) ->
      let opName = (sprintf "%A" op).ToLowerInvariant()
      kudos1004.Add(orig, $"hardware bitwise immediate `{opName}` instruction")
    | Op(Tcc(cc, _, _)) ->
      if not (branchOrigins.Contains orig) then
        if isOrderingCond cc then
          kudos1001.Add(orig, $"branchless `{Print.item it}` select")
        kudos3001.Add(orig, $"branchless `{Print.item it}` conditional transfer")
    | Op(Loop(_, count', { expr = Sym endLbl })) ->
      hasDoLoop <- true
      if firstDoOrigin = Origin.synthetic then firstDoOrigin <- orig
      match labelIdx.TryGetValue endLbl with
      | true, endIdx when endIdx > i ->
        let bodyOps = opsBetween (i + 1) (endIdx - 1)
        // Check dsp-1004: 8..24 trip count loop doing bit-by-bit btst/bchg + shift
        let isSmallBitCount =
          match count' with
          | Count { expr = Num n } -> n >= 8L && n <= 24L
          | _ -> false
        if isSmallBitCount then
          let hasBitOp =
            bodyOps |> List.exists (fun (_, bIt, _) ->
              match bIt with
              | Op(BitOp((Btst | Bchg | Bset | Bclr), _, _)) -> true
              | _ -> false)
          let hasShiftOp =
            bodyOps |> List.exists (fun (_, bIt, _) ->
              match bIt with
              | Op(ShiftImm _) | Op(ShiftReg _) | Op(Par(Some(Unary((AluUnary.Asl | AluUnary.Asr | AluUnary.Lsl | AluUnary.Lsr), _)), _)) -> true
              | _ -> false)
          if hasBitOp && hasShiftOp then
            bitLoops1004.Add(orig, "DO loop emulating bitwise operation bit-by-bit")
        // Check dsp-2001 / dsp-k2001: temp workset spills inside DO loop
        let spillMoves =
          [ for _, bIt, bOrig in bodyOps do
              match bIt with
              | Op(Par(_, moves)) ->
                for m in moves do
                  if isTempWorksetMove layout m then yield (if bOrig > 0 then bOrig else orig), Print.item bIt
              | _ -> () ]
        if spillMoves.IsEmpty then
          kudos2001.Add(orig, $"DO loop `{endLbl}` has 0 temporary workset spills")
        else
          let sOrig, sText = spillMoves.Head
          loopSpills2001.Add(sOrig, $"{spillMoves.Length} temporary workset spill move(s) in DO loop (first: `{sText.Trim()}`)")
        // Check dsp-4001 / dsp-k4001: Disp addressing vs pointer walking inside DO loop
        let dispMoves =
          [ for _, bIt, _ in bodyOps do
              match bIt with
              | Op(Par(_, moves)) ->
                for m in moves do
                  if isDispMove m then yield Print.item bIt
              | _ -> () ]
        let walkMoves =
          [ for _, bIt, _ in bodyOps do
              match bIt with
              | Op(Par(_, moves)) ->
                for m in moves do
                  if isPointerWalkMove m then yield Print.item bIt
              | _ -> () ]
        if dispMoves.Length >= 3 then
          loopDisps4001.Add(orig, $"{dispMoves.Length} indexed `(rN+offset)` moves in DO loop `{endLbl}`")
        elif dispMoves.IsEmpty && not walkMoves.IsEmpty then
          kudos4001.Add(orig, $"DO loop `{endLbl}` uses indirect pointer walking with 0 indexed `Disp` moves")
      | _ -> ()
    | Op(Branch(Bra, { expr = Sym target })) ->
      match labelIdx.TryGetValue target with
      | true, targetIdx when targetIdx < i ->
        backEdges3002.Add(orig, $"backward software loop branch `bra {target}`")
      | _ -> ()
    | Op(Branch(Bcc cc, { expr = Sym target })) ->
      match labelIdx.TryGetValue target with
      | true, targetIdx when targetIdx > i ->
        let bodyOps = opsBetween (i + 1) (targetIdx - 1)
        if not bodyOps.IsEmpty && bodyOps.Length <= 6 then
          // Check dsp-1003: tst + bge/bpl/bgt over `neg a`/`neg b`
          let isSingleNeg =
            match bodyOps with
            | [ (_, Op(Par(Some(Unary(AluUnary.Neg, _)), [||])), _) ] when (cc = Cond.Ge || cc = Cond.Pl || cc = Cond.Gt) -> true
            | _ -> false
          if isSingleNeg then
            absDiamonds1003.Add(orig, $"branch `{Print.item it}` over `neg` instead of `abs`")
          else
            // Check if this is a short diamond (either fallthrough-to-target or if-else with bra join)
            let diamondOps, isDiamond =
              match List.last bodyOps with
              | _, Op(Branch(Bra, { expr = Sym joinLbl })), _ ->
                match labelIdx.TryGetValue joinLbl with
                | true, joinIdx when joinIdx > targetIdx ->
                  let elseOps = opsBetween (targetIdx + 1) (joinIdx - 1)
                  if elseOps.Length <= 4 then (List.take (bodyOps.Length - 1) bodyOps @ elseOps), true
                  else bodyOps, false
                | _ -> bodyOps, false
              | _ -> bodyOps, (bodyOps.Length <= 3)
            if isDiamond && not diamondOps.IsEmpty then
              let hasStore =
                diamondOps |> List.exists (fun (_, dIt, _) ->
                  match dIt with
                  | Op(Par(_, moves)) -> moves |> Array.exists (function Store _ -> true | _ -> false)
                  | _ -> false)
              let onlyDataMoves =
                diamondOps |> List.forall (fun (_, dIt, _) ->
                  match dIt with
                  | Op(Par(None, moves)) -> moves.Length > 0 && moves |> Array.forall (function Store _ -> false | _ -> true)
                  | Op(Par(Some(Binary(AluOp.Tfr, _, _) | Unary(AluUnary.Clr, _)), [||])) -> true
                  | _ -> false)
              if isOrderingCond cc && onlyDataMoves && not hasStore then
                diamonds1001.Add(orig, $"comparison branch diamond (`{Print.item it}`) selecting value across {diamondOps.Length} move(s)")
                diamonds3001.Add(orig, $"select could not become `Tcc`: lowered as `{Print.item it}` branch diamond ({diamondOps.Length} move(s))")
              else
                let why =
                  if hasStore then "branch body writes memory"
                  elif not onlyDataMoves then "branch body performs ALU or control operation"
                  else $"condition `{cc}` not lowered to Tcc"
                diamonds3001.Add(orig, $"short branch diamond (`{Print.item it}`) could not become `Tcc`: {why}")
      | _ -> ()
    | Op(Jump(Jmp, { expr = Sym target })) ->
      match labelIdx.TryGetValue target with
      | true, targetIdx when targetIdx < i ->
        backEdges3002.Add(orig, $"backward software loop jump `jmp {target}`")
      | _ -> ()
    | _ -> ()

  if sawA1 && sawA0 && not hasRep24Shift && subwordMoveOrigin > 0 then
    kudos1002.Add(subwordMoveOrigin, "direct `a1`/`a0` sub-register moves without 24-bit shift loop")
  if hasDoLoop && backEdges3002.Count = 0 then
    kudos3002.Add(firstDoOrigin, "all loops use hardware `DO` with 0 backward jump loops")

  { missingOrigins  = List.ofSeq missingOrigins
    diamonds1001    = List.ofSeq diamonds1001
    shifts1002      = List.ofSeq shifts1002
    absDiamonds1003 = List.ofSeq absDiamonds1003
    bitLoops1004   = List.ofSeq bitLoops1004
    loopSpills2001  = List.ofSeq loopSpills2001
    diamonds3001    = List.ofSeq diamonds3001
    backEdges3002   = List.ofSeq backEdges3002
    loopDisps4001   = List.ofSeq loopDisps4001
    kudos1001       = List.ofSeq kudos1001
    kudos1002       = List.ofSeq kudos1002
    kudos1003       = List.ofSeq kudos1003
    kudos1004       = List.ofSeq kudos1004
    kudos2001       = List.ofSeq kudos2001
    kudos3001       = List.ofSeq kudos3001
    kudos3002       = List.ofSeq kudos3002
    kudos4001       = List.ofSeq kudos4001 }

/// Collects backend (`asm`) lint and kudos facts into `sink` by comparing `directItems` (optimiser off)
/// against `peepholeItems` (optimiser on).
let collectAsmFacts (sink: Sink) (layout: Layout) (directItems: (Item * int) array) (peepholeItems: (Item * int) array) =
  let d = scanAsm layout directItems
  let p = scanAsm layout peepholeItems
  for orig, detail in p.missingOrigins do
    Sink.record sink (if orig > 0 then orig else Origin.synthetic) AsmLayer "dsp-0001" $"survives|instruction `{detail}` has no provenance origin"
  let recordComparative (id: string) (directRows: (int * string) list) (peepholeRows: (int * string) list) =
    let peepholeLines = peepholeRows |> List.map (fun (o, _) -> Origin.line o) |> Set.ofList
    for orig, detail in peepholeRows do
      Sink.record sink orig AsmLayer id $"survives|{detail}"
    for orig, detail in directRows do
      if not (peepholeLines.Contains (Origin.line orig)) && peepholeRows.IsEmpty then
        Sink.record sink orig AsmLayer id $"cleaned|{detail}"
  recordComparative "dsp-1001" d.diamonds1001 p.diamonds1001
  recordComparative "dsp-1002" d.shifts1002 p.shifts1002
  recordComparative "dsp-1003" d.absDiamonds1003 p.absDiamonds1003
  recordComparative "dsp-1004" d.bitLoops1004 p.bitLoops1004
  recordComparative "dsp-2001" d.loopSpills2001 p.loopSpills2001
  recordComparative "dsp-3001" d.diamonds3001 p.diamonds3001
  recordComparative "dsp-3002" d.backEdges3002 p.backEdges3002
  recordComparative "dsp-4001" d.loopDisps4001 p.loopDisps4001
  for orig, detail in p.kudos1001 do Sink.record sink orig AsmLayer "dsp-k1001" detail
  for orig, detail in p.kudos1002 do Sink.record sink orig AsmLayer "dsp-k1002" detail
  for orig, detail in p.kudos1003 do Sink.record sink orig AsmLayer "dsp-k1003" detail
  for orig, detail in p.kudos1004 do Sink.record sink orig AsmLayer "dsp-k1004" detail
  for orig, detail in p.kudos2001 do Sink.record sink orig AsmLayer "dsp-k2001" detail
  for orig, detail in p.kudos3001 do Sink.record sink orig AsmLayer "dsp-k3001" detail
  for orig, detail in p.kudos3002 do Sink.record sink orig AsmLayer "dsp-k3002" detail
  for orig, detail in p.kudos4001 do Sink.record sink orig AsmLayer "dsp-k4001" detail

/// Records grading-only kudos facts (`dsp-k0001` optimiser-off cycle budget and `dsp-k5001` 0 B .NET allocation).
let recordGradingFacts (sink: Sink) (origin: int) (options: Options) =
  let orig = if origin > 0 then origin else Origin.synthetic
  match options.directCycles, options.declaredBudget with
  | Some dc, Some budget when budget > 0 && dc <= float budget ->
    Sink.record sink orig AsmLayer "dsp-k0001" $"Direct cycles {dc:F0} <= declared budget {budget} with optimiser off"
  | _ -> ()
  match options.netAllocatedBytes with
  | Some 0L ->
    Sink.record sink orig IrLayer "dsp-k5001" "0 B allocated per RENDER block on .NET"
  | _ -> ()

// ---------------------------------------------------------------------------------------------
// Querying warnings, kudos, and per-kernel score from the diagnostics sink.

let private formatWarningMessage (r: Rule) (layer: Layer) (optimiserCleaned: bool) (detail: string) =
  let reliance =
    if optimiserCleaned then
      "speed currently depends on the optimiser cleaning up this pattern; rewrite so speed does not rely on it"
    else
      "not cleaned up by the optimiser (survives in emitted assembly)"
  $"{r.id} [{Layer.name layer}] ({r.anchor}): {r.title} — {detail} ({reliance})"

let private formatKudosMessage (r: Rule) (layer: Layer) (outcome: string) =
  $"{r.kudosId} [{Layer.name layer}] ({r.anchor}): {r.kudosSummary} — verified: {outcome}"

/// Queries warnings from `sink` after the build, applying optional `--lint-layer` filtering.
let queryWarnings (options: Options) (sink: Sink) : Warning array =
  let facts = Sink.toList sink
  // Determine which rule IDs survived in final assembly vs were cleaned up by the optimiser
  let asmSurvivedIds =
    facts
    |> List.choose (fun f ->
      if f.layer = AsmLayer && f.id.StartsWith "dsp-" && not (f.id.StartsWith "dsp-k") && f.data.StartsWith "survives|" then
        Some f.id
      else None)
    |> Set.ofList
  let seen = HashSet<string * Layer * int>()
  [| for f in facts do
       match tryRule f.id with
       | Some r when (options.layer |> Option.forall ((=) f.layer)) ->
         let lineKey = Origin.line f.origin
         if seen.Add((f.id, f.layer, lineKey)) then
           let cleaned, detail =
             if f.layer = AsmLayer then
               if f.data.StartsWith "cleaned|" then true, f.data.Substring "cleaned|".Length
               elif f.data.StartsWith "survives|" then false, f.data.Substring "survives|".Length
               else false, f.data
             else
               // An IR warning whose pattern no longer appears in final Peephole assembly was cleaned up by the compiler/optimiser
               let cleanedByBackend =
                 match f.id with
                 | "dsp-5001" -> false // host .NET boxing is never removed by DSP optimiser
                 | id -> not (asmSurvivedIds.Contains id)
               cleanedByBackend, f.data
           let ranges = Origin.ranges f.origin
           yield
             { id               = r.id
               family           = r.family
               layer            = f.layer
               origin           = f.origin
               ranges           = ranges
               anchor           = r.anchor
               optimiserCleaned = cleaned
               detail           = detail
               message          = formatWarningMessage r f.layer cleaned detail }
       | _ -> () |]

/// Queries verified-outcome kudos from `sink` after the build.
/// Off by default (`options.kudos = false`), and suppressed whenever grading is not bit-exact (`options.bitExact = false`).
let queryKudos (options: Options) (sink: Sink) : Kudos array =
  if not options.kudos || not options.bitExact then [||]
  else
    let facts = Sink.toList sink
    let seen = HashSet<string * Layer * int>()
    [| for f in facts do
         match tryKudosRule f.id with
         | Some r when (options.layer |> Option.forall ((=) f.layer)) ->
           let lineKey = Origin.line f.origin
           if seen.Add((f.id, f.layer, lineKey)) then
             let ranges = Origin.ranges f.origin
             yield
               { id       = r.kudosId
                 mirrorId = r.id
                 family   = r.family
                 layer    = f.layer
                 origin   = f.origin
                 ranges   = ranges
                 anchor   = r.anchor
                 outcome  = f.data
                 message  = formatKudosMessage r f.layer f.data }
         | _ -> () |]

/// Computes the per-kernel authoring score from the queried warnings, kudos, and cycle ratios.
let computeScore (options: Options) (directStaticCost: int64) (optimisedStaticCost: int64) (warnings: Warning array) (kudos: Kudos array) : Score =
  let ratio =
    match options.directCycles, options.optimisedCycles with
    | Some dc, Some oc when oc > 0.0 -> dc / oc
    | _ ->
      if optimisedStaticCost > 0L then float directStaticCost / float optimisedStaticCost else 1.0
  { kudosCount             = if options.bitExact then kudos.Length else 0
    warningCount           = warnings.Length
    optimiserRelianceCount = warnings |> Array.filter _.optimiserCleaned |> Array.length
    directToOptimisedRatio = ratio
    bitExact               = options.bitExact }

// ---------------------------------------------------------------------------------------------
// Measuring .NET host allocation for dsp-k5001 (grading only).

/// Measures the managed bytes allocated on the current thread by one .NET `RENDER` block after warmup.
let measureNetAllocation (target: Target) (k: Kernel) : int64 =
  let layout = Kernel.layout target k
  let zero (t: Type) = Activator.CreateInstance t
  let state : obj | null =
    if k.stateType = typeof<unit> then null
    else
      let initial (p: PropertyInfo) =
        match (layout.field p.Name).ty with
        | TArray n -> box (Array.create n Frac.zero)
        | TLongArray n -> box (Array.create n Long.zero)
        | _ -> zero p.PropertyType
      FSharpValue.MakeRecord(k.stateType, FSharpType.GetRecordFields k.stateType |> Array.map initial)
  let parameters =
    FSharpValue.MakeRecord(
      k.paramsType,
      FSharpType.GetRecordFields k.paramsType |> Array.mapi (fun i _ -> box (Frac.ofRaw (0x100000 + i * 0x11111))))
  let output = Output()
  // Build a strongly-typed delegate so MethodInfo.Invoke's object[] allocation is not counted
  let renderAction : Action =
    let args =
      [| for p in k.render.GetParameters() ->
           if p.ParameterType = k.paramsType then System.Linq.Expressions.Expression.Constant(parameters, k.paramsType) :> System.Linq.Expressions.Expression
           elif p.ParameterType = k.stateType then System.Linq.Expressions.Expression.Constant(state, k.stateType) :> System.Linq.Expressions.Expression
           elif p.ParameterType = typeof<Output> then System.Linq.Expressions.Expression.Constant(output, typeof<Output>) :> System.Linq.Expressions.Expression
           else System.Linq.Expressions.Expression.Default(p.ParameterType) :> System.Linq.Expressions.Expression |]
    let callExpr = System.Linq.Expressions.Expression.Call(k.render, args)
    System.Linq.Expressions.Expression.Lambda<Action>(callExpr).Compile()
  try
    k.init.Invoke(null, [| parameters; state |]) |> ignore
    k.trig.Invoke(null, [| parameters; state |]) |> ignore
    output.Clear()
    renderAction.Invoke()
    output.Clear()
    let before = GC.GetAllocatedBytesForCurrentThread()
    renderAction.Invoke()
    let after = GC.GetAllocatedBytesForCurrentThread()
    max 0L (after - before)
  with _ ->
    0L

// ---------------------------------------------------------------------------------------------
// High-level entry points.

/// Runs IR and ASM fact collection and queries lints and kudos for an `IrKernel`.
let checkIrKernelWith (options: Options) (target: Target) (ik: IrKernel) : Result<ResultReport, string> =
  let sink = Sink.create ()
  collectIrFacts sink ik
  match Compile.generateIrWith Set.empty Set.empty None target ik,
        Optimize.compileIrChoosingWithSink sink Set.empty (Optimize.subroutinesPolicy ()) (Kernel.programLimit target |> Option.map fst) Optimize.Peephole target ik with
  | Error e, _ | _, Error e -> Error e
  | Ok(directC, _), Ok peepholeC ->
    let directZipped = Array.zip directC.items directC.origins
    let peepholeZipped = Array.zip peepholeC.items peepholeC.origins
    collectAsmFacts sink peepholeC.layout directZipped peepholeZipped
    let rootOrigin = Compile.reflected ik.render |> Option.map _.Origin |> Option.defaultValue Origin.synthetic
    recordGradingFacts sink rootOrigin options
    let warnings = queryWarnings options sink
    let kudos = queryKudos options sink
    let dCost, _ = Optimize.staticCost directZipped
    let pCost, _ = Optimize.staticCost peepholeZipped
    let score = computeScore options dCost pCost warnings kudos
    Ok
      { kernel   = ik.name
        warnings = warnings
        kudos    = kudos
        score    = score
        facts    = Sink.toList sink }

/// Runs IR and ASM lints (and optional kudos) on a quotation `Kernel`.
let checkKernelWith (options: Options) (target: Target) (k: Kernel) : Result<ResultReport, string> =
  checkIrKernelWith options target (FromQuotation.kernel k)

/// Default lint check on a quotation `Kernel` (both layers, kudos off).
let checkKernel (target: Target) (k: Kernel) : Result<ResultReport, string> =
  checkKernelWith Options.defaults target k

/// Formats a `ResultReport` as human-readable text.
let formatReport (options: Options) (r: ResultReport) : string =
  let lines = ResizeArray<string>()
  for w in r.warnings do
    let loc =
      match w.ranges with
      | rng :: _ when rng.file <> "" && rng.file <> "<synthetic>" -> $"{IO.Path.GetFileName rng.file}:{rng.line}: "
      | rng :: _ when rng.line > 0 -> $"line {rng.line}: "
      | _ -> ""
    lines.Add $"  warning {loc}{w.message}"
  if options.kudos then
    for k in r.kudos do
      let loc =
        match k.ranges with
        | rng :: _ when rng.file <> "" && rng.file <> "<synthetic>" -> $"{IO.Path.GetFileName rng.file}:{rng.line}: "
        | rng :: _ when rng.line > 0 -> $"line {rng.line}: "
        | _ -> ""
      lines.Add $"  kudos   {loc}{k.message}"
    let status = if r.score.bitExact then "bit-exact" else "MISMATCH (kudos suppressed)"
    lines.Add $"  score   {r.kernel}: {r.score.kudosCount} kudos, {r.score.warningCount} warning(s) ({r.score.optimiserRelianceCount} optimiser-reliant), direct/optimised ratio x{r.score.directToOptimisedRatio:F2} ({status})"
  String.concat "\n" lines

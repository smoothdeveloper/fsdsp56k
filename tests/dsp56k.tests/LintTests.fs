module dsp56k.tests.LintTests

open System
open System.IO
open Expecto
open dsp56k
open dsp56k.kernel
open dsp56k.kernel.Ir
open dsp56k.tests.Fixtures

[<ReflectedDefinition>]
module LintProbes =
  type P =
    { a : Frac
      b : Frac
      c : Frac }

  type S =
    { mutable w   : Frac
      mutable acc : Long
      [<Length 8>]
      buf         : Frac array }

  let init (_: P) (s: S) =
    s.w <- Frac.zero
    s.acc <- Long.zero
    for i = 0 to 7 do
      s.buf[i] <- Frac.zero

  let trig (_: P) (_: S) = ()

  // --- dsp-1001: manual comparison select vs Frac.Clamp / Frac.Min / Frac.Max ---
  let fire1001 (p: P) (s: S) (out: Output) =
    for _ = 0 to 31 do
      let x = p.a
      let y = if x > p.b then p.b else x
      s.w <- y
      out.Write y

  let clean1001 (p: P) (s: S) (out: Output) =
    for _ = 0 to 31 do
      let y = Frac.Clamp(p.a, p.b, p.c)
      s.w <- Frac.Min(y, p.c)
      out.Write(Frac.Max(s.w, p.b))

  // --- dsp-1002: manual 24-bit shift pack vs Long.pack / Long.hi / Long.lo ---
  let fire1002 (p: P) (s: S) (out: Output) =
    for _ = 0 to 31 do
      let packed = (Acc.ofFrac p.a >>> 24) + Acc.ofFrac p.b
      s.acc <- Long.ofAcc packed
      out.Write(Acc.hi packed)

  let clean1002 (p: P) (s: S) (out: Output) =
    for _ = 0 to 31 do
      s.acc <- Long.pack p.a p.b
      let a = Acc.ofLong s.acc
      s.w <- Long.lo s.acc
      out.Write(Acc.sat (Acc.ofFrac (Long.hi s.acc) + Acc.hi a))

  // --- dsp-1003: manual sign branch over neg vs Acc.abs / Frac.abs ---
  let fire1003 (p: P) (s: S) (out: Output) =
    for _ = 0 to 31 do
      let x = Acc.ofFrac p.a
      let m = if x < Acc.zero then -x else x
      let y = Acc.sat m
      s.w <- y
      out.Write y

  let clean1003 (p: P) (s: S) (out: Output) =
    for _ = 0 to 31 do
      let m = Acc.abs (Acc.ofFrac p.a)
      let y = Frac.abs p.b
      s.w <- y
      out.Write(Acc.sat (m + y))

  // --- dsp-1004: bit-by-bit loop vs hardware bitwise ---
  let fire1004 (p: P) (s: S) (out: Output) =
    let mutable acc = Acc.ofFrac p.a
    for _ = 0 to 23 do
      if Frac.bit 23 (Acc.hi acc) then
        acc <- (acc <<< 1) + Acc.ofFrac p.b
      else
        acc <- acc <<< 1
    let res = Acc.sat acc
    s.w <- res
    for _ = 0 to 31 do
      out.Write res

  let clean1004 (p: P) (s: S) (out: Output) =
    for _ = 0 to 31 do
      let res = Acc.hi (Acc.andHi 0x00ff0f (Acc.ofFrac p.a))
      s.w <- res
      out.Write res

  // --- dsp-2001: nested compound Acc in loop vs linear Acc chain ---
  let fire2001 (p: P) (s: S) (out: Output) =
    for _ = 0 to 31 do
      let y = Acc.sat ((p.a * p.b) + (p.b * p.c))
      s.w <- y
      out.Write y

  let clean2001 (p: P) (s: S) (out: Output) =
    for _ = 0 to 31 do
      let y = Acc.sat (Acc.mac (p.a * p.b) p.b p.c)
      s.w <- y
      out.Write y

  // --- dsp-3002: WhileLoop vs hardware DO ForIntegerRangeLoop ---
  let fire3002 (p: P) (s: S) (out: Output) =
    let mutable k = p.a
    while k > Frac.zero do
      k <- Acc.sat (Acc.ofFrac k - p.b)
    s.w <- k
    for _ = 0 to 31 do
      out.Write k

  let clean3002 (p: P) (s: S) (out: Output) =
    for _ = 0 to 31 do
      s.w <- p.a
      out.Write p.a

  // --- dsp-4001: >=3 indexed array accesses in loop vs Cursor ---
  let fire4001 (p: P) (s: S) (out: Output) =
    for i = 0 to 3 do
      let a = s.buf[i]
      let b = s.buf[i + 1]
      let c = s.buf[i + 2]
      let sum = Acc.sat (Acc.ofFrac a + b + c + p.a)
      s.buf[i] <- sum
    for _ = 0 to 31 do
      out.Write s.buf[0]

  let clean4001 (p: P) (s: S) (out: Output) =
    let rd = Cursor.linearIn s.buf Frac.zero 1
    let mutable acc = Acc.ofFrac p.a
    for _ = 0 to 3 do
      acc <- acc + rd.Next()
    let y = Acc.sat acc
    s.w <- y
    for _ = 0 to 31 do
      out.Write y

  // --- dsp-5001: generic comparison on Frac struct inside RENDER boxes on .NET ---
  let fire5001 (p: P) (s: S) (out: Output) =
    for _ = 0 to 31 do
      let y = if p.a = p.b then p.a else p.b
      s.w <- y
      out.Write y

  let fire5001Min (p: P) (s: S) (out: Output) =
    for _ = 0 to 31 do
      let y = min p.a p.b
      s.w <- y
      out.Write y

  let clean5001 (p: P) (s: S) (out: Output) =
    for _ = 0 to 31 do
      let y = Frac.Clamp(p.a, p.b, p.c)
      s.w <- Frac.Min(y, p.b)
      out.Write(Frac.Max(s.w, p.c))

let private probeKernel name render =
  Kernel.define name <@ LintProbes.init @> <@ LintProbes.trig @> render

let private checkWith opts name render =
  match Lint.checkKernelWith opts Kernel.mds (probeKernel name render) with
  | Ok r -> r
  | Error e -> failtest $"Lint.checkKernelWith failed on {name}: {e}"

let private check name render = checkWith Lint.Options.defaults name render

let private docsAnchors () =
  let path = Path.Combine(repoRoot, "docs", "lints.md")
  Expect.isTrue (File.Exists path) $"expected docs/lints.md at {path}"
  File.ReadAllLines path
  |> Array.choose (fun l ->
    if l.StartsWith "## dsp-" then
      Some("docs/lints.md#" + l.Substring(3).Trim())
    else None)
  |> Set.ofArray

let provenanceTests =
  testList "provenance" [
    testCase "every IR node and emitted instruction has Origin > 0" <| fun () ->
      let k = samples.kernels.Sine.kernel
      let ik = FromQuotation.kernel k
      for label, exprOpt in [ "init", Compile.reflected ik.init; "trig", Compile.reflected ik.trig; "render", Compile.reflected ik.render ] do
        let expr = Expect.wantSome exprOpt $"expected reflected {label}"
        let rec checkExpr (e: Expr) =
          Expect.isGreaterThan e.Origin 0 $"IR node {e} in {label} must have Origin > 0"
          match e with
          | ExprShape.ShapeVar _ -> ()
          | ExprShape.ShapeLambda(_, body) -> checkExpr body
          | ExprShape.ShapeCombination(_, args) -> List.iter checkExpr args
        checkExpr expr
      let directC = Expect.wantOk (Compile.compile Kernel.mds k) "direct compile"
      Expect.equal directC.origins.Length directC.items.Length "direct origins length matches items"
      for i = 0 to directC.items.Length - 1 do
        match directC.items[i] with
        | Op _ -> Expect.isGreaterThan directC.origins[i] 0 $"direct Op at {i} ({Print.item directC.items[i]}) must have origin > 0"
        | _ -> ()
      let peepC = Expect.wantOk (Optimize.compile Optimize.Peephole Kernel.mds k) "peephole compile"
      Expect.equal peepC.origins.Length peepC.items.Length "peephole origins length matches items"
      for i = 0 to peepC.items.Length - 1 do
        match peepC.items[i] with
        | Op _ -> Expect.isGreaterThan peepC.origins[i] 0 $"peephole Op at {i} ({Print.item peepC.items[i]}) must have origin > 0"
        | _ -> ()

    testCase "Origin.merge combines source ranges without duplicates" <| fun () ->
      let o1 = Origin.ofRange { file = "a.fs"; line = 10 }
      let o2 = Origin.ofRange { file = "a.fs"; line = 14 }
      let merged = Origin.merge o1 o2
      Expect.isGreaterThan merged 0 "merged origin > 0"
      let rs = Origin.ranges merged
      Expect.equal (rs |> List.map _.line) [ 10; 14 ] "merged origin preserves both source lines in order"
      Expect.equal (Origin.merge o1 o1) o1 "merging identical origin is idempotent"

    testCase "optimiser raises KernelError when a pass drops an Op origin" <| fun () ->
      let validInput = [| Op Nop, Origin.synthetic |]
      let buggyPass (_: (Item * int) array) = [| Op Nop, 0 |]
      let raised =
        try
          Optimize.runPassChecked "buggyPass" buggyPass validInput |> ignore
          false
        with Kernel.KernelError msg ->
          msg.Contains "dropped provenance origin"
      Expect.isTrue raised "expected Optimize.runPassChecked to reject Op with origin <= 0"

    testCase "opaque Sink API records, snapshots, iterates, and ignores Sink.none" <| fun () ->
      let o1 = Origin.ofRange { file = "a.fs"; line = 10 }
      let o2 = Origin.ofRange { file = "a.fs"; line = 20 }
      Expect.isFalse (Sink.isActive Sink.none) "Sink.none is inactive"
      Sink.record Sink.none o1 IrLayer "dsp-1001" "ignored"
      Expect.isEmpty (Sink.toList Sink.none) "Sink.none discards facts"
      let s = Sink.create ()
      Expect.isTrue (Sink.isActive s) "Sink.create () is active"
      Sink.record s o1 IrLayer "dsp-1001" "kept"
      let rewind = Sink.snapshot s
      Sink.record s o2 AsmLayer "dsp-3001" "rolled back"
      rewind ()
      let facts = Sink.toList s
      Expect.equal facts.Length 1 "snapshot rewind removes speculative facts"
      Expect.equal facts.Head.id "dsp-1001" "preserved fact id"
      let mutable count = 0
      Sink.iter (fun _ -> count <- count + 1) s
      Expect.equal count 1 "Sink.iter visits kept facts"
  ]

let ruleTests =
  testList "rules" [
    testCase "all 10 lint rules and 10 mirror kudos resolve to docs/lints.md#dsp-xxxx anchors" <| fun () ->
      let anchors = docsAnchors ()
      Expect.equal Lint.rules.Length 10 "expected 10 lint rules"
      for r in Lint.rules do
        Expect.isTrue (anchors.Contains r.anchor) $"anchor {r.anchor} for {r.id}/{r.kudosId} missing in docs/lints.md"

    testCase "dsp-0001 and dsp-k0001: provenance check and Direct budget kudos" <| fun () ->
      let sink = Sink.create ()
      let layout = Kernel.layout Kernel.mds (probeKernel "clean3002" <@ LintProbes.clean3002 @>)
      let badAsm = [| Op Nop, 0 |]
      Lint.collectAsmFacts sink layout badAsm badAsm
      let ws = Lint.queryWarnings Lint.Options.defaults sink
      Expect.isTrue (ws |> Array.exists (fun w -> w.id = "dsp-0001" && w.layer = AsmLayer && w.anchor = "docs/lints.md#dsp-0001")) "dsp-0001 fires on missing origin"
      let rClean = check "clean3002" <@ LintProbes.clean3002 @>
      Expect.isFalse (rClean.warnings |> Array.exists (fun w -> w.id = "dsp-0001")) "clean kernel has no dsp-0001"
      let kPass = checkWith { Lint.Options.defaults with kudos = true; directCycles = Some 1200.0; declaredBudget = Some 2000 } "clean3002" <@ LintProbes.clean3002 @>
      Expect.isTrue (kPass.kudos |> Array.exists (fun k -> k.id = "dsp-k0001" && k.anchor = "docs/lints.md#dsp-0001")) "dsp-k0001 fires when Direct cycles <= budget"
      let kFail = checkWith { Lint.Options.defaults with kudos = true; directCycles = Some 2500.0; declaredBudget = Some 2000 } "clean3002" <@ LintProbes.clean3002 @>
      Expect.isFalse (kFail.kudos |> Array.exists (fun k -> k.id = "dsp-k0001")) "dsp-k0001 does not fire when Direct cycles > budget"

    testCase "dsp-1001 and dsp-k1001: comparison select vs branchless Tcc" <| fun () ->
      let rFire = check "fire1001" <@ LintProbes.fire1001 @>
      let w1001 = rFire.warnings |> Array.filter (fun w -> w.id = "dsp-1001")
      Expect.isNonEmpty w1001 "dsp-1001 fires on manual comparison select"
      for w in w1001 do
        Expect.equal w.anchor "docs/lints.md#dsp-1001" "dsp-1001 anchor"
        Expect.isTrue (w.message.Contains "docs/lints.md#dsp-1001") "message includes anchor"
      let rClean = checkWith { Lint.Options.defaults with kudos = true } "clean1001" <@ LintProbes.clean1001 @>
      Expect.isEmpty (rClean.warnings |> Array.filter (fun w -> w.id = "dsp-1001")) "clean1001 has no dsp-1001"
      Expect.isTrue (rClean.kudos |> Array.exists (fun k -> k.id = "dsp-k1001" && k.anchor = "docs/lints.md#dsp-1001")) "clean1001 earns dsp-k1001 from compiled Frac.Clamp/Min/Max"
      // Verify dsp-k1001 is suppressed when the same origin also emitted a branch
      let layout = Kernel.layout Kernel.mds (probeKernel "clean1001" <@ LintProbes.clean1001 @>)
      let orig = Origin.ofRange { file = "a.fs"; line = 20 }
      let branchedAsm = [| Op(Branch(Bcc Cond.Lt, { expr = Sym "L1"; force = Auto })), orig; Op(Tcc(Cond.Lt, Some(X0, dsp56k.Acc.A), None)), orig; Label "L1", orig |]
      let sinkBr = Sink.create ()
      Lint.collectAsmFacts sinkBr layout branchedAsm branchedAsm
      let ksBr = Lint.queryKudos { Lint.Options.defaults with kudos = true } sinkBr
      Expect.isFalse (ksBr |> Array.exists (fun k -> k.id = "dsp-k1001")) "dsp-k1001 does not fire if same origin also emitted a branch"

    testCase "dsp-1002 and dsp-k1002: 24-bit shift pack vs direct sub-register moves" <| fun () ->
      let rFire = check "fire1002" <@ LintProbes.fire1002 @>
      let w1002 = rFire.warnings |> Array.filter (fun w -> w.id = "dsp-1002")
      Expect.isNonEmpty w1002 "dsp-1002 fires on 24-bit shift pack"
      Expect.isTrue (w1002 |> Array.exists (fun w -> w.layer = IrLayer && w.optimiserCleaned)) "IR dsp-1002 notes when optimiser cleans up 24-bit shift"
      let rClean = checkWith { Lint.Options.defaults with kudos = true } "clean1002" <@ LintProbes.clean1002 @>
      Expect.isEmpty (rClean.warnings |> Array.filter (fun w -> w.id = "dsp-1002")) "clean1002 has no dsp-1002"
      Expect.isTrue (rClean.kudos |> Array.exists (fun k -> k.id = "dsp-k1002" && k.anchor = "docs/lints.md#dsp-1002")) "clean1002 earns dsp-k1002"

    testCase "dsp-1003 and dsp-k1003: sign branch over neg vs hardware abs" <| fun () ->
      let rFire = check "fire1003" <@ LintProbes.fire1003 @>
      let w1003 = rFire.warnings |> Array.filter (fun w -> w.id = "dsp-1003")
      Expect.isNonEmpty w1003 "dsp-1003 fires on manual sign branch over neg"
      let rClean = checkWith { Lint.Options.defaults with kudos = true } "clean1003" <@ LintProbes.clean1003 @>
      Expect.isEmpty (rClean.warnings |> Array.filter (fun w -> w.id = "dsp-1003")) "clean1003 has no dsp-1003"
      Expect.isTrue (rClean.kudos |> Array.exists (fun k -> k.id = "dsp-k1003" && k.anchor = "docs/lints.md#dsp-1003")) "clean1003 earns dsp-k1003 from compiled Acc.abs / Frac.abs"

    testCase "dsp-1004 and dsp-k1004: bit-by-bit loop vs hardware bitwise" <| fun () ->
      let rFire = check "fire1004" <@ LintProbes.fire1004 @>
      let w1004 = rFire.warnings |> Array.filter (fun w -> w.id = "dsp-1004")
      Expect.isNonEmpty w1004 "dsp-1004 fires on 24-trip bit test + shift loop"
      let rClean = checkWith { Lint.Options.defaults with kudos = true } "clean1004" <@ LintProbes.clean1004 @>
      Expect.isEmpty (rClean.warnings |> Array.filter (fun w -> w.id = "dsp-1004")) "clean1004 has no dsp-1004"
      Expect.isTrue (rClean.kudos |> Array.exists (fun k -> k.id = "dsp-k1004" && k.anchor = "docs/lints.md#dsp-1004")) "clean1004 earns dsp-k1004"

    testCase "dsp-2001 and dsp-k2001: compound Acc spill in DO loop vs zero-spill loop" <| fun () ->
      let rFire = check "fire2001" <@ LintProbes.fire2001 @>
      let w2001 = rFire.warnings |> Array.filter (fun w -> w.id = "dsp-2001")
      Expect.isNonEmpty w2001 "dsp-2001 fires on nested compound Acc in loop"
      let rClean = checkWith { Lint.Options.defaults with kudos = true } "clean2001" <@ LintProbes.clean2001 @>
      Expect.isEmpty (rClean.warnings |> Array.filter (fun w -> w.id = "dsp-2001")) "clean2001 has no dsp-2001"
      Expect.isTrue (rClean.kudos |> Array.exists (fun k -> k.id = "dsp-k2001" && k.anchor = "docs/lints.md#dsp-2001")) "clean2001 earns dsp-k2001"

    testCase "dsp-3001 and dsp-k3001: branch diamond vs Tcc" <| fun () ->
      let rFire = check "fire1001" <@ LintProbes.fire1001 @>
      let w3001 = rFire.warnings |> Array.filter (fun w -> w.id = "dsp-3001")
      Expect.isNonEmpty w3001 "dsp-3001 fires on short branch diamond"
      let rClean = checkWith { Lint.Options.defaults with kudos = true } "clean1001" <@ LintProbes.clean1001 @>
      Expect.isEmpty (rClean.warnings |> Array.filter (fun w -> w.id = "dsp-3001")) "clean1001 has no dsp-3001"
      Expect.isTrue (rClean.kudos |> Array.exists (fun k -> k.id = "dsp-k3001" && k.anchor = "docs/lints.md#dsp-3001")) "clean1001 earns dsp-k3001 from compiled Tcc"

    testCase "dsp-3002 and dsp-k3002: software backward branch loop vs hardware DO loop" <| fun () ->
      let rFire = check "fire3002" <@ LintProbes.fire3002 @>
      let w3002 = rFire.warnings |> Array.filter (fun w -> w.id = "dsp-3002")
      Expect.isNonEmpty w3002 "dsp-3002 fires on WhileLoop / backward branch loop"
      let rClean = checkWith { Lint.Options.defaults with kudos = true } "clean3002" <@ LintProbes.clean3002 @>
      Expect.isEmpty (rClean.warnings |> Array.filter (fun w -> w.id = "dsp-3002")) "clean3002 has no dsp-3002"
      Expect.isTrue (rClean.kudos |> Array.exists (fun k -> k.id = "dsp-k3002" && k.anchor = "docs/lints.md#dsp-3002")) "clean3002 earns dsp-k3002"

    testCase "dsp-4001 and dsp-k4001: indexed Disp in DO loop vs Cursor pointer walking" <| fun () ->
      let rFire = check "fire4001" <@ LintProbes.fire4001 @>
      let w4001 = rFire.warnings |> Array.filter (fun w -> w.id = "dsp-4001")
      Expect.isNonEmpty w4001 "dsp-4001 fires on >= 3 indexed accesses in DO loop"
      let rClean = checkWith { Lint.Options.defaults with kudos = true } "clean4001" <@ LintProbes.clean4001 @>
      Expect.isEmpty (rClean.warnings |> Array.filter (fun w -> w.id = "dsp-4001")) "clean4001 has no dsp-4001"
      Expect.isTrue (rClean.kudos |> Array.exists (fun k -> k.id = "dsp-k4001" && k.anchor = "docs/lints.md#dsp-4001")) "clean4001 earns dsp-k4001"

    testCase "dsp-5001 and dsp-k5001: .NET boxing in RENDER vs 0 B allocation" <| fun () ->
      let rFire = check "fire5001" <@ LintProbes.fire5001 @>
      let w5001 = rFire.warnings |> Array.filter (fun w -> w.id = "dsp-5001")
      Expect.isNonEmpty w5001 "dsp-5001 fires on generic = inside RENDER"
      let rFireMin = checkWith { Lint.Options.defaults with kudos = true } "fire5001Min" <@ LintProbes.fire5001Min @>
      Expect.isNonEmpty (rFireMin.warnings |> Array.filter (fun w -> w.id = "dsp-5001")) "dsp-5001 fires on FSharp.Core min on Frac (boxes on .NET)"
      Expect.isEmpty (rFireMin.warnings |> Array.filter (fun w -> w.id = "dsp-1001")) "FSharp.Core min lowers to branchless Tcc without dsp-1001"
      Expect.isTrue (rFireMin.kudos |> Array.exists (fun k -> k.id = "dsp-k1001")) "FSharp.Core min earns dsp-k1001 on DSP"
      let cleanK = probeKernel "clean5001" <@ LintProbes.clean5001 @>
      let allocClean = Lint.measureNetAllocation Kernel.mds cleanK
      Expect.equal allocClean 0L "clean5001 (Frac.Min/Max/Clamp) allocates 0 B on .NET"
      let rClean = checkWith { Lint.Options.defaults with kudos = true; netAllocatedBytes = Some allocClean } "clean5001" <@ LintProbes.clean5001 @>
      Expect.isEmpty (rClean.warnings |> Array.filter (fun w -> w.id = "dsp-5001")) "clean5001 has no dsp-5001"
      Expect.isTrue (rClean.kudos |> Array.exists (fun k -> k.id = "dsp-k5001" && k.anchor = "docs/lints.md#dsp-5001")) "0 B .NET allocation earns dsp-k5001"
      let rAlloc = checkWith { Lint.Options.defaults with kudos = true; netAllocatedBytes = Some 24L } "clean5001" <@ LintProbes.clean5001 @>
      Expect.isFalse (rAlloc.kudos |> Array.exists (fun k -> k.id = "dsp-k5001")) "non-zero allocation does not earn dsp-k5001"

    testCase "layer filter (--lint-layer ir | asm) and anti-gaming (bitExact = false suppresses kudos)" <| fun () ->
      let irOnly = checkWith { Lint.Options.defaults with layer = Some IrLayer; kudos = true } "fire1001" <@ LintProbes.fire1001 @>
      Expect.isNonEmpty irOnly.warnings "ir filter returns IR warnings"
      Expect.isTrue (irOnly.warnings |> Array.forall (fun w -> w.layer = IrLayer)) "all warnings under --lint-layer ir have IrLayer"
      let asmOnly = checkWith { Lint.Options.defaults with layer = Some AsmLayer; kudos = true } "fire1001" <@ LintProbes.fire1001 @>
      Expect.isNonEmpty asmOnly.warnings "asm filter returns ASM warnings"
      Expect.isTrue (asmOnly.warnings |> Array.forall (fun w -> w.layer = AsmLayer)) "all warnings under --lint-layer asm have AsmLayer"
      let mismatch = checkWith { Lint.Options.defaults with kudos = true; bitExact = false; directCycles = Some 500.0; declaredBudget = Some 2000; netAllocatedBytes = Some 0L } "clean1002" <@ LintProbes.clean1002 @>
      Expect.isEmpty mismatch.kudos "bitExact = false suppresses all kudos"
      Expect.equal mismatch.score.kudosCount 0 "score.kudosCount is 0 when bitExact = false"
      let defaultOff = check "clean1002" <@ LintProbes.clean1002 @>
      Expect.isEmpty defaultOff.kudos "kudos are off by default"
  ]

let corpusTests =
  testList "public-corpus" [
    testCase "emitted assembly is identical with lints and kudos on vs off across all public samples" <| fun () ->
      for s in samples.kernels.Grading.samples do
        let cOff = Expect.wantOk (Optimize.compile Optimize.Peephole Kernel.mds s.kernel) $"compile {s.kernel.name} off"
        let sink = Sink.create ()
        let cOn = Expect.wantOk (Optimize.compileWithSink sink Optimize.Peephole Kernel.mds s.kernel) $"compile {s.kernel.name} on"
        let _ = Expect.wantOk (Lint.checkKernelWith { Lint.Options.defaults with kudos = true } Kernel.mds s.kernel) $"lint {s.kernel.name}"
        Expect.equal cOn.items cOff.items $"emitted items for {s.kernel.name} must be identical with sink on vs off"

    testCase "public sample kernels match golden/public-lints.tsv baseline with zero unexpected warnings" <| fun () ->
      let goldenPath = Path.Combine(repoRoot, "tests", "dsp56k.tests", "golden", "public-lints.tsv")
      let rows =
        [| yield "kernel\tid\tlayer\toptimiser_cleaned\tline"
           for s in samples.kernels.Grading.samples do
             let r = Expect.wantOk (Lint.checkKernel Kernel.mds s.kernel) $"checkKernel {s.kernel.name}"
             for w in r.warnings |> Array.sortBy (fun w -> w.id, Layer.name w.layer, Origin.line w.origin) do
               yield $"{s.kernel.name}\t{w.id}\t{Layer.name w.layer}\t{w.optimiserCleaned}\t{Origin.line w.origin}" |]
      let actual = String.concat "\n" rows + "\n"
      if Environment.GetEnvironmentVariable "FSDSP56K_UPDATE_GOLDEN" = "1" || not (File.Exists goldenPath) then
        Directory.CreateDirectory(Path.GetDirectoryName goldenPath) |> ignore
        File.WriteAllText(goldenPath, actual)
      let expected = File.ReadAllText(goldenPath).Replace("\r\n", "\n")
      Expect.equal actual expected "public sample lints must match tests/dsp56k.tests/golden/public-lints.tsv"
  ]

let tests =
  testList "lint" [
    provenanceTests
    ruleTests
    corpusTests
  ]

# Authoring Lints & Kudos (`dsp-xxxx` / `dsp-kxxxx`)

The `fsdsp56k` kernel toolchain includes non-invasive authoring diagnostics across two layers:

- **`ir` layer**: inspects the quotation/typed-tree `Ir.Expr` before instruction emission.
- **`asm` layer**: compares unoptimised (`Direct`) and optimised (`Peephole`) instruction streams after compilation.

Every warning reports its stable code (`dsp-xxxx`), layer (`[ir]` or `[asm]`), source location, whether the pattern was cleaned up by the optimiser (`optimiserCleaned`) or survives in emitted assembly, and an anchor into this reference (`docs/lints.md#dsp-xxxx`).

When grading with `--kudos` (`fsdsp56k grade --kudos <kernel>`), each lint rule also has a mirror kudos (`dsp-kxxxx`) sharing the same `#dsp-xxxx` section below. Kudos fire **only** on a verified compiler/emulator outcome (and are suppressed whenever grading is not bit-exact against .NET).

Use `--lint-layer ir` or `--lint-layer asm` on `fsdsp56k grade` to filter diagnostics to a single layer.

---

## dsp-0001

- **Lint**: `dsp-0001` (`[asm]`) — Emitted instruction lacks a provenance origin (`Origin <= 0`).
- **Kudos**: `dsp-k0001` (`[asm]`) — Unoptimised (`Direct`) cycle count already meets the declared budget.

### Why it matters

Every `Ir.Expr` node and every emitted DSP56300 instruction carries a non-zero provenance `Origin` handle back to the F# source range(s) that produced it. When peephole or loop passes fuse instructions, their origins are merged via `Origin.merge`. `dsp-0001` guards this invariant.

Its mirror kudos `dsp-k0001` rewards kernels whose unoptimised (`Direct`) `TRIG + RENDER` cycle count already stays within the machine's declared cycle budget, meaning the kernel's real-time performance does not depend on peephole passes.

---

## dsp-1001

- **Lint**: `dsp-1001` (`[ir]`, `[asm]`) — Comparison select diamond (`if a < b then a else b`) lowered to conditional branch instead of `Tcc`.
- **Kudos**: `dsp-k1001` (`[asm]`) — Branchless `Tcc` conditional transfer emitted for an ordering select (`tlt`, `tgt`, `tge`, `tle`, `tpl`, `tmi`) with zero branch instructions from that origin.

### Pattern

```fsharp
// Fires dsp-1001: comparison select diamond emits cmp + bcc + bra
let clipped =
  if x > hi then hi
  elif x < lo then lo
  else x
```

### Recommended rewrite (compiles today)

```fsharp
// Branchless cmp + tlt + cmp + tgt on DSP, and 0 B .NET allocation:
let clipped = Frac.Clamp(x, lo, hi)
let loPick  = Frac.Min(a, b)
let hiPick  = Frac.Max(a, b)
```

`Frac.Min`, `Frac.Max`, `Frac.Clamp` (and curried `Frac.min`, `Frac.max`, `Frac.clamp`), along with `Acc` and `Long` counterparts and `FSharp.Core.Operators.min`/`max`, lower directly to branchless `cmp` + `tlt`/`tgt` conditional transfers. On .NET, `Frac.Min`/`Max`/`Clamp` (and `Acc`/`Long` counterparts) compare `.Raw` directly (`0 B` allocation). `System.Math.Min`/`Max`/`Clamp` are supported for compile-time constants.

---

## dsp-1002

- **Lint**: `dsp-1002` (`[ir]`, `[asm]`) — Manual 24-bit shift/pack (`<<< 24`, `>>> 24`, `rep #24` + shift, or `Acc.bclr 23` packing) instead of sub-register conversions.
- **Kudos**: `dsp-k1002` (`[asm]`) — Direct `a1` / `a0` sub-register moves emitted with zero `rep #24` shift loops.

### Pattern

Assembling or splitting a 48-bit `Long` value by shifting an `Acc` 24 bits (`acc >>> 24` or `acc <<< 24`) and clearing bit 23 (`Acc.bclr 23`) emits a multi-instruction shift sequence in direct codegen (currently cleaned up by the optimiser, so speed relies on peephole passes).

### Recommended rewrite (compiles today)

```fsharp
// Pack two 24-bit Frac words directly into a 48-bit Long, and read upper/lower words:
s.pair <- Long.pack hi lo
let hiWord = Long.hi s.pair
let loWord = Long.lo s.pair

// Or convert between 56-bit Acc and 48-bit Long:
s.acc <- Long.ofAcc acc
let restored = Acc.ofLong s.acc
```

---

## dsp-1003

- **Lint**: `dsp-1003` (`[ir]`, `[asm]`) — Sign-branch negation diamond (`if x < Acc.zero then -x else x`) instead of hardware `abs`.
- **Kudos**: `dsp-k1003` (`[asm]`) — Single-cycle hardware `abs a` / `abs b` instruction emitted.

### Pattern

```fsharp
// Fires dsp-1003: emits tst + bge + neg branch diamond
let mag = if x < Acc.zero then -x else x
```

### Recommended rewrite (compiles today)

```fsharp
// Emits single-word, 1-cycle hardware `abs a`:
let mag  = Acc.abs x
let fmag = Frac.abs y
// FSharp.Core `abs x` on Acc or Frac also lowers to hardware `abs a`:
let mag2 = abs x
```

---

## dsp-1004

- **Lint**: `dsp-1004` (`[ir]`, `[asm]`) — Bit-by-bit loop (`for b = 0 to 23` with `Frac.bit` / bit ops + shift) emulating a wordwise bitwise or LFSR operation.
- **Kudos**: `dsp-k1004` (`[asm]`) — Single-cycle hardware `eor`, `or`, `and`, or `not` instruction emitted.

### Pattern

Looping over individual bits with `Frac.bit` (`btst`) and shifts to compute masks or LFSR steps costs dozens of cycles per sample.

### Recommended rewrite (compiles today)

Use the bitwise and LFSR intrinsics supported by the compiler today:

```fsharp
// High-word AND mask on an accumulator (`and #mask,a`):
let masked = Acc.hi (Acc.andHi 0x00ff0f (Acc.ofFrac x))

// Single-step Galois LFSR (`lsl a` + conditional `eor #taps,a`):
let nextState = Frac.lfsr 0x400007 state
```

- **Planned**: General wordwise `eor` / `or` / `not` intrinsics if corpus counts justify them.

---

## dsp-2001

- **Lint**: `dsp-2001` (`[ir]`, `[asm]`) — Nested compound `Acc` subexpression or temporary workset spill (`X:(r6+k)` / `Y:(r6+k)`) inside a `DO` loop.
- **Kudos**: `dsp-k2001` (`[asm]`) — `DO` loop body has zero temporary workset spill moves.

### Pattern

When a binary `Acc` expression has two compound `Acc` operands inside a loop (such as `(a * b) + (c * d)`), or when too many live temporaries cross a loop body, direct codegen must spill an intermediate 56-bit accumulator to temporary workset memory inside the hot loop.

### Recommended rewrite (compiles today)

Sequence accumulator operations linearly with `Acc.mac` or `acc - x * y` (which lowers to `mac -x0,y0,a`) and hoist loop-invariant subexpressions outside the `for` loop:

```fsharp
// Linear MAC chain stays in accumulator A without spilling:
let sum = Acc.mac (p.a * p.b) p.b p.c
let diff = (p.a * p.b) - p.b * p.c
```

---

## dsp-3001

- **Lint**: `dsp-3001` (`[ir]`, `[asm]`) — Short branch diamond in emitted assembly that could not be lowered to a conditional transfer (`Tcc`).
- **Kudos**: `dsp-k3001` (`[asm]`) — Branchless `Tcc` instruction emitted with zero branch instructions from the same origin.

### Why a branch diamond fails `Tcc` lowering

The diagnostic message states why the diamond remained a branch:
1. **Branch body writes memory** (`Store` inside a branch arm cannot be predicated with `Tcc`).
2. **Branch body performs ALU or control operations** (`Tcc` only transfers registers).
3. **Condition code is not lowered to `Tcc` yet** (for ordering selects, use `Frac.Min`/`Max`/`Clamp` or `min`/`max`; general `select -> Tcc` pass A4 is planned in Step 3).

---

## dsp-3002

- **Lint**: `dsp-3002` (`[ir]`, `[asm]`) — Loop could not use a hardware `DO` loop (`WhileLoop` or backward `bra`/`jmp` software loop back-edge).
- **Kudos**: `dsp-k3002` (`[asm]`) — Every loop in the kernel lowers to a zero-overhead hardware `DO` loop with zero backward branch/jump loops.

### Pattern

`while` loops whose bounds or step cannot be canonicalised at compile time lower to software compare-and-branch loops, paying branch overhead on every iteration.

### Recommended rewrite (compiles today)

```fsharp
// Counted integer range loop lowers to hardware `do #count,label`:
for i = 0 to 31 do
  out.Write p.a
```

---

## dsp-4001

- **Lint**: `dsp-4001` (`[ir]`, `[asm]`) — Three or more indexed array/workset accesses (`s.buf[i]` -> `(rN+offset)`) in a `DO` loop instead of `Cursor` pointer walking.
- **Kudos**: `dsp-k4001` (`[asm]`) — `DO` loop accesses buffers exclusively through indirect pointer walking (`(rN)+`, `(rN)-`, `(rN)`) with zero indexed `Disp` moves.

### Pattern

Indexed accesses (`s.buf[i + k]` or `Region.read reg idx`) inside a loop require address arithmetic or two-word `Disp` instructions (`move X:(r6+offset),x0`) in direct codegen, which either rely on the `walk` optimiser pass or survive as 2-word moves.

### Recommended rewrite (compiles today)

Bind a `Cursor` before the loop (`Cursor.linearIn`, `Cursor.ringIn`, `Cursor.linear`, or `Cursor.ring`) and step it with `.Next()`, `.Peek()`, or `.Put v` so the compiler emits single-word `(rN)+` post-increment addressing directly:

```fsharp
let rd = Cursor.linearIn s.buf Frac.zero 1
let mutable acc = Acc.ofFrac p.a
for _ = 0 to 3 do
  acc <- acc + rd.Next()
```

---

## dsp-5001

- **Lint**: `dsp-5001` (`[ir]`) — Generic F# comparison (`<`, `>`, `<=`, `>=`, `=`, `min`, `max`) on `Frac` / `Acc` / `Long` structs inside `RENDER` boxes on .NET (48 B/call).
- **Kudos**: `dsp-k5001` (`[ir]`) — .NET `RENDER` allocates `0 B` per block after warmup (measured via `GC.GetAllocatedBytesForCurrentThread()`).

### Why it matters & recommended rewrite (compiles today)

Grading and stress sweeps execute the .NET reference `RENDER` thousands of times alongside the DSP emulator. Calling F#'s generic comparison operators (`<`, `>`, `<=`, `>=`, `=`, `min`, `max`) on `Frac`, `Acc`, or `Long` structs dispatches through `LanguagePrimitives.GenericComparison` on .NET, boxing both operands (`48 B` per call).

Use `Frac.Min`, `Frac.Max`, `Frac.Clamp` (or `Frac.min`, `Frac.max`, `Frac.clamp`) and their `Acc` / `Long` counterparts, which compare `.Raw` directly on .NET (`0 B` allocation) and compile to branchless `cmp` + `Tcc` on the DSP:

```fsharp
// 0 B allocation on .NET, branchless cmp + tlt/tgt on DSP:
let clipped = Frac.Clamp(x, lo, hi)
let smaller = Frac.Min(a, b)
let larger  = Frac.Max(a, b)
```



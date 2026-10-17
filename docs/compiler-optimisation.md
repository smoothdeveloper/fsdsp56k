# Closing the gap to hand-written DSP56300 code

The kernel compiler (`Dsp56k.Kernel`) translates F# quotations directly: every expression is
evaluated into A (or X0), operands come through B, X0 and Y0, `Frac` locals live in X1/Y1, at most
one `Acc` local stays in B, and everything else spills to `x:(r6+n)` (an `Acc` takes three words:
a1, a0, a2). `Optimize` then cleans the result up at the `Peephole` level: value numbering
(`Facts`), copy forwarding, dead temporaries, loop-invariant hoisting, parallel-move packing, a
one-stage software pipeline (`rotate`) and loop fix-ups.

The output is correct (every graded kernel compiles to a program equal to its .NET model, word
for word), but `Grade` shows it is several times slower than the hand-written RENDER it is
compared with. This note maps where the cycles go to known compiler techniques, in order of
expected value per effort.

## Where the cycles go

A static classification of the RENDER instructions of every graded kernel, compiled against
hand-written:

| Instruction kind               | Compiled | Hand-written |
|--------------------------------|---------:|-------------:|
| spill / reload of x temporaries|    ~5000 |         ~110 |
| a2 / b2 extension moves        |    ~1800 |            0 |
| Y work-set moves               |    ~3250 |         ~940 |
| register to register moves     |    ~3000 |         ~960 |
| immediate loads                |    ~1050 |         ~570 |
| branches                       |     ~730 |         ~330 |
| shifts                         |     ~880 |         ~260 |
| `div`                          |        2 |           28 |

About a third of the compiled instructions are spill traffic. The other recurring causes:

- **Accumulator locals spilled as three words** and reloaded every sample. Hand-written code keeps
  48-bit state in B, in X1:X0 / Y1:Y0 pairs, or in address registers (phases).
- **Loop-invariant branches** (an oscillator on or off, a mode) tested every sample instead of once
  per block.
- **Emulated instructions.** A division written as 24 F# steps instead of `rep #24 div`, a
  signed-by-unsigned product written as a product plus a sign fix-up instead of `mpysu`, a 48-bit
  gain instead of `mpysu` then `dmacss`.
- **Table reads** through an address register reloaded from a temporary, where hand-written code
  uses `x:(rn+base)` with a long displacement, or modulo addressing.
- **Immediates** loaded again into a register that already holds them, and `move #k,x0` then
  `sub x0,a` instead of `sub #k,a`.

## Techniques, mapped to the gaps

1. **Intrinsics for instructions the hardware has (idiom recognition).** Cheapest, with the
   largest wins on the kernels that hit them. Give the DSL a .NET-modelled member per instruction
   and lower it to the one instruction: `Acc.divideCarry` (`andi`/`ori ccr`, `rep #24`, `div`),
   `Frac.mulSU` (`mpysu`), `Acc.dmac` (`dmacss`). A later step is pattern recognition of the F#
   idioms themselves, so kernels don't have to name them.
2. **Global register allocation over the whole DSP register file.** Replace the fixed roles
   (A/X0 for expressions, X1/Y1 for locals) with an allocator over A, B, X0, X1, Y0, Y1 and the
   pairs X1:X0, Y1:Y0, A10, B10 for 48-bit values. Allocate across the loop (linear scan over
   live ranges, or graph colouring with the X/Y bank constraints of parallel moves). Values that
   still spill should spill as 48 bits (`a10`, two words) when the extension is known to be a sign
   extension. This is the largest single item: it removes most of the spill traffic and every
   a2/b2 move.
3. **Loop unswitching and loop fission.** Move a branch on a block-invariant value out of the
   sample loop: one loop per case (unswitching), or one loop per independent part
   (fission: one pass per oscillator, each with its own registers).
4. **Address-register allocation and addressing modes.** Keep phases and table bases in R/N
   registers, use `(rn+disp)` and `(rn)+nn`, and set `m` registers for ring buffers instead of
   masking.
5. **Immediate operands and immediate value numbering.** Track constants in `Facts` so a register
   already holding one is not loaded again (prototyped below), and fold a constant operand into
   the ALU instruction (`sub #k,a`, `cmp #k,a`, `and #k,a`).
6. **Parallel-move packing as X/Y bank colouring.** Packing exists; it gets better once the
   allocator places a value in X or Y memory and registers with packing in mind.
7. **Software pipelining (modulo scheduling)** beyond the current one stage, once the
   instruction count is down.
8. **Superoptimisation of short blocks** (STOKE-style stochastic search, or equality saturation
   with e-graphs over the DSP56300 rewrite rules), checked against the emulator, as a last pass on
   hot loop bodies.

## Is a missing `clr` the problem?

No. The emitter already writes `clr` when it loads the accumulator constant zero
(`loadAccConst`), and `tst` when it compares with zero. A `Frac.zero` stored to state goes through
`move #$0,x0` then `move x0,...`, the same count as the hand-written `clr a` then `move a1,...`.
In the hand-written kitbasher TX6 kernels every `clr` (4 to 7 per kernel) is in INIT, in TRIG, or in
the loop that writes a silent block, and none is in the per-sample path. What made those kernels
pay was elsewhere: the compiled silent block loaded `#$0` again on each of its 32 passes, since
`hoist` only moved workset loads out of a DO loop. Hoisting immediates as well fixes that, and
other constants loaded inside loops, and gives 3 to 4% on every TX6 kernel.

The fusions `clr` would allow are still worth having once registers are allocated:
- `clr a x:(r0)+,x0` (the clear in parallel with a move) comes from the packer, as soon as
  `clr` is emitted next to a move.
- `acc = 0; acc += x * y` should become `mpy`, not `clr` then `mac`. That is a peephole on the
  lowered code, or a rewrite in the front end.

Each of these saves one instruction per occurrence. They are not what separates 1.8x from 1x.
That gap is spills and 56-bit reloads, compares not done with an immediate (`cmp #>415,a`),
clamps written as branches instead of `tgt`, and loops that are not split. Splitting a noise loop
from a filter loop, for example, lets the filter read both operands in one parallel move
(`move x:(r3)+,x0 y:(r5)+,y0`).

## Measured, on the public samples

RENDER mean cycles, compiled, against the hand-written code each sample is graded with. Before is
the compiler without these changes. After has all of them; only the constant hoisting and the
immediate compares move these samples, which use none of the emulated forms:

| Sample     | Before | After | Hand-written | Ratio before | Ratio after |
|------------|-------:|------:|-------------:|-------------:|------------:|
| TX6 BD     |   1925 |  1863 |         1202 |        1.60x |       1.55x |
| TX6 SD     |   1580 |  1527 |          878 |        1.80x |       1.74x |
| TX6 CP     |   1479 |  1366 |          773 |        1.91x |       1.77x |
| TX6 CH     |   1265 |  1218 |          670 |        1.89x |       1.82x |
| TX6 OH     |   2302 |  2242 |         1208 |        1.91x |       1.86x |
| TX6 CY     |   2406 |  2344 |         1262 |        1.91x |       1.86x |
| TX6 LT     |   1752 |  1698 |          997 |        1.76x |       1.70x |
| TX6 HT     |   1633 |  1580 |          923 |        1.77x |       1.71x |
| sine       |   1123 |  1123 |          625 |        1.80x |       1.80x |
| pluck      |    635 |   635 |          406 |        1.56x |       1.56x |
| main tap   |    377 |   377 |          265 |        1.42x |       1.42x |
| input thru |    119 |   119 |          148 |        0.80x |       0.80x |

## Prototypes

Each one ships with a conformance test: the compiled program run on the emulator equals the .NET
kernel on random parameters and state.

- `Optimize`: a long or zero immediate that a register already holds is not loaded again; one that
  another data register holds is copied from it.
- `Acc.divideCarry carry a s`: 24 non-restoring division steps with the carry given and no
  shift first, lowered to `andi #$fe,ccr` (or `ori #$01,ccr`), `rep #24`, `div x0,a`.
- `Frac.mulSU s u` lowered to `mpysu`, and `Acc.dmac acc x y` lowered to `dmacss`: together, a
  word times a 48-bit level hi:lo in two instructions.
- `Optimize.hoist`: an immediate loaded inside a DO loop into a register nothing else in the loop
  writes is loaded once, before the loop.
- `Acc.macSU acc s u` lowered to `macsu`.
- A constant that is a word in a1 (a0 zero, a2 its sign) is added, subtracted or compared as an
  immediate: `cmp #k,a`, not a load into B. Helpers the compiler inlines that only compute fold
  on constant arguments too, so `Acc.ofFrac (at 415)` is a constant.

Not done, and why:
- `clr` fused into a parallel move, and `acc = 0; acc += x * y` to `mpy`: the graded code has no
  `clr` and no clear-then-accumulate to fuse, so neither would move a number yet.
- `tgt`/`tlt` for clamps: the clamped values mostly sit in X1/Y1 (Tcc only writes A or B), so this
  waits for the register allocator.

The intrinsics only pay off once kernels use them. On graded kernels that had the emulated forms,
calling the division intrinsic instead of 24 F# steps took a RENDER from about 20x the
hand-written cycles to under 3x. Calling `mulSU`/`dmac` instead of the product plus sign fix-up
roughly halved the RENDER of the kernels that apply a 48-bit gain in their sample loop.

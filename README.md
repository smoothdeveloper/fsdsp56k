<img src="docs/fsdsp56k-logo.svg" alt="fsdsp56k" width="96" align="right">

# fsdsp56k

F# tooling for the Motorola DSP56300 family, and on top of it, support for writing Elektron
Machinedrum machines (MDX X.20 MDS packages).

## Layers

The DSP56300 libraries know nothing about the Machinedrum; the Machinedrum libraries build on
them; samples and the command-line tool sit on top.

| Path | What it is |
|---|---|
| **Pure DSP56300** | |
| `src/dsp56k` | Typed ISA, encoder, Motorola-syntax printer and parser, two-pass assembler (`include`, `define`, `macro`, `if`) with asm56300's sizing rule |
| `src/dsp56k.dsl` | The F# DSL: instruction helpers (`move`, `mac`, `doTo`, `brclr`…), workset layouts, table generation, optional lowering passes (parallel-move packing, AGU hazards), transcription of items to DSL source, listing import |
| `src/dsp56k.native` | P/Invoke over `native/`: the oracle assembler/disassembler, the JIT emulator (run, call, profile), program recovery from words |
| `native` | Rust `cdylib` with a C ABI over mborgerson/dsp56300 (MIT), pinned |
| **Machinedrum MDS** | |
| `src/machinedrum` | MDS v1 packages (JSON definitions, byte-exact writer, reader, verification), the machine ABI as DSL helpers (workset, resources, services, patch markers), SysEx dumps |
| `src/machinedrum.run` | A track on the emulator (loader relocation and imports, parameter conversion, sample-service stand-ins, cycle budgets), WAV I/O, binary import of packages to DSL items |
| **Kernel F# (spike)** | |
| `src/dsp56k.kernel.intrinsics` | `Frac`, `Acc`, `Long`, `Table`, `Output`: DSP56300 arithmetic as .NET code, each operation tagged with the compiler's intrinsic name |
| `src/dsp56k.kernel` | Quotation compiler from `[<ReflectedDefinition>]` kernels to DSL items (direct translation, no optimisation yet), and a .NET host running the same kernel |
| `src/dsp56k.kernel.grade` | Runs a scenario three ways (.NET kernel, compiled program, reference) on a harness (the MDS track, or a program image of your own loaded at fixed addresses) and reports correctness, fidelity, cost and listing alignment |
| **On top** | |
| `src/dsp56k.tool` | The `fsdsp56k` command line |
| `samples` | DSL programs reproducing shipped machines word for word |
| `tests/dsp56k.tests` | Expecto tests |

## The DSL

A program is an array of items written with F# functions, so loops, tables and variants are
ordinary F# code:

```fsharp
open Dsp56k
open Dsp56k.Dsl
open Dsp56k.Dsl.Asm

let clearWorkset = [|
  label "init"
  move r6 r0
  move (imm 9) n0
  update (incN r0)
  clr a
  doTo (imm 19) "init_end"
  move a (ym (inc r0))
  label "init_end"
  rts |]

let words = Program.words clearWorkset        // assembled with the same encoder as listings
let listing = Program.source clearWorkset     // Motorola syntax, for asm56300 or review
```


## Kernels

A machine can also be written as ordinary F# over fixed-point types, run as .NET code and
compiled to DSP56300 code (`tests/dsp56k.tests/Kernels/Sine.fs`, jmamma/MDS's sine example):

```fsharp
[<ReflectedDefinition>]
module Sine
let render (p: Params) (s: State) (out: Output) =
  let step = Acc.ofFrac p.pitch >>> 6
  let ramp = (Acc.ofFrac p.amp - Acc.ofFrac s.ampPrev) >>> 5
  let mutable gain = Acc.ofFrac s.ampPrev
  let mutable phase = Acc.ofLong s.phase
  for _ in 0 .. 31 do
    gain <- gain + ramp
    let v = Table.read Rom.sine (Acc.hi phase)
    out.Write(Acc.sat ((v * Acc.hi gain) >>> 2))
    phase <- Acc.andHi 0x7fff (phase + step)
  s.phase <- Long.ofAcc phase
  s.ampPrev <- p.amp
```

The compiler translates directly (values through A, B, X0, Y0, spills to the X workset);
`Optimize.fs` then numbers values in straight-line code (dropping reloads and redundant stores),
removes dead spills by liveness, forwards copies and packs moves into parallel slots. `Frac`
locals worth it (uses weighted by loop trip counts) live in X1/Y1, multiplications place their
factors around them, and constant factors ride in MPYI/MACI. State records hold `Frac`, `Long` (two words), `Acc` (three: kept exact,
extension byte included) and fixed-length `[<Length n>] Frac array` fields read and written
with constant indices or the loop's index (`s.h[i + 1]`, `Frac.ofRaw i`); helpers may take the
parameter and state records. Helpers are inlined: their locals never take a register a caller's
local still needs (a local stays live through the whole expression and statement reading it),
and where one argument's effect (a state write, a cursor step, a region write) could change what
another reads, the arguments are evaluated in .NET's order, left to right. State lives in Y from the target's `stateBase`; fields marked
`[<XMemory>]` (`[<XMemory; Length 32>] mutable residuals : Frac array`) live in X from X:(r6+0),
in the order declared, and the compiler's locals follow them in X (then in the Y words after the
state, when the X state fills the workset). A loop stepping an X array and a Y array gives them
registers of both banks, so their loads share an XY move. `Host.Instance.StateWords` and the
grade compare the Y state words, then the X ones. An index computed at run time is a word
passed through `Frac.index` (`s.h[Frac.index k]`, `s.h[Frac.index d + 32]`, X or Y), and
`Region.read`/`Region.write` index a region the same way: n0 holds the index (plus the array's
offset), r0 the workset or the region, and the access is (r0+n0), linear, as the TX8 machines'
residual writes. On .NET an index outside the array fails; on the DSP it reaches another word. Cursors also
walk state arrays: `Cursor.linearIn s.h start step` and `Cursor.ringIn s.h start step`, the
latter with modulo addressing (M = n - 1) as TX8-CY's six-word Y ring (`lua (r6+$10),r4`,
`movec #5,m4`, `move #3,n4`); `Up`, `Down`, `PutUp` and `PutDown` move one word either way
whatever the step. A ring must start on a multiple of the power of two at or above its length
(the compiler says so otherwise), and only on the Machinedrum, whose r6 is 64-word aligned. Beyond arithmetic, kernels use cursors (an address register walking a
region, linear or modulo, with a step), firmware resources (the sine ROM, the input and
main-output rings and their position words, the shared temporary memory), tables placed in
program memory, MPYR/MACR, and an LFSR step.

The TX6 kit's eight drums, by Matthew Fecher (AnalogMatthew) and Fadedlimes, published under the MIT licence in
[etcetc000/kitbasher](https://github.com/etcetc000/kitbasher), are the most comprehensive suite of real machines
the compiler is held to: glides, layered metal, noise and tables, each reproduced word for word. They are what
made the compiler robust; many thanks to their authors.

`tests/dsp56k.tests/Kernels` ports twelve machines: jmamma/MDS's four examples and all eight drums of kitbasher's
TX6 kit (BD, LT, HT, CH, OH, CY, SD, CP, in five kernel files). The tests grade each against the machine it
was ported from:

| Kernel | Ported from | RENDER cycles |
|---|---|---|
| `Sine.fs` | jmamma/MDS examples/sine | x2.78 |
| `InputThru.fs` | jmamma/MDS examples/imports/input_thru | x1.43 |
| `MainTap.fs` | jmamma/MDS examples/imports/main_tap | x1.77 |
| `Pluck.fs` | jmamma/MDS examples/imports/pluck | x1.55 |
| `Tx6Bd.fs` | kitbasher mds/tx6/bd (MIT) | x2.11 |
| `Tx6Tom.fs` (LT, HT) | kitbasher mds/tx6/lt, ht (MIT) | x2.05, x2.07 |
| `Tx6Metal.fs` (CH, OH, CY) | kitbasher mds/tx6/ch, oh, cy (MIT) | x2.03 each |
| `Tx6Sd.fs` | kitbasher mds/tx6/sd (MIT) | x1.84 |
| `Tx6Cp.fs` | kitbasher mds/tx6/cp (MIT) | x1.95 |

The TX6 toms and hats are one engine each, parameterized by a `Voice` record the compiler
reads: LT's pitch glide (`if voice.glide`) and CY's bell layer (`match voice.layer with ...`)
are decided when compiling, so each kernel contains only its own code.

Everything a grade produces is under `out/kernels/` (untracked, and deleted at the start of the
run that regenerates it): `report.html`, then one folder per machine, grouped by origin
(`kitbasher/tx6/bd/`, `jmamma-mds/examples/sine/`, ...), each with the compiled
kernel's output and the reference's as WAV files, the scenario script, an Audacity label track
(File > Import > Labels...) and the compiled listing; the tests keep theirs in
`out/kernels/tests/` (probes, samples). The scenarios play two bars at 120 BPM
as a sequencer would (trigs, parameter locks), then sweep each control from its default and
cover every pair of controls at low, default and high values.

Each is word for word identical to the shipped machine on the emulator, and to the same F# run
as .NET code. The page shows per kernel the verdicts, the output over the reference (SVG), the
cycles of every call against the track limit, the costs, and three listings side by side: the F#
kernel, the generated assembly (hovering a line highlights the F# it came from and back; kernels
are compiled with `--quotations-debug+` for the source ranges) and the source it was ported
from, plus each entry point aligned with the reference's disassembly. The tests run each
intrinsic's .NET meaning against the emulator on random and extreme words, and every sample's
grade.

## The tool

    dotnet run --project src/dsp56k.tool -- <command>

| Command | Does |
|---|---|
| `asm src.asm [-I dir] [-D NAME=VALUE] [-l out.lst] [--hex out.hex]` | assemble, print the listing |
| `check src.asm …` | assemble, then confirm every instruction against the dsp56300 oracle |
| `disasm machine.mds\|.syx [--slot n]` | recover a package's program as Motorola source (labels, entry points, patch markers) |
| `import src.asm\|machine.mds\|.syx [-m Module] [-o out.fs]` | listing or package to F# DSL source |
| `mds info\|pack\|syx\|unsyx …` | inspect, build (JSON + source + sprite), and convert packages and SysEx dumps |
| `run machine.mds\|.syx [--settings 64,1,…] [--blocks n] [--trig-every n] [--sample in.wav] [--wav out.wav] [--profile]` | render on the emulator; cycle report, hot addresses |

## Build and test

    dotnet tool restore && dotnet paket install
    (cd native && cargo build --release)      # oracle, emulator, import, run
    ./scripts/fetch-fixtures.sh               # shipped machines the tests compare against
    dotnet run --project tests/dsp56k.tests

What the tests check:

- Generated instructions across every form the ISA models encode exactly as
  mborgerson/dsp56300's assembler encodes their printed text; the oracle's text for all 256
  parallel ALU bytes parses and encodes back to the same byte.
- 16 MDS packages built with asm56300 and `mds_pack.py` (murdenihcam, jmamma/MDS) and the 8
  TX6 machines of kitbasher: the F# assembler reproduces every program word, the F# packer every
  package byte, CRCs and SHA-256 content ID included.
- The 69 distinct packages of the machine library: every decodable word (82,561) parses and
  re-encodes, and every package imports to DSL items that rebuild it byte for byte.
- The samples, as above.
- The tool, run as a process: `mds pack`/`syx`/`unsyx` reproduce a shipped package, `check`
  agrees with the oracle, `run` renders within budget.
- Assembled routines run in the emulator.

## Third-party sources

Not vendored. `scripts/fetch-fixtures.sh` clones them into `fixtures/`, which is git-ignored:

- etcetc000/kitbasher (GPL-3.0; its examples and TX6 carry MIT licences)
- jmamma/MDS (format documentation and examples; no licence published)

`native/` depends on mborgerson/dsp56300 (MIT) through Cargo.

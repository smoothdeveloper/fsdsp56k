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

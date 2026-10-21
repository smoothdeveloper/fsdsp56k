# Direct F# kernel targets

Run the kernel functions themselves, keeping `Frac`/`Acc`/`Long` arithmetic. These targets do
not emit or interpret DSP56300 instructions. Existing DSP graders and their defaults are unchanged.

| Target | Status |
|---|---|
| .NET desktop | Built and measured, Release configuration |
| Fable → JavaScript | Built with Vite/Fable; checked and measured in Node |
| Fable 5.18 → Rust → WASM | Built with Cargo release; checked and measured through Node's WASM engine |
| Fable 5.18 → Rust → native | Cargo release dynamic library; same ABI and exact checks as Rust/WASM |
| F# → .NET NativeAOT | Self-contained native runner; explicit JSON codec supports trimming |
| F# → .NET WASM interpreted / AOT | Separate builds using `wasm-tools`; Node hosts the browser-wasm runtime, with grading inside .NET |

All seven targets passed the SINE, PLUCK and INPUT-THRU fixtures on 2026-10-19,
checking output/state after every block and every declared final buffer.

The first adapters cover existing SINE, PLUCK and INPUT-THRU samples. They call their named
`init`, `trig`, `render` functions; they do not copy synthesis code or use the DSP compiler IR.
`prepare.fsx` stages those sources under ignored `out/portable/sources`, omitting their
single-line quotation registration and erasing `Hz`/`ratio` type annotations and the
`plain` identity cast. It refuses unfamiliar registrations. Originals are untouched.
For new sources, keep quotation-only registration behind `#if !FABLE_COMPILER`. DSP compilation
can already select named functions with `FromTypedTree.kernelIn` / the JSON frontend and
`Compile.compileIr`; a quotation is not required by those frontends.

## Build and measure

From this repository's root, using the existing .NET SDK, Node and pnpm:

```sh
cd portable
pnpm install --frozen-lockfile
pnpm build
cd ..
dotnet fsi portable/check.fsx
dotnet fsi --define:FABLE_COMPILER --define:FABLE_COMPILER_RUST portable/check.fsx
dotnet fsi portable/grade.fsx --dotnet --javascript
```

For optional Rust/WASM, install the matching Rust standard library first:

```sh
rustup target add wasm32-unknown-unknown
dotnet fsi portable/build-rust.fsx
dotnet fsi portable/grade.fsx --dotnet --javascript --rust-wasm
```

All targets are optional; select a comma-separated list or `all`:

```sh
dotnet fsi portable/install-wasm.fsx
dotnet fsi portable/grade.fsx --targets all
dotnet fsi portable/grade.fsx --targets rust-native,dotnet-aot
```

`install-wasm.fsx` copies installed SDK **10.0.401** into a writable
`out/toolchains/dotnet10` installation and installs `wasm-tools` there. It needs registry access
and substantial disk space; system workloads stay untouched. Shared runtime/manifest directories
are linked to the system installation and must remain available. The SDK itself is copied because
MSBuild resolves SDK symlinks to the system root and then misses account-local workloads.
Set `FSDSP56K_WASM_DOTNET` to another `dotnet` executable with the workload to override it.
NativeAOT needs the platform's native linker; WASM AOT needs the Emscripten workload.
Project dependencies stay in the existing project/Paket setup; no package reference is added.
The official workload installer supplies its own Emscripten, Node and Python toolchain packs.

`Targets.fsx` is the shared builder/runner for this suite and external kernel adapters.
`measurements/<run>/compile-times.tsv`, `context.tsv` and `workloads.tsv` record build wall time,
hardware/runtime context, fixture hashes, warmup/trial counts and result paths. All live under
ignored `out/`; each run keeps its own result JSON. Timings are machine-, workload- and
cache-dependent, not canonical metadata.
Build durations exclude workload installation and dynamic JIT compilation; JIT activity can
occur during warmup and measured execution. AOT publish timings include native compilation/linking.
The .NET WASM host runs the actual browser-wasm runtime under Node, not an AudioWorklet.
Native Rust uses C ABI calls and output copies through a .NET host; those costs are included.

`build-rust.fsx` installs Fable **5.18.0** under `.bench/tools` if missing. It does not change the
global Fable installation. Cargo dependencies are locked. The script selects rustup's `rustc`:
macOS Homebrew's compiler can otherwise shadow it and fail to find the installed WASM target.

`out/portable/grades.tsv` contains one row per kernel/target, exact check counts, runtime,
median/worst render milliseconds and audio-duration/render-time ratio. `provenance.tsv` records
the repository revision, source hashes, compiler identity and built artifact hashes, including
uncommitted experiments. Fixtures and per-target measurements stay beside them. No firmware is
included. Progress is printed before each kernel/target pair; a mismatch stops the command.

Each scenario renders 512 × 32 mono frames, changes parameters and retriggers. The .NET run
establishes expected output/state at every block and final scratch-X, scratch-Y and track-memory
buffers. Each target verifies them before two warmup scenarios and five timed scenarios.
Timings sum render calls and include normal output allocation and, for WASM, C-ABI output copies.
Parameter/init/trig calls, fixture parsing, verification and buffer snapshots are outside timings.
Worst is the slowest whole render trial, **not** the worst individual block or callback.
No DSP cycle/instruction count is invented for native execution. These are offline throughput
measurements, not proof of real-time scheduling in a browser AudioWorklet. Other system activity,
JIT warmup, garbage collection and bridge cost affect them.

## Optional hook for existing DSP grades

After an existing grader produces a `Grade.Report`, call:

```fsharp
let runner: PortableGrade.Runner =
  { executable = "node"
    arguments = [ "/absolute/path/to/out/portable/js/grade.mjs" ]
    warmup = 2
    trials = 5 }

let comparison = PortableGrade.check outputDirectory runner expectedFinalBuffers report
```

Use an absolute executable/module path in automation. For Rust/WASM, append `"--wasm"` and the
absolute module path to `arguments`. NativeAOT uses its published executable directly; native Rust
uses the .NET runner with `--native` and the library path. .NET WASM uses `dotnet-wasm-grade.mjs`
with `dotnet.js` and the target before the standard fixture/result/warmup/trials arguments.
Supply every declared expected final buffer, exactly once; an omitted or duplicate buffer fails.
Adapters must match the kernel name and Y-then-X state word order.
Settings use the report's existing conversion; Words, Init, Trig and Render retain their order.
Dynamic `Grade.beforeRender` inputs/resources need an explicit matching fixture/adapter; this
hook cannot infer firmware tables or arbitrary callbacks. A missing adapter fails explicitly.

The hook checks direct output/state against `report.net`, and reports that output's difference
from the original assembly reference when `report.refRun` exists. It does not equate the compiled
DSP kernel with the original reference. The checks cover
three samples.

Run the integration check after building the grading library and the JS target:

```sh
dotnet build src/dsp56k.kernel.grade
dotnet fsi --quotations-debug+ portable/check-dsp.fsx
```

It first checks compiled DSP output/state against .NET, then sends that same scenario through
the optional direct runner. It needs the repository's native DSP emulator.

## AudioWorklet integration

`wasm/PortableProcessor.fs` hosts the Rust/WASM exports in an isolated audio worklet. Global stand-in resources (`Scratch`, `Track.memory`, input and
main rings) require voice isolation: one WASM instance per voice, or explicitly owned JS memory.
Several direct JS voices must not silently share those mutable stand-ins. The current adapters
allocate output arrays, and the Rust bridge reads one output word per export call; bulk buffers
and allocation-free rendering are subsequent optimizations to measure, not assumed speedups.

The JS build also emits browser-safe `out/portable/js/kernels.mjs` (typed adapter factories)
and `rust-wasm.mjs` (WASM instantiation and the same factory interface). Only `grade.mjs` imports
Node APIs. Keep the generated shared `.mjs` chunks beside these entry modules when serving them.

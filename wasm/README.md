# DSP56300 in the browser

The Usual Suspects' dsp56300 interpreter runs assembled programs in WebAssembly. The loader,
AudioWorklet, checks and browser bindings are F#, compiled by `vite-plugin-fable`.
pnpm reads `package.yaml` directly; no JSON manifest is generated.

From the repository root, with .NET 10, Emscripten, CMake and pnpm 10.6.4 available:

```sh
git clone https://github.com/dsp56300/dsp56300 ../../dsp56300/dsp56300
git -C ../../dsp56300/dsp56300 submodule update --init --depth 1 source/asmjit
dotnet fsi wasm/build.fsx ../../dsp56300/dsp56300
cd wasm
pnpm install --frozen-lockfile
pnpm build
pnpm check
sh to-webui.sh
```

`build.fsx` takes the DSP56300 sources from a dsp56300 checkout (with its submodule `source/asmjit`, at the commits
checked out) and exports them into `.bench/wasm/upstream`. `out/wasm/SOURCE.txt` names the revision built, for the
GPL-3.0 source offer. Checked with dsp56300 `7d84319`: the 14
kernels of the samples give the native emulator's output exactly (`fixture-all 48`, then `check.mjs`).
It applies checked build adaptations: skip native JIT allocation/trampoline creation,
omit the native instruction-cache flush, disable native assertion backtraces, and permit
32-bit function pointers in an unused JIT emitter. Unmapped-read diagnostics are capped
at four messages to protect the audio thread from console flooding; reads still return zero. The C++ wrapper always calls
`execInterpreter`; native JIT emitters are linked only because the upstream DSP owns a JIT.
Ordinary flat P/X/Y memories need no virtual-memory support. No upstream checkout is edited.
`KernelImage.externalMemory = Some boundary` optionally maps X/Y addresses at and
above that word to the same P backing store, using upstream's software address
translation. The default remains independent banks. Host writes through any alias
invalidate the interpreter's opcode cache. The native oracle has a matching optional
`Emulator(..., externalMemory = boundary)` constructor for differential checks.
Emscripten's cache defaults to `.bench/wasm/emscripten-cache` so an installed SDK
can remain read-only; an explicit `EM_CACHE` takes precedence.

Generated runtime files go to `out/wasm`, including the upstream GPL-3.0 licence.
Do not distribute the generated interpreter without the corresponding upstream source,
these adaptations and its licence. No firmware is included in this project.

## Stepping and register snapshots

`_dsp_begin(entry)` starts a routine as `_dsp_call` does; `_dsp_step()` runs one instruction (1: returned, 0: running).
A `do` loop runs whole in one step: the upstream interpreter executes a hardware loop inside the `do` instruction.
`_dsp_snapshot()` takes every register (the `RegisterWrite` numbers, then PC, SR, OMR, LA, LC, SP; not SSH/SSL, whose
read pops the stack) and `_dsp_snapshot_word(i)` reads one. In the worklet, `begin` and `step` messages answer `step`
messages (status, pc, steps, registers, written); `activity` adds to each `level` message how many blocks changed each
register (`writes`), a cheap feed for a display.

## Fable client

Include `Runtime.fs` in the consuming Fable project. Serve the generated `.mjs` and `.wasm`
files together. Create an audio context on a user gesture, compile the WASM with
`compileModule`, load `processor.mjs` with `addProcessor`, and pass the compiled module
to `createNode`. AudioWorkletGlobalScope has no fetch/URL API; compiling on the page
avoids depending on either inside the audio thread.

Listen to `node.port.onmessage` for `ready`, `error` and `level` (peak and last block).
Call `load` with a version-1 `KernelImage`, wait for `ready`, then `play`.
`write` changes memory, `registers` changes arguments restored before every render,
and `gain` sets output gain from 0 to 0.5. The source image's sample rate must match the
audio context; this prototype does not resample. A new image resets all DSP state.

An image describes P/X/Y segments, initialization calls, registers, render/trig entries,
and a stereo output buffer. Addresses count 24-bit words. Bank numbers are P=0, X=1,
Y=2. Register numbers are R0–R7=0–7, N0–N7=8–15, M0–M7=16–23,
X0/X1/Y0/Y1=24–27, A0/A1/A2/B0/B1/B2=28–33. Extension words A2/B2 are 8 bits.
Images support up to 0x180000 words per bank. Trigger calls restore the same
prepared registers as rendering. Modulo registers start linear. Calls have an instruction watchdog; recreate the DSP
after a nonreturning call. This hosts isolated trusted kernels, not a complete device.

## Exact checks

`MACRI` is signed immediate MAC followed by convergent rounding; `build.fsx` stops on a dsp56300 revision that
leaves it unimplemented. Run
`dotnet fsi wasm/check_macri.fsx` from the repository root to compare 672 cases with
the Rust native emulator: four source registers, both destinations, signs, negative
operands, rounding boundaries, all accumulator words and CCR. No firmware is needed.
dsp56300 `7d84319` passes all 672 cases.

`pnpm check` verifies bounds, return handling, replacement of cached instructions and
the instruction watchdog. Supply image/reference pairs to check output, state and scratch
words against an independent emulator; no reference images are tracked here:

```sh
node out/wasm/check.mjs path/to/image.json path/to/reference.json
```

The C ABI exposes monotonic `_dsp_cycles()` and `_dsp_operations()` counters from
the interpreter. `Instance.LastCycles`/`LastOperations` measure each complete Render
call; operations are executed DSP instructions, including parallel-move instructions
as one. They are neither source expressions nor program word counts. The checker
writes `<image>.metrics.json`: defaults, 16 warmup blocks, then the mean of 128
blocks. It measures before differential checks so an experimental image can still
have timing figures; timing does not imply correctness. Counts include the image's
caller adapters. Input buffers retain their initialized data during this benchmark.
# Live callback timing

AudioWorklet level messages also carry `timing`: total/DSP/overhead percentages,
peak deadline load, definite overrun count, mean callback time and quantum budget.
`timer` identifies high-resolution or 1 ms timing. Percentages measure wall-clock
deadline usage on the client, not OS CPU usage. DSP time includes `Instance.Render`
and its adapter/readback; the remainder includes sample copies and trace capture.
Telemetry transfer and browser/device audio overhead are excluded. Pausing/loading
resets the measurement window. Coarse clocks discount one tick before counting a
definite deadline overrun. Verify with `dotnet fsi wasm/check_processing_timing.fsx`.

# Direct Fable/Rust kernels

The same build emits `portable-processor.mjs`, registered as `fsharp-rust-wasm`.
`PortableRuntime.fs` provides the F# node/load/parameter/resource bindings. Supply a
compiled direct-kernel `WebAssembly.Module` with the C ABI in `portable/RustAbi.fs`.
Each node instantiates that module separately, isolating track, scratch and resource state.
No firmware or machine address mapping belongs to this processor.

The current transport supports 16 stereo frames at 44100 Hz, eight parameter slots and
indexed resource arrays. Loading initializes a fresh kernel; messages take effect between
callbacks. It shares the interpreter's gain/play/trig and level/timing messages, including
output traces, and accepts `snapshot` for an exact state diagnostic. Input-dependent kernels
are not supported by this direct host yet. Timing includes C-ABI output copies; it does not
invent DSP cycle/instruction counts for native execution.

A Node harness verifies the actual built processor's messages, sample conversion and state
against a portable fixture. It does not measure browser scheduling:

```sh
node out/wasm/portable-check.mjs fixture.json kernels.wasm
```

Direct images now specify `inputResource`: use `-1` for generators, or the resource index of
an interleaved input buffer. Each callback converts incoming float samples to signed 24-bit words
before each 16-frame render (mono is duplicated; a disconnected input supplies zero). Effects
return the input trace alongside output for the existing feeder meter. `init` reruns kernel
initialization without replacing persistent state, so warm-init preservation can be checked.
The Node replay checker covers this bridge, warm init and callbacks up to 128 frames.

For intermediate signals, send `resource-trace` with `regions` containing `bank` (resource index),
`address` (word offset), and `words` (1–64). Up to eight regions return rolling 1024-word arrays
in the level message's `traces`; without that command, the existing input trace is retained.
Fixtures can set `audio.traceRegions`; the worklet checker verifies their final words against
host resource snapshots as well as audio and persistent state.

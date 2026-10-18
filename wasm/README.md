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
```

`build.fsx` exports a dsp56300 checkout (with its `source/asmjit` submodule), at the commit checked out, into
`.bench/wasm/upstream`.
It applies four checked build adaptations: skip native JIT allocation/trampoline creation,
omit the native instruction-cache flush, disable native assertion backtraces, and permit
32-bit function pointers in an unused JIT emitter. The C++ wrapper always calls
`execInterpreter`; native JIT emitters are linked only because the upstream DSP owns a JIT.
Ordinary flat P/X/Y memories need no virtual-memory support. No upstream checkout is edited.

Generated runtime files go to `out/wasm`, including the upstream GPL-3.0 licence.
Do not distribute the generated interpreter without the corresponding upstream source,
these adaptations and its licence. No firmware is included in this project.

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
Modulo registers start linear. Calls have an instruction watchdog; recreate the DSP
after a nonreturning call. This hosts isolated trusted kernels, not a complete device.

## Exact checks

`pnpm check` verifies bounds, return handling, replacement of cached instructions and
the instruction watchdog. Supply image/reference pairs to check output, state and scratch
words against an independent emulator; no reference images are tracked here:

```sh
node out/wasm/check.mjs path/to/image.json path/to/reference.json
```

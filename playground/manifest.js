// fsdsp56k playground: what dist/playground holds. Written by `dotnet fsi build.fsx playground-manifest`; do not edit.
fsdsp56kPlayground.manifest({
  version: 1,
  catalogue: "machines.js",
  kernels: [
    { id: "sine", data: "kernels/sine.js", reference: { phrase: "kernels/sine.phrase.wav", hit: "kernels/sine.hit.wav" }, runtimes: { js: { available: false, why: "not in the fable-js build" }, rust: { available: true, graded: "512 blocks, 16384 output and 1536 state words equal to .NET" } } },
    { id: "pluck", data: "kernels/pluck.js", reference: { phrase: "kernels/pluck.phrase.wav", hit: "kernels/pluck.hit.wav" }, runtimes: { js: { available: false, why: "not in the fable-js build" }, rust: { available: true, graded: "512 blocks, 16384 output and 3072 state words equal to .NET" } } },
    { id: "tx6-bd", data: "kernels/tx6-bd.js", reference: { phrase: "kernels/tx6-bd.phrase.wav", hit: "kernels/tx6-bd.hit.wav" }, runtimes: { js: { available: false, why: "not in the fable-js build" }, rust: { available: true, graded: "512 blocks, 16384 output and 10752 state words equal to .NET" } } },
    { id: "tx6-sd", data: "kernels/tx6-sd.js", reference: { phrase: "kernels/tx6-sd.phrase.wav", hit: "kernels/tx6-sd.hit.wav" }, runtimes: { js: { available: false, why: "not in the fable-js build" }, rust: { available: true, graded: "512 blocks, 16384 output and 13312 state words equal to .NET" } } },
    { id: "tx6-cp", data: "kernels/tx6-cp.js", reference: { phrase: "kernels/tx6-cp.phrase.wav", hit: "kernels/tx6-cp.hit.wav" }, runtimes: { js: { available: false, why: "not in the fable-js build" }, rust: { available: true, graded: "512 blocks, 16384 output and 13312 state words equal to .NET" } } },
    { id: "tx6-lt", data: "kernels/tx6-lt.js", reference: { phrase: "kernels/tx6-lt.phrase.wav", hit: "kernels/tx6-lt.hit.wav" }, runtimes: { js: { available: false, why: "not in the fable-js build" }, rust: { available: true, graded: "512 blocks, 16384 output and 13312 state words equal to .NET" } } },
    { id: "tx6-ht", data: "kernels/tx6-ht.js", reference: { phrase: "kernels/tx6-ht.phrase.wav", hit: "kernels/tx6-ht.hit.wav" }, runtimes: { js: { available: false, why: "not in the fable-js build" }, rust: { available: true, graded: "512 blocks, 16384 output and 13312 state words equal to .NET" } } },
    { id: "tx6-ch", data: "kernels/tx6-ch.js", reference: { phrase: "kernels/tx6-ch.phrase.wav", hit: "kernels/tx6-ch.hit.wav" }, runtimes: { js: { available: false, why: "not in the fable-js build" }, rust: { available: true, graded: "512 blocks, 16384 output and 12800 state words equal to .NET" } } },
    { id: "tx6-oh", data: "kernels/tx6-oh.js", reference: { phrase: "kernels/tx6-oh.phrase.wav", hit: "kernels/tx6-oh.hit.wav" }, runtimes: { js: { available: false, why: "not in the fable-js build" }, rust: { available: true, graded: "512 blocks, 16384 output and 12800 state words equal to .NET" } } },
    { id: "tx6-cy", data: "kernels/tx6-cy.js", reference: { phrase: "kernels/tx6-cy.phrase.wav", hit: "kernels/tx6-cy.hit.wav" }, runtimes: { js: { available: false, why: "not in the fable-js build" }, rust: { available: true, graded: "512 blocks, 16384 output and 12800 state words equal to .NET" } } }
  ],
  runtimes: { js: null, rust: { wasm: "rust/dsp56k_portable.wasm", kernels: ["sine", "pluck", "tx6-bd", "tx6-sd", "tx6-cp", "tx6-lt", "tx6-ht", "tx6-ch", "tx6-oh", "tx6-cy"] }, dsp56300: { glue: "dsp56300/dsp56300.mjs", wasm: "dsp56300/dsp56300.wasm", processor: "dsp56300/processor.mjs", package: "dsp56300/package.mjs" } }
});

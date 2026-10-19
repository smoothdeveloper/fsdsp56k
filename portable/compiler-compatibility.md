# Direct-target compatibility findings

Checked with Vite plugin 0.5.2 and the Rust backend of Fable 5.18.0.

| Finding | Treatment | Evidence |
|---|---|---|
| Source `Kernel.define` registrations contain unsupported quotations | Stage existing sample functions without that binding; named entry points remain intact | `prepare.fsx`; Fable explicitly rejects quotation expressions |
| `BitOperations.LeadingZeroCount(uint64)` unsupported by Fable | `FABLE_COMPILER` integer-loop fallback in `Acc.clb`; .NET branch unchanged | `check.fsx`, signed power-of-two boundaries |
| Rust backend lacks `Math.Round(float, MidpointRounding.ToEven)` | `FABLE_COMPILER_RUST` explicit ties-to-even quantization; other branches unchanged | Q23 signed midpoint checks; direct output comparisons |
| Rust backend emits `Track.current_0040` as a value instead of a static field path | Rust-only module storage with explicit static accessors | Cargo E0423 before workaround; successful WASM build after it |
| Generic interpolated error-label argument became Rust `Display` error | Annotate the diagnostic label as `string` | Cargo E0277 before annotation |
| Rust attribute classes warn about secondary constructor inheritance | Kernel compiler attributes are unused by direct execution; warnings remain | Cargo succeeds and three adapters pass exact checks |

Do not patch generated Rust: fixes belong in F# compatibility branches or upstream Fable.
The normal .NET intrinsic implementations and the DSP instruction compiler were not optimized
or replaced. Input resources generated from `Math.Sin` are stand-ins, not firmware tables;
other kernels need their actual resources supplied identically to all targets.

.NET browser AOT is a separate integration: this machine initially had no WebAssembly
workloads. It needs an explicit runtime host and measured worklet loading/memory behavior.
Keep it labelled unavailable until it has produced and passed a fixture run.

When a shared `.props` source list changes, Fable 5.18's cached project crack can omit newly
included files. Building with `--noCache` refreshes those options.
Resource fixtures now carry bounded `memory:name:offset` steps. They replay firmware tables
and prepared inputs outside render timing, through the same host interface on each target.

Seeded `System.Random` in a test resource initializer still needs Fable Rust's optional `random`
feature. Enabling it also pulls `chrono`/wasm-bindgen imports into a bare WASM module, even if
synthesis never requests time or entropy. Do not stub those browser imports. A resource supplied
by the loader should have an empty runtime descriptor; keep the random test filler in its grading
setup. The consumer stages that descriptor explicitly and verifies all synthesis functions
unchanged against native output/state. A future seed-only runtime feature could remove the
unrelated clock/browser dependency. Bare direct modules currently have zero WASM imports.

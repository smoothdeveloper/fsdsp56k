#!/bin/sh
# Puts the DSP runtime where the explorer page serves it (<public folder>/wasm, the folder given as the argument): the interpreter built by build.fsx
# (dsp56300.mjs, dsp56300.wasm, its GPL-3.0 licence) and the worklet built by `pnpm build` (processor.mjs and its chunks, and Kernel.mjs
# and check.mjs for the checks). Run `dotnet fsi wasm/build.fsx` and `cd wasm && pnpm build` first.
set -eu
here="$(cd "$(dirname "$0")" && pwd)"
out="$here/../out/wasm"
dest="${1:?usage: to-webui.sh <the web page's public folder>}/wasm"
mkdir -p "$dest"
for f in dsp56300.mjs dsp56300.wasm LICENSE.dsp56300.md processor.mjs; do
  test -f "$out/$f" || { echo "missing $out/$f (build.fsx and pnpm build first)" >&2; exit 1; }
done
# the worklet's chunks (Array.mjs, String.mjs...) come with it
cp "$out"/*.mjs "$out"/dsp56300.wasm "$out"/LICENSE.dsp56300.md "$dest/"
du -sh "$dest"

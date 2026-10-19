#!/bin/sh
# Puts the browser language service's static files where the WebUi serves them: public/ls/{worker.min.js, bundle.min.js, assemblies/}.
#   ./ls-assets.sh <standalone dist, built with fable-5.18.0-typed-tree.patch> <fable-metadata lib> <out dir>
# The assemblies are the reference assemblies of Fable's compiler plus the kernel libraries the source may open (built Debug).
set -eu
here="$(cd "$(dirname "$0")" && pwd)"
dist="$1"; metadata="$2"
out="${3:?usage: ls-assets.sh <dist> <fable-metadata lib> <out dir: the web page's public/ls>}"
src="$here/../../src"
rm -rf "$out"; mkdir -p "$out/assemblies"
cp "$dist/worker.min.js" "$dist/bundle.min.js" "$out/"
cp "$metadata"/*.dll "$out/assemblies/"
for p in Dsp56k.Kernel.Intrinsics Dsp56k Dsp56k.Dsl Dsp56k.Kernel Machinedrum; do
  dll=$(ls "$src"/*/bin/Debug/net10.0/$p.dll 2>/dev/null | head -1)
  test -n "$dll" || { echo "build $p first (dotnet build $src/dsp56k.kernel)" >&2; exit 1; }
  cp "$dll" "$out/assemblies/"
done
du -sh "$out"

#!/bin/sh
# Builds Fable.Standalone 5.18.0 with the typed tree entry (the GetTypedTree worker message) into <fable checkout>/src/fable-standalone/dist.
#   ./build.sh <path of a clone of github.com/fable-compiler/fable>        (takes about 7 minutes, needs the dotnet SDK and node)
# The checkout must be at the 5.18.0 tag; the patch is applied if it is not applied yet.
set -eu
here="$(cd "$(dirname "$0")" && pwd)"
fable="$1"
cd "$fable"
test "$(git describe --tags)" = "5.18.0" || { echo "$fable is not at the tag 5.18.0" >&2; exit 1; }
if git apply --reverse --check "$here/fable-5.18.0-typed-tree.patch" 2>/dev/null; then
  echo "patch already applied"
else
  git apply "$here/fable-5.18.0-typed-tree.patch"
fi
export TYPED_TREE_JSON_DIR="$here/../../src/dsp56k.kernel.typedtree"
dotnet run --project src/Fable.Build/Fable.Build.fsproj -- standalone
ls -la src/fable-standalone/dist

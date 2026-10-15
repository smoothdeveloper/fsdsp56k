#!/usr/bin/env bash
# Third-party sources the tests and samples check against, at pinned revisions, in fixtures/
# (git-ignored: their licenses differ from this repository's, and murdenihcam's carries none).
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p fixtures
fetch() {   # name url rev
  if [[ ! -d fixtures/$1/.git ]]; then git clone --quiet "$2" "fixtures/$1"; fi
  git -C "fixtures/$1" fetch --quiet --depth 50 origin
  git -C "fixtures/$1" checkout --quiet "$3"
  echo "fixtures/$1 at $3"
}
fetch kitbasher   https://github.com/etcetc000/kitbasher                   "${KITBASHER_REV:-510bdbd}"
fetch murdenihcam https://github.com/murdenihcam/murdenihcam-usr-machines  "${MURDENIHCAM_REV:-ab92ccf}"
fetch MDS         https://github.com/jmamma/MDS                            "${MDS_REV:-c446179}"

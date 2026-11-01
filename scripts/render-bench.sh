#!/usr/bin/env bash
#
# GAME-RENDER-ADOPT-01.2 (FS.GG.Game#746): build the rendering benchmark page and run the benchmark.
#
# 1. Materialises the pinned Babylon and Pixi bindings (scripts/materialize-js-bindings.sh), compiles
#    tests/Render.Browser.Bench with Fable, installs the exact pinned npm closures beside it and writes the page
#    (instrument.js first, then the import map and the module) to artifacts/render-bench/page.
# 2. Unless --build-only, runs tools/render-bench/driver.mjs with the remaining arguments and the harness commit.
#
#   usage: scripts/render-bench.sh [--build-only] [--skip-build] -- <driver run arguments>
#   e.g.   scripts/render-bench.sh -- --mode gpu-angle-vulkan --out gpu.json \
#            --capacity-probe ../.github/scripts/fdev-lane-capacity.py --capacity-args "--max-load-per-core 0.25"
#   env:   RENDER_BENCH_BROWSER=<chrome binary>   (default: first of chromium, google-chrome, chrome)
#          RENDER_BENCH_PAGE=<dir>                page output (default: artifacts/render-bench/page)
#          JS_BINDINGS_TEMPLATES_ROOT=<dir>       a FS.GG.Templates checkout at the pinned commit (offline use)
#
# See tools/render-bench/README.md.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PAGE="${RENDER_BENCH_PAGE:-$REPO_ROOT/artifacts/render-bench/page}"

BUILD=1
RUN=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --build-only) RUN=0; shift ;;
    --skip-build) BUILD=0; shift ;;
    --) shift; break ;;
    *) echo "render-bench: unknown argument '$1' (driver arguments go after --)" >&2; exit 2 ;;
  esac
done

for command in dotnet node npm git; do
  command -v "$command" >/dev/null || {
    echo "render-bench: required command '$command' was not found" >&2
    exit 2
  }
done

if [[ $BUILD == 1 ]]; then
  cd "$REPO_ROOT"
  dotnet tool restore >/dev/null
  "$SCRIPT_DIR/materialize-js-bindings.sh" all "$REPO_ROOT/artifacts/js-bindings"
  pin() { sed -n "s/^$1=//p" "$REPO_ROOT/eng/js-bindings/pin.env" | head -n 1; }

  rm -rf "$PAGE"
  mkdir -p "$PAGE"
  dotnet restore tests/Render.Browser.Bench/FS.GG.Game.Render.Browser.Bench.fsproj >/dev/null
  dotnet fable tests/Render.Browser.Bench --outDir "$PAGE" --noCache
  echo

  for corpus in babylon pixi; do
    mkdir -p "$PAGE/npm-$corpus"
    cp "$REPO_ROOT/artifacts/js-bindings/$corpus/npm/package.json" "$REPO_ROOT/artifacts/js-bindings/$corpus/npm/package-lock.json" "$PAGE/npm-$corpus/"
    (cd "$PAGE/npm-$corpus" && npm ci --ignore-scripts --omit=dev --no-audit --no-fund >/dev/null)
  done
  grep -Fq "$(pin BABYLON_NPM_INTEGRITY)" "$PAGE/npm-babylon/package-lock.json" || {
    echo "render-bench: the installed Babylon closure is not the pinned @babylonjs/core integrity" >&2
    exit 1
  }
  grep -Fq "$(pin PIXI_NPM_INTEGRITY)" "$PAGE/npm-pixi/package-lock.json" || {
    echo "render-bench: the installed Pixi closure is not the pinned pixi.js integrity" >&2
    exit 1
  }

  cp "$REPO_ROOT/tests/Render.Browser.Bench/instrument.js" "$PAGE/instrument.js"
  printf '%s\n' \
    '<!doctype html>' \
    '<meta charset="utf-8">' \
    '<title>FS.GG.Game rendering benchmark</title>' \
    '<style>html,body{margin:0;background:#000}</style>' \
    '<script src="./instrument.js"></script>' \
    '<script type="importmap">{"imports":{"@babylonjs/core/":"./npm-babylon/node_modules/@babylonjs/core/","@babylonjs/loaders/":"./npm-babylon/node_modules/@babylonjs/loaders/","pixi.js":"./npm-pixi/node_modules/pixi.js/dist/pixi.mjs","pixi.js/events":"./npm-pixi/node_modules/pixi.js/dist/pixi.mjs"}}</script>' \
    '<script type="module" src="./Program.js"></script>' \
    >"$PAGE/index.html"
  echo "render-bench: page built at $PAGE"
fi

[[ $RUN == 1 ]] || exit 0

BROWSER="${RENDER_BENCH_BROWSER:-}"
if [[ -z $BROWSER ]]; then
  for candidate in chromium google-chrome chrome; do
    if command -v "$candidate" >/dev/null; then
      BROWSER="$(command -v "$candidate")"
      break
    fi
  done
fi
[[ -n $BROWSER ]] || {
  echo "render-bench: no browser; set RENDER_BENCH_BROWSER" >&2
  exit 2
}

COMMIT="$(git -C "$REPO_ROOT" rev-parse HEAD)"
DIRTY=false
[[ -z $(git -C "$REPO_ROOT" status --porcelain -- src tests tools scripts) ]] || DIRTY=true

exec node "$REPO_ROOT/tools/render-bench/driver.mjs" run \
  --page "$PAGE" --browser "$BROWSER" --harness-commit "$COMMIT" --harness-dirty "$DIRTY" "$@"

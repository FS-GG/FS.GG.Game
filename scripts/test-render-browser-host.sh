#!/usr/bin/env bash
#
# GAME-RENDER-MULTI-01.4 (FS.GG.Game#730): browser smoke for the binding-independent renderer host.
#
# 1. Packs FS.GG.Game.Render and asserts its bounded Fable source view (fable/RendererSelection.*, the
#    metadata project and the compatibility profile) is present, byte-identical to the canonical sources,
#    and excludes the .NET-only Adapter/DefaultKeymap.
# 2. Compiles tests/Render.Browser.Smoke (which references src/Render.Browser and, through the SAME
#    Fable/FS.GG.Game.Render.fsproj the package ships, the renderer-selection sources) with Fable.
# 3. Loads the output in headless Chromium/Chrome and requires every smoke check to pass.
#
# The smoke proves the SVG adapter renders through RendererSelection + RendererHost + the retained
# SvgBrowser surface, that a later adapter can plug in, and that the BabylonJS adapter really renders: a WebGL
# canvas, one engine, meshes, a pixel read back, SVG -> Babylon -> SVG with the game state preserved and every
# resource released. It does NOT exercise PixiJS (its adapter waits for the Templates#808 bindings).
#
# The Babylon bindings are not vendored. scripts/build-babylon-bindings-feed.sh packs them from the pinned
# FS.GG.Templates commit (eng/babylon-bindings/pin.env) into artifacts/babylon-bindings/feed, which the two Fable
# projects restore as an ordinary PackageReference; the exact Babylon npm closure is installed beside the output
# and mapped with an import map.
#
#   usage: scripts/test-render-browser-host.sh
#   env:   RENDER_SMOKE_BROWSER=<chrome binary>   (default: first of chromium, google-chrome, chrome)
#          RENDER_SMOKE_BROWSER_FLAGS=<flags>     browser flags (default: headless, software WebGL through
#                                                 SwiftShader, which is what CI uses). For a GPU host use e.g.
#                                                 "--headless=new --no-sandbox --use-angle=vulkan
#                                                 --enable-features=Vulkan --ignore-gpu-blocklist"
#          RENDER_SMOKE_KEEP=1                    keep the temp directory for inspection
#          BABYLON_BINDINGS_TEMPLATES_ROOT=<dir>  a FS.GG.Templates checkout at the pinned commit (offline use)
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
MIN_CHECKS=96
PACK_VERSION="0.0.0-render-smoke"
PACKAGE="FS.GG.Game.Render.${PACK_VERSION}.nupkg"

for command in dotnet node npm git python3 unzip cmp grep sed; do
  command -v "$command" >/dev/null || {
    echo "test-render-browser-host: required command '$command' was not found" >&2
    exit 2
  }
done

BROWSER="${RENDER_SMOKE_BROWSER:-}"
if [[ -z $BROWSER ]]; then
  for candidate in chromium google-chrome chrome; do
    if command -v "$candidate" >/dev/null; then
      BROWSER="$candidate"
      break
    fi
  done
fi
[[ -n $BROWSER ]] || {
  echo "test-render-browser-host: required browser 'chromium' or 'google-chrome' was not found" >&2
  exit 2
}

TMP="$(mktemp -d)"
if [[ ${RENDER_SMOKE_KEEP:-0} == 1 ]]; then
  echo "test-render-browser-host: keeping $TMP"
else
  trap 'rm -rf "$TMP"' EXIT
fi
mkdir -p "$TMP/feed" "$TMP/js"

cd "$REPO_ROOT"
dotnet tool restore >/dev/null

# 0. The packed Babylon bindings and their exact npm closure (never committed; see the header).
"$SCRIPT_DIR/build-babylon-bindings-feed.sh" "$REPO_ROOT/artifacts/babylon-bindings"
BABYLON_INTEGRITY="$(sed -n 's/^BABYLON_CORE_INTEGRITY=//p' "$REPO_ROOT/eng/babylon-bindings/pin.env" | head -n 1)"

# 1. The packed Fable source view.
dotnet pack src/Game.Render/FS.GG.Game.Render.fsproj -c Release -p:PackageVersion="$PACK_VERSION" -o "$TMP/feed" >/dev/null
[[ -f $TMP/feed/$PACKAGE ]] || {
  echo "test-render-browser-host: expected package was not produced: $PACKAGE" >&2
  exit 1
}

PACKAGE_LIST="$(unzip -Z1 "$TMP/feed/$PACKAGE")"
for entry in \
  fable/FS.GG.Game.Render.fsproj \
  fable/RendererSelection.fsi \
  fable/RendererSelection.fs \
  fable-compatibility/compatibility-profile.v1.json; do
  grep -Fx "$entry" <<<"$PACKAGE_LIST" >/dev/null || {
    echo "test-render-browser-host: package is missing $entry" >&2
    exit 1
  }
done

if grep -E '^fable/(Adapter|DefaultKeymap)\.fsi?$' <<<"$PACKAGE_LIST" >/dev/null; then
  echo "test-render-browser-host: the bounded Fable view contains a .NET-only source file" >&2
  exit 1
fi

if [[ $(grep -c '^fable/.*\.fsi\?$' <<<"$PACKAGE_LIST") -ne 2 ]]; then
  echo "test-render-browser-host: the bounded Fable view must contain exactly RendererSelection.fsi/.fs" >&2
  exit 1
fi

mkdir -p "$TMP/unpacked"
unzip -q "$TMP/feed/$PACKAGE" 'fable/*' -d "$TMP/unpacked"
cmp "$TMP/unpacked/fable/RendererSelection.fs" src/Game.Render/RendererSelection.fs
cmp "$TMP/unpacked/fable/RendererSelection.fsi" src/Game.Render/RendererSelection.fsi
cmp "$TMP/unpacked/fable/FS.GG.Game.Render.fsproj" src/Game.Render/Fable/FS.GG.Game.Render.fsproj

# The packed layout puts the sources beside the project (the in-repo layout reaches one directory up), so
# build the unpacked view on its own to exercise that branch of the project's Exists() conditions. The
# consumer's central package pin stands in for the one a Fable consumer's restore provides.
SCENE_VERSION="$(sed -n 's/.*Include="FS.GG.UI.Scene" Version="\([^"]*\)".*/\1/p' Directory.Packages.local.props | head -n 1)"
[[ -n $SCENE_VERSION ]] || {
  echo "test-render-browser-host: could not read the FS.GG.UI.Scene pin" >&2
  exit 1
}
printf '%s\n' \
  '<Project>' \
  '  <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>' \
  "  <ItemGroup><PackageVersion Include=\"FS.GG.UI.Scene\" Version=\"$SCENE_VERSION\" /></ItemGroup>" \
  '</Project>' \
  >"$TMP/unpacked/Directory.Packages.props"
NUGET_PACKAGES="$TMP/packed-view-packages" dotnet build "$TMP/unpacked/fable/FS.GG.Game.Render.fsproj" -c Release -v quiet >/dev/null

# 2. Fable compile of the host and smoke.
dotnet restore tests/Render.Browser.Smoke/FS.GG.Game.Render.Browser.Smoke.fsproj >/dev/null
dotnet fable tests/Render.Browser.Smoke --outDir "$TMP/js" --noCache
echo

# The exact Babylon npm closure the bindings were qualified against, resolved by an import map because the Fable
# output imports `@babylonjs/core/...` by bare specifier.
cp "$REPO_ROOT/artifacts/babylon-bindings/npm/package.json" "$REPO_ROOT/artifacts/babylon-bindings/npm/package-lock.json" "$TMP/js/"
(cd "$TMP/js" && npm ci --ignore-scripts --omit=dev --no-audit --no-fund >/dev/null)
grep -Fq "$BABYLON_INTEGRITY" "$TMP/js/package-lock.json" || {
  echo "test-render-browser-host: the installed Babylon closure is not the pinned @babylonjs/core integrity" >&2
  exit 1
}
[[ -f $TMP/js/node_modules/@babylonjs/core/Engines/engineStore.js ]] || {
  echo "test-render-browser-host: @babylonjs/core did not install" >&2
  exit 1
}

printf '%s\n' \
  '<!doctype html>' \
  '<meta charset="utf-8">' \
  '<title>FS.GG.Game.Render browser host smoke</title>' \
  '<script type="importmap">{"imports":{"@babylonjs/core/":"./node_modules/@babylonjs/core/","@babylonjs/loaders/":"./node_modules/@babylonjs/loaders/"}}</script>' \
  '<script type="module" src="./Program.js"></script>' \
  >"$TMP/js/index.html"

# 3. Real browser run, in REAL time. A WebGL canvas needs compositor frames (Babylon compiles shaders
#    asynchronously and the smoke waits for the first drawn frame), and a virtual-time browser run delivers only a
#    couple of animation frames, so the page reports through the console instead: it logs one
#    RENDER-SMOKE-RESULT line (counts and a base64 report) when it is done and this script waits for that line,
#    bounded, then stops the browser.
#
#    The default flags give WebGL through SwiftShader (software), which is what a CI runner has;
#    RENDER_SMOKE_BROWSER_FLAGS selects a GPU instead. The result names the WebGL renderer that ran.
BROWSER_FLAGS="${RENDER_SMOKE_BROWSER_FLAGS:---headless=new --no-sandbox --disable-gpu --enable-unsafe-swiftshader --use-angle=swiftshader}"
BROWSER_TIMEOUT="${RENDER_SMOKE_TIMEOUT_SECONDS:-120}"
mkdir -p "$TMP/profile"
: >"$TMP/browser.stderr"
# shellcheck disable=SC2086
setsid "$BROWSER" \
  $BROWSER_FLAGS \
  --user-data-dir="$TMP/profile" \
  --allow-file-access-from-files \
  --enable-logging=stderr --v=0 \
  "file://$TMP/js/index.html" \
  >/dev/null 2>"$TMP/browser.stderr" &
BROWSER_PID=$!
stop_browser() { kill -- "-$BROWSER_PID" 2>/dev/null || kill "$BROWSER_PID" 2>/dev/null || true; }
trap 'stop_browser; [[ ${RENDER_SMOKE_KEEP:-0} == 1 ]] || rm -rf "$TMP"' EXIT

waited=0
until grep -q 'RENDER-SMOKE-RESULT' "$TMP/browser.stderr" 2>/dev/null; do
  if ! kill -0 "$BROWSER_PID" 2>/dev/null; then
    break
  fi
  if [[ $waited -ge $((BROWSER_TIMEOUT * 4)) ]]; then
    break
  fi
  sleep 0.25
  waited=$((waited + 1))
done
stop_browser

if ! grep -q 'RENDER-SMOKE-RESULT' "$TMP/browser.stderr"; then
  echo "test-render-browser-host: the page never reported a result within ${BROWSER_TIMEOUT}s (the module did not run or did not finish)" >&2
  grep -E 'CONSOLE|Uncaught' "$TMP/browser.stderr" | head -n 40 >&2 || true
  exit 1
fi

# The report line is: RENDER-SMOKE-RESULT checks=<n> failures=<n> <base64 of the PASS/FAIL/INFO lines>
python3 -I - "$TMP/browser.stderr" "$TMP/result.txt" "$TMP/result.counts" <<'PY'
import base64, re, sys

stderr, report, counts = sys.argv[1:4]
found = None
for line in open(stderr, errors="replace"):
    match = re.search(r"RENDER-SMOKE-RESULT checks=(\d+) failures=(\d+) ([A-Za-z0-9+/=]*)", line)
    if match:
        found = match
if found is None:
    sys.exit("test-render-browser-host: could not read the smoke result")
open(report, "w").write(base64.b64decode(found.group(3)).decode())
open(counts, "w").write(f"{found.group(1)} {found.group(2)}\n")
PY

read -r CHECKS FAILURES <"$TMP/result.counts"
grep -E '^(PASS|FAIL|INFO) ' "$TMP/result.txt" || true

if [[ -z $CHECKS || -z $FAILURES ]]; then
  echo "test-render-browser-host: could not read the smoke result" >&2
  exit 1
fi

# The subject is asserted before the verdict: a smoke that ran fewer checks than it should have is a failure.
if [[ $CHECKS -lt $MIN_CHECKS ]]; then
  echo "test-render-browser-host: only $CHECKS checks ran (expected at least $MIN_CHECKS)" >&2
  exit 1
fi

if [[ $FAILURES -ne 0 ]] || grep -q '^FAIL ' "$TMP/result.txt"; then
  echo "test-render-browser-host: $FAILURES of $CHECKS browser checks failed" >&2
  exit 1
fi

WEBGL="$(grep -E '^INFO webgl: ' "$TMP/result.txt" | head -n 1 | sed 's/^INFO webgl: //')"
echo "test-render-browser-host: OK — $CHECKS browser checks passed; package=$PACKAGE browser=$("$BROWSER" --version 2>/dev/null | head -n 1) flags=[$BROWSER_FLAGS] webgl=[$WEBGL]"

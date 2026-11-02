#!/usr/bin/env bash
#
# GAME-RENDER-MULTI-01.4 (FS.GG.Game#730): browser smoke for the binding-independent renderer host.
#
# 1. Packs FS.GG.Game.Render and asserts its bounded Fable source view (fable/RendererSelection.*,
#    SceneRenderers.* and RendererConformance.*, the metadata project and the compatibility profile) is present,
#    byte-identical to the canonical sources, and excludes the .NET-only Adapter/DefaultKeymap (SceneInteraction
#    joined the view with FS.GG.Game#751).
# 2. Compiles tests/Render.Browser.Smoke (which references src/Render.Browser and, through the SAME
#    Fable/FS.GG.Game.Render.fsproj the package ships, the renderer-selection sources) with Fable.
# 3. Loads the output in headless Chromium/Chrome and requires every smoke check to pass.
#
# The smoke proves the SVG adapter renders through RendererSelection + RendererHost + the retained
# SvgBrowser surface, that a later adapter can plug in, and that the BabylonJS adapter really renders: a WebGL
# canvas, one engine, meshes, a pixel read back, SVG -> Babylon -> SVG with the game state preserved and every
# resource released; the same for PixiJS; and SVG <-> Pixi <-> Babylon switching. Then the conformance kit
# (RendererConformance, GAME-RENDER-ADOPT-01.1) runs on the reference game's renderer declaration and on the
# generator's sample (scripts/new-game-renderers.sh), on all three real backends; the reference game's run includes
# the kit's interaction section (FS.GG.Game#751: hit test, keyboard, accessible mirror and input-host disposal on
# every backend), and the reference game is then played by pointer through its renderer driver on each backend.
# Last, the input host of SceneRenderers.withInput (FS.GG.Game#788) is played with real input on every backend, as a
# listbox and as a grid: mouse, touch, long-press, secondary, hover and mirror events, the single Tab stop and its owned
# ARIA items, the APG grid keyboard, keydown and keyup containment, the focus ring's two tones at 1x and 2x CSS scale and
# DPR 2, and release.
#
# The JavaScript bindings are not vendored. scripts/materialize-js-bindings.sh generates them from the pinned
# FS.GG.Templates commit (eng/js-bindings/pin.env) under artifacts/js-bindings, and the committed
# src/Render.Browser.Bindings project compiles the generated sources; the exact Babylon and Pixi npm closures are installed
# beside the output and mapped with an import map.
#
#   usage: scripts/test-render-browser-host.sh
#   env:   RENDER_SMOKE_BROWSER=<chrome binary>   (default: first of chromium, google-chrome, chrome)
#          RENDER_SMOKE_BROWSER_FLAGS=<flags>     browser flags (default: headless, software WebGL through
#                                                 SwiftShader, which is what CI uses). For a GPU host use e.g.
#                                                 "--headless=new --no-sandbox --use-angle=vulkan
#                                                 --enable-features=Vulkan --ignore-gpu-blocklist"
#          RENDER_SMOKE_KEEP=1                    keep the temp directory for inspection
#          RENDER_SMOKE_PART=all|main|layout      the page's part (default all). CI runs `main` and `layout` as two
#                                                 jobs: `layout` is the input host's page-layout matrix (FS.GG.Game#788),
#                                                 `main` is every other section
#          RENDER_SMOKE_TIMEOUT_SECONDS=<s>       the page's timeout (default 300). Every run prints each section's
#                                                 duration and the page's time against this timeout, and warns below 2x
#                                                 headroom
#          JS_BINDINGS_TEMPLATES_ROOT=<dir>       a FS.GG.Templates checkout at the pinned commit (offline use)
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PART="${RENDER_SMOKE_PART:-all}"
# The checks each part must run at least (main 909 + layout 246; main includes 35 theme-proof and 14 partial-mount checks).
case "$PART" in
  all) MIN_CHECKS=1155 ;;
  main) MIN_CHECKS=909 ;;
  layout) MIN_CHECKS=246 ;;
  *)
    echo "test-render-browser-host: RENDER_SMOKE_PART must be all, main or layout (got '$PART')" >&2
    exit 2
    ;;
esac
PACK_VERSION="0.0.0-render-smoke"
PACKAGE="FS.GG.Game.Render.${PACK_VERSION}.nupkg"

for command in dotnet node npm git python3 unzip cmp grep sed; do
  command -v "$command" >/dev/null || {
    echo "test-render-browser-host: required command '$command' was not found" >&2
    exit 2
  }
done

# Exercise CDP session ownership before package materialisation or a browser is started.
node --test tests/Render.Browser.Smoke/driver/run-smoke.test.mjs

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
# Playwright launches the browser from an executable PATH, never a command name, so resolve the bare name (from the
# default list or RENDER_SMOKE_BROWSER) and any symlinks to the real file.
RESOLVED="$(command -v "$BROWSER" || true)"
[[ -n $RESOLVED ]] || {
  echo "test-render-browser-host: browser '$BROWSER' was not found" >&2
  exit 2
}
BROWSER="$(readlink -f "$RESOLVED")"
[[ -x $BROWSER ]] || {
  echo "test-render-browser-host: browser '$BROWSER' is not executable" >&2
  exit 2
}
echo "test-render-browser-host: browser executable $BROWSER ($("$BROWSER" --version 2>/dev/null | head -n 1))"

TMP="$(mktemp -d)"
if [[ ${RENDER_SMOKE_KEEP:-0} == 1 ]]; then
  echo "test-render-browser-host: keeping $TMP"
else
  trap 'rm -rf "$TMP"' EXIT
fi
mkdir -p "$TMP/feed" "$TMP/js"

cd "$REPO_ROOT"
dotnet tool restore >/dev/null

# 0. The Babylon and Pixi bindings and their exact npm closures (never committed; see the header).
"$SCRIPT_DIR/materialize-js-bindings.sh" all "$REPO_ROOT/artifacts/js-bindings"
pin() { sed -n "s/^$1=//p" "$REPO_ROOT/eng/js-bindings/pin.env" | head -n 1; }
BABYLON_INTEGRITY="$(pin BABYLON_NPM_INTEGRITY)"
PIXI_INTEGRITY="$(pin PIXI_NPM_INTEGRITY)"

# 1. The packed Fable source view.
dotnet pack src/Game.Render/FS.GG.Game.Render.fsproj -c Release -p:PackageVersion="$PACK_VERSION" -o "$TMP/feed" >/dev/null
[[ -f $TMP/feed/$PACKAGE ]] || {
  echo "test-render-browser-host: expected package was not produced: $PACKAGE" >&2
  exit 1
}

PACKAGE_LIST="$(unzip -Z1 "$TMP/feed/$PACKAGE")"
grep -Fx fable-compatibility/compatibility-profile.v1.json <<<"$PACKAGE_LIST" >/dev/null || {
  echo "test-render-browser-host: package is missing fable-compatibility/compatibility-profile.v1.json" >&2
  exit 1
}
if grep -E '^fable/(Adapter|DefaultKeymap)\.fsi?$' <<<"$PACKAGE_LIST" >/dev/null; then
  echo "test-render-browser-host: the bounded Fable view contains a .NET-only source file" >&2
  exit 1
fi
mkdir -p "$TMP/unpacked"
unzip -q "$TMP/feed/$PACKAGE" 'fable/*' -d "$TMP/unpacked"

# The packed layout puts the sources beside the project (the in-repo layout reaches one directory up), so build the
# unpacked view on its own: the packed project must compile its sources where they sit. The consumer's central
# package pin stands in for the one a Fable consumer's restore provides.
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

# 1b. The packed browser host (FS.GG.Game.Render.Browser, #745): its Fable view is the host's sources byte for byte,
#     the two generated bindings at the pinned sha256, and the packed project that lists exactly those files; the
#     bindings assembly is not a dependency. Then a clean consumer restores both packages (and Game.Core) from this
#     feed, generates its renderer module, builds it with .NET and compiles it with Fable from the packed sources.
BROWSER_PACKAGE="FS.GG.Game.Render.Browser.${PACK_VERSION}.nupkg"
dotnet pack src/Render.Browser/FS.GG.Game.Render.Browser.fsproj -c Release -p:PackageVersion="$PACK_VERSION" -o "$TMP/feed" >/dev/null
dotnet pack src/Game.Core/FS.GG.Game.Core.fsproj -c Release -p:PackageVersion="$PACK_VERSION" -o "$TMP/feed" >/dev/null
[[ -f $TMP/feed/$BROWSER_PACKAGE ]] || {
  echo "test-render-browser-host: expected package was not produced: $BROWSER_PACKAGE" >&2
  exit 1
}
# Both packed Fable views against the source tree: lists derived from src/Game.Render/Fable and src/Render.Browser
# (bindings first), no MSBuild condition anywhere in either packed project (parsed as XML), every listed source in the
# package and byte-identical to the tree, and the bindings at the pinned sha256 (tests/release/check-fable-views.py).
python3 -I "$REPO_ROOT/tests/release/check-fable-views.py" --root "$REPO_ROOT" \
  --render "$TMP/feed/$PACKAGE" --browser "$TMP/feed/$BROWSER_PACKAGE"
mkdir -p "$TMP/unpacked-browser"
unzip -q "$TMP/feed/$BROWSER_PACKAGE" -d "$TMP/unpacked-browser"
cmp "$TMP/unpacked-browser/fable/FS.GG.Game.Render.Browser.fsproj" src/Render.Browser/Fable/FS.GG.Game.Render.Browser.fsproj
cmp "$TMP/unpacked-browser/fable-compatibility/compatibility-profile.v1.json" src/Render.Browser/Fable/compatibility-profile.v1.json
for corpus in babylon pixi; do
  for file in package.json package-lock.json; do
    cmp "$TMP/unpacked-browser/js-bindings/$corpus/$file" "$REPO_ROOT/artifacts/js-bindings/$corpus/npm/$file"
  done
done
NUSPEC="$(unzip -p "$TMP/feed/$BROWSER_PACKAGE" FS.GG.Game.Render.Browser.nuspec)"
grep -q "<dependency id=\"FS.GG.Game.Render\" version=\"$PACK_VERSION\"" <<<"$NUSPEC" || {
  echo "test-render-browser-host: the browser host package does not depend on FS.GG.Game.Render $PACK_VERSION" >&2
  exit 1
}
if grep -Eq 'Bindings|Fable\.Metadata|Fable\.Package\.SDK' <<<"$NUSPEC"; then
  echo "test-render-browser-host: the browser host package depends on a repository-only project or build tool" >&2
  exit 1
fi
"$REPO_ROOT/tests/release/test-render-browser-package-consumer.sh" --packages "$TMP/feed" --version "$PACK_VERSION"

# The generator's sample is its output for the smoke's arguments, byte for byte, so the template the smoke compiles
# and runs the conformance kit on is the template a game gets (GAME-RENDER-ADOPT-01.1, FS.GG.Game#745).
"$SCRIPT_DIR/new-game-renderers.sh" --namespace FS.GG.Game.Render.Browser.Smoke --module GeneratedRenderers \
  --state int --label "Generated renderers" --stdout | cmp - tests/Render.Browser.Smoke/GeneratedRenderers.fs || {
  echo "test-render-browser-host: tests/Render.Browser.Smoke/GeneratedRenderers.fs is not the generator's output; regenerate it" >&2
  exit 1
}

# 2. Fable compile of the host and smoke.
dotnet restore tests/Render.Browser.Smoke/FS.GG.Game.Render.Browser.Smoke.fsproj >/dev/null
dotnet fable tests/Render.Browser.Smoke --outDir "$TMP/js" --noCache
echo

# The exact npm closures the bindings were qualified against, resolved by an import map because the Fable output
# imports `@babylonjs/core/...` and `pixi.js` by bare specifier. Pixi resolves to its self-contained `dist/pixi.mjs`
# (the `lib/` modules need a bundler for two CommonJS-only dependencies), as the Templates browser smoke does.
for corpus in babylon pixi; do
  mkdir -p "$TMP/js/npm-$corpus"
  cp "$REPO_ROOT/artifacts/js-bindings/$corpus/npm/package.json" "$REPO_ROOT/artifacts/js-bindings/$corpus/npm/package-lock.json" "$TMP/js/npm-$corpus/"
  (cd "$TMP/js/npm-$corpus" && npm ci --ignore-scripts --omit=dev --no-audit --no-fund >/dev/null)
done
grep -Fq "$BABYLON_INTEGRITY" "$TMP/js/npm-babylon/package-lock.json" || {
  echo "test-render-browser-host: the installed Babylon closure is not the pinned @babylonjs/core integrity" >&2
  exit 1
}
grep -Fq "$PIXI_INTEGRITY" "$TMP/js/npm-pixi/package-lock.json" || {
  echo "test-render-browser-host: the installed Pixi closure is not the pinned pixi.js integrity" >&2
  exit 1
}
[[ -f $TMP/js/npm-babylon/node_modules/@babylonjs/core/Engines/engineStore.js ]] || {
  echo "test-render-browser-host: @babylonjs/core did not install" >&2
  exit 1
}
[[ -f $TMP/js/npm-pixi/node_modules/pixi.js/dist/pixi.mjs ]] || {
  echo "test-render-browser-host: pixi.js did not install" >&2
  exit 1
}

printf '%s\n' \
  '<!doctype html>' \
  '<meta charset="utf-8">' \
  '<title>FS.GG.Game.Render browser host smoke</title>' \
  '<script type="importmap">{"imports":{"@babylonjs/core/":"./npm-babylon/node_modules/@babylonjs/core/","@babylonjs/loaders/":"./npm-babylon/node_modules/@babylonjs/loaders/","pixi.js":"./npm-pixi/node_modules/pixi.js/dist/pixi.mjs","pixi.js/events":"./npm-pixi/node_modules/pixi.js/dist/pixi.mjs"}}</script>' \
  '<script type="module" src="./Program.js"></script>' \
  >"$TMP/js/index.html"

# 3. Real browser run, in REAL time. A WebGL canvas needs compositor frames (Babylon compiles shaders
#    asynchronously and the smoke waits for the first drawn frame), and a virtual-time browser run delivers only a
#    couple of animation frames, so the page reports through the console instead: it logs one
#    RENDER-SMOKE-RESULT line (counts and a base64 report) when it is done.
#
#    The browser is driven by tests/Render.Browser.Smoke/driver/run-smoke.mjs (Playwright, pinned in its
#    package-lock.json), because the interaction checks (FS.GG.Game#751) need REAL input: the driver performs the
#    page's pointer and keyboard requests with Playwright's trusted page.mouse and page.keyboard. It writes every
#    console line to the log this script parses and stops the browser once the result is reported, bounded.
#
#    The default flags give WebGL through SwiftShader (software), which is what a CI runner has;
#    RENDER_SMOKE_BROWSER_FLAGS selects a GPU instead. The result names the WebGL renderer that ran.
BROWSER_FLAGS="${RENDER_SMOKE_BROWSER_FLAGS:---headless=new --no-sandbox --disable-gpu --enable-unsafe-swiftshader --use-angle=swiftshader}"
BROWSER_TIMEOUT="${RENDER_SMOKE_TIMEOUT_SECONDS:-300}"
mkdir -p "$TMP/driver"
cp tests/Render.Browser.Smoke/driver/package.json tests/Render.Browser.Smoke/driver/package-lock.json \
  tests/Render.Browser.Smoke/driver/run-smoke.mjs "$TMP/driver/"
(cd "$TMP/driver" && npm ci --ignore-scripts --no-audit --no-fund >/dev/null)
: >"$TMP/browser.stderr"
# shellcheck disable=SC2086
RENDER_SMOKE_PART="$PART" node "$TMP/driver/run-smoke.mjs" "$TMP/js/index.html" "$BROWSER" "$TMP/browser.stderr" "$BROWSER_TIMEOUT" $BROWSER_FLAGS || {
  echo "test-render-browser-host: the real-input driver failed" >&2
  grep -E 'CONSOLE|Uncaught|DRIVER' "$TMP/browser.stderr" | head -n 40 >&2 || true
  exit 1
}

if ! grep -q 'RENDER-SMOKE-RESULT' "$TMP/browser.stderr"; then
  echo "test-render-browser-host: the page never reported a result within ${BROWSER_TIMEOUT}s (the module did not run or did not finish)" >&2
  grep -E 'CONSOLE|Uncaught|DRIVER' "$TMP/browser.stderr" | head -n 40 >&2 || true
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

# The margin, on every run: each section's duration, and the page's time against its timeout.
sed -n 's/^CONSOLE RENDER-SMOKE-SECTION \(.*\) \([0-9.]*\)$/test-render-browser-host: section \1: \2 s/p' "$TMP/browser.stderr"
PAGE_SECONDS="$(sed -n 's/^DRIVER page-seconds \([0-9.]*\) of .*/\1/p' "$TMP/browser.stderr" | tail -n 1)"
if [[ -n $PAGE_SECONDS ]]; then
  HEADROOM="$(python3 -I -c "import sys; print(f'{float(sys.argv[2]) / max(float(sys.argv[1]), 0.1):.1f}')" "$PAGE_SECONDS" "$BROWSER_TIMEOUT")"
  echo "test-render-browser-host: part $PART page took ${PAGE_SECONDS} s of its ${BROWSER_TIMEOUT} s timeout (${HEADROOM}x headroom)"
  if python3 -I -c "import sys; sys.exit(0 if float(sys.argv[1]) * 2 > float(sys.argv[2]) else 1)" "$PAGE_SECONDS" "$BROWSER_TIMEOUT"; then
    echo "::warning::test-render-browser-host: part $PART page took ${PAGE_SECONDS} s of its ${BROWSER_TIMEOUT} s timeout, under 2x headroom; shard or speed up the page rather than raising the timeout"
  fi
fi

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
echo "test-render-browser-host: OK — part $PART: $CHECKS browser checks passed; package=$PACKAGE browser=$("$BROWSER" --version 2>/dev/null | head -n 1) flags=[$BROWSER_FLAGS] webgl=[$WEBGL]"

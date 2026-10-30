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
# SvgBrowser surface, and that a later adapter can plug in. It does NOT exercise PixiJS or BabylonJS
# (their adapters wait for the Templates#808/#809 bindings).
#
#   usage: scripts/test-render-browser-host.sh
#   env:   RENDER_SMOKE_BROWSER=<chrome binary>   (default: first of chromium, google-chrome, chrome)
#          RENDER_SMOKE_KEEP=1                    keep the temp directory for inspection
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
MIN_CHECKS=42
PACK_VERSION="0.0.0-render-smoke"
PACKAGE="FS.GG.Game.Render.${PACK_VERSION}.nupkg"

for command in dotnet node unzip cmp grep sed; do
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

printf '%s\n' \
  '<!doctype html>' \
  '<meta charset="utf-8">' \
  '<title>FS.GG.Game.Render browser host smoke</title>' \
  '<script type="module" src="./Program.js"></script>' \
  >"$TMP/js/index.html"

# 3. Real browser run.
"$BROWSER" \
  --headless=new \
  --no-sandbox \
  --disable-gpu \
  --allow-file-access-from-files \
  --virtual-time-budget=10000 \
  --dump-dom "file://$TMP/js/index.html" \
  >"$TMP/browser-dom.html" 2>"$TMP/browser.stderr"

unescape() { sed -e 's/&lt;/</g' -e 's/&gt;/>/g' -e 's/&quot;/"/g' -e 's/&amp;/\&/g'; }

if ! grep -q 'id="smoke-result"' "$TMP/browser-dom.html"; then
  echo "test-render-browser-host: the page never reported a result (the module did not run)" >&2
  cat "$TMP/browser-dom.html" "$TMP/browser.stderr" >&2
  exit 1
fi

CHECKS="$(sed -n 's/.*id="smoke-result"[^>]*data-checks="\([0-9]*\)".*/\1/p' "$TMP/browser-dom.html" | head -n 1)"
FAILURES="$(sed -n 's/.*id="smoke-result"[^>]*data-failures="\([0-9]*\)".*/\1/p' "$TMP/browser-dom.html" | head -n 1)"
unescape <"$TMP/browser-dom.html" |
  sed -e 's|^.*<pre id="smoke-result"[^>]*>||' -e 's|</pre>.*$||' |
  grep -E '^(PASS|FAIL) ' || true

if [[ -z $CHECKS || -z $FAILURES ]]; then
  echo "test-render-browser-host: could not read the smoke result" >&2
  exit 1
fi

# The subject is asserted before the verdict: a smoke that ran fewer checks than it should have is a failure.
if [[ $CHECKS -lt $MIN_CHECKS ]]; then
  echo "test-render-browser-host: only $CHECKS checks ran (expected at least $MIN_CHECKS)" >&2
  exit 1
fi

if [[ $FAILURES -ne 0 ]] || ! grep -q 'data-smoke="pass"' "$TMP/browser-dom.html"; then
  echo "test-render-browser-host: $FAILURES of $CHECKS browser checks failed" >&2
  exit 1
fi

echo "test-render-browser-host: OK — $CHECKS browser checks passed; package=$PACKAGE browser=$("$BROWSER" --version 2>/dev/null | head -n 1)"

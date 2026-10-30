#!/usr/bin/env bash
#
# GAME-RENDER-MULTI-01.4 (FS.GG.Game#730): materialise the BabylonJS Fable bindings workspace from its pinned origin.
#
# The bindings are owned by FS.GG.Templates (`templates/fs-gg-fable-bindings`: BabylonJS from Templates#809 / PR #812,
# PixiJS from Templates#808 / PR #814) and are not published. This script instantiates that template from a PINNED
# Templates commit (eng/js-bindings/pin.env) into a git-ignored directory per corpus; the committed
# src/Render.Browser.Bindings project compiles each generated Bindings.fs for the browser host. Nothing generated is
# committed to this repository.
#
# Why the source and not the packed NuGet the Templates composition lane builds: a packed archive's bytes depend on
# the .NET SDK that compiled it (the same template compiled by SDK 10.0.401 and 10.0.403 packs different
# assemblies), and the NuGet lock file records the archive's content hash, so a committed lock cannot hold for a
# package built on the consumer's machine. Why not a ProjectReference to the generated project: the org lock-range
# gate requires every project a lock names to be a committed project file.
#
#   usage: scripts/materialize-js-bindings.sh <babylon|pixi|all> [output-dir]      (default: artifacts/js-bindings)
#   writes: <output-dir>/<corpus>/product/                 the generated workspace (its source is src/<id>/Bindings.fs)
#           <output-dir>/<corpus>/npm/package.json, package-lock.json   the exact npm closure of the corpus
#   env:    JS_BINDINGS_TEMPLATES_ROOT=<checkout>   use this FS.GG.Templates checkout instead of fetching it; its HEAD
#                                                   must be the pinned commit
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PIN="$REPO_ROOT/eng/js-bindings/pin.env"

die() {
  echo "materialize-js-bindings: $*" >&2
  exit 1
}

for command in dotnet node npm git sha256sum; do
  command -v "$command" >/dev/null || {
    echo "materialize-js-bindings: required command '$command' was not found" >&2
    exit 2
  }
done

[[ -f $PIN ]] || die "missing pin file $PIN"
pin() { sed -n "s/^$1=//p" "$PIN" | head -n 1; }

CORPUS="${1:-}"
case "$CORPUS" in
  babylon | pixi | all) ;;
  *) die "usage: materialize-js-bindings.sh <babylon|pixi|all> [output-dir]" ;;
esac
OUT="${2:-$REPO_ROOT/artifacts/js-bindings}"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"

TEMPLATES_REPOSITORY="$(pin TEMPLATES_REPOSITORY)"
TEMPLATES_COMMIT="$(pin TEMPLATES_COMMIT)"
for value in "$TEMPLATES_REPOSITORY" "$TEMPLATES_COMMIT"; do
  [[ -n $value ]] || die "the pin file is incomplete"
done

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# 1. The pinned template source.
if [[ -n ${JS_BINDINGS_TEMPLATES_ROOT:-} ]]; then
  head="$(git -C "$JS_BINDINGS_TEMPLATES_ROOT" rev-parse HEAD)"
  [[ $head == "$TEMPLATES_COMMIT" ]] ||
    die "JS_BINDINGS_TEMPLATES_ROOT is at $head, not the pinned $TEMPLATES_COMMIT"
  git -C "$JS_BINDINGS_TEMPLATES_ROOT" diff --quiet HEAD -- templates/fs-gg-fable-bindings ||
    die "JS_BINDINGS_TEMPLATES_ROOT has local changes under templates/fs-gg-fable-bindings"
  git -C "$JS_BINDINGS_TEMPLATES_ROOT" archive "$TEMPLATES_COMMIT" templates/fs-gg-fable-bindings | tar -x -C "$WORK"
else
  git init -q "$WORK/clone"
  git -C "$WORK/clone" fetch -q --depth 1 "$TEMPLATES_REPOSITORY" "$TEMPLATES_COMMIT"
  [[ $(git -C "$WORK/clone" rev-parse FETCH_HEAD) == "$TEMPLATES_COMMIT" ]] ||
    die "fetched commit does not match the pin"
  git -C "$WORK/clone" archive FETCH_HEAD templates/fs-gg-fable-bindings | tar -x -C "$WORK"
fi
TEMPLATE_DIR="$WORK/templates/fs-gg-fable-bindings"
[[ -f $TEMPLATE_DIR/.template.config/template.json ]] || die "the pinned commit has no fs-gg-fable-bindings template"

# 2. Instantiate it with a private template hive, so the host's `dotnet new` list is neither read nor changed.
export DOTNET_CLI_HOME="$WORK/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
mkdir -p "$DOTNET_CLI_HOME"
dotnet new install "$TEMPLATE_DIR" >/dev/null

materialize() {
  local corpus="$1" prefix project package version integrity product
  prefix="$(printf '%s' "$corpus" | tr '[:lower:]' '[:upper:]')"
  project="$(pin "${prefix}_PROJECT_NAME")"
  package="$(pin "${prefix}_NPM_PACKAGE")"
  version="$(pin "${prefix}_NPM_VERSION")"
  integrity="$(pin "${prefix}_NPM_INTEGRITY")"
  for value in "$project" "$package" "$version" "$integrity"; do
    [[ -n $value ]] || die "the pin file is incomplete for $corpus"
  done

  rm -rf "${OUT:?}/$corpus"
  mkdir -p "$OUT/$corpus/npm"
  product="$OUT/$corpus/product"
  dotnet new fs-gg-fable-bindings -o "$product" --name "$project" --npmPackage="$package" \
    --npmVersion "$version" --bindingTarget browser --lifecycle none >/dev/null

  grep -Fq "\"${package}\": \"$version\"" "$product/package.json" ||
    die "the template does not pin $package $version"
  grep -Fq "$integrity" "$product/package-lock.json" ||
    die "the template's npm lock does not carry the pinned $package integrity"
  [[ -f $product/src/$project/Bindings.fs ]] || die "the instantiated template has no src/$project/Bindings.fs"

  # The generated source must be exactly the source the pin names: a moved template, a changed generator or a
  # tampered file all show here, before anything compiles it.
  expected="$(pin "${prefix}_BINDINGS_SHA256")"
  actual="$(sha256sum "$product/src/$project/Bindings.fs" | cut -d' ' -f1)"
  [[ -n $expected ]] || die "the pin file has no ${prefix}_BINDINGS_SHA256 (generated Bindings.fs is $actual)"
  [[ $actual == "$expected" ]] ||
    die "$corpus: the generated Bindings.fs is $actual, but the pin names $expected (${prefix}_BINDINGS_SHA256 in eng/js-bindings/pin.env)"

  cp "$product/package.json" "$product/package-lock.json" "$OUT/$corpus/npm/"
  echo "materialize-js-bindings: OK — $corpus: $project from FS.GG.Templates ${TEMPLATES_COMMIT:0:8}, $package $version, Bindings.fs sha256=$actual"
}

if [[ $CORPUS == all ]]; then
  materialize babylon
  materialize pixi
else
  materialize "$CORPUS"
fi

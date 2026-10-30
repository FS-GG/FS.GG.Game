#!/usr/bin/env bash
#
# GAME-RENDER-MULTI-01.4 (FS.GG.Game#730): materialise the BabylonJS Fable bindings workspace from its pinned origin.
#
# The bindings are owned by FS.GG.Templates (`templates/fs-gg-fable-bindings`, Templates#809 / PR #812) and are not
# published. This script instantiates that template from a PINNED Templates commit (eng/babylon-bindings/pin.env)
# into a git-ignored directory; src/Render.Browser references the generated library project and Fable compiles its
# source with the host. Nothing generated is committed to this repository.
#
# Why a project reference and not the packed NuGet the Templates composition lane builds: a packed archive's bytes
# depend on the .NET SDK that compiled it (the same template compiled by SDK 10.0.401 and 10.0.403 packs
# different assemblies), and the NuGet lock file records the archive's content hash, so a committed lock cannot
# hold for a package built on the consumer's machine. A project reference has no content hash, and the generated
# workspace keeps its own committed, locked restore.
#
#   usage: scripts/materialize-babylon-bindings.sh [output-dir]      (default: artifacts/babylon-bindings)
#   writes: <output-dir>/product/                      the generated workspace (its library is src/<id>/<id>.fsproj)
#           <output-dir>/npm/package.json, package-lock.json   the exact Babylon npm closure
#   env:    BABYLON_BINDINGS_TEMPLATES_ROOT=<checkout>   use this FS.GG.Templates checkout instead of fetching it;
#                                                          its HEAD must be the pinned commit
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PIN="$REPO_ROOT/eng/babylon-bindings/pin.env"

die() {
  echo "materialize-babylon-bindings: $*" >&2
  exit 1
}

for command in dotnet node npm git sha256sum; do
  command -v "$command" >/dev/null || {
    echo "materialize-babylon-bindings: required command '$command' was not found" >&2
    exit 2
  }
done

[[ -f $PIN ]] || die "missing pin file $PIN"
pin() { sed -n "s/^$1=//p" "$PIN" | head -n 1; }
TEMPLATES_REPOSITORY="$(pin TEMPLATES_REPOSITORY)"
TEMPLATES_COMMIT="$(pin TEMPLATES_COMMIT)"
PROJECT_NAME="$(pin BINDINGS_PROJECT_NAME)"
BABYLON_VERSION="$(pin BABYLON_NPM_VERSION)"
BABYLON_INTEGRITY="$(pin BABYLON_CORE_INTEGRITY)"
for value in "$TEMPLATES_REPOSITORY" "$TEMPLATES_COMMIT" "$PROJECT_NAME" "$BABYLON_VERSION" "$BABYLON_INTEGRITY"; do
  [[ -n $value ]] || die "the pin file is incomplete"
done

OUT="${1:-$REPO_ROOT/artifacts/babylon-bindings}"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
rm -rf "$OUT/product" "$OUT/npm"
mkdir -p "$OUT/npm"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# 1. The pinned template source.
if [[ -n ${BABYLON_BINDINGS_TEMPLATES_ROOT:-} ]]; then
  head="$(git -C "$BABYLON_BINDINGS_TEMPLATES_ROOT" rev-parse HEAD)"
  [[ $head == "$TEMPLATES_COMMIT" ]] ||
    die "BABYLON_BINDINGS_TEMPLATES_ROOT is at $head, not the pinned $TEMPLATES_COMMIT"
  git -C "$BABYLON_BINDINGS_TEMPLATES_ROOT" diff --quiet HEAD -- templates/fs-gg-fable-bindings ||
    die "BABYLON_BINDINGS_TEMPLATES_ROOT has local changes under templates/fs-gg-fable-bindings"
  git -C "$BABYLON_BINDINGS_TEMPLATES_ROOT" archive "$TEMPLATES_COMMIT" templates/fs-gg-fable-bindings | tar -x -C "$WORK"
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
PRODUCT="$OUT/product"
dotnet new fs-gg-fable-bindings -o "$PRODUCT" --name "$PROJECT_NAME" --npmPackage=@babylonjs/core \
  --npmVersion "$BABYLON_VERSION" --bindingTarget browser --lifecycle none >/dev/null

grep -Fq "\"@babylonjs/core\": \"$BABYLON_VERSION\"" "$PRODUCT/package.json" ||
  die "the template does not pin @babylonjs/core $BABYLON_VERSION"
grep -Fq "$BABYLON_INTEGRITY" "$PRODUCT/package-lock.json" ||
  die "the template's npm lock does not carry the pinned @babylonjs/core integrity"
[[ -f $PRODUCT/src/$PROJECT_NAME/$PROJECT_NAME.fsproj ]] || die "the instantiated template has no src/$PROJECT_NAME/$PROJECT_NAME.fsproj"

cp "$PRODUCT/package.json" "$PRODUCT/package-lock.json" "$OUT/npm/"

echo "materialize-babylon-bindings: OK — $PROJECT_NAME from FS.GG.Templates ${TEMPLATES_COMMIT:0:8}, babylon $BABYLON_VERSION, Bindings.fs sha256=$(sha256sum "$PRODUCT/src/$PROJECT_NAME/Bindings.fs" | cut -d' ' -f1)"

#!/usr/bin/env bash
#
# GAME-RENDER-MULTI-01.4 (FS.GG.Game#730): build the packed BabylonJS Fable bindings into a local NuGet feed.
#
# The bindings are owned by FS.GG.Templates (`templates/fs-gg-fable-bindings`, Templates#809 / PR #812). That
# template is not published on a feed, so this script follows the consumption route the Templates composition
# lane established: instantiate the template from a PINNED Templates commit, restore it in locked mode, build and
# pack it, then let src/Render.Browser consume the .nupkg with an ordinary PackageReference. Nothing generated
# is committed to this repository.
#
#   usage: scripts/build-babylon-bindings-feed.sh [output-dir]      (default: artifacts/babylon-bindings)
#   writes: <output-dir>/feed/<BINDINGS_PACKAGE_ID>.<version>.nupkg   (the local NuGet feed)
#           <output-dir>/npm/package.json, package-lock.json          (the exact Babylon npm closure)
#   env:    BABYLON_BINDINGS_TEMPLATES_ROOT=<checkout>   use this FS.GG.Templates checkout instead of cloning; its
#                                                          HEAD must be the pinned commit
#
# The packed archive is normalised (fixed timestamps, stored entries, fixed order) because `dotnet pack` stamps
# the build time into the zip, which would otherwise make the NuGet lock file's contentHash differ on every
# machine. The archive's members are untouched.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PIN="$REPO_ROOT/eng/babylon-bindings/pin.env"

die() {
  echo "build-babylon-bindings-feed: $*" >&2
  exit 1
}

for command in dotnet node npm git python3 sha256sum; do
  command -v "$command" >/dev/null || {
    echo "build-babylon-bindings-feed: required command '$command' was not found" >&2
    exit 2
  }
done

[[ -f $PIN ]] || die "missing pin file $PIN"
pin() { sed -n "s/^$1=//p" "$PIN" | head -n 1; }
TEMPLATES_REPOSITORY="$(pin TEMPLATES_REPOSITORY)"
TEMPLATES_COMMIT="$(pin TEMPLATES_COMMIT)"
PACKAGE_ID="$(pin BINDINGS_PACKAGE_ID)"
PACKAGE_VERSION="$(pin BINDINGS_PACKAGE_VERSION)"
BABYLON_VERSION="$(pin BABYLON_NPM_VERSION)"
BABYLON_INTEGRITY="$(pin BABYLON_CORE_INTEGRITY)"
for value in "$TEMPLATES_REPOSITORY" "$TEMPLATES_COMMIT" "$PACKAGE_ID" "$PACKAGE_VERSION" "$BABYLON_VERSION" "$BABYLON_INTEGRITY"; do
  [[ -n $value ]] || die "the pin file is incomplete"
done

OUT="${1:-$REPO_ROOT/artifacts/babylon-bindings}"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
rm -rf "$OUT/feed" "$OUT/npm"
mkdir -p "$OUT/feed" "$OUT/npm"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# 1. The pinned template source.
if [[ -n ${BABYLON_BINDINGS_TEMPLATES_ROOT:-} ]]; then
  head="$(git -C "$BABYLON_BINDINGS_TEMPLATES_ROOT" rev-parse HEAD)"
  [[ $head == "$TEMPLATES_COMMIT" ]] ||
    die "BABYLON_BINDINGS_TEMPLATES_ROOT is at $head, not the pinned $TEMPLATES_COMMIT"
  git -C "$BABYLON_BINDINGS_TEMPLATES_ROOT" diff --quiet HEAD -- templates/fs-gg-fable-bindings ||
    die "BABYLON_BINDINGS_TEMPLATES_ROOT has local changes under templates/fs-gg-fable-bindings"
  mkdir -p "$WORK/templates"
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

# 2. Instantiate it. A private template hive and package cache keep the host's `dotnet new` list and NuGet
#    cache out of the result (the template's own Directory.Build.props sets a workspace-local package folder).
export DOTNET_CLI_HOME="$WORK/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
mkdir -p "$DOTNET_CLI_HOME"
dotnet new install "$TEMPLATE_DIR" >/dev/null
PRODUCT="$WORK/product"
dotnet new fs-gg-fable-bindings -o "$PRODUCT" --name "$PACKAGE_ID" --npmPackage=@babylonjs/core \
  --npmVersion "$BABYLON_VERSION" --bindingTarget browser --lifecycle none >/dev/null

grep -Fq "\"@babylonjs/core\": \"$BABYLON_VERSION\"" "$PRODUCT/package.json" ||
  die "the template does not pin @babylonjs/core $BABYLON_VERSION"
grep -Fq "$BABYLON_INTEGRITY" "$PRODUCT/package-lock.json" ||
  die "the template's npm lock does not carry the pinned @babylonjs/core integrity"

# 3. Locked restore, build, pack.
PROJECT="$PRODUCT/src/$PACKAGE_ID/$PACKAGE_ID.fsproj"
[[ -f $PROJECT ]] || die "the instantiated template has no $PROJECT"
dotnet restore "$PROJECT" --locked-mode >/dev/null
# PathMap + ContinuousIntegrationBuild keep the temporary workspace path out of the assembly (its debug
# directory records the pdb path), so the archive is the same on every machine.
dotnet build "$PROJECT" -c Release --no-restore -p:ContinuousIntegrationBuild=true -p:PathMap="$PRODUCT=/_/bindings" >/dev/null
dotnet pack "$PROJECT" -c Release --no-build -o "$WORK/pack" >/dev/null
RAW="$WORK/pack/$PACKAGE_ID.$PACKAGE_VERSION.nupkg"
[[ -f $RAW ]] || die "expected package was not produced: $PACKAGE_ID.$PACKAGE_VERSION.nupkg"

# 4. Normalise the archive so its content hash is a function of its members only.
python3 -I - "$RAW" "$OUT/feed/$PACKAGE_ID.$PACKAGE_VERSION.nupkg" "$PACKAGE_ID" "$PACKAGE_VERSION" <<'PY'
import sys, zipfile

source, target, package_id, version = sys.argv[1:5]
with zipfile.ZipFile(source) as archive:
    names = sorted(archive.namelist())
    members = {name: archive.read(name) for name in names}

nuspec = members.get(f"{package_id}.nuspec", b"").decode()
for needle in (f"<id>{package_id}</id>", f"<version>{version}</version>"):
    if needle not in nuspec:
        sys.exit(f"build-babylon-bindings-feed: the nuspec lacks {needle}")
if not any(name.startswith("lib/netstandard2.1/") and name.endswith(".dll") for name in names):
    sys.exit("build-babylon-bindings-feed: the package has no netstandard2.1 assembly")

with zipfile.ZipFile(target, "w", zipfile.ZIP_STORED) as out:
    for name in names:
        info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_STORED
        info.external_attr = 0o644 << 16
        out.writestr(info, members[name])
PY

cp "$PRODUCT/package.json" "$PRODUCT/package-lock.json" "$OUT/npm/"

echo "build-babylon-bindings-feed: OK — $PACKAGE_ID $PACKAGE_VERSION from FS.GG.Templates ${TEMPLATES_COMMIT:0:8}, babylon $BABYLON_VERSION, sha256=$(sha256sum "$OUT/feed/$PACKAGE_ID.$PACKAGE_VERSION.nupkg" | cut -d' ' -f1)"

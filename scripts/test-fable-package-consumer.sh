#!/usr/bin/env bash
#
# M3 packed-artifact Fable spike. Packs Game.Core, restores a consumer in an isolated directory and
# package cache, compiles only the package-derived source view, and executes the result under Node.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SPIKE_VERSION="0.12.0-m3-spike"
PACKAGE_NAME="FS.GG.Game.Core.${SPIKE_VERSION}.nupkg"

for command in dotnet node unzip sha256sum; do
  command -v "$command" >/dev/null || {
    echo "test-fable-package-consumer: required command '$command' was not found" >&2
    exit 2
  }
done

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$TMP/feed" "$TMP/consumer" "$TMP/packages"

dotnet tool restore
dotnet pack "$REPO_ROOT/src/Game.Core/FS.GG.Game.Core.fsproj" \
  -c Release \
  -p:PackageVersion="$SPIKE_VERSION" \
  -o "$TMP/feed"

PACKAGE="$TMP/feed/$PACKAGE_NAME"
[[ -f $PACKAGE ]] || {
  echo "test-fable-package-consumer: expected package was not produced: $PACKAGE_NAME" >&2
  exit 1
}

PACKAGE_LIST="$(unzip -Z1 "$PACKAGE")"
required_entries=(
  "fable/FS.GG.Game.Core.fsproj"
  "fable/Primitives.fsi"
  "fable/Primitives.fs"
  "fable/Pathfinding.fsi"
  "fable/Pathfinding.fs"
  "fable/Edges.fsi"
  "fable/Edges.fs"
  "fable/Los.fsi"
  "fable/Los.fs"
  "fable-compatibility/compatibility-profile.v1.json"
  "fable-compatibility/fixture-schema.v1.json"
  "fable-compatibility/toolchain-manifest.v1.json"
)

for entry in "${required_entries[@]}"; do
  grep -Fx "$entry" <<<"$PACKAGE_LIST" >/dev/null || {
    echo "test-fable-package-consumer: package is missing $entry" >&2
    exit 1
  }
done

if grep -E '^fable/(FixedStep|Loop|Grids|Ballistics|Visibility|Effects|Ai)\\.fs$' <<<"$PACKAGE_LIST" >/dev/null; then
  echo "test-fable-package-consumer: the bounded Fable view contains an unsupported source file" >&2
  exit 1
fi

cp \
  "$REPO_ROOT/tests/Game.Core.Fable.Tests/package-consumer/FS.GG.Game.Core.Fable.Consumer.fsproj" \
  "$REPO_ROOT/tests/Game.Core.Fable.Tests/package-consumer/Program.fs" \
  "$TMP/consumer/"

# A generated config with <clear/> prevents a developer's or runner's package-source mapping from
# silently excluding the isolated feed. NuGet.org supplies only the pinned toolchain dependencies.
dotnet new nugetconfig -o "$TMP/consumer" --force >/dev/null
dotnet nuget add source "$TMP/feed" \
  --name m3-spike \
  --configfile "$TMP/consumer/nuget.config" >/dev/null

NUGET_PACKAGES="$TMP/packages" \
  dotnet restore "$TMP/consumer/FS.GG.Game.Core.Fable.Consumer.fsproj" \
    --configfile "$TMP/consumer/nuget.config" \
    -p:SpikePackageVersion="$SPIKE_VERSION"

NUGET_PACKAGES="$TMP/packages" \
  dotnet fable "$TMP/consumer/FS.GG.Game.Core.Fable.Consumer.fsproj" \
    --outDir "$TMP/javascript" \
    --noRestore \
    --noCache

node "$TMP/javascript/Program.js"

PACKAGE_SHA256="$(sha256sum "$PACKAGE" | cut -d ' ' -f 1)"
printf 'test-fable-package-consumer: OK — package=%s sha256=%s fable=5.13.0 node=%s\n' \
  "$PACKAGE_NAME" \
  "$PACKAGE_SHA256" \
  "$(node --version)"

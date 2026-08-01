#!/usr/bin/env bash
#
# M4 packed-artifact lockstep gate. Packs Game.Core, restores a consumer in an isolated directory and
# package cache, then executes the same generated fixtures against the .NET binary and package-derived
# Fable source view. Each runtime is compared with the checked-in canonical binary oracle.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CONFORMANCE_VERSION="0.13.0-m4-conformance"
PACKAGE_NAME="FS.GG.Game.Core.${CONFORMANCE_VERSION}.nupkg"
RUNTIME="${1:---all}"
EXPECTED="$REPO_ROOT/tests/Game.Core.Fable.Tests/fixtures/v1/expected.bin"
BROWSER=""

case "$RUNTIME" in
  --all|--dotnet|--fable|--browser) ;;
  *)
    echo "usage: $0 [--all|--dotnet|--fable|--browser]" >&2
    exit 2
    ;;
esac

for command in dotnet unzip sha256sum python3; do
  command -v "$command" >/dev/null || {
    echo "test-fable-package-consumer: required command '$command' was not found" >&2
    exit 2
  }
done

if [[ $RUNTIME != --dotnet ]]; then
  command -v node >/dev/null || {
    echo "test-fable-package-consumer: required command 'node' was not found" >&2
    exit 2
  }
fi

if [[ $RUNTIME == --browser ]]; then
  for candidate in chromium google-chrome; do
    if command -v "$candidate" >/dev/null; then
      BROWSER="$candidate"
      break
    fi
  done

  [[ -n $BROWSER ]] || {
    echo "test-fable-package-consumer: required browser 'chromium' or 'google-chrome' was not found" >&2
    exit 2
  }
fi

python3 "$REPO_ROOT/scripts/generate-fable-lockstep-cases.py" --check

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$TMP/feed" "$TMP/consumer" "$TMP/packages"

dotnet tool restore
dotnet pack "$REPO_ROOT/src/Game.Core/FS.GG.Game.Core.fsproj" \
  -c Release \
  -p:PackageVersion="$CONFORMANCE_VERSION" \
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
  "fable-compatibility/fixtures/v1/cases.json"
  "fable-compatibility/fixtures/v1/expected.bin"
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
  "$REPO_ROOT/tests/Game.Core.Fable.Tests/shared/GeneratedCases.fs" \
  "$REPO_ROOT/tests/Game.Core.Fable.Tests/shared/FixtureProtocol.fs" \
  "$TMP/consumer/"

# A generated config with <clear/> prevents a developer's or runner's package-source mapping from
# silently excluding the isolated feed. NuGet.org supplies only the pinned toolchain dependencies.
dotnet new nugetconfig -o "$TMP/consumer" --force >/dev/null
dotnet nuget add source "$TMP/feed" \
  --name m4-conformance \
  --configfile "$TMP/consumer/nuget.config" >/dev/null

NUGET_PACKAGES="$TMP/packages" \
  dotnet restore "$TMP/consumer/FS.GG.Game.Core.Fable.Consumer.fsproj" \
    --configfile "$TMP/consumer/nuget.config" \
    -p:ConformancePackageVersion="$CONFORMANCE_VERSION"

run_and_decode() {
  local runtime="$1"
  local output="$2"
  local hex

  case "$runtime" in
    dotnet)
      hex="$(
        NUGET_PACKAGES="$TMP/packages" \
          dotnet run \
            --project "$TMP/consumer/FS.GG.Game.Core.Fable.Consumer.fsproj" \
            --no-restore \
            -p:ConformancePackageVersion="$CONFORMANCE_VERSION" |
          sed -n 's/^FSGG_FIXTURES_V1_HEX=//p'
      )"
      ;;
    fable|browser)
      NUGET_PACKAGES="$TMP/packages" \
        dotnet fable "$TMP/consumer/FS.GG.Game.Core.Fable.Consumer.fsproj" \
          --outDir "$TMP/javascript" \
          --noRestore \
          --noCache

      if [[ $runtime == fable ]]; then
        hex="$(node "$TMP/javascript/Program.js" | sed -n 's/^FSGG_FIXTURES_V1_HEX=//p')"
      else
        printf '%s\n' \
          '<!doctype html>' \
          '<meta charset="utf-8">' \
          '<title>FS.GG.Game.Core Fable lockstep</title>' \
          '<script>console.log = (...args) => { document.body.textContent = args.join(" "); };</script>' \
          '<script type="module" src="./Program.js"></script>' \
          >"$TMP/javascript/index.html"
        "$BROWSER" \
          --headless \
          --no-sandbox \
          --disable-gpu \
          --allow-file-access-from-files \
          --virtual-time-budget=5000 \
          --dump-dom "file://$TMP/javascript/index.html" \
          >"$TMP/browser-dom.html" 2>"$TMP/chromium.stderr"
        hex="$(sed -n 's/.*FSGG_FIXTURES_V1_HEX=\([0-9a-f]*\).*/\1/p' "$TMP/browser-dom.html")"
      fi
      ;;
  esac

  if [[ -z $hex || $hex =~ [^0-9a-f] || $((${#hex} % 2)) -ne 0 ]]; then
    echo "test-fable-package-consumer: $runtime runner emitted invalid canonical hex" >&2
    if [[ $runtime == browser && -f $TMP/chromium.stderr ]]; then
      printf '%s\n' 'browser DOM:' >&2
      cat "$TMP/browser-dom.html" >&2
      cat "$TMP/chromium.stderr" >&2
    fi
    exit 1
  fi

  printf '%s' "$hex" |
    python3 -c 'import sys; sys.stdout.buffer.write(bytes.fromhex(sys.stdin.read()))' >"$output"
}

if [[ $RUNTIME == --all || $RUNTIME == --dotnet ]]; then
  run_and_decode dotnet "$TMP/actual-dotnet.bin"

  if [[ ${UPDATE_EXPECTED:-0} == 1 ]]; then
    cp "$TMP/actual-dotnet.bin" "$EXPECTED"
    echo "test-fable-package-consumer: updated ${EXPECTED#"$REPO_ROOT/"}"
  fi

  [[ -f $EXPECTED ]] || {
    echo "test-fable-package-consumer: missing canonical oracle: ${EXPECTED#"$REPO_ROOT/"}" >&2
    exit 1
  }

  if ! python3 "$REPO_ROOT/scripts/compare-fable-lockstep-fixtures.py" \
    "$EXPECTED" "$TMP/actual-dotnet.bin"; then
    echo "lockstep runtime profile: dotnet (package=$PACKAGE_NAME)" >&2
    exit 1
  fi
fi

if [[ $RUNTIME == --all || $RUNTIME == --fable ]]; then
  [[ -f $EXPECTED ]] || {
    echo "test-fable-package-consumer: missing canonical oracle: ${EXPECTED#"$REPO_ROOT/"}" >&2
    exit 1
  }

  run_and_decode fable "$TMP/actual-fable.bin"
  if ! python3 "$REPO_ROOT/scripts/compare-fable-lockstep-fixtures.py" \
    "$EXPECTED" "$TMP/actual-fable.bin"; then
    echo "lockstep runtime profile: fable-node (fable=5.13.0 node=$(node --version))" >&2
    exit 1
  fi
fi

if [[ $RUNTIME == --browser ]]; then
  [[ -f $EXPECTED ]] || {
    echo "test-fable-package-consumer: missing canonical oracle: ${EXPECTED#"$REPO_ROOT/"}" >&2
    exit 1
  }

  run_and_decode browser "$TMP/actual-browser.bin"
  if ! python3 "$REPO_ROOT/scripts/compare-fable-lockstep-fixtures.py" \
    "$EXPECTED" "$TMP/actual-browser.bin"; then
    echo "lockstep runtime profile: fable-browser (fable=5.13.0 browser=$($BROWSER --version))" >&2
    cat "$TMP/chromium.stderr" >&2
    exit 1
  fi
fi

PACKAGE_SHA256="$(sha256sum "$PACKAGE" | cut -d ' ' -f 1)"
printf 'test-fable-package-consumer: OK — runtime=%s package=%s sha256=%s profile=fs-gg-game-core-fable-lockstep-v1 fixture-schema=1 fsharp-core=10.1.302 fable=5.13.0 node=%s browser=%s\n' \
  "${RUNTIME#--}" \
  "$PACKAGE_NAME" \
  "$PACKAGE_SHA256" \
  "$(node --version 2>/dev/null || printf 'not-required')" \
  "$($BROWSER --version 2>/dev/null || printf 'not-required')"

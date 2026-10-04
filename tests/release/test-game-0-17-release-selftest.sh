#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT

make_fixture() {
  local fixture="$1"
  mkdir -p "$fixture/.github/workflows"
  cp "$root/Directory.Build.local.props" "$fixture/Directory.Build.local.props"
  cp "$root/.github/workflows/release.yml" "$fixture/.github/workflows/release.yml"
}

positive="$scratch/positive"
make_fixture "$positive"
"$root/tests/release/test-game-0-17-release.sh" --root "$positive" --source-only
echo "positive control accepted both actual dotnet nuget push operations"

expect_push_mutation_red() {
  local label="$1" indexes="$2" output
  local fixture="$scratch/$label"
  make_fixture "$fixture"
  python3 - "$fixture/.github/workflows/release.yml" "$indexes" <<'PY'
import pathlib, sys

path = pathlib.Path(sys.argv[1])
selected = {int(value) for value in sys.argv[2].split(",")}
needle = 'dotnet nuget push "artifacts/packages/*.nupkg"'
text = path.read_text(encoding="utf-8")
parts = text.split(needle)
if len(parts) != 3:
    raise SystemExit(f"fixture expected two push operations, observed {len(parts) - 1}")
rebuilt = parts[0]
for index, tail in enumerate(parts[1:]):
    rebuilt += ("echo package-push-disabled" if index in selected else needle) + tail
path.write_text(rebuilt, encoding="utf-8")
PY
  if output=$("$root/tests/release/test-game-0-17-release.sh" --root "$fixture" --source-only 2>&1); then
    echo "$label mutation unexpectedly passed" >&2
    exit 1
  fi
  grep -q "exactly two actual dotnet nuget push operations" <<<"$output" || {
    echo "$label mutation failed for the wrong reason: $output" >&2
    exit 1
  }
  echo "$label witness: substituted push operation rejected"
}

expect_push_mutation_red first-push 0
expect_push_mutation_red second-push 1
expect_push_mutation_red both-pushes 0,1

version_fixture="$scratch/version"
make_fixture "$version_fixture"
sed -i 's:<Version>0.17.0</Version>:<Version>0.17.1</Version>:' "$version_fixture/Directory.Build.local.props"
if output=$("$root/tests/release/test-game-0-17-release.sh" --root "$version_fixture" --source-only 2>&1); then
  echo "release gate inversion unexpectedly passed" >&2
  exit 1
fi
grep -q "release scalar must be 0.17.0" <<<"$output" || {
  echo "release gate inversion failed for the wrong reason: $output" >&2
  exit 1
}
echo "version witness: 0.17.1 scalar rejected"

baseline_fixture="$scratch/baseline"
make_fixture "$baseline_fixture"
sed -i 's:>0.16.0</PackageValidationBaselineVersion>:>0.15.0</PackageValidationBaselineVersion>:' "$baseline_fixture/Directory.Build.local.props"
if output=$("$root/tests/release/test-game-0-17-release.sh" --root "$baseline_fixture" --source-only 2>&1); then
  echo "foreign API baseline mutation unexpectedly passed" >&2; exit 1
fi
grep -q 'existing packages must use the published 0.16.0 API baseline' <<<"$output"
echo "baseline witness: foreign predecessor rejected"

roster_fixture="$scratch/roster"
make_fixture "$roster_fixture"
sed -i 's:\[ "$count" = 4 \]:[ "$count" = 3 ]:' "$roster_fixture/.github/workflows/release.yml"
if output=$("$root/tests/release/test-game-0-17-release.sh" --root "$roster_fixture" --source-only 2>&1); then
  echo "missing adapter custody mutation unexpectedly passed" >&2; exit 1
fi
grep -q 'release custody and dual-feed readback must each require four packages' <<<"$output"
echo "roster witness: missing adapter custody rejected"

# These controls exercise exact artifact admission before any metadata/CLR consumer work can launch.
missing="$scratch/missing-adapter"
mkdir "$missing"
for id in Core Harness Render; do : > "$missing/FS.GG.Game.$id.0.17.0.nupkg"; done
if output=$("$root/tests/release/test-game-0-17-release.sh" --packages "$missing" 2>&1); then
  echo "missing adapter package mutation unexpectedly passed" >&2; exit 1
fi
grep -q 'expected exactly the coherent 0.17.0 package set' <<<"$output"
echo "artifact witness: missing adapter refused before consumer launch"

extra="$scratch/extra-package"
mkdir "$extra"
for id in Core Harness Render Physics.Box2D Foreign; do : > "$extra/FS.GG.Game.$id.0.17.0.nupkg"; done
if output=$("$root/tests/release/test-game-0-17-release.sh" --packages "$extra" 2>&1); then
  echo "foreign package mutation unexpectedly passed" >&2; exit 1
fi
grep -q 'expected exactly the coherent 0.17.0 package set' <<<"$output"
echo "artifact witness: foreign package refused before consumer launch"

wrong="$scratch/wrong-package-version"
mkdir "$wrong"
for id in Core Harness Render; do : > "$wrong/FS.GG.Game.$id.0.17.0.nupkg"; done
: > "$wrong/FS.GG.Game.Physics.Box2D.0.17.1.nupkg"
if output=$("$root/tests/release/test-game-0-17-release.sh" --packages "$wrong" 2>&1); then
  echo "mismatched adapter version mutation unexpectedly passed" >&2; exit 1
fi
grep -q 'expected exactly the coherent 0.17.0 package set' <<<"$output"
echo "artifact witness: mismatched adapter version refused before consumer launch"

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
"$root/tests/release/test-game-0-14-release.sh" --root "$positive" --source-only
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
  if output=$("$root/tests/release/test-game-0-14-release.sh" --root "$fixture" --source-only 2>&1); then
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
sed -i 's:<Version>0.14.0</Version>:<Version>0.14.1</Version>:' "$version_fixture/Directory.Build.local.props"
if output=$("$root/tests/release/test-game-0-14-release.sh" --root "$version_fixture" --source-only 2>&1); then
  echo "release gate inversion unexpectedly passed" >&2
  exit 1
fi
grep -q "release scalar must be 0.14.0" <<<"$output" || {
  echo "release gate inversion failed for the wrong reason: $output" >&2
  exit 1
}
echo "version witness: 0.14.1 scalar rejected"

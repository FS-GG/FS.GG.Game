#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT

mkdir -p "$fixture/.github/workflows"
cp "$root/Directory.Build.local.props" "$fixture/Directory.Build.local.props"
cp "$root/.github/workflows/release.yml" "$fixture/.github/workflows/release.yml"

"$root/tests/release/test-game-0-14-release.sh" --root "$fixture" --source-only

sed -i 's:<Version>0.14.0</Version>:<Version>0.14.1</Version>:' "$fixture/Directory.Build.local.props"
if output=$("$root/tests/release/test-game-0-14-release.sh" --root "$fixture" --source-only 2>&1); then
  echo "release gate inversion unexpectedly passed" >&2
  exit 1
fi
grep -q "release scalar must be 0.14.0" <<<"$output" || {
  echo "release gate inversion failed for the wrong reason: $output" >&2
  exit 1
}
echo "release gate inversion rejected a 0.14.1 scalar as expected"

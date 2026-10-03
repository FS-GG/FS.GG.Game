#!/usr/bin/env bash
set -euo pipefail
export DOTNET_PROCESSOR_COUNT=1 MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
custody="$(realpath -m "${1:?usage: prepare.sh <empty-custody-directory>}")"
[[ -z "$(git -C "$repo" status --porcelain)" ]] || { echo "release custody requires a clean exact source checkout" >&2; exit 2; }
[[ ! -e "$custody" ]] || { echo "custody destination already exists: $custody" >&2; exit 2; }
mkdir -p "$custody"
chmod 700 "$custody"
version="$(sed -n 's:.*<WasmSharedVersion>\([^<]*\)</WasmSharedVersion>.*:\1:p' "$repo/eng/wasm-shared/version.props")"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "stable WASM version required" >&2; exit 2; }
[[ "$(cat "$repo/sdk/wasm/VERSION")" == "$version" ]]
"$repo/tests/release/wasm/test-release-wasm.sh" --source-only
[[ "$(dotnet --version)" == 10.0.401 ]]
[[ "$(node --version)" == v26.* ]]
[[ "$(rustc --version)" == 'rustc 1.90.0 (1159e78c4 2025-09-14)' ]]
[[ "$(cargo --version)" == 'cargo 1.90.0 (840b83a10 2025-07-30)' ]]
[[ "$(quint --version)" == 0.32.0 ]]
fable_version="$(dotnet tool run fable -- --version | sed -E $'s/\x1B\[[0-9;]*[mK]//g' | head -n 1)"
[[ "$fable_version" == 5.18.0 ]]
source="$(git -C "$repo" rev-parse HEAD)"
tree="$(git -C "$repo" rev-parse 'HEAD^{tree}')"
NUGET_PACKAGES="$(mktemp -d "${TMPDIR:-/tmp}/wasm-release-nuget.XXXXXX")"
export NUGET_PACKAGES
policy_work="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/wasm-worker-policy.XXXXXX")"
trap 'rm -rf "$NUGET_PACKAGES" "$policy_work"' EXIT
"$repo/scripts/wasm-release/build-worker-policy.sh" "$policy_work/policy"
dotnet pack "$repo/src/Wasm.Contracts/FS.GG.Wasm.Contracts.fsproj" -c Release -o "$custody" --nologo -p:RepositoryCommit="$source" -m:1 -nr:false -p:UseSharedCompilation=false
dotnet pack "$repo/src/Wasm.Browser/FS.GG.Wasm.Browser.fsproj" -c Release -o "$custody" --nologo -p:RepositoryCommit="$source" -p:WasmWorkerPolicyRoot="$policy_work/policy" -m:1 -nr:false -p:UseSharedCompilation=false
"$repo/sdk/wasm/build-source-archive.sh" "$custody"
python3 "$repo/scripts/wasm-release/compare-published-baseline.py" --custody "$custody" --output "$custody/api-baseline-comparison.json"
python3 "$repo/scripts/wasm-release/release_manifest.py" prepare \
  --custody "$custody" --version "$version" --source "$source" --tree "$tree" --api-comparison "$custody/api-baseline-comparison.json"
(cd "$custody" && sha256sum -- FS.GG.Wasm.*.nupkg fsgg-wasm-sdk-*.tar.gz release-manifest.json | sort -k2 > SHA256SUMS)
printf 'wasm-release-custody=%s version=%s source=%s artifacts=3\n' "$custody" "$version" "$source"

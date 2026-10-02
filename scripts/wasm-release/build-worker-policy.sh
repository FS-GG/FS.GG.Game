#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
output="$(realpath -m "${1:?usage: build-worker-policy.sh <private-output-root>}")"
case "$output/" in "$repo/"*) echo 'generated Worker policy requires a root outside source' >&2; exit 2 ;; esac
[[ ! -e "$output" ]] || { echo 'generated Worker policy root already exists' >&2; exit 2; }
dotnet tool run fable "$repo/src/Wasm.Browser/FS.GG.Wasm.Browser.fsproj" --outDir "$output" --noCache
[[ -f "$output/WorkerEntry.js" ]]
# The native module Worker imports this generated, source-authoritative policy.
node --check "$output/WorkerEntry.js"
python3 "$repo/tests/release/wasm/test-worker-policy-closure.py" --generated "$output"

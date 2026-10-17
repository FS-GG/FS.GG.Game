#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
output="$(realpath -m "${1:?usage: build-worker-policy.sh <private-output-root>}")"
case "$output/" in "$repo/"*) echo 'generated Worker policy requires a root outside source' >&2; exit 2 ;; esac
[[ ! -e "$output" ]] || { echo 'generated Worker policy root already exists' >&2; exit 2; }
node --test "$repo/tests/release/wasm/test-worker-transport.mjs"
dotnet tool run fable "$repo/src/Wasm.Browser/FS.GG.Wasm.Browser.fsproj" --outDir "$output" --noCache
[[ -f "$output/WorkerEntry.js" ]]
# The native module Worker imports this generated, source-authoritative policy.
node --check "$output/WorkerEntry.js"
python3 "$repo/tests/release/wasm/test-worker-policy-closure.py" --generated "$output"
# This build-only tool has exact dependency/integrity locks. No dependency
# install scripts run; its platform binary is authenticated by the lock and
# recorded in the generated build receipt, never shipped as a runtime tool.
tool_root="$(mktemp -d "${output}.build-tool.XXXXXX")"
trap 'rm -rf "$tool_root"' EXIT
# Dependency installation never dirties source or enters the raw policy glob.
for leaf in package.json package-lock.json build.mjs closed-module.mjs closed-module.test.mjs; do
  cp "$repo/scripts/wasm-release/worker-bundle/$leaf" "$tool_root/$leaf"
done
npm ci --prefix "$tool_root" --ignore-scripts --include=dev --include=optional --no-audit --no-fund
node --test "$tool_root/closed-module.test.mjs"
node "$tool_root/build.mjs" "$output" "$repo"
node --check "$output/module-worker.mjs"

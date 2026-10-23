#!/usr/bin/env bash
set -euo pipefail
root="${1:?usage: run.sh <fresh-consumer-public-root>}"
python3 -m http.server 41784 --bind 127.0.0.1 --directory "$root" >/tmp/fsgg-wasm-package-consumer-http.log 2>&1 &
server=$!
trap 'kill "$server" 2>/dev/null || true' EXIT
cd "$(dirname "${BASH_SOURCE[0]}")"
# Hosted-like CPU budget: the hosted runner class that refused a cold Firefox load
# has 4 vCPUs. A larger host pins the browser processes to 4 CPUs; a host with 4 or
# fewer runs unchanged. The cold-load report records the effective constraint.
cpus="$(nproc)"
if (( cpus > 4 )) && command -v taskset >/dev/null; then
  export WASM_BROWSER_CPU_LIMIT="taskset:0-3 of $cpus"
  taskset -c 0-3 npm test
else
  export WASM_BROWSER_CPU_LIMIT="host:$cpus"
  npm test
fi

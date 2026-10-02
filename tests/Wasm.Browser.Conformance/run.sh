#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
python3 -m http.server 41783 --bind 127.0.0.1 --directory "$root" >/tmp/fsgg-wasm-conformance-http.log 2>&1 &
server=$!
trap 'kill "$server" 2>/dev/null || true' EXIT
cd "$root"
npm test

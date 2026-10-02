#!/usr/bin/env bash
set -euo pipefail
root="${1:?usage: run.sh <fresh-consumer-public-root>}"
python3 -m http.server 41784 --bind 127.0.0.1 --directory "$root" >/tmp/fsgg-wasm-package-consumer-http.log 2>&1 &
server=$!
trap 'kill "$server" 2>/dev/null || true' EXIT
cd "$(dirname "${BASH_SOURCE[0]}")"
npm test

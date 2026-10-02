#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
runner_temp="$(realpath -e "${RUNNER_TEMP:?browser tool setup requires RUNNER_TEMP}")"
case "$runner_temp/" in
  "$repo/"*) echo "browser tool setup refuses RUNNER_TEMP inside source checkout" >&2; exit 2 ;;
esac
work="$(mktemp -d "$runner_temp/wasm-browser-tools.XXXXXX")"
trap 'rm -rf "$work"' EXIT
cp "$repo/tests/Wasm.PackageConsumer/browser/package.json" \
  "$repo/tests/Wasm.PackageConsumer/browser/package-lock.json" "$work/"
cd "$work"
npm ci
./node_modules/.bin/playwright install --with-deps chromium

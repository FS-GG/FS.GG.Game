#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compiled="$(realpath -e "${1:?usage: verify-wasm-supervisor-browser.sh <supervisor-gate-output> <new-private-public-root>}")"
public="$(realpath -m "${2:?requires fresh private public root}")"
case "$public/" in "$repo/"*) echo 'browser qualification requires private output outside source' >&2; exit 2;; esac
[[ ! -e "$public" ]] || { echo 'browser qualification output must be fresh' >&2; exit 2; }
[[ -f "$compiled/installed-fable/BrowserBridge.js" ]]
wasi="${WASM_WASI_SDK_ROOT:?requires pinned WASI SDK 34 root}"
[[ "$("$wasi/bin/clang" --version | head -n 1)" == 'clang version 23.1.0-wasi-sdk (https://github.com/llvm/llvm-project 895aa2c896ada719451be2e3673c83da8ddf1141)' ]]
custody="${WASM_CANDIDATE_CUSTODY:?requires exact packed candidate feed}"
version="${WASM_CANDIDATE_VERSION:?requires exact candidate version}"
mkdir -p "$public"
cp -a "$compiled/installed-fable" "$public/fable"
cp "$repo/tests/Wasm.Supervisor.Compatibility/browser/index.html" "$repo/tests/Wasm.Supervisor.Compatibility/browser/compatible-consumer.mjs" "$public/"
"$wasi/bin/clang" --target=wasm32 -std=c17 -Oz -nostdlib -mno-bulk-memory -mno-reference-types -mno-multivalue \
  "$repo/tests/Wasm.Supervisor.Compatibility/browser/limits-guest.c" \
  -Wl,--no-entry -Wl,--export-memory -Wl,--initial-memory=2097152 -Wl,--max-memory=8388608 \
  -Wl,--export=sc2c_abi_version -Wl,--export=sc2c_alloc -Wl,--export=sc2c_free \
  -Wl,--export=sc2c_initialize -Wl,--export=sc2c_process -Wl,--export=sc2c_shutdown -o "$public/limits-guest.wasm"
python3 - "$custody/FS.GG.Wasm.Browser.$version.nupkg" "$public" <<'PY'
import pathlib,sys,zipfile
root=pathlib.Path(sys.argv[2])
with zipfile.ZipFile(sys.argv[1]) as archive:
    for name in archive.namelist():
        if name.startswith('contentFiles/any/any/_content/'):
            target=root/name.removeprefix('contentFiles/any/any/')
            assert target.resolve().is_relative_to(root.resolve())
            target.parent.mkdir(parents=True,exist_ok=True)
            target.write_bytes(archive.read(name))
PY
python3 -m http.server 41785 --bind 127.0.0.1 --directory "$public" > "$public/http.log" 2>&1 &
server=$!
trap 'kill "$server" 2>/dev/null || true' EXIT
cd "$repo/tests/Wasm.Supervisor.Compatibility/browser"
npm ci --ignore-scripts
[[ "$(node -p "require('@playwright/test/package.json').version")" == 1.63.0 ]]
npx playwright test --config playwright.config.mjs

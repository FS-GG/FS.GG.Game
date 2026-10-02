#!/usr/bin/env bash
set -euo pipefail
archive="${1:?usage: verify-sdk.sh <source-archive> <wasi-sdk-root> <output-directory>}"
wasi="$(realpath -e "${2:?missing wasi-sdk root}")"
out="${3:?missing output directory}"
[[ "$(rustc --version)" == 'rustc 1.90.0 (1159e78c4 2025-09-14)' ]]
[[ "$(cargo --version)" == 'cargo 1.90.0 (840b83a10 2025-07-30)' ]]
[[ "$("$wasi/bin/clang" --version | head -n 1)" == 'clang version 23.1.0-wasi-sdk (https://github.com/llvm/llvm-project 895aa2c896ada719451be2e3673c83da8ddf1141)' ]]
work="$(mktemp -d "${TMPDIR:-/tmp}/fsgg-wasm-sdk-verify.XXXXXX")"
trap 'rm -rf "$work"' EXIT
python3 - "$archive" <<'PY'
import sys, tarfile
with tarfile.open(sys.argv[1], 'r:gz') as value:
    for item in value.getmembers():
        if item.issym() or item.islnk() or item.name.startswith('/') or '..' in item.name.split('/'):
            raise SystemExit(f"unsafe SDK archive member: {item.name}")
PY
tar -xzf "$archive" -C "$work"
root="$(find "$work" -mindepth 1 -maxdepth 1 -type d -name 'fsgg-wasm-sdk-*' -print -quit)"
[[ -n "$root" ]]
(cd "$root" && sha256sum --check --strict SHA256SUMS)
mkdir -p "$out"
rust_flags='-Copt-level=z -Cpanic=abort -Ctarget-feature=-bulk-memory,-multivalue,-reference-types,-relaxed-simd,-sign-ext,-simd128,-tail-call -Clink-arg=--no-entry -Clink-arg=--export-memory -Clink-arg=--initial-memory=2097152'
CARGO_TARGET_DIR="$work/target-bar" RUSTFLAGS="$rust_flags -Clink-arg=--max-memory=67108864" \
  cargo build --locked --release --target wasm32-unknown-unknown --manifest-path "$root/examples/wasm/rust/bar-guest/Cargo.toml"
CARGO_TARGET_DIR="$work/target-sc2" RUSTFLAGS="$rust_flags -Clink-arg=--max-memory=8388608 -Clink-arg=--table-base=0" \
  cargo build --locked --release --target wasm32-unknown-unknown --manifest-path "$root/examples/wasm/rust/sc2-guest/Cargo.toml"
cp "$work/target-bar/wasm32-unknown-unknown/release/fsgg_wasm_bar_example.wasm" "$out/rust-bar.wasm"
node "$root/sdk/wasm/tools/strip-unused-table.mjs" \
  "$work/target-sc2/wasm32-unknown-unknown/release/fsgg_wasm_sc2_example.wasm" "$out/rust-sc2.wasm"
compile_c() {
  local source=$1 output=$2 max=$3 prefix=$4
  "$wasi/bin/clang" --target=wasm32 -std=c17 -Oz -nostdlib \
    -I "$root/sdk/wasm" "$root/sdk/wasm/fsgg-wasm-guest.c" "$source" \
    -Wl,--no-entry -Wl,--export-memory -Wl,--initial-memory=2097152 -Wl,"--max-memory=$max" \
    -Wl,"--export=${prefix}_abi_version" -Wl,"--export=${prefix}_alloc" -Wl,"--export=${prefix}_free" \
    -Wl,"--export=${prefix}_initialize" -Wl,"--export=${prefix}_process" -Wl,"--export=${prefix}_shutdown" \
    -o "$output"
}
compile_c "$root/examples/wasm/c/bar_guest.c" "$out/c-bar.wasm" 67108864 barc
compile_c "$root/examples/wasm/c/sc2_guest.c" "$out/c-sc2.wasm" 8388608 sc2c
for variant in trap loop; do
  macro=FSGG_WASM_TRAP; [[ "$variant" == loop ]] && macro=FSGG_WASM_LOOP
  "$wasi/bin/clang" --target=wasm32 -std=c17 -Oz -nostdlib -D"$macro" \
    -I "$root/sdk/wasm" "$root/sdk/wasm/fsgg-wasm-guest.c" "$root/examples/wasm/c/adversarial_sc2_guest.c" \
    -Wl,--no-entry -Wl,--export-memory -Wl,--initial-memory=2097152 -Wl,--max-memory=8388608 \
    -Wl,--export=sc2c_abi_version -Wl,--export=sc2c_alloc -Wl,--export=sc2c_free \
    -Wl,--export=sc2c_initialize -Wl,--export=sc2c_process -Wl,--export=sc2c_shutdown \
    -o "$out/c-$variant.wasm"
done
sha256sum "$out"/*.wasm | sort -k2 > "$out/SHA256SUMS"
printf 'sdk-examples: rust=2 c=4 source-root=%s\n' "$root"

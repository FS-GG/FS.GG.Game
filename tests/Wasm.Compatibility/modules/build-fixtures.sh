#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
rust_version=$(rustc --version)
case "$rust_version" in
  "rustc 1.90.0 "*) ;;
  *) echo "expected rustc 1.90.0, found: $rust_version" >&2; exit 2 ;;
esac

# shellcheck disable=SC2054 # commas belong to rustc's one -Ctarget-feature argument
flags=(--edition=2021 --crate-type=cdylib --target=wasm32-unknown-unknown -Copt-level=z -Cpanic=abort -Ctarget-feature=-atomics,-bulk-memory,-exception-handling,-multivalue,-reference-types,-relaxed-simd,-sign-ext,-simd128,-tail-call -Clink-arg=--no-entry -Clink-arg=--export-memory -Clink-arg=--initial-memory=2097152)
rustc "${flags[@]}" -Clink-arg=--max-memory=67108864 "$root/bar-rust/lib.rs" -o "$root/bar-rust/bar-conformance.wasm"
rustc "${flags[@]}" -Clink-arg=--table-base=0 -Clink-arg=--max-memory=8388608 "$root/sc2-rust/lib.rs" -o "$root/sc2-rust/sc2-compiler-output.wasm"
node "$root/strip-unused-table.mjs" "$root/sc2-rust/sc2-compiler-output.wasm" "$root/sc2-rust/sc2-conformance.wasm"
for variant in loop_forever call_trap grow_memory malformed_descriptor free_trap; do
  rustc "${flags[@]}" --cfg "$variant" -Clink-arg=--max-memory=8388608 "$root/synthetic-hostile/lib.rs" -o "$root/synthetic-hostile/$variant.wasm"
done
chmod 0644 "$root/bar-rust/bar-conformance.wasm" "$root/sc2-rust/sc2-conformance.wasm" "$root/synthetic-hostile/"*.wasm

bar_source=$(sha256sum "$root/bar-rust/lib.rs" | cut -d' ' -f1)
bar_output=$(sha256sum "$root/bar-rust/bar-conformance.wasm" | cut -d' ' -f1)
sc2_source=$(sha256sum "$root/sc2-rust/lib.rs" | cut -d' ' -f1)
sc2_output=$(sha256sum "$root/sc2-rust/sc2-conformance.wasm" | cut -d' ' -f1)
builder=$(sha256sum "$root/build-fixtures.sh" | cut -d' ' -f1)
transformer=$(sha256sum "$root/strip-unused-table.mjs" | cut -d' ' -f1)
hostile_source=$(sha256sum "$root/synthetic-hostile/lib.rs" | cut -d' ' -f1)
rm "$root/sc2-rust/sc2-compiler-output.wasm"
sed \
  -e "s/@RUST_VERSION@/$rust_version/" \
  -e "s/@BAR_SOURCE@/$bar_source/" -e "s/@BAR_OUTPUT@/$bar_output/" \
  -e "s/@SC2_SOURCE@/$sc2_source/" -e "s/@SC2_OUTPUT@/$sc2_output/" \
  -e "s/@BUILDER@/$builder/" -e "s/@TRANSFORMER@/$transformer/" -e "s/@HOSTILE_SOURCE@/$hostile_source/" \
  -e "s/@LOOP_OUTPUT@/$(sha256sum "$root/synthetic-hostile/loop_forever.wasm" | cut -d' ' -f1)/" \
  -e "s/@TRAP_OUTPUT@/$(sha256sum "$root/synthetic-hostile/call_trap.wasm" | cut -d' ' -f1)/" \
  -e "s/@GROW_OUTPUT@/$(sha256sum "$root/synthetic-hostile/grow_memory.wasm" | cut -d' ' -f1)/" \
  -e "s/@MALFORMED_OUTPUT@/$(sha256sum "$root/synthetic-hostile/malformed_descriptor.wasm" | cut -d' ' -f1)/" \
  -e "s/@FREE_TRAP_OUTPUT@/$(sha256sum "$root/synthetic-hostile/free_trap.wasm" | cut -d' ' -f1)/" \
  "$root/provenance.template.json" > "$root/provenance.json"

# Installed consumer counterexample fixture

`bar-conformance.wasm` is the unchanged actual BAR Rust compatibility fixture from `tests/Wasm.Compatibility/modules/bar-rust/bar-conformance.wasm` at protected source `3062be7e37c97959b9d522492ea1505f89a4c74e`. SHA-256: `1557e76b7a97b8e301c31c1e082c9060d8b66258a23308dcf9d2d17dbfd9de7d`. It is a consumer input, independently copied alongside the published SDK Rust/C outputs. Byte mutations in `package-consumer.mjs` reproduce the admitted empty, unaligned and configured output-cap counterexamples.

`connected-controls.c` is a repository test guest, compiled with the acquired
WASI34 compiler in the consumer's private temporary root. Its normal exports
encode distinct lifecycle markers. Input modes and compile macros deliberately
loop or trap, return invalid/aliased allocation spans, grow memory, return an
aliased descriptor, or advertise an incorrect ABI version. The guest contains
no host policy. Invalid descriptor/aliased input variants loop in subsequent
allocation/free so a forbidden mechanical call changes a prompt refusal into
a watchdog timeout. No generated control binary is published in either package
or the SDK archive.

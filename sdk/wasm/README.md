# FS.GG shared WASM guest SDK source archive

Version `0.1.0-source.3` is the unpublished authoring source for WASM-SHARED-01.3. The archive contains a `no_std` Rust helper crate, a freestanding C header/source pair, BAR ABI 1 and SC2 ABI `0x00010000` examples, the MIT license, provenance, checksums and pinned build routes. It contains no product codec, role grant or native authority.

Builds require Rust/Cargo 1.90.0 with `wasm32-unknown-unknown`. C examples require official WASI SDK 34.0 x86-64 Linux archive SHA-256 `b761e3a0721dbae9c09a0059e5fdb2bf917d1b4a8a7b430fb3b5aafb0984b2c4`, whose Clang identifies as `23.1.0-wasi-sdk`. The acquisition script verifies these identities before extraction.

`build-source-archive.sh` creates normalized archive bytes. `verify-sdk.sh` extracts those bytes into a clean root and builds all Rust/C examples only from the extracted archive. The examples exercise the shared descriptor/allocation surface; product input/output formats remain with BAR and SC2.

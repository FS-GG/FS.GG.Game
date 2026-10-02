# Shared WebAssembly guest SDK source archive

WASM-SHARED-01.3 distributes guest authoring support as a normalized source
archive. Version `0.1.1` contains a `no_std` Rust crate, a freestanding C
header/source pair, BAR ABI 1 and SC2 ABI `0x00010000` examples, provenance, MIT
license and per-file checksums. It contains no BAR/SC2 wire codec, product role
grant or native effect authority.

The Rust route pins Rust and Cargo 1.90.0 with `wasm32-unknown-unknown`. The C
route pins official WASI SDK 34.0 for x86-64 Linux, archive SHA-256
`b761e3a0721dbae9c09a0059e5fdb2bf917d1b4a8a7b430fb3b5aafb0984b2c4`, Clang
`23.1.0-wasi-sdk` and LLVM revision
`895aa2c896ada719451be2e3673c83da8ddf1141`. Both routes emit freestanding core
WASM modules without WASI imports.

Build and verify a local archive with:

```console
sdk/wasm/build-source-archive.sh /tmp/fsgg-wasm-sdk
WASM_WASI_SDK_ROOT=/absolute/path/to/wasi-sdk-34.0-x86_64-linux \
  scripts/verify-wasm-package-consumer.sh
```

The verifier extracts the archive into an isolated root, validates its checksum
manifest, builds independent Rust and C examples for both profiles and exercises
their exported ABI through the installed browser package Worker. Trap and infinite
guest examples prove containment and deadline termination. The fresh Fable consumer
restores local nupkgs only for `FS.GG.Wasm.*`, compiles without sibling project
references and loads package-owned assets below `/sub/app`.

These source archives and nupkgs are unpublished candidates. Feed publication,
public readback, product migration and native acceptance remain later stages.
The stable release-set and exact-byte custody rules are documented in
[the publication contract](publication.md).

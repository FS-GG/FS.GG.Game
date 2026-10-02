# Source provenance

The shared allocation/descriptor helpers are bounded adaptations of these protected inputs:

- BAR: `FS-GG/FSBarV2` revision `9f0d8712e2c9a21cbf49f515a65b520516239b5a`, `sdk/barc/barc-guest-sdk/src/lib.rs`.
- SC2 Rust: `FS-GG/FS.GG.SC2.Client` revision `57eadf00bb9e776664e00fe200bb4f2952f72829`, `sdk/rust/sc2c-sdk/src/lib.rs`.
- SC2 C/toolchain: the same revision, `sdk/c/**` and its protected WASI SDK 34.0 author-toolchain contract.

The source archive keeps only ABI-neutral allocation, last-owned-span release and descriptor writing. BAR/SC2 wire codecs, native roles, capabilities and effect authority are excluded. The repository MIT license applies to this adaptation.

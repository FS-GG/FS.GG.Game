# Shared WebAssembly compatibility contract

This is the source contract for stage `.1` of
[WASM-SHARED-01](https://github.com/FS-GG/.github/blob/67a78de8d85cc360bac33d39a322dc91bcb37379/docs/roadmaps/2026-10-02-shared-wasm-foundation.md).
FS.GG.Game is the producer for the proposed `FS.GG.Wasm.Contracts` and
`FS.GG.Wasm.Browser` packages. This window contains the contracts package only.
Version `0.1.0` identifies the stable shared-WASM release candidate. It has not
yet been published, enrolled in the Game solution, or added to the existing
`0.16.0` coherent release set. See the [publication contract](publication.md).

The contract is intentionally independent of Game.Core, rendering, session
reducers, BAR and SC2 product types. Generic input and output are opaque bytes.
Roles describe capacity and never grant native command authority. A product
adapter must validate decoded identity, current grants and effect eligibility
before any result can affect native state.

## Frozen ABI shapes

Both ABIs export `memory` and six core `i32` functions. The version function is
`() -> i32`, allocation is `(i32) -> i32`, free is `(i32, i32) -> ()`,
initialize and process are `(i32, i32, i32) -> i32`, and shutdown is
`() -> i32`. The descriptor is two little-endian u32 values: output pointer,
then output length.

| Path | ABI | Admission | Memory/table | Invocation | Scheduling and replacement |
|---|---|---|---|---|---|
| BAR protected | `barc_*`, version `1` | Parse before compilation; no imports or start; complete signatures; only `__data_end` and `__heap_base` extra globals | One finite nonshared memory, at most 1024 pages; at most one finite table, at most 4096 elements | Module 8 MiB; input/output 64 KiB; nonempty spans aligned to four bytes; successful output nonempty | 250 ms phase watchdog, immediate busy refusal, destructive load |
| SC2 imported strict | `sc2c_*`, version `0x00010000` | Restricted parser before Worker; no imports/start; complete signatures including free; additional global exports currently allowed | One bounded nonshared wasm32 memory, at most 128 pages; no tables | Module 8 MiB; input 256 KiB; output 64 KiB or lower configured limit; no extra alignment rule; empty worker output allowed | At most 250 ms end-to-end from enqueue, bounded FIFO of four, transactional candidate and unresolved-receipt freeze |
| SC2 legacy direct URL | Same SC2 ABI identity | Compile and instantiate precede import/export checks; start may already run; required-presence check omits free | Only current memory up to 8 MiB is observed | Existing worker span rules | Inventory only; a shared runtime cannot select this path |

The legacy path records what exists so migration does not erase evidence. It is
not an unsafe admission switch. Each legacy module needs an inventory and an
explicit migration result before it can enter the strict SC2 path.

## Shared safety corrections

New execution must zero a fresh descriptor before initialize/process and must
refresh memory views after every guest call that can grow memory. Descriptor,
input and output ranges are checked in wasm32 arithmetic, against the current
memory, and for overlap before bytes are used. Cleanup receives only spans with
validated, non-aliased ownership. A rejected, aliased or unknown allocator span
is not passed back to untrusted `free`; the Worker is terminated instead.

A cleanup fault retains the primary and cleanup diagnostics, settles the
request once and disposes the Worker. Copied output is not successful until
cleanup succeeds. These rules deliberately repair gaps in both existing
workers; they do not weaken a profile in order to reproduce unsafe cleanup.
The executable reducer, effects and browser containment belong to stage `.2`.

Memory page limits cover WebAssembly linear memory. Browser timers terminate
when the supervisor observes a deadline. Neither supplies deterministic
instruction fuel, a complete process/compile/JavaScript heap quota, nor replay
determinism. FourD remains an optional nonauthoritative example until a separate
deterministic execution contract is qualified.

## Pinned source corpus

The checked-in
[`baselines.v1.json`](../../tests/Wasm.Compatibility/fixtures/baselines.v1.json)
attributes the inspected source and hashes to BAR protected revision
`9f0d8712e2c9a21cbf49f515a65b520516239b5a` (tree
`644e7bbc7d4e9906934d89453970ae6634ef895b`) and SC2 protected revision
`57eadf00bb9e776664e00fe200bb4f2952f72829` (tree
`991ff12af5485203d94749150b29d3e547cdf1cc`). It points to BAR's independent
Rust guests and SC2's Rust/C authoring sources and strict binary inspector.

No built `.wasm` file was present in either supplied protected checkout, so the
corpus records source hashes and does not invent artifact or toolchain digests.
The BAR browser demonstration and SC2 bounded demonstrations were source
inspected, not rerun in this window. SC2 tip
`4c6080ca0198884559bbe8a2aaa037b3e1d4c1e2` remains an unmerged canonical-live-
profile repair and is not copied or accepted here.

[`expected-decisions.v1.json`](../../tests/Wasm.Compatibility/fixtures/expected-decisions.v1.json)
keeps positive and adversarial decisions for ABI signatures, admission,
alignment, empty output, scheduling, replacement, cleanup and role authority.
The F# tests independently assert the same guards rather than treating the JSON
as executable authority.

## Packaging and qualification

The guest SDK distribution selected for stage `.3` is a versioned source archive
containing the Rust crate and C header/source, examples, license/provenance,
pinned toolchains and checksums. No crates.io or npm publication is assumed.
The implementation is documented in [the SDK guide](sdk.md).

Run:

```console
./scripts/verify-wasm-contracts.sh
```

Set `WASM_CONTRACTS_FEED` to retain the exact locally tested nupkg in a chosen
directory; otherwise the gate uses and removes a temporary feed.

The gate builds the F# contract and negative controls, packs only
`FS.GG.Wasm.Contracts`, checks its public `.fsi`, curated Fable source view and
compatibility metadata, then restores a tiny .NET and Fable consumer from the
local nupkg with no sibling project reference. It prints the exact local package
SHA-256 and matching consumer-output digest. This proves candidate contract
packaging and Fable compilation. It does not prove a browser host, published
clean import, native acceptance, product adoption, or package installation from
either release feed.

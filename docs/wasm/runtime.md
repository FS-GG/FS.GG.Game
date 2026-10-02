# Shared WebAssembly browser runtime

This is the source candidate for stage `.2` of
[WASM-SHARED-01](https://github.com/FS-GG/.github/blob/67a78de8d85cc360bac33d39a322dc91bcb37379/docs/roadmaps/2026-10-02-shared-wasm-foundation.md).
It builds on the [compatibility contract](compatibility.md) and keeps BAR and
SC2 behavior distinct. Version `0.1.0-source.2` identifies local candidate
bytes. The package is unpublished and is outside the existing `0.16.0`
coherent release set.

`Lifecycle.update` is the production F# authority for one browser supervisor.
It owns active, candidate and retiring generations; ordinary, ordered and
replaceable snapshot queues; deadlines; freeze state; terminal delivery; and
disposal. The reducer emits data-only effects. `Host` stores reducer state and
passes those effects to an injected mechanical interpreter.

BAR keeps immediate busy refusal and destructive replacement. SC2 keeps its
four-item ordinary FIFO, bounded ordered lane, latest-only pending snapshot and
transactional candidate. A candidate needs successful initialization plus an
exact transaction and generation validation before commit. Retiring workers
continue to consume capacity until their termination is observed. Completions
from a frozen or retired generation are historical and cannot become current.
Every event checks host-observed monotonic time; a request may complete at its
deadline and expires after it.

The canonical Quint model is
[`eng/wasm-shared/lifecycle.qnt`](../../eng/wasm-shared/lifecycle.qnt). Pinned
model-generated ITF traces replay the complete projected state and ordered
effects through the production reducer under both .NET and Fable/Node. The
negative control mutates historical settlement into current settlement and
must fail the same correspondence comparison. The bounded simulation uses seed
`20261002`, 2,000 samples and 40 steps. It supplies sampled evidence, not an
exhaustive proof.

Run:

```console
./scripts/verify-wasm-lifecycle.sh
```

The verifier requires Quint `0.32.0`. It typechecks and tests the model, checks
the bounded simulation and deterministic trace bytes, runs lifecycle and
correspondence tests, executes the correspondence suite from Fable output, and
packs the local browser candidate to check its curated source and JavaScript
asset closure. It then restores a fresh Fable consumer from the local contracts
and browser packages and executes the generated JavaScript. This source gate
does not establish hosted package publication, product adoption, native
authority, or a qualified browser host.

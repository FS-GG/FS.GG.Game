# Shared WebAssembly browser runtime

This is the closed runtime source and stage `.3` package candidate for
[WASM-SHARED-01](https://github.com/FS-GG/.github/blob/67a78de8d85cc360bac33d39a322dc91bcb37379/docs/roadmaps/2026-10-02-shared-wasm-foundation.md).
It builds on the [compatibility contract](compatibility.md) and keeps BAR and
SC2 behavior distinct. Version `0.1.1` identifies the stable shared-WASM release
candidate. The package is unpublished and is outside the existing `0.16.0`
coherent release set. Its separate release contract is [documented here](publication.md).

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

Pull requests that change this source closure also run the independent
`wasm-shared` hosted job. It uses exact .NET, Node, Fable, Quint, Rust and
Playwright versions, rebuilds the checked-in WebAssembly fixtures, runs the
contract and lifecycle gates from cold package state, restores fresh package
consumers, and executes the six Chromium module Worker cases. The existing Game
gate and release workflows remain separate. Because this is one finite linear
job with no fan-out, artifact reuse, publication or retry protocol, its
preflight is a static check of required pins, inputs and step order; the runtime
Quint model remains the behavioral authority.

The verifier requires Quint `0.32.0`. It typechecks and tests the model, checks
the bounded simulation and deterministic trace bytes, runs lifecycle and
correspondence tests, executes the correspondence suite from Fable output, and
packs the local browser candidate to check its curated source and JavaScript
asset closure. It then restores a fresh Fable consumer from the local contracts
and browser packages and executes the generated JavaScript. This source gate
does not establish hosted package publication, product adoption, native
authority, or a qualified browser host.

## Package-owned Worker assets

The browser nupkg carries `worker-client.mjs` and `module-worker.mjs` as
build-transitive content under `_content/FS.GG.Wasm.Browser/`. A consuming build
materializes both files together. The client resolves the Worker relative to its
installed asset base, so a deployment below `/sub/app` does not assume a root URL.
The assets are mechanical: F# remains responsible for validated configuration,
admission, lifecycle effects, deadlines and settlement decisions.

The stage `.3` verifier restores the local contracts/browser packages in a fresh
temporary root with no project references, compiles all 15 Fable sources and loads
the installed asset closure at a non-root URL. Actual Workers call independently
built Rust and C BAR/SC2 guests; adversarial C guests cover trap, deadline
termination and repeated disposal. This source verifier does not publish either
nupkg or qualify an installed feed.

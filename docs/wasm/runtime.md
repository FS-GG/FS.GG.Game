# Shared WebAssembly browser runtime

The shared runtime preserves BAR and SC2 compatibility profiles and their ABI
exports. Published version `0.1.1` remains the accepted publication and installed
feed baseline. Version `0.2.0` is the connected runtime correction prepared for
stage `.5-P`; publication and product adoption remain separate outcomes. The
Game `0.16.0` and Skills `0.9.0` version axes are independent.

`Lifecycle.update` is the production F# authority for one browser supervisor.
It owns active, candidate and retiring generations; ordinary, ordered and
replaceable snapshot queues; deadlines; freeze state; terminal delivery; and
disposal. The reducer emits data-only effects. `Host` stores reducer state and
passes those effects to an injected mechanical interpreter. `Host.CreateConnected`
provides the package-owned interpreter using the primitive `HostTransport` seam.
The JavaScript `createHostTransport` owns Worker and timer handles only.

A load carries the exact artifact and its `ValidatedConfiguration` to the real
Worker. The Worker runs generated F# `WorkerEntry` admission and digest checks
before instantiation. Its initialize/process commands run the authoritative
`Invocation` allocation, span, output-limit and cleanup policy. The JavaScript
Worker only calls WebAssembly exports and accesses fresh memory views. The Fable
policy and its imports are generated in an isolated root and packaged beneath
`_content/FS.GG.Wasm.Browser/policy/`; packing without that closure fails.

Call `LoadConfigured`, wait for its compile settlement, call `Initialize`, then
submit `Invoke` calls after initialization succeeds. `Shutdown` calls the actual
configured shutdown export. Each command and observation binds Worker instance,
request, generation and operation correlation. A refused or failed load never
grants initialized authority. Candidate loading uses
`PrepareConfiguredCandidate`, followed by explicit initialization, validation and
commit with the exact transaction and generations. The result callback delivers
`BrowserResult` metadata; product effect eligibility remains the adapter's decision.

BAR keeps immediate busy refusal and destructive replacement. SC2 keeps its
four-item ordinary FIFO, bounded ordered lane, latest-only pending snapshot and
transactional candidate. A candidate needs successful initialization plus an
exact transaction and generation validation before commit. Retiring workers
continue to consume capacity until their termination is observed. Completions
from a frozen or retired generation are historical and cannot become current.
Every event checks host-observed monotonic time; a request may complete at its
deadline and expires after it. The event boundary first clamps the original
observation time, expires elapsed work, then handles the same original input.
Expiry effects precede input effects. This also applies to dispose, completion,
phase, freeze and resume inputs. A queued request invalidated with its Worker is
distinct from a queued request whose own deadline elapsed; raw outcome presence,
dispatch and terminal reason remain observable.

The successor canonical qualification retains each original typed input
independently of `LastAction`. It corrects the historical model omission without
changing the published 0.2.0 runtime, package or SDK bytes. Before/at/after
deterministic boundary traces and raw terminal comparisons supplement full state
and ordered effect comparison; source acceptance remains pending native replay.

The canonical Quint model is
[`eng/wasm-shared/lifecycle.qnt`](../../eng/wasm-shared/lifecycle.qnt). Pinned
model-generated ITF traces replay the complete projected state and ordered
effects through the production reducer under both .NET and Fable/Node. The
negative control mutates historical settlement into current settlement and
must fail the same correspondence comparison. Additional causal mutations drop
posted artifact bytes, replace shutdown with processing, or alter a phase timer
deadline. Command operation, byte count and correlation, plus timer phase and due
time, are included in the ordered effect comparison. The bounded simulation uses seed
`20261002`, 1,000 cold samples plus 1,000 reachable initialized samples, and 40
steps per sample. The initialized entry executes the same four explicit readiness
actions before those steps. It supplies sampled evidence, not an
exhaustive proof.

Run:

```console
./scripts/verify-wasm-lifecycle.sh
```

Pull requests that change this source closure also run the independent
`wasm-shared` hosted job. It uses exact .NET, Node, Fable, Quint, Rust and
Playwright versions, rebuilds the checked-in WebAssembly fixtures, runs the
contract and lifecycle gates from cold package state, restores fresh package
consumers, and executes the independent Chromium module Worker cases and the connected installed package controls. The existing Game
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

The package consumer restores contracts/browser packages in two fresh temporary
roots without project references, locks the second restore, compiles the installed
Fable host and loads the full installed asset closure below `/sub/app`. Actual
Workers call independently built SDK Rust and C BAR/SC2 guests. WASI34 C builds
explicitly disable bulk-memory, reference-types and multivalue to satisfy the
unchanged strict admission profile.

Consumer guest controls distinguish initialize/process/shutdown exports; reject
unsafe descriptor and aliased input allocations before extra allocation or free;
refuse admitted empty, unaligned and over-cap BAR outputs; refresh grown memory;
retain primary and cleanup traps; and terminate infinite compile, initialize,
process, free and shutdown phases. Active expiry terminates its Worker before
queued work can dispatch. Wrong correlation, Worker and generation observations
cannot change authority. Transaction, freeze and retiring-worker controls use
the same installed F# host.

Local custody qualification establishes the candidate bytes only. The manual
read-only installed workflow binds an immutable qualification revision to a
published tag, source/tree, native release asset digests and downloaded manifest/
SDK bytes, then runs serial org/public feed-only consumer jobs. Its required
receipts are supplied after root verifies publication; it never publishes,
repacks or changes a tag.


## Migration from published 0.1.1

Version `0.2.0` intentionally changes four Browser record constructors:
`WorkerCommand` now carries validated configuration; `RequestProjection` adds
Worker, operation, phase and correlation; `EffectProjection` adds actual command
and timer metadata; and `HostProjection` adds compiled/initialized/control state.
The native published-DLL comparison reports these four `CP0002` changes; the
Contracts package comparison passes. There is no binary compatibility claim for
the Browser transition.

Recompile consumers against the coherent `0.2.0` pair and materialize the complete
installed Worker asset tree, including generated `policy/` dependencies. Use
`Host.CreateConnected`, load the configured artifact, wait for compilation, and
explicitly initialize before processing. Shutdown is a distinct guest export.
Candidate initialization cannot grant current authority before exact validation
and commit. Products must validate result identity and native effect eligibility
in their own adapters; this producer correction does not establish BAR/SC2
product adoption. Published `0.1.1` bytes, tags and installed receipts remain
unchanged.

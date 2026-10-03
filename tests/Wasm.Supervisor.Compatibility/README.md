# Selected supervisor qualification

This fixture exercises the additive producer request limits and immutable
`Sc2SupervisorV1` policy. `Baseline/` reproduces mixed dispatch priority and
queued dispatch during the former Resume/Commit/Freeze workaround against
published 0.2.0. It is gap evidence, not successor acceptance.

`Compatibility.fsproj` uses production source. `Installed/InstalledCompatibility.fsproj`
uses an exact package selected by `WasmCandidateVersion`; restore it into a
fresh package cache with explicit candidate source mapping, then rebuild in
locked mode. Both replay the original 47 default canonical traces and all
selected traces through `Host.Create` and `Host.CreateConnected`, compare full
states and ordered effects, and check connected callbacks and raw terminals.
The additional wire fixture exercises the actual F# receiver with controlled
guest mechanics; it makes no claim of native browser execution.

`generate-compatible-traces.py` uses Quint 0.32.0, seed 20261003, and the
canonical `compatibleLifecycle` module. Its deterministic gzip files retain
complete ITF states. `--check` regenerates and byte-compares every fixture
and the generated F# projections. Capacity and byte equality fixtures retain
all admitted requests. A held snapshot's FIFO position begins on admission,
not when its earlier identity was created. Negative guards and causal
mutations cover limit capture, mixed capacity, snapshot promotion, inclusive
head expiry, frozen queue invalidation and token-bound commit.

`native-api-compat.py` runs SDK 10.0.401's native package validation against
the hash-pinned immutable 0.2.0 Browser and Contracts archives. It accepts
explicit custody, candidate version, baseline feed, official package-cache
and SDK paths. No suppression file is generated. An ephemeral package must
retain at least assembly version 0.2.0.0 for this API comparison; that does
not reserve a release version.

`browser/` calls the installed producer through its real module Worker assets
in Chromium. Its guest is independently compiled from `limits-guest.c` with
pinned WASI SDK 34. JavaScript owns fetch, Worker handles and Promises; shared
F# owns admission, queue order, deadline/output checks, freeze and promotion.
The acceptance covers a 44-byte initialized output followed by a 60-byte
process output from the same loaded guest, selected output rejection, actual
mixed FIFO posts, frozen promotion without old queued dispatch, historical
completion and expired enclosing admission.

These checks establish bounded producer and package behavior. Publication,
fresh qualification from both native feeds, and SC2's complete product API
acceptance remain separate joins controlled by the programme owner.

The executable gates are `scripts/verify-wasm-supervisor.sh` and
`scripts/verify-wasm-supervisor-browser.sh`. The first requires an explicit
baseline qualification root, coherent candidate custody and candidate version,
and a fresh output outside source. It limits local .NET work to one process
with node reuse and shared compilation disabled. The second consumes the
first gate's installed Fable output, requires pinned WASI SDK 34, independently
builds its guest, extracts the exact candidate Worker assets and runs all eight
pinned Playwright controls from a fresh external public directory. Its looping
initialization/process and enclosing-budget cases assert containment, without
claiming exact wall-clock scheduling latency.

The unpublished pure admission and frozen-commit functions return `HostDecision`.
Its state and effects must be applied independently of its result. A valid
request can advance the host clock, retire existing expired work and then
return a refusal; the refused action cannot erase those expiry effects.
`Host` applies this decision before returning its existing public result.
Malformed limits, exhausted enclosing budgets and nonmatching frozen receipts
remain pre-admission refusals with unchanged state and no effects. The retained
owner-expiry/refusal and frozen-commit/expiry traces verify this distinction
through both facades, including synchronous termination callbacks and raw
settlement order. Dropping those expiry effects is a rejected causal mutation.

Selected sampling now includes valid phase progression, enclosing budgets and
clock-advanced admissions/commits. Twenty-four state/effect witnesses record
actual reached counts. The gate requires each witness to occur in at least one
of the cold or initialized 1,000-trace samples; it does not turn a zero count
in one entry into a claim of coverage there.

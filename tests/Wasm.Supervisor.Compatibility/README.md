# Selected supervisor qualification

This fixture exercises the additive producer request limits and immutable
`Sc2SupervisorV1` policy. `Baseline/` reproduces mixed dispatch priority and
queued dispatch during the former Resume/Commit/Freeze workaround against
published 0.2.0. It is gap evidence, not successor acceptance.

`Compatibility.fsproj` uses production source. `Installed/InstalledCompatibility.fsproj`
uses an exact package selected by `WasmCandidateVersion`; restore it into a
fresh package cache with explicit candidate source mapping, then rebuild in
locked mode. Both replay the 53 default canonical traces and all 16
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

`Baseline/` and `native-api-compat.py` retain immutable historical 0.2.0 gap
and API evidence. They are not current acceptance gates. Current qualification
classifies the coherent candidate against the actual published 0.3.0 baseline
once in `prepare.sh`; it imposes no old-version compatibility requirement.

Current source and exact-package projects exercise present lifecycle contracts,
including both default BAR/SC2 settings and the selected supervisor policy. The
shared controls annotate host identities, outcomes and settings explicitly so
source includes and installed public namespaces resolve the same types. The
singleton effect interpreter's command capture accumulates each callback; its
ordinary Post/Arm mutation control refuses a missing command.

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

In hosted qualification, official tool bootstrap precedes the existing source
restore/build and CLR/Fable in-memory controls. After one coherent pack, the
installed lock-generation restore and fresh-cache locked build run with their
existing CLR/Fable and lock-refusal controls. Model sampling and real browser
work follow these phases. The final custody stage checks the same-job source,
tree, version, assembly hashes and successful phase logs before reusing their
outputs; it does not repeat the builds or in-memory suites. All stages share
the unchanged 35-minute job limit. This ordering moves diagnosis earlier;
measured time or cost savings are not established.

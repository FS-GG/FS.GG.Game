# WASM-SHARED-01 — producer qualification successor

## Coherent 0.4 candidate and current published-baseline classification

The unpublished terminal API selects stable coherent 0.4.0 before final source CI.
Both .NET packages, Contracts source-profile metadata, SDK version, Rust helper and
both Rust examples move together. Browser's exact same-version Contracts dependency
continues to derive from `WasmSharedVersion`; no new package identity is introduced.
This is source preparation, not a published version or a product consumer pin.

Current release classification compares actual 0.3.0 from producer
`16a401692f4c0dee7f6da1a86d0cd49e6ea3ec2c`, tree
`f06c9fef54b90d9dcd190b5bc1484f2e2e72f67a`, with manifest SHA256
`a10046daf5417304732605745608b084f14481212931cb69939d51f90233ed07`.
The selected policy binds the actual public served archives: Browser SHA256
`74a11425bfb64c152ffbeab5be2753434e08f55f806e85113cdc25cacbc86fa5`
and Contracts SHA256
`90839f0bef2688447a55568dcb370941efec6f4e043742b9b4b8969c247acfa1`.
Read-only archive/nuspec inspection established these inputs; native API comparison
of the new DLLs remains unexecuted. Actual classification is reported by the native
comparison, with unavailable or unexpected results refused. Historical 0.2 and 0.3
policy bytes remain explicit read-only evidence mappings, without granting current
publisher authority or promising compatibility for old callers.

The first PR713 required lock-range check at source `05f33ef` found one stale
Lifecycle.Tests Browser-to-Contracts Project dependency range, still recording
0.3. The correction changes only that Project range to 0.4 and adds its actual
version join and stale-range refusal to the cheap candidate preflight. No package
`contentHash` was edited and no local restore was run. The maintained shared static
lock-range guard checks all 16 project-reference ranges across 24 locks before the
replacement push; full exact-head CI remains required. The original failed job
`117318369385` in run `39005954355` retains its failure evidence.

`prepare.sh` already seals the current published-baseline comparison against the same
candidate archives in the custody manifest. Current supervisor acceptance therefore
removes its duplicate 0.2 DLL comparison and unused 0.2 bootstrap feed acquisition.
Official Core/compiler/Fable/WASI preparation, canonical model and correspondence,
BAR/SC2 ABI controls, both Host facades, locked installed consumers, numeric/content
refusals and installed Worker/browser coverage remain required. PackageConsumer now
requires an explicit exact selected version and refuses absence, matching the maintained
custody and feed-only callers; its old 0.2 fallback is removed.

The existing 0.3 publication and installed evidence retain their original identities.
The separately owned current publisher/installed correction must join this metadata
and independently reviewed genuine generated files before final coherent source CI.
Publication, fresh both-feed installed acceptance, registry inventory and consumer
adoption remain distinct later boundaries. Neither public 0.4 absence observed during
planning nor source preparation establishes organization occupancy or authorization.

## Current source and installed compile boundary

PR713 full qualification [39006292599](https://github.com/FS-GG/FS.GG.Game/actions/runs/39006292599),
attempt 1, job `117319354159`, failed at source `5e1c48b` in the supervisor
source build. Earlier contract, lifecycle, browser Worker, coherent package/current
0.3 classification and fresh package-consumer steps passed. Selected model controls,
sampling and both trace checks also passed before the build failed. The compiler
reported HostIdentity/WireHostIdentity inference errors in shared controls. Only the
last 60 source-build log lines survived the original failure trap; the complete raw
source-build log is unavailable. Installed supervisor suites and eligible custody
freeze/upload were unreached. This run remains failed.

The next full qualifier [39008574356](https://github.com/FS-GG/FS.GG.Game/actions/runs/39008574356),
attempt 1 at `efe2840`, failed in the early source phase before Quint, packing,
model or browser work. Its bounded first-error diagnostics exposed an unparenthesized
conditional record field followed by another field, plus EffectProjection/Mechanical
inference in the new omission control. The follow-up parenthesizes the field expression
and explicitly types both effect-search lambdas. The same head also failed required
shell lint (`39008574510`, job `117326008491`) on SC2174: combining mkdir mode
with parent creation. The correction creates parents separately, then creates the
fresh private evidence directory with its explicit mode. This is actual early failure detection,
not successful source compilation or full qualification; both failed operations remain.

The connected correction annotates shared identity, outcome, settings and boundary
records across all source/installed include contexts. It also accumulates command
capture across singleton effect callbacks and adds an ordinary Post/Arm omission
control. `Compatibility.fsproj` checks current producer lifecycle behavior, shared
BAR/SC2 and selected controls, generated correspondence and both facades; its name
does not impose old-version compatibility. The separate installed host fixture now
requires an explicit exact current version instead of an obsolete 0.2 fallback.
Historical baseline source and audit evidence retain their original identities.

Pipeline preflight selects a linear dependency/order correction with inert controls,
without a new pipeline model. Official bootstrap enables the existing source build
and CLR/Fable in-memory controls before Quint/Rust/browser setup. One coherent pack
then enables installed lock-generation restore, one fresh-cache locked build and
existing installed CLR/Fable/raw-numeric/NU1403 controls before model/browser work.
The final supervisor stage validates same-job source/tree/version/status and actual
assembly hashes before reusing these outputs. Both final model/browser suites remain
required; builds and in-memory suites are not duplicated. Known-stale phase evidence
is refused by the actual helper in inert preflight controls. Failure reporting now
retains bounded first compiler errors plus the existing tail, preserving first status.
The 35-minute job limit is unchanged. Earlier detection is the intended benefit;
actual cold fit, runtime success and cost savings await the next hosted qualification.

## Third hosted attempt: complete generated trace preparation

The separately selected [39004844417](https://github.com/FS-GG/FS.GG.Game/actions/runs/39004844417),
attempt 1 at `bfdf70d66d0b64959b755dc23a920cc3848e29c7`, completed successfully.
Both early model typechecks, all 53 default schedules and all 16 selected schedules
passed within the original generation window (400.58 seconds). The original
40,381-byte generation log has no omitted bytes. Node 26.12.0 and Quint 0.32.0
were observed on Ubuntu 24 image `20261012.334.1`.

Artifact `12090851663`, outer SHA256
`63a4b8b1db41653930038ef35c7c16542e3d779995a8dcc46b1b87d63f08ae09`,
contains the exact 71 generated files (53 JSON traces, 16 compressed selected traces
and two F# projections), plus the manifest and generation log. Owner data-only
checks matched every output size/hash and all seven exact source-input hashes.
Independent data-only review accepted the exact 71 files before their source commit,
including nine new terminal traces and equivalence of 60 historical traces. All seven
model/generator input hashes remain identical to the dispatched source. No downloaded
source was executed during review. The full source/model/package/browser qualification job
was intentionally skipped in generation mode; preparation success does not qualify
the joined 0.4 source or authorize publication.

Collection reported no errors, direct generator-process return and owned scratch
removal. Full descendant settlement remains unproven. Both prior failed operations
retain their original outcomes; this independently selected job used a fresh hosted
VM and does not establish teardown of either earlier job.

## Second hosted attempt: selected module scope and early model checks

The new-source preparation [39003742514](https://github.com/FS-GG/FS.GG.Game/actions/runs/39003742514),
attempt 1 at `75854a55d7a8ee42d52f8b0cd4f664286154506f`, failed separately from
the original attempt. All 53 default schedules and their F# projection completed
successfully. The first selected schedule, `terminal-held`, stopped during parsing:
`QNT404` could not resolve `currentResultsHaveAuthority` in the selected test module.
No selected simulation ran. Wildcard import of `compatibleLifecycle` does not
re-export a helper that module imports from `lifecycle`.

Artifact `12090088469`, outer SHA256
`7f6d080abcc9ec8991c30d00cfd2187a336e452f984951e8242543b6db4dca41`,
retains exact failed-schedule metadata and the complete 6,730,681-byte generation
log with zero omitted bytes. No fresh ITF exists for this parse failure; the gap is
explicit. Collection reported no errors, direct process return and owned scratch
removal, with full descendant settlement unproven. No partial fixtures or coherent
qualification are accepted, and neither failed operation grants another dispatch.

The correction exports `selectedCurrentResultsHaveAuthority` as a module-owned
observation and uses it in selected tests and `compatibilitySafe`. Its predicate,
production runtime, seeds, sample counts, step bounds and ITF semantics are unchanged.
Before any schedule simulation, the existing bounded preparation shell typechecks
the default event-boundary entry (including the canonical lifecycle model) and the
selected compatibility entry. Both commands share the original 20-minute window;
ordered stage/status records identify a typecheck refusal before either generator.
These new commands and the corrected selected module remain unexecuted.

Both generators add `--verbosity=1`, recorded in the actual command log and manifest.
Quint's pinned [verbosity definitions](https://github.com/informalsystems/quint/blob/v0.32.0/quint/src/verbosity.ts)
and [simulator path](https://github.com/informalsystems/quint/blob/v0.32.0/quint/src/cliCommands.ts)
retain result and violated-invariant reporting while suppressing full state dumps;
the [ITF hook](https://github.com/informalsystems/quint/blob/v0.32.0/quint/src/cliReporting.ts)
remains independent. The observed default success log was already 6.7 MiB; overflow
from later selected state dumps is a prospective risk, not an observed failure.
Fresh failed ITF remains bounded diagnostic evidence, with explicit missing/partial
gaps and the existing shared diagnostic budget. No new streaming runner or deadline
is introduced. Static/inert checks and independent source review precede any further
separately admitted generation or final coherent CI.

## First hosted terminal trace attempt and model authority correction

The separately admitted trace preparation [39002952646](https://github.com/FS-GG/FS.GG.Game/actions/runs/39002952646),
attempt 1 at `f57a83f5dad5a733b5acef4ed6f41628adfc9da2`, failed. Exact checkout,
dispatch guards, cold static preflight and Node/Quint setup passed. The default
generator passed its preceding five schedules, then Quint reported `Invariant
violated` for `terminalBefore` (`lifecycleSafe`, seed 20261002, ten steps).
The remaining default schedules and all sixteen selected schedules were unreached.
Artifact `12089723109`, outer SHA256
`31e2743fdbaef0decb4a7db9bea33c50dfa0ee0546082110bda2fc0fcc7f476a`,
retains the manifest and complete 1178-byte failure log. It accepts no fixture
outputs or qualification. The original process returned; collection reported no
errors and removed owned scratch. Full descendant settlement is not established.
No counterexample ITF or failing conjunct was retained, so the exact invariant
conjunct diagnosis is a source-supported inference rather than executed evidence.

The old current-result invariant compared a terminal result's preserved generation
with final `activeGeneration`, which retirement intentionally clears to zero.
The model-only correction captures active worker/generation after expiry and before
the actual event handler in observational input metadata. Current results must match
that authority, the handled request and a valid result event. This preserves stale
worker/generation/request/event rejection without exempting disposed states. Both
default and selected event boundaries capture the same provenance; production
reducer, Host, result classification and Worker ABI are unchanged. Existing selected
model tests gain prepared terminal, expiry, historical and forged-authority controls;
those tests and the changed model were initially unexecuted. The second attempt above records later default-schedule execution; prepared mutation tests remain unexecuted.

Both maintained generators reuse one original-call wrapper in the existing projection
helper. Each scheduled output is removed immediately before its original Quint call,
so historical bytes cannot masquerade as a fresh failed counterexample. Original
Quint stdout joins the bounded generation log. On failure, exact schedule/argv/status
and any fresh ITF are retained under the existing diagnostic window: at most 4 MiB
of counterexample bytes plus bounded metadata, sharing the total 8-MiB retained
budget with the log. Missing, partial, truncated or unavailable evidence is explicit;
collection runs before scratch removal and never accepts failed output as fixtures.
No extra Quint invocation or generated-fixture hand edit is introduced. Original
failure remains historical; a repaired source, independent review and separately
admitted new operation are needed before another generation attempt.

## Bounded hosted trace preparation for the terminal correction

Source preparation adds explicit `generate-traces` manual mode to the existing
`wasm-shared` workflow. Default dispatch, PR and push retain the complete 35-minute
qualification route. The preparation job has its own non-canceling concurrency key
and a distinct name; it requires the reviewed expected head to match both the workflow
and clean checkout before setup or generator execution. The existing owning plan
and protected shared foundation supply the canonical .5-P2 → S1 lineage; ordinary
PR admission uses its stable campaign/chain without creating a duplicate issue.

`scripts/prepare-wasm-traces.sh` runs the existing generators sequentially under one
20-minute process-group timeout with 30-second termination grace, inside a 30-minute
hosted job. One original five-minute setup cutoff is recorded before checkout;
late or invalid setup refuses before generation, with no renewed setup window. It requires Node 26, Quint 0.32.0 selected identically through QUINT/PATH,
V8 old-space 2 GiB, core dumps disabled and a 64-MiB hard per-file limit. These are
not aggregate RSS/storage bounds or proof of perfect descendant settlement. A timeout
or unknown cleanup is incomplete and grants no retry. The job uses Contents-read,
with no CLR/Rust/browser/package/publication stages.

The inert helper derives the exact 53 default/16 compatible schedule census from
allowlisted AST declarations without importing generators. Successful preparation
requires both generators to return zero, exactly 71 regular outputs without symbolic or hard links,
unchanged source/model/generator inputs and no other tracked/untracked/ignored
changes. Generated payload is capped at 64 MiB, retained diagnostics at 8 MiB,
manifest at 64 KiB and the inner archive at 80 MiB. The bounded manifest retains
source/tree/workflow/run/attempt, observed tool files/versions, schedules/seeds,
output hashes, stage times and independent original failure/collection/cleanup facts.
Npm version provisioning does not attest the complete dependency closure.

Preflight uses the existing static workflow validator plus focused inert controls
for dispatch/head/dirty-tree/missing-tool refusal, extra/linked outputs, unrelated
changes, failed generation, uncertain cleanup and archive failure. No new formal
pipeline model is justified for this linear route. Expected reuse is each relevant
WASM workflow amendment; the source-check investment is bounded to this small route.
Cold setup/generation fit, avoided runner minutes and monetary savings are unknown.
The first full existing static preflight encountered its expected dirty-worktree
refusal in release-source controls. The committed preparation source `7065525`
then exposed an inherited historical audit test that compared evolving current
consumer bytes with immutable 0.2 qualifier hashes. Both failures retain their
original outcomes. The connected repair uses maintained exact historical byte
fixtures, checked by the unchanged production blob/SHA256 audit predicates and
mutation refusals. Clean shallow CI needs no history fetch or historical Git objects;
the accepted audit and binder are unchanged. Independent review then found that
successful inert collection controls still resolved installed tools before hosted
setup. Source `045756b` replaces those tool identities with inert files/doubles;
the missing-tool refusal remains explicit. This preserves a cold-runner preflight
that needs only its existing Python/Git source-check dependencies. The corrected
clean static preflight passed; no generator or runtime execution is implied.

Preparation artifacts are inputs for independent review and a subsequent generated
fixture commit. They are not coherent qualification or package acceptance. Root must
admit one exact hosted operation after source review and fresh native queue/ref facts.
After accepted generated files are committed, run all 19 required protection checks
and the unchanged full WASM qualification on the exact final source. Authentic model,
.NET/Fable/browser qualification, publication and installed consumer readback remain
separate; historical accepted 0.3 H3/H4 evidence retains its original scope.


Protected qualifier base7397d4b408b8e7b44393fee9f837278723934867;
genuine published producer4afacb501b9371b4cc81494b7bf91b46c880a663,
Contracts/Browser/SDK0.2.0. Genuine prior publication and both-feed installed
qualification37047631080 remain historical CLOSED for their tested scope.

## S1-discovered terminal Process correction — existing .5-P2 → .5-S1

The original 0.3.0 H3 publication and H4/.5-P2 installed qualification remain
accepted for their tested scope (shared foundation evidence, `.github` PR4485,
run37191263989). S1's complete-supervisor-API inventory adds one corrective
assertion: a reset executes its supplied ordinary Process payload and retires the
complete session before the next dispatch or public completion. This is remaining
work under WASM-SHARED-01/.5-P2 feeding S1, without reopening those original receipts.

`LimitedInvokeAndRetire(identity,input)` uses the existing `SubmitLimited` request
limits and unchanged `ProcessGuest(Ordinary,input)` Worker ABI. An immutable flag
on the admitted request selects session retirement on its matching terminal result,
including rejected ABI status, trap, timeout, worker loss, cleanup failure and
exact-current adapter output rejection. Adapter rejection retains its original
reason without fabricating an ABI outcome.
The reducer clears active/candidate/retiring/controls/queues/held state before effects;
workers, timers and followers retire before publishing the preserved actual result.
Terminal dispatch arms its watchdog before posting so a synchronous transport
completion can cancel it without an outer batch arming a stale timer afterward.
Host interprets effects individually and mechanically rechecks pure Lifecycle
eligibility before each one. Disposed sessions cannot post or arm, including an
ordinary ancestor resuming after nested terminal completion. For a batch selecting
a terminal current request, the guard additionally checks its exact current
identity/correlation and timer phase. Earlier synchronous expiry, previous-result
callbacks or replacement therefore suppress stale terminal effects. Ordinary live
reducer behavior/effect order and Freeze retention of an already selected current
request retain their existing behavior.
Queued cancellation, invalid-limit refusal, ordinary admission ceilings and captured
composite deadlines retain their existing behavior. BAR normal invocation/shutdown
and SC2 normal ordinary/ordered/snapshot/replacement paths retain their selected behavior.
Product F# still owns closing/reset state and queue-overflow result mapping.

- [ ] Independently review the exact producer correction, canonical model and
  prepared focused reducer/facade/installed-browser qualification sources.
- [ ] Under separately admitted resources, typecheck/run the canonical model and
  existing boundary/expiry/compatible witnesses, regenerate authentic traces, and
  execute source .NET/Fable correspondence plus both facades and causal mutations.
- [ ] Execute real installed Worker qualification for terminal success/rejection,
  traps/cleanup/timeouts, repeated terminal followers, held snapshots and candidate
  retirement; preserve exact ABI evidence and retirement-before-result ordering.
- [ ] Deliver reviewed source with relevant checks and required coherent CI through
  the existing canonical owner route, which programme ownership must reconcile.
- [ ] Select and qualify an unused coherent successor version, then separately
  authorize publication and verify fresh both-feed installed readback before an S1
  consumer pin. Published 0.3.0 does not expose this new capability.

Current boundary: source preparation only. Model execution, trace regeneration,
CLR/Fable/browser/Worker execution, SDK restore/build/pack, remote source delivery,
publication and installed acceptance have not run for this correction. Committed
pre-existing generated traces remain historical until the maintained generators run;
new required mutation checks intentionally depend on those regenerated fixtures.
The owning shared foundation S1/final .5-P2 contract remains the integration authority.
No runtime shim, JavaScript queue, transport freeze hook or new guest ABI is selected.


## Event boundary model correction

- [x] Select documented universal pre-event expiry; keep published runtime unchanged.
- [x] Retain original input/time/request/phase independently of projected LastAction.
- [x] Generate deterministic before/at/after dispose/completion/timer/phase/freeze/resume/compile/initialize witnesses from one corrected canonical model.
- [ ] Qualify full production.NET/Fable state, ordered effects and raw terminal reason/outcome/dispatch; reject causal mutants.
- [ ] Qualify exact installed0.2.0 Host.Create and connected callback boundary seams.
- [ ] Bind reviewed qualification-only amendment, exact model/qualifier/proof hashes and unchanged executable/package/SDK inputs through typed eligibility.
- [ ] Root native admission and merge readback.

Historical old canonical SHAe463e49a44ded21e1f30379aef25043e2bab2471538b9affde71a721ecfae8a8
and SC2 checkpoint4c207b29 remain unchanged. The original observed dispose
counterexample is preserved: preclock255/deadline255/queueddeadline256, event256;
old model discarded both while installed runtime timed out both. New expected
bytes derive from the independent public every-event contract and canonical
execution, never observed-runtime normalization.

Configured125/44 product coverage, combined Host/product correspondence,
real Worker/browser and separately owned native pairs remain independent OPEN.
No new release is selected unless qualification requires packaged byte changes;
then stop for a separate0.2.1 publication decision.


## Compatible supervisor successor — .5-P2

The existing event boundary qualifier landed through PR681 at protected
0b8a3217c94e54bcb5667df48a42dee4398e21c9. Its model-only approval and
published 0.2.0 packages remain historical. This source window implements the
approved compatible supervisor packet from the unified programme roadmap.

- [x] Add captured per-request deadline/output ceilings and enclosing budgets.
- [x] Add opt-in Sc2SupervisorV1 mixed FIFO, bounded capacity/bytes, one admitted
  and one held snapshot, FIFO tail promotion and inclusive queue-head retirement.
- [x] Drain queued/held work on freeze; atomically commit a matching candidate
  receipt while retaining the recovery fence.
- [x] Preserve default 47 trace bytes and old public signatures; replay 13 selected
  full-state schedules through source and fresh installed .NET/Fable facades.
- [x] Repair valid clock-driven refusal to retain expiry state and effects through
  both Host facades, including matching frozen-commit expiry; retain causal controls.
- [x] Root review actual canonical model/source, 24 reachability witnesses, native
  ApiCompat, official Core byte joins and 8 browser controls at b72f615.
- [x] Qualify coherent 0.3.0 packages freshly packed from exact 9638363; repack after review metadata commit.
- [ ] Root admit source PR, protected merge and exact final release custody.
- [ ] Authenticated release reservation, byte-identical dual publication and fresh
  both-feed installed qualification before downstream activation.

The selected version is compatible MINOR 0.3.0: SubmitLimited, RequestLimits,
selected policy/projections and atomic frozen commit are new receiver-visible API.
Native ApiCompat establishes preservation of published 0.2.0 members; it does not
turn new capability into a patch. Contracts/Browser/guest SDK move coherently.
Protected consumer pins remain 0.2.0 until publication and installed readback.

The committed root review and original Pending owner proof retain their exact
historical digests. SupervisorSourcePolicy permits only unchanged reviewed
semantic inputs plus enumerated source preparation metadata. Its accepted result
is administrative source eligibility, with publication and native acceptance false.
Organization feed occupancy is unresolved when authenticated enumeration returns
403; absent public version/tag does not reserve the release. No publication is
claimed by local candidate qualification.


Pipeline preflight uses the existing static validator against the actual one-job
YAML and candidate metadata. It catches omitted supervisor/bootstrap gates,
wrong ordering, stale candidate package paths and missing version override before
cold compiler/model/browser work. Historic source/model/browser/SDK consumer gates
remain required. The implementation is bounded to this linear workflow; a new
pipeline model would add no ordering coverage. Reuse is expected on each producer
PR and release preparation; no measured runner savings are claimed. The existing
canonical lifecycle remains the semantic model authority.


Root renewed the narrow source/gate binding at exact 9638363 after independent
hash review of the one changed qualifier input, external locked restore and real
NU1403 refusal, complete historical/selected gates, six actual Core DLL joins and
source cleanliness. The root receipt e7b1af81 and owner proof bdf76d88 retain their
exact reviewed-source bindings. Original b72 review 57d1c6c3 and Pending owner
proof eb11b68a remain byte-identical historical records. Renewed typed eligibility
allows only binding metadata after the reviewed source; publication and native
product acceptance remain false. Final source admission and publication are pending.


## 0.3 publisher route — WASM-SHARED-01.3-R1

The compatible supervisor source landed through Game PR 683 at protected producer
`16a401692f4c0dee7f6da1a86d0cd49e6ea3ec2c`, tree
`f06c9fef54b90d9dcd190b5bc1484f2e2e72f67a`. Native shared qualification
[37119471374](https://github.com/FS-GG/FS.GG.Game/actions/runs/37119471374)
passed the equivalent candidate tree at d87f9e3. Its private candidate archives
retain d87 provenance; they are not protected-producer release custody.

- [x] R1: publisher-route source closed through [Game #685](https://github.com/FS-GG/FS.GG.Game/pull/685), protected executor `ccdec51db27e7fbcce8fcfda71a36aa9a2ca7b95`; full native [37132653543](https://github.com/FS-GG/FS.GG.Game/actions/runs/37132653543) passed.
- [ ] R2: prepare once from protected producer P; qualify and retain its exact native originals.
- [ ] R3: review original custody, authenticate current permissions/occupancy, promote without repacking, and qualify fresh installed consumers from both feeds.
- [ ] R4: project actual publication and installed acceptance into the programme and published registry.

The source repair separates producer P from the accepted protected executor E.
[Release preparation](../../.github/workflows/release-wasm.yml) uses read permissions
and P's unchanged preparation and full qualification helpers. Promotion, recovery
and readback consume a successful exact run attempt and artifact digest, including
all original archive member inventories. The
[promotion adapter](../../scripts/wasm-release/promotion.py) creates the literal P
tag through the workflow token, retains an immutable draft transaction binding,
verifies both GitHub Packages members before public NuGet, and publishes exact
SDK assets after readback. Recovery admission verifies every occupied org/public
member against the originals; beginning recovery repeats those readbacks before
any draft or binding write. Package stages also verify occupied members before
writing a missing sibling. Unknown or foreign state refuses; it does not repack
or delete.

Public preflight discovers the package-base resource and reads complete version
indices, including unlisted versions, together with consistent exact endpoints.
Exceptional moderator-removal history and atomic version reservation remain
Unknown. Actual server rejection leaves a partial transaction for same-custody
recovery. GitHub active and deleted occupancy require authenticated complete reads;
NuGet authorization requires a real current trusted-publisher exchange.

[Installed qualification](../../.github/workflows/wasm-installed-org.yml) adds the
unchanged 47 default and 13 selected .NET/Fable schedules and both Host facades,
real lock refusal, official Core byte joins and eight browser controls. The selected
worker comes from the actual fresh restored cache. The protected 0.2 consumer pins,
producer/model/SDK inputs and historical audits stay unchanged. An explicit
accepted-executor binding permits only the finite release/qualification glue delta.

Offline source controls cover actual workflow ordering/privileges, native
run-attempt and artifact eligibility, occupancy refusals, signature trust before
payload allowance, installed member/lock/worker substitutions and interrupted
promotion replay. These checks establish source behavior only. Native preparation,
current authentication, release effects and genuine both-feed acceptance remain
pending. Current published WASM is still 0.2.0; no product adoption is activated.

The R2 cold preparation route first obtains the exact official `quint-co/quint`
`evaluator/v0.6.0` release asset into a fresh private `QUINT_HOME`, verifies its
archive and sole regular binary member, and runs producer P's test and cold/ready
model/witness bounds through the Rust backend before packing. This additional
Rust qualification is separate from the unchanged historical TypeScript proof.
Failure retains diagnostics and prevents packing; successful native preparation
still packs P once and runs all unchanged custody gates before eligible retention.
The bootstrap source does not establish a completed R2 run or publication.


R2 native preparation [37146128965](https://github.com/FS-GG/FS.GG.Game/actions/runs/37146128965)
succeeded at protected executor `94bce296fbdfacdff8492ae02ad62789ac051cb9` and retained
eligible artifact `11282417557`. Root verified the original outer archive and all
159 inventory members across the three original archives, but promotion admission
refused duplicate native step names: the two unnamed pinned checkout actions
produced duplicate run and post-run names. The original archive remains preserved;
no promotion was admitted or executed. R2/R3 acceptance remains pending.

The bounded source repair gives preparation's executor and producer checkouts
explicit unique names. Static preflight checks the actual linear preparation job
and existing duplicate-name refusal before another cold preparation. Existing
source-only release controls pass; the recorded native metadata still refuses,
while a prospective name-only projection satisfies the unchanged validator. This
projection is a source control, not a new native qualification. No custom model is
needed for two step labels; scope and investment are limited to those labels, this
evidence note and existing offline checks, with reuse on future preparations and
no measured runner savings claimed. The six gates, original custody, producer P,
executor binding, permissions and action pins retain their existing behavior.

After this repair lands, root must review a fresh protected executor E and run
fresh preparation, then authenticate its genuine run/attempt, retained archive
and custody before any promotion decision. The old E archive cannot be relabeled
or reused as preparation evidence for the new E. Publication, installed acceptance
and consumer activation remain pending.


The fresh protected preparation [37150982238](https://github.com/FS-GG/FS.GG.Game/actions/runs/37150982238)
at executor `f8b6f5e1ec579fb54d22d4c2b5bb22505cfbb685` passed all six genuine
gates and retained eligible artifact `11284177576`, outer SHA256
`4b0cb76934498e740f6c4c7ca22ecdc088a14e4135945dcad183a2e64033765e`.
Root authenticated the run/attempt, original outer archive and all 159 inventory
members. These originals remain preserved for that executor; the earlier
duplicate-name-refused preparation remains historical and refused.

Promotion [37152503839](https://github.com/FS-GG/FS.GG.Game/actions/runs/37152503839)
at the same executor passed preflight and genuine NuGet trusted-publisher login,
then refused admission on the first authenticated deleted Contracts version
query with HTTP 403. All transaction and publication jobs were skipped. No tag,
draft transaction or package publication was admitted by this failed attempt.

The authentic read-only diagnostic
[37154778966](https://github.com/FS-GG/FS.GG.Game/actions/runs/37154778966)
used the existing workflow token and pinned `2022-11-28` API for both packages:
active queries returned 200 under read and write grants; deleted queries returned
403 under read and 200 with empty first pages under write. This establishes the
job permission correction in this repository without changing credentials or
package access settings. The diagnostic sampled first pages; production still
requires complete pagination and every existing occupancy and non-200 refusal.

The bounded source repair grants job-local `packages: write` to admission,
begin and public, whose adapter paths enumerate active and deleted versions;
org already has that grant. Offline preflight requires exactly those four write
jobs and refuses missing or spurious grants. Global permissions, remaining jobs,
credentials, producer P, helper logic and custody checks retain their existing
bindings. This source correction does not complete R3 or authorize publication.

After source closure, root must review the new protected executor and perform
fresh preparation and original-custody authentication before promotion. The
accepted f8 originals cannot be relabeled as preparation evidence for the changed
executor. Genuine publication, fresh both-feed installed acceptance and consumer
activation remain pending.


Fresh preparation [37157871393](https://github.com/FS-GG/FS.GG.Game/actions/runs/37157871393),
attempt 1, succeeded at protected executor
`9fc4c6d8bd7b0643226d3ba4b4dee99f2aca563c`. Root accepted all six native gates,
artifact `11287095456` with SHA256
`dd040e206f1cc2e73366c94347a172ec7027428baee8f5a49c2e4d0fe0730ff2`,
and all 590 outer and 159 original archive members. This custody remains bound
to that executor and the unchanged producer P.

Fresh promotion [37159983975](https://github.com/FS-GG/FS.GG.Game/actions/runs/37159983975),
attempt 1, passed preflight and native admission, including genuine NuGet login
and authenticated complete active/deleted/public occupancy. Its admission artifact
`11287676062`, SHA256
`fc112c8bbc49418dca88789a0bc89f0c21d0b1e2d3ad8a266a69a746579b5034`,
retains the exact reviewed tuple and all six occupancy values false. Begin then
failed in the inline custody downloader's unchanged clean-checkout guard, before
reaching tag or draft creation. Every package and asset writer was skipped.

The original inline import creates an untracked Python bytecode cache before the
adapter's module body disables bytecode writing. A cold private fixture with the
actual adapter bytes reproduces the same dirty-checkout refusal; `python3 -B`
keeps the checkout clean and passes the unchanged guard. The native failure log
did not print the dirty filename, so that filename is established by the pure
reproduction rather than direct native observation.

This bounded repair adds `-B` to all six inline custody imports. Focused controls
reject its omission in each job and retain genuine dirty-source, wrong-tree and
wrong-head refusals. Permissions, credentials, producer, custody helpers and
publication guards retain their existing behavior. No cache ignore or cleanup
bypasses the clean-checkout boundary.

After source closure and the immediate Unified progress projection, root must
review the new protected executor, run fresh preparation and authenticate its
new originals and tuple before any promotion. The 9fc originals, successful
admission and failed begin remain historical; they cannot be relabeled under the
changed executor. No rerun or recovery is admitted by this source repair.
Publication, genuine both-feed installed acceptance and consumer activation remain
pending; public/default WASM stays 0.2.0.


### R3 successor recovery horizon (source H1)

The first promotion at preparation executor `8b92b7c4a8061157c03155662c97d33c0bc01551`, run `37166812270` attempt 1, failed in `org` with `no durable transaction before writer`. Admission and begin succeeded. The literal producer tag exists, and draft release `402763234` retains journal asset `608859205`, SHA-256 `5b5382d6b246ace3f4841d9b3de17ef1b446ba4e41536caa60ae96be766dcf57`. No native package push or SDK upload was reached; public/assets/complete/readback skipped. Local organization reads remain 403/Unknown. This partial transaction is preserved, rather than replayed as a fresh promotion.

The source-path diagnosis is that the published-only by-tag lookup hides the draft. The native org HTTP status was not logged; current authenticated tag 200, by-tag 404, complete draft listing/by-ID 200 and unchanged pure adapter reproduction support the diagnosis. [GitHub's by-tag endpoint](https://docs.github.com/en/rest/releases/releases#get-a-release-by-tag-name) returns published releases; [authenticated listing](https://docs.github.com/en/rest/releases/releases#list-releases) requires push access for drafts.

- [ ] R3-H1: deliver the complete successor recovery source contract and exact native source qualification. Source controls cover complete draft discovery, every actual asset caller, closed preparation/execution joins, exact historical and current native admission, inspection exclusion and installed cache propagation. The routine source PR may close H1 only after protected merge and native required checks.
- [ ] R3-H2: root accept the protected successor executor and one separately admitted effect-free native inspection. Fix the exact successor commit/tree/workflow and finite changed-path/blob/hash inventory in the separate dispatch binding only after merge. Hold that executor through the native window and immediately project source closure into Unified §0 before selecting recovery.
- [ ] R3-H3: root separately admit one fresh same-custody recovery, attempt 1, naming original run `37166812270` attempt 1. Fresh native occupancy/OIDC and exact occupied-member readback precede missing-member writes. Begin preserves the existing tag/draft/journal. Failure ends the effect window; no automatic retry.
- [ ] R3-H4: independently accept immutable publication and then fresh org/public installed qualification. Both-feed payload/member identity, genuine public repository-signature verification, exact SDK/manifest/checksum assets and unchanged journal are publication conditions. Installation retains locked fresh caches, official Core joins, selected Worker assets, 47 default/13 selected schedules, both Host facades and eight browser controls. Later product adoption and template/workspace composition remain separate.

The original preparation remains run `37163937340` attempt 1, artifact `11289067127`, outer SHA-256 `7b7f957e2506ef1a2d4a257eaa334c80ca116abaa0462c71315a1079aa38d723`, tuple SHA-256 `6ba4084f7835e81cec93fe58b23ee9fa70cec67e0b01e2e4195b16e7f2b5d937`, binding SHA-256 `14d2376b01b3498c9a904ce3bafefa654ae4e241bec06e80c3aad126575e4c73`: six genuine gates, 590 outer files and all 159 original archive members. Historical journal, original admission and original tuple continue naming that preparation executor. A separately reviewed `fsgg.wasm.successor-execution/v1` binding joins it to a clean protected descendant through exact workflow bytes and a finite path/blob/content inventory; it cannot authorize prepare/promote, an arbitrary descendant, a rerun, a changed producer input or a replacement journal. No repacking, relabeling or changed consumer pin is permitted. Source merge is not publication, installed acceptance or activation.

Pipeline preflight uses the existing source-only static/production-caller controls, before costly native qualification. The defect is draft visibility and the two-source join; a new disconnected model would duplicate literal caller checks. Scope is the actual workflow dependencies/permissions, adapter, shell asset callers and installed binder/cache. The investment is bounded to this coherent repair and reusable negative controls; runner cost and whole-item usage remain unknown. The reviewed job-local Contents-write correction covers admission/org/public/readback plus the effect-free inspection, preserving global Contents-read, preflight/prepare grants, Packages/OIDC sets and all credential/action routes. Inspection uses GET-only native transport and emits no reusable recovery admission. An operator's draft visibility does not qualify the workflow token.

Public/default consumers remain 0.2.0; opt-in supervisor selection and producer `16a401692f4c0dee7f6da1a86d0cd49e6ea3ec2c` remain unchanged. The earlier f8/9fc failures and all original evidence retain their outcomes. This horizon has no provider/lifecycle/default/scaffold change, fleet migration or activation.


H1 caller review also requires job-local `actions: read` in the installed matrix: its real binder/cache entry reads the current Actions attempt and authenticates historical run/jobs/artifacts and the original admission download. Global Contents-read and installed Contents-read/Packages-read remain unchanged. Org/public curl readback enforces the same 512 MiB binary ceiling as the shared transport; an oversized response refuses before payload/signature acceptance.

The successor dispatch boundary is the exact protected main source, closed reviewed binding, fresh `workflow_dispatch` attempt 1, and current successful native admission under the scoped workflow token. It does not claim a separate named-operator allowlist. GitHub controls who may dispatch; root retains the external operation admission. Actor/triggering-actor names are not package authority, and no unreviewed operator identity is added to the immutable execution contract.


The separately admitted inspection run [37172924046](https://github.com/FS-GG/FS.GG.Game/actions/runs/37172924046), attempt 1 at `ad5db7ee70cf03fa4d860a0ede7157334d96793c`, failed in source preflight before inspection. Its inherited inspection mode and successor binding reached ordinary promotion test fixtures, causing four fixture errors and one causal assertion failure. All other jobs skipped, no artifacts were produced, and no release effects occurred. This failed run remains historical evidence; inspection and recovery remain open.

The source repair isolates each promotion test's transaction inputs and default mode while retaining explicit successor fixtures and production guards. Source preflight also runs the real promotion suite under inherited inspection and recovery inputs. Only existing test and owning-plan paths change, so the finite successor delta allowlist needs no extension. A distinct protected source executor and separately admitted inspection are required after this repair lands; the original preparation tuple, journal and package bytes remain unchanged.


The distinct inspection [37176783775](https://github.com/FS-GG/FS.GG.Game/actions/runs/37176783775), attempt 1 at `393c1c6caa204268f42b0c705d5d61f43a6ba8e2`, passed source preflight but failed before draft inspection. Its genuine API receipt records five GET 200 responses followed by artifact `11289067127` ZIP HTTP 415. The adapter requested `application/octet-stream` on an Actions archive redirect endpoint and reported its non-200 response as a digest mismatch. A separately authorized JSON-negotiated read recovered the byte-identical original ZIP, SHA-256 `7b7f957e2506ef1a2d4a257eaa334c80ca116abaa0462c71315a1079aa38d723`; unchanged pure validators accepted all 590 outer files and 159 original members. No archive drift was observed. The other eight jobs skipped; this failed run and its receipt artifact remain immutable.

The source repair scopes JSON media negotiation to exact GET Actions artifact ZIP and run-log API routes, keeps redirected archive bytes opaque and release-asset binary negotiation unchanged, and distinguishes HTTP Unknown from actual digest drift. The independent installed repair checks the whole restored archive SHA-512 against its cache sidecar, and computes NuGet's compressed unsigned-content SHA-512 against both `.nupkg.metadata` and the lock, with an exact metadata source. The algorithm follows [SDK 10.0.401's pinned NuGet source](https://github.com/dotnet/dotnet/blob/e34a38d2ae1fc26406a317517196e55c68ff83ab/src/nuget-client/src/NuGet.Core/NuGet.Packaging/Signing/Archive/SignedPackageArchiveUtility.cs): remove the signature span and central record, relocate surviving offsets, adjust EOCD fields, and hash the original compressed bytes. Unsupported ZIP64/multidisk/encrypted or ambiguous layouts refuse. Genuine public repository-signature verification, the original org archive, every payload/Worker member, locked versions and fresh exact-feed caches remain required. Existing accepted 0.2 receivers retain their scoped evidence; no signed-public 0.3 installed success is claimed.

This changes only existing adapter, test and owning-plan paths within the closed successor allowlist. Source controls cover media roles, distinct HTTP/hash refusals, unsigned and signed hash roles, signature position and data descriptors, malformed archive layouts, and cache/source/member substitutions. Authentic retained UTEL signed archives independently match the pinned primary NuGet content-hash projections; those are offline algorithm counterexamples, not WASM installation or signature-trust evidence. Publication, installed acceptance and activation remain open. A new protected executor and separately admitted distinct inspection are required after source closure; no failed inspection is retried or relabeled, and the original preparation tuple/journal/packages stay unchanged.

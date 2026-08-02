namespace FS.GG.Game.Harness

open FS.GG.Game.Core

/// Timestamp-free host events understood by a production journey. Products keep their own key,
/// pointer, menu-action, and deterministic effect-result types.
[<RequireQualifiedAccess>]
type JourneyEvent<'key, 'pointer, 'menu, 'effectResult> =
    | Start
    | MenuAction of 'menu
    | KeyInput of key: 'key * pressed: bool
    | PointerInput of 'pointer
    | Interact
    | Pause
    | Resume
    | FixedTick
    | EffectResult of 'effectResult

/// The production raw-event mapper explicitly says whether a displayed action is wired.
[<RequireQualifiedAccess>]
type JourneyDispatch<'message> =
    | Mapped of 'message list
    | Unbound of action: string

/// A structural or empirical gap in a product's displayed-action coverage. Journey coverage is
/// defined over the events a committed script actually issues; both cases name a way that
/// definition can go blind to an action no player-emittable message reaches (`FS.GG.Game#563`).
[<RequireQualifiedAccess>]
type ActionCoverageGap =
    /// `MapEvent` returns `JourneyDispatch.Unbound action` for `event`, and `event` (by
    /// `adapter.EncodeEvent`) appears in none of the committed scripts supplied to
    /// `Journey.checkActionCoverage`. The arm exists in source and is exercised by nothing: no
    /// gate that only runs committed scripts can ever see it fire or fail to fire.
    | UnexercisedUnbound of action: string * event: string
    /// The declared `vocabulary` supplies only one distinct producible value at the named slot
    /// (`"menu"`, `"key"`, or `"pointer"`). With one inhabitant, no script — however written —
    /// can construct a second value at that slot, so an `Unbound` arm distinguishing it from
    /// anything else is unreachable by construction, not merely unexercised by the current suite.
    /// This is reported independently of `UnexercisedUnbound`: it can be the only signal when the
    /// dead arm never appears in `MapEvent`'s output for the single inhabitant that exists (the
    /// shape `FS.GG.Game#563` was filed against).
    | DegenerateVocabulary of slot: string * inhabitants: int

/// A product-owned adapter over its real composition root. Unlike `Playable`, it owns boot,
/// timestamp-free host mapping, message dispatch, fixed ticks, and deterministic effect results.
type ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint> =
    { RouteId: string
      ScenarioId: string
      TestId: string
      MaxSteps: int
      Boot: unit -> 'model
      MapEvent:
        JourneyEvent<'key, 'pointer, 'menu, 'effectResult> ->
        'model ->
            JourneyDispatch<'message>
      Update: 'message -> 'model -> 'model
      FixedTick: 'model -> 'model
      ApplyEffectResult: 'effectResult -> 'model -> 'model
      IsTerminal: 'model -> bool
      Fingerprint: 'model -> 'fingerprint
      EncodeEvent: JourneyEvent<'key, 'pointer, 'menu, 'effectResult> -> string
      EncodeFingerprint: 'fingerprint -> string }

/// Runner outcome. Exhaustion and unbound displayed actions are explicit failures.
[<RequireQualifiedAccess>]
type JourneyResult =
    | Passed
    | Failed of reason: string

/// The runner route which produced the captured input. A seeded policy is identified separately
/// from replaying its captured fixed script.
[<RequireQualifiedAccess>]
type JourneyInputKind =
    | FixedScript
    | SeededPolicy

/// Opaque machine-issued receipt. Only the production-journey runner can construct one.
[<Sealed>]
type JourneyReceipt

[<RequireQualifiedAccess>]
module JourneyReceipt =
    val schemaVersion: JourneyReceipt -> int
    val runnerIdentity: JourneyReceipt -> string
    val runnerVersion: JourneyReceipt -> string
    val compositionAuthority: JourneyReceipt -> string
    val origin: JourneyReceipt -> Origin
    val routeId: JourneyReceipt -> string
    val scenarioId: JourneyReceipt -> string
    val testId: JourneyReceipt -> string
    val inputKind: JourneyReceipt -> JourneyInputKind
    val inputIdentity: JourneyReceipt -> string
    val inputDigest: JourneyReceipt -> string
    val scriptDigest: JourneyReceipt -> string
    val traceDigest: JourneyReceipt -> string
    val initialFingerprintDigest: JourneyReceipt -> string
    val terminalFingerprintDigest: JourneyReceipt -> string
    val terminalPredicateIdentity: JourneyReceipt -> string
    val terminalPredicateReached: JourneyReceipt -> bool
    val result: JourneyReceipt -> JourneyResult
    val steps: JourneyReceipt -> int
    val maxSteps: JourneyReceipt -> int

/// Non-generic executable proof boundary loaded by `fsgg-playtest`. Implementations can return a
/// receipt only by running the typed journey API; the CLI consumes the opaque value in-process and
/// never accepts production provenance from JSON or another caller-authored text artifact.
type IProductionJourneyProof =
    abstract TestId: string
    abstract Run: unit -> JourneyReceipt

/// Schema-v1 export proof. Its declared identities are independently matched to the opaque receipt
/// after execution; legacy proofs remain executable but cannot mint a serialized v1 receipt.
type IProductionJourneyProofV1 =
    inherit IProductionJourneyProof
    abstract CompositionAuthority: string
    abstract RouteId: string
    abstract ScenarioId: string
    abstract InputIdentity: string
    abstract TerminalPredicateIdentity: string

/// A journey trace, captured event stream, final model, and runner-issued receipt.
type JourneyRun<'model, 'event, 'fingerprint> =
    { Trace: Trace<'fingerprint>
      Captured: 'event list
      Final: 'model
      Receipt: JourneyReceipt }

/// A seeded policy which emits timestamp-free production events from the currently observed model.
type JourneyPolicy<'model, 'event> =
    { DecideEvents: 'model -> Rng -> struct ('event list * Rng) }

/// The verdict of `Journey.checkActionCoverage`: every gap found. Empty means the declared
/// vocabulary is both fully wired, per the committed suite, and rich enough for that proof to mean
/// something — a vocabulary that can express no more than one value per slot proves nothing by
/// staying green, so it cannot be clean by omission.
type ActionCoverageReport = { Gaps: ActionCoverageGap list }

[<RequireQualifiedAccess>]
module ActionCoverageReport =
    /// True only when `Gaps` is empty.
    val isClean: ActionCoverageReport -> bool

    /// One human-readable line per gap, in `Gaps` order — for a failed-test message or a CI log.
    val describe: ActionCoverageReport -> string list

[<RequireQualifiedAccess>]
module Journey =
    val runScriptWithIdentity:
        inputIdentity: string ->
        terminalPredicateIdentity: string ->
        adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint> ->
        script: JourneyEvent<'key, 'pointer, 'menu, 'effectResult> list ->
            JourneyRun<'model, JourneyEvent<'key, 'pointer, 'menu, 'effectResult>, 'fingerprint>

    val runScript:
        adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint> ->
        script: JourneyEvent<'key, 'pointer, 'menu, 'effectResult> list ->
            JourneyRun<'model, JourneyEvent<'key, 'pointer, 'menu, 'effectResult>, 'fingerprint>

    val runPolicyWithIdentity:
        policyIdentity: string ->
        terminalPredicateIdentity: string ->
        adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint> ->
        policy: JourneyPolicy<'model, JourneyEvent<'key, 'pointer, 'menu, 'effectResult>> ->
        seed: uint64 ->
            JourneyRun<'model, JourneyEvent<'key, 'pointer, 'menu, 'effectResult>, 'fingerprint>

    val runPolicy:
        adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint> ->
        policy: JourneyPolicy<'model, JourneyEvent<'key, 'pointer, 'menu, 'effectResult>> ->
        seed: uint64 ->
            JourneyRun<'model, JourneyEvent<'key, 'pointer, 'menu, 'effectResult>, 'fingerprint>

    /// Names every displayed-action coverage gap the committed suite cannot see on its own
    /// (`FS.GG.Game#563`): an `Unbound` arm `MapEvent` can produce for a declared `vocabulary`
    /// event that no script in `committedScripts` ever issues, and a `'menu`/`'key`/`'pointer`
    /// slot whose `vocabulary` supplies only one distinct producible value (`'menu` instantiated
    /// with `unit` being the shape this was filed against).
    ///
    /// `vocabulary` must be the product's own declaration of every event its displayed surface can
    /// emit — every menu action, every key, every pointer gesture a player can actually trigger.
    /// This sweep evaluates `MapEvent` once per vocabulary event against the freshly booted model;
    /// it does not vary model state, so an `Unbound` arm that only a later game state reaches is
    /// outside what one call proves. A `vocabulary` narrower than the real product understates
    /// coverage, never overstates it: an event never declared here is invisible to this check the
    /// same way it is invisible to the committed suite. This also cannot see a `'message` that
    /// `MapEvent` produces for no event at all — a message no input path emits rather than one it
    /// explicitly refuses — which is `FS.GG.Game#565`'s scope, not this function's.
    val checkActionCoverage:
        adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint> ->
        vocabulary: JourneyEvent<'key, 'pointer, 'menu, 'effectResult> list ->
        committedScripts: JourneyEvent<'key, 'pointer, 'menu, 'effectResult> list list ->
            ActionCoverageReport

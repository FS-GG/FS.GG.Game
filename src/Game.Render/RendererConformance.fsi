namespace FS.GG.Game.Render

/// Public contract type exposed by the FS.GG.Game.Render package.
/// The outcome of one conformance check: what was required, whether it held, and what was seen. Built only by
/// `RendererConformance.run`.
[<Sealed>]
type ConformanceResult =

    /// The requirement, prefixed by its section (`subject`, `registry`, `round-trip`, `switch`, `dispose`,
    /// `late failure`, `present failure`, `viewport`).
    member Check: string

    /// Whether the requirement held.
    member Passed: bool

    /// What was observed. Empty when the check passed.
    member Detail: string

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// A running renderer host, as the conformance kit drives it: the browser's `RendererDriver` (through
/// `BrowserConformance.host`), or `RendererConformance.inProcess`. Built by `RendererConformance.host`. Each call
/// answers for the model the host has realised, so `Model ()` after `Dispatch` already reflects any fallback.
[<Sealed>]
type ConformanceHost<'State> =

    /// The current Elmish model.
    member Model: unit -> RendererModel<'State>

    /// The backend whose resources the host holds, if any.
    member Mounted: unit -> RendererBackend option

    /// Apply one message and everything it causes.
    member Dispatch: msg: RendererMsg<'State> -> unit

    /// Report that `backend`'s live mount failed after its `Mount` returned `Ok`, with the semantics of
    /// `RendererDriver.failBackend`: a `MountFailed` for the model's current attempt, ignored unless `backend` is
    /// selected and active.
    member FailBackend: backend: RendererBackend -> reason: string -> unit

    /// Release the host and whatever it holds. The host is inert afterwards.
    member Dispose: unit -> unit

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// How the kit makes one backend's adapter fail late through the adapter's OWN reporting path: in the browser, Pixi's
/// asynchronous initialisation fails and the adapter reports through `PixiOptions.OnFailure`, the
/// `LateFailureRoute` and `RendererDriver.failBackend`. Built by `LateFailureTrigger.create`.
[<Sealed>]
type LateFailureTrigger

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module LateFailureTrigger =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// `arm ()` makes the backend's NEXT mount return `Ok` and then fail later, reported (or, for a mount already
    /// disposed, not reported) by the adapter itself. `settle` lets the armed failure happen, waits until it has,
    /// whether or not it was reported, and says whether it did.
    val create: arm: (unit -> unit) -> settle: Async<bool> -> LateFailureTrigger

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// What the kit needs to know about one game's renderers. Built by `RendererConformance.subject`, which fills the
/// in-process defaults, and adjusted by `RendererConformance.withStart`, `withWaitUntil`, `withLateFailure` and
/// `withViewport`.
[<NoEquality; NoComparison>]
type ConformanceSubject<'State>

/// Public contract module exposed by the FS.GG.Game.Render package.
///
/// The conformance kit every renderer adapter must pass, independent of any test framework. `run` instruments the
/// registry's adapters (counting mounts, presented states and disposals, never changing what they do), drives a
/// host through the Elmish messages and returns one `ConformanceResult` per requirement:
///
/// - **subject**: at least two distinct states; the expected backends include SVG;
/// - **registry**: exactly the expected backends are offered; selecting one that has no adapter is refused as
///   `Unavailable` and acquires nothing;
/// - **round-trip**: on every backend, each presented state comes back unchanged from the model, reaches the
///   adapter unchanged and is what the backend shows, and the output probe rejects at least one other state, so a
///   view or probe that ignores the state fails;
/// - **switch**: runtime switches through `RendererMsg.SelectBackend`, covering every ordered pair of offered
///   backends, keep the game state, leave only the selected backend holding resources and show the kept state;
/// - **dispose**: switching away and disposing the host release every resource (including a canvas's WebGL
///   context), each mount is disposed exactly once and a disposed host stays inert;
/// - **late failure (host)**, for each non-default backend: `FailBackend` for the live mount falls back to SVG
///   with the state intact and releases the backend, a report for an unselected backend is ignored, and
///   re-selecting recovers;
/// - **late failure (adapter)**, for each backend with a `LateFailureTrigger`: the adapter's own report of its live
///   mount's failure falls back to SVG with the state intact and releases the backend, and a failure of a mount
///   already disposed is not reported, so the live mount of the same backend stays (the live-mount rule);
/// - **present failure**, for each backend: the kit makes the next `Present` throw (the adapter is wrapped; its own
///   code is not edited). A canvas backend falls back to SVG with the latest state and is released. The DEFAULT
///   backend is released and mounted again with the latest state, exactly one live mount remains, and the cause is
///   in `Refusal`. If that remount fails too, `Status` is `Failed` with the cause, nothing is mounted or held, and
///   the next state mounts and shows. A throwing `Present` is therefore never a silent blank;
/// - **viewport**, when the subject declares one (`withViewport`): every state is presented at its own viewport, read
///   back from the backend, in both directions, with one live mount at the end and every superseded one disposed once.
///   When the registry also declares the viewport (`RendererRegistry.withViewport`), a mount is replaced exactly when
///   the viewport changes and states with the same viewport are presented in place.
[<RequireQualifiedAccess>]
module RendererConformance =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A host from its operations: the model, the mounted backend, dispatch, `failBackend` and dispose.
    val host:
        model: (unit -> RendererModel<'State>) ->
        mounted: (unit -> RendererBackend option) ->
        dispatch: (RendererMsg<'State> -> unit) ->
        failBackend: (RendererBackend -> string -> unit) ->
        dispose: (unit -> unit) ->
            ConformanceHost<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// An in-process host over `RendererSelection` and `RendererHost`, with the browser driver's queueing (messages
    /// dispatched while it runs are applied in order after the current one) and its `failBackend` semantics.
    val inProcess: registry: RendererRegistry<'State> -> state: 'State -> ConformanceHost<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A subject for `registry`, which must offer exactly `expected`; `states` are the game states to present (at
    /// least two distinct ones, the first is the initial state); `resources backend` is what `backend` holds now,
    /// observed independently of its adapter (zero when unmounted, at least one while mounted); `shows host backend
    /// state` reads `backend`'s output on `host` back and says whether it shows `state`. The host is the in-process
    /// one, waits are immediate and no late-failure trigger is declared.
    val subject:
        registry: RendererRegistry<'State> ->
        expected: RendererBackend list ->
        states: 'State list ->
        resources: (RendererBackend -> int) ->
        shows: (ConformanceHost<'State> -> RendererBackend -> 'State -> Async<Result<unit, string>>) ->
            ConformanceSubject<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Start each host with `start`, over the registry it is given (the kit passes an instrumented copy).
    val withStart:
        start: (RendererRegistry<'State> -> 'State -> ConformanceHost<'State>) ->
        subject: ConformanceSubject<'State> ->
            ConformanceSubject<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Wait with `waitUntil condition`: bounded, and saying whether `condition` held. Asynchronous backends need
    /// frames to pass.
    val withWaitUntil:
        waitUntil: ((unit -> bool) -> Async<bool>) -> subject: ConformanceSubject<'State> -> ConformanceSubject<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Declare how `backend`'s adapter fails late, replacing any trigger already declared for it. The adapter
    /// late-failure checks run for every backend with a trigger.
    val withLateFailure:
        backend: RendererBackend ->
        trigger: LateFailureTrigger ->
        subject: ConformanceSubject<'State> ->
            ConformanceSubject<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Declare the viewport the game's states have: `expected state` is the `(width, height)` in pixels the backend
    /// must present `state` at, and `actual host backend` reads the viewport `backend` has now (`None` when it has
    /// none). The viewport checks run for every offered backend, and the subject must contain states with at least two
    /// different viewports, or the check fails rather than pass vacuously. A backend whose mount is fixed to its
    /// viewport conforms by declaring it with `RendererRegistry.withViewport`, or by remounting itself.
    val withViewport:
        expected: ('State -> float * float) ->
        actual: (ConformanceHost<'State> -> RendererBackend -> Async<(float * float) option>) ->
        subject: ConformanceSubject<'State> ->
            ConformanceSubject<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Run every section against `subject` and return the results in order. Adapter exceptions are contained by the
    /// host as usual; an exception from the subject's own functions propagates.
    val run: subject: ConformanceSubject<'State> -> Async<ConformanceResult list> when 'State: equality

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The results that did not pass.
    val failures: results: ConformanceResult list -> ConformanceResult list

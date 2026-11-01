namespace FS.GG.Game.Render

/// Public contract type exposed by the FS.GG.Game.Render package.
/// The outcome of one conformance check: what was required, whether it held, and what was seen. Built only by
/// `RendererConformance.run`.
[<Sealed>]
type ConformanceResult =

    /// The requirement, prefixed by its section (`subject`, `registry`, `round-trip`, `switch`, `dispose`,
    /// `late failure`, `interaction`).
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
/// One entry of a backend's accessible mirror, as read back from the backend's output.
type MirrorEntry =
    {
        /// The object the entry selects (`SelectableObject.Id`).
        Id: string
        /// The entry's accessible text.
        Label: string
        /// Whether the entry says its object is selected (`aria-pressed`).
        Pressed: bool
    }

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// How the kit plays a backend's input host from outside (FS.GG.Game#751): REAL input (a browser's trusted pointer
/// and keyboard events, never synthesised DOM events) delivered to the mounted backend's output, real keyboard focus,
/// the accessible mirror read back, and an independent census of the input hosts the backend holds. Built by
/// `InteractionProbe.create`.
[<Sealed>]
type InteractionProbe

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module InteractionProbe =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// - `press backend point`: a real primary pointer press and release at `point` (scene coordinates) on
    ///   `backend`'s output;
    /// - `move backend point`: a real pointer move to `point` over `backend`'s output;
    /// - `focus backend`: give `backend`'s focusable output (its SVG root or canvas) the keyboard focus;
    /// - `key name`: a real key press (a `KeyboardEvent.key` value such as `ArrowRight`, `Enter`, `Escape`, `Tab` or
    ///   `" "`) delivered to whatever has the keyboard focus;
    /// - `focused backend`: what has the keyboard focus, as `surface` (the backend's output), `control:<id>` (the
    ///   backend's mirror entry of object `id`) or anything else for other elements;
    /// - `mirror backend`: `backend`'s accessible mirror (`None` when there is none);
    /// - `liveWrites backend`: how many times `backend`'s live status has been written so far;
    /// - `tabIndex backend`: the `tabindex` of `backend`'s output;
    /// - `inputHosts backend`: what `backend`'s input hosts hold now (listeners and mirrors, observed independently
    ///   of the adapter). The kit compares it with its value before each host starts: an input host must raise it
    ///   while mounted and bring it back when released.
    val create:
        press: (RendererBackend -> FS.GG.UI.Scene.Point -> Async<unit>) ->
        move: (RendererBackend -> FS.GG.UI.Scene.Point -> Async<unit>) ->
        focus: (RendererBackend -> Async<unit>) ->
        key: (string -> Async<unit>) ->
        focused: (RendererBackend -> string) ->
        mirror: (RendererBackend -> MirrorEntry list option) ->
        liveWrites: (RendererBackend -> int) ->
        tabIndex: (RendererBackend -> string option) ->
        inputHosts: (RendererBackend -> int) ->
            InteractionProbe

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// What the kit needs to know about one game's renderers. Built by `RendererConformance.subject`, which fills the
/// in-process defaults, and adjusted by `RendererConformance.withStart`, `withWaitUntil`, `withLateFailure` and
/// `withInteraction`.
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
/// - **interaction**, when `withInteraction` declared one (FS.GG.Game#751): the states declare valid selectable
///   objects; then on every backend, with real input, the input host is attached while mounted, the accessible
///   mirror lists every object with its label, a pointer move leaves the surface at tabindex 0, the keyboard
///   focuses objects in order and selects and clears through the game's own rule, a pointer press on each object
///   reachable by a pointer selects it (the backend's hit test agrees with `SceneInteraction.hitTest`) and a press
///   where no object is asks for nothing, Tab walks the mirror entries in order and Space selects one, keyboard
///   focus stays on that entry across the selection's Present and across unchanged Presents, an unchanged Present
///   does not write the live status, the selection survives a switch away and back, and switching away and
///   disposing the host release the input host.
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
    /// Declare the game's interaction (FS.GG.Game#751), replacing any declared before: the registry's adapters report
    /// to `route` (`SceneRenderers.withInteraction interaction route`), and `probe` plays each backend's input host.
    /// The interaction checks run on every offered backend. The kit binds `route` to each host it starts, applying
    /// every request with `SceneInteraction.apply` and presenting the result, and unbinds it when the section ends.
    val withInteraction:
        interaction: SceneInteraction<'State> ->
        route: SelectionRoute ->
        probe: InteractionProbe ->
        subject: ConformanceSubject<'State> ->
            ConformanceSubject<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Run every section against `subject` and return the results in order. Adapter exceptions are contained by the
    /// host as usual; an exception from the subject's own functions propagates.
    val run: subject: ConformanceSubject<'State> -> Async<ConformanceResult list> when 'State: equality

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The results that did not pass.
    val failures: results: ConformanceResult list -> ConformanceResult list

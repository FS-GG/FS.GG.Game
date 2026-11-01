namespace FS.GG.Game.Render

/// Public contract type exposed by the FS.GG.Game.Render package.
/// The outcome of one conformance check: what was required, whether it held, and what was seen.
[<NoComparison>]
type ConformanceResult =
    {
        /// The requirement, prefixed by its section (`registry`, `round-trip`, `switch`, `dispose`, `late failure`).
        Check: string
        /// Whether the requirement held.
        Passed: bool
        /// What was observed. Empty when the check passed and there is nothing to add.
        Detail: string
    }

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// A running renderer host, as the conformance kit drives it: the browser's `RendererDriver` (through the
/// browser host's conformance adapter), or `RendererConformance.inProcess`. Each call answers for the model the
/// host has realised, so `Model ()` after `Dispatch` already reflects any fallback the message caused.
[<NoEquality; NoComparison>]
type ConformanceHost<'State> =
    {
        /// The current Elmish model.
        Model: unit -> RendererModel<'State>
        /// The backend whose resources the host holds, if any.
        Mounted: unit -> RendererBackend option
        /// Apply one message and everything it causes.
        Dispatch: RendererMsg<'State> -> unit
        /// Report that `backend`'s live mount failed after its `Mount` returned `Ok`, with the semantics of
        /// `RendererDriver.failBackend`: a `MountFailed` for the model's current attempt, ignored unless `backend` is
        /// selected and active.
        FailBackend: RendererBackend -> string -> unit
        /// Release the host and whatever it holds. The host is inert afterwards.
        Dispose: unit -> unit
    }

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// What the kit needs to know about one game's renderers. `RendererConformance.subject` fills the in-process
/// defaults; a browser harness replaces `Start` and `WaitUntil`.
[<NoEquality; NoComparison>]
type ConformanceSubject<'State> =
    {
        /// The registry under test, typically `SceneRenderers.registry` of the game's declaration.
        Registry: RendererRegistry<'State>
        /// The backends the game claims to offer. The registry must offer exactly these, and `Svg` is required.
        Expected: RendererBackend list
        /// Game states to present, at least two distinct ones. The first is the initial state.
        States: 'State list
        /// Start a host over the registry it is given (the kit passes an instrumented copy of `Registry`).
        Start: RendererRegistry<'State> -> 'State -> ConformanceHost<'State>
        /// The resources `backend` holds now, as observed independently of its adapter: SVG roots for SVG; canvases,
        /// engines or applications and live WebGL contexts for the canvas backends. Zero when nothing of that backend
        /// is mounted, and at least one while it is.
        Resources: RendererBackend -> int
        /// Whether `backend`'s output, as mounted on the host, shows `state`: read the DOM or a pixel back, not the
        /// model. `Error detail` when it does not. The host is passed so a canvas probe can present the state again and
        /// read the frame it renders.
        Shows: ConformanceHost<'State> -> RendererBackend -> 'State -> Async<Result<unit, string>>
        /// Wait until `condition` holds, bounded, and say whether it did. Asynchronous backends (Pixi initialises its
        /// renderer and releases its application after a promise settles) need frames to pass; in process it is
        /// `condition ()`.
        WaitUntil: (unit -> bool) -> Async<bool>
    }

/// Public contract module exposed by the FS.GG.Game.Render package.
///
/// The conformance kit every renderer adapter must pass, independent of any test framework. `run` instruments the
/// registry's adapters (counting mounts, presented states and disposals, never changing what they do), drives a
/// host through the Elmish messages and returns one `ConformanceResult` per requirement:
///
/// - **registry**: exactly the expected backends are offered; selecting one that has no adapter is refused as
///   `Unavailable` and acquires nothing;
/// - **round-trip**: on every backend, each presented state comes back unchanged from the model, reaches the
///   adapter unchanged and is what the backend shows;
/// - **switch**: runtime switches through `RendererMsg.SelectBackend`, covering every ordered pair of offered
///   backends, keep the game state, leave only the selected backend holding resources and show the kept state;
/// - **dispose**: switching away and disposing the host release every resource (including a canvas's WebGL
///   context), each mount is disposed exactly once and a disposed host stays inert;
/// - **late failure**: for each canvas backend, a late failure reported by its live mount (`failBackend`) falls
///   back to SVG with the state intact and releases the backend, a report from a disposed mount of the same backend
///   is dropped (the live-mount rule), a report for an unselected backend is ignored, and re-selecting recovers.
[<RequireQualifiedAccess>]
module RendererConformance =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// An in-process host over `RendererSelection` and `RendererHost`, with the browser driver's queueing (messages
    /// dispatched while it runs are applied in order after the current one) and its `failBackend` semantics.
    val inProcess: registry: RendererRegistry<'State> -> state: 'State -> ConformanceHost<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A subject with the in-process `Start` and an immediate `WaitUntil`.
    val subject:
        registry: RendererRegistry<'State> ->
        expected: RendererBackend list ->
        states: 'State list ->
        resources: (RendererBackend -> int) ->
        shows: (ConformanceHost<'State> -> RendererBackend -> 'State -> Async<Result<unit, string>>) ->
            ConformanceSubject<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Run every section against `subject` and return the results in order. Adapter exceptions are contained by the
    /// host as usual; an exception from the subject's own functions propagates.
    val run: subject: ConformanceSubject<'State> -> Async<ConformanceResult list> when 'State: equality

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The results that did not pass.
    val failures: results: ConformanceResult list -> ConformanceResult list

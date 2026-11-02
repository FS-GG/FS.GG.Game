namespace FS.GG.Game.Render

/// Public contract type exposed by the FS.GG.Game.Render package.
/// The outcome of one conformance check: what was required, whether it held, and what was seen. Built only by
/// `RendererConformance.run`.
[<Sealed>]
type ConformanceResult =

    /// The requirement, prefixed by its section (`subject`, `registry`, `round-trip`, `switch`, `dispose`,
    /// `late failure`, `present failure`, `viewport`, `pixels`, `interaction`).
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
/// One pixel a backend must show (FS.GG.Game#753): the pixel containing `Point` (scene coordinates, y down, one unit
/// per pixel of the scene's own size) shows the scene colour `Colour`. Built by `PixelExpectation.create`.
[<Sealed>]
type PixelExpectation =

    /// Where, in scene coordinates.
    member Point: FS.GG.UI.Scene.Point

    /// The scene colour the pixel must show (an opaque one; black for a pixel nothing is drawn on).
    member Colour: FS.GG.UI.Scene.Color

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module PixelExpectation =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The pixel containing `point` must show `colour`.
    val create: point: FS.GG.UI.Scene.Point -> colour: FS.GG.UI.Scene.Color -> PixelExpectation

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// How the kit reads what a backend DREW (FS.GG.Game#753): the colour of canvas pixels (or of the rasterised SVG) at
/// scene points, never the adapter's own state. Built by `PixelProbe.create`.
[<Sealed>]
type PixelProbe

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module PixelProbe =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// - `read backend points`: the RGB of the pixel containing each point, in order, as `backend` shows it now (`None`
    ///   for a point it has no pixel for). The kit presents the state again just before each call, so a canvas that
    ///   renders on `Present` is read in the task that rendered it;
    /// - `appearance backend colour`: the RGB `backend` shows for the scene colour `colour` (a lit backend shows it
    ///   scaled);
    /// - `tolerance backend`: how far, per channel, a shown pixel may be from `appearance`.
    val create:
        read: (RendererBackend -> FS.GG.UI.Scene.Point list -> Async<(int * int * int) option list>) ->
        appearance: (RendererBackend -> FS.GG.UI.Scene.Color -> int * int * int) ->
        tolerance: (RendererBackend -> int) ->
            PixelProbe

/// Public contract module exposed by the FS.GG.Game.Render package.
///
/// The pixels a scene decides (FS.GG.Game#753), for `RendererConformance.withPixels`.
[<RequireQualifiedAccess>]
module ScenePixels =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Pixels `scene` decides on a canvas of `width` x `height` scene units, from its rectangles, ellipses and circles
    /// (through groups and translations): for each node its middle pixel, the first whole pixel inside and outside the
    /// middle of each edge of a rectangle (a rectangle that overhangs the canvas is read at the canvas edge), and the
    /// pixel one clear of each extreme of a round shape (the pixel its extreme touches takes antialiasing from some
    /// rasterisers). A pixel is listed when the last node that wholly covers it is opaque and no later node covers it
    /// in part, or when no node touches it, in which case it must show the clear colour (black, as SVG, Pixi and
    /// Babylon all do), so a node drawn larger than declared fails it. A backend that draws a node at another
    /// position or size, or a later node at another layer, fails it too.
    ///
    /// Only those three kinds are modelled. A node of another kind whose bounds are known (FS.GG.Game#789, ADR D6) —
    /// a stroked rectangle or ellipse, a line, a path, points or an arc whose paint has no blur, drop shadow or
    /// displacing path effect; an image; a scene clipped to a rectangle or a path — leaves open every pixel it touches
    /// inside its bounds, grown by its stroke reach (the stroke width, times the miter limit at a mitred join) plus one
    /// unit, and draws nothing outside them. A node the kit cannot bound (text, glyph runs, regions, vertices, pictures,
    /// a filtered paint...) draws where the kit cannot say, so it leaves open every pixel it could overdraw: nothing is
    /// expected to be the clear colour in a scene that has one, and a pixel is decided only by an opaque node that
    /// wholly covers it AFTER the last such node. A scene made of unmodelled nodes alone lists no pixel.
    val expectations: width: float -> height: float -> scene: FS.GG.UI.Scene.Scene -> PixelExpectation list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// `ScenePixels.expectations` followed by the corner and quarter-edge samples of ADR D6 (FS.GG.Game#789, review R3
    /// of #794): for every rectangle, the first whole pixel inside and outside each corner (the outside one diagonally
    /// past it), and the first whole pixel inside and outside each edge at a quarter and at three quarters of its
    /// length, each kept when the scene decides it by the same rule. Identity frames and index badges are built from
    /// bars that differ at corners and quarter points, where the middle-of-edge samples cannot see them: with these
    /// samples the kit tells every pair of the five identity frames, and every pair of the nine badge digits, apart.
    val identityExpectations: width: float -> height: float -> scene: FS.GG.UI.Scene.Scene -> PixelExpectation list

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// What the kit needs to know about one game's renderers. Built by `RendererConformance.subject`, which fills the
/// in-process defaults, and adjusted by `RendererConformance.withStart`, `withWaitUntil`, `withLateFailure`,
/// `withViewport` and `withInteraction`.
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
///   the viewport changes and states with the same viewport are presented in place;
/// - **pixels**, when `withPixels` declared one (FS.GG.Game#753): on every backend each state's drawn pixels are the
///   scene's, at the declared positions and sizes; with `withInteraction` too, each box object's hit region and drawn
///   edge coincide;
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
    /// Declare the game's input pipeline for the kit's `input` section (FS.GG.Game#787, `docs/adr/0001-standard-input-pipeline.md`
    /// "Kit specification"), replacing any declared before: the registry's input hosts report to `route`, `probe` plays
    /// them with real input, and `events` reads back, in order, the events the game's binding of `route` received. The
    /// section checks the events (source, modifiers, primary, secondary, repeated activation, hover, focus, Cancel), the
    /// drawn focus ring, the single-Tab-stop listbox or grid surface (pointer-only, grouped and cell objects) and
    /// disposal on every backend, and a selection that the game answers later (through a guest) within the subject's
    /// wait. Contract only:
    /// FS.GG.Game#788 implements it, and until then it raises.
    val withInput:
        interaction: SceneInteraction<'State> ->
        route: InputRoute ->
        probe: InteractionProbe ->
        events: (unit -> InputEvent list) ->
        subject: ConformanceSubject<'State> ->
            ConformanceSubject<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Declare that `backend`'s input host is the game's own (a hybrid page: SC2's own SVG map beside the canvas
    /// backends, FS.GG.Game#772). The `interaction` and `input` sections then check that backend through `probe` and
    /// the events it reports, not through the library's mirror markup. Contract only: FS.GG.Game#788 implements it,
    /// and until then it raises.
    val withOwnInputHost: backend: RendererBackend -> subject: ConformanceSubject<'State> -> ConformanceSubject<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Declare what the states must DRAW (FS.GG.Game#753), replacing any declared before: `expected state` lists the
    /// pixels the state must show (`ScenePixels.expectations` of its scene), and `probe` reads each backend's drawn
    /// pixels. The pixel checks run on every offered backend: every state's pixels are shown at their declared
    /// positions, and, when `withInteraction` is declared too, the hit region of each box object and its drawn edge
    /// coincide (the pixels just inside and just outside each edge differ, and a real press on each is where
    /// `SceneInteraction.hitTest` says).
    val withPixels:
        expected: ('State -> PixelExpectation list) ->
        probe: PixelProbe ->
        subject: ConformanceSubject<'State> ->
            ConformanceSubject<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Run every section against `subject` and return the results in order. Adapter exceptions are contained by the
    /// host as usual; an exception from the subject's own functions propagates.
    val run: subject: ConformanceSubject<'State> -> Async<ConformanceResult list> when 'State: equality

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The results that did not pass.
    val failures: results: ConformanceResult list -> ConformanceResult list

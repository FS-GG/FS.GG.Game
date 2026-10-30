namespace FS.GG.Game.Render.Browser

open Browser.Types
open FS.GG.Game.Render
open FS.GG.UI.Scene

/// Options for a Pixi canvas: pixel size, accessible label, antialiasing, and where an asynchronous failure goes.
[<NoEquality; NoComparison>]
type PixiOptions =
    {
        /// Accessible label of the canvas. Must not be blank.
        Label: string
        /// Canvas width in pixels. Finite and greater than zero.
        Width: float
        /// Canvas height in pixels. Finite and greater than zero.
        Height: float
        /// Request antialiasing from the WebGL context.
        Antialias: bool
        /// Pixi initialises its renderer asynchronously, after `Mount` has already returned `Ok`. If that
        /// initialisation fails, the live mount reports the reason here, from a promise callback and never from
        /// inside `Mount`, `Present` or `Dispose`. A host wires it to
        /// `RendererDriver.failBackend driver RendererBackend.Pixi`, which falls back to SVG. A mount that was
        /// already disposed reports nothing. Whatever is reported, the next `Present` on that mount raises the same
        /// reason, so a host without the callback still contains the failure.
        OnFailure: string -> unit
    }

/// The lifetime totals of the Pixi adapter in this page: what it acquired and what it released. These are the
/// adapter's own books; the browser smoke checks them against Pixi's live objects and the DOM.
[<NoEquality; NoComparison>]
type PixiCounters =
    {
        /// Pixi applications created.
        ApplicationsCreated: int
        /// Pixi applications destroyed.
        ApplicationsDestroyed: int
        /// Display objects (one per drawable scene node) created.
        ShapesCreated: int
        /// Display objects destroyed.
        ShapesDestroyed: int
        /// Asynchronous renderer initialisations that failed.
        InitFailures: int
        /// Scene nodes the Pixi projection has no shape for, summed over every presented scene.
        UnsupportedNodes: int
    }

/// What Pixi itself reports about the applications that have not been destroyed.
[<NoEquality; NoComparison>]
type PixiLive =
    {
        /// Applications created and not yet destroyed (including one whose asynchronous initialisation is pending).
        Applications: int
        /// Per-frame listeners registered on those applications' tickers (the render callback is one per application).
        TickerListeners: int
        /// Children of those applications' stages: one display object per drawable scene node.
        DisplayObjects: int
    }

/// The PixiJS renderer adapter for the renderer-selection core, over the Pixi bindings
/// (`FS.GG.Game.Render.PixiBindings`, generated from FS.GG.Templates#808 by `scripts/materialize-js-bindings.sh`).
///
/// It projects the SAME `FS.GG.UI.Scene` the SVG adapter draws, so one `view: 'State -> Scene` can feed every
/// backend: `Rectangle`, `FilledEllipse` and `Circle` become Pixi `Graphics` (later nodes on top), `Group`,
/// `Translate` and `Empty` are structural, and every other node is counted in `PixiCounters.UnsupportedNodes` and
/// not drawn. A mount owns one canvas, one Pixi application (its own ticker renders every frame) and one display
/// object per drawable node. `Present` reconciles those in place. Because Pixi's renderer initialises
/// asynchronously, a mount is `Ok` before anything is drawn: states presented meanwhile are kept (the latest
/// wins) and drawn when initialisation completes, and a `Dispose` during initialisation removes the canvas at
/// once and destroys the application as soon as initialisation settles.
[<RequireQualifiedAccess>]
module PixiSurface =

    /// Options with antialiasing off and no failure callback.
    val defaultOptions: label: string -> width: float -> height: float -> PixiOptions

    /// The Pixi mount for a `Scene`. Each call appends one canvas to `container` and starts one application on it.
    /// Refusals that can be decided synchronously (invalid options, no WebGL context) are returned as `Error`,
    /// never thrown, and release whatever was acquired.
    val mountScene:
        container: HTMLElement -> options: PixiOptions -> scene: Scene -> Result<RendererMount<Scene>, string>

    /// The Pixi adapter for a game-state type: every state is projected through `view` first.
    val adapter: container: HTMLElement -> options: PixiOptions -> view: ('State -> Scene) -> RendererAdapter<'State>

    /// `RendererRegistry.register (adapter ...)`: makes `RendererBackend.Pixi` selectable for this state type.
    val register:
        container: HTMLElement ->
        options: PixiOptions ->
        view: ('State -> Scene) ->
        registry: RendererRegistry<'State> ->
            RendererRegistry<'State>

    /// The lifetime totals so far.
    val counters: unit -> PixiCounters

    /// Pixi's own report on the applications still alive.
    val live: unit -> PixiLive

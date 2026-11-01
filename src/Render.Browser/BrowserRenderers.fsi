namespace FS.GG.Game.Render.Browser

open Browser.Types
open FS.GG.Game.Render
open FS.GG.UI.Scene

/// Where an asynchronously initialising canvas adapter (Pixi) reports a failure that happens after its `Mount`
/// returned `Ok`. The adapter is built with the registry, before the driver that must hear the report exists, so a
/// route is created first, handed to the adapter, and bound to the driver by `BrowserRenderers.start`.
///
/// The route forwards to `RendererDriver.failBackend` and keeps that function's live-mount rule: the adapter reports
/// only while its mount is undisposed (`PixiSurface` does), because the driver cannot tell two mounts of the same
/// backend apart.
[<Sealed>]
type LateFailureRoute =

    /// Report `backend`'s late failure to the bound driver. Dropped while no driver is bound.
    member Report: backend: RendererBackend -> reason: string -> unit

    /// The report of the driver bound now (a no-op while none is). `BrowserRenderers.surfaces` captures it when each
    /// Pixi mount is created, so a late failure of a mount reaches the driver that mounted it and never a driver
    /// bound later to the same route.
    member Capture: unit -> (RendererBackend -> string -> unit)

/// One size and accessible label for all three backends of a game.
[<NoEquality; NoComparison>]
type BrowserRenderOptions =
    {
        /// Accessible label of the SVG root and of each canvas. Must not be blank.
        Label: string
        /// Width in pixels. Finite and greater than zero.
        Width: float
        /// Height in pixels. Finite and greater than zero.
        Height: float
    }

/// The browser half of the three-adapter authoring path: every game declares its SVG, Pixi and Babylon renderers
/// the same way, from one `view: 'State -> Scene`.
///
/// ```fsharp
/// let route = BrowserRenderers.route ()
/// let renderers = BrowserRenderers.declare container (BrowserRenderers.options "Board" 320.0 180.0) route view
/// let driver = BrowserRenderers.start route (SceneRenderers.registry renderers) initial onChange
/// driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Babylon)
/// ```
[<RequireQualifiedAccess>]
module BrowserRenderers =

    /// A route with no driver bound yet.
    val route: unit -> LateFailureRoute

    /// Options for all three backends.
    val options: label: string -> width: float -> height: float -> BrowserRenderOptions

    /// The SVG, Pixi and Babylon surfaces in `container`, in that order, each at `options`' size with the backends'
    /// default settings. Each Pixi mount reports a late initialisation failure to the driver `route` is bound to when
    /// that mount is created.
    val surfaces: container: HTMLElement -> options: BrowserRenderOptions -> route: LateFailureRoute -> SceneSurface list

    /// Declare all three backends for a game-state type: `SceneRenderers.create view` over `surfaces`. Add
    /// `SceneRenderers.withView` for a backend that needs its own representation.
    val declare:
        container: HTMLElement ->
        options: BrowserRenderOptions ->
        route: LateFailureRoute ->
        view: ('State -> Scene) ->
            SceneRenderers<'State>

    /// `RendererDriver.start registry state onChange`, then bind `route` to that driver for mounts created from now on.
    /// Mounts of a driver bound earlier keep reporting to that driver, which ignores reports once it is disposed.
    val start:
        route: LateFailureRoute ->
        registry: RendererRegistry<'State> ->
        state: 'State ->
        onChange: (RendererModel<'State> -> unit) ->
            RendererDriver<'State>

/// Browser support for running `RendererConformance` on real backends: an independent resource probe that counts what
/// the page holds (SVG roots, canvases, Babylon engines, Pixi applications and live WebGL contexts), frame waits and
/// pixel readback.
[<RequireQualifiedAccess>]
module BrowserConformance =

    /// Start counting the WebGL contexts the page creates, by wrapping `HTMLCanvasElement.prototype.getContext` once.
    /// Call it before any canvas adapter mounts; it is idempotent.
    val installContextProbe: unit -> unit

    /// The WebGL contexts created on `backend`'s adapter canvases (`data-renderer`) that are not lost, whether or not
    /// the canvas is still in the page. Raises when `installContextProbe` was not called, so the probe never reads
    /// zero for want of a subject.
    val liveContexts: backend: RendererBackend -> int

    /// What `backend` holds in the page now: SVG roots in `container` for SVG; for Pixi and Babylon, their canvases in
    /// `container`, live WebGL contexts and live applications (Pixi's own report) or engines (Babylon's engine store).
    val resources: container: HTMLElement -> backend: RendererBackend -> int

    /// The `(width, height)` of the SVG root in `container`, read from its attributes, when there is exactly one: the
    /// viewport probe for `RendererConformance.withViewport` on the SVG backend.
    val svgViewport: container: HTMLElement -> (float * float) option

    /// Wait animation frames until `condition` holds, at most `maxFrames`; whether it held.
    val waitUntil: maxFrames: int -> condition: (unit -> bool) -> Async<bool>

    /// The kit's view of a running driver.
    val host: driver: RendererDriver<'State> -> ConformanceHost<'State>

    /// Pixi's late-failure trigger for the kit: `arm` refuses every WebGL context of the next Pixi adapter canvas, so
    /// that mount returns `Ok`, its asynchronous initialisation fails, and the adapter reports through its own
    /// `PixiOptions.OnFailure` (or, once disposed, does not); `settle` waits until Pixi counts the failure.
    val pixiLateFailure: unit -> LateFailureTrigger

    /// The RGBA of one pixel of `backend`'s canvas in `container`, read after presenting the host's current state
    /// again, so the frame read is the one that state rendered. Repeats frame by frame until `accept` holds, at most
    /// `maxFrames`; returns the last pixel read, or an empty array when the backend has no canvas there.
    val readPixel:
        host: ConformanceHost<'State> ->
        container: HTMLElement ->
        backend: RendererBackend ->
        x: int ->
        y: int ->
        accept: (int[] -> bool) ->
        maxFrames: int ->
            Async<int[]>

    /// A subject for real browser backends in `container`: hosts are started with `BrowserRenderers.start route`,
    /// resources are `resources container`, waits last up to 600 frames, and, when the registry offers Pixi, Pixi's
    /// adapter late failure is `pixiLateFailure ()`.
    val subject:
        container: HTMLElement ->
        route: LateFailureRoute ->
        registry: RendererRegistry<'State> ->
        expected: RendererBackend list ->
        states: 'State list ->
        shows: (ConformanceHost<'State> -> RendererBackend -> 'State -> Async<Result<unit, string>>) ->
            ConformanceSubject<'State>

namespace FS.GG.Game.Render.Browser

open Browser.Types
open FS.GG.Game.Render
open FS.GG.UI.Scene

/// Options for a Babylon canvas: pixel size, accessible label and antialiasing.
[<NoEquality; NoComparison>]
type BabylonOptions =
    {
        /// Accessible label of the canvas. Must not be blank.
        Label: string
        /// Canvas width in pixels. Finite and greater than zero.
        Width: float
        /// Canvas height in pixels. Finite and greater than zero.
        Height: float
        /// Request multisample antialiasing from the WebGL context.
        Antialias: bool
    }

/// The lifetime totals of the Babylon adapter in this page: what it acquired and what it released. These are the
/// adapter's own books; the browser smoke checks them against Babylon's engine store and the DOM, which are
/// independent of them.
[<NoEquality; NoComparison>]
type BabylonCounters =
    {
        /// WebGL engines created.
        EnginesCreated: int
        /// WebGL engines disposed.
        EnginesDisposed: int
        /// Render loops started.
        LoopsStarted: int
        /// Render loops stopped.
        LoopsStopped: int
        /// Meshes created.
        MeshesCreated: int
        /// Meshes disposed.
        MeshesDisposed: int
        /// Scene nodes the Babylon projection has no 3D form for, summed over every presented scene.
        UnsupportedNodes: int
    }

/// The BabylonJS renderer adapter for the renderer-selection core, over the packed Babylon bindings
/// (`FS.GG.Game.Render.BabylonBindings`, built from FS.GG.Templates#809 by `scripts/build-babylon-bindings-feed.sh`).
///
/// It projects the SAME `FS.GG.UI.Scene` the SVG adapter draws, so one `view: 'State -> Scene` can feed both:
/// `Rectangle`, `FilledEllipse` and `Circle` become flat meshes in the plane of the scene (later nodes nearer
/// the camera), `Group`, `Translate` and `Empty` are structural, and every other node is counted in
/// `BabylonCounters.UnsupportedNodes` and not drawn. A mount owns exactly one canvas, one engine, one scene and one
/// render loop; `Present` reconciles meshes in place (no mesh is created for a node that already has one), and
/// `Dispose` stops the loop, disposes the meshes, scene and engine and removes the canvas.
[<RequireQualifiedAccess>]
module BabylonSurface =

    /// Options with antialiasing on.
    val defaultOptions: label: string -> width: float -> height: float -> BabylonOptions

    /// The Babylon mount for a `Scene`. Each call appends one canvas to `container`, creates a WebGL engine on it
    /// and renders `scene` once, synchronously, before starting the render loop. Refusals are returned as `Error`,
    /// never thrown, and release whatever was acquired before the refusal: invalid options, no WebGL context, or an
    /// engine that cannot start. `Present` renders the new scene synchronously; `Dispose` releases everything
    /// even when one release step throws (the first failure is rethrown after the rest have run).
    val mountScene:
        container: HTMLElement -> options: BabylonOptions -> scene: Scene -> Result<RendererMount<Scene>, string>

    /// The Babylon adapter for a game-state type: every state is projected through `view` first.
    val adapter: container: HTMLElement -> options: BabylonOptions -> view: ('State -> Scene) -> RendererAdapter<'State>

    /// `RendererRegistry.register (adapter ...)`: makes `RendererBackend.Babylon` selectable for this state type.
    val register:
        container: HTMLElement ->
        options: BabylonOptions ->
        view: ('State -> Scene) ->
        registry: RendererRegistry<'State> ->
            RendererRegistry<'State>

    /// The lifetime totals so far.
    val counters: unit -> BabylonCounters

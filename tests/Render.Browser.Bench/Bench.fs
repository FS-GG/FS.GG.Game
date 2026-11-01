module FS.GG.Game.Render.Bench.Runner

// GAME-RENDER-ADOPT-01.2 (FS.GG.Game#746): the in-page runner. One page load measures ONE game, scene, size and
// backend; tools/render-bench/driver.mjs loads a fresh browser for every run and reads the window through
// `window.__fsggBench` (tests/Render.Browser.Bench/instrument.js).
//
// The route is the one games ship on Render.Browser 0.18.0 (FS.GG.Game#745, #751, #758): `BrowserRenderers.declare`
// declares the game's view on the SVG, Pixi and Babylon surfaces, `SceneRenderers.withInteraction` (interactive mode)
// makes every backend mount through its `mountInteractive`, `BrowserRenderers.start` starts the `RendererDriver` on SVG
// and binds the late-failure route, `RendererMsg.SelectBackend` switches to the backend under test, and every frame the
// scripted state is presented through `RendererMsg.StateChanged`. The plain mode omits `withInteraction`, so each backend
// mounts the plain `mountScene` without an input host. The runner refuses (the page reports `error`) when the backend is
// not the one mounted and active, when the scene does not hold its declared node count, or when a canvas backend never
// draws.

open Browser.Dom
open Browser.Types
open Fable.Core
open FS.GG.Game.Render
open FS.GG.Game.Render.Browser
open FS.GG.UI.Scene

/// One run's parameters, read from the page URL.
type BenchConfig =
    {
        Backend: RendererBackend
        SceneId: string
        Size: int
        WarmupFrames: int
        /// Declare the game's `SceneInteraction` (input hosts, hit regions, accessible mirror) and report its scripted
        /// selections through the game's `SelectionRoute`.
        Interactive: bool
    }

[<Emit("window.__fsggBench.phase = $0")>]
let private setPhase (phase: string) : unit = jsNative

[<Emit("window.__fsggBench.error = $0; window.__fsggBench.phase = 'error'")>]
let private setError (message: string) : unit = jsNative

[<Emit("window.__fsggBench.info[$0] = $1")>]
let private setInfo (key: string) (value: 'T) : unit = jsNative

[<Emit("window.__fsggBench.frameStart($0)")>]
let private frameStart (timestamp: float) : unit = jsNative

[<Emit("window.__fsggBench.frameWork($0)")>]
let private frameWork (ms: float) : unit = jsNative

[<Emit("window.__fsggBench.stop === true")>]
let private stopRequested () : bool = jsNative

[<Emit("window.__fsggBench.counters().drawCalls")>]
let private drawCalls () : float = jsNative

[<Emit("performance.now()")>]
let private now () : float = jsNative

module private Probe =
    [<Import("EngineStore", "@babylonjs/core/Engines/engineStore.js");
      Emit("$0.Instances.reduce((n, e) => n + e.scenes.reduce((m, s) => m + s.meshes.filter(x => x.fsggSelectable === undefined).length, 0), 0)")>]
    let babylonMeshes () : int = jsNative

    [<Import("EngineStore", "@babylonjs/core/Engines/engineStore.js"); Emit("$0.Instances.length")>]
    let babylonEngines () : int = jsNative

    /// The meshes Babylon selected for drawing in its last render (frustum-culled). Fewer than the scene's nodes
    /// means part of the scene is not drawn, which a frame time alone would read as "faster".
    [<Import("EngineStore", "@babylonjs/core/Engines/engineStore.js");
      Emit("$0.Instances.reduce((n, e) => n + e.scenes.reduce((m, s) => m + s.getActiveMeshes().length, 0), 0)")>]
    let babylonActiveMeshes () : int = jsNative

let private count (container: HTMLElement) (selector: string) = int (container.querySelectorAll selector).length

/// What the mounted backend holds for the scene: one SVG element, Pixi display object or Babylon mesh per drawable node.
/// In the interactive mode the SVG surface also holds one transparent hit element per selectable object, which is not
/// a drawable node of the scene: `hitElements` are subtracted. (Pixi's hit regions are off the stage and Babylon's are
/// invisible meshes excluded by the probe.)
let private backendNodes (container: HTMLElement) (backend: RendererBackend) (hitElements: int) =
    match backend with
    | RendererBackend.Svg -> count container "svg rect, svg circle, svg ellipse" - hitElements
    | RendererBackend.Pixi -> (PixiSurface.live ()).DisplayObjects
    | RendererBackend.Babylon -> Probe.babylonMeshes ()

let run (game: BenchGame<'State>) (config: BenchConfig) : unit =
    try
        let scene =
            match game.Scenes |> List.tryFind (fun s -> s.Id = config.SceneId) with
            | Some scene -> scene
            | None -> failwithf "game %s has no scene %s" game.Id config.SceneId

        let width = game.Width
        let height = game.Height
        let label = game.Id + "/" + scene.Id
        let view = scene.View config.Size

        let container = document.createElement "div"
        container.id <- "stage"
        document.body.appendChild container |> ignore

        // The renderers exactly as a game declares them (Render.Browser 0.18.0): one view, three surfaces, and in the
        // interactive mode the game's own interaction. Pixi's late failures reach the driver through the route.
        let route = BrowserRenderers.route ()
        let options = BrowserRenderers.options label width height
        let selection = SelectionRoute.create ()
        let declared = BrowserRenderers.declare container options route view

        let renderers =
            if config.Interactive then
                declared |> SceneRenderers.withInteraction game.Interaction selection
            else
                declared

        let pixiOptions = PixiSurface.defaultOptions label width height
        let babylonOptions = BabylonSurface.defaultOptions label width height

        let state = ref (game.Init())
        let drawable = BenchScene.drawableNodes (view state.Value)

        if drawable <> config.Size then
            failwithf "scene %s at size %d draws %d nodes, not %d" scene.Id config.Size drawable config.Size

        // Interactive: the hit regions of the selectable objects. SVG draws them as transparent elements; Pixi keeps
        // them off the stage and Babylon makes them invisible meshes, so only SVG's element count includes them.
        let hitRegions =
            if config.Interactive then
                (SceneInteraction.objects game.Interaction state.Value).Length
            else
                0

        setInfo "game" (box game.Id)
        setInfo "stateType" (box game.StateType)
        setInfo "scene" (box scene.Id)
        setInfo "sceneDescription" (box scene.Description)
        setInfo "size" (box config.Size)
        setInfo "interactive" (box config.Interactive)
        setInfo "selectableObjects" (box hitRegions)
        setInfo "canvas" (box [| width; height |])
        setInfo "pixiAntialias" (box pixiOptions.Antialias)
        setInfo "babylonAntialias" (box babylonOptions.Antialias)
        setPhase "mounting"

        let driver = BrowserRenderers.start route (SceneRenderers.registry renderers) state.Value ignore

        if config.Interactive then
            // What the game does with an input host's request: its own rule, then present the new state. The scripted
            // frame reports the request through the route, which is the path a real press, key or mirror button takes.
            selection
            |> SelectionRoute.bind (fun request ->
                state.Value <- SceneInteraction.apply game.Interaction request state.Value
                driver.Dispatch(RendererMsg.StateChanged state.Value))


        if config.Backend <> RendererBackend.Svg then
            driver.Dispatch(RendererMsg.SelectBackend config.Backend)

        let selected () =
            driver.Model.Selected = config.Backend
            && driver.Mounted = Some config.Backend
            && driver.Model.Status = RendererStatus.Active

        if not (selected ()) then
            failwithf "backend %s did not mount: %A %A" (RendererBackend.id config.Backend) driver.Model.Status driver.Model.Refusal

        // Mounted is not drawing: Pixi initialises asynchronously. A canvas backend is drawing once a frame issues
        // draw calls and it holds one object per drawable node; SVG once its root holds one element per node.
        let mutable frame = 0
        let mutable drawingSince = -1
        let mutable lastDraws = drawCalls ()
        let mountDeadline = 600

        let rec loop (timestamp: float) =
            if not (stopRequested ()) then
                try
                    frameStart timestamp
                    let started = now ()
                    state.Value <- game.Step frame state.Value
                    driver.Dispatch(RendererMsg.StateChanged state.Value)

                    if config.Interactive then
                        game.Selections frame
                        |> Option.iter (fun request -> SelectionRoute.report request selection)

                    frameWork (now () - started)

                    if not (selected ()) then
                        failwithf "backend %s is no longer the active selection: %A %A" (RendererBackend.id config.Backend) driver.Model.Status driver.Model.Refusal

                    let draws = drawCalls ()
                    let issued = draws > lastDraws
                    lastDraws <- draws

                    if drawingSince < 0 then
                        let nodes = backendNodes container config.Backend hitRegions

                        let drawing =
                            nodes = config.Size
                            && (config.Backend = RendererBackend.Svg || issued)

                        if drawing then
                            drawingSince <- frame
                            setInfo "framesToFirstDraw" (box frame)
                            setInfo "backendNodes" (box nodes)
                            setPhase "warmup"
                        elif frame >= mountDeadline then
                            failwithf
                                "backend %s was not drawing %d nodes after %d frames (holds %d)"
                                (RendererBackend.id config.Backend)
                                config.Size
                                frame
                                nodes
                    elif frame = drawingSince + config.WarmupFrames then
                        setInfo "babylonEngines" (box (Probe.babylonEngines ()))
                        setInfo "babylonActiveMeshes" (box (Probe.babylonActiveMeshes ()))
                        setInfo "pixiApplications" (box (PixiSurface.live ()).Applications)
                        // The route is the one that was asked for: the interactive mounts attach an accessible mirror with one
                        // button per selectable object; the plain canvas mounts attach no mirror and the plain SVG mount's
                        // retained mirror holds no selectable button. A run on the other route would label its numbers with
                        // a route it did not measure.
                        let mirrors = count container "[data-scene-controls]"
                        let buttons = count container "[data-scene-controls] button"
                        setInfo "inputMirrors" (box mirrors)
                        setInfo "inputMirrorButtons" (box buttons)

                        if config.Interactive && (mirrors <> 1 || buttons <> hitRegions) then
                            failwithf
                                "interactive run on %s holds %d accessible mirrors with %d buttons, expected 1 with %d"
                                (RendererBackend.id config.Backend)
                                mirrors
                                buttons
                                hitRegions

                        if not config.Interactive && buttons <> 0 then
                            failwithf "plain run on %s holds %d selectable buttons, expected none" (RendererBackend.id config.Backend) buttons

                        setInfo "svgRoots" (box (count container "svg"))
                        setInfo "canvases" (box (count container "canvas"))
                        setPhase "ready"

                    frame <- frame + 1
                    window.requestAnimationFrame loop |> ignore
                with ex ->
                    setError ex.Message
            else
                SelectionRoute.unbind selection
                (driver :> System.IDisposable).Dispose()
                setPhase "stopped"

        window.requestAnimationFrame loop |> ignore
    with ex ->
        setError ex.Message

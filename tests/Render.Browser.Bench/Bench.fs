module FS.GG.Game.Render.Bench.Runner

// GAME-RENDER-ADOPT-01.2 (FS.GG.Game#746): the in-page runner. One page load measures ONE game, scene, size and
// backend; tools/render-bench/driver.mjs loads a fresh browser for every run and reads the window through
// `window.__fsggBench` (tests/Render.Browser.Bench/instrument.js).
//
// The route is the production one: the game's view is registered with the SVG, Pixi and Babylon adapters through
// `RendererRegistry`, `RendererDriver.start` mounts SVG, `RendererMsg.SelectBackend` switches to the backend under
// test, and every frame the scripted state is presented through `RendererMsg.StateChanged`. The runner refuses (the
// page reports `error`) when the backend is not the one mounted and active, when the scene does not hold its
// declared node count, or when a canvas backend never draws.

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
      Emit("$0.Instances.reduce((n, e) => n + e.scenes.reduce((m, s) => m + s.meshes.length, 0), 0)")>]
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
let private backendNodes (container: HTMLElement) (backend: RendererBackend) =
    match backend with
    | RendererBackend.Svg -> count container "svg rect, svg circle, svg ellipse"
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

        let driverRef: RendererDriver<'State> option ref = ref None

        let pixiOptions =
            { PixiSurface.defaultOptions label width height with
                OnFailure =
                    fun reason ->
                        match driverRef.Value with
                        | Some d -> RendererDriver.failBackend d RendererBackend.Pixi reason
                        | None -> ()
            }

        let babylonOptions = BabylonSurface.defaultOptions label width height

        let registry =
            SvgSurface.registry container (SvgSurface.defaultOptions label width height) view
            |> PixiSurface.register container pixiOptions view
            |> BabylonSurface.register container babylonOptions view

        let mutable state = game.Init()
        let declared = BenchScene.drawableNodes (view state)

        if declared <> config.Size then
            failwithf "scene %s at size %d draws %d nodes, not %d" scene.Id config.Size declared config.Size

        setInfo "game" (box game.Id)
        setInfo "stateType" (box game.StateType)
        setInfo "scene" (box scene.Id)
        setInfo "sceneDescription" (box scene.Description)
        setInfo "size" (box config.Size)
        setInfo "canvas" (box [| width; height |])
        setInfo "pixiAntialias" (box pixiOptions.Antialias)
        setInfo "babylonAntialias" (box babylonOptions.Antialias)
        setPhase "mounting"

        let driver = RendererDriver.start registry state ignore
        driverRef.Value <- Some driver

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
                    state <- game.Step frame state
                    driver.Dispatch(RendererMsg.StateChanged state)
                    frameWork (now () - started)

                    if not (selected ()) then
                        failwithf "backend %s is no longer the active selection: %A %A" (RendererBackend.id config.Backend) driver.Model.Status driver.Model.Refusal

                    let draws = drawCalls ()
                    let issued = draws > lastDraws
                    lastDraws <- draws

                    if drawingSince < 0 then
                        let nodes = backendNodes container config.Backend

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
                        setInfo "svgRoots" (box (count container "svg"))
                        setInfo "canvases" (box (count container "canvas"))
                        setPhase "ready"

                    frame <- frame + 1
                    window.requestAnimationFrame loop |> ignore
                with ex ->
                    setError ex.Message
            else
                (driver :> System.IDisposable).Dispose()
                setPhase "stopped"

        window.requestAnimationFrame loop |> ignore
    with ex ->
        setError ex.Message

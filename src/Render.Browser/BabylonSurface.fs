namespace FS.GG.Game.Render.Browser

open Browser.Dom
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open FS.GG.Game.Render
open FS.GG.UI.Scene
open FS.GG.Game.Render.Browser.SceneShapes

[<NoEquality; NoComparison>]
type BabylonOptions =
    {
        Label: string
        Width: float
        Height: float
        Antialias: bool
    }

[<NoEquality; NoComparison>]
type BabylonCounters =
    {
        EnginesCreated: int
        EnginesDisposed: int
        LoopsStarted: int
        LoopsStopped: int
        MeshesCreated: int
        MeshesDisposed: int
        UnsupportedNodes: int
    }

[<RequireQualifiedAccess>]
module BabylonSurface =

    module B = Qualification.Babylon

    // ── Lifetime totals ──────────────────────────────────────────────────────────────────────
    let mutable private enginesCreated = 0
    let mutable private enginesDisposed = 0
    let mutable private loopsStarted = 0
    let mutable private loopsStopped = 0
    let mutable private meshesCreated = 0
    let mutable private meshesDisposed = 0
    let mutable private unsupportedNodes = 0

    let counters () : BabylonCounters =
        {
            EnginesCreated = enginesCreated
            EnginesDisposed = enginesDisposed
            LoopsStarted = loopsStarted
            LoopsStopped = loopsStopped
            MeshesCreated = meshesCreated
            MeshesDisposed = meshesDisposed
            UnsupportedNodes = unsupportedNodes
        }

    let defaultOptions (label: string) (width: float) (height: float) : BabylonOptions =
        {
            Label = label
            Width = width
            Height = height
            Antialias = true
        }

    // `Scene.pick` is registered by the ray module's side effect (FS.GG.Game#751).
    importSideEffects "@babylonjs/core/Culling/ray.js"

    /// Make `mesh` an invisible hit region of the object `id`: never drawn, picked only by `pickRegion`.
    [<Emit("(($0.isVisible = false), ($0.fsggSelectable = $1))")>]
    let private markRegion (_mesh: B.Mesh) (_id: string) : unit = jsNative

    /// Babylon's own pick, through the camera, restricted to hit regions: the id of the nearest one under the scene point
    /// `(x, y)` (board units). `scene.pick` takes render pixels, and the engine sizes its backing store to the canvas's
    /// CSS size, so a canvas styled to another size than the board has another render size: the point is scaled by
    /// the live render size over the board size (FS.GG.Game#753).
    // `Scene.pick` multiplies its point by 1 / the hardware scaling level, so the point is given in render pixels times
    // that level (FS.GG.Game#788: an input-host mount scales for the device pixel ratio).
    [<Emit("(() => { const k = $3.getHardwareScalingLevel(); const p = $0.pick($1 * $3.getRenderWidth() * k / $4, $2 * $3.getRenderHeight() * k / $5, m => m.fsggSelectable !== undefined); return p && p.hit && p.pickedMesh ? p.pickedMesh.fsggSelectable : null })()")>]
    let private pickRegion
        (_scene: B.Scene)
        (_x: float)
        (_y: float)
        (_engine: B.Engine)
        (_boardWidth: float)
        (_boardHeight: float)
        : string option =
        jsNative

    /// Babylon's input manager sets the canvas's tabIndex to `engine.canvasTabIndex` (1 by default) on every pointer
    /// move, which would put the canvas ahead of the page in tab order (FS.GG.Game#751 review B2). Zero keeps it in
    /// document order, like the SVG root and the Pixi canvas.
    [<Emit("$0.canvasTabIndex = 0")>]
    let private keepDocumentTabOrder (_engine: B.Engine) : unit = jsNative

    /// FS.GG.Game#788: render an input-host canvas at the device pixel ratio (a hardware scaling level of 1 / DPR) and
    /// at its presented CSS size, so the cue layer's 2 CSS px bands are drawn sharp; whether it changed anything.
    [<Emit("(() => { const want = 1 / (window.devicePixelRatio || 1); const w = $0.getRenderWidth(); const h = $0.getRenderHeight(); if (Math.abs($0.getHardwareScalingLevel() - want) > 0.001) $0.setHardwareScalingLevel(want); $0.resize(); return $0.getRenderWidth() !== w || $0.getRenderHeight() !== h })()")>]
    let private adaptDensity (_engine: B.Engine) : bool = jsNative

    /// Call `action` whenever `element`'s presented size changes; returns the function that stops observing.
    [<Emit("(() => { const o = new ResizeObserver(() => $1()); o.observe($0); return () => o.disconnect() })()")>]
    let private observeSize (_element: HTMLElement) (_action: unit -> unit) : (unit -> unit) = jsNative

    let private channel (value: byte) = float value / 255.0

    /// Thickness of a flat shape, and the spacing between consecutive layers, in scene units.
    let private thickness = 0.25
    let private layerStep = 0.5

    /// Space between the camera and the frontmost layer, in scene units.
    let private clearance = 10.0

    /// FS.GG.Game#753: an orthographic camera shows the plane z = 0 as exactly the canvas (one scene unit per pixel)
    /// and every layer at its own size, wherever it sits in depth. A perspective camera magnified later layers about
    /// the centre of the canvas and put those beyond about 400 nodes behind it.
    [<Emit("(($0.mode = 1), ($0.orthoLeft = -$1 / 2), ($0.orthoRight = $1 / 2), ($0.orthoTop = $2 / 2), ($0.orthoBottom = -$2 / 2))")>]
    let private useOrthographic (_camera: B.Camera) (_width: float) (_height: float) : unit = jsNative

    /// Back the camera off so that all `layers` layers (the frontmost at z = -(layers - 1) * layerStep) are in front of
    /// it and inside its depth range: the stack can be as deep as the scene has nodes.
    [<Emit("(($0.position.z = -($1 * $2 + $3)), ($0.minZ = 1), ($0.maxZ = $1 * $2 + $3 + 10))")>]
    let private backOffCamera (_camera: B.Camera) (_layers: float) (_step: float) (_clearance: float) : unit = jsNative

    /// Render the scene now, and leave the render loop to draw again only while a material is still compiling. Readiness
    /// (it visits every mesh) is asked BEFORE the render, so a frame that skipped a mesh still compiling is never taken
    /// for a complete one, and only after meshes were created or while the loop is still waiting.
    [<Emit("(() => { const ready = ($1 || $0.fsggPending) ? $0.isReady() : true; $0.render(); $0.fsggPending = !ready })()")>]
    let private renderNow (_scene: B.Scene) (_created: bool) : unit = jsNative

    /// A render loop that draws only what `renderNow` has not finished: the scene is drawn when the state is presented,
    /// so a loop that drew it again every frame doubled the work (FS.GG.Game#753). It also draws again after a lost
    /// WebGL context is restored.
    [<Emit("(($1.fsggPending = true), $0.onContextRestoredObservable.add(() => { $1.fsggPending = true }), $0.runRenderLoop(() => { if ($1.fsggPending) { const ready = $1.isReady(); $1.render(); $1.fsggPending = !ready } }))")>]
    let private runPendingRenderLoop (_engine: B.Engine) (_scene: B.Scene) : unit = jsNative

    /// A mesh sharing `source`'s geometry (and so its vertex buffers), visible and with no material of its own.
    [<Emit("(() => { const m = $0.clone($1); m.isVisible = true; return m })()")>]
    let private shareGeometry (_source: B.Mesh) (_name: string) : B.Mesh = jsNative

    /// No specular highlight. Babylon's shader takes the view vector from the camera's POSITION even when the camera is
    /// orthographic, so a camera as close as the layers need to be would put a highlight on every flat face (a bright
    /// spot at the centre of the canvas). A flat 2D scene is lit diffusely only.
    [<Emit("$0.specularColor.copyFromFloats(0, 0, 0)")>]
    let private noSpecular (_material: B.StandardMaterial) : unit = jsNative

    /// FS.GG.Game#786/#788: an unlit material has no light term at all (`disableLighting`), so its colour is its
    /// EMISSIVE colour, with a black diffuse; a lit one is diffuse with a black emissive. The cue layer is unlit so it
    /// keeps its declared tones and contrast on Babylon; the scene's own shapes stay lit.
    [<Emit("(($0.disableLighting = $1), ($0.emissiveColor = $1 ? $2 : $3), ($0.diffuseColor = $1 ? $3 : $2))")>]
    let private paint (_material: B.StandardMaterial) (_unlit: bool) (_colour: B.Color3) (_black: B.Color3) : unit = jsNative

    /// Take `mesh` out of the scene's mesh list: it is never drawn or picked, and keeps only the geometry its clones share.
    [<Emit("$0.getScene().removeMesh($0)")>]
    let private takeOutOfScene (_mesh: B.Mesh) : unit = jsNative

    /// One live mesh and the material that colours it. A slot outlives its mesh only as pooled material.
    type private Slot =
        {
            Kind: Kind
            Mesh: B.Mesh
            Material: B.StandardMaterial
            mutable Fill: Color
            /// Whether the material is unlit (a cue-layer shape).
            mutable Unlit: bool
            /// What the mesh was last placed and sized for; `None` until it is placed.
            mutable Placed: Shape option
        }

    /// The mount behind `mountScene`, `mountInteractive` and `mountInput`: with a `host`, the mount also holds an input
    /// host whose hit test is Babylon's own pick over one invisible mesh per selectable object.
    let private mountWith
        (container: HTMLElement)
        (options: BabylonOptions)
        (host: InputHost.CanvasHost option)
        (first: InteractiveScene)
        : Result<RendererMount<InteractiveScene>, string> =
        let scene = first.Scene

        if System.String.IsNullOrWhiteSpace options.Label then
            Error "Babylon surface refused the scene: the accessible label is blank"
        elif
            not (
                CanvasSupport.isFinitePositive options.Width
                && CanvasSupport.isFinitePositive options.Height
            )
        then
            Error(sprintf "Babylon surface refused the scene: invalid size %Ax%A" options.Width options.Height)
        elif not (CanvasSupport.webGlAvailable ()) then
            Error "Babylon surface refused the scene: the browser has no WebGL context"
        else
            // Everything acquired so far, most recent first. Mount failure and Dispose both run it.
            let releases: (unit -> unit) list ref = ref []

            let acquire (release: unit -> unit) =
                releases.Value <- release :: releases.Value

            let releaseAll () =
                let mutable first: exn option = None
                let pending = releases.Value
                releases.Value <- []

                for release in pending do
                    try
                        release ()
                    with ex ->
                        if first.IsNone then
                            first <- Some ex

                match first with
                | Some ex -> raise ex
                | None -> ()

            try
                let canvas = document.createElement "canvas" :?> HTMLCanvasElement
                canvas.width <- options.Width
                canvas.height <- options.Height
                canvas.setAttribute ("data-renderer", "babylon")
                canvas.setAttribute ("role", "img")
                canvas.setAttribute ("aria-label", options.Label)
                container.appendChild canvas |> ignore
                acquire (fun () -> container.removeChild canvas |> ignore)

                // `webGlEngine` throws when the browser has no WebGL context.
                let engine = B.webGlEngine (unbox<B.Canvas>(box canvas)) options.Antialias
                keepDocumentTabOrder engine
                enginesCreated <- enginesCreated + 1

                acquire (fun () ->
                    try
                        B.disposeEngine engine
                        enginesDisposed <- enginesDisposed + 1
                    finally
                        CanvasSupport.loseContext canvas)

                let babylon = B.scene engine
                acquire (fun () -> B.disposeScene babylon)
                B.setClearColor babylon (B.color3 0.0 0.0 0.0)

                // The camera looks down +z at the plane z = 0 and is orthographic, so that plane is exactly the canvas
                // and layer i (at z = -i * layerStep, nearer the camera) is the size and place it was given; the
                // light is tilted so a face toward the camera is diffusely lit and has no highlight.
                let camera = B.freeCamera "camera" (B.vector3 0.0 0.0 (-clearance)) babylon
                B.setCameraTarget camera (B.vector3 0.0 0.0 0.0)
                useOrthographic camera options.Width options.Height
                backOffCamera camera 1.0 layerStep clearance
                let light = B.hemisphericLight "light" (B.vector3 0.0 0.866 -0.5) babylon
                B.setLightIntensity light 1.0

                let slots = ResizeArray<Slot>()
                // Materials of meshes that were disposed because their node went away: the bindings have no
                // material disposal, so they are reused instead of accumulating in the scene.
                let pool = System.Collections.Generic.Stack<B.StandardMaterial>()
                let serial = ref 0

                let release (slot: Slot) =
                    B.disposeMesh slot.Mesh
                    meshesDisposed <- meshesDisposed + 1
                    pool.Push slot.Material

                acquire (fun () ->
                    for slot in slots do
                        B.disposeMesh slot.Mesh
                        meshesDisposed <- meshesDisposed + 1

                    slots.Clear())

                // One sphere's geometry shared by every round shape and round hit region (FS.GG.Game#753): a mesh per
                // circle used to build its own 24 x 24 vertex data.
                let sphereSource: B.Mesh option ref = ref None

                acquire (fun () ->
                    match sphereSource.Value with
                    | Some source ->
                        // Not counted: the source is never one of the scene's meshes.
                        B.disposeMesh source
                        sphereSource.Value <- None
                    | None -> ())

                let sphere (name: string) : B.Mesh =
                    let source =
                        match sphereSource.Value with
                        | Some source -> source
                        | None ->
                            let source = B.sphere "sphere-source" 1.0 24 babylon
                            takeOutOfScene source
                            sphereSource.Value <- Some source
                            source

                    shareGeometry source name

                let colour (fill: Color) =
                    B.color3 (channel fill.Red) (channel fill.Green) (channel fill.Blue)

                // Whether the last reconcile created a mesh (its material may still be compiling).
                let created = ref false
                // How many layers the camera is backed off for.
                let depth = ref -1

                let create (index: int) (shape: Shape) (unlit: bool) : Slot =
                    created.Value <- true
                    serial.Value <- serial.Value + 1
                    let name = sprintf "shape-%d" serial.Value

                    let mesh =
                        match shape.Kind with
                        | Flat -> B.box name 1.0 babylon
                        | Round -> sphere name

                    meshesCreated <- meshesCreated + 1

                    let material =
                        if pool.Count > 0 then
                            pool.Pop()
                        else
                            let fresh = B.standardMaterial (sprintf "material-%d" serial.Value) babylon
                            noSpecular fresh
                            fresh

                    paint material unlit (colour shape.Fill) (B.color3 0.0 0.0 0.0)
                    B.setMaterial mesh material

                    {
                        Kind = shape.Kind
                        Mesh = mesh
                        Material = material
                        Fill = shape.Fill
                        Unlit = unlit
                        Placed = None
                    }

                let sameBox (a: Shape) (b: Shape) =
                    a.X = b.X && a.Y = b.Y && a.Width = b.Width && a.Height = b.Height

                let place (index: int) (slot: Slot) (shape: Shape) (unlit: bool) =
                    if slot.Unlit <> unlit || slot.Fill <> shape.Fill then
                        paint slot.Material unlit (colour shape.Fill) (B.color3 0.0 0.0 0.0)
                        slot.Unlit <- unlit
                        slot.Fill <- shape.Fill

                    // A shape that is where and what size it was needs no new vectors and does not dirty its mesh
                    // (the same scene presented again costs a render and nothing else).
                    match slot.Placed with
                    | Some placed when sameBox placed shape -> ()
                    | _ ->
                        // Scene space has y down and its origin at the top left; Babylon has y up and the plane centred.
                        let x = shape.X + shape.Width / 2.0 - options.Width / 2.0
                        let y = options.Height / 2.0 - (shape.Y + shape.Height / 2.0)
                        B.setPosition slot.Mesh (B.vector3 x y (-(float index) * layerStep))
                        B.setScaling slot.Mesh (B.vector3 shape.Width shape.Height thickness)
                        slot.Placed <- Some shape



                /// Reconcile the meshes with `next` and then the unlit cue layer `cues` above it: reuse slot i for shape
                /// i, recreate it when the kind changes, create the missing ones and dispose the surplus.
                let reconcileWith (next: Scene) (cues: Scene) =
                    created.Value <- false
                    let shapes = ResizeArray<Shape>()
                    unsupportedNodes <- unsupportedNodes + collect (0.0, 0.0) next shapes
                    let lit = shapes.Count
                    unsupportedNodes <- unsupportedNodes + collect (0.0, 0.0) cues shapes

                    for index in 0 .. shapes.Count - 1 do
                        let shape = shapes.[index]
                        let unlit = index >= lit

                        if index < slots.Count && slots.[index].Kind <> shape.Kind then
                            release slots.[index]
                            slots.[index] <- create index shape unlit
                        elif index >= slots.Count then
                            slots.Add(create index shape unlit)

                        place index slots.[index] shapes.[index] unlit

                    while slots.Count > shapes.Count do
                        release slots.[slots.Count - 1]
                        slots.RemoveAt(slots.Count - 1)

                    // The stack is as deep as the scene has nodes.
                    if shapes.Count <> depth.Value then
                        depth.Value <- shapes.Count
                        backOffCamera camera (float (max shapes.Count 1)) layerStep clearance

                    renderNow babylon created.Value

                let reconcile (next: Scene) = reconcileWith next { Nodes = [] }

                reconcile scene

                // FS.GG.Game#751: one invisible region per selectable object, in the plane of the scene and stacked
                // toward the camera in the objects' order, so the nearest pick is the topmost object.
                let regions = ResizeArray<B.Mesh>()
                let regionsOf: SelectableObject list ref = ref []

                let clearRegions () =
                    for region in regions do
                        B.disposeMesh region
                        meshesDisposed <- meshesDisposed + 1

                    regions.Clear()

                let reregion (objects: SelectableObject list) =
                    if objects <> regionsOf.Value then
                        clearRegions ()

                        objects
                        |> List.iteri (fun index target ->
                            serial.Value <- serial.Value + 1
                            let name = sprintf "region-%d" serial.Value

                            let mesh =
                                match target.Shape with
                                | SelectableShape.Box -> B.box name 1.0 babylon
                                | SelectableShape.Ellipse -> sphere name

                            meshesCreated <- meshesCreated + 1
                            markRegion mesh target.Id
                            let bounds = target.Bounds
                            let x = bounds.X + bounds.Width / 2.0 - options.Width / 2.0
                            let y = options.Height / 2.0 - (bounds.Y + bounds.Height / 2.0)
                            B.setPosition mesh (B.vector3 x y (-(float (index + 1)) * 0.01))
                            B.setScaling mesh (B.vector3 (max bounds.Width 0.001) (max bounds.Height 0.001) 0.001)
                            regions.Add mesh)

                        regionsOf.Value <- objects

                acquire clearRegions

                let input =
                    match host with
                    | None -> None
                    | Some(InputHost.CanvasHost.Selection report) ->
                        reregion first.Objects

                        let attached =
                            CanvasInput.attach
                                container
                                canvas
                                RendererBackend.Babylon
                                options.Label
                                options.Width
                                options.Height
                                (fun point -> pickRegion babylon point.X point.Y engine options.Width options.Height)
                                report
                                first

                        Some((fun frame -> attached.Update frame), (fun () -> attached.Dispose()), false)
                    | Some(InputHost.CanvasHost.Input(spec, report)) ->
                        reregion first.Objects
                        // FS.GG.Game#788: the device pixel ratio and the presented size, now and on every resize.
                        let lastDrawn = ref (first.Scene, ([]: SceneNode list))
                        adaptDensity engine |> ignore

                        acquire (
                            observeSize (unbox<HTMLElement> (box canvas)) (fun () ->
                                let next, cues = lastDrawn.Value

                                if adaptDensity engine then
                                    reconcileWith next { Nodes = cues })
                        )

                        let attached =
                            InputHost.attach
                                canvas
                                InputHost.Ownership.Fallback
                                RendererBackend.Babylon
                                options.Label
                                options.Width
                                options.Height
                                false
                                (fun point -> pickRegion babylon point.X point.Y engine options.Width options.Height)
                                spec
                                report
                                (fun next cues ->
                                    lastDrawn.Value <- (next, cues)
                                    adaptDensity engine |> ignore
                                    reconcileWith next { Nodes = cues })
                                first

                        Some((fun frame -> attached.Update frame), (fun () -> attached.Dispose()), true)

                acquire (fun () -> input |> Option.iter (fun (_, dispose, _) -> dispose ()))

                runPendingRenderLoop engine babylon
                loopsStarted <- loopsStarted + 1

                acquire (fun () ->
                    B.stopRenderLoop engine
                    loopsStopped <- loopsStopped + 1)

                let disposed = ref false

                Ok
                    {
                        Present =
                            fun frame ->
                                if not disposed.Value then
                                    match input with
                                    | Some(update, _, drawsItself) ->
                                        match SceneInteraction.problems frame.Objects with
                                        | [] -> ()
                                        | problems ->
                                            failwithf
                                                "Babylon surface refused the objects: %s"
                                                (String.concat "; " problems)

                                        reregion frame.Objects
                                        update frame

                                        if not drawsItself then
                                            reconcile frame.Scene
                                    | None -> reconcile frame.Scene
                        Dispose =
                            fun () ->
                                if not disposed.Value then
                                    disposed.Value <- true
                                    releaseAll ()
                    }
            with ex ->
                // A throwing mount hands the host nothing to dispose: release what was acquired here.
                try
                    releaseAll ()
                with _ ->
                    ()

                Error(sprintf "Babylon surface could not start: %s" ex.Message)

    let private plain (scene: Scene) : InteractiveScene =
        {
            Scene = scene
            Objects = []
            Selected = None
        }

    let mountScene
        (container: HTMLElement)
        (options: BabylonOptions)
        (scene: Scene)
        : Result<RendererMount<Scene>, string> =
        mountWith container options None (plain scene)
        |> Result.map (fun mount ->
            {
                Present = fun next -> mount.Present(plain next)
                Dispose = mount.Dispose
            })

    let mountInteractive
        (container: HTMLElement)
        (options: BabylonOptions)
        (report: SelectionRequest -> unit)
        (frame: InteractiveScene)
        : Result<RendererMount<InteractiveScene>, string> =
        match SceneInteraction.problems frame.Objects with
        | [] -> mountWith container options (Some(InputHost.CanvasHost.Selection report)) frame
        | problems -> Error(sprintf "Babylon surface refused the objects: %s" (String.concat "; " problems))

    let mountInput
        (container: HTMLElement)
        (options: BabylonOptions)
        (spec: InputHostSpec)
        (report: InputEvent -> unit)
        (frame: InteractiveScene)
        : Result<RendererMount<InteractiveScene>, string> =
        match SceneInteraction.problems frame.Objects with
        | [] -> mountWith container options (Some(InputHost.CanvasHost.Input(spec, report))) frame
        | problems -> Error(sprintf "Babylon surface refused the objects: %s" (String.concat "; " problems))

    let surface (container: HTMLElement) (options: BabylonOptions) : SceneSurface =
        SceneSurface.create RendererBackend.Babylon (mountScene container options)
        |> SceneSurface.withInteraction (mountInteractive container options)
        |> SceneSurface.withInputHost (mountInput container options)

    let adapter (container: HTMLElement) (options: BabylonOptions) (view: 'State -> Scene) : RendererAdapter<'State> =
        SceneSurface.adapter view (surface container options)

    let register
        (container: HTMLElement)
        (options: BabylonOptions)
        (view: 'State -> Scene)
        (registry: RendererRegistry<'State>)
        =
        RendererRegistry.register (adapter container options view) registry

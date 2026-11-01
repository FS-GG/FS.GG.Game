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

    /// Babylon's own pick, through the camera, restricted to hit regions: the id of the nearest one under the canvas
    /// pixel `(x, y)`.
    [<Emit("(() => { const p = $0.pick($1, $2, m => m.fsggSelectable !== undefined); return p && p.hit && p.pickedMesh ? p.pickedMesh.fsggSelectable : null })()")>]
    let private pickRegion (_scene: B.Scene) (_x: float) (_y: float) : string option = jsNative

    let private channel (value: byte) = float value / 255.0

    /// Vertical field of view of Babylon's default camera, in radians.
    let private fieldOfView = 0.8

    /// Thickness of a flat shape, and the spacing between consecutive layers, in scene units.
    let private thickness = 0.25
    let private layerStep = 0.5

    /// One live mesh and the material that colours it. A slot outlives its mesh only as pooled material.
    type private Slot =
        {
            Kind: Kind
            Mesh: B.Mesh
            Material: B.StandardMaterial
            mutable Fill: Color
        }

    /// The mount behind `mountScene` and `mountInteractive`: with `interactive`, the mount also holds an input host
    /// whose hit test is Babylon's own pick over one invisible mesh per selectable object.
    let private mountWith
        (container: HTMLElement)
        (options: BabylonOptions)
        (interactive: (SelectionRequest -> unit) option)
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

                // The camera looks down +z at the plane z = 0 from a distance that makes that plane exactly the
                // canvas; the light is tilted so a face toward the camera is diffusely lit and has no highlight.
                let distance = (options.Height / 2.0) / System.Math.Tan(fieldOfView / 2.0)
                let camera = B.freeCamera "camera" (B.vector3 0.0 0.0 (-distance)) babylon
                B.setCameraTarget camera (B.vector3 0.0 0.0 0.0)
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

                let colour (fill: Color) =
                    B.color3 (channel fill.Red) (channel fill.Green) (channel fill.Blue)

                let create (index: int) (shape: Shape) : Slot =
                    serial.Value <- serial.Value + 1
                    let name = sprintf "shape-%d" serial.Value

                    let mesh =
                        match shape.Kind with
                        | Flat -> B.box name 1.0 babylon
                        | Round -> B.sphere name 1.0 24 babylon

                    meshesCreated <- meshesCreated + 1

                    let material =
                        if pool.Count > 0 then
                            pool.Pop()
                        else
                            B.standardMaterial (sprintf "material-%d" serial.Value) babylon

                    B.setDiffuseColor material (colour shape.Fill)
                    B.setMaterial mesh material

                    {
                        Kind = shape.Kind
                        Mesh = mesh
                        Material = material
                        Fill = shape.Fill
                    }

                let place (index: int) (slot: Slot) (shape: Shape) =
                    // Scene space has y down and its origin at the top left; Babylon has y up and the plane centred.
                    let x = shape.X + shape.Width / 2.0 - options.Width / 2.0
                    let y = options.Height / 2.0 - (shape.Y + shape.Height / 2.0)
                    B.setPosition slot.Mesh (B.vector3 x y (-(float index) * layerStep))
                    B.setScaling slot.Mesh (B.vector3 shape.Width shape.Height thickness)

                    if slot.Fill <> shape.Fill then
                        B.setDiffuseColor slot.Material (colour shape.Fill)
                        slot.Fill <- shape.Fill

                /// Reconcile the meshes with `next`: reuse slot i for shape i, recreate it when the kind changes,
                /// create the missing ones and dispose the surplus.
                let reconcile (next: Scene) =
                    let shapes = ResizeArray<Shape>()
                    unsupportedNodes <- unsupportedNodes + collect (0.0, 0.0) next shapes

                    for index in 0 .. shapes.Count - 1 do
                        let shape = shapes.[index]

                        if index < slots.Count && slots.[index].Kind <> shape.Kind then
                            release slots.[index]
                            slots.[index] <- create index shape
                        elif index >= slots.Count then
                            slots.Add(create index shape)

                        place index slots.[index] shapes.[index]

                    while slots.Count > shapes.Count do
                        release slots.[slots.Count - 1]
                        slots.RemoveAt(slots.Count - 1)

                    B.render babylon

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
                                | SelectableShape.Ellipse -> B.sphere name 1.0 24 babylon

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
                    interactive
                    |> Option.map (fun report ->
                        reregion first.Objects

                        CanvasInput.attach
                            container
                            canvas
                            RendererBackend.Babylon
                            options.Label
                            options.Width
                            options.Height
                            (fun point -> pickRegion babylon point.X point.Y)
                            report
                            first)

                acquire (fun () -> input |> Option.iter (fun host -> host.Dispose()))

                B.runRenderLoop engine babylon
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
                                    | Some host ->
                                        match SceneInteraction.problems frame.Objects with
                                        | [] -> ()
                                        | problems ->
                                            failwithf
                                                "Babylon surface refused the objects: %s"
                                                (String.concat "; " problems)

                                        reregion frame.Objects
                                        host.Update frame
                                    | None -> ()

                                    reconcile frame.Scene
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
        | [] -> mountWith container options (Some report) frame
        | problems -> Error(sprintf "Babylon surface refused the objects: %s" (String.concat "; " problems))

    let surface (container: HTMLElement) (options: BabylonOptions) : SceneSurface =
        SceneSurface.create RendererBackend.Babylon (mountScene container options)
        |> SceneSurface.withInteraction (mountInteractive container options)

    let adapter (container: HTMLElement) (options: BabylonOptions) (view: 'State -> Scene) : RendererAdapter<'State> =
        SceneSurface.adapter view (surface container options)

    let register
        (container: HTMLElement)
        (options: BabylonOptions)
        (view: 'State -> Scene)
        (registry: RendererRegistry<'State>)
        =
        RendererRegistry.register (adapter container options view) registry

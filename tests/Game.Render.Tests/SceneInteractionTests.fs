module Game.Render.Tests.SceneInteractionTests

// FS.GG.Game#751: the canvas interaction contract (`SceneInteraction`, `SelectionRoute`, the interactive surfaces of
// `SceneSurface`/`SceneRenderers`) and the conformance kit's interaction section. The reference game's interaction
// passes the kit on three in-process interactive surfaces whose input hosts hold countable listeners and a mirror;
// each negative control breaks one obligation (no hit test, a leaked input host, no interactive mount, a mirror
// without state, a game rule that ignores requests) and must fail exactly the checks that guard it. The browser run
// of the same section, on the real SVG, Pixi and Babylon input hosts, is tests/Render.Browser.Smoke.

open Expecto
open FS.GG.UI.Scene
open FS.GG.Game.Render
open FS.GG.Game.Reference
open FS.GG.Game.Reference.ReferenceState

let private box id x y w h =
    {
        Id = id
        Label = "Box " + id
        Bounds = { X = x; Y = y; Width = w; Height = h }
        Shape = SelectableShape.Box
    }

let private ellipse id x y w h =
    { box id x y w h with
        Label = "Ellipse " + id
        Shape = SelectableShape.Ellipse
    }

let private frameOf (objects: SelectableObject list) (selected: string option) : InteractiveScene =
    {
        Scene = { Nodes = [] }
        Objects = objects
        Selected = selected
    }

let private at x y : Point = { X = x; Y = y }

/// The negative controls of one in-process interactive backend.
type private Breakage =
    | Intact
    /// The input host never finds an object under the pointer.
    | NoHitTest
    /// Dispose releases the backend's resources but leaves its input host attached.
    | LeakInputHost
    /// The surface has no interactive mount at all.
    | NotInteractive
    /// The mirror never marks the selected object.
    | MirrorWithoutState

/// One mount's input host: the contract implemented in memory over the reference hit test and keyboard reducer,
/// holding two "listeners" (pointer and keyboard) and a mirror while attached.
type private FakeInputHost(report: SelectionRequest -> unit, first: InteractiveScene, breakage: Breakage) =
    member val Frame = first with get, set
    member val Focused: string option = None with get, set
    member val Attached = true with get, set

    member this.Press(point: Point) =
        if this.Attached && breakage <> NoHitTest then
            match SceneInteraction.hitTest point this.Frame.Objects with
            | Some id ->
                this.Focused <- Some id
                SceneInteraction.press point this.Frame |> Option.iter report
            | None -> ()

    member this.Key(name: string) =
        let key =
            match name with
            | "ArrowRight"
            | "ArrowDown" -> Some InteractionKey.FocusNext
            | "ArrowLeft"
            | "ArrowUp" -> Some InteractionKey.FocusPrevious
            | "Enter"
            | " " -> Some InteractionKey.Activate
            | "Escape" -> Some InteractionKey.Clear
            | _ -> None

        match key with
        | Some key when this.Attached ->
            let focused, request = SceneInteraction.key key this.Focused this.Frame
            this.Focused <- focused
            request |> Option.iter report
        | _ -> ()

    member this.Mirror: MirrorEntry list =
        this.Frame.Objects
        |> List.map (fun target ->
            {
                Id = target.Id
                Label = target.Label
                Pressed = breakage <> MirrorWithoutState && this.Frame.Selected = Some target.Id
            })

    member this.Activate(id: string) =
        if
            this.Attached
            && this.Frame.Objects |> List.exists (fun target -> target.Id = id)
            && this.Frame.Selected <> Some id
        then
            report (SelectionRequest.Select id)

    /// Two listeners and one mirror while attached.
    member this.Holds = if this.Attached then 3 else 0

/// One in-process backend whose mounts hold a countable resource and, when interactive, an input host.
type private InteractiveFake(backend: RendererBackend, breakage: Breakage) =
    let hosts = ResizeArray<FakeInputHost>()

    member val Live = 0 with get, set
    member val Shown: Scene option = None with get, set

    member _.LiveHost: FakeInputHost option =
        hosts |> Seq.filter (fun host -> host.Attached) |> Seq.tryLast

    member _.InputHosts = hosts |> Seq.sumBy (fun host -> host.Holds)

    member this.Surface: SceneSurface =
        let plain =
            SceneSurface.create backend (fun scene ->
                this.Live <- this.Live + 1
                this.Shown <- Some scene
                let disposed = ref false

                Ok
                    {
                        Present = fun next -> this.Shown <- Some next
                        Dispose =
                            fun () ->
                                if not disposed.Value then
                                    disposed.Value <- true
                                    this.Live <- this.Live - 1
                                    this.Shown <- None
                    })

        match breakage with
        | NotInteractive -> plain
        | _ ->
            plain
            |> SceneSurface.withInteraction (fun report frame ->
                this.Live <- this.Live + 1
                this.Shown <- Some frame.Scene
                let host = FakeInputHost(report, frame, breakage)
                hosts.Add host
                let disposed = ref false

                Ok
                    {
                        Present =
                            fun next ->
                                this.Shown <- Some next.Scene
                                host.Frame <- next
                        Dispose =
                            fun () ->
                                if not disposed.Value then
                                    disposed.Value <- true
                                    this.Live <- this.Live - 1
                                    this.Shown <- None

                                    if breakage <> LeakInputHost then
                                        host.Attached <- false
                    })

type private InteractiveFakes(breakOf: RendererBackend -> Breakage) =
    let fakes =
        RendererBackend.all
        |> List.map (fun backend -> backend, InteractiveFake(backend, breakOf backend))
        |> Map.ofList

    member _.Of(backend: RendererBackend) = fakes.[backend]

    member this.Probe: InteractionProbe =
        let onHost backend action =
            async { this.Of(backend).LiveHost |> Option.iter action }

        InteractionProbe.create
            (fun backend point -> onHost backend (fun host -> host.Press point))
            (fun backend name -> onHost backend (fun host -> host.Key name))
            (fun backend -> this.Of(backend).LiveHost |> Option.map (fun host -> host.Mirror))
            (fun backend id -> onHost backend (fun host -> host.Activate id))
            (fun backend -> this.Of(backend).InputHosts)

    member this.Shows (_: ConformanceHost<Model>) (backend: RendererBackend) (state: Model) =
        async {
            let shown = this.Of(backend).Shown

            return
                if shown = Some(ReferenceRenderers.view state) then
                    Ok()
                else
                    Error(sprintf "%s shows %A" (RendererBackend.id backend) shown)
        }

let private states: Model list =
    let start = Composition.boot ()

    [
        start
        { start with
            Screen = Playing
            X = 1
            Aim = 12, 7
        }
        { start with
            Screen = Paused
            Room = Vault
            X = 3
            Tick = 4
            Selected = Some "token"
        }
        { start with
            Screen = Won
            Room = Exit
            X = 5
            Aim = 200, 90
        }
    ]

/// The reference game's renderers on three in-process interactive surfaces, run through the kit with its interaction.
let private runKit (breakOf: RendererBackend -> Breakage) (interaction: SceneInteraction<Model>) =
    let fakes = InteractiveFakes(breakOf)
    let route = SelectionRoute.create ()

    let renderers =
        ReferenceRenderers.declare
            (SceneSurface.mountScene (fakes.Of RendererBackend.Svg).Surface)
            [
                (fakes.Of RendererBackend.Svg).Surface
                (fakes.Of RendererBackend.Pixi).Surface
                (fakes.Of RendererBackend.Babylon).Surface
            ]
        |> SceneRenderers.withInteraction interaction route

    let results =
        RendererConformance.subject
            (SceneRenderers.registry renderers)
            RendererBackend.all
            states
            (fun backend -> (fakes.Of backend).Live)
            fakes.Shows
        |> RendererConformance.withInteraction interaction route fakes.Probe
        |> RendererConformance.run
        |> Async.RunSynchronously

    fakes, results

let private failedChecks (results: ConformanceResult list) =
    RendererConformance.failures results |> List.map (fun result -> result.Check)

let private interactionChecks (results: ConformanceResult list) =
    results |> List.filter (fun result -> result.Check.StartsWith "interaction")

/// The kit's checks for one game on three interactive backends without late-failure triggers: 59 (the sections of
/// FS.GG.Game#745 without the adapter late-failure checks) plus 28 interaction checks.
let private interactionCount = 28

[<Tests>]
let tests =
    testList
        "scene interaction"
        [
            testCase "the reference hit test takes the topmost object and respects each shape"
            <| fun _ ->
                let objects = [ box "under" 0.0 0.0 20.0 20.0; ellipse "over" 10.0 10.0 20.0 20.0 ]

                Expect.equal (SceneInteraction.hitTest (at 5.0 5.0) objects) (Some "under") "only the box"

                Expect.equal
                    (SceneInteraction.hitTest (at 15.0 15.0) objects)
                    (Some "over")
                    "the later object is on top"
                // Inside the ellipse's bounds but outside the ellipse: the box beneath is hit.
                Expect.equal
                    (SceneInteraction.hitTest (at 11.0 11.0) objects)
                    (Some "under")
                    "the ellipse's corner is not the ellipse"

                Expect.equal
                    (SceneInteraction.hitTest (at 29.0 29.0) objects)
                    None
                    "the ellipse's far corner hits nothing"

                Expect.equal (SceneInteraction.hitTest (at 20.0 0.0) objects) (Some "under") "edges are included"
                Expect.equal (SceneInteraction.hitTest (at 40.0 40.0) objects) None "a miss"

            testCase "a press asks only for a change"
            <| fun _ ->
                let objects = [ box "a" 0.0 0.0 10.0 10.0 ]

                Expect.equal
                    (SceneInteraction.press (at 5.0 5.0) (frameOf objects None))
                    (Some(SelectionRequest.Select "a"))
                    "select"

                Expect.equal (SceneInteraction.press (at 5.0 5.0) (frameOf objects (Some "a"))) None "already selected"

                Expect.equal
                    (SceneInteraction.press (at 50.0 5.0) (frameOf objects (Some "a")))
                    None
                    "a miss asks nothing"

            testCase "the keyboard reducer wraps like the retained SVG surface and asks only for changes"
            <| fun _ ->
                let objects =
                    [ box "a" 0.0 0.0 1.0 1.0; box "b" 0.0 0.0 1.0 1.0; box "c" 0.0 0.0 1.0 1.0 ]

                let frame = frameOf objects None

                Expect.equal
                    (SceneInteraction.key InteractionKey.FocusNext None frame)
                    (Some "a", None)
                    "next from nothing is the first"

                Expect.equal
                    (SceneInteraction.key InteractionKey.FocusPrevious None frame)
                    (Some "c", None)
                    "previous from nothing is the last"

                Expect.equal
                    (SceneInteraction.key InteractionKey.FocusNext (Some "c") frame)
                    (Some "a", None)
                    "next wraps"

                Expect.equal
                    (SceneInteraction.key InteractionKey.FocusPrevious (Some "a") frame)
                    (Some "c", None)
                    "previous wraps"

                Expect.equal
                    (SceneInteraction.key InteractionKey.Activate (Some "b") frame)
                    (Some "b", Some(SelectionRequest.Select "b"))
                    "Enter selects the focused object"

                Expect.equal
                    (SceneInteraction.key InteractionKey.Activate (Some "b") (frameOf objects (Some "b")))
                    (Some "b", None)
                    "selecting the selected object asks nothing"

                Expect.equal (SceneInteraction.key InteractionKey.Activate None frame) (None, None) "nothing focused"

                Expect.equal
                    (SceneInteraction.key InteractionKey.Activate (Some "gone") frame)
                    (None, None)
                    "a focus the frame no longer has is dropped"

                Expect.equal (SceneInteraction.key InteractionKey.Clear None frame) (None, None) "nothing to clear"

                Expect.equal
                    (SceneInteraction.key InteractionKey.Clear (Some "a") (frameOf objects (Some "b")))
                    (Some "a", Some SelectionRequest.Clear)
                    "Escape clears"

                Expect.equal
                    (SceneInteraction.key InteractionKey.FocusNext None (frameOf [] None))
                    (None, None)
                    "no objects"

            testCase "problems names blank and repeated ids, blank labels and bad bounds"
            <| fun _ ->
                Expect.isEmpty
                    (SceneInteraction.problems (ReferenceRenderers.objects (Composition.boot ())))
                    "the reference objects are valid"

                let bad =
                    [
                        box "" 0.0 0.0 1.0 1.0
                        { box "x" 0.0 0.0 1.0 1.0 with
                            Label = " "
                        }
                        box "x" nan 0.0 1.0 1.0
                        box "y" 0.0 0.0 -1.0 1.0
                    ]

                Expect.equal (SceneInteraction.problems bad).Length 5 (sprintf "%A" (SceneInteraction.problems bad))

            testCase "a route delivers to what is bound now and drops what nothing hears"
            <| fun _ ->
                let route = SelectionRoute.create ()
                let heard = ResizeArray<SelectionRequest>()
                SelectionRoute.report SelectionRequest.Clear route
                SelectionRoute.bind heard.Add route
                SelectionRoute.report (SelectionRequest.Select "a") route
                SelectionRoute.unbind route
                SelectionRoute.report SelectionRequest.Clear route
                Expect.equal (List.ofSeq heard) [ SelectionRequest.Select "a" ] "only the bound report"

            testCase "withInteraction makes every declared backend's adapter present frames and report to the route"
            <| fun _ ->
                let fakes = InteractiveFakes(fun _ -> Intact)
                let route = SelectionRoute.create ()
                let heard = ResizeArray<SelectionRequest>()
                SelectionRoute.bind heard.Add route

                let renderers =
                    ReferenceRenderers.declare
                        (SceneSurface.mountScene (fakes.Of RendererBackend.Svg).Surface)
                        [
                            (fakes.Of RendererBackend.Svg).Surface
                            (fakes.Of RendererBackend.Pixi).Surface
                        ]
                    |> SceneRenderers.withInteraction ReferenceRenderers.interaction route

                let registry = SceneRenderers.registry renderers

                Expect.equal
                    (RendererRegistry.available registry)
                    [ RendererBackend.Svg; RendererBackend.Pixi ]
                    "declared backends"

                for backend in RendererRegistry.available registry do
                    let adapter = (RendererRegistry.tryFind backend registry).Value

                    let state =
                        { Composition.boot () with
                            Selected = Some "aim"
                        }

                    match adapter.Mount state with
                    | Error reason -> failtest reason
                    | Ok mount ->
                        let host = (fakes.Of backend).LiveHost.Value

                        Expect.equal
                            host.Frame
                            (SceneInteraction.frame ReferenceRenderers.view ReferenceRenderers.interaction state)
                            "the first frame"

                        let next = { state with X = 2; Selected = None }
                        mount.Present next
                        Expect.equal host.Frame.Objects (ReferenceRenderers.objects next) "present projects the objects"
                        Expect.equal host.Frame.Selected None "and the selection"
                        heard.Clear()
                        host.Activate "token"

                        Expect.equal
                            (List.ofSeq heard)
                            [ SelectionRequest.Select "token" ]
                            "the request reaches the route"

                        mount.Dispose()
                        Expect.equal (fakes.Of backend).InputHosts 0 "the input host goes with the mount"

            testCase "a surface without an interactive mount gives the plain adapter"
            <| fun _ ->
                let plain = InteractiveFake(RendererBackend.Babylon, NotInteractive)
                Expect.isFalse (SceneSurface.isInteractive plain.Surface) "not interactive"

                let adapter =
                    SceneSurface.interactiveAdapter
                        ReferenceRenderers.view
                        ReferenceRenderers.interaction
                        (SelectionRoute.create ())
                        plain.Surface

                match adapter.Mount(Composition.boot ()) with
                | Error reason -> failtest reason
                | Ok _ ->
                    Expect.equal plain.Shown (Some(ReferenceRenderers.view (Composition.boot ()))) "the plain Scene"
                    Expect.equal plain.InputHosts 0 "and no input host"

            testCase "the reference game's interaction passes the kit on three interactive backends"
            <| fun _ ->
                let _, results = runKit (fun _ -> Intact) ReferenceRenderers.interaction
                Expect.isEmpty (failedChecks results) "every check passes"

                Expect.equal
                    (interactionChecks results).Length
                    interactionCount
                    "the interaction section ran on every backend"

                Expect.equal results.Length (59 + interactionCount) "and every other section ran too"

                Expect.contains
                    (results |> List.map (fun result -> result.Check))
                    "interaction babylon: a pointer press on each object selects it in the game state (hit test)"
                    "the hit test is checked per backend"

            testCase "the kit without an interaction runs no interaction check"
            <| fun _ ->
                let fakes = InteractiveFakes(fun _ -> Intact)

                let renderers =
                    ReferenceRenderers.declare
                        (SceneSurface.mountScene (fakes.Of RendererBackend.Svg).Surface)
                        [ (fakes.Of RendererBackend.Pixi).Surface ]

                let results =
                    RendererConformance.subject
                        (SceneRenderers.registry renderers)
                        [ RendererBackend.Svg; RendererBackend.Pixi ]
                        states
                        (fun backend -> (fakes.Of backend).Live)
                        fakes.Shows
                    |> RendererConformance.run
                    |> Async.RunSynchronously

                Expect.isEmpty (failedChecks results) "passes"
                Expect.isEmpty (interactionChecks results) "no interaction section"

            testCase "negative control: a backend with no hit test fails exactly its pointer check"
            <| fun _ ->
                let _, results =
                    runKit
                        (function
                        | RendererBackend.Pixi -> NoHitTest
                        | _ -> Intact)
                        ReferenceRenderers.interaction

                Expect.equal
                    (failedChecks results)
                    [
                        "interaction pixi: a pointer press on each object selects it in the game state (hit test)"
                    ]
                    "the missing hit test is caught, and nothing else is blamed"

            testCase "negative control: a backend that leaks its input host on dispose fails exactly its release check"
            <| fun _ ->
                let fakes, results =
                    runKit
                        (function
                        | RendererBackend.Babylon -> LeakInputHost
                        | _ -> Intact)
                        ReferenceRenderers.interaction

                Expect.equal
                    (failedChecks results)
                    [
                        "interaction babylon: switching away and disposing the host release the input host (listeners and mirror)"
                    ]
                    "the leaked input host is caught"

                Expect.isGreaterThan (fakes.Of RendererBackend.Babylon).InputHosts 0 "the control did leak"

            testCase "negative control: a backend without an interactive mount fails its interaction checks"
            <| fun _ ->
                let _, results =
                    runKit
                        (function
                        | RendererBackend.Pixi -> NotInteractive
                        | _ -> Intact)
                        ReferenceRenderers.interaction

                let failed = failedChecks results
                Expect.all failed (fun check -> check.StartsWith "interaction") "only interaction checks fail"

                for check in
                    [
                        "interaction pixi: the input host is attached while the backend is mounted"
                        "interaction pixi: the accessible mirror lists every selectable object in order, with its label, none pressed"
                        "interaction pixi: a pointer press on each object selects it in the game state (hit test)"
                        "interaction svg: the selection survives a switch to pixi and back, and both mirrors show it"
                    ] do
                    Expect.contains failed check "a backend that cannot be played by selection is caught"

            testCase "negative control: a mirror that does not show the selection fails the selection checks"
            <| fun _ ->
                let _, results =
                    runKit
                        (function
                        | RendererBackend.Svg -> MirrorWithoutState
                        | _ -> Intact)
                        ReferenceRenderers.interaction

                let failed = failedChecks results

                Expect.contains
                    failed
                    "interaction svg: a pointer press on each object selects it in the game state (hit test)"
                    "pointer"

                Expect.contains
                    failed
                    "interaction svg: activating a mirror entry selects its object in the game state"
                    "mirror"

                Expect.all failed (fun check -> check.StartsWith "interaction") "only interaction checks fail"

            testCase "negative control: a game rule that ignores requests fails every selecting check"
            <| fun _ ->
                let ignoring =
                    SceneInteraction.create ReferenceRenderers.objects (fun model -> model.Selected) (fun _ model ->
                        model)

                let _, results = runKit (fun _ -> Intact) ignoring
                let failed = failedChecks results
                // The play state is cleared by the game's own rule; states.[0] has nothing selected, so the subject check
                // holds and the selection checks fail on every backend.
                for backend in RendererBackend.all do
                    Expect.contains
                        failed
                        (sprintf
                            "interaction %s: activating a mirror entry selects its object in the game state"
                            (RendererBackend.id backend))
                        "the request is made but the game state never changes"
        ]

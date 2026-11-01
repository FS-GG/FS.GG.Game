module Game.Render.Tests.RendererConformanceTests

// GAME-RENDER-ADOPT-01.1 (FS.GG.Game#745): the three-adapter authoring path (`SceneSurface`, `SceneRenderers`)
// and the conformance kit (`RendererConformance`). The reference game's own renderer declaration passes the kit on
// three in-process surfaces that own countable resources and fail late through their own reporting path; each
// negative control breaks one adapter obligation and must fail the section that guards it, so a passing kit is
// evidence and not a kit that cannot fail. The browser run of the same kit, on the real SVG, Pixi and Babylon
// surfaces, is tests/Render.Browser.Smoke.

open Expecto
open FS.GG.UI.Scene
open FS.GG.Game.Render
open FS.GG.Game.Reference
open FS.GG.Game.Reference.ReferenceState

/// One in-process backend: a surface whose mounts hold a countable resource, record the Scene they show and, when
/// armed, fail later through the surface's own report (the shape of Pixi's asynchronous initialisation).
type private FakeBackend(backend: RendererBackend) =
    /// Late failures waiting to "settle", like a pending promise.
    let pending = ResizeArray<unit -> unit>()

    member val Live = 0 with get, set
    member val Shown: Scene option = None with get, set
    member val Mounts = 0 with get, set
    member val Armed = false with get, set
    /// Where a late failure goes; the test binds it to the current host, like `LateFailureRoute`.
    member val Report: RendererBackend -> string -> unit = (fun _ _ -> ()) with get, set
    /// Negative controls.
    member val LeakOnDispose = false with get, set
    member val IgnorePresent = false with get, set
    member val ReportAfterDispose = false with get, set

    member this.Surface: SceneSurface =
        SceneSurface.create backend (fun scene ->
            this.Mounts <- this.Mounts + 1
            this.Live <- this.Live + 1
            this.Shown <- Some scene
            let disposed = ref false
            // Captured at mount time: the report reaches the host this mount belongs to.
            let report = this.Report

            if this.Armed then
                this.Armed <- false

                pending.Add(fun () ->
                    // The live-mount rule: a disposed mount reports nothing (unless the control removes the guard).
                    if not disposed.Value || this.ReportAfterDispose then
                        report backend "fake: late failure")

            Ok
                {
                    Present =
                        fun next ->
                            if not this.IgnorePresent then
                                this.Shown <- Some next
                    Dispose =
                        fun () ->
                            if not disposed.Value && not this.LeakOnDispose then
                                disposed.Value <- true
                                this.Live <- this.Live - 1
                                this.Shown <- None
                })

    member this.Trigger: LateFailureTrigger =
        LateFailureTrigger.create
            (fun () -> this.Armed <- true)
            (async {
                let settled = pending.Count > 0

                for failure in List.ofSeq pending do
                    failure ()

                pending.Clear()
                return settled
            })

type private Fakes() =
    member val Svg = FakeBackend(RendererBackend.Svg)
    member val Pixi = FakeBackend(RendererBackend.Pixi)
    member val Babylon = FakeBackend(RendererBackend.Babylon)

    member this.Of(backend: RendererBackend) =
        match backend with
        | RendererBackend.Svg -> this.Svg
        | RendererBackend.Pixi -> this.Pixi
        | RendererBackend.Babylon -> this.Babylon

    member this.Resources(backend: RendererBackend) = this.Of(backend).Live

    /// What the backend shows, read from the surface (not the model): the Scene of the state.
    member this.Shows (_: ConformanceHost<Model>) (backend: RendererBackend) (state: Model) =
        async {
            let shown = this.Of(backend).Shown

            return
                if shown = Some(ReferenceRenderers.view state) then
                    Ok()
                else
                    Error(sprintf "%s shows %A" (RendererBackend.id backend) shown)
        }

    /// The in-process host, with every fake's late-failure report bound to it (the `BrowserRenderers.start` shape).
    member this.Start (registry: RendererRegistry<Model>) (state: Model) =
        let host = RendererConformance.inProcess registry state

        for fake in [ this.Svg; this.Pixi; this.Babylon ] do
            fake.Report <- host.FailBackend

        host

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
        }
        { start with
            Screen = Won
            Room = Exit
            X = 5
            Aim = 200, 90
        }
    ]

let private declareAll (fakes: Fakes) =
    ReferenceRenderers.declare (SceneSurface.mountScene fakes.Svg.Surface) [ fakes.Pixi.Surface; fakes.Babylon.Surface ]

let private subjectOf (fakes: Fakes) (registry: RendererRegistry<Model>) (expected: RendererBackend list) =
    RendererConformance.subject registry expected states fakes.Resources fakes.Shows
    |> RendererConformance.withStart fakes.Start
    |> RendererConformance.withLateFailure RendererBackend.Pixi fakes.Pixi.Trigger
    |> RendererConformance.withLateFailure RendererBackend.Babylon fakes.Babylon.Trigger

let private run (subject: ConformanceSubject<Model>) =
    RendererConformance.run subject |> Async.RunSynchronously

let private runKit (fakes: Fakes) (registry: RendererRegistry<Model>) (expected: RendererBackend list) =
    subjectOf fakes registry expected |> run

let private failedChecks (results: ConformanceResult list) =
    RendererConformance.failures results |> List.map (fun result -> result.Check)

let private section (prefix: string) (results: ConformanceResult list) =
    results |> List.filter (fun result -> result.Check.StartsWith prefix)

[<Tests>]
let tests =
    testList
        "renderer authoring kit"
        [
            testCase "SceneRenderers offers exactly the declared backends, SVG always"
            <| fun () ->
                let fakes = Fakes()
                let svgOnly = SceneRenderers.create ReferenceRenderers.view (SceneSurface.mountScene fakes.Svg.Surface)

                Expect.equal (SceneRenderers.backends svgOnly) [ RendererBackend.Svg ] "SVG is declared by create"

                Expect.equal
                    (RendererRegistry.available (SceneRenderers.registry svgOnly))
                    [ RendererBackend.Svg ]
                    "only SVG is offered"

                let babylonFirst =
                    svgOnly
                    |> SceneRenderers.withSurface fakes.Babylon.Surface
                    |> SceneRenderers.withSurface fakes.Pixi.Surface

                Expect.equal
                    (RendererRegistry.available (SceneRenderers.registry babylonFirst))
                    RendererBackend.all
                    "declaration order does not matter; the registry offers declared order"

            testCase "SceneSurface.adapter projects every state through the view, like RendererAdapter.svg"
            <| fun () ->
                let fakes = Fakes()
                Expect.equal (SceneSurface.backend fakes.Pixi.Surface) RendererBackend.Pixi "create keeps the backend"
                let adapter = SceneSurface.adapter ReferenceRenderers.view fakes.Pixi.Surface
                Expect.equal adapter.Backend RendererBackend.Pixi "backend comes from the surface"

                match adapter.Mount states.[0] with
                | Error reason -> failtest reason
                | Ok mount ->
                    Expect.equal fakes.Pixi.Shown (Some(ReferenceRenderers.view states.[0])) "mount presents the first state's Scene"
                    mount.Present states.[2]
                    Expect.equal fakes.Pixi.Shown (Some(ReferenceRenderers.view states.[2])) "present projects the next state"
                    mount.Dispose()
                    Expect.equal fakes.Pixi.Live 0 "dispose reaches the surface"

            testCase "withView gives one backend its own representation and leaves the others on the shared view"
            <| fun () ->
                let fakes = Fakes()
                let blank (_: Model) : Scene = { Nodes = [] }

                let renderers =
                    declareAll fakes |> SceneRenderers.withView RendererBackend.Babylon blank

                Expect.equal ((SceneRenderers.view RendererBackend.Babylon renderers) states.[1]) (blank states.[1]) "own view"

                Expect.equal
                    ((SceneRenderers.view RendererBackend.Pixi renderers) states.[1])
                    (ReferenceRenderers.view states.[1])
                    "shared view"

                let registry = SceneRenderers.registry renderers

                match RendererRegistry.tryFind RendererBackend.Babylon registry with
                | None -> failtest "babylon is declared"
                | Some adapter ->
                    adapter.Mount states.[1] |> ignore
                    Expect.equal fakes.Babylon.Shown (Some(blank states.[1])) "the registry's Babylon adapter draws its own view"

            testCase "the reference game passes the conformance kit on SVG, Pixi and Babylon"
            <| fun () ->
                let fakes = Fakes()
                let results = runKit fakes (SceneRenderers.registry (declareAll fakes)) RendererBackend.all

                Expect.isEmpty (failedChecks results) "every conformance check passes"

                for prefix in [ "subject"; "registry"; "round-trip"; "switch"; "dispose"; "late failure"; "present failure" ] do
                    Expect.isNonEmpty (section prefix results) (sprintf "the %s section ran" prefix)

                // 2 subject + 1 registry + 3x4 round-trip + (7x3 + 1) switch + (4 + 2x5) dispose
                // + 2x4 late failure (host) + 2x3 late failure (adapter, both canvas fakes have a trigger)
                // + (5 + 2x3) present failure (the default backend, then each canvas backend).
                Expect.equal results.Length 76 "the kit ran every check for three backends"

                Expect.equal
                    (results
                     |> List.filter (fun result -> result.Check.StartsWith "switch ")
                     |> List.filter (fun result -> result.Check.EndsWith "with the game state kept")
                     |> List.length)
                    7
                    "the switch walk covers all six ordered pairs in seven switches"

                Expect.equal (section "late failure pixi (adapter)" results |> List.length) 3 "pixi's own late failure ran"
                Expect.equal (fakes.Svg.Live, fakes.Pixi.Live, fakes.Babylon.Live) (0, 0, 0) "nothing is left mounted"

            testCase "an SVG-only declaration passes, and selecting an undeclared backend is refused"
            <| fun () ->
                let fakes = Fakes()

                let registry =
                    SceneRenderers.registry (ReferenceRenderers.declare (SceneSurface.mountScene fakes.Svg.Surface) [])

                let results = runKit fakes registry [ RendererBackend.Svg ]
                Expect.isEmpty (failedChecks results) "an SVG-only game conforms"

                Expect.equal
                    (section "registry: selecting" results |> List.length)
                    2
                    "pixi and babylon are each refused as unavailable"

                Expect.isEmpty (section "switch" results) "one backend has no switch"
                Expect.isEmpty (section "late failure" results) "SVG has no late-failure section"
                Expect.equal (fakes.Pixi.Mounts, fakes.Babylon.Mounts) (0, 0) "undeclared backends were never mounted"

            testCase "negative control: a missing adapter fails the registry check"
            <| fun () ->
                let fakes = Fakes()

                let registry =
                    SceneRenderers.registry (
                        ReferenceRenderers.declare (SceneSurface.mountScene fakes.Svg.Surface) [ fakes.Pixi.Surface ]
                    )

                let failed = runKit fakes registry RendererBackend.all |> failedChecks

                Expect.equal
                    failed
                    [ "registry: offers exactly the expected backends, in declared order" ]
                    "only the registry check fails"

            testCase "negative control: a backend that leaks on dispose fails the switch and dispose checks"
            <| fun () ->
                let fakes = Fakes()
                fakes.Babylon.LeakOnDispose <- true
                let failed = runKit fakes (SceneRenderers.registry (declareAll fakes)) RendererBackend.all |> failedChecks

                Expect.contains failed "dispose babylon: switching away releases its resources" "switching away"
                Expect.contains failed "dispose babylon: disposing the host releases every resource" "host dispose"

                Expect.contains
                    failed
                    "late failure babylon (host): the failed backend's resources are released"
                    "late failure"

                Expect.isTrue
                    (failed |> List.exists (fun check -> check.EndsWith "only the selected backend holds resources"))
                    "exclusivity"

                // A leak is page-wide: the leaked Babylon resources also fail later whole-page "releases every
                // resource" checks. What stays green is everything a leak cannot affect.
                Expect.isFalse
                    (failed
                     |> List.exists (fun check ->
                         check.StartsWith "round-trip"
                         || check = "dispose pixi: switching away releases its resources"))
                    "round-trip and the pixi release still conform"

            testCase "negative control: a backend that ignores Present fails round-trip"
            <| fun () ->
                let fakes = Fakes()
                fakes.Pixi.IgnorePresent <- true
                let failed = runKit fakes (SceneRenderers.registry (declareAll fakes)) RendererBackend.all |> failedChecks

                Expect.contains failed "round-trip pixi: the backend shows every presented state" "round-trip shows"

                Expect.isFalse
                    (failed
                     |> List.exists (fun check -> check.StartsWith "round-trip svg" || check.StartsWith "round-trip babylon"))
                    "the other backends still round-trip"

            testCase "negative control: an adapter that reports a late failure after its own dispose breaks the live-mount rule"
            <| fun () ->
                let fakes = Fakes()
                fakes.Pixi.ReportAfterDispose <- true
                let failed = runKit fakes (SceneRenderers.registry (declareAll fakes)) RendererBackend.all |> failedChecks

                Expect.equal
                    failed
                    [
                        "late failure pixi (adapter): a failure of a disposed mount is not reported, and the live mount of the same backend stays"
                    ]
                    "exactly the live-mount rule fails"

            testCase "negative control: a view or probe that ignores the state fails round-trip"
            <| fun () ->
                let fakes = Fakes()
                let constant (_: Model) : Scene = ReferenceRenderers.view states.[0]

                let renderers =
                    SceneRenderers.create constant (SceneSurface.mountScene fakes.Svg.Surface)
                    |> SceneRenderers.withSurface fakes.Pixi.Surface
                    |> SceneRenderers.withSurface fakes.Babylon.Surface

                let blindShows _ _ _ = async { return Ok() }

                let failed =
                    RendererConformance.subject (SceneRenderers.registry renderers) RendererBackend.all states fakes.Resources blindShows
                    |> RendererConformance.withStart fakes.Start
                    |> run
                    |> failedChecks

                for backend in RendererBackend.all do
                    Expect.contains
                        failed
                        (sprintf "round-trip %s: the output probe tells the shown state from another" (RendererBackend.id backend))
                        "the discrimination guard fails"

            testCase "negative control: a resource probe that sees nothing fails instead of passing vacuously"
            <| fun () ->
                let fakes = Fakes()

                let failed =
                    RendererConformance.subject
                        (SceneRenderers.registry (declareAll fakes))
                        RendererBackend.all
                        states
                        (fun _ -> 0)
                        fakes.Shows
                    |> RendererConformance.withStart fakes.Start
                    |> run
                    |> failedChecks

                for backend in RendererBackend.all do
                    Expect.contains
                        failed
                        (sprintf "dispose %s: the resource probe sees the mounted backend" (RendererBackend.id backend))
                        "the probe's subject is asserted"

            testCase "negative control: one state is not enough"
            <| fun () ->
                let fakes = Fakes()

                let failed =
                    RendererConformance.subject
                        (SceneRenderers.registry (declareAll fakes))
                        RendererBackend.all
                        [ states.[0]; states.[0] ]
                        fakes.Resources
                        fakes.Shows
                    |> run
                    |> failedChecks

                Expect.contains failed "subject: at least two distinct game states" "the subject is checked"

            testCase "the in-process host has the driver's failBackend semantics"
            <| fun () ->
                let fakes = Fakes()
                let host = RendererConformance.inProcess (SceneRenderers.registry (declareAll fakes)) states.[1]
                host.FailBackend RendererBackend.Pixi "not selected"
                Expect.equal ((host.Model()).Refusal, (host.Model()).Attempt) (None, 1) "a report for an unselected backend is ignored"
                host.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
                host.FailBackend RendererBackend.Pixi "late"
                let model = host.Model()
                Expect.equal (model.Selected, host.Mounted(), model.State) (RendererBackend.Svg, Some RendererBackend.Svg, states.[1]) "falls back"
                Expect.equal fakes.Pixi.Live 0 "the failed mount is released"
                host.Dispose()
                Expect.equal fakes.Svg.Live 0 "dispose releases SVG"

            testCase "present failure: every backend recovers, SVG by remounting, and the failure is reported"
            <| fun () ->
                let fakes = Fakes()
                let results = runKit fakes (SceneRenderers.registry (declareAll fakes)) RendererBackend.all
                Expect.isEmpty (failedChecks results) "conforms"
                let section = section "present failure" results
                Expect.equal (section |> List.filter (fun result -> result.Check.StartsWith "present failure svg") |> List.length) 5 "svg: contained, one live mount, shown, remount failure reported, next state recovers"
                Expect.equal (section |> List.filter (fun result -> result.Check.StartsWith "present failure pixi") |> List.length) 3 "pixi: contained, released, shown"
                Expect.equal (fakes.Svg.Live, fakes.Pixi.Live, fakes.Babylon.Live) (0, 0, 0) "nothing is left mounted"

            testCase "negative control: a backend that leaks the mount a failing Present left behind fails the release check"
            <| fun () ->
                let fakes = Fakes()
                fakes.Pixi.LeakOnDispose <- true
                let failed = runKit fakes (SceneRenderers.registry (declareAll fakes)) RendererBackend.all |> failedChecks

                Expect.contains
                    failed
                    "present failure pixi: the failed mount is disposed once and exactly one live mount remains"
                    "the leaked canvas is seen"

            testCase "negative control: a host that leaves a throwing default Present Failed fails the SVG present-failure checks"
            <| fun () ->
                // The behaviour before GAME-RENDER-ADOPT-01.3: a throwing Present on the default backend reaches the
                // model as a mount failure, and a state change while Failed only stores the state.
                let fakes = Fakes()

                let legacy (registry: RendererRegistry<Model>) (state: Model) =
                    let rendererHost = RendererHost.create registry
                    let initial, initialEffects = RendererSelection.init registry state
                    let model = ref initial
                    let pending = ref (rendererHost.Run initialEffects)

                    let asLegacy msg =
                        match msg with
                        | RendererMsg.PresentFailed(backend, attempt, reason) -> RendererMsg.MountFailed(backend, attempt, reason)
                        | other -> other

                    let rec drain () =
                        match pending.Value with
                        | [] -> ()
                        | msg :: rest ->
                            pending.Value <- rest

                            match msg, model.Value.Status with
                            | RendererMsg.StateChanged next, RendererStatus.Failed _ -> model.Value <- { model.Value with State = next }
                            | _ ->
                                let next, effects = RendererSelection.update msg model.Value
                                model.Value <- next
                                pending.Value <- (rendererHost.Run effects |> List.map asLegacy) @ pending.Value

                            drain ()

                    drain ()

                    let dispatch msg =
                        pending.Value <- pending.Value @ [ msg ]
                        drain ()

                    RendererConformance.host
                        (fun () -> model.Value)
                        (fun () -> rendererHost.Mounted)
                        dispatch
                        (fun _ _ -> ())
                        (fun () -> (rendererHost :> System.IDisposable).Dispose())

                let failed =
                    subjectOf fakes (SceneRenderers.registry (declareAll fakes)) RendererBackend.all
                    |> RendererConformance.withStart legacy
                    |> run
                    |> failedChecks
                    |> List.filter (fun check -> check.StartsWith "present failure")

                Expect.contains
                    failed
                    "present failure svg: a throwing Present is contained and the default backend mounts the latest state"
                    "the blank map is caught"

                Expect.isFalse
                    (failed |> List.exists (fun check -> check.StartsWith "present failure pixi" || check.StartsWith "present failure babylon"))
                    "the canvas fallback was already correct"
        ]

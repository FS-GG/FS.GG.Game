module Game.Render.Tests.RendererRegistryTests

// GAME-RENDER-ADOPT-01.2 (FS.GG.Game#750): a registry from a READY adapter. A game that owns a retained SVG surface
// has no `FS.GG.UI.Scene` to hand to `RendererRegistry.svg`; `RendererRegistry.create` takes the adapter as it is, so
// no refusing placeholder is ever registered. The same registry passes the conformance kit, and the kit's own
// negative controls still fail on it.

open Expecto
open FS.GG.Game.Render

/// A game state that is not a Scene and has no projection to one.
type private Board = { Stamp: int; Chosen: string option }

let private boards =
    [
        { Stamp = 1; Chosen = Some "b" }
        { Stamp = 2; Chosen = Some "c" }
        { Stamp = 3; Chosen = None }
    ]

/// A retained surface stand-in: holds one countable resource per mount and shows the last state presented.
type private Surface() =
    member val Live = 0 with get, set
    member val Mounts = 0 with get, set
    member val Shown: Board option = None with get, set
    member val Refuse: string option = None with get, set
    member val IgnorePresent = false with get, set

    member this.Adapter(backend: RendererBackend) : RendererAdapter<Board> =
        {
            Backend = backend
            Mount =
                fun board ->
                    match this.Refuse with
                    | Some reason -> Error reason
                    | None ->
                        this.Mounts <- this.Mounts + 1
                        this.Live <- this.Live + 1
                        this.Shown <- Some board
                        let disposed = ref false

                        Ok
                            {
                                Present =
                                    fun next ->
                                        if not this.IgnorePresent then
                                            this.Shown <- Some next
                                Dispose =
                                    fun () ->
                                        if not disposed.Value then
                                            disposed.Value <- true
                                            this.Live <- this.Live - 1
                                            this.Shown <- None
                            }
        }

let private registryOf (adapter: RendererAdapter<Board>) =
    match RendererRegistry.create adapter with
    | Ok registry -> registry
    | Error reason -> failtest reason

let private subjectOf (surface: Surface) (registry: RendererRegistry<Board>) (expected: RendererBackend list) =
    let shows (_: ConformanceHost<Board>) (_: RendererBackend) (board: Board) =
        async {
            return
                if surface.Shown = Some board then
                    Ok()
                else
                    Error(sprintf "the surface shows %A" surface.Shown)
        }

    RendererConformance.subject registry expected boards (fun backend -> if backend = RendererBackend.Svg then surface.Live else 0) shows

let private failedChecks (results: ConformanceResult list) =
    RendererConformance.failures results |> List.map (fun result -> result.Check)

[<Tests>]
let tests =
    testList
        "ready adapter registry"
        [
            testCase "create registers a ready SVG adapter as the default, with no Scene projection"
            <| fun () ->
                let surface = Surface()
                let registry = registryOf (surface.Adapter RendererBackend.Svg)

                Expect.equal (RendererRegistry.available registry) [ RendererBackend.Svg ] "SVG is the only backend offered"
                Expect.isSome (RendererRegistry.tryFind RendererBackend.Svg registry) "the adapter is found"
                Expect.isFalse (RendererRegistry.isAvailable RendererBackend.Pixi registry) "nothing else is registered"

                let model, effects = RendererSelection.init registry boards.Head
                let host = RendererHost.create registry
                let feedback = host.Run effects

                Expect.isEmpty feedback "the ready adapter mounts"
                Expect.equal (model.Selected, model.Status, host.Mounted) (RendererBackend.Svg, RendererStatus.Active, Some RendererBackend.Svg) "mounted and active"
                Expect.equal surface.Shown (Some boards.Head) "the adapter was mounted with the state itself"

                (host :> System.IDisposable).Dispose()
                Expect.equal surface.Live 0 "disposing the host releases the surface"

            testCase "create refuses an adapter that is not SVG, and builds nothing"
            <| fun () ->
                let surface = Surface()

                for backend in [ RendererBackend.Pixi; RendererBackend.Babylon ] do
                    match RendererRegistry.create (surface.Adapter backend) with
                    | Ok _ -> failtestf "%s cannot be the default adapter" (RendererBackend.id backend)
                    | Error reason ->
                        Expect.stringContains reason (RendererBackend.id backend) "the refusal names the backend"
                        Expect.stringContains reason "svg" "the refusal names the default"

                Expect.equal (surface.Mounts, surface.Live) (0, 0) "refusing acquired nothing"

            testCase "other adapters register on top of a created registry, and replace by backend"
            <| fun () ->
                let svg = Surface()
                let pixi = Surface()
                let babylon = Surface()

                let registry =
                    registryOf (svg.Adapter RendererBackend.Svg)
                    |> RendererRegistry.register (pixi.Adapter RendererBackend.Pixi)
                    |> RendererRegistry.register (babylon.Adapter RendererBackend.Babylon)

                Expect.equal (RendererRegistry.available registry) RendererBackend.all "all three are offered"

                let replacement = Surface()
                let replaced = RendererRegistry.register (replacement.Adapter RendererBackend.Svg) registry
                let host = RendererHost.create replaced
                host.Run [ RendererEffect.Mount(RendererBackend.Svg, 1, boards.Head) ] |> ignore

                Expect.equal (svg.Mounts, replacement.Mounts) (0, 1) "the replacement adapter is the one that mounts"
                (host :> System.IDisposable).Dispose()

            testCase "a created registry passes the conformance kit, alone and with canvas adapters"
            <| fun () ->
                let svg = Surface()
                let alone = registryOf (svg.Adapter RendererBackend.Svg)

                let results = RendererConformance.run (subjectOf svg alone [ RendererBackend.Svg ]) |> Async.RunSynchronously
                if not (List.isEmpty (RendererConformance.failures results)) then failtestf "%A" (RendererConformance.failures results |> List.map string)

                // 2 subject + 1 registry + 2 refused + 4 round-trip + 4 dispose + 5 present failure; no switch or late
                // failure with one backend.
                Expect.equal results.Length 18 "the kit ran every check for one backend"
                Expect.equal svg.Live 0 "nothing is left mounted"

                // The kit's instrumented copy starts from the ready adapter: no placeholder ever mounts.
                Expect.isGreaterThan svg.Mounts 0 "the ready adapter was mounted by the kit"

                // Three backends, each with its own surface and its own resource count.
                let surfaces = RendererBackend.all |> List.map (fun backend -> backend, Surface()) |> Map.ofList
                let live backend = surfaces.[backend].Live

                let all =
                    registryOf (surfaces.[RendererBackend.Svg].Adapter RendererBackend.Svg)
                    |> RendererRegistry.register (surfaces.[RendererBackend.Pixi].Adapter RendererBackend.Pixi)
                    |> RendererRegistry.register (surfaces.[RendererBackend.Babylon].Adapter RendererBackend.Babylon)

                let shows (host: ConformanceHost<Board>) (backend: RendererBackend) (board: Board) =
                    async {
                        return
                            if surfaces.[backend].Shown = Some board then
                                Ok()
                            else
                                Error(sprintf "%s shows %A" (RendererBackend.id backend) surfaces.[backend].Shown)
                    }

                let failed =
                    RendererConformance.subject all RendererBackend.all boards live shows
                    |> RendererConformance.run
                    |> Async.RunSynchronously
                    |> failedChecks

                Expect.isEmpty failed "a created registry with registered canvas adapters conforms"

            testCase "negative control: a ready adapter that ignores Present fails round-trip"
            <| fun () ->
                let surface = Surface()
                surface.IgnorePresent <- true
                let registry = registryOf (surface.Adapter RendererBackend.Svg)

                let failed =
                    RendererConformance.run (subjectOf surface registry [ RendererBackend.Svg ])
                    |> Async.RunSynchronously
                    |> failedChecks

                Expect.contains failed "round-trip svg: the backend shows every presented state" "the ready adapter is held to the kit"

            testCase "negative control: a ready adapter that refuses to mount fails the kit instead of passing"
            <| fun () ->
                let surface = Surface()
                surface.Refuse <- Some "no container"
                let registry = registryOf (surface.Adapter RendererBackend.Svg)

                let failed =
                    RendererConformance.run (subjectOf surface registry [ RendererBackend.Svg ])
                    |> Async.RunSynchronously
                    |> failedChecks

                Expect.isNonEmpty failed "a surface that never mounts does not conform"
                Expect.contains failed "dispose svg: the resource probe sees the mounted backend" "the probe's subject is asserted"
        ]


// GAME-RENDER-ADOPT-01.3 (FS.GG.Game#752): a mount fixed to its viewport, and the remount rule that presents a change.

/// A state with a size; the viewport is `(Width, 50)`.
type private Sized = { Id: int; Width: float }

let private sizedStates =
    [
        { Id = 1; Width = 100.0 }
        { Id = 2; Width = 100.0 }
        { Id = 3; Width = 200.0 }
        { Id = 4; Width = 300.0 }
    ]

let private viewportOf (state: Sized) = state.Width, 50.0

/// The shape of `SvgBrowser`: the viewport is fixed when the mount is created. `InPlace` is the other kind of adapter,
/// which resizes itself when a state asks for it.
type private SizedSurface() =
    member val Live = 0 with get, set
    member val Mounts = 0 with get, set
    member val Shown: Sized option = None with get, set
    member val Viewport: (float * float) option = None with get, set
    member val InPlace = false with get, set
    /// A stale viewport raises in `Present`, like the retained SVG surface does.
    member val RaiseOnStale = false with get, set

    member this.Adapter: RendererAdapter<Sized> =
        {
            Backend = RendererBackend.Svg
            Mount =
                fun state ->
                    this.Mounts <- this.Mounts + 1
                    this.Live <- this.Live + 1
                    this.Shown <- Some state
                    this.Viewport <- Some(viewportOf state)
                    let disposed = ref false

                    Ok
                        {
                            Present =
                                fun next ->
                                    if this.RaiseOnStale && this.Viewport <> Some(viewportOf next) then
                                        failwith "mounted at another viewport"

                                    this.Shown <- Some next

                                    if this.InPlace then
                                        this.Viewport <- Some(viewportOf next)
                            Dispose =
                                fun () ->
                                    if not disposed.Value then
                                        disposed.Value <- true
                                        this.Live <- this.Live - 1
                                        this.Shown <- None
                                        this.Viewport <- None
                        }
        }

let private sizedKit (surface: SizedSurface) (registry: RendererRegistry<Sized>) (states: Sized list) =
    let shows (_: ConformanceHost<Sized>) (_: RendererBackend) (state: Sized) =
        async { return if surface.Shown = Some state then Ok() else Error(sprintf "shows %A" surface.Shown) }

    RendererConformance.subject registry [ RendererBackend.Svg ] states (fun backend -> if backend = RendererBackend.Svg then surface.Live else 0) shows
    |> RendererConformance.withViewport viewportOf (fun _ _ -> async { return surface.Viewport })
    |> RendererConformance.run
    |> Async.RunSynchronously
    |> RendererConformance.failures
    |> List.map (fun result -> result.Check)

let private sizedRegistry (surface: SizedSurface) (declare: bool) =
    let registry =
        match RendererRegistry.create surface.Adapter with
        | Ok registry -> registry
        | Error reason -> failwith reason

    if declare then
        RendererRegistry.withViewport RendererBackend.Svg viewportOf registry
    else
        registry

[<Tests>]
let viewportTests =
    testList
        "viewport lifecycle"
        [
            testCase "a mount fixed to its viewport conforms when the registry declares the viewport (remount rule)"
            <| fun () ->
                let surface = SizedSurface()
                let failed = sizedKit surface (sizedRegistry surface true) sizedStates in if not failed.IsEmpty then failtestf "%A" failed
                Expect.equal surface.Live 0 "nothing is left mounted"

            testCase "negative control: a fixed viewport that is not declared fails the viewport check"
            <| fun () ->
                let surface = SizedSurface()
                let failed = sizedKit surface (sizedRegistry surface false) sizedStates

                Expect.equal
                    failed
                    [ "viewport svg: every state is presented at its own viewport, growing and shrinking" ]
                    "only the stale viewport fails: the surface kept the size it was mounted at"

            testCase "negative control: a declared viewport that never changes leaves the stale size"
            <| fun () ->
                let surface = SizedSurface()

                let registry =
                    sizedRegistry surface false |> RendererRegistry.withViewport RendererBackend.Svg (fun _ -> 1.0, 1.0)

                Expect.contains
                    (sizedKit surface registry sizedStates)
                    "viewport svg: every state is presented at its own viewport, growing and shrinking"
                    "a wrong declaration is caught by reading the backend back"

            testCase "an adapter that resizes itself conforms without a declaration"
            <| fun () ->
                let surface = SizedSurface()
                surface.InPlace <- true
                Expect.isEmpty (sizedKit surface (sizedRegistry surface false) sizedStates) "in-place resize conforms"

            testCase "negative control: states with one viewport do not pass the viewport check vacuously"
            <| fun () ->
                let surface = SizedSurface()

                let same = sizedStates |> List.map (fun state -> { state with Width = 100.0 })

                Expect.contains
                    (sizedKit surface (sizedRegistry surface true) same)
                    "viewport: the subject's states include two different viewports"
                    "the subject is checked"

            testCase "a declared viewport remounts only when it changes: the kit counts the mounts"
            <| fun () ->
                let surface = SizedSurface()
                let registry = sizedRegistry surface true
                let host = RendererConformance.inProcess registry sizedStates.[0]

                for state in sizedStates do
                    host.Dispatch(RendererMsg.StateChanged state)

                // 1 mount, +0 (same width), +1 (200), +1 (300).
                Expect.equal surface.Mounts 3 "two viewport changes, two remounts"
                Expect.equal surface.Viewport (Some(300.0, 50.0)) "the live mount has the latest viewport"
                host.Dispose()
                Expect.equal surface.Live 0 "released"

            testCase "negative control: a viewport change that only works through a present failure is not a viewport change"
            <| fun () ->
                // Undeclared, the stale viewport raises in Present; the failure recovery mounts again, so every state IS
                // presented at its own viewport, but each change is recorded as a failure.
                let surface = SizedSurface()
                surface.RaiseOnStale <- true
                let failed = sizedKit surface (sizedRegistry surface false) sizedStates

                Expect.equal
                    failed
                    [ "viewport svg: a viewport change is not a failure, so Refusal is untouched" ]
                    "only the refusal check fails: the recovery presented every viewport"

                let declared = SizedSurface()
                declared.RaiseOnStale <- true
                Expect.isEmpty (sizedKit declared (sizedRegistry declared true) sizedStates) "declared, the remount rule is not a failure"

            testCase "negative control: NaN viewports are one viewport, not one per state"
            <| fun () ->
                let surface = SizedSurface()
                let nans = sizedStates |> List.map (fun state -> { state with Width = nan })

                Expect.contains
                    (sizedKit surface (sizedRegistry surface true) nans)
                    "viewport: the subject's states include two different viewports"
                    "NaN equals NaN, so the subject has one viewport"
        ]

namespace FS.GG.Game.Render

/// See RendererConformance.fsi for the contract.
[<Sealed>]
type ConformanceResult internal (check: string, passed: bool, detail: string) =
    member _.Check = check
    member _.Passed = passed
    member _.Detail = detail
    override _.ToString() = sprintf "%s %s%s" (if passed then "PASS" else "FAIL") check (if passed then "" else ": " + detail)

[<Sealed>]
type ConformanceHost<'State>
    internal
    (
        model: unit -> RendererModel<'State>,
        mounted: unit -> RendererBackend option,
        dispatch: RendererMsg<'State> -> unit,
        failBackend: RendererBackend -> string -> unit,
        dispose: unit -> unit
    ) =
    member _.Model() = model ()
    member _.Mounted() = mounted ()
    member _.Dispatch(msg: RendererMsg<'State>) = dispatch msg
    member _.FailBackend (backend: RendererBackend) (reason: string) = failBackend backend reason
    member _.Dispose() = dispose ()

[<Sealed>]
type LateFailureTrigger internal (arm: unit -> unit, settle: Async<bool>) =
    member internal _.Arm() = arm ()
    member internal _.Settle = settle

[<RequireQualifiedAccess>]
module LateFailureTrigger =

    let create (arm: unit -> unit) (settle: Async<bool>) = LateFailureTrigger(arm, settle)

/// Opaque behind the .fsi: built by `RendererConformance.subject` and its `with*` functions only, so a field added
/// later is not a breaking change.
[<NoEquality; NoComparison>]
type ConformanceSubject<'State> =
    {
        Registry: RendererRegistry<'State>
        Expected: RendererBackend list
        States: 'State list
        Start: RendererRegistry<'State> -> 'State -> ConformanceHost<'State>
        Resources: RendererBackend -> int
        Shows: ConformanceHost<'State> -> RendererBackend -> 'State -> Async<Result<unit, string>>
        WaitUntil: (unit -> bool) -> Async<bool>
        LateFailures: Map<RendererBackend, LateFailureTrigger>
    }

/// One mount the instrumented registry handed out: the states it received (newest first, the mount's own state
/// last) and how many times it was disposed.
[<NoEquality; NoComparison>]
type internal ConformanceMount<'State> =
    {
        Backend: RendererBackend
        mutable Received: 'State list
        mutable Disposals: int
    }

[<RequireQualifiedAccess>]
module RendererConformance =

    let host model mounted dispatch failBackend dispose : ConformanceHost<'State> =
        ConformanceHost<'State>(model, mounted, dispatch, failBackend, dispose)

    let inProcess (registry: RendererRegistry<'State>) (state: 'State) : ConformanceHost<'State> =
        let rendererHost = RendererHost.create registry
        let initial, initialEffects = RendererSelection.init registry state
        let model = ref initial
        let pending: RendererMsg<'State> list ref = ref []
        let running = ref false
        let disposed = ref false

        // A message's host feedback goes to the FRONT of the queue, so a fallback completes before anything
        // dispatched later is applied (RendererDriver's order).
        let drain () =
            running.Value <- true

            try
                while not disposed.Value && not pending.Value.IsEmpty do
                    let msg = pending.Value.Head
                    pending.Value <- pending.Value.Tail
                    let next, effects = RendererSelection.update msg model.Value
                    model.Value <- next
                    pending.Value <- rendererHost.Run effects @ pending.Value
            finally
                running.Value <- false

        let dispatch (msg: RendererMsg<'State>) =
            if not disposed.Value then
                pending.Value <- pending.Value @ [ msg ]

                if not running.Value then
                    drain ()

        pending.Value <- rendererHost.Run initialEffects
        drain ()

        host
            (fun () -> model.Value)
            (fun () -> rendererHost.Mounted)
            dispatch
            (fun backend reason ->
                let current = model.Value

                if current.Selected = backend && current.Status = RendererStatus.Active then
                    dispatch (RendererMsg.MountFailed(backend, current.Attempt, reason)))
            (fun () ->
                if not disposed.Value then
                    disposed.Value <- true
                    pending.Value <- []
                    (rendererHost :> System.IDisposable).Dispose())

    let subject registry expected states resources shows : ConformanceSubject<'State> =
        {
            Registry = registry
            Expected = expected
            States = states
            Start = inProcess
            Resources = resources
            Shows = shows
            WaitUntil = fun condition -> async { return condition () }
            LateFailures = Map.empty
        }

    let withStart start (subject: ConformanceSubject<'State>) = { subject with Start = start }

    let withWaitUntil waitUntil (subject: ConformanceSubject<'State>) = { subject with WaitUntil = waitUntil }

    let withLateFailure backend trigger (subject: ConformanceSubject<'State>) =
        { subject with
            LateFailures = Map.add backend trigger subject.LateFailures
        }

    let failures (results: ConformanceResult list) =
        results |> List.filter (fun result -> not result.Passed)

    /// A walk over the offered backends, starting at the default, that switches along every ordered pair at least
    /// once. When the current backend has no unwalked pair left, it moves to the source of the next one.
    let private switchWalk (offered: RendererBackend list) : RendererBackend list =
        let pairs =
            [
                for source in offered do
                    for target in offered do
                        if source <> target then
                            yield source, target
            ]

        let rec walk current remaining acc =
            match remaining with
            | [] -> List.rev acc
            | _ ->
                match remaining |> List.tryFind (fun (source, _) -> source = current) with
                | Some((_, target) as pair) -> walk target (List.filter ((<>) pair) remaining) (target :: acc)
                | None ->
                    let source, _ = List.head remaining
                    walk source remaining (source :: acc)

        walk RendererBackend.defaultBackend pairs []

    let run (subject: ConformanceSubject<'State>) : Async<ConformanceResult list> =
        async {
            let results = ResizeArray<ConformanceResult>()

            let check (name: string) (passed: bool) (detail: string) =
                results.Add(ConformanceResult(name, passed, (if passed then "" else detail)))

            let id = RendererBackend.id
            let mounts = ResizeArray<ConformanceMount<'State>>()

            let instrument (adapter: RendererAdapter<'State>) : RendererAdapter<'State> =
                {
                    Backend = adapter.Backend
                    Mount =
                        fun state ->
                            match adapter.Mount state with
                            | Ok mount ->
                                let record =
                                    {
                                        Backend = adapter.Backend
                                        Received = [ state ]
                                        Disposals = 0
                                    }

                                mounts.Add record

                                Ok
                                    {
                                        Present =
                                            fun next ->
                                                record.Received <- next :: record.Received
                                                mount.Present next
                                        Dispose =
                                            fun () ->
                                                record.Disposals <- record.Disposals + 1
                                                mount.Dispose()
                                    }
                            | Error reason -> Error reason
                }

            let offered = RendererRegistry.available subject.Registry

            // The instrumented copy of the subject's registry. It starts from the instrumented default adapter, so the
            // kit never builds a placeholder default, and a registry built from a ready adapter is held to the same
            // checks as one built from a Scene.
            let registry =
                let adapters =
                    offered
                    |> List.choose (fun backend -> RendererRegistry.tryFind backend subject.Registry)
                    |> List.map instrument

                match adapters |> List.partition (fun adapter -> adapter.Backend = RendererBackend.defaultBackend) with
                | [ svg ], others ->
                    match RendererRegistry.create svg with
                    | Ok created -> others |> List.fold (fun registry adapter -> RendererRegistry.register adapter registry) created
                    | Error reason -> failwith reason
                | _ -> failwith "a registry always offers exactly one default adapter"

            let mountsOf backend =
                mounts |> Seq.filter (fun record -> record.Backend = backend) |> List.ofSeq

            let latest backend = mountsOf backend |> List.tryLast

            let resources () =
                RendererBackend.all
                |> List.map (fun backend -> sprintf "%s=%d" (id backend) (subject.Resources backend))
                |> String.concat " "

            let nothingHeld () =
                RendererBackend.all |> List.forall (fun backend -> subject.Resources backend = 0)

            let describe (host: ConformanceHost<'State>) =
                let model = host.Model()

                sprintf
                    "selected=%s mounted=%s status=%A attempt=%d refusal=%A %s"
                    (id model.Selected)
                    (host.Mounted() |> Option.map id |> Option.defaultValue "none")
                    model.Status
                    model.Attempt
                    model.Refusal
                    (resources ())

            let isOn (host: ConformanceHost<'State>) backend =
                let model = host.Model()

                model.Selected = backend
                && host.Mounted() = Some backend
                && model.Status = RendererStatus.Active

            let select (host: ConformanceHost<'State>) backend =
                host.Dispatch(RendererMsg.SelectBackend backend)

            let stop (host: ConformanceHost<'State>) =
                async {
                    host.Dispose()
                    let! _ = subject.WaitUntil nothingHeld
                    return ()
                }

            let distinct = List.distinct subject.States

            check
                "subject: at least two distinct game states"
                (distinct.Length >= 2)
                (sprintf "%d distinct" distinct.Length)

            check
                "subject: the expected backends include the default SVG"
                (List.contains RendererBackend.Svg subject.Expected)
                (subject.Expected |> List.map id |> String.concat ",")

            match subject.States with
            | [] -> ()
            | initial :: _ ->
                let states = Array.ofList subject.States
                let stateAt index = states.[index % states.Length]

                // registry ---------------------------------------------------------------------------------------
                let expected =
                    RendererBackend.all
                    |> List.filter (fun backend -> List.contains backend subject.Expected)

                check
                    "registry: offers exactly the expected backends, in declared order"
                    (offered = expected)
                    (sprintf
                        "offered=%s expected=%s"
                        (offered |> List.map id |> String.concat ",")
                        (expected |> List.map id |> String.concat ","))

                for backend in RendererBackend.all |> List.filter (fun b -> not (List.contains b offered)) do
                    mounts.Clear()
                    let host = subject.Start registry initial
                    select host backend
                    let model = host.Model()

                    check
                        (sprintf "registry: selecting %s, which has no adapter, is refused and acquires nothing" (id backend))
                        (model.Refusal = Some(RendererRefusal.Unavailable backend)
                         && isOn host RendererBackend.Svg
                         && model.Attempt = 1
                         && mountsOf backend |> List.isEmpty
                         && subject.Resources backend = 0)
                        (describe host)

                    do! stop host

                // round-trip -------------------------------------------------------------------------------------
                for backend in offered do
                    mounts.Clear()
                    let host = subject.Start registry initial

                    if backend <> RendererBackend.Svg then
                        select host backend

                    let modelKept = ResizeArray<string>()
                    let adapterKept = ResizeArray<string>()
                    let shown = ResizeArray<string>()

                    for index in 0 .. states.Length - 1 do
                        let state = states.[index]
                        host.Dispatch(RendererMsg.StateChanged state)

                        if (host.Model()).State <> state then
                            modelKept.Add(sprintf "state %d: %A" index (host.Model()).State)

                        match latest backend with
                        | Some record when record.Disposals = 0 && List.tryHead record.Received = Some state -> ()
                        | Some record ->
                            adapterKept.Add(sprintf "state %d: received %A, disposed %d" index (List.tryHead record.Received) record.Disposals)
                        | None -> adapterKept.Add(sprintf "state %d: %s was never mounted (%s)" index (id backend) (describe host))

                        let! shows = subject.Shows host backend state

                        match shows with
                        | Ok() -> ()
                        | Error detail -> shown.Add(sprintf "state %d: %s" index detail)

                    let name = id backend

                    check
                        (sprintf "round-trip %s: the model returns every presented state unchanged" name)
                        (isOn host backend && modelKept.Count = 0)
                        (describe host + " " + String.concat "; " modelKept)

                    check
                        (sprintf "round-trip %s: the adapter receives every presented state unchanged" name)
                        (adapterKept.Count = 0)
                        (String.concat "; " adapterKept)

                    check
                        (sprintf "round-trip %s: the backend shows every presented state" name)
                        (shown.Count = 0)
                        (String.concat "; " shown)

                    // A view or an output probe that ignores the state would pass the check above for any state: the
                    // probe must reject at least one other state while the last one is shown.
                    let last = states.[states.Length - 1]
                    let rejected = ref []

                    for other in distinct |> List.filter ((<>) last) do
                        let! verdict = subject.Shows host backend other

                        if verdict <> Ok() then
                            rejected.Value <- other :: rejected.Value

                    check
                        (sprintf "round-trip %s: the output probe tells the shown state from another" name)
                        (not rejected.Value.IsEmpty)
                        "the backend's output passes for every other state: the view or the probe ignores the state"

                    do! stop host

                // switch -----------------------------------------------------------------------------------------
                if offered.Length >= 2 then
                    mounts.Clear()
                    let host = subject.Start registry initial
                    let previous = ref RendererBackend.Svg
                    let step = ref 0

                    for backend in switchWalk offered do
                        step.Value <- step.Value + 1
                        let state = stateAt step.Value
                        let name = sprintf "switch %s -> %s (step %d)" (id previous.Value) (id backend) step.Value
                        host.Dispatch(RendererMsg.StateChanged state)
                        select host backend
                        let model = host.Model()

                        let mountedWith =
                            match latest backend with
                            | Some record when record.Disposals = 0 -> List.tryLast record.Received
                            | _ -> None

                        check
                            (name + ": selected, mounted and active with the game state kept")
                            (isOn host backend && model.State = state && mountedWith = Some state)
                            (sprintf "%s mountedWith=%A" (describe host) mountedWith)

                        let others = RendererBackend.all |> List.filter ((<>) backend)

                        let! exclusive =
                            subject.WaitUntil(fun () ->
                                subject.Resources backend >= 1
                                && others |> List.forall (fun other -> subject.Resources other = 0))

                        check (name + ": only the selected backend holds resources") exclusive (resources ())

                        let! shows = subject.Shows host backend state

                        check
                            (name + ": the backend shows the kept state")
                            (shows = Ok())
                            (match shows with
                             | Error detail -> detail
                             | Ok() -> "")

                        previous.Value <- backend

                    let live = mounts |> Seq.filter (fun record -> record.Disposals = 0) |> List.ofSeq

                    check
                        "switch: every superseded mount was disposed exactly once"
                        (live.Length = 1
                         && mounts |> Seq.forall (fun record -> record.Disposals <= 1))
                        (mounts
                         |> Seq.map (fun record -> sprintf "%s:%d" (id record.Backend) record.Disposals)
                         |> String.concat " ")

                    do! stop host

                // dispose ----------------------------------------------------------------------------------------
                for backend in offered do
                    mounts.Clear()
                    let name = id backend
                    let host = subject.Start registry initial

                    if backend <> RendererBackend.Svg then
                        select host backend

                    let! held = subject.WaitUntil(fun () -> subject.Resources backend >= 1)

                    check
                        (sprintf "dispose %s: the resource probe sees the mounted backend" name)
                        (held && isOn host backend)
                        (describe host)

                    if backend <> RendererBackend.Svg then
                        select host RendererBackend.Svg
                        let! released = subject.WaitUntil(fun () -> subject.Resources backend = 0)

                        check
                            (sprintf "dispose %s: switching away releases its resources" name)
                            (released
                             && mountsOf backend |> List.forall (fun record -> record.Disposals = 1))
                            (describe host)

                        select host backend

                    host.Dispose()
                    let! released = subject.WaitUntil nothingHeld

                    check
                        (sprintf "dispose %s: disposing the host releases every resource" name)
                        (released && host.Mounted() = None)
                        (resources ())

                    check
                        (sprintf "dispose %s: every mount was disposed exactly once" name)
                        (mounts.Count >= 1
                         && mounts |> Seq.forall (fun record -> record.Disposals = 1))
                        (mounts
                         |> Seq.map (fun record -> sprintf "%s:%d" (id record.Backend) record.Disposals)
                         |> String.concat " ")

                    let mountsBefore = mounts.Count
                    host.Dispatch(RendererMsg.StateChanged(stateAt 1))
                    select host backend
                    select host RendererBackend.Svg

                    check
                        (sprintf "dispose %s: a disposed host stays inert" name)
                        (mounts.Count = mountsBefore && host.Mounted() = None && nothingHeld ())
                        (sprintf "mounts %d -> %d %s" mountsBefore mounts.Count (resources ()))

                // late failure (host) -----------------------------------------------------------------------------
                for backend in offered |> List.filter ((<>) RendererBackend.defaultBackend) do
                    mounts.Clear()
                    let name = id backend
                    let host = subject.Start registry initial
                    let kept = stateAt 1
                    select host backend
                    host.Dispatch(RendererMsg.StateChanged kept)
                    let live = latest backend
                    let reason = "conformance: late failure of the live mount"
                    host.FailBackend backend reason
                    let model = host.Model()

                    check
                        (sprintf "late failure %s (host): failBackend for the live mount falls back to SVG with the game state intact" name)
                        (live.IsSome
                         && isOn host RendererBackend.Svg
                         && model.State = kept
                         && model.Refusal = Some(RendererRefusal.MountFailed(backend, reason)))
                        (describe host)

                    let! released = subject.WaitUntil(fun () -> subject.Resources backend = 0)

                    check
                        (sprintf "late failure %s (host): the failed backend's resources are released" name)
                        (released
                         && live |> Option.exists (fun record -> record.Disposals = 1))
                        (resources ())

                    let attempt = (host.Model()).Attempt
                    host.FailBackend backend "conformance: report for a backend that is not selected"

                    check
                        (sprintf "late failure %s (host): a report for a backend that is not selected is ignored" name)
                        (isOn host RendererBackend.Svg && (host.Model()).Attempt = attempt)
                        (describe host)

                    select host backend
                    let! shows = subject.Shows host backend kept

                    check
                        (sprintf "late failure %s (host): re-selecting the backend recovers it with the state intact" name)
                        (isOn host backend && (host.Model()).State = kept && shows = Ok())
                        (sprintf "%s shows=%A" (describe host) shows)

                    do! stop host

                // late failure (adapter) --------------------------------------------------------------------------
                for backend in offered |> List.filter ((<>) RendererBackend.defaultBackend) do
                    match Map.tryFind backend subject.LateFailures with
                    | None -> ()
                    | Some trigger ->
                        let name = id backend
                        let kept = stateAt 1

                        // The adapter reports its live mount's failure itself.
                        mounts.Clear()
                        let host = subject.Start registry initial
                        host.Dispatch(RendererMsg.StateChanged kept)
                        trigger.Arm()
                        select host backend
                        let failing = latest backend
                        let! settled = trigger.Settle

                        let! fellBack =
                            subject.WaitUntil(fun () -> isOn host RendererBackend.Svg)

                        let model = host.Model()

                        check
                            (sprintf "late failure %s (adapter): the live mount's own failure report falls back to SVG with the game state intact" name)
                            (settled
                             && fellBack
                             && model.State = kept
                             && (match model.Refusal with
                                 | Some(RendererRefusal.MountFailed(failed, _)) -> failed = backend
                                 | _ -> false))
                            (sprintf "settled=%b %s" settled (describe host))

                        let! released = subject.WaitUntil(fun () -> subject.Resources backend = 0)

                        check
                            (sprintf "late failure %s (adapter): the failed mount's resources are released" name)
                            (released
                             && failing |> Option.exists (fun record -> record.Disposals = 1))
                            (resources ())

                        do! stop host

                        // The live-mount rule: a mount disposed before its failure arrives must not report it, because
                        // the host cannot tell it from the live mount of the same backend.
                        mounts.Clear()
                        let host = subject.Start registry initial
                        host.Dispatch(RendererMsg.StateChanged kept)
                        trigger.Arm()
                        select host backend
                        select host RendererBackend.Svg
                        select host backend
                        let attempt = (host.Model()).Attempt
                        let! settled = trigger.Settle
                        let! _ = subject.WaitUntil(fun () -> subject.Resources backend >= 1)

                        check
                            (sprintf "late failure %s (adapter): a failure of a disposed mount is not reported, and the live mount of the same backend stays" name)
                            (settled
                             && isOn host backend
                             && (host.Model()).Attempt = attempt
                             && (host.Model()).Refusal = None
                             && mountsOf backend |> List.length = 2)
                            (sprintf "settled=%b %s" settled (describe host))

                        do! stop host

            return List.ofSeq results
        }

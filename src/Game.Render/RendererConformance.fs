namespace FS.GG.Game.Render

open FS.GG.UI.Scene

/// See RendererConformance.fsi for the contract.
[<NoComparison>]
type ConformanceResult =
    {
        Check: string
        Passed: bool
        Detail: string
    }

[<NoEquality; NoComparison>]
type ConformanceHost<'State> =
    {
        Model: unit -> RendererModel<'State>
        Mounted: unit -> RendererBackend option
        Dispatch: RendererMsg<'State> -> unit
        FailBackend: RendererBackend -> string -> unit
        Dispose: unit -> unit
    }

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

    let inProcess (registry: RendererRegistry<'State>) (state: 'State) : ConformanceHost<'State> =
        let host = RendererHost.create registry
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
                    pending.Value <- host.Run effects @ pending.Value
            finally
                running.Value <- false

        let dispatch (msg: RendererMsg<'State>) =
            if not disposed.Value then
                pending.Value <- pending.Value @ [ msg ]

                if not running.Value then
                    drain ()

        pending.Value <- host.Run initialEffects
        drain ()

        {
            Model = fun () -> model.Value
            Mounted = fun () -> host.Mounted
            Dispatch = dispatch
            FailBackend =
                fun backend reason ->
                    let current = model.Value

                    if current.Selected = backend && current.Status = RendererStatus.Active then
                        dispatch (RendererMsg.MountFailed(backend, current.Attempt, reason))
            Dispose =
                fun () ->
                    if not disposed.Value then
                        disposed.Value <- true
                        pending.Value <- []
                        (host :> System.IDisposable).Dispose()
        }

    let subject registry expected states resources shows : ConformanceSubject<'State> =
        {
            Registry = registry
            Expected = expected
            States = states
            Start = inProcess
            Resources = resources
            Shows = shows
            WaitUntil = fun condition -> async { return condition () }
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
                results.Add
                    {
                        Check = name
                        Passed = passed
                        Detail = (if passed then "" else detail)
                    }

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

            let registry =
                offered
                |> List.choose (fun backend -> RendererRegistry.tryFind backend subject.Registry)
                |> List.fold
                    (fun registry adapter -> RendererRegistry.register (instrument adapter) registry)
                    // Replaced by the instrumented SVG adapter: every registry offers SVG.
                    (RendererRegistry.svg (fun _ -> ({ Nodes = [] }: Scene)) (fun _ -> Error "placeholder"))

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

                // late failure -----------------------------------------------------------------------------------
                for backend in offered |> List.filter ((<>) RendererBackend.defaultBackend) do
                    mounts.Clear()
                    let name = id backend
                    let host = subject.Start registry initial
                    let kept = stateAt 1

                    // The live-mount rule: an adapter reports a late failure only while its own mount is undisposed.
                    let reportFrom (record: ConformanceMount<'State>) reason =
                        if record.Disposals = 0 then
                            host.FailBackend record.Backend reason
                            true
                        else
                            false

                    select host backend
                    host.Dispatch(RendererMsg.StateChanged kept)
                    let first = latest backend
                    select host RendererBackend.Svg
                    select host backend
                    let second = latest backend

                    match first, second with
                    | Some first, Some second when not (obj.ReferenceEquals(first, second)) ->
                        let attempt = (host.Model()).Attempt
                        let delivered = reportFrom first "conformance: late failure of a disposed mount"

                        check
                            (sprintf "late failure %s: a report from a disposed mount is dropped and the live mount stays" name)
                            (not delivered
                             && first.Disposals = 1
                             && isOn host backend
                             && (host.Model()).Attempt = attempt)
                            (sprintf "delivered=%b %s" delivered (describe host))

                        let reason = "conformance: late failure of the live mount"
                        let delivered = reportFrom second reason
                        let model = host.Model()

                        check
                            (sprintf "late failure %s: a report from the live mount falls back to SVG with the game state intact" name)
                            (delivered
                             && isOn host RendererBackend.Svg
                             && model.State = kept
                             && model.Refusal = Some(RendererRefusal.MountFailed(backend, reason)))
                            (sprintf "delivered=%b %s" delivered (describe host))

                        let! released = subject.WaitUntil(fun () -> subject.Resources backend = 0)

                        check
                            (sprintf "late failure %s: the failed backend's resources are released" name)
                            (released && second.Disposals = 1)
                            (sprintf "disposals=%d %s" second.Disposals (resources ()))

                        let attempt = (host.Model()).Attempt
                        host.FailBackend backend "conformance: report for a backend that is not selected"

                        check
                            (sprintf "late failure %s: a report for a backend that is not selected is ignored" name)
                            (isOn host RendererBackend.Svg && (host.Model()).Attempt = attempt)
                            (describe host)

                        select host backend
                        let! shows = subject.Shows host backend kept

                        check
                            (sprintf "late failure %s: re-selecting the backend recovers it with the state intact" name)
                            (isOn host backend && (host.Model()).State = kept && shows = Ok())
                            (sprintf "%s shows=%A" (describe host) shows)
                    | _ ->
                        check
                            (sprintf "late failure %s: the backend mounts twice for the live-mount rule" name)
                            false
                            (describe host)

                    do! stop host

            return List.ofSeq results
        }

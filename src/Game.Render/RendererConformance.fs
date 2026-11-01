namespace FS.GG.Game.Render

open FS.GG.UI.Scene

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

type MirrorEntry =
    {
        Id: string
        Label: string
        Pressed: bool
    }

[<Sealed>]
type InteractionProbe
    internal
    (
        press: RendererBackend -> Point -> Async<unit>,
        move: RendererBackend -> Point -> Async<unit>,
        focus: RendererBackend -> Async<unit>,
        key: string -> Async<unit>,
        focused: RendererBackend -> string,
        mirror: RendererBackend -> MirrorEntry list option,
        liveWrites: RendererBackend -> int,
        tabIndex: RendererBackend -> string option,
        inputHosts: RendererBackend -> int
    ) =
    member internal _.Press backend point = press backend point
    member internal _.Move backend point = move backend point
    member internal _.Focus backend = focus backend
    member internal _.Key name = key name
    member internal _.Focused backend = focused backend
    member internal _.Mirror backend = mirror backend
    member internal _.LiveWrites backend = liveWrites backend
    member internal _.TabIndex backend = tabIndex backend
    member internal _.InputHosts backend = inputHosts backend

[<RequireQualifiedAccess>]
module InteractionProbe =

    let create press move focus key focused mirror liveWrites tabIndex inputHosts =
        InteractionProbe(press, move, focus, key, focused, mirror, liveWrites, tabIndex, inputHosts)

/// One pixel a backend must show: the pixel containing `Point` (scene coordinates, y down) is the scene colour `Colour`.
[<Sealed>]
type PixelExpectation internal (point: Point, colour: Color) =
    member _.Point = point
    member _.Colour = colour

[<RequireQualifiedAccess>]
module PixelExpectation =

    let create (point: Point) (colour: Color) = PixelExpectation(point, colour)

[<Sealed>]
type PixelProbe
    internal
    (
        read: RendererBackend -> Point list -> Async<(int * int * int) option list>,
        appearance: RendererBackend -> Color -> int * int * int,
        tolerance: RendererBackend -> int
    ) =
    member internal _.Read backend points = read backend points
    member internal _.Appearance backend colour = appearance backend colour
    member internal _.Tolerance backend = tolerance backend

[<RequireQualifiedAccess>]
module PixelProbe =

    let create read appearance tolerance = PixelProbe(read, appearance, tolerance)

[<RequireQualifiedAccess>]
module ScenePixels =

    /// A drawn node with its bounds in scene coordinates.
    type private Drawn =
        | Box of left: float * top: float * right: float * bottom: float * colour: Color
        | Oval of cx: float * cy: float * rx: float * ry: float * colour: Color
        /// A node of a kind the kit does not model (text, lines, paths, images, clips...): it draws somewhere the kit
        /// cannot say, so no pixel it could overdraw is decided.
        | Unmodelled

    let private colourOf drawn =
        match drawn with
        | Box(_, _, _, _, colour)
        | Oval(_, _, _, _, colour) -> colour
        | Unmodelled -> failwith "an unmodelled node has no colour"

    let rec private flatten (dx: float, dy: float) (scene: Scene) (drawn: ResizeArray<Drawn>) : unit =
        for node in scene.Nodes do
            match node with
            | SceneNode.Group inner ->
                for scene in inner do
                    flatten (dx, dy) scene drawn
            | SceneNode.Translate((ox, oy), inner) -> flatten (dx + ox, dy + oy) inner drawn
            | SceneNode.Rectangle((x, y, w, h), fill) ->
                drawn.Add(Box(x + dx, y + dy, x + dx + w, y + dy + h, fill))
            | SceneNode.FilledEllipse(bounds, fill) ->
                drawn.Add(
                    Oval(
                        bounds.X + dx + bounds.Width / 2.0,
                        bounds.Y + dy + bounds.Height / 2.0,
                        bounds.Width / 2.0,
                        bounds.Height / 2.0,
                        fill
                    )
                )
            | SceneNode.Circle(center, radius, fill) -> drawn.Add(Oval(center.X + dx, center.Y + dy, radius, radius, fill))
            | SceneNode.Empty -> ()
            | _ -> drawn.Add Unmodelled

    /// How a node covers the unit pixel `[col, col+1] x [row, row+1]`.
    type private Cover =
        | Full
        | Partial
        | Clear

    let private cover (col: int) (row: int) (drawn: Drawn) : Cover =
        let x0 = float col
        let y0 = float row
        let x1 = x0 + 1.0
        let y1 = y0 + 1.0
        let epsilon = 1e-9

        match drawn with
        | Unmodelled -> Partial
        | Box(l, t, r, b, _) ->
            if x1 <= l + epsilon || x0 >= r - epsilon || y1 <= t + epsilon || y0 >= b - epsilon then
                Clear
            elif x0 >= l - epsilon && x1 <= r + epsilon && y0 >= t - epsilon && y1 <= b + epsilon then
                Full
            else
                Partial
        | Oval(cx, cy, rx, ry, _) ->
            // In the ellipse's own unit space the pixel is still an axis-aligned box.
            let ux0 = (x0 - cx) / rx
            let ux1 = (x1 - cx) / rx
            let uy0 = (y0 - cy) / ry
            let uy1 = (y1 - cy) / ry
            let nearX = if ux0 > 0.0 then ux0 elif ux1 < 0.0 then ux1 else 0.0
            let nearY = if uy0 > 0.0 then uy0 elif uy1 < 0.0 then uy1 else 0.0

            if nearX * nearX + nearY * nearY >= 1.0 - epsilon then
                Clear
            else
                let farX = max (abs ux0) (abs ux1)
                let farY = max (abs uy0) (abs uy1)
                if farX * farX + farY * farY <= 1.0 - 1e-6 then Full else Partial

    /// What a pixel must show, when the scene decides it.
    type private Decided =
        | Colour of Color
        | Nothing
        | Open

    /// The topmost node that wholly covers the pixel decides its colour when it is opaque and no node covers the pixel
    /// in part; a pixel no node touches is empty, which every backend shows as its clear colour (black).
    let private decide (drawn: ResizeArray<Drawn>) (col: int) (row: int) : Decided =
        let mutable top: Color option = None
        let mutable touched = false
        let mutable unsure = false

        for node in drawn do
            match cover col row node with
            | Clear -> ()
            | Partial ->
                // A node that covers the pixel in part, or one the kit cannot place (which may draw anywhere).
                touched <- true
                unsure <- true
            | Full ->
                touched <- true
                let colour = colourOf node

                if colour.Alpha = 255uy then
                    // An opaque node that wholly covers the pixel decides it, whatever was drawn before.
                    top <- Some colour
                    unsure <- false
                else
                    top <- None
                    unsure <- true

        if unsure then Open
        else
            match top with
            | Some colour -> Colour colour
            | None -> if touched then Open else Nothing

    let private ceilInt (value: float) = int (System.Math.Ceiling value)
    let private floorInt (value: float) = int (System.Math.Floor value)

    /// The clear colour: what SVG (transparent), Pixi and Babylon all show where nothing is drawn.
    let private clear: Color =
        {
            Red = 0uy
            Green = 0uy
            Blue = 0uy
            Alpha = 255uy
        }

    let expectations (width: float) (height: float) (scene: Scene) : PixelExpectation list =
        let drawn = ResizeArray<Drawn>()
        flatten (0.0, 0.0) scene drawn
        let columns = floorInt width
        let rows = floorInt height
        let seen = System.Collections.Generic.HashSet<int * int>()
        let found = ResizeArray<PixelExpectation>()

        let sample (col: int) (row: int) =
            if col >= 0 && col < columns && row >= 0 && row < rows && seen.Add((col, row)) then
                let wanted =
                    match decide drawn col row with
                    | Colour colour -> Some colour
                    | Nothing -> Some clear
                    | Open -> None

                match wanted with
                | Some colour ->
                    found.Add(
                        PixelExpectation.create
                            {
                                X = float col + 0.5
                                Y = float row + 0.5
                            }
                            colour
                    )
                | None -> ()

        for node in drawn do
            match node with
            | Box(l, t, r, b, _) ->
                // The part inside the canvas: a node that overhangs it is read at the canvas edge.
                let l = max l 0.0
                let t = max t 0.0
                let r = min r (float columns)
                let b = min b (float rows)

                if r > l && b > t then
                    let midRow = floorInt ((t + b) / 2.0)
                    let midCol = floorInt ((l + r) / 2.0)
                    // The first whole pixel inside and the first whole pixel outside each edge, on the middle row or column.
                    sample midCol midRow
                    sample (ceilInt l) midRow
                    sample (floorInt l - 1) midRow
                    sample (floorInt r - 1) midRow
                    sample (ceilInt r) midRow
                    sample midCol (ceilInt t)
                    sample midCol (floorInt t - 1)
                    sample midCol (floorInt b - 1)
                    sample midCol (ceilInt b)
            | Unmodelled -> ()
            | Oval(cx, cy, rx, ry, _) ->
                // The centre, and the pixel one clear of the extreme on each axis: a round shape's inside is shaded
                // differently by lit backends, so its edge is read from outside, and the pixel the extreme touches
                // takes a share of the shape's colour from a GPU-rasterised SVG (about 13% of a white circle's).
                let midRow = floorInt cy
                let midCol = floorInt cx
                sample midCol midRow
                sample (ceilInt (cx + rx) + 1) midRow
                sample (floorInt (cx - rx) - 2) midRow
                sample midCol (ceilInt (cy + ry) + 1)
                sample midCol (floorInt (cy - ry) - 2)

        List.ofSeq found

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
        Viewport: (('State -> float * float) * (ConformanceHost<'State> -> RendererBackend -> Async<(float * float) option>)) option
        Interaction: (SceneInteraction<'State> * SelectionRoute * InteractionProbe) option
        Pixels: (('State -> PixelExpectation list) * PixelProbe) option
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
            Viewport = None
            Interaction = None
            Pixels = None
        }

    let withStart start (subject: ConformanceSubject<'State>) = { subject with Start = start }

    let withWaitUntil waitUntil (subject: ConformanceSubject<'State>) = { subject with WaitUntil = waitUntil }

    let withLateFailure backend trigger (subject: ConformanceSubject<'State>) =
        { subject with
            LateFailures = Map.add backend trigger subject.LateFailures
        }

    let withViewport expected actual (subject: ConformanceSubject<'State>) =
        { subject with
            Viewport = Some(expected, actual)
        }

    let withInteraction interaction route probe (subject: ConformanceSubject<'State>) =
        { subject with
            Interaction = Some(interaction, route, probe)
        }

    let withPixels expected probe (subject: ConformanceSubject<'State>) =
        { subject with
            Pixels = Some(expected, probe)
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

    /// Points inside an object's bounds, from its centre outwards, at which the kit tries a pointer press.
    let private pressPoints (target: SelectableObject) : Point list =
        let bounds = target.Bounds

        [
            0.5, 0.5
            0.3, 0.3
            0.7, 0.3
            0.3, 0.7
            0.7, 0.7
            0.5, 0.2
            0.5, 0.8
            0.2, 0.5
            0.8, 0.5
        ]
        |> List.map (fun (fx, fy) ->
            {
                X = bounds.X + fx * bounds.Width
                Y = bounds.Y + fy * bounds.Height
            })

    /// A point inside the objects' joint bounds that is at least two units away from every object's bounds, so no
    /// backend can hit an object there through antialiasing or rounding; scanned row by row.
    let private missPoint (objects: SelectableObject list) : Point option =
        match objects with
        | [] -> None
        | _ ->
            let margin = 2.0
            let left = objects |> List.map (fun o -> o.Bounds.X) |> List.min
            let top = objects |> List.map (fun o -> o.Bounds.Y) |> List.min
            let right = objects |> List.map (fun o -> o.Bounds.X + o.Bounds.Width) |> List.max
            let bottom = objects |> List.map (fun o -> o.Bounds.Y + o.Bounds.Height) |> List.max

            let clear (point: Point) =
                objects
                |> List.forall (fun o ->
                    point.X < o.Bounds.X - margin
                    || point.X > o.Bounds.X + o.Bounds.Width + margin
                    || point.Y < o.Bounds.Y - margin
                    || point.Y > o.Bounds.Y + o.Bounds.Height + margin)

            let columns = int (System.Math.Floor(right - left))
            let rows = int (System.Math.Floor(bottom - top))

            seq {
                for row in 1..rows - 1 do
                    for column in 1..columns - 1 do
                        yield
                            {
                                X = left + float column
                                Y = top + float row
                            }
            }
            |> Seq.tryFind clear

    let run (subject: ConformanceSubject<'State>) : Async<ConformanceResult list> =
        async {
            let results = ResizeArray<ConformanceResult>()

            let check (name: string) (passed: bool) (detail: string) =
                results.Add(ConformanceResult(name, passed, (if passed then "" else detail)))

            let id = RendererBackend.id
            let mounts = ResizeArray<ConformanceMount<'State>>()
            // Faults the kit injects around an adapter, each consumed by the next call it names: the adapter's own code is
            // never edited, so what is checked is the contract between the host and any adapter.
            let failNextPresent = ResizeArray<RendererBackend>()
            let failNextMount = ResizeArray<RendererBackend>()

            let instrument (adapter: RendererAdapter<'State>) : RendererAdapter<'State> =
                {
                    Backend = adapter.Backend
                    Mount =
                        fun state ->
                            match (if failNextMount.Remove adapter.Backend then Error "conformance: injected mount failure" else adapter.Mount state) with
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
                                                if failNextPresent.Remove adapter.Backend then
                                                    failwith "conformance: injected present failure"

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
                    | Ok created ->
                        others
                        |> List.fold (fun registry adapter -> RendererRegistry.register adapter registry) created
                        // The declared viewports are part of the registry's contract: keep them on the copy.
                        |> fun copy ->
                            offered
                            |> List.fold
                                (fun registry backend ->
                                    match RendererRegistry.tryViewport backend subject.Registry with
                                    | Some viewport -> RendererRegistry.withViewport backend viewport registry
                                    | None -> registry)
                                copy
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


                // present failure ---------------------------------------------------------------------------------
                for backend in offered do
                    mounts.Clear()
                    let name = id backend
                    let isDefault = backend = RendererBackend.defaultBackend
                    let host = subject.Start registry initial

                    if not isDefault then
                        select host backend

                    // Present `kept` once unarmed, so the armed present below is a present in place even when a
                    // declared viewport changed between the initial state and `kept`.
                    let kept = stateAt 1
                    host.Dispatch(RendererMsg.StateChanged kept)
                    let! _ = subject.WaitUntil(fun () -> subject.Resources backend >= 1)
                    let failing = latest backend
                    let attempt = (host.Model()).Attempt
                    failNextPresent.Add backend
                    host.Dispatch(RendererMsg.StateChanged kept)
                    let consumed = failNextPresent.Count = 0
                    failNextPresent.Clear()
                    let model = host.Model()
                    let recovered = latest RendererBackend.Svg

                    let mountedWithKept =
                        match recovered with
                        | Some record when record.Disposals = 0 -> List.tryLast record.Received = Some kept
                        | _ -> false

                    let causeNamed =
                        match model.Refusal with
                        | Some(RendererRefusal.PresentFailed(failed, reason)) ->
                            failed = backend && reason.Contains "injected present failure"
                        | _ -> false

                    check
                        (sprintf
                            "present failure %s: a throwing Present is contained and %s mounts the latest state"
                            name
                            (if isDefault then "the default backend" else "SVG"))
                        (consumed
                         && isOn host RendererBackend.Svg
                         && model.State = kept
                         && model.Attempt = attempt + 1
                         && causeNamed
                         && mountedWithKept)
                        (sprintf "consumed=%b %s" consumed (describe host))

                    let! released = subject.WaitUntil(fun () -> isDefault || subject.Resources backend = 0)

                    check
                        (sprintf "present failure %s: the failed mount is disposed once and exactly one live mount remains" name)
                        (released
                         && failing |> Option.exists (fun record -> record.Disposals = 1)
                         && mounts |> Seq.filter (fun record -> record.Disposals = 0) |> Seq.length = 1
                         && mounts |> Seq.forall (fun record -> record.Disposals <= 1))
                        (resources ())

                    let! shows = subject.Shows host RendererBackend.Svg kept

                    check
                        (sprintf "present failure %s: the recovered SVG shows the latest state" name)
                        (shows = Ok())
                        (match shows with
                         | Error detail -> detail
                         | Ok() -> "")

                    if isDefault then
                        // The remount fails too: the failure is reported, nothing is held, the next state recovers.
                        let next = stateAt 2
                        host.Dispatch(RendererMsg.StateChanged next)
                        let attemptBefore = (host.Model()).Attempt
                        failNextPresent.Add backend
                        failNextMount.Add backend
                        host.Dispatch(RendererMsg.StateChanged next)
                        let consumedBoth = failNextPresent.Count = 0 && failNextMount.Count = 0
                        failNextPresent.Clear()
                        failNextMount.Clear()
                        let failed = host.Model()
                        let! nothing = subject.WaitUntil nothingHeld

                        check
                            "present failure svg: when the remount fails too, the failure is reported with nothing mounted or held"
                            (consumedBoth
                             && nothing
                             && host.Mounted() = None
                             && failed.State = next
                             && failed.Attempt = attemptBefore + 1
                             && (match failed.Status with
                                 | RendererStatus.Failed((failedBackend, reason) :: _) ->
                                     failedBackend = backend && reason.Contains "injected mount failure"
                                 | _ -> false)
                             && (match failed.Refusal with
                                 | Some(RendererRefusal.MountFailed(failedBackend, reason)) ->
                                     failedBackend = backend && reason.Contains "injected mount failure"
                                 | _ -> false)
                             && (match failed.Status with
                                 | RendererStatus.Failed [ _; (causeBackend, cause) ] ->
                                     causeBackend = backend && cause.Contains "injected present failure"
                                 | _ -> false))
                            (sprintf "consumed=%b %s" consumedBoth (describe host))

                        let after = stateAt 3
                        host.Dispatch(RendererMsg.StateChanged after)
                        let! shows = subject.Shows host RendererBackend.Svg after

                        check
                            "present failure svg: the next state mounts the default again and shows it"
                            (isOn host RendererBackend.Svg && (host.Model()).State = after && shows = Ok())
                            (sprintf "%s shows=%A" (describe host) shows)

                    do! stop host

                // viewport ----------------------------------------------------------------------------------------
                match subject.Viewport with
                | None -> ()
                | Some(expectedViewport, actualViewport) ->
                    // Distinct under `RendererViewport.same`, so a NaN viewport is one viewport, not one per state.
                    let viewports =
                        states
                        |> Array.map expectedViewport
                        |> Array.fold
                            (fun seen viewport ->
                                if seen |> List.exists (RendererViewport.same viewport) then
                                    seen
                                else
                                    viewport :: seen)
                            []
                        |> List.rev

                    check
                        "viewport: the subject's states include two different viewports"
                        (viewports.Length >= 2)
                        (sprintf "%d distinct: %A" viewports.Length viewports)

                    for backend in offered do
                        mounts.Clear()
                        let name = id backend
                        let host = subject.Start registry initial

                        if backend <> RendererBackend.Svg then
                            select host backend

                        // Every state, then back again: a viewport that grows and one that shrinks.
                        let sequence = List.ofArray states @ List.rev (List.ofArray states)
                        let wrong = ResizeArray<string>()
                        let failures = ResizeArray<string>()
                        let refusalBefore = (host.Model()).Refusal

                        for state in sequence do
                            host.Dispatch(RendererMsg.StateChanged state)
                            let! got = actualViewport host backend

                            if not (got |> Option.exists (RendererViewport.same (expectedViewport state))) then
                                wrong.Add(sprintf "state wants %A, backend has %A" (expectedViewport state) got)

                            // A viewport change is not a failure: the model records no refusal for it.
                            if (host.Model()).Refusal <> refusalBefore then
                                failures.Add(sprintf "refusal became %A" (host.Model()).Refusal)

                        check
                            (sprintf "viewport %s: every state is presented at its own viewport, growing and shrinking" name)
                            (wrong.Count = 0 && isOn host backend)
                            (String.concat "; " wrong + " " + describe host)

                        check
                            (sprintf "viewport %s: a viewport change is not a failure, so Refusal is untouched" name)
                            (failures.Count = 0)
                            (String.concat "; " failures)

                        let live = mountsOf backend |> List.filter (fun record -> record.Disposals = 0)

                        check
                            (sprintf "viewport %s: viewport changes leave one live mount, and every superseded mount was disposed once" name)
                            (live.Length = 1 && mountsOf backend |> List.forall (fun record -> record.Disposals <= 1))
                            (mountsOf backend
                             |> List.map (fun record -> string record.Disposals)
                             |> String.concat ",")

                        match RendererRegistry.tryViewport backend subject.Registry with
                        | None -> ()
                        | Some declared ->
                            let changes =
                                (initial :: sequence)
                                |> List.pairwise
                                |> List.filter (fun (before, after) -> not (RendererViewport.same (declared before) (declared after)))
                                |> List.length

                            check
                                (sprintf "viewport %s: the declared viewport replaces the mount exactly when it changes" name)
                                ((mountsOf backend).Length = 1 + changes)
                                (sprintf "%d mounts for %d changes" (mountsOf backend).Length changes)

                        do! stop host

                // pixels -----------------------------------------------------------------------------------------
                match subject.Pixels with
                | None -> ()
                | Some(expectedPixels, pixelProbe) ->
                    let perState = states |> Array.map expectedPixels

                    check
                        "pixels: every state declares at least one pixel to read back"
                        (perState |> Array.forall (fun wanted -> not wanted.IsEmpty))
                        (sprintf
                            "pixels per state: %s"
                            (perState |> Array.map (fun wanted -> string wanted.Length) |> String.concat ","))

                    // One frame: a condition that holds on its second evaluation.
                    let oneFrame () =
                        let calls = ref 0

                        subject.WaitUntil(fun () ->
                            calls.Value <- calls.Value + 1
                            calls.Value > 1)

                    let differences (backend: RendererBackend) (wanted: PixelExpectation list) (got: (int * int * int) option list) =
                        let tolerance = pixelProbe.Tolerance backend

                        if wanted.Length <> got.Length then
                            [ sprintf "the probe returned %d pixels for %d points" got.Length wanted.Length ]
                        else
                            List.zip wanted got
                            |> List.choose (fun (expectation, shown) ->
                                let wantR, wantG, wantB = pixelProbe.Appearance backend expectation.Colour

                                match shown with
                                | Some(r, g, b) when abs (r - wantR) <= tolerance && abs (g - wantG) <= tolerance && abs (b - wantB) <= tolerance ->
                                    None
                                | _ ->
                                    Some(
                                        sprintf
                                            "(%g, %g) wants (%d,%d,%d) shows %A"
                                            expectation.Point.X
                                            expectation.Point.Y
                                            wantR
                                            wantG
                                            wantB
                                            shown
                                    ))

                    // The state's pixels, polled frame by frame: asynchronous backends need frames to draw, and a canvas
                    // that renders when it is presented is read in the task that presented it.
                    let settle (host: ConformanceHost<'State>) backend (state: 'State) (wanted: PixelExpectation list) (frames: int) =
                        async {
                            let points = wanted |> List.map (fun expectation -> expectation.Point)
                            let remaining = ref frames
                            let finished = ref false
                            let last: string list ref = ref [ "not read" ]

                            while not finished.Value do
                                host.Dispatch(RendererMsg.StateChanged state)
                                let! got = pixelProbe.Read backend points
                                last.Value <- differences backend wanted got

                                if last.Value.IsEmpty || remaining.Value <= 0 then
                                    finished.Value <- true
                                else
                                    remaining.Value <- remaining.Value - 1
                                    let! _ = oneFrame ()
                                    ()

                            return last.Value
                        }

                    for backend in offered do
                        let name = id backend
                        let host = subject.Start registry initial

                        if backend <> RendererBackend.Svg then
                            select host backend

                        let wrong = ResizeArray<string>()

                        for index in 0 .. states.Length - 1 do
                            host.Dispatch(RendererMsg.StateChanged states.[index])
                            let! left = settle host backend states.[index] perState.[index] (if index = 0 then 600 else 120)

                            if not left.IsEmpty then
                                wrong.Add(
                                    sprintf
                                        "state %d: %d of %d pixels differ, e.g. %s"
                                        index
                                        left.Length
                                        perState.[index].Length
                                        (left |> List.truncate 3 |> String.concat "; ")
                                )

                        check
                            (sprintf "pixels %s: every state's nodes are drawn at their declared positions and sizes" name)
                            (wrong.Count = 0 && isOn host backend)
                            (String.concat " | " wrong + " " + describe host)

                        do! stop host

                    // Hit regions and drawn pixels agree: at the first whole pixel inside and outside each edge of a box
                    // object, the backend's hit test is the one `SceneInteraction.hitTest` gives, and the drawn pixels
                    // differ, so the object is drawn where it is hit.
                    match subject.Interaction with
                    | None -> ()
                    | Some(interaction, route, probe) ->
                        let objectsOf = SceneInteraction.objects interaction

                        let play =
                            subject.States
                            |> List.tryFind (fun state ->
                                objectsOf state |> List.exists (fun target -> target.Shape = SelectableShape.Box))
                            |> Option.map (SceneInteraction.apply interaction SelectionRequest.Clear)

                        let ceil' (value: float) = int (System.Math.Ceiling value)
                        let floor' (value: float) = int (System.Math.Floor value)

                        let centre (col: int) (row: int) : Point =
                            {
                                X = float col + 0.5
                                Y = float row + 0.5
                            }

                        // (inside, outside) at the middle of each of the four edges.
                        let edges (bounds: Rect) =
                            let midRow = floor' (bounds.Y + bounds.Height / 2.0)
                            let midCol = floor' (bounds.X + bounds.Width / 2.0)

                            [
                                centre (ceil' bounds.X) midRow, centre (floor' bounds.X - 1) midRow
                                centre (floor' (bounds.X + bounds.Width) - 1) midRow, centre (ceil' (bounds.X + bounds.Width)) midRow
                                centre midCol (ceil' bounds.Y), centre midCol (floor' bounds.Y - 1)
                                centre midCol (floor' (bounds.Y + bounds.Height) - 1), centre midCol (ceil' (bounds.Y + bounds.Height))
                            ]

                        for backend in offered do
                            let name = id backend
                            let baseline = RendererBackend.all |> List.map (fun other -> other, probe.InputHosts other) |> Map.ofList
                            let host = subject.Start registry initial
                            let requests = ResizeArray<SelectionRequest>()

                            route
                            |> SelectionRoute.bind (fun request ->
                                requests.Add request

                                host.Dispatch(
                                    RendererMsg.StateChanged(SceneInteraction.apply interaction request (host.Model()).State)
                                ))

                            if backend <> RendererBackend.Svg then
                                select host backend

                            let problems = ResizeArray<string>()

                            match play with
                            | None -> problems.Add "no state has a box object to compare"
                            | Some play ->
                                host.Dispatch(RendererMsg.StateChanged play)
                                let objects = objectsOf play
                                let! attached = subject.WaitUntil(fun () -> probe.InputHosts backend > baseline.[backend])

                                if not attached then
                                    problems.Add "the input host never attached"

                                for target in objects |> List.filter (fun target -> target.Shape = SelectableShape.Box) do
                                    for inside, outside in edges target.Bounds do
                                        let wanted =
                                            [ inside; outside ]
                                            |> List.map (fun point -> point, SceneInteraction.hitTest point objects)

                                        // Drawn: the two pixels differ (the state is presented again, with nothing selected, in
                                        // the task that reads; a backend that draws asynchronously gets a few frames).
                                        let tolerance = pixelProbe.Tolerance backend
                                        let differ = ref false
                                        let remaining = ref 30
                                        let shown: (int * int * int) option list ref = ref []

                                        while not differ.Value && remaining.Value >= 0 do
                                            host.Dispatch(RendererMsg.StateChanged play)
                                            let! got = pixelProbe.Read backend [ inside; outside ]
                                            shown.Value <- got

                                            match got with
                                            | [ Some(r1, g1, b1); Some(r2, g2, b2) ] ->
                                                differ.Value <-
                                                    abs (r1 - r2) > tolerance || abs (g1 - g2) > tolerance || abs (b1 - b2) > tolerance
                                            | _ -> ()

                                            if not differ.Value then
                                                remaining.Value <- remaining.Value - 1
                                                let! _ = oneFrame ()
                                                ()

                                        if not differ.Value then
                                            problems.Add(
                                                sprintf
                                                    "%s: no drawn edge between (%g, %g) and (%g, %g): %A"
                                                    target.Id
                                                    inside.X
                                                    inside.Y
                                                    outside.X
                                                    outside.Y
                                                    shown.Value
                                            )

                                        // Hit: a real press on each lands where the hit test says.
                                        for point, expected in wanted do
                                            host.Dispatch(RendererMsg.StateChanged play)
                                            requests.Clear()
                                            do! probe.Press backend point

                                            let selected = SceneInteraction.selected interaction (host.Model()).State

                                            if selected <> expected then
                                                problems.Add(
                                                    sprintf
                                                        "%s: a press at (%g, %g) selected %A, the hit test says %A"
                                                        target.Id
                                                        point.X
                                                        point.Y
                                                        selected
                                                        expected
                                                )

                            check
                                (sprintf "pixels %s: the hit region and the drawn edge of each box object coincide" name)
                                (problems.Count = 0 && isOn host backend)
                                (String.concat "; " (Seq.truncate 4 problems) + " " + describe host)

                            do! stop host
                            SelectionRoute.unbind route

                // interaction ------------------------------------------------------------------------------------
                match subject.Interaction with
                | None -> ()
                | Some(interaction, route, probe) ->
                    let objectsOf = SceneInteraction.objects interaction
                    let selectedOf = SceneInteraction.selected interaction

                    let invalid =
                        subject.States
                        |> List.indexed
                        |> List.collect (fun (index, state) ->
                            SceneInteraction.problems (objectsOf state)
                            |> List.map (sprintf "state %d: %s" index))

                    // The state the section plays: the first with two or more objects (else one), with its selection
                    // cleared by the game's own rule.
                    let play =
                        subject.States
                        |> List.tryFind (fun state -> (objectsOf state).Length >= 2)
                        |> Option.orElse (subject.States |> List.tryFind (fun state -> not (objectsOf state).IsEmpty))
                        |> Option.map (SceneInteraction.apply interaction SelectionRequest.Clear)

                    check
                        "interaction: the states declare valid selectable objects, and the game's Clear leaves one with objects and nothing selected"
                        (invalid.IsEmpty
                         && (match play with
                             | Some state -> not (objectsOf state).IsEmpty && selectedOf state = None
                             | None -> false))
                        (match play with
                         | None -> "no state declares a selectable object " + String.concat "; " invalid
                         | Some state ->
                             sprintf "selected after Clear=%A %s" (selectedOf state) (String.concat "; " invalid))

                    match play with
                    | None -> ()
                    | Some play ->
                        let objects = objectsOf play

                        let reachable =
                            objects
                            |> List.choose (fun target ->
                                pressPoints target
                                |> List.tryFind (fun point -> SceneInteraction.hitTest point objects = Some target.Id)
                                |> Option.map (fun point -> target, point))

                        let miss = missPoint objects

                        let expectedMirror =
                            objects
                            |> List.map (fun target ->
                                {
                                    Id = target.Id
                                    Label = target.Label
                                    Pressed = false
                                })

                        for backend in offered do
                            let name = id backend
                            mounts.Clear()
                            // What the backends' input hosts hold before this host starts: the census is page-wide, so
                            // what an earlier mount left (a renderer's own listeners on a canvas whose initialisation
                            // failed) is not this host's.
                            let baseline = RendererBackend.all |> List.map (fun other -> other, probe.InputHosts other) |> Map.ofList
                            let host = subject.Start registry initial
                            let requests = ResizeArray<SelectionRequest>()

                            route
                            |> SelectionRoute.bind (fun request ->
                                requests.Add request

                                host.Dispatch(
                                    RendererMsg.StateChanged(SceneInteraction.apply interaction request (host.Model()).State)
                                ))

                            if backend <> RendererBackend.Svg then
                                select host backend

                            host.Dispatch(RendererMsg.StateChanged play)

                            let selectedNow () = selectedOf (host.Model()).State

                            let pressed (target: RendererBackend) =
                                probe.Mirror target
                                |> Option.map (List.filter (fun entry -> entry.Pressed) >> List.map (fun entry -> entry.Id))

                            let observed () =
                                sprintf
                                    "requests=%A selected=%A pressed=%A %s"
                                    (List.ofSeq requests)
                                    (selectedNow ())
                                    (pressed backend)
                                    (describe host)

                            let! attached = subject.WaitUntil(fun () -> probe.InputHosts backend > baseline.[backend])

                            check
                                (sprintf "interaction %s: the input host is attached while the backend is mounted" name)
                                (attached && isOn host backend)
                                (sprintf "inputHosts=%d before=%d %s" (probe.InputHosts backend) baseline.[backend] (describe host))

                            let mirror = probe.Mirror backend

                            check
                                (sprintf "interaction %s: the accessible mirror lists every selectable object in order, with its label, none pressed" name)
                                (mirror = Some expectedMirror)
                                (sprintf "mirror=%A expected=%A" mirror expectedMirror)

                            // A real pointer moving over the surface leaves it in document tab order (tabindex 0): a
                            // renderer that raises it puts the surface ahead of the page.
                            match miss with
                            | Some point -> do! probe.Move backend point
                            | None -> ()

                            do! probe.Move backend { X = 1.0; Y = 1.0 }
                            let tabIndex = probe.TabIndex backend

                            check
                                (sprintf "interaction %s: the surface stays in document tab order (tabindex 0) after a real pointer move" name)
                                (tabIndex = Some "0")
                                (sprintf "tabindex=%A" tabIndex)

                            // Keyboard: two steps from no focus reach the second object (the first of one), Enter
                            // selects it, Escape clears.
                            requests.Clear()
                            do! probe.Focus backend
                            do! probe.Key "ArrowRight"
                            do! probe.Key "ArrowRight"
                            do! probe.Key "Enter"
                            let target = objects.[1 % objects.Length]

                            check
                                (sprintf "interaction %s: the arrows move the keyboard focus in order and Enter selects the focused object in the game state" name)
                                (List.ofSeq requests = [ SelectionRequest.Select target.Id ]
                                 && selectedNow () = Some target.Id
                                 && pressed backend = Some [ target.Id ])
                                (sprintf "want %s; %s" target.Id (observed ()))

                            requests.Clear()
                            do! probe.Key "Escape"

                            check
                                (sprintf "interaction %s: Escape clears the game's selection" name)
                                (List.ofSeq requests = [ SelectionRequest.Clear ]
                                 && selectedNow () = None
                                 && pressed backend = Some [])
                                (observed ())

                            // Pointer: the backend's own hit test on a point of each reachable object.
                            let missed = ResizeArray<string>()

                            for target, point in reachable do
                                let before = selectedNow ()
                                requests.Clear()
                                do! probe.Press backend point

                                let expected =
                                    if before = Some target.Id then
                                        []
                                    else
                                        [ SelectionRequest.Select target.Id ]

                                if
                                    not (
                                        List.ofSeq requests = expected
                                        && selectedNow () = Some target.Id
                                        && pressed backend = Some [ target.Id ]
                                    )
                                then
                                    missed.Add(sprintf "%s at (%g, %g): %s" target.Id point.X point.Y (observed ()))

                            check
                                (sprintf "interaction %s: a pointer press on each object selects it in the game state (hit test)" name)
                                (not reachable.IsEmpty && missed.Count = 0)
                                (if reachable.IsEmpty then
                                     "no object can be reached by a pointer: every press point is covered by another object"
                                 else
                                     String.concat "; " missed)

                            match miss with
                            | None ->
                                check
                                    (sprintf "interaction %s: a pointer press where no object is asks for nothing" name)
                                    true
                                    ""
                            | Some point ->
                                let before = selectedNow ()
                                requests.Clear()
                                do! probe.Press backend point

                                check
                                    (sprintf "interaction %s: a pointer press where no object is asks for nothing" name)
                                    (requests.Count = 0 && selectedNow () = before)
                                    (sprintf "at (%g, %g): %s" point.X point.Y (observed ()))

                            // The mirror, through real focus: Tab from the surface walks the entries in order, and Space
                            // selects the focused one (a single object is cleared first).
                            if objects |> List.forall (fun candidate -> Some candidate.Id = selectedNow ()) then
                                do! probe.Focus backend
                                do! probe.Key "Escape"

                            let entryIndex =
                                objects
                                |> List.findIndexBack (fun candidate -> Some candidate.Id <> selectedNow ())

                            let entry = objects.[entryIndex]
                            requests.Clear()
                            do! probe.Focus backend
                            let walked = ResizeArray<string>()

                            for _ in 0..entryIndex do
                                do! probe.Key "Tab"
                                walked.Add(probe.Focused backend)

                            let control = "control:" + entry.Id
                            let reached = List.ofSeq walked = (objects |> List.take (entryIndex + 1) |> List.map (fun o -> "control:" + o.Id))
                            do! probe.Key " "

                            check
                                (sprintf "interaction %s: Tab walks the mirror entries in order and Space selects the focused object in the game state" name)
                                (reached
                                 && List.ofSeq requests = [ SelectionRequest.Select entry.Id ]
                                 && selectedNow () = Some entry.Id
                                 && pressed backend = Some [ entry.Id ])
                                (sprintf "want %s; tabbed to %A; %s" entry.Id (List.ofSeq walked) (observed ()))

                            // The Present that followed the selection, then unchanged Presents (one, then ten): the
                            // keyboard focus stays on the entry, and the live status is not written again.
                            let afterSelect = probe.Focused backend
                            let state = (host.Model()).State
                            let writes = probe.LiveWrites backend
                            host.Dispatch(RendererMsg.StateChanged state)
                            let afterOne = probe.Focused backend

                            for _ in 1..10 do
                                host.Dispatch(RendererMsg.StateChanged state)

                            let afterTen = probe.Focused backend
                            let unchangedWrites = probe.LiveWrites backend - writes

                            check
                                (sprintf "interaction %s: keyboard focus stays on the mirror entry across the Present of its selection and across unchanged Presents" name)
                                (reached && afterSelect = control && afterOne = control && afterTen = control)
                                (sprintf
                                    "want %s; after the selection %s, after one unchanged Present %s, after ten %s"
                                    control
                                    afterSelect
                                    afterOne
                                    afterTen)

                            check
                                (sprintf "interaction %s: an unchanged Present does not write the live status" name)
                                (unchangedWrites = 0)
                                (sprintf "%d live-status writes over eleven unchanged Presents" unchangedWrites)

                            // The selection is game state: it survives a switch away and back, and the other backend
                            // shows it.
                            let away = offered |> List.tryFind ((<>) backend)

                            match away with
                            | None -> ()
                            | Some away ->
                                let kept = selectedNow ()
                                select host away
                                let awayPressed = pressed away
                                select host backend
                                let backPressed = pressed backend

                                check
                                    (sprintf "interaction %s: the selection survives a switch to %s and back, and both mirrors show it" name (id away))
                                    (kept.IsSome
                                     && awayPressed = Option.map List.singleton kept
                                     && backPressed = Option.map List.singleton kept
                                     && selectedNow () = kept
                                     && isOn host backend)
                                    (sprintf "kept=%A away=%A back=%A %s" kept awayPressed backPressed (describe host))

                            // Disposal: the input host goes with its mount.
                            let! releasedOnSwitch =
                                match away with
                                | None -> async { return true }
                                | Some away ->
                                    select host away
                                    subject.WaitUntil(fun () -> probe.InputHosts backend <= baseline.[backend])

                            let onSwitch = probe.InputHosts backend
                            do! stop host

                            let! released =
                                subject.WaitUntil(fun () ->
                                    RendererBackend.all
                                    |> List.forall (fun other -> probe.InputHosts other <= baseline.[other]))

                            check
                                (sprintf "interaction %s: switching away and disposing the host release the input host (listeners and mirror)" name)
                                (releasedOnSwitch && released)
                                (sprintf
                                    "before %s; after switching away %d; after dispose %s"
                                    (baseline |> Map.toList |> List.map (fun (other, count) -> sprintf "%s=%d" (id other) count) |> String.concat " ")
                                    onSwitch
                                    (RendererBackend.all
                                     |> List.map (fun other -> sprintf "%s=%d" (id other) (probe.InputHosts other))
                                     |> String.concat " "))

                        SelectionRoute.unbind route

            return List.ofSeq results
        }

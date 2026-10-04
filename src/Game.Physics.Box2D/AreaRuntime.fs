namespace FS.GG.Game.Physics.Box2D

open System
open FS.GG.Game.Core
open FS.GG.Game.Physics.Box2D

/// An independent active local world with a whole-body capacity, including static/sensor bodies.
type AreaSettings = { Area: AreaTopology.AreaId; Physics: Settings; Capacity: int }
/// A game command routed to exactly one active local world.
type AreaCommand = { Area: AreaTopology.AreaId; Command: Command }
/// Copied whole-body traversal, published only after source removal and ownership commit.
type Traversal =
    { Tick: int64; Portal: AreaTopology.PortalId; Entity: string
      Source: AreaTopology.AreaId; Destination: AreaTopology.AreaId
      SourcePose: Pose; DestinationPose: Pose }
/// A known refusal preserves source ownership; uncertain engine mutation stops the whole owner.
type TransferRefusal =
    | MissingDestination | CapacityExceeded | DestinationBlocked | JointConnected | StagingRejected of reason: string
/// A refused proposal for one entity during one tick.
type RefusedTraversal = { Entity: string; Portal: AreaTopology.PortalId; Reason: TransferRefusal }
/// Immutable copied observations after all local steps and transfers.
type AreaSnapshot =
    { Tick: int64; Areas: Map<AreaTopology.AreaId, Snapshot>
      Ownership: Map<string, AreaTopology.AreaId>; Traversals: Traversal list; Refused: RefusedTraversal list }
/// Explicit failure disposition. A stopped result is never successful acceptance and may contain
/// staged duplicates; LastKnown is the last fully published immutable state, not a rollback promise.
type AreaStepResult = Advanced of AreaSnapshot | Stopped of reason: string * lastKnown: AreaSnapshot

/// Owns independent Runtime instances and whole-body transfer transactions on one simulation owner.
[<Sealed>]
type AreaRuntime(graph: AreaTopology.Graph, settings: AreaSettings list, hysteresis: float) =
    let configs = settings |> List.map (fun s -> s.Area, s) |> Map.ofList
    do
        if configs.Count <> settings.Length || settings.IsEmpty then invalidArg "settings" "Active areas must be unique and nonempty."
        if not (Double.IsFinite hysteresis) || hysteresis <= 0. then invalidArg "hysteresis" "Hysteresis must be finite and positive."
        if settings |> List.exists (fun s -> s.Capacity < 1) then invalidArg "settings" "Capacity must be positive."
        if settings |> List.exists (fun s -> s.Physics.TickSeconds <> settings.Head.Physics.TickSeconds) then
            invalidArg "settings" "All active worlds must use the same fixed interval."
    let worlds =
        let mutable created = Map.empty
        try
            for KeyValue(area, config) in configs do created <- Map.add area (new Runtime(config.Physics)) created
            created
        with _ ->
            for KeyValue(_, world) in created do (world :> IDisposable).Dispose()
            reraise ()
    let mutable disposed = false
    let mutable stopped = false
    let mutable commitFault: (unit -> unit) option = None
    let mutable tick = 0L
    let mutable blocked: Map<string, AreaTopology.PortalId * AreaTopology.Opening * float> = Map.empty
    let snapshots () = worlds |> Map.map (fun _ world -> world.Snapshot())
    let ownership areas =
        areas |> Map.toList |> List.collect (fun (area, snapshot: Snapshot) -> snapshot.Bodies |> Map.toList |> List.map (fun (entity, _) -> entity, area)) |> Map.ofList
    let initialAreas = snapshots ()
    let mutable last = { Tick = 0L; Areas = initialAreas; Ownership = Map.empty; Traversals = []; Refused = [] }
    let ensureActive () =
        if disposed then raise (ObjectDisposedException(nameof AreaRuntime))
        if stopped then invalidOp "Area simulation is stopped; dispose it."
    let radius (descriptor: BodyDescriptor) = match descriptor.Shape with Circle r -> r | Box(w, h) -> sqrt (w * w + h * h)
    let pose (descriptor: BodyDescriptor) : Pose =
        { Position = descriptor.Position; Rotation = descriptor.Rotation
          LinearVelocity = descriptor.LinearVelocity; AngularVelocity = descriptor.AngularVelocity }
    let clear (point: Point) (opening: AreaTopology.Opening) clearance =
        let dx, dy = opening.Finish.X - opening.Start.X, opening.Finish.Y - opening.Start.Y
        let length2 = dx * dx + dy * dy
        let along = max 0. (min 1. (((point.X - opening.Start.X) * dx + (point.Y - opening.Start.Y) * dy) / length2))
        let x, y = opening.Start.X + along * dx, opening.Start.Y + along * dy
        let px, py = point.X - x, point.Y - y
        px * px + py * py > clearance * clearance
    let placementAllowed destination descriptor =
        let current = worlds[destination].Snapshot()
        current.Bodies |> Map.forall (fun entity _ ->
            let other, _, _ = worlds[destination].Describe entity
            if other.IsSensor || descriptor.IsSensor then true
            else
                let x, y = other.Position.X - descriptor.Position.X, other.Position.Y - descriptor.Position.Y
                let sum = radius descriptor + radius other
                x * x + y * y >= sum * sum)
    let stop reason =
        stopped <- true
        worlds |> Map.iter (fun _ world -> world.Stop())
        Stopped(reason, last)

    member _.Snapshot() = ensureActive (); last
    member _.InspectStopped() =
        if disposed then raise (ObjectDisposedException(nameof AreaRuntime))
        if not stopped then invalidOp "Inspection is for a stopped owner."
        worlds |> Map.map (fun _ world -> world.Inspect())
    member internal _.CommitFault with set value = commitFault <- value
    member internal _.SetAwake(area, entity, awake) = ensureActive (); worlds[area].SetAwake(entity, awake)
    member internal _.BodyState(area, entity) = ensureActive (); worlds[area].Describe entity
    member internal _.LocalTicks = worlds |> Map.map (fun _ world -> world.Snapshot().Tick)

    member _.Step(commands: AreaCommand list) =
        ensureActive ()
        // Validate the entire global batch before stepping any local world.
        let batches = worlds |> Map.map (fun area _ -> commands |> List.choose (fun c -> if c.Area = area then Some c.Command else None))
        for command in commands do
            if not (Map.containsKey command.Area worlds) then invalidArg "commands" "Unknown active area."
        let mutable ids = last.Ownership |> Map.map (fun _ _ -> ())
        let mutable removed = Set.empty
        for command in commands do
            match command.Command with
            | CreateBody body ->
                if Map.containsKey body.Entity ids || Set.contains body.Entity removed then invalidArg "commands" "Duplicate/reused global entity ID."
                ids <- Map.add body.Entity () ids
            | RemoveBody entity -> ids <- Map.remove entity ids; removed <- Set.add entity removed
            | _ -> ()
        worlds |> Map.iter (fun area world -> world.ValidateBatch batches[area])
        for KeyValue(area, world) in worlds do
            let mutable count = world.Snapshot().Bodies.Count
            for command in batches[area] do
                match command with
                | CreateBody _ -> count <- count + 1
                | RemoveBody _ -> count <- count - 1
                | _ -> ()
                if count > configs[area].Capacity then invalidArg "commands" "Area capacity exceeded."
        try
            let previous = snapshots ()
            let stepped = worlds |> Map.map (fun area world -> world.Step batches[area])
            tick <- tick + 1L
            let mutable refusals = []
            let mutable traversals = []
            // Collect every proposal before any world is mutated. Stable area/entity ordering.
            let candidates =
                [ for KeyValue(area, snapshot) in stepped do
                    for KeyValue(entity, current) in snapshot.Bodies do
                        let descriptor, awake, jointed = worlds[area].Describe entity
                        let prior =
                            match Map.tryFind entity previous[area].Bodies with
                            | Some old -> Some old.Position
                            | None -> commands |> List.tryPick (fun c ->
                                match c.Command with
                                | CreateBody body when c.Area = area && body.Entity = entity -> Some body.Position
                                | _ -> None)
                        match Map.tryFind entity blocked with
                        | Some (_, opening, clearance) when prior |> Option.exists (fun position -> clear position opening clearance) -> blocked <- Map.remove entity blocked
                        | _ -> ()
                        let excluded = match Map.tryFind entity blocked with Some (id, _, _) -> Set.singleton id | None -> Set.empty
                        match prior with
                        | None -> ()
                        | Some position ->
                            let motion : AreaTopology.Motion =
                                { Position = current.Position; Velocity = current.LinearVelocity
                                  Orientation = current.Rotation; AngularVelocity = current.AngularVelocity }
                            match AreaTopology.tryCrossing graph area excluded (radius descriptor) position motion with
                            | Some crossing -> yield entity, descriptor, awake, jointed, crossing
                            | None -> () ]
            for entity, descriptor, awake, jointed, crossing in candidates do
                let portal = crossing.Portal
                let mapped = crossing.MappedMotion
                let destination =
                    { descriptor with Position = mapped.Position; Rotation = mapped.Orientation
                                      LinearVelocity = mapped.Velocity; AngularVelocity = mapped.AngularVelocity }
                let refusal =
                    if jointed then Some JointConnected
                    elif not (Map.containsKey portal.Destination worlds) then Some MissingDestination
                    elif worlds[portal.Destination].Snapshot().Bodies.Count >= configs[portal.Destination].Capacity then Some CapacityExceeded
                    elif not (placementAllowed portal.Destination destination) then Some DestinationBlocked
                    else None
                let mutable rejection = refusal
                if rejection.IsNone then
                    try worlds[portal.Destination].ValidateBatch [CreateBody destination]
                    with :? ArgumentException as error -> rejection <- Some (StagingRejected error.Message)
                match rejection with
                | Some reason -> refusals <- { Entity = entity; Portal = portal.Id; Reason = reason } :: refusals
                | None ->
                    let target, source = worlds[portal.Destination], worlds[portal.Source]
                    // Keep source until destination body/fixture/awake state exist. Never step staged bodies.
                    target.Stage(destination, awake)
                    try
                        commitFault |> Option.iter (fun fault -> fault ())
                        source.RemoveWithoutStep entity
                    with _ ->
                        try target.RemoveWithoutStep entity
                        with _ -> target.Stop()
                        reraise ()
                    let actual, actualAwake, _ = target.Describe entity
                    if actualAwake <> awake && descriptor.Kind = Dynamic then invalidOp "Destination awake state differs; simulation stopped."
                    let finalPose = pose actual
                    traversals <- { Tick = tick; Portal = portal.Id; Entity = entity
                                    Source = portal.Source; Destination = portal.Destination
                                    SourcePose = pose descriptor; DestinationPose = finalPose } :: traversals
                    match portal.Reverse with
                    | Some reverse -> blocked <- Map.add entity (reverse, portal.DestinationOpening, radius descriptor + hysteresis) blocked
                    | None -> blocked <- Map.remove entity blocked
            let areas = snapshots () |> Map.map (fun area snapshot -> { snapshot with Events = stepped[area].Events })
            let owners = ownership areas
            if owners.Count <> (areas |> Map.toSeq |> Seq.sumBy (fun (_, snapshot) -> snapshot.Bodies.Count)) then
                invalidOp "Entity ownership is duplicated; simulation stopped."
            blocked <- blocked |> Map.filter (fun entity _ -> Map.containsKey entity owners)
            last <- { Tick = tick; Areas = areas; Ownership = owners; Traversals = List.rev traversals; Refused = List.rev refusals }
            Advanced last
        with error -> stop error.Message

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                for KeyValue(_, world) in worlds do (world :> IDisposable).Dispose()
                disposed <- true

/// Portal-aware presentation snaps transferred entities and interpolates others normally.
[<RequireQualifiedAccess>]
module AreaPhysics =
    let interpolate alpha (previous: AreaSnapshot) (current: AreaSnapshot) =
        if not (Double.IsFinite alpha) || alpha < 0. || alpha > 1. then invalidArg "alpha" "Alpha must be in [0,1]."
        if current.Tick <> previous.Tick + 1L then invalidArg "current" "Snapshots must be consecutive."
        let moved = current.Traversals |> List.map _.Entity |> Set.ofList
        current.Areas |> Map.map (fun area snapshot ->
            match Map.tryFind area previous.Areas with
            | None -> snapshot.Bodies
            | Some old ->
                let ordinary = Physics.interpolate alpha old snapshot
                snapshot.Bodies |> Map.map (fun entity pose ->
                    if Set.contains entity moved || Map.tryFind entity previous.Ownership <> Some area then pose
                    else ordinary[entity]))

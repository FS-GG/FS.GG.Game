namespace FS.GG.Game.Physics.Box2D

open System
open Box2D.NET
open FS.GG.Game.Core

module Validation =
    let scalar name value =
        if not (Double.IsFinite value) || not (Single.IsFinite(float32 value)) then
            invalidArg name "Value must be finite and representable in single precision."
    let positive name value =
        scalar name value
        if value <= 0. || float32 value <= 0.f then invalidArg name "Value must be positive."
    let nonnegative name value =
        scalar name value
        if value < 0. then invalidArg name "Value must be nonnegative."
    let point name (value: Point) = scalar name value.X; scalar name value.Y
    let identifier name value =
        if String.IsNullOrWhiteSpace value then invalidArg name "ID must be nonempty."
    let body (body: BodyDescriptor) =
        identifier "Entity" body.Entity
        point "Position" body.Position
        point "LinearVelocity" body.LinearVelocity
        scalar "Rotation" body.Rotation
        scalar "AngularVelocity" body.AngularVelocity
        nonnegative "Density" body.Density
        if body.Kind = Dynamic then positive "Density" body.Density
        nonnegative "Friction" body.Friction
        nonnegative "Restitution" body.Restitution
        match body.Shape with
        | Circle radius -> positive "Radius" radius
        | Box (width, height) -> positive "HalfWidth" width; positive "HalfHeight" height

// The pinned engine uses a process-global world slot table. Allocation scans inUse then clears
// and initializes the slot without synchronization; concurrent constructors can own the same slot.
// Keep lifetime mutations serialized across adapter instances, while independent worlds may step.
module WorldLifetime =
    let gate = obj ()

[<Sealed>]
type Runtime(settings: Settings) =
    let vec (point: Point) = B2Vec2(float32 point.X, float32 point.Y)
    let point (value: B2Vec2) : Point = { X = float value.X; Y = float value.Y }
    let key (id: B2ShapeId) = struct(id.index1, id.world0, id.generation)
    let world =
        Validation.point "Gravity" settings.Gravity
        Validation.positive "TickSeconds" settings.TickSeconds
        if settings.Substeps < 1 || settings.Substeps > 64 then
            invalidArg "Substeps" "Substeps must be in [1,64]."
        let mutable definition = B2Types.b2DefaultWorldDef()
        definition.gravity <- vec settings.Gravity
        lock WorldLifetime.gate (fun () ->
            let id = B2Worlds.b2CreateWorld(&definition)
            if id.index1 = 0us then invalidOp "Box2D world capacity is exhausted."
            id)
    let mutable bodies: Map<string, B2BodyId * B2ShapeId * BodyDescriptor> = Map.empty
    let mutable joints: Map<string, B2JointId * DistanceJointDescriptor> = Map.empty
    let mutable shapes: Map<struct(int * uint16 * uint16), string> = Map.empty
    let mutable disposed = false
    let mutable stopped = false
    let mutable tick = 0L

    let ensureActive () =
        if disposed then raise (ObjectDisposedException(nameof Runtime))
        if stopped then invalidOp "An engine operation failed; this runtime is stopped. Dispose it."

    let capture events =
        let poses = bodies |> Map.map (fun _ (id, _, _) ->
            let rotation = B2Bodies.b2Body_GetRotation id
            { Position = point (B2Bodies.b2Body_GetPosition id)
              Rotation = float (B2MathFunction.b2Rot_GetAngle(&rotation))
              LinearVelocity = point (B2Bodies.b2Body_GetLinearVelocity id)
              AngularVelocity = float (B2Bodies.b2Body_GetAngularVelocity id) })
        { Tick = tick; Bodies = poses; Events = events }

    let validate commands =
        let mutable declaredBodies = bodies |> Map.map (fun _ (_, _, descriptor) -> descriptor.Kind)
        let mutable declaredJoints = joints |> Map.map (fun _ (_, descriptor) -> descriptor)
        let mutable removed = Set.empty
        let requireBody entity =
            Validation.identifier "entity" entity
            if not (Map.containsKey entity declaredBodies) then invalidArg "commands" ("Unknown body: " + entity)
        for command in commands do
            match command with
            | CreateBody descriptor ->
                Validation.body descriptor
                if Map.containsKey descriptor.Entity declaredBodies || Set.contains descriptor.Entity removed then
                    invalidArg "commands" ("Duplicate/reused body ID: " + descriptor.Entity)
                declaredBodies <- Map.add descriptor.Entity descriptor.Kind declaredBodies
            | RemoveBody entity ->
                requireBody entity
                declaredBodies <- Map.remove entity declaredBodies
                declaredJoints <- Map.filter (fun _ joint -> joint.EntityA <> entity && joint.EntityB <> entity) declaredJoints
                removed <- Set.add entity removed
            | ApplyForce (entity, value) | ApplyImpulse (entity, value) ->
                requireBody entity
                Validation.point "force/impulse" value
                if declaredBodies[entity] <> Dynamic then invalidArg "commands" "Force/impulse requires a dynamic body."
            | CreateDistanceJoint descriptor ->
                Validation.identifier "Joint" descriptor.Joint
                Validation.positive "Length" descriptor.Length
                requireBody descriptor.EntityA
                requireBody descriptor.EntityB
                if descriptor.EntityA = descriptor.EntityB then invalidArg "commands" "Joint endpoints must differ."
                if declaredBodies[descriptor.EntityA] <> Dynamic && declaredBodies[descriptor.EntityB] <> Dynamic then
                    invalidArg "commands" "A joint must include a dynamic body."
                if Map.containsKey descriptor.Joint declaredJoints then invalidArg "commands" "Duplicate joint ID."
                declaredJoints <- Map.add descriptor.Joint descriptor declaredJoints
            | RemoveJoint joint ->
                if not (Map.containsKey joint declaredJoints) then invalidArg "commands" ("Unknown joint: " + joint)
                declaredJoints <- Map.remove joint declaredJoints

    let apply command =
        match command with
        | CreateBody descriptor ->
            let mutable bodyDef = B2Types.b2DefaultBodyDef()
            bodyDef.``type`` <-
                match descriptor.Kind with
                | Static -> B2BodyType.b2_staticBody
                | Kinematic -> B2BodyType.b2_kinematicBody
                | Dynamic -> B2BodyType.b2_dynamicBody
            bodyDef.position <- vec descriptor.Position
            bodyDef.rotation <- B2MathFunction.b2MakeRot(float32 descriptor.Rotation)
            bodyDef.linearVelocity <- vec descriptor.LinearVelocity
            bodyDef.angularVelocity <- float32 descriptor.AngularVelocity
            let id = B2Bodies.b2CreateBody(world, &bodyDef)
            try
                let mutable shapeDef = B2Types.b2DefaultShapeDef()
                shapeDef.density <- float32 descriptor.Density
                shapeDef.material.friction <- float32 descriptor.Friction
                shapeDef.material.restitution <- float32 descriptor.Restitution
                shapeDef.isSensor <- descriptor.IsSensor
                shapeDef.enableSensorEvents <- true
                shapeDef.enableContactEvents <- true
                let shape =
                    match descriptor.Shape with
                    | Circle radius ->
                        let geometry = B2Circle(B2Vec2(0.f, 0.f), float32 radius)
                        B2Shapes.b2CreateCircleShape(id, &shapeDef, &geometry)
                    | Box (width, height) ->
                        let geometry = B2Geometries.b2MakeBox(float32 width, float32 height)
                        B2Shapes.b2CreatePolygonShape(id, &shapeDef, &geometry)
                bodies <- Map.add descriptor.Entity (id, shape, descriptor) bodies
                shapes <- Map.add (key shape) descriptor.Entity shapes
            with _ ->
                // Retain the source during staging; clean this known partial allocation before propagating.
                B2Bodies.b2DestroyBody id
                bodies <- Map.remove descriptor.Entity bodies
                shapes <- Map.filter (fun _ entity -> entity <> descriptor.Entity) shapes
                reraise ()
        | RemoveBody entity ->
            let (id, _, _) = bodies[entity]
            B2Bodies.b2DestroyBody id
            bodies <- Map.remove entity bodies
            joints <- Map.filter (fun _ (_, joint) -> joint.EntityA <> entity && joint.EntityB <> entity) joints
        | ApplyForce (entity, value) ->
            let (id, _, _) = bodies[entity]
            B2Bodies.b2Body_ApplyForceToCenter(id, vec value, true)
        | ApplyImpulse (entity, value) ->
            let (id, _, _) = bodies[entity]
            B2Bodies.b2Body_ApplyLinearImpulseToCenter(id, vec value, true)
        | CreateDistanceJoint descriptor ->
            let (idA, _, _) = bodies[descriptor.EntityA]
            let (idB, _, _) = bodies[descriptor.EntityB]
            let mutable definition = B2Joints.b2DefaultDistanceJointDef()
            definition.``base``.bodyIdA <- idA
            definition.``base``.bodyIdB <- idB
            definition.length <- float32 descriptor.Length
            let id = B2Joints.b2CreateDistanceJoint(world, &definition)
            joints <- Map.add descriptor.Joint (id, descriptor) joints
        | RemoveJoint joint ->
            let (id, _) = joints[joint]
            B2Joints.b2DestroyJoint(id, true)
            joints <- Map.remove joint joints

    let copyEvents () =
        let resolve shape =
            match Map.tryFind (key shape) shapes with
            | Some entity -> entity
            | None -> invalidOp "Engine event referenced an unknown shape; simulation stopped."
        let pair a b =
            let a, b = resolve a, resolve b
            if StringComparer.Ordinal.Compare(a, b) <= 0 then a, b else b, a
        let sensor = B2Worlds.b2World_GetSensorEvents world
        let contact = B2Worlds.b2World_GetContactEvents world
        [ for i in 0 .. sensor.beginCount - 1 do
              let e = sensor.beginEvents[i]
              yield SensorEntered(resolve e.sensorShapeId, resolve e.visitorShapeId)
          for i in 0 .. sensor.endCount - 1 do
              let e = sensor.endEvents[i]
              yield SensorExited(resolve e.sensorShapeId, resolve e.visitorShapeId)
          for i in 0 .. contact.beginCount - 1 do
              let e = contact.beginEvents[i]
              yield ContactStarted(pair e.shapeIdA e.shapeIdB)
          for i in 0 .. contact.endCount - 1 do
              let e = contact.endEvents[i]
              yield ContactEnded(pair e.shapeIdA e.shapeIdB) ] |> List.sort

    member internal _.ValidateBatch(commands: FS.GG.Game.Physics.Box2D.Command list) = ensureActive (); validate commands
    member internal _.IsStopped = stopped
    member internal _.Inspect() = if disposed then None else try Some (capture []) with _ -> None
    member internal _.Stop() = stopped <- true
    member internal _.Describe(entity) =
        ensureActive ()
        let (id, _, descriptor) = bodies[entity]
        let pose = (capture []).Bodies[entity]
        let current = { descriptor with Position = pose.Position; Rotation = pose.Rotation
                                        LinearVelocity = pose.LinearVelocity; AngularVelocity = pose.AngularVelocity }
        current, B2Bodies.b2Body_IsAwake id,
            (joints |> Map.exists (fun _ (_, joint) -> joint.EntityA = entity || joint.EntityB = entity))
    member internal _.Stage(descriptor, awake) =
        ensureActive ()
        validate [CreateBody descriptor]
        try
            apply (CreateBody descriptor)
            let (id, _, _) = bodies[descriptor.Entity]
            // Staging must not accumulate b2MakeRot's approximate sine error on every traversal.
            let rotation = B2Rot(float32 (cos descriptor.Rotation), float32 (sin descriptor.Rotation))
            B2Bodies.b2Body_SetTransform(id, vec descriptor.Position, rotation)
            B2Bodies.b2Body_SetAwake(id, awake)
        with _ -> stopped <- true; reraise ()
    member internal _.RemoveWithoutStep(entity) =
        ensureActive ()
        validate [RemoveBody entity]
        try apply (RemoveBody entity)
        with _ -> stopped <- true; reraise ()
    member internal _.SetAwake(entity, awake) =
        ensureActive ()
        let (id, _, _) = bodies[entity]
        B2Bodies.b2Body_SetAwake(id, awake)

    member _.Settings = settings
    member _.Snapshot() = ensureActive (); capture []
    member _.Step(commands) =
        ensureActive ()
        validate commands
        try
            commands |> List.iter apply
            B2Worlds.b2World_Step(world, float32 settings.TickSeconds, settings.Substeps)
            tick <- tick + 1L
            let snapshot = capture (copyEvents ())
            shapes <- bodies |> Map.toSeq |> Seq.map (fun (entity, (_, shape, _)) -> key shape, entity) |> Map.ofSeq
            snapshot
        with _ ->
            stopped <- true
            reraise ()

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                lock WorldLifetime.gate (fun () -> B2Worlds.b2DestroyWorld world)
                disposed <- true
                bodies <- Map.empty
                joints <- Map.empty
                shapes <- Map.empty

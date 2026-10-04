module Game.Physics.Box2D.Tests.AreaRuntimeTests

open System
open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Physics.Box2D

module Scene = Box2D.Portals.Scene
let private p x y : Point = { X = x; Y = y }
let private advance = Scene.advance
let private command area cmd : AreaCommand = { Area = area; Command = cmd }
let private configuration = Scene.settings |> List.map (fun s -> { s with Physics = { s.Physics with Gravity = p 0. 0. } })
let private get = function Ok value -> value | Error reason -> failwith reason
let private checkRefusal reason (snapshot: AreaSnapshot) =
    Expect.equal snapshot.Refused.Head.Reason reason "declared refusal"
    Expect.equal snapshot.Ownership.[Scene.moving.Entity] Scene.areaA "source ownership retained"
    Expect.isEmpty snapshot.Traversals "no false success"
let private near a b = Expect.isLessThan (abs (a - b)) 2e-5 "single precision conversion tolerance"

[<Tests>]
let tests = testList "Box2D AreaRuntime" [
    test "real rotated two-area scene preserves identity momentum once-step and exact replay" {
        let observed = Scene.qualify ()
        Expect.equal observed.Length 5 "actual headless scene"
    }
    test "whole supported descriptor and awake state survive non-stepping stage" {
        use source = new Runtime(configuration.Head.Physics)
        use destination = new Runtime(configuration.Head.Physics)
        let body = { Scene.moving with LinearVelocity = p 0. 0.; AngularVelocity = 0.; Shape = Box(0.2, 0.3) }
        source.Step [CreateBody body] |> ignore
        source.SetAwake(body.Entity, false)
        let descriptor, awake, jointed = source.Describe body.Entity
        Expect.isFalse awake "engine source is asleep"
        Expect.isFalse jointed "unjointed"
        destination.Stage(descriptor, awake)
        source.RemoveWithoutStep body.Entity
        let actual, actualAwake, _ = destination.Describe body.Entity
        Expect.equal { actual with Rotation = descriptor.Rotation } descriptor "shape/material/kind/sensor/pose/momentum preserved"
        near (Math.IEEERemainder(actual.Rotation - descriptor.Rotation, 2. * Math.PI)) 0.
        Expect.equal actualAwake awake "engine sleeping state preserved"
        Expect.equal (source.Snapshot().Tick, destination.Snapshot().Tick) (1L, 0L) "mutation never steps"
        Expect.isEmpty (source.Snapshot().Bodies) "source gone after commit"
    }
    test "missing destination preserves source after its ordinary step" {
        use runtime = new AreaRuntime(Scene.graph, [configuration.Head], 0.05)
        let snapshot = runtime.Step [command Scene.areaA (CreateBody Scene.moving)] |> advance
        checkRefusal MissingDestination snapshot
    }
    test "capacity failure preserves source and resident" {
        let configs = configuration |> List.map (fun s -> if s.Area = Scene.areaB then { s with Capacity = 1 } else s)
        use runtime = new AreaRuntime(Scene.graph, configs, 0.05)
        let resident = { Physics.circle "resident" 0.1 (p 30. 30.) with Kind = Static }
        let snapshot = runtime.Step [command Scene.areaA (CreateBody Scene.moving); command Scene.areaB (CreateBody resident)] |> advance
        checkRefusal CapacityExceeded snapshot
        Expect.equal snapshot.Ownership.Count 2 "both entities retained"
    }
    test "blocked destination placement preserves source" {
        use runtime = new AreaRuntime(Scene.graph, configuration, 0.05)
        let resident = { Physics.circle "resident" 0.5 (p 9.9 5.15) with Kind = Static }
        let snapshot = runtime.Step [command Scene.areaA (CreateBody Scene.moving); command Scene.areaB (CreateBody resident)] |> advance
        checkRefusal DestinationBlocked snapshot
    }
    test "joint-connected crossing refuses without implicitly breaking joint" {
        use runtime = new AreaRuntime(Scene.graph, configuration, 0.05)
        let anchor = { Physics.circle "anchor" 0.1 (p -0.25 -1.) with Kind = Static }
        let body = { Scene.moving with LinearVelocity = p 10. 0.; AngularVelocity = 0. }
        let snapshot = runtime.Step [command Scene.areaA (CreateBody anchor); command Scene.areaA (CreateBody body)
                                     command Scene.areaA (CreateDistanceJoint { Joint = "joint"; EntityA = anchor.Entity; EntityB = body.Entity; Length = 1. })] |> advance
        checkRefusal JointConnected snapshot
        let _, _, jointed = runtime.BodyState(Scene.areaA, body.Entity)
        Expect.isTrue jointed "joint remains attached"
    }
    test "actual staging descriptor precision refusal preserves source without destination allocation" {
        let mapping = AreaTopology.rigidMap 0. (p 1e39 0.) |> get
        let portal = { Scene.forward with Mapping = mapping; Reverse = None
                                          DestinationOpening = { Start = p 1e39 -2.; Finish = p 1e39 2. } }
        let graph = AreaTopology.create [Scene.areaA; Scene.areaB] [portal] |> get
        use runtime = new AreaRuntime(graph, configuration, 0.05)
        let snapshot = runtime.Step [command Scene.areaA (CreateBody Scene.moving)] |> advance
        match snapshot.Refused.Head.Reason with StagingRejected _ -> () | reason -> failtestf "Expected staging refusal, got %A" reason
        Expect.equal snapshot.Ownership.[Scene.moving.Entity] Scene.areaA "source retained"
        Expect.isEmpty snapshot.Areas.[Scene.areaB].Bodies "no staged allocation"
    }
    test "injected commit failure cleans real staged destination and stops with actual observations" {
        use runtime = new AreaRuntime(Scene.graph, configuration, 0.05)
        let body = { Scene.moving with Position = p -0.65 0. }
        runtime.Step [command Scene.areaA (CreateBody body)] |> advance |> ignore
        runtime.CommitFault <- Some (fun () -> failwith "injected commit interruption")
        match runtime.Step [] with
        | Advanced _ -> failtest "Uncertain transaction must not report success."
        | Stopped(reason, lastKnown) ->
            Expect.stringContains reason "injected" "fault provenance"
            Expect.equal lastKnown.Tick 1L "last fully published tick retained"
        let actual = runtime.InspectStopped()
        Expect.isSome actual.[Scene.areaA] "source actual state readable"
        Expect.isTrue (actual.[Scene.areaA].Value.Bodies.ContainsKey body.Entity) "real source preserved"
        Expect.isEmpty actual.[Scene.areaB].Value.Bodies "real staged destination removed"
        Expect.throws (fun () -> runtime.Step [] |> ignore) "stopped owner refuses further simulation"
    }
    test "exit hysteresis blocks immediate bounce then re-arms after recorded clearance" {
        use runtime = new AreaRuntime(Scene.graph, configuration, 0.25)
        let first = runtime.Step [command Scene.areaA (CreateBody Scene.moving)] |> advance
        Expect.equal first.Traversals.Length 1 "first crossing"
        let bounced = runtime.Step [command Scene.areaB (ApplyImpulse(Scene.moving.Entity, p 0. (-8. * Math.PI * 0.1 * 0.1 * Scene.moving.Density)))] |> advance
        Expect.isEmpty bounced.Traversals "immediate reverse crossing excluded"
        // Restore positive Y velocity; first clear below, then travel above the aperture, then return.
        runtime.Step [command Scene.areaB (ApplyImpulse(Scene.moving.Entity, p 0. (8. * Math.PI * 0.1 * 0.1 * Scene.moving.Density)))] |> advance |> ignore
        for _ in 1 .. 3 do runtime.Step [] |> advance |> ignore
        runtime.Step [command Scene.areaB (ApplyImpulse(Scene.moving.Entity, p 0. (-8. * Math.PI * 0.1 * 0.1 * Scene.moving.Density)))] |> advance |> ignore
        let returned = [for _ in 1 .. 5 -> runtime.Step [] |> advance] |> List.collect _.Traversals
        Expect.equal returned.Length 1 "one reverse crossing after clearance"
        Expect.equal returned.Head.Destination Scene.areaA "back in original area"
    }
    test "traversal interpolation snaps moved body while ordinary entities blend" {
        use runtime = new AreaRuntime(Scene.graph, configuration, 0.05)
        let body = { Scene.moving with Position = p -0.65 0. }
        let other = { Physics.circle "ordinary" 0.1 (p -10. 0.) with LinearVelocity = p 1. 0. }
        let previous = runtime.Step [command Scene.areaA (CreateBody body); command Scene.areaA (CreateBody other)] |> advance
        let current = runtime.Step [] |> advance
        let displayed = AreaPhysics.interpolate 0.5 previous current
        Expect.equal displayed.[Scene.areaB].[body.Entity] current.Areas.[Scene.areaB].Bodies.[body.Entity] "cross-area snap"
        let oldX, newX = previous.Areas.[Scene.areaA].Bodies.[other.Entity].Position.X, current.Areas.[Scene.areaA].Bodies.[other.Entity].Position.X
        near displayed.[Scene.areaA].[other.Entity].Position.X ((oldX + newX) / 2.)
        let retained = current
        runtime.Step [] |> advance |> ignore
        Expect.equal current retained "retained immutable state"
    }
    test "global command validation refuses duplicate identity before any world steps" {
        use runtime = new AreaRuntime(Scene.graph, configuration, 0.05)
        Expect.throws (fun () -> runtime.Step [command Scene.areaA (CreateBody Scene.moving); command Scene.areaB (CreateBody Scene.moving)] |> ignore) "global identity refusal"
        Expect.equal runtime.LocalTicks (Map.ofList [Scene.areaA, 0L; Scene.areaB, 0L]) "all worlds unstepped"
    }
]

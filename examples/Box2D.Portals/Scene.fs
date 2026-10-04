module Box2D.Portals.Scene

open System
open FS.GG.Game.Core
open FS.GG.Game.Physics.Box2D

let areaA = AreaTopology.AreaId "a"
let areaB = AreaTopology.AreaId "b"
let private get = function Ok value -> value | Error reason -> failwith reason
let private point x y : Point = { X = x; Y = y }
let mapping = AreaTopology.rigidMap (Math.PI / 2.) (point 10. 5.) |> get
let sourceOpening : AreaTopology.Opening = { Start = point 0. -2.; Finish = point 0. 2. }
let destinationOpening : AreaTopology.Opening =
    { Start = AreaTopology.mapPoint mapping sourceOpening.Start; Finish = AreaTopology.mapPoint mapping sourceOpening.Finish }
let forward : AreaTopology.Portal =
    { Id = AreaTopology.PortalId "forward"; Source = areaA; Destination = areaB
      SourceOpening = sourceOpening; DestinationOpening = destinationOpening
      Direction = AreaTopology.PositiveToNegative; Mapping = mapping; Reverse = Some (AreaTopology.PortalId "back") }
let back : AreaTopology.Portal =
    { Id = AreaTopology.PortalId "back"; Source = areaB; Destination = areaA
      SourceOpening = destinationOpening; DestinationOpening = sourceOpening
      Direction = AreaTopology.NegativeToPositive; Mapping = AreaTopology.inverse mapping; Reverse = Some forward.Id }
let graph = AreaTopology.create [areaA; areaB] [forward; back] |> get
let settings =
    [ { Area = areaA; Physics = { Physics.defaultSettings with Gravity = point 0. 0.; TickSeconds = 0.1 }; Capacity = 16 }
      { Area = areaB; Physics = { Physics.defaultSettings with Gravity = point 0. -3.; TickSeconds = 0.1 }; Capacity = 16 } ]
let moving =
    { Physics.circle "traveller" 0.1 (point -0.25 0.) with
        LinearVelocity = point 4. 1.; Rotation = 0.3; AngularVelocity = 2.
        Density = 2.; Friction = 0.4; Restitution = 0.2 }
let inputs =
    [ [ { Area = areaA; Command = CreateBody moving } ]; []; []; []; [] ]
let advance = function Advanced snapshot -> snapshot | Stopped(reason, _) -> failwith ("Stopped: " + reason)
let run () =
    use runtime = new AreaRuntime(graph, settings, 0.05)
    inputs |> List.map (runtime.Step >> advance)
let qualify () =
    let observed = run ()
    if observed <> run () then failwith "Pinned-profile portal replay differs."
    let transferred = observed.Head
    let event = match transferred.Traversals with [event] -> event | _ -> failwith "Expected one committed traversal."
    if transferred.Ownership <> Map.ofList [moving.Entity, areaB] then failwith "Entity identity/ownership differs."
    if transferred.Areas |> Map.toSeq |> Seq.sumBy (fun (_, snapshot) -> snapshot.Bodies.Count) <> 1 then
        failwith "Expected exactly one live body after commit."
    if transferred.Areas |> Map.exists (fun _ snapshot -> snapshot.Tick <> 1L) then failwith "A local world stepped more than once."
    let sourceMotion : AreaTopology.Motion =
        { Position = event.SourcePose.Position; Velocity = event.SourcePose.LinearVelocity
          Orientation = event.SourcePose.Rotation; AngularVelocity = event.SourcePose.AngularVelocity }
    let expected = AreaTopology.mapMotion mapping sourceMotion
    let close a b = abs (a - b) <= 2e-5
    let pose = event.DestinationPose
    if not (close pose.Position.X expected.Position.X && close pose.Position.Y expected.Position.Y
            && close pose.LinearVelocity.X expected.Velocity.X && close pose.LinearVelocity.Y expected.Velocity.Y
            && close pose.AngularVelocity expected.AngularVelocity
            && close (Math.IEEERemainder(pose.Rotation - expected.Orientation, 2. * Math.PI)) 0.) then
        failwithf "Rotated pose/momentum differ. actual=%A expected=%A source=%A" pose expected event.SourcePose
    if observed |> List.sumBy (fun snapshot -> snapshot.Traversals.Length) <> 1 then failwith "Unexpected repeated transfer."
    observed

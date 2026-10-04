module Box2D.Portals.Program

[<EntryPoint>]
let main _ =
    let observed = Scene.qualify ()
    let event = observed.Head.Traversals.Head
    printfn "PASS portal: tick=%d entity=%s source=%A destination=%A bodies=1 traversals=1 replay=exact"
        event.Tick event.Entity event.Source event.Destination
    printfn "pose=(%g,%g) velocity=(%g,%g) angularVelocity=%g"
        event.DestinationPose.Position.X event.DestinationPose.Position.Y
        event.DestinationPose.LinearVelocity.X event.DestinationPose.LinearVelocity.Y event.DestinationPose.AngularVelocity
    0

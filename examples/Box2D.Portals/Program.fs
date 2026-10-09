module Box2D.Portals.Program

let private qualifyDefault () =
    let observed = Scene.qualify ()
    let event = observed.Head.Traversals.Head
    printfn "PASS portal: tick=%d entity=%s source=%A destination=%A bodies=1 traversals=1 replay=exact"
        event.Tick event.Entity event.Source event.Destination
    printfn "pose=(%g,%g) velocity=(%g,%g) angularVelocity=%g"
        event.DestinationPose.Position.X event.DestinationPose.Position.Y
        event.DestinationPose.LinearVelocity.X event.DestinationPose.LinearVelocity.Y event.DestinationPose.AngularVelocity
    0

[<EntryPoint>]
let main argv =
    if argv = [| "--presentation" |] then
        let snapshots, frames = Presentation.qualify ()
        let counts = frames |> List.map (fun frame -> Presentation.inspect snapshots[int frame.Tick - 1] frame)
        printfn "PASS portal presentation: ticks=%d frames=%d areaNodes=%d points=%d alpha=0,0.5,1 local-metres=+Y-up"
            snapshots.Length frames.Length (counts |> List.sumBy fst) (counts |> List.sumBy snd)
        0
    elif argv.Length <> 0 then
        eprintfn "Usage: Box2D.Portals [--presentation]"
        2
    else
        qualifyDefault ()

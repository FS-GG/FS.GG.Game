module Box2D.Headless.Program

open System
open System.Runtime.InteropServices
open System.Text.Json

[<EntryPoint>]
let main _ =
    let snapshots, jointError = Scene.qualify ()
    let portals = Box2D.Portals.Scene.qualify ()
    let traversal = portals.Head.Traversals.Head
    let events = snapshots |> List.collect _.Events |> List.map string |> List.toArray
    let final = snapshots |> List.last
    let result =
        {| package = "Box2D.NET"
           version = "3.1.654"
           runtime = RuntimeInformation.FrameworkDescription
           runtimeVersion = Environment.Version.ToString()
           os = RuntimeInformation.OSDescription
           architecture = RuntimeInformation.ProcessArchitecture.ToString()
           portalTicks = portals.Length
           portalEntity = traversal.Entity
           portalTraversals = portals |> List.sumBy (fun snapshot -> snapshot.Traversals.Length)
           portalPositionX = traversal.DestinationPose.Position.X
           portalPositionY = traversal.DestinationPose.Position.Y
           portalVelocityX = traversal.DestinationPose.LinearVelocity.X
           portalVelocityY = traversal.DestinationPose.LinearVelocity.Y
           ticks = final.Tick
           fallingY = final.Bodies["falling"].Position.Y
           maximumJointError = jointError
           replay = "exact-pinned-profile"
           events = events |}
    printfn "%s" (JsonSerializer.Serialize result)
    0

module Box2D.Headless.Program

open System
open System.Runtime.InteropServices
open System.Text.Json

[<EntryPoint>]
let main _ =
    let snapshots, jointError = Scene.qualify ()
    let events = snapshots |> List.collect _.Events |> List.map string |> List.toArray
    let final = snapshots |> List.last
    let result =
        {| package = "Box2D.NET"
           version = "3.1.654"
           runtime = RuntimeInformation.FrameworkDescription
           runtimeVersion = Environment.Version.ToString()
           os = RuntimeInformation.OSDescription
           architecture = RuntimeInformation.ProcessArchitecture.ToString()
           ticks = final.Tick
           fallingY = final.Bodies["falling"].Position.Y
           maximumJointError = jointError
           replay = "exact-pinned-profile"
           events = events |}
    printfn "%s" (JsonSerializer.Serialize result)
    0

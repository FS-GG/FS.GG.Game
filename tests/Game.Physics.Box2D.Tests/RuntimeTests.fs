module Game.Physics.Box2D.Tests.RuntimeTests

open System
open Expecto
open FS.GG.Game.Physics.Box2D

[<Tests>]
let tests = testList "Box2D runtime" [
    testCase "real headless falling body joint sensor and exact replay" <| fun _ ->
        let snapshots, error = Box2D.Headless.Scene.qualify ()
        Expect.equal snapshots.Length 240 "Every fixed tick is captured"
        Expect.isLessThan error 0.03 "Engine enforces the distance joint"

    testCase "concurrent independent world lifetimes keep distinct ownership" <| fun _ ->
        let options = System.Threading.Tasks.ParallelOptions(MaxDegreeOfParallelism = 8)
        System.Threading.Tasks.Parallel.For(0, 128, options, Action<int>(fun index ->
            use runtime = new Runtime({ Physics.defaultSettings with Gravity = { X = 0.; Y = 0. } })
            let x = float index
            let snapshot = runtime.Step [ CreateBody (Physics.circle "body" 0.5 { X = x; Y = 10. }) ]
            Expect.equal snapshot.Bodies["body"].Position.X x "World owns its uniquely placed body"
            Expect.equal snapshot.Bodies.Count 1 "No foreign bodies enter the world"
            runtime.Step [] |> ignore)) |> ignore

    testCase "retained observations survive steps and disposal" <| fun _ ->
        let runtime = new Runtime(Physics.defaultSettings)
        let first = runtime.Step [ CreateBody (Physics.circle "body" 0.5 { X = 0.; Y = 10. }) ]
        let second = runtime.Step []
        let firstY, secondY = first.Bodies["body"].Position.Y, second.Bodies["body"].Position.Y
        for _ in 1 .. 30 do runtime.Step [] |> ignore
        (runtime :> IDisposable).Dispose()
        Expect.equal first.Bodies["body"].Position.Y firstY "Previous pose is retained"
        Expect.equal second.Bodies["body"].Position.Y secondY "Current pose is retained"
        Expect.isGreaterThan firstY secondY "Distinct buffers capture falling motion"
        Expect.throwsT<ObjectDisposedException> (fun () -> runtime.Step [] |> ignore) "Disposed access refuses"
        (runtime :> IDisposable).Dispose()

    testCase "invalid command batch leaves existing world unchanged" <| fun _ ->
        use runtime = new Runtime(Physics.defaultSettings)
        let before = runtime.Snapshot()
        Expect.throwsT<ArgumentException> (fun () -> runtime.Step [
            CreateBody (Physics.circle "body" 1. { X = 0.; Y = 2. }); ApplyImpulse("missing", { X = 1.; Y = 0. }) ] |> ignore)
            "Unknown ID refuses all commands"
        Expect.equal (runtime.Snapshot()) before "No creation or tick occurs"
        Expect.throwsT<ArgumentException> (fun () -> runtime.Step [
            CreateBody { Physics.circle "nan" 1. { X = 0.; Y = 0. } with Rotation = nan } ] |> ignore)
            "Nonfinite boundary input refuses"
        Expect.equal (runtime.Snapshot()) before "World remains usable"

    testCase "removed shape events retain entity IDs and stale commands refuse" <| fun _ ->
        use runtime = new Runtime({ Physics.defaultSettings with Gravity = { X = 0.; Y = 0. } })
        let entered = runtime.Step [
            CreateBody { Physics.circle "sensor" 2. { X = 0.; Y = 0. } with Kind = Static; IsSensor = true }
            CreateBody (Physics.circle "visitor" 0.5 { X = 0.; Y = 0. }) ]
        Expect.contains entered.Events (SensorEntered("sensor", "visitor")) "Copied enter IDs"
        let exited = runtime.Step [ RemoveBody "visitor" ]
        Expect.contains exited.Events (SensorExited("sensor", "visitor")) "Removed shape resolves without dereferencing stale engine handle"
        Expect.isFalse (Map.containsKey "visitor" exited.Bodies) "Removed body absent"
        Expect.throwsT<ArgumentException> (fun () -> runtime.Step [ ApplyForce("visitor", { X = 0.; Y = 1. }) ] |> ignore)
            "Stale entity refuses"

    testCase "interpolation uses shortest arc and current membership" <| fun _ ->
        let pose angle x = { Position = { X = x; Y = 0. }; Rotation = angle
                             LinearVelocity = { X = 0.; Y = 0. }; AngularVelocity = 0. }
        let previous = { Tick = 1L; Bodies = Map.ofList ["body", pose (Math.PI - 0.1) 0.; "removed", pose 0. 0.]; Events = [] }
        let current = { Tick = 2L; Bodies = Map.ofList ["body", pose (-Math.PI + 0.1) 2.; "new", pose 0. 4.]; Events = [] }
        let middle = Physics.interpolate 0.5 previous current
        Expect.floatClose Accuracy.high middle["body"].Position.X 1. "Midpoint position"
        Expect.floatClose Accuracy.high middle["body"].Rotation Math.PI "Crossing pi follows the short arc"
        Expect.equal middle["new"] current.Bodies["new"] "New body snaps"
        Expect.isFalse (Map.containsKey "removed" middle) "Removed body disappears"
        Expect.throwsT<ArgumentException> (fun () -> Physics.interpolate nan previous current |> ignore) "Invalid alpha refuses"
]

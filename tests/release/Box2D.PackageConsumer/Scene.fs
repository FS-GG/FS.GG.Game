module Box2D.Headless.Scene

open FS.GG.Game.Physics.Box2D

let inputs =
    let circle = Physics.circle
    let floor = { circle "floor" 1. { X = 0.; Y = -0.5 } with Kind = Static; Shape = Box(12., 0.5) }
    let sensor = { circle "sensor" 1. { X = 0.; Y = 4. } with Kind = Static; Shape = Box(2., 0.25); IsSensor = true }
    let anchor = { circle "anchor" 0.2 { X = 4.; Y = 8. } with Kind = Static }
    let bob = circle "bob" 0.3 { X = 4.; Y = 6. }
    [ for tick in 1 .. 240 ->
          match tick with
          | 1 -> [ CreateBody floor; CreateBody sensor; CreateBody (circle "falling" 0.4 { X = 0.; Y = 8. })
                   CreateBody anchor; CreateBody bob
                   CreateDistanceJoint { Joint = "rope"; EntityA = "anchor"; EntityB = "bob"; Length = 2. } ]
          | 30 -> [ ApplyImpulse("bob", { X = 0.25; Y = 0. }) ]
          | 90 -> [ ApplyForce("bob", { X = -1.; Y = 0. }) ]
          | _ -> [] ]

let run () =
    use runtime = new Runtime(Physics.defaultSettings)
    inputs |> List.map runtime.Step

let qualify () =
    use runtime = new Runtime(Physics.defaultSettings)
    let first = runtime.Step inputs.Head
    let second = runtime.Step inputs.Tail.Head
    let retainedY = first.Bodies["falling"].Position.Y, second.Bodies["falling"].Position.Y
    let rest = inputs |> List.skip 2 |> List.map runtime.Step
    let replay = run ()
    let observed = first :: second :: rest
    if observed <> replay then failwith "Pinned-profile exact replay differs."
    if retainedY <> (first.Bodies["falling"].Position.Y, second.Bodies["falling"].Position.Y) then
        failwith "Retained snapshots changed."
    let middle = Physics.interpolate 0.5 first second
    let expectedY = (first.Bodies["falling"].Position.Y + second.Bodies["falling"].Position.Y) / 2.
    if abs (middle["falling"].Position.Y - expectedY) > 1e-12 then failwith "Interpolation differs."
    let events = observed |> List.collect _.Events
    if not (List.contains (SensorEntered("sensor", "falling")) events) then failwith "Missing sensor enter."
    if not (List.contains (SensorExited("sensor", "falling")) events) then failwith "Missing sensor exit."
    if not (List.contains (ContactStarted("falling", "floor")) events) then failwith "Missing floor contact."
    if observed[60].Bodies["falling"].Position.Y >= first.Bodies["falling"].Position.Y - 1. then
        failwith "Body did not fall."
    let jointError = observed |> List.map (fun snapshot ->
        let a, b = snapshot.Bodies["anchor"].Position, snapshot.Bodies["bob"].Position
        abs (sqrt ((a.X - b.X) ** 2. + (a.Y - b.Y) ** 2.) - 2.)) |> List.max
    if jointError > 0.03 then failwithf "Distance joint error: %g" jointError
    observed, jointError

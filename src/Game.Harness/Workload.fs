namespace FS.GG.Game.Harness

open System
open System.Diagnostics
open System.Globalization
open System.Text
open System.Text.Json
open FS.GG.Game.Core

[<RequireQualifiedAccess>]
type WorkloadKind =
    | Idle
    | MovementAndAiming
    | CombatAndEffects
    | MaximumVisibilityAndFog
    | MaximumExpectedActors

[<RequireQualifiedAccess>]
type WorkloadClass =
    | NormalExpected
    | WorstExpected

type WorkloadCost =
    { SimulationSteps: int
      AiWorkUnits: int
      PerceptionWorkUnits: int
      PathfindingWorkUnits: int
      EntityCount: int
      SceneNodeCount: int
      CatchUpSteps: int
      MovingEntityCount: int
      InterpolatedMovingEntityCount: int
      StaticBlockerBuilds: int
      StaticBlockerQueries: int }

type WorkloadBudget =
    { P95Ms: float
      P99Ms: float
      MaximumAiWorkUnits: int
      MaximumPerceptionWorkUnits: int
      MaximumPathfindingWorkUnits: int
      MaximumSceneNodes: int
      MaximumCatchUpSteps: int
      MaximumStaticBlockerBuilds: int }

type ExpectedWorkload<'key> =
    { Id: string
      Definition: string
      Kind: WorkloadKind
      Class: WorkloadClass
      WarmupFrames: 'key list list
      Frames: 'key list list
      Budget: WorkloadBudget }

type WorkloadAdapter<'world, 'key, 'fingerprint when 'key: comparison> =
    { Playable: Playable<'world, 'key>
      Fingerprint: 'world -> 'fingerprint
      ObserveCost: 'world -> WorkloadCost }

type WorkloadVerdict =
    { Passed: bool
      Reasons: string list }

type WorkloadObservation =
    { WorkloadId: string
      Definition: string
      Kind: WorkloadKind
      Class: WorkloadClass
      SampleFrames: int
      P50Ms: float
      P95Ms: float
      P99Ms: float
      MaximumCost: WorkloadCost
      Verdict: WorkloadVerdict }

type WorkloadRun<'fingerprint> =
    { Trace: Trace<'fingerprint>
      Observation: WorkloadObservation }

[<RequireQualifiedAccess>]
module Workload =

    let private zeroCost =
        { SimulationSteps = 0
          AiWorkUnits = 0
          PerceptionWorkUnits = 0
          PathfindingWorkUnits = 0
          EntityCount = 0
          SceneNodeCount = 0
          CatchUpSteps = 0
          MovingEntityCount = 0
          InterpolatedMovingEntityCount = 0
          StaticBlockerBuilds = 0
          StaticBlockerQueries = 0 }

    let private maximum a b =
        { SimulationSteps = max a.SimulationSteps b.SimulationSteps
          AiWorkUnits = max a.AiWorkUnits b.AiWorkUnits
          PerceptionWorkUnits = max a.PerceptionWorkUnits b.PerceptionWorkUnits
          PathfindingWorkUnits = max a.PathfindingWorkUnits b.PathfindingWorkUnits
          EntityCount = max a.EntityCount b.EntityCount
          SceneNodeCount = max a.SceneNodeCount b.SceneNodeCount
          CatchUpSteps = max a.CatchUpSteps b.CatchUpSteps
          MovingEntityCount = max a.MovingEntityCount b.MovingEntityCount
          InterpolatedMovingEntityCount =
              max a.InterpolatedMovingEntityCount b.InterpolatedMovingEntityCount
          StaticBlockerBuilds = max a.StaticBlockerBuilds b.StaticBlockerBuilds
          StaticBlockerQueries = max a.StaticBlockerQueries b.StaticBlockerQueries }

    let evaluateCost budget samples =
        let peak = samples |> List.fold maximum zeroCost
        let incompleteInterpolation =
            samples
            |> List.tryFind (fun sample ->
                sample.InterpolatedMovingEntityCount < sample.MovingEntityCount)

        let reasons =
            [ if peak.AiWorkUnits > budget.MaximumAiWorkUnits then
                  $"AI work {peak.AiWorkUnits} exceeds {budget.MaximumAiWorkUnits}"
              if peak.PerceptionWorkUnits > budget.MaximumPerceptionWorkUnits then
                  $"perception work {peak.PerceptionWorkUnits} exceeds {budget.MaximumPerceptionWorkUnits}"
              if peak.PathfindingWorkUnits > budget.MaximumPathfindingWorkUnits then
                  $"pathfinding work {peak.PathfindingWorkUnits} exceeds {budget.MaximumPathfindingWorkUnits}"
              if peak.SceneNodeCount > budget.MaximumSceneNodes then
                  $"scene nodes {peak.SceneNodeCount} exceed {budget.MaximumSceneNodes}"
              if peak.CatchUpSteps > budget.MaximumCatchUpSteps then
                  $"catch-up steps {peak.CatchUpSteps} exceed {budget.MaximumCatchUpSteps}"
              if peak.StaticBlockerBuilds > budget.MaximumStaticBlockerBuilds then
                  $"static blocker rebuilds {peak.StaticBlockerBuilds} exceed {budget.MaximumStaticBlockerBuilds}"
              match incompleteInterpolation with
              | Some sample ->
                  $"only {sample.InterpolatedMovingEntityCount} of {sample.MovingEntityCount} moving entities are interpolated"
              | None -> () ]

        { Passed = List.isEmpty reasons
          Reasons = reasons }

    let private percentile p values =
        match List.sort values with
        | [] -> 0.0
        | sorted ->
            let index =
                Math.Ceiling(p / 100.0 * float sorted.Length)
                |> int
                |> fun i -> Math.Clamp(i - 1, 0, sorted.Length - 1)

            sorted.[index]

    let private step
        (playable: Playable<'world, 'key>)
        (keys: 'key list)
        (world: 'world)
        : 'world =
        let commands = keys |> List.choose (Playable.resolve playable)
        let applied = commands |> List.fold (fun state command -> playable.Apply command state) world
        playable.Step applied playable.Dt

    let run
        (adapter: WorkloadAdapter<'world, 'key, 'fingerprint>)
        (workload: ExpectedWorkload<'key>)
        : WorkloadRun<'fingerprint> =
        let mutable world = adapter.Playable.Init

        for keys in workload.WarmupFrames do
            world <- step adapter.Playable keys world
            adapter.ObserveCost world |> ignore

        let fingerprints = ResizeArray<_>(workload.Frames.Length)
        let costs = ResizeArray<_>(workload.Frames.Length)
        let timings = ResizeArray<_>(workload.Frames.Length)

        for keys in workload.Frames do
            let timer = Stopwatch.StartNew()
            world <- step adapter.Playable keys world
            let cost = adapter.ObserveCost world
            timer.Stop()
            fingerprints.Add(adapter.Fingerprint world)
            costs.Add cost
            timings.Add timer.Elapsed.TotalMilliseconds

        let costList = List.ofSeq costs
        let timingList = List.ofSeq timings
        let structural = evaluateCost workload.Budget costList
        let p50 = percentile 50.0 timingList
        let p95 = percentile 95.0 timingList
        let p99 = percentile 99.0 timingList

        let timingReasons =
            [ if p95 > workload.Budget.P95Ms then
                  $"p95 {p95:F3} ms exceeds {workload.Budget.P95Ms:F3} ms"
              if p99 > workload.Budget.P99Ms then
                  $"p99 {p99:F3} ms exceeds {workload.Budget.P99Ms:F3} ms" ]

        let reasons = structural.Reasons @ timingReasons

        { Trace = Trace.create Origin.InputDriven (List.ofSeq fingerprints)
          Observation =
            { WorkloadId = workload.Id
              Definition = workload.Definition
              Kind = workload.Kind
              Class = workload.Class
              SampleFrames = workload.Frames.Length
              P50Ms = p50
              P95Ms = p95
              P99Ms = p99
              MaximumCost = costList |> List.fold maximum zeroCost
              Verdict =
                { Passed = List.isEmpty reasons
                  Reasons = reasons } } }

    let private kindToken =
        function
        | WorkloadKind.Idle -> "idle"
        | WorkloadKind.MovementAndAiming -> "movement-aiming"
        | WorkloadKind.CombatAndEffects -> "combat-effects"
        | WorkloadKind.MaximumVisibilityAndFog -> "maximum-visibility-fog"
        | WorkloadKind.MaximumExpectedActors -> "maximum-expected-actors"

    let private classToken =
        function
        | WorkloadClass.NormalExpected -> "normal"
        | WorkloadClass.WorstExpected -> "worst-expected"

    let renderArtifact (hostProfile: string) (observations: WorkloadObservation list) =
        let quoted (value: string) = "\"" + JsonEncodedText.Encode(value).ToString() + "\""
        let number (value: float) = value.ToString("R", CultureInfo.InvariantCulture)
        let builder = StringBuilder()
        builder.Append("{\"schemaVersion\":1") |> ignore
        builder.Append(",\"measurementCapability\":\"bounded-headless-update-and-scene-route\"") |> ignore
        builder.Append(",\"notAuthoritativeFor\":\"live-compositor,swapchain,vblank,vsync\"") |> ignore
        builder.Append(",\"hostProfile\":").Append(quoted hostProfile) |> ignore
        builder.Append(",\"warmupSamplePolicy\":\"per-workload; monotonic Stopwatch; warmup excluded\"") |> ignore
        builder.Append(",\"workloads\":[") |> ignore

        observations
        |> List.iteri (fun index observation ->
            let cost = observation.MaximumCost
            if index > 0 then builder.Append(',') |> ignore
            builder.Append("{\"id\":").Append(quoted observation.WorkloadId) |> ignore
            builder.Append(",\"definition\":").Append(quoted observation.Definition) |> ignore
            builder.Append(",\"class\":").Append(quoted (classToken observation.Class)).Append(",\"workloadKind\":").Append(quoted (kindToken observation.Kind)) |> ignore
            builder.Append(",\"sampleFrames\":").Append(observation.SampleFrames) |> ignore
            builder.Append(",\"p50Ms\":").Append(number observation.P50Ms) |> ignore
            builder.Append(",\"p95Ms\":").Append(number observation.P95Ms) |> ignore
            builder.Append(",\"p99Ms\":").Append(number observation.P99Ms) |> ignore
            builder.Append(",\"updateCount\":").Append(observation.SampleFrames) |> ignore
            builder.Append(",\"catchUpFrames\":").Append(cost.CatchUpSteps) |> ignore
            builder.Append(",\"entityCount\":").Append(cost.EntityCount) |> ignore
            builder.Append(",\"aiWorkUnits\":").Append(cost.AiWorkUnits) |> ignore
            builder.Append(",\"perceptionWorkUnits\":").Append(cost.PerceptionWorkUnits) |> ignore
            builder.Append(",\"pathfindingWorkUnits\":").Append(cost.PathfindingWorkUnits) |> ignore
            builder.Append(",\"sceneNodesByLayer\":{\"product-scene\":").Append(cost.SceneNodeCount).Append('}') |> ignore
            builder.Append(",\"passed\":").Append(if observation.Verdict.Passed then "true" else "false") |> ignore
            builder.Append(",\"reasons\":[") |> ignore
            observation.Verdict.Reasons
            |> List.iteri (fun reasonIndex reason ->
                if reasonIndex > 0 then builder.Append(',') |> ignore
                builder.Append(quoted reason) |> ignore)
            builder.Append("]}") |> ignore)

        builder.Append("]}").ToString()

module Game.Harness.Tests.WorkloadTests

open System.Text.Json
open Expecto
open FS.GG.Game.Harness

let private budget =
    { P95Ms = 1000.0
      P99Ms = 1000.0
      MaximumAiWorkUnits = 24
      MaximumPerceptionWorkUnits = 64
      MaximumPathfindingWorkUnits = 144
      MaximumSceneNodes = 146
      MaximumCatchUpSteps = 1
      MaximumStaticBlockerBuilds = 1 }

let private cost blockers moving interpolated ai perception path scene =
    { SimulationSteps = 1
      AiWorkUnits = ai
      PerceptionWorkUnits = perception
      PathfindingWorkUnits = path
      EntityCount = moving
      SceneNodeCount = scene
      CatchUpSteps = 0
      MovingEntityCount = moving
      InterpolatedMovingEntityCount = interpolated
      StaticBlockerBuilds = blockers
      StaticBlockerQueries = perception }

let tests =
    testList
        "Workload"
        [ testCase "unbounded repeated blocker scans and search fail the structural gate"
          <| fun _ ->
              let verdict = Workload.evaluateCost budget [ cost 30 8 8 100 500 1000 120 ]
              Expect.isFalse verdict.Passed "repeated scans and unbounded AI/search are rejected"
              Expect.isTrue (verdict.Reasons |> List.exists (_.Contains("static blocker"))) "blocker rebuild failure is named"
              Expect.isTrue (verdict.Reasons |> List.exists (_.Contains("pathfinding"))) "path bound failure is named"

          testCase "indexed bounded work and complete stable-identity interpolation pass"
          <| fun _ ->
              let verdict = Workload.evaluateCost budget [ cost 1 8 8 12 40 120 120 ]
              Expect.isTrue verdict.Passed "preindexed blockers, bounded search, compact scene, and full interpolation pass"

          testCase "missing interpolation for any continuously moving entity fails"
          <| fun _ ->
              let verdict = Workload.evaluateCost budget [ cost 1 8 7 12 40 120 120 ]
              Expect.isFalse verdict.Passed "every mover needs stable-identity interpolation"
              Expect.isTrue (verdict.Reasons |> List.exists (_.Contains("moving entities"))) "failure names interpolation coverage"

          testCase "workload timing is separate from deterministic trace equality"
          <| fun _ ->
              let adapter =
                  { Playable = PongSim.playable
                    Fingerprint = Driver.identityFingerprint
                    ObserveCost = fun _ -> cost 1 1 1 1 1 1 1 }

              let workload =
                  { Id = "idle"
                    Definition = "real input route at fixed step"
                    Kind = WorkloadKind.Idle
                    Class = WorkloadClass.NormalExpected
                    WarmupFrames = [ [] ]
                    Frames = [ []; [ "w" ]; [] ]
                    Budget = budget }

              let a = Workload.run adapter workload
              let b = Workload.run adapter workload
              Expect.isTrue (Trace.equalFrames a.Trace b.Trace) "wall-clock observations do not contaminate trace frames"
              Expect.equal (Trace.origin a.Trace) Origin.InputDriven "workload evidence uses the real input route"

          testCase "artifact uses scaffold performance schema fields"
          <| fun _ ->
              let observation =
                  { WorkloadId = "maximum-visibility-fog"
                    Definition = "row-run fog geometry"
                    Kind = WorkloadKind.MaximumVisibilityAndFog
                    Class = WorkloadClass.WorstExpected
                    SampleFrames = 120
                    P50Ms = 5.0
                    P95Ms = 12.0
                    P99Ms = 18.0
                    MaximumCost = cost 1 8 8 12 40 120 146
                    Verdict = { Passed = true; Reasons = [] } }

              use json = JsonDocument.Parse(Workload.renderArtifact "test" [ observation ])
              let root = json.RootElement
              Expect.equal (root.GetProperty("schemaVersion").GetInt32()) 1 "schema version matches scaffold"
              let item = root.GetProperty("workloads").EnumerateArray() |> Seq.exactlyOne
              Expect.equal (item.GetProperty("p95Ms").GetDouble()) 12.0 "scaffold timing field is present"
              Expect.equal (item.GetProperty("sceneNodesByLayer").GetProperty("product-scene").GetInt32()) 146 "scene schema is compatible" ]

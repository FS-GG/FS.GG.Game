namespace FS.GG.Game.Harness

open FS.GG.Game.Core

/// The five representative workload shapes every generated game must define.
[<RequireQualifiedAccess>]
type WorkloadKind =
    | Idle
    | MovementAndAiming
    | CombatAndEffects
    | MaximumVisibilityAndFog
    | MaximumExpectedActors

/// Whether the scenario represents ordinary play or the largest supported workload.
[<RequireQualifiedAccess>]
type WorkloadClass =
    | NormalExpected
    | WorstExpected

/// Per-frame product facts. Counts describe work actually performed, not elapsed time.
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

/// Expected-workload limits. Wall-clock limits are evaluated separately from deterministic traces.
type WorkloadBudget =
    { P95Ms: float
      P99Ms: float
      MaximumAiWorkUnits: int
      MaximumPerceptionWorkUnits: int
      MaximumPathfindingWorkUnits: int
      MaximumSceneNodes: int
      MaximumCatchUpSteps: int
      MaximumStaticBlockerBuilds: int }

/// A named input-driven scenario. `Frames` uses the real raw-key route.
type ExpectedWorkload<'key> =
    { Id: string
      Definition: string
      Kind: WorkloadKind
      Class: WorkloadClass
      WarmupFrames: 'key list list
      Frames: 'key list list
      Budget: WorkloadBudget }

/// Product-owned projection joining simulation, AI/perception/pathfinding, presentation, and scene cost.
type WorkloadAdapter<'world, 'key, 'fingerprint when 'key: comparison> =
    { Playable: Playable<'world, 'key>
      Fingerprint: 'world -> 'fingerprint
      ObserveCost: 'world -> WorkloadCost }

type WorkloadVerdict =
    { Passed: bool
      Reasons: string list }

/// Timing/cost evidence kept outside `Trace`, so replay equality remains purely structural.
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

    /// Run a named workload through raw key resolution and fixed steps. Use Release builds for timing
    /// verdicts; deterministic tests should assert only on `Trace`.
    val run:
        adapter: WorkloadAdapter<'world, 'key, 'fingerprint> ->
        workload: ExpectedWorkload<'key> ->
            WorkloadRun<'fingerprint>

    /// Evaluate bounded work and complete moving-entity interpolation without running a clock.
    val evaluateCost: budget: WorkloadBudget -> samples: WorkloadCost list -> WorkloadVerdict

    /// Render observations using the generated scaffold performance-evidence field names.
    val renderArtifact: hostProfile: string -> observations: WorkloadObservation list -> string

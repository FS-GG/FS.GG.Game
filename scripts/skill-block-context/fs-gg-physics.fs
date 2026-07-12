// Typecheck fixtures for fs-gg-physics (see scripts/typecheck-md-blocks.fsx).

//#block 1 "[<RequireQualifiedAccess>]"
//#skip an .fsi SIGNATURE listing (the skill's proposed `module Physics` surface: `val step: ...`, `type World` with no definition). Not implementation code, so it cannot be compiled as an .fs. The real surface is gated byte-for-byte by the surface-baseline-drift job instead.

//#block 2 "let simInterval = 1.0 / 60.0"
// `Physics.step` IS the integrate function `Loop.advance` takes — that fit is the block's claim,
// and compiling it is what checks the claim. The Model carries the StepState over Physics.World.
let config : Physics.Config =
    { Gravity = { X = 0.0; Y = 9.81 }
      VelocityIterations = 8
      PositionIterations = 3
      Slop = 0.01
      Correction = 0.2
      BounceThreshold = 1.0
      SleepLinearSq = 0.01
      SleepAngular = 0.01
      SleepTicks = 60
      BroadPhaseCellSize = 32.0 }
let dtSeconds = 1.0 / 144.0
let model = {| Sim = Loop.init (Physics.empty config) |}

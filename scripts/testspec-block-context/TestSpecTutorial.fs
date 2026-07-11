// Typecheck fixtures for TestSpecTutorial (see scripts/typecheck-md-blocks.fsx).
//
// Part C teaches how a §14 acceptance scenario becomes an Expecto test, using a Pong-style model the
// tutorial never declares (it is the reader's own `update`/`Model`, and the prose says so). The
// fixture supplies exactly that model — shaped after docs/TestSpecs/Games/pong.md §5, which is the
// spec the tutorial is quoting.
//
// The one field that MATTERS here is `Pos: Vec2` (= the scaffold's Geometry.Vec2, Vx/Vy). It is what
// makes the tutorial's record literals typecheck against the same collision-safe vector every
// TestSpec mandates — and it is what would have caught the X/Y-labelled literal this block shipped
// with until #149. Weaken `Ball` to a bare `{ X: float; Y: float }` here and the gate would go green
// over the very defect it exists to catch.

//#block 1
type Vec2 = Geometry.Vec2

type Ball =
    { Pos: Vec2
      Vel: Vec2 }

type Model = { Ball: Ball }

type Msg = Tick of dt: float

let initial : Model =
    { Ball = { Pos = { Vx = 640.0; Vy = 360.0 }; Vel = { Vx = 0.0; Vy = 0.0 } } }

/// The reader's pure MVU step. The tutorial only ever CALLS it; its body is theirs to write.
let update (_msg: Msg) (model: Model) : Model = model

// Typecheck fixtures for TestSpecTutorial (see scripts/typecheck-md-blocks.fsx).
//
// NOTE THE ORDINALS. This document has TWO blocks, not one. The `plan` sketch (block 1) is fenced
// INSIDE a bullet, so its fence is indented — and until #176 the extractor only saw column-0 fences
// and dropped it, which is why the Part C fixture below used to be keyed `//#block 1`. Adding a block
// AHEAD of an existing one re-keys every block after it, and the harness's stale-fixture check cannot
// catch that (1 is still a valid ordinal in a 2-block document) — it would simply have bound this
// fixture to the wrong block, in silence. Re-key deliberately when a block is inserted.

//#block 1
// The tutorial's §5 `plan` sketch: "§7 State Model is your F# architecture… For Pong that's roughly:"
// It declares `Screen`, `Model` and `Msg` itself, and leans on five types it never names, because the
// reader is meant to have written them in §4 — `Ball`, `Paddle`, `Config`, `Side`, `Direction`.
//
// Those five are reconstructed here from the document the tutorial is quoting, docs/TestSpecs/Games/
// pong.md §5 (`Side`, `Paddle`, `Ball`, verbatim) and §12 (the `Config` of tunables the prose says to
// make data-driven). Two of them carry the whole point: `Ball.Pos` is the scaffold's collision-safe
// `Geometry.Vec2` (Vx/Vy), and `Paddle` says `TopY` rather than `Y` — pong.md's own prose spells out
// why ("do not put X/Y/Width/Height labels on Paddle… the clash surfaces in LayoutEvidence.fs, a file
// you must not touch"). Weaken either to an X/Y record here and this gate would go green over exactly
// the #129/#132/#140/#144 defect it exists to catch.
type Side = Left | Right

type Direction = Up | Down

type BallState =
    | Frozen of timer: float
    | Live

type Paddle =
    { Side: Side
      TopY: float }          // top edge, px — NOT `Y`: that label collides with Scene's Point/Rect

type Ball =
    { Pos: Geometry.Vec2     // centre, px — the collision-safe Vx/Vy vector, never an X/Y record
      Vel: Geometry.Vec2     // px/s
      State: BallState }

/// pong.md §12's tunables, as the data-driven record the tutorial's §12 bullet asks for. A
/// representative subset: the block only needs the TYPE to exist, and a full transcription of the
/// table would be a second copy of it to drift from.
type Config =
    { PlayfieldW: float      // 1280 — a scalar takes an honest name; `Width` would collide
      PlayfieldH: float      // 720
      PlayerSpeed: float     // 600 px/s
      ServeSpeed: float      // 420 px/s
      WinScore: int }        // 11

//#block 2
// Part C teaches how a §14 acceptance scenario becomes an Expecto test, using a Pong-style model the
// tutorial never declares (it is the reader's own `update`/`Model`, and the prose says so). The
// fixture supplies exactly that model — shaped after docs/TestSpecs/Games/pong.md §5, which is the
// spec the tutorial is quoting.
//
// It re-declares `Vec2`/`Ball`/`Model`/`Msg` rather than reusing block 1's, and that is deliberate:
// this corpus is CUMULATIVE, so block 1's declarations are in scope here, and these shadow them.
// Part C's model is the smaller one its assertions actually need (a `Ball` and a `Tick`), and binding
// it to block 1's fuller sketch would couple the tutorial's test example to a sketch the prose only
// ever called "roughly" right. Shadowing across the module boundary is legal, and it is what the gate
// already relies on elsewhere (doodle-jump re-states `Vec2` for the reader's benefit the same way).
//
// The one field that MATTERS here is `Pos: Vec2` (= the scaffold's Geometry.Vec2, Vx/Vy). It is what
// makes the tutorial's record literals typecheck against the same collision-safe vector every
// TestSpec mandates — and it is what would have caught the X/Y-labelled literal this block shipped
// with until #149. Weaken `Ball` to a bare `{ X: float; Y: float }` here and the gate would go green
// over the very defect it exists to catch.
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

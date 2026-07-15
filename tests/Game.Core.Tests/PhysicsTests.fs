module Game.Core.Tests.PhysicsTests

// Physics: the broad phase, and only the broad phase (#74). Three things here are load-bearing, and each
// is asserted the only way that means anything:
//
//   * exactness    — `pairs` has no false negatives (a property test against an O(n²) oracle) and no
//                    false positives (the same oracle, the other direction). A broad phase is normally
//                    allowed a loose superset; this one is not, and the oracle is what holds it to that.
//   * the extent   — the grid buckets a body under every cell its AABB touches (`buildBounds`), so a
//                    large body whose ORIGIN sits far outside a small body's box is still a candidate.
//                    The regression test puts the large body at the HIGHER index, because that is the
//                    only ordering in which the `j > i` scan can miss it. (#84 replaced a global `reach`
//                    dilation here; these assertions are what pinned the behaviour across that swap.)
//   * the order    — `(a, b)` ascending, `a < b`, no duplicates. Unsorted pair order is the #1
//                    determinism leak in a physics step, so it is asserted as a contract, not assumed
//                    from the container.
//
// The strict-edge convention is asserted against `Geometry.intersects` directly, so the two can never
// drift apart silently.
//
// Two honest limits of the oracle, since it is the load-bearing assertion. It shares `Geometry.intersects`
// with the implementation, so the EDGE CONVENTION is checked by the dedicated example above and not by the
// property; and `bodyOf` bounds generated extents, so the property never reaches the overflow regime the
// `at any magnitude` tests below cover by hand. The oracle proves the grid, the extent bucketing, the
// dedup and the ordering — which is what it is there for.

open System
open Expecto
open FsCheck
open FS.GG.Game.Core

let private p x y : Point = { X = x; Y = y }

let private noMaterial: Physics.Material = { Restitution = 0.0; Friction = 0.0 }

/// A config whose only field this slice reads is the cell size; the rest are the solver's contract.
let private config (cellSize: float) : Physics.Config =
    { Gravity = p 0.0 -9.81
      VelocityIterations = 8
      PositionIterations = 3
      Slop = 0.01
      Correction = 0.2
      BounceThreshold = 1.0
      SleepLinearSq = 0.01
      SleepAngular = 0.01
      SleepTicks = 60
      BroadPhaseCellSize = cellSize }

/// `(kind, shape, position)` triples in insertion order, so a body's index is its position in the list.
let private worldOf (cellSize: float) (bodies: (Physics.BodyKind * Physics.Shape * Point) list) : Physics.World =
    bodies
    |> List.fold
        (fun w (kind, shape, pos) ->
            let struct (_, w') = Physics.addBody kind shape noMaterial pos w
            w')
        (Physics.empty (config cellSize))

let private pairList (w: Physics.World) : (int * int) list =
    Physics.pairs w |> Array.map (fun (struct (a, b)) -> a, b) |> Array.toList

let private box hx hy = Physics.SBox(p hx hy)

/// A CCW right triangle with its origin at the corner — deliberately ASYMMETRIC about that origin, which
/// is the case an extent measured as a radius about the origin, rather than from the box corners, would
/// get wrong.
let private triangle (size: float) =
    Physics.SPoly { Vertices = [| p 0.0 0.0; p size 0.0; p 0.0 size |] }

// ---------------------------------------------------------------------------------------------------
// The oracle. Recomputes each body's world AABB from its own (shape, position) and brute-forces every
// pair. It reimplements the contract rather than calling the module, so agreement is evidence.
// ---------------------------------------------------------------------------------------------------

let private finite (v: float) = not (Double.IsNaN v) && not (Double.IsInfinity v)
let private finitePoint (q: Point) = finite q.X && finite q.Y

let private ringArea (v: Point[]) =
    let mutable acc = 0.0

    for i in 0 .. v.Length - 1 do
        let a = v.[i]
        let b = v.[(i + 1) % v.Length]
        acc <- acc + (a.X * b.Y - b.X * a.Y)

    abs acc / 2.0

let private oracleAabb (shape: Physics.Shape) (pos: Point) : Rect option =
    if not (finitePoint pos) then
        None
    else
        match shape with
        | Physics.SCircle r when finite r && r > 0.0 ->
            Some
                { X = pos.X - r
                  Y = pos.Y - r
                  Width = 2.0 * r
                  Height = 2.0 * r }
        | Physics.SBox h when finitePoint h && h.X > 0.0 && h.Y > 0.0 ->
            Some
                { X = pos.X - h.X
                  Y = pos.Y - h.Y
                  Width = 2.0 * h.X
                  Height = 2.0 * h.Y }
        | Physics.SPoly poly when
            poly.Vertices.Length >= 3
            && poly.Vertices |> Array.forall finitePoint
            && ringArea poly.Vertices > 0.0
            ->
            let v = poly.Vertices

            Some
                { X = pos.X + (v |> Array.map (fun q -> q.X) |> Array.min)
                  Y = pos.Y + (v |> Array.map (fun q -> q.Y) |> Array.min)
                  Width = (v |> Array.map (fun q -> q.X) |> Array.max) - (v |> Array.map (fun q -> q.X) |> Array.min)
                  Height = (v |> Array.map (fun q -> q.Y) |> Array.max) - (v |> Array.map (fun q -> q.Y) |> Array.min) }
        | _ -> None

let private oraclePairs (bodies: (Physics.BodyKind * Physics.Shape * Point) list) : (int * int) list =
    let arr = List.toArray bodies

    [ for i in 0 .. arr.Length - 1 do
          for j in i + 1 .. arr.Length - 1 do
              let (ki, si, pi) = arr.[i]
              let (kj, sj, pj) = arr.[j]

              match oracleAabb si pi, oracleAabb sj pj with
              | Some bi, Some bj when
                  (ki = Physics.Dynamic || kj = Physics.Dynamic)
                  && Geometry.intersects bi bj
                  ->
                  yield i, j
              | _ -> () ]

// ---------------------------------------------------------------------------------------------------

let private clampCoord (v: float) =
    if Double.IsNaN v || Double.IsInfinity v then 0.0 else max -40.0 (min 40.0 v)

/// Keeps generated extents positive, bounded, and comparable to the coordinate range, so the generator
/// actually produces overlaps rather than a cloud of disjoint specks.
let private extentOf (v: float) =
    let b = if Double.IsNaN v || Double.IsInfinity v then 1.0 else abs v
    0.25 + (b % 4.0)

let private kindOf (k: int) =
    match ((k % 3) + 3) % 3 with
    | 0 -> Physics.Static
    | 1 -> Physics.Kinematic
    | _ -> Physics.Dynamic

let private shapeOf (s: int) (size: float) =
    match ((s % 3) + 3) % 3 with
    | 0 -> Physics.SCircle size
    | 1 -> box size size
    | _ -> triangle size

let private bodyOf (x, y, size, k, s) =
    kindOf k, shapeOf s (extentOf size), p (clampCoord x) (clampCoord y)

[<Tests>]
let tests =
    testList
        "Game.Core Physics broad phase (#74)"
        [

          test "an empty world has no pairs" {
              Expect.isEmpty (Physics.pairs (Physics.empty (config 8.0))) "no bodies, no pairs"
          }

          test "addBody returns dense ascending indices, and a degenerate body does not shift them" {
              let w0 = Physics.empty (config 8.0)
              let struct (i0, w1) = Physics.addBody Physics.Dynamic (Physics.SCircle 1.0) noMaterial (p 0.0 0.0) w0
              // Degenerate: a zero radius is a no-collision input, but it still occupies an index.
              let struct (i1, w2) = Physics.addBody Physics.Dynamic (Physics.SCircle 0.0) noMaterial (p 0.0 0.0) w1
              let struct (i2, _) = Physics.addBody Physics.Dynamic (Physics.SCircle 1.0) noMaterial (p 0.5 0.0) w2

              Expect.equal (i0, i1, i2) (0, 1, 2) "indices are dense and ascending in insertion order"
              // w2 holds bodies 0 and 1 only; body 1 is the zero-radius one, so it pairs with nothing.
              Expect.equal (pairList w2) [] "a degenerate body occupies an index but collides with nothing"
          }

          test "overlapping dynamic circles are a pair; a gap is not" {
              let overlapping =
                  worldOf 8.0 [ Physics.Dynamic, Physics.SCircle 1.0, p 0.0 0.0
                                Physics.Dynamic, Physics.SCircle 1.0, p 1.0 0.0 ]

              let apart =
                  worldOf 8.0 [ Physics.Dynamic, Physics.SCircle 1.0, p 0.0 0.0
                                Physics.Dynamic, Physics.SCircle 1.0, p 5.0 0.0 ]

              Expect.equal (pairList overlapping) [ 0, 1 ] "boxes overlap on positive area"
              Expect.equal (pairList apart) [] "boxes are disjoint"
          }

          test "boxes that merely touch are NOT a pair, and that agrees with Geometry.intersects" {
              // Half-extent 1 at x=0 and x=2: the boxes share the plane x=1 and overlap on zero area.
              let w =
                  worldOf 8.0 [ Physics.Dynamic, box 1.0 1.0, p 0.0 0.0
                                Physics.Dynamic, box 1.0 1.0, p 2.0 0.0 ]

              let a: Rect = { X = -1.0; Y = -1.0; Width = 2.0; Height = 2.0 }
              let b: Rect = { X = 1.0; Y = -1.0; Width = 2.0; Height = 2.0 }

              Expect.isFalse (Geometry.intersects a b) "the strict-edge convention this inherits"
              Expect.equal (pairList w) [] "a touch is not a pair — the narrow phase would report no contact either"
          }

          test "a pair needs at least one Dynamic body" {
              let kinds = [ Physics.Static; Physics.Kinematic; Physics.Dynamic ]

              for ka in kinds do
                  for kb in kinds do
                      let w =
                          worldOf 8.0 [ ka, Physics.SCircle 1.0, p 0.0 0.0
                                        kb, Physics.SCircle 1.0, p 1.0 0.0 ]

                      let expected = if ka = Physics.Dynamic || kb = Physics.Dynamic then [ 0, 1 ] else []

                      Expect.equal
                          (pairList w)
                          expected
                          (sprintf "%A vs %A: only a pair with a Dynamic member can ever resolve" ka kb)
          }

          test "a large body at a HIGHER index is still found — the grid buckets it by extent" {
              // Body 0's box is [89,91]². Body 1's box is [-100,100]², so the two overlap — but body 1's
              // ORIGIN is (0,0), nowhere near body 0's box. The scan queries body 0's region and takes
              // j > i, so a grid that filed body 1 under its origin's cell alone would never offer it as a
              // candidate, and the pair would be silently lost. Ordering matters: swap the two and a
              // position-bucketed query would find it anyway.
              let w =
                  worldOf 8.0 [ Physics.Dynamic, Physics.SCircle 1.0, p 90.0 90.0
                                Physics.Dynamic, box 100.0 100.0, p 0.0 0.0 ]

              Expect.equal (pairList w) [ 0, 1 ] "body 0's own box must reach the large body's cells"
          }

          test "an asymmetric polygon's extent is measured from its box corners, not a radius" {
              // The triangle's origin is its corner, so it extends [0,10]² — entirely to the +X/+Y side.
              // Body 0's box is [11.5,12.5]²; it overlaps nothing. Body 2 sits inside the triangle.
              let w =
                  worldOf 4.0 [ Physics.Dynamic, Physics.SCircle 0.5, p 12.0 12.0
                                Physics.Dynamic, triangle 10.0, p 0.0 0.0
                                Physics.Dynamic, Physics.SCircle 0.5, p 9.0 9.0 ]

              Expect.equal (pairList w) [ 1, 2 ] "only the triangle and the circle inside its box overlap"
          }

          test "a chain of boxes yields exactly the consecutive pairs, ascending" {
              // Half-extent 1, spaced 1.5: neighbours overlap (gap 1.5 < 2.0), next-nearest do not (3.0 > 2.0).
              let bodies =
                  [ for i in 0..5 -> Physics.Dynamic, box 1.0 1.0, p (1.5 * float i) 0.0 ]

              Expect.equal
                  (pairList (worldOf 8.0 bodies))
                  [ 0, 1; 1, 2; 2, 3; 3, 4; 4, 5 ]
                  "consecutive only, sorted ascending by (a, b), no duplicates"

              Expect.equal (pairList (worldOf 8.0 bodies)) (oraclePairs bodies) "and the oracle agrees"
          }

          test "degenerate shapes and non-finite positions collide with nothing, and never throw" {
              let bodies =
                  [ Physics.Dynamic, Physics.SCircle 0.0, p 0.0 0.0 // zero radius
                    Physics.Dynamic, Physics.SCircle nan, p 0.0 0.0 // NaN radius
                    Physics.Dynamic, box 0.0 1.0, p 0.0 0.0 // zero half-extent
                    Physics.Dynamic, Physics.SPoly { Vertices = [| p 0.0 0.0; p 1.0 1.0 |] }, p 0.0 0.0 // < 3 vertices
                    Physics.Dynamic, Physics.SPoly { Vertices = [| p 0.0 0.0; p 1.0 0.0; p 2.0 0.0 |] }, p 0.0 0.0 // collinear
                    Physics.Dynamic, Physics.SCircle 1.0, p nan 0.0 // NaN position
                    Physics.Dynamic, Physics.SCircle 1.0, p infinity 0.0 ] // infinite position

              Expect.equal (pairList (worldOf 8.0 bodies)) [] "every body here is a no-collision input"

              // ...and a real body dropped in among them still finds its real partner, at the right indices.
              let withReal =
                  bodies
                  @ [ Physics.Dynamic, Physics.SCircle 1.0, p 3.0 3.0
                      Physics.Dynamic, Physics.SCircle 1.0, p 3.5 3.0 ]

              Expect.equal (pairList (worldOf 8.0 withReal)) [ 7, 8 ] "degenerate bodies hold their indices"
          }

          test "a degenerate cell size degrades to one bucket, never to a wrong answer" {
              let bodies =
                  [ for i in 0..5 -> Physics.Dynamic, box 1.0 1.0, p (1.5 * float i) 0.0 ]

              for cellSize in [ 0.0; -1.0; nan; infinity; 0.001; 1000.0 ] do
                  Expect.equal
                      (pairList (worldOf cellSize bodies))
                      (oraclePairs bodies)
                      (sprintf "cellSize %f is an acceleration choice, never a correctness one" cellSize)
          }

          test "one body with an unboundable extent costs ITSELF acceleration, never pairs" {
              // Bodies 0 and 1 plainly overlap and one is Dynamic. Body 2's half-extents overflow its box
              // width to +infinity, so its AABB cannot be filed under any finite set of cells. The grid
              // defers it — it becomes a candidate for every query and is then settled by the same exact
              // `Geometry.intersects` filter as anything else. Bodies 0 and 1 keep their own cell queries;
              // under the `reach` dilation this one body drove the global constant to infinity and cost
              // the WHOLE world its acceleration.
              let sane =
                  [ Physics.Dynamic, Physics.SCircle 1.0, p 0.0 0.0
                    Physics.Dynamic, Physics.SCircle 1.0, p 0.5 0.0 ]

              Expect.equal (pairList (worldOf 8.0 sane)) [ 0, 1 ] "the pair every later assertion depends on"

              let withHuge = sane @ [ Physics.Static, box 1e308 1e308, p 0.0 0.0 ]

              // Body 2 is Static and genuinely contains both, so it pairs with each of them.
              Expect.equal
                  (pairList (worldOf 8.0 withHuge))
                  [ 0, 1; 0, 2; 1, 2 ]
                  "a deferred body still finds every pair when its extent cannot be bucketed"
          }

          test "the ground-plane scene — one huge floor under N small bodies — stays exact" {
              // The scene #84 was filed for. Under the old global `reach` dilation the 500-unit floor
              // widened EVERY body's query to span the world, so the broad phase degenerated to a scan
              // that was strictly worse than the naive O(n²) double loop it exists to avoid.
              //
              // Exactness is what a test can assert here; the cost is what the change is for. Both cell
              // sizes are checked because they straddle the grid's per-item cell cap: at 1.0 the floor's
              // 1000×2 box spans ~2000 cells and is deferred as unbucketable, at 16.0 it spans ~63 and is
              // filed normally. The pairs must not care which.
              let floor = Physics.Static, box 500.0 1.0, p 0.0 0.0

              let resting =
                  [ for i in 0..24 -> Physics.Dynamic, Physics.SCircle 0.5, p (float i * 2.0 - 24.0) 1.2 ]

              let bodies = floor :: resting

              for cellSize in [ 1.0; 16.0 ] do
                  Expect.equal
                      (pairList (worldOf cellSize bodies))
                      (oraclePairs bodies)
                      (sprintf "cellSize %f: the floor is a neighbour of each resting body, and of nothing else" cellSize)
          }

          test "a polygon whose shoelace overflows to NaN is refused, exactly as Geometry refuses it" {
              // The shoelace terms of this ring overflow to ±infinity and cancel, so its area is NaN.
              // `NaN <= 0.0` and `NaN > 0.0` are BOTH false, so only the negated guard rejects it. If
              // Physics keeps the body while Geometry drops it, `pairs` emits a pair whose narrow phase
              // can never produce a contact.
              let ring: ConvexPolygon =
                  { Vertices = [| p 1e200 1e200; p -1e200 1e200; p -1e200 -1e200 |] }

              Expect.isNone (Geometry.polygonContact ring ring) "Geometry refuses a NaN-area ring"

              let w =
                  worldOf 8.0 [ Physics.Dynamic, Physics.SPoly ring, p 0.0 0.0
                                Physics.Dynamic, Physics.SCircle 1.0, p 0.0 0.0 ]

              Expect.equal (pairList w) [] "and so must Physics — the two guards agree on NaN or neither is safe"
          }

          test "pairs is exact against a brute-force oracle, and strictly ascending" {
              let prop (raw: (float * float * float * int * int) list) =
                  // FsCheck's list can be long; the oracle is O(n²), so cap it where the property still bites.
                  let bodies = raw |> List.truncate 24 |> List.map bodyOf
                  let got = pairList (worldOf 8.0 bodies)

                  let exact = got = oraclePairs bodies

                  let ascending =
                      got
                      |> List.pairwise
                      |> List.forall (fun ((a1, b1), (a2, b2)) -> a1 < a2 || (a1 = a2 && b1 < b2))

                  let canonical = got |> List.forall (fun (a, b) -> a < b)

                  exact && ascending && canonical

              Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
          }

          test "pairs is a pure function of the world — the same world twice is the same array" {
              let bodies =
                  [ Physics.Dynamic, Physics.SCircle 1.0, p 0.0 0.0
                    Physics.Static, box 20.0 1.0, p 0.0 -1.5
                    Physics.Kinematic, triangle 3.0, p -1.0 -1.0
                    Physics.Dynamic, Physics.SCircle 1.5, p 1.0 0.5 ]

              let a = Physics.pairs (worldOf 8.0 bodies)
              let b = Physics.pairs (worldOf 8.0 bodies)

              Expect.equal a b "byte-identical worlds yield a byte-identical array"
          } ]

// =====================================================================================================
// `step`, `manifold`, `checksum` (#75)
// =====================================================================================================
//
// `World` is opaque and this slice ships no reader for it — `interpolate` (#78) is the design's accessor
// and has not landed. So every assertion below observes the simulation through the two windows that ARE
// public: `manifold`, whose `Points` are world-space coordinates and whose `Normal` is the contact
// direction, and `checksum`, which is a pure function of body state. That is a narrower keyhole than a
// position getter, but it is not a weaker one: a box that fell through the floor has no contact, a box
// that sank has a growing depth, and a box that tipped has a rotated normal.
//
// **What is deliberately NOT asserted: that a resting box holds `AngVel = 0`.** It does not, quite. A
// sequential-impulse solver run for a FIXED 8 iterations leaves a small residual angular velocity, biased
// by the order in which it visits the two contact points. That residual is a convergence artifact, not a
// modelling error, and the test `the resting tilt is a convergence residual` below pins it as one by
// showing it vanish as the iteration count rises. Asserting zero here would be asserting a falsehood.
//
// #76 shrank that residual by ~300x at 8 iterations (warm starting) and then stopped the box moving at all
// (sleeping). Both are measured in the `#76` list further down; the tests in THIS list are written to keep
// observing the solver rather than the levers, so those that care run with sleeping switched off.

let private material r f : Physics.Material = { Restitution = r; Friction = f }

/// The tuning the scenes below share. Only `Gravity` and the iteration counts matter to most of them;
/// `Slop`/`Correction` are named explicitly because two tests assert against them directly.
let private stepConfig: Physics.Config =
    { Gravity = p 0.0 -9.81
      VelocityIterations = 8
      PositionIterations = 3
      Slop = 0.01
      Correction = 0.2
      BounceThreshold = 1.0
      SleepLinearSq = 0.01
      SleepAngular = 0.01
      SleepTicks = 60
      BroadPhaseCellSize = 4.0 }

/// `stepConfig` with the sleeping lever switched off, so a scene can be watched for as long as it takes
/// without freezing partway. A non-positive `SleepTicks` is the documented off switch (#76).
let private noSleep: Physics.Config = { stepConfig with SleepTicks = 0 }

let private tick = 1.0 / 60.0

let private advance (n: int) (w: Physics.World) =
    let mutable acc = w

    for _ in 1..n do
        acc <- Physics.step acc tick

    acc

/// Body 0 is a static floor whose top surface is exactly `y = 1.0`; body 1 is the dynamic body under
/// test, dropped from `y`. Every scene in this section shares this layout, so `manifold w 0 1` is always
/// "the contact between the floor and the thing resting on it".
let private floorTop = 1.0

let private dropped (cfg: Physics.Config) (shape: Physics.Shape) (m: Physics.Material) (y: float) =
    let w = Physics.empty cfg
    let struct (_, w) = Physics.addBody Physics.Static (box 50.0 1.0) (material 0.0 0.5) (p 0.0 0.0) w
    let struct (_, w) = Physics.addBody Physics.Dynamic shape m (p 0.0 y) w
    w

let private restingContact (w: Physics.World) = Physics.manifold w 0 1

/// A CCW right-triangle ramp of the given `rise` over a run of 10, plus a small box placed on its slope.
/// The two bodies carry INDEPENDENT μ, which is the point of the friction-combination tests below: the
/// `onRamp` helper above gives both bodies the same μ, so a solver that combined only one body's friction
/// would pass it. Returns whether the box is still in contact with the ramp after it has had time to slide
/// off — the only slide/hold readout the public surface offers (no velocity, no position).
let private holdsOnRamp (rise: float) (rampMu: float) (boxMu: float) =
    let ramp = Physics.SPoly { Vertices = [| p -5.0 -rise; p 5.0 -rise; p 5.0 rise |] }
    let w = Physics.empty stepConfig
    let struct (_, w) = Physics.addBody Physics.Static ramp (material 0.0 rampMu) (p 0.0 0.0) w
    let struct (_, w) = Physics.addBody Physics.Dynamic (box 0.3 0.3) (material 0.0 boxMu) (p 1.0 0.9) w
    ValueOption.isSome (Physics.manifold (advance 300 w) 0 1)

/// How far a settled box is off level, read as the height difference between the two points of its
/// face-on-face contact with the floor. Zero is a level rest; the value is the residual a fixed-iteration
/// Gauss-Seidel solver leaves behind, and it is the sharpest thing this module's public surface can see
/// about solver convergence. Both the `#75` residual test and the `#76` warm-start tests measure it.
let private tiltAfter (cfg: Physics.Config) iterations =
    let cfg = { cfg with VelocityIterations = iterations }

    match restingContact (advance 2000 (dropped cfg (box 0.5 0.5) (material 0.0 0.5) 2.0)) with
    | ValueSome m when m.PointCount = 2 -> abs (m.Points.[0].Y - m.Points.[1].Y)
    | _ -> failtest "the box did not come to a two-point rest"

/// True once the body has touched the floor and then left it again — the only externally visible
/// signature of a bounce, given that velocity is not readable.
let private bouncesWithin (n: int) (w0: Physics.World) =
    let mutable w = w0
    let mutable touched = false
    let mutable left = false

    for _ in 1..n do
        w <- Physics.step w tick

        match Physics.manifold w 0 1 with
        | ValueSome _ -> touched <- true
        | ValueNone -> if touched then left <- true

    touched, left

[<Tests>]
let stepTests =
    testList
        "Game.Core Physics step, narrow phase and checksum (#75)"
        [

          // -----------------------------------------------------------------------------------------
          // `step` IS `Loop.advance`'s `integrate`
          // -----------------------------------------------------------------------------------------

          test "step fits Loop.advance's integrate with no adapter" {
              // The whole reason `Config` is baked into the `World` at `empty`. If this stops compiling,
              // the signature has drifted and `Physics` has stopped being usable as a `Loop` world —
              // which is the claim `Physics.fsi` makes in its first paragraph.
              let w = dropped stepConfig (box 0.5 0.5) (material 0.0 0.5) 5.0

              let state: StepState<Physics.World> =
                  { Current = w
                    Previous = w
                    Accumulator = 0.0 }

              // No adapter, no lambda, no shim: `Physics.step` is passed straight in as `integrate`.
              let advanced = Loop.advance tick Physics.step 0.05 state

              Expect.notEqual
                  (Physics.checksum advanced.Current)
                  (Physics.checksum state.Current)
                  "Loop.advance drove Physics.step, and the world moved"
          }

          // -----------------------------------------------------------------------------------------
          // Totality of `step`
          // -----------------------------------------------------------------------------------------

          test "a non-finite or non-positive dt is a no-op" {
              let w = advance 90 (dropped stepConfig (box 0.5 0.5) (material 0.0 0.5) 5.0)
              let before = Physics.checksum w

              for dt in [ 0.0; -1.0; -0.0; nan; infinity; -infinity ] do
                  Expect.equal
                      (Physics.checksum (Physics.step w dt))
                      before
                      (sprintf "dt = %f leaves the world untouched" dt)
          }

          test "an empty world steps to itself" {
              let e = Physics.empty stepConfig
              Expect.equal (Physics.checksum (Physics.step e tick)) (Physics.checksum e) "nothing to integrate"
          }

          test "a static body never moves, however long it is stepped" {
              // A world of one static floor and nothing else: gravity must not touch it.
              let w = Physics.empty stepConfig
              let struct (_, w) = Physics.addBody Physics.Static (box 50.0 1.0) (material 0.0 0.5) (p 0.0 0.0) w
              Expect.equal (Physics.checksum (advance 600 w)) (Physics.checksum w) "infinite mass, zero motion"
          }

          test "step is total on degenerate and non-finite bodies, and a real body among them still settles" {
              // `pairs` totality on these no-collision inputs is asserted in the broad-phase list; this
              // drives the SAME zoo through the whole solver — integrate, narrow phase, solve, correct,
              // sleep — for 600 ticks, the thing `pairs` alone never exercises. Every body here is a
              // no-collision input (zero/NaN shape, degenerate ring, non-finite position), so `step` must
              // carry them without throwing and without minting a contact.
              let degenerate =
                  [ Physics.Dynamic, Physics.SCircle 0.0, p 0.0 5.0 // zero radius
                    Physics.Dynamic, Physics.SCircle nan, p 1.0 5.0 // NaN radius
                    Physics.Dynamic, box 0.0 1.0, p 2.0 5.0 // zero half-extent
                    Physics.Dynamic, Physics.SPoly { Vertices = [| p 0.0 0.0; p 1.0 1.0 |] }, p 3.0 5.0 // < 3 vertices
                    Physics.Dynamic, Physics.SCircle 1.0, p nan 5.0 // NaN position
                    Physics.Dynamic, Physics.SCircle 1.0, p infinity 5.0 ] // infinite position

              // A real floor and a real box dropped among the zoo, so the run is not vacuously total over
              // bodies the solver skips entirely: body 6 is a floor, body 7 falls onto it and must rest.
              let bodies =
                  degenerate
                  @ [ Physics.Static, box 50.0 1.0, p 0.0 0.0
                      Physics.Dynamic, box 0.5 0.5, p 0.0 3.0 ]

              let w = worldOf 4.0 bodies |> advance 600

              // No throw got us here. The degenerate bodies collide with nothing...
              for i in 0..5 do
                  for j in 0..7 do
                      if i <> j then
                          Expect.isTrue
                              (ValueOption.isNone (Physics.manifold w (min i j) (max i j)))
                              (sprintf "degenerate body %d contacts nothing, even after 600 steps" i)

              // ...and the real box came to rest on the real floor, so the solver genuinely ran.
              Expect.isTrue (ValueOption.isSome (Physics.manifold w 6 7)) "the real box settled on the real floor"
          }

          // Kinematic bodies through `step`. The public surface has NO velocity setter — `addBody` seeds
          // every body at rest and gravity is the only thing that imparts a velocity (the speculative-CCD
          // section below leans on the same fact) — so the "moved by the game" leg of the Kinematic
          // contract cannot be driven from here. What CAN be observed is its shadow and the other two
          // legs: gravity/contacts do not move it (only the game does), and it holds a load unpushed.

          test "a kinematic body is unmoved by gravity — only a dynamic one falls" {
              // Kinematic, like Static, moves ONLY under a velocity the game gives it; at rest under
              // gravity it stays put. The control is the identical scene with a Dynamic body, which falls.
              let build kind =
                  let w = Physics.empty stepConfig
                  let struct (_, w) = Physics.addBody kind (box 0.5 0.5) (material 0.0 0.5) (p 0.0 10.0) w
                  w

              let kin = build Physics.Kinematic
              Expect.equal (Physics.checksum (advance 600 kin)) (Physics.checksum kin) "kinematic: gravity does not move it"
              let dyn = build Physics.Dynamic
              Expect.notEqual (Physics.checksum (advance 600 dyn)) (Physics.checksum dyn) "control: a dynamic body does fall"
          }

          test "a kinematic floor holds a dynamic load without being pushed by it" {
              // "unmoved by impulses" + "holds a load": a dynamic box dropped onto a KINEMATIC floor comes
              // to rest ON it (infinite mass — things pile on it, not through it), and the floor's own pose
              // is left bit-identical by the resting contact's impulse. Control: a DYNAMIC floor (nothing
              // holding it up) does move.
              let build floorKind =
                  let w = Physics.empty stepConfig
                  let struct (fi, w) = Physics.addBody floorKind (box 50.0 1.0) (material 0.0 0.5) (p 0.0 0.0) w
                  let struct (di, w) = Physics.addBody Physics.Dynamic (box 0.5 0.5) (material 0.0 0.5) (p 0.0 5.0) w
                  struct (fi, di, w)

              let poseOf i w = (Physics.interpolate 1.0 w w).[i].Position

              let struct (fi, di, w0) = build Physics.Kinematic
              let floorBefore = poseOf fi w0
              let settled = advance 600 w0
              Expect.isTrue
                  (ValueOption.isSome (Physics.manifold settled fi di))
                  "the dynamic load rests ON the kinematic floor, not through it"
              Expect.equal (poseOf fi settled) floorBefore "the kinematic floor is not pushed by the load it holds"

              // Control: a dynamic floor under the same load is NOT held fixed.
              let struct (fi2, _, w1) = build Physics.Dynamic
              let dynFloorBefore = poseOf fi2 w1
              Expect.notEqual (poseOf fi2 (advance 600 w1)) dynFloorBefore "control: a dynamic floor moves"
          }

          // -----------------------------------------------------------------------------------------
          // Integrate, solve, correct
          // -----------------------------------------------------------------------------------------

          test "a dropped box falls, lands, and rests within slop of the floor" {
              let scene () = dropped stepConfig (box 0.5 0.5) (material 0.0 0.5) 5.0

              Expect.isTrue (ValueOption.isNone (restingContact (scene ()))) "starts clear of the floor"

              match restingContact (advance 600 (scene ())) with
              | ValueNone -> failtest "the box fell through the floor, or never reached it"
              | ValueSome m ->
                  // Penetration settles AT the slop — the correction stops there by construction — and
                  // the box rests on the floor's top surface rather than inside it.
                  Expect.isLessThanOrEqual m.Depth (stepConfig.Slop + 1e-6) "penetration settles within slop"
                  Expect.equal m.PointCount 2 "a flat box on a flat floor is a face-on-face contact"

                  // Both contact points sit on the floor's top surface, to within the penetration allowed
                  // plus the residual tilt described in this section's header (~1e-3 after 600 ticks).
                  // Demanding they sit at an IDENTICAL height would be asserting the tilt away.
                  for q in m.Points.[0 .. m.PointCount - 1] do
                      Expect.isLessThan
                          (abs (q.Y - floorTop))
                          (stepConfig.Slop + 1e-3)
                          "contact sits on the floor surface, within slop"
          }

          test "a resting box does not sink: penetration is bounded for 5000 ticks" {
              // The failure this catches is a positional correction that under-pushes, letting each tick's
              // gravity add a sliver of overlap that never comes back out.
              //
              // Measured only AFTER the box has settled. The tick it lands on penetrates well past slop —
              // it arrives with speed, and one step of gravity carries it in — and the correction takes a
              // few dozen ticks to push that back out. That transient is not sinking; including it would
              // make this test pass for the wrong reason (a bound loose enough to cover the impact would
              // also cover a slow sink).
              let settleTicks = 300
              let mutable w = advance settleTicks (dropped stepConfig (box 0.5 0.5) (material 0.0 0.5) 2.0)
              let mutable worst = 0.0

              for _ in 1 .. 5000 - settleTicks do
                  w <- Physics.step w tick

                  match restingContact w with
                  | ValueSome m -> worst <- max worst m.Depth
                  | ValueNone -> failtest "the box left the floor"

              Expect.isLessThanOrEqual worst (stepConfig.Slop + 1e-6) "overlap never accumulates once settled"
          }

          test "a dropped circle rests on the floor with a single contact point" {
              match restingContact (advance 600 (dropped stepConfig (Physics.SCircle 0.5) (material 0.0 0.5) 5.0)) with
              | ValueNone -> failtest "the circle fell through the floor"
              | ValueSome m ->
                  Expect.equal m.PointCount 1 "a circle touches a plane at one point"
                  Expect.isLessThanOrEqual m.Depth (stepConfig.Slop + 1e-6) "penetration settles within slop"
                  Expect.floatClose Accuracy.medium m.Normal.Y 1.0 "the floor pushes the circle straight up"
          }

          test "two stacked boxes both rest within slop" {
              let w = Physics.empty stepConfig
              let struct (_, w) = Physics.addBody Physics.Static (box 50.0 1.0) (material 0.0 0.5) (p 0.0 0.0) w
              let struct (_, w) = Physics.addBody Physics.Dynamic (box 0.5 0.5) (material 0.0 0.5) (p 0.0 1.6) w
              let struct (_, w) = Physics.addBody Physics.Dynamic (box 0.5 0.5) (material 0.0 0.5) (p 0.0 2.7) w
              let settled = advance 1200 w

              match Physics.manifold settled 0 1, Physics.manifold settled 1 2 with
              | ValueSome lower, ValueSome upper ->
                  Expect.isLessThanOrEqual lower.Depth (stepConfig.Slop + 1e-3) "the lower box rests on the floor"
                  Expect.isLessThanOrEqual upper.Depth (stepConfig.Slop + 1e-3) "the upper box rests on the lower"
              | _ -> failtest "the stack collapsed or fell through"
          }

          test "a polygon's mass is its area, wherever the ring sits relative to its origin" {
              // Mass is derived from the shape (unit density), and the fan sum that measures a polygon's
              // area must take the sign off the TOTAL, never off each fan triangle. A triangle whose
              // winding opposes the ring contributes a negative area, and that cancellation is how the fan
              // measures a ring the origin lies OUTSIDE of. Summing `abs` per term instead weighs such a
              // polygon many times over — 21x for `[(10,0); (11,0); (10,1)]`, 2x for the square below.
              //
              // Mass is not readable, so this observes it through an impulse. The scene is built so that
              // every contact normal is vertical and passes through every body's origin, making every
              // lever arm `r x n = 0`: rotational inertia (which legitimately DOES change when the origin
              // moves, by the parallel-axis theorem) drops out entirely, and the only thing left that can
              // move the trace is the polygon's mass.
              //
              //   body 0  static floor, top surface at y = 1
              //   body 1  dynamic circle resting on it at (0, 1.5)
              //   body 2  dynamic unit square, world centre (0, 3.1), falling onto the circle
              //
              // The circle's penetration into the floor is the readout: a heavier square drives it deeper.
              let scene (vertexOffset: Point) =
                  let h = 0.5

                  let vs =
                      [| p -h -h; p h -h; p h h; p -h h |]
                      |> Array.map (fun v -> p (v.X + vertexOffset.X) (v.Y + vertexOffset.Y))

                  // Move the body by -offset so the WORLD square is identical for every offset.
                  let bodyPos = p (0.0 - vertexOffset.X) (3.1 - vertexOffset.Y)
                  let w = Physics.empty stepConfig
                  let struct (_, w) = Physics.addBody Physics.Static (box 50.0 1.0) (material 0.0 0.5) (p 0.0 0.0) w
                  let struct (_, w) = Physics.addBody Physics.Dynamic (Physics.SCircle 0.5) (material 0.0 0.5) (p 0.0 1.5) w
                  let struct (_, w) = Physics.addBody Physics.Dynamic (Physics.SPoly { Vertices = vs }) (material 0.0 0.5) bodyPos w
                  w

              let trace (vertexOffset: Point) =
                  let mutable w = scene vertexOffset

                  [ for _ in 1..200 ->
                        w <- Physics.step w tick

                        match Physics.manifold w 0 1 with
                        | ValueSome m -> m.Depth
                        | ValueNone -> 0.0 ]

              let maxDiff a b = List.map2 (fun x y -> abs (x - y)) a b |> List.max

              let originInside = trace (p 0.0 0.0) // origin at the square's centre
              let originBelow = trace (p 0.0 1.5) // origin outside the ring, below it
              let originAbove = trace (p 0.0 -4.0) // origin outside the ring, well above it

              Expect.isLessThan (maxDiff originInside originBelow) 1e-12 "the same square weighs the same with its origin below it"
              Expect.isLessThan (maxDiff originInside originAbove) 1e-12 "...and with its origin far above it"

              // The readout has teeth: a genuinely heavier square DOES move it. Without this, the two
              // assertions above would pass just as happily against a trace that ignored mass entirely.
              let genuinelyHeavier =
                  let mutable w =
                      let vs = [| p -0.7 -0.7; p 0.7 -0.7; p 0.7 0.7; p -0.7 0.7 |]
                      let w = Physics.empty stepConfig
                      let struct (_, w) = Physics.addBody Physics.Static (box 50.0 1.0) (material 0.0 0.5) (p 0.0 0.0) w
                      let struct (_, w) = Physics.addBody Physics.Dynamic (Physics.SCircle 0.5) (material 0.0 0.5) (p 0.0 1.5) w
                      let struct (_, w) = Physics.addBody Physics.Dynamic (Physics.SPoly { Vertices = vs }) (material 0.0 0.5) (p 0.0 3.1) w
                      w

                  [ for _ in 1..200 ->
                        w <- Physics.step w tick

                        match Physics.manifold w 0 1 with
                        | ValueSome m -> m.Depth
                        | ValueNone -> 0.0 ]

              Expect.isGreaterThan (maxDiff originInside genuinelyHeavier) 1e-4 "a square of larger area is a heavier square"
          }

          // -----------------------------------------------------------------------------------------
          // Torque: the reason contact points exist at all (design §5 gap 1)
          // -----------------------------------------------------------------------------------------

          test "an overhanging box tips off a pedestal instead of landing flat" {
              // A box whose centre of mass overhangs its support must rotate. Without lever arms from the
              // contact points there is no torque, and it would balance forever. The rotation is visible
              // in the contact NORMAL swinging away from straight-up, and in the face-on-face contact
              // collapsing to a single corner point.
              let w = Physics.empty stepConfig
              let struct (_, w) = Physics.addBody Physics.Static (box 0.25 1.0) (material 0.0 0.9) (p 0.0 0.0) w
              let struct (_, w) = Physics.addBody Physics.Dynamic (box 0.5 0.1) (material 0.0 0.9) (p 0.55 1.2) w

              let mutable acc = w
              let mutable landedFlat = false
              let mutable maxTiltOfNormal = 0.0

              for _ in 1..30 do
                  acc <- Physics.step acc tick

                  match Physics.manifold acc 0 1 with
                  | ValueSome m ->
                      if m.PointCount = 2 && abs m.Normal.X < 1e-9 then landedFlat <- true
                      maxTiltOfNormal <- max maxTiltOfNormal (abs m.Normal.X)
                  | ValueNone -> ()

              Expect.isTrue landedFlat "it first lands flat, normal straight up"
              Expect.isGreaterThan maxTiltOfNormal 0.5 "then it tips: the contact normal rotates well off vertical"
          }

          test "the resting tilt is a convergence residual, not a spin-up" {
              // Documents the artifact the header describes. A resting box holds a small residual angular
              // velocity at a fixed 8 iterations; it is Gauss-Seidel not yet converged, so it must shrink
              // toward zero as iterations rise. If this ever fails, the solver has a real torque bug and
              // no iteration count will save it.
              //
              // Sleeping is OFF here. With it on the box stops after ~112 ticks and this measures the pose
              // it happened to freeze in — which is a fact about the sleep threshold, not about the solver.
              let coarse = tiltAfter noSleep 8
              let fine = tiltAfter noSleep 64

              Expect.isLessThan fine coarse "more iterations converge closer to a level rest"
              Expect.isLessThan fine 1e-9 "and 64 iterations reach a level rest to within float noise"
          }

          // -----------------------------------------------------------------------------------------
          // Restitution
          // -----------------------------------------------------------------------------------------

          test "a bouncy body leaves the floor again; a dead one does not" {
              let bouncy = dropped stepConfig (Physics.SCircle 0.5) (material 0.9 0.0) 5.0
              let dead = dropped stepConfig (Physics.SCircle 0.5) (material 0.0 0.0) 5.0

              let touchedB, leftB = bouncesWithin 400 bouncy
              let touchedD, leftD = bouncesWithin 400 dead

              Expect.isTrue touchedB "the bouncy ball reaches the floor"
              Expect.isTrue leftB "and rebounds off it"
              Expect.isTrue touchedD "the dead ball reaches the floor"
              Expect.isFalse leftD "and stays there"
          }

          test "restitution combines as the MAXIMUM of the two materials" {
              // The floor `dropped` builds has `Restitution = 0.0`, as floors do. Under a `min` rule the
              // ball could never bounce off it, and restitution would be unreachable in the very scene it
              // exists for. This test is the guard on that choice.
              let _, left = bouncesWithin 400 (dropped stepConfig (Physics.SCircle 0.5) (material 0.9 0.0) 5.0)
              Expect.isTrue left "a bouncy ball bounces off a dead floor"
          }

          test "restitution is gated below BounceThreshold" {
              // Identical ball, identical drop — only the gate moves. Raising the threshold above any
              // approach speed the fall can produce forces `e = 0` at impact, so the ball that bounced in
              // the test above now stays down. That gate is what stops a resting box trading a sliver of
              // approach velocity for a sliver of bounce, forever.
              let never = { stepConfig with BounceThreshold = 1e9 }
              let always = stepConfig

              let _, leftGated = bouncesWithin 400 (dropped never (Physics.SCircle 0.5) (material 0.9 0.0) 5.0)
              let _, leftUngated = bouncesWithin 400 (dropped always (Physics.SCircle 0.5) (material 0.9 0.0) 5.0)

              Expect.isFalse leftGated "an approach below the threshold is perfectly inelastic"
              Expect.isTrue leftUngated "and the same ball above the threshold does bounce"
          }

          // -----------------------------------------------------------------------------------------
          // Friction
          // -----------------------------------------------------------------------------------------

          test "friction decides whether a box holds on a ramp or slides off it" {
              // A CCW right triangle whose hypotenuse runs from (-5,-2) up to (5,2): a ramp. The box is
              // placed on the slope. With no friction it slides off the end; with plenty it stays put.
              let ramp = Physics.SPoly { Vertices = [| p -5.0 -2.0; p 5.0 -2.0; p 5.0 2.0 |] }

              let onRamp mu =
                  let w = Physics.empty stepConfig
                  let struct (_, w) = Physics.addBody Physics.Static ramp (material 0.0 mu) (p 0.0 0.0) w
                  let struct (_, w) = Physics.addBody Physics.Dynamic (box 0.3 0.3) (material 0.0 mu) (p 1.0 0.9) w
                  Physics.manifold (advance 300 w) 0 1

              Expect.isTrue (ValueOption.isNone (onRamp 0.0)) "a frictionless box slides off the ramp"
              Expect.isTrue (ValueOption.isSome (onRamp 0.9)) "a rough box is still on the ramp"
          }

          test "friction combines from BOTH materials — a frictionless partner frees the pair, in either order" {
              // The companion to "restitution combines as the MAXIMUM". Friction is the geometric mean
              // sqrt(μa·μb), so a single frictionless body zeroes the pair however rough the OTHER is — and
              // the rule must read both bodies' μ to do it. The ramp test above gives the two bodies the
              // SAME μ; these differ, so a rule that read only one body's μ is caught here.
              //
              // A rough box on a frictionless ramp slides: sqrt(0.9·0) = 0, not the max 0.9. Catches a rule
              // that ignores the ramp's μ.
              Expect.isFalse (holdsOnRamp 2.0 0.0 0.9) "a rough box slides on a frictionless ramp"
              // ...and symmetrically, a frictionless box on a rough ramp. Catches a rule that ignores the box's μ.
              Expect.isFalse (holdsOnRamp 2.0 0.9 0.0) "a frictionless box slides on a rough ramp"
              // Anti-vacuity: this ramp CAN hold a rough pair, so "everything slides" is not why the two above pass.
              Expect.isTrue (holdsOnRamp 2.0 0.9 0.9) "and two rough bodies hold — the ramp is not simply too steep"
          }

          test "the combined friction is the geometric mean, not the minimum" {
              // sqrt(0.25·1.0) = 0.5 — the SAME effective μ as a symmetric 0.5/0.5 pair, yet a minimum rule
              // would read the asymmetric pair as 0.25. On a gentler ramp (rise 1.5 over run 10, tan θ = 0.3)
              // tuned so 0.5 holds and 0.25 slides, the asymmetric 0.25/1.0 pair must HOLD — pinning the
              // combination as the geometric mean rather than the minimum of the two.
              Expect.isTrue (holdsOnRamp 1.5 0.5 0.5) "calibration: an effective μ of 0.5 holds on this ramp"
              Expect.isFalse (holdsOnRamp 1.5 0.25 0.25) "calibration: an effective μ of 0.25 slides on this ramp"
              Expect.isTrue (holdsOnRamp 1.5 0.25 1.0) "sqrt(0.25·1.0) = 0.5 holds — the geometric mean, not the min 0.25"
          }

          // -----------------------------------------------------------------------------------------
          // The narrow phase
          // -----------------------------------------------------------------------------------------

          test "manifold reports A and B as the body indices, in the order asked" {
              let w = worldOf 8.0 [ Physics.Dynamic, Physics.SCircle 1.0, p 0.0 0.0
                                    Physics.Dynamic, Physics.SCircle 1.0, p 1.5 0.0 ]

              match Physics.manifold w 0 1 with
              | ValueSome m ->
                  Expect.equal (m.A, m.B) (0, 1) "A and B index the world, not polygonManifold's 0 and 1"
              | ValueNone -> failtest "the circles overlap"
          }

          test "the contact normal always points from a toward b" {
              let w = worldOf 8.0 [ Physics.Dynamic, Physics.SCircle 1.0, p 0.0 0.0
                                    Physics.Dynamic, Physics.SCircle 1.0, p 1.5 0.0 ]

              match Physics.manifold w 0 1, Physics.manifold w 1 0 with
              | ValueSome ab, ValueSome ba ->
                  Expect.floatClose Accuracy.medium ab.Normal.X 1.0 "0 -> 1 points along +x"
                  Expect.floatClose Accuracy.medium ba.Normal.X -1.0 "1 -> 0 points along -x"
                  Expect.floatClose Accuracy.medium ab.Depth ba.Depth "depth does not depend on the order"
              | _ -> failtest "the circles overlap in both orders"
          }

          test "a circle against a box normals the same way whichever is a" {
              let cb = worldOf 8.0 [ Physics.Dynamic, Physics.SCircle 0.5, p 0.0 0.0
                                     Physics.Dynamic, box 0.5 0.5, p 0.8 0.0 ]

              let bc = worldOf 8.0 [ Physics.Dynamic, box 0.5 0.5, p 0.0 0.0
                                     Physics.Dynamic, Physics.SCircle 0.5, p 0.8 0.0 ]

              match Physics.manifold cb 0 1, Physics.manifold bc 0 1 with
              | ValueSome circleFirst, ValueSome boxFirst ->
                  Expect.floatClose Accuracy.medium circleFirst.Normal.X 1.0 "circle -> box points at the box"
                  Expect.floatClose Accuracy.medium boxFirst.Normal.X 1.0 "box -> circle points at the circle"
              | _ -> failtest "both pairs overlap"
          }

          test "a touch is not a contact: two circles at exactly d = ra + rb" {
              // The strict-edge convention `pairs`, `aabbContact` and `polygonManifold` all share. The
              // coordinates are exact in binary, so this is a real equality, not a near-miss.
              let w = worldOf 8.0 [ Physics.Dynamic, Physics.SCircle 1.0, p 0.0 0.0
                                    Physics.Dynamic, Physics.SCircle 1.0, p 2.0 0.0 ]

              Expect.isTrue (ValueOption.isNone (Physics.manifold w 0 1)) "touching circles do not contact"
          }

          test "coincident circle centres yield no contact rather than an invented normal" {
              let w = worldOf 8.0 [ Physics.Dynamic, Physics.SCircle 1.0, p 0.0 0.0
                                    Physics.Dynamic, Physics.SCircle 1.0, p 0.0 0.0 ]

              Expect.isTrue (ValueOption.isNone (Physics.manifold w 0 1)) "there is no direction to separate along"
          }

          test "manifold is total on bad indices and degenerate bodies" {
              let w = worldOf 8.0 [ Physics.Dynamic, Physics.SCircle 1.0, p 0.0 0.0
                                    Physics.Dynamic, Physics.SCircle 1.0, p 0.5 0.0
                                    Physics.Dynamic, Physics.SCircle 0.0, p 0.5 0.0
                                    Physics.Dynamic, Physics.SCircle 1.0, p nan 0.0 ]

              Expect.isTrue (ValueOption.isNone (Physics.manifold w 0 0)) "a body does not contact itself"
              Expect.isTrue (ValueOption.isNone (Physics.manifold w 0 99)) "an out-of-range index"
              Expect.isTrue (ValueOption.isNone (Physics.manifold w -1 0)) "a negative index"
              Expect.isTrue (ValueOption.isNone (Physics.manifold w 1 2)) "a degenerate shape collides with nothing"
              Expect.isTrue (ValueOption.isNone (Physics.manifold w 1 3)) "a non-finite position collides with nothing"
          }

          test "manifold agrees with pairs: no pair, no contact" {
              // The broad phase is exact over AABBs, so a rejected pair cannot have had a contact to lose.
              let bodies =
                  [ Physics.Static, box 20.0 1.0, p 0.0 -1.5
                    Physics.Dynamic, Physics.SCircle 1.5, p 1.0 0.5
                    Physics.Dynamic, box 0.5 0.5, p 12.0 8.0 ]

              let w = worldOf 8.0 bodies
              let paired = pairList w |> Set.ofList

              for a in 0..2 do
                  for b in 0..2 do
                      if a < b && not (paired.Contains(a, b)) then
                          Expect.isTrue
                              (ValueOption.isNone (Physics.manifold w a b))
                              (sprintf "(%d, %d) is not a pair, so it has no contact" a b)
          }

          test "circle feature ids never collide with polygonManifold's" {
              // `polygonManifold` packs a face pair into a NON-NEGATIVE int; the circle cases mint theirs
              // from the negative half. #76's warm-start cache keys on this, so the disjointness matters.
              let cc = worldOf 8.0 [ Physics.Dynamic, Physics.SCircle 1.0, p 0.0 0.0
                                     Physics.Dynamic, Physics.SCircle 1.0, p 1.5 0.0 ]

              let cb = worldOf 8.0 [ Physics.Dynamic, Physics.SCircle 0.5, p 0.0 0.0
                                     Physics.Dynamic, box 0.5 0.5, p 0.8 0.0 ]

              let pp = worldOf 8.0 [ Physics.Dynamic, box 0.5 0.5, p 0.0 0.0
                                     Physics.Dynamic, box 0.5 0.5, p 0.8 0.0 ]

              match Physics.manifold cc 0 1, Physics.manifold cb 0 1, Physics.manifold pp 0 1 with
              | ValueSome a, ValueSome b, ValueSome c ->
                  Expect.isLessThan a.FeatureId 0 "circle-circle ids are negative"
                  Expect.isLessThan b.FeatureId 0 "circle-polygon ids are negative"
                  Expect.isGreaterThanOrEqual c.FeatureId 0 "polygon-polygon ids come from polygonManifold"
              | _ -> failtest "all three pairs overlap"
          }

          test "an unmoving pair keeps its feature id across ticks" {
              // The warm-start cache key contract. A resting box on a floor must name the same feature
              // every tick, or #76 would discard its accumulated impulse on every step.
              let scene = dropped stepConfig (box 0.5 0.5) (material 0.0 0.5) 1.6
              let settled = advance 300 scene

              let ids =
                  [ 0..20 ]
                  |> List.map (fun i ->
                      match restingContact (advance i settled) with
                      | ValueSome m -> m.FeatureId
                      | ValueNone -> failtest "the box left the floor")
                  |> List.distinct

              Expect.equal ids.Length 1 "one stable feature id across 20 ticks of rest"
          }

          // -----------------------------------------------------------------------------------------
          // The checksum
          // -----------------------------------------------------------------------------------------

          test "identical worlds stepped identically checksum identically" {
              let run () = advance 240 (dropped stepConfig (box 0.5 0.5) (material 0.0 0.5) 5.0)
              Expect.equal (Physics.checksum (run ())) (Physics.checksum (run ())) "the replay tripwire does not fire on a replay"
          }

          test "the checksum moves when body state moves" {
              let scene = dropped stepConfig (box 0.5 0.5) (material 0.0 0.5) 5.0
              let before = Physics.checksum scene
              let after = Physics.checksum (Physics.step scene tick)
              Expect.notEqual before after "one tick of gravity changes the world"
          }

          test "the checksum distinguishes worlds of different body counts" {
              // The body count is folded in first precisely so that an extra all-zero body cannot hash
              // like its absence.
              let one = Physics.empty stepConfig
              let struct (_, two) = Physics.addBody Physics.Static (box 1.0 1.0) (material 0.0 0.0) (p 0.0 0.0) one

              Expect.notEqual (Physics.checksum one) (Physics.checksum two) "N and N+1 bodies hash apart"
          }

          test "the checksum is stable across the process, not just the run" {
              // Golden values. They are FNV-1a over the IEEE-754 bits of Pos/Vel/Rot/AngVel, so they are
              // reproducible on any runtime that agrees on IEEE-754 double arithmetic — which is what
              // `.NET` guarantees on a fixed compiler and ISA. A cross-platform lockstep guarantee needs
              // fixed-point, and is a later ADR'd decision (design §6).
              //
              // #76 MOVED `boxOnFloor`, and this is the deliberate record of it that the slice owed. The
              // old value was 12790444109480856124UL. Both of the slice's levers move it, independently:
              //
              //   warm starting alone (sleeping off)  -> 1048092559667977464UL
              //   warm starting and sleeping together -> the value asserted below
              //
              // The two values below did NOT move, and that is the interesting half. `empty` proves the
              // eight new `World` fields — two sleep arrays, six cache arrays — stay out of the hash (R3).
              // `circleOnFloor` proves something sharper: a circle on a floor is a ONE-point contact that
              // the solver converges exactly, so its rest is a true fixed point. Seeding a fixed point with
              // its own impulse returns it unchanged, and freezing a body that was not moving changes
              // nothing. Both levers are therefore exact no-ops on a converged rest — bit-for-bit, at 240
              // ticks and at 600.
              //
              // `boxOnFloor` moves precisely because its rest is NOT converged: it is the two-point contact
              // whose residual tilt the `#75` test above pins, and #76 both shrinks that residual and stops
              // the box before it can creep. A scene that had genuinely settled would not have noticed.
              let boxOnFloor = advance 240 (dropped stepConfig (box 0.5 0.5) (material 0.0 0.5) 5.0)
              let circleOnFloor = advance 240 (dropped stepConfig (Physics.SCircle 0.5) (material 0.0 0.5) 5.0)

              Expect.equal (Physics.checksum (Physics.empty stepConfig)) 12161962213042174405UL "empty world"
              Expect.equal (Physics.checksum boxOnFloor) 9427473436406466390UL "a box asleep after 240 ticks"
              Expect.equal (Physics.checksum circleOnFloor) 12544979940497693507UL "a circle at rest after 240 ticks"
          } ]

// =====================================================================================================
// Warm starting and sleeping — the performance slice (#76)
// =====================================================================================================
//
// Two levers, neither of which is supposed to change what the engine MEANS. They are measured through the
// same two keyholes the `#75` list uses — `manifold` and `checksum` — because #76 adds no public surface:
// `World` gained a sleep flag, a tick counter and a warm-start cache, and all three are hidden by the
// `.fsi`, unhashed by `checksum`, and observable only through what the simulation does.
//
//   * **Sleeping** is observed as a `checksum` that STOPS MOVING. That is a stronger reading than an
//     `isAsleep` accessor would give: it says the body stopped integrating, which is the thing sleeping is
//     for. Its converse matters as much — a scene with the lever off must never freeze — so every
//     freeze assertion below is paired with a no-freeze control.
//
//   * **Warm starting** is observed as CONVERGENCE. Seeding a contact with last tick's impulse cannot be
//     seen directly, but a solver that starts warm settles a box level in fewer iterations than one that
//     starts cold, and that gap is measurable. The cold numbers below were measured on the #75 build, the
//     last commit before the cache existed, and are quoted as constants because that build is gone.
//
// A silent-failure note, since the mechanism invites one. Warm starting is a linear merge of two sorted
// key sequences: last tick's cache and this tick's staged contacts. If those orders ever disagree, the
// merge finds no match, every contact starts cold, and the engine stays CORRECT while quietly losing the
// whole slice. Nothing throws. `warm starting beats the cold solver at a quarter of the iterations` is the
// tripwire for exactly that, and it is why the assertion is against an absolute cold constant rather than
// against "some improvement".

/// The residual tilt the #75 build — cold, no cache — left at a given velocity-iteration count, measured
/// by `tiltAfter` on the same scene before warm starting existed. Quoted, not computed: the build that
/// produced them cannot be linked alongside the one that must beat them.
let private coldTiltAt4 = 0.0095000108992362442
let private coldTiltAt10 = 0.00072038225065007566

/// True when a world has stopped changing: one more tick leaves every body's `Pos`/`Vel`/`Rot`/`AngVel`
/// bit-identical. For a body under gravity this can only mean it has stopped integrating.
let private frozen (w: Physics.World) =
    Physics.checksum (Physics.step w tick) = Physics.checksum w

/// `n` unit boxes stacked on the static floor, each dropped 0.02 above the one below so the stack settles
/// rather than starting in a contact. Body 0 is the floor; bodies 1..n rise from it.
let private stack (cfg: Physics.Config) (n: int) =
    let w = Physics.empty cfg
    let struct (_, w) = Physics.addBody Physics.Static (box 50.0 1.0) (material 0.0 0.5) (p 0.0 0.0) w
    let mutable acc = w

    for i in 0 .. n - 1 do
        let struct (_, grown) =
            Physics.addBody Physics.Dynamic (box 0.5 0.5) (material 0.0 0.5) (p 0.0 (1.5 + float i * 1.02)) acc

        acc <- grown

    acc

/// The first tick on which `w` stops changing, or `-1` if it never does within `limit`.
let private freezeTick (limit: int) (w0: Physics.World) =
    let mutable w = w0
    let mutable t = 0
    let mutable found = -1

    while found < 0 && t < limit do
        let next = Physics.step w tick
        t <- t + 1
        if Physics.checksum next = Physics.checksum w then found <- t
        w <- next

    found

[<Tests>]
let sleepAndWarmStartTests =
    testList
        "Game.Core Physics warm starting and sleeping (#76)"
        [

          // -----------------------------------------------------------------------------------------
          // Sleeping: a settled scene stops integrating
          // -----------------------------------------------------------------------------------------

          test "a box that comes to rest eventually stops integrating, and stays stopped" {
              let w = advance 200 (dropped stepConfig (box 0.5 0.5) (material 0.0 0.5) 5.0)

              Expect.isTrue (frozen w) "the box has fallen asleep"

              // Not merely "asleep this tick": a static floor is not a mover and must never wake it, so the
              // freeze has to survive an arbitrary wait. 5000 ticks is 83 seconds of game time.
              Expect.equal
                  (Physics.checksum (advance 5000 w))
                  (Physics.checksum w)
                  "and nothing wakes it, however long the scene is left running"
          }

          test "the same box never stops when the lever is off" {
              // The control for every freeze assertion above and below. Without it, a bug that froze the
              // world for the wrong reason — a solver that zeroed velocity, say — would read as a pass.
              let w = advance 200 (dropped noSleep (box 0.5 0.5) (material 0.0 0.5) 5.0)
              Expect.isFalse (frozen w) "with SleepTicks = 0 the box keeps creeping on its convergence residual"
          }

          test "each of the three thresholds disables sleeping on its own" {
              // `SleepTicks <= 0` is handled in code; the other two need none, because no squared speed is
              // below zero and NaN fails every `<`. All three are asserted, because "needs no code" is
              // exactly the claim that rots.
              let settled (cfg: Physics.Config) =
                  frozen (advance 400 (dropped cfg (box 0.5 0.5) (material 0.0 0.5) 5.0))

              Expect.isFalse (settled { stepConfig with SleepTicks = 0 }) "SleepTicks = 0 disables sleeping"
              Expect.isFalse (settled { stepConfig with SleepTicks = -1 }) "a negative SleepTicks disables it too"
              Expect.isFalse (settled { stepConfig with SleepLinearSq = 0.0 }) "SleepLinearSq = 0 disables sleeping"
              Expect.isFalse (settled { stepConfig with SleepAngular = 0.0 }) "SleepAngular = 0 disables sleeping"
              Expect.isTrue (settled stepConfig) "and the default tuning does sleep"
          }

          test "the sleep counter wants CONSECUTIVE ticks, not a total" {
              // A bouncy ball passes below the linear sleep threshold at the top of every arc, where its
              // velocity turns over. A counter that ACCUMULATED those ticks would reach `SleepTicks` and
              // freeze the ball in mid-air. A counter that demands them in a row never does.
              //
              // The horizon is chosen so the test can actually falsify that: it must run long enough for
              // MORE THAN `SleepTicks` apexes to have gone by, or a cumulative counter would not have
              // fired either and the assertion would prove nothing. At 1200 ticks there are only ~25.
              // So the apex count is asserted too — the test checks its own premise rather than trusting a
              // tick number to stay meaningful.
              let mutable w = dropped stepConfig (Physics.SCircle 0.5) (material 0.9 0.0) 5.0
              let mutable inContact = false
              let mutable apexes = 0

              for _ in 1..4000 do
                  w <- Physics.step w tick
                  let touching = (restingContact w).IsSome
                  if touching && not inContact then apexes <- apexes + 1
                  inContact <- touching

              Expect.isGreaterThan
                  apexes
                  stepConfig.SleepTicks
                  "the ball left and re-met the floor more often than a cumulative counter would have needed"

              Expect.isFalse (frozen w) "yet it never slept, because it was never still for SleepTicks in a row"
          }

          test "a body in free fall never sleeps" {
              // Nothing to rest on, and gravity means the speed threshold is crossed upward, never down.
              let w = Physics.empty stepConfig
              let struct (_, w) = Physics.addBody Physics.Dynamic (Physics.SCircle 0.5) (material 0.0 0.0) (p 0.0 0.0) w

              Expect.isFalse (frozen (advance 400 w)) "a falling body is not a resting one"
          }

          // -----------------------------------------------------------------------------------------
          // Sleeping: a stack, which is where the lever is worth having — and where it is easy to get wrong
          // -----------------------------------------------------------------------------------------

          test "a stack of any height settles and sleeps, together" {
              // The regression that matters, and it guards TWO invariants that have no cheaper observable.
              //
              // 1. Waking keys on whether a neighbour is MOVING, not on whether it is awake. Keyed on
              //    "awake", two stacked bodies whose sleep counters filled a tick apart would wake each
              //    other forever. A stack of two might still sleep, by the luck of both counters filling on
              //    the same tick; a stack of THREE never would. Hence the sweep over heights — `n = 1` and
              //    `n = 2` pass under the bug.
              //
              // 2. A sleeping body is immovable (zero effective inverse mass), not merely un-integrated.
              //    A sleeper that kept its real mass would absorb impulses it never spends, so the awake
              //    body resting on it would never have its gravity fully cancelled, would never come to
              //    rest, and would never sleep. That is a cumulative effect with no single-tick signature:
              //    a body woken this tick starts at zero velocity, so the one tick it leans on a still-
              //    sleeping neighbour carries no impulse worth measuring. This test is where it shows.
              for n in 1..5 do
                  let t = freezeTick 1200 (stack stepConfig n)
                  Expect.isGreaterThan t 0 (sprintf "a stack of %d comes to a complete stop" n)

                  // The control: the same stack with the lever off never stops in that window. Without it,
                  // a stack that merely converged to a bit-stable pose would pass the freeze above for the
                  // wrong reason — the assertion would be about the solver, not about sleeping.
                  Expect.equal
                      (freezeTick 1200 (stack noSleep n))
                      -1
                      (sprintf "and a stack of %d with sleeping off keeps creeping" n)
          }

          test "more solver iterations reach sleep sooner, never later" {
              // A monotonicity check, and the second half of the mutual-waking regression: under the
              // "awake wakes the sleeper" bug this sequence was not merely slow but UNORDERED, because
              // whether a stack ever slept turned on counters aligning rather than on convergence.
              let ticks = [ 4; 8; 16; 32 ] |> List.map (fun it -> freezeTick 1200 (stack { stepConfig with VelocityIterations = it } 4))

              Expect.allEqual (ticks |> List.map (fun t -> t > 0)) true "every iteration count reaches sleep"
              Expect.isTrue (ticks = List.sortDescending ticks) (sprintf "sleep arrives no later as iterations rise: %A" ticks)
          }

          test "relaxing a sleep threshold never delays sleep" {
              // The other monotonicity. Under the mutual-waking bug, relaxing BOTH thresholds made a stack
              // that had slept stop sleeping — bodies dozed off out of step and woke one another. A
              // threshold is a permission; loosening it cannot take sleep away.
              let baseline = freezeTick 1200 (stack stepConfig 4)
              let looserAngular = freezeTick 1200 (stack { stepConfig with SleepAngular = 0.1 } 4)
              let looserBoth = freezeTick 1200 (stack { stepConfig with SleepAngular = 0.1; SleepLinearSq = 1.0 } 4)

              Expect.isGreaterThan baseline 0 "the baseline stack sleeps at all"
              Expect.isLessThanOrEqual looserAngular baseline "a looser angular threshold sleeps no later"
              Expect.isLessThanOrEqual looserBoth looserAngular "and loosening both sleeps no later still"
          }

          test "a settled stack holds its shape rather than sinking into itself" {
              // Sleeping must not be a way to hide a sinking stack: freezing a scene mid-penetration would
              // pass every `frozen` assertion above. Every contact must be resting at the slop.
              let w = advance 1200 (stack stepConfig 4)
              Expect.isTrue (frozen w) "the stack is asleep"

              let mutable contacts = 0

              for a in 0..4 do
                  for b in a + 1 .. 4 do
                      match Physics.manifold w a b with
                      | ValueSome m ->
                          contacts <- contacts + 1
                          Expect.isLessThan m.Depth (stepConfig.Slop + 1e-4) "no contact is penetrating past the slop"
                      | ValueNone -> ()

              // floor-1, 1-2, 2-3, 3-4: a chain, and nothing skipping a link.
              Expect.equal contacts 4 "the stack is intact: four contacts, each between neighbours"
          }

          // -----------------------------------------------------------------------------------------
          // Waking
          // -----------------------------------------------------------------------------------------

          test "a falling body wakes the sleeper it lands on, and the scene re-sleeps" {
              let settled =
                  let w = Physics.empty stepConfig
                  let struct (_, w) = Physics.addBody Physics.Static (box 50.0 1.0) (material 0.0 0.5) (p 0.0 0.0) w
                  let struct (_, w) = Physics.addBody Physics.Dynamic (box 0.5 0.5) (material 0.0 0.5) (p 0.0 1.5) w
                  advance 200 w

              Expect.isTrue (frozen settled) "the first box is asleep before anything is dropped on it"

              // `addBody` must not disturb the sleeper by itself — only the falling body may.
              let struct (_, disturbed) =
                  Physics.addBody Physics.Dynamic (box 0.5 0.5) (material 0.0 0.5) (p 0.0 4.0) settled

              let landed = advance 120 disturbed

              match Physics.manifold landed 1 2 with
              | ValueSome m -> Expect.isLessThan m.Depth (stepConfig.Slop + 1e-4) "the dropped box rests ON the sleeper"
              | ValueNone -> failtest "the dropped box never reached the sleeper"

              // It did not tunnel through into the floor, which is what a body treated as absent would let it do.
              Expect.isTrue (Physics.manifold landed 0 2).IsNone "and never touches the floor through it"

              Expect.isTrue (frozen (advance 600 disturbed)) "and the disturbed pair settles back to sleep"
          }

          test "adding a body does not itself wake a settled scene" {
              // The warm-start cache is keyed on body indices and `addBody` only ever appends, so no entry
              // is invalidated and no sleeper need be disturbed. A body added far away must change nothing.
              let settled = advance 200 (dropped stepConfig (box 0.5 0.5) (material 0.0 0.5) 5.0)
              let before = Physics.checksum settled

              let struct (_, grown) =
                  Physics.addBody Physics.Dynamic (Physics.SCircle 0.5) (material 0.0 0.5) (p 40.0 40.0) settled

              // The far body falls; the sleeper does not stir. Their state is hashed together, so compare
              // the sleeper through the contact it holds with the floor instead.
              match restingContact settled, restingContact (Physics.step grown tick) with
              | ValueSome a, ValueSome b ->
                  Expect.equal b.Depth a.Depth "the sleeper's penetration is untouched by a body added across the world"
              | _ -> failtest "the box left the floor"

              Expect.equal (Physics.checksum settled) before "and `addBody` did not step the world"
          }

          // -----------------------------------------------------------------------------------------
          // Warm starting
          // -----------------------------------------------------------------------------------------

          test "warm starting beats the cold solver at a quarter of the iterations" {
              // The design's own claim: "a warm-started 4-iteration solver beats a cold 10-iteration one on
              // stacks." Asserted against the #75 build's measured residual, because a warm build cannot
              // produce a cold number to compare itself against.
              //
              // This is also the tripwire for a silently-broken cache: if the merge stops finding last
              // tick's impulses, every contact starts cold and `warm4` regresses to `coldTiltAt4`, which is
              // 48x the number asserted here. Nothing else in the suite would notice.
              let warm4 = tiltAfter noSleep 4

              Expect.isLessThan warm4 coldTiltAt10 "4 warm iterations settle the box flatter than 10 cold ones"
              Expect.isLessThan (warm4 * 10.0) coldTiltAt4 "and beat 4 cold iterations by more than an order of magnitude"
          }

          test "warm starting still converges toward a level rest as iterations rise" {
              // Warm starting is a convergence accelerator, not a different answer: the sequence must remain
              // monotone in the iteration count, and must still reach a level rest. If seeding ever pushed
              // the solver somewhere the cold one would not go, this is where it would show.
              let tilts = [ 4; 8; 16 ] |> List.map (tiltAfter noSleep)

              Expect.isTrue (tilts = List.sortDescending tilts) (sprintf "more iterations, flatter rest: %A" tilts)
              Expect.isLessThan (tiltAfter noSleep 64) 1e-12 "and 64 warm iterations reach a level rest"
          }

          test "warm starting and sleeping are exact no-ops on a converged rest" {
              // A circle on a floor is a one-point contact the solver converges exactly, so its rest is a
              // fixed point: seeding it with its own impulse returns it unchanged, and freezing a body that
              // has stopped moving changes nothing. Bit-for-bit, with each lever alone and with both.
              //
              // This is the honest form of the slice's acceptance criterion. A scene that has genuinely
              // settled does not notice #76; `boxOnFloor` moves only because its rest never converged.
              let circle cfg = advance 240 (dropped cfg (Physics.SCircle 0.5) (material 0.0 0.5) 5.0)

              Expect.equal
                  (Physics.checksum (circle noSleep))
                  12544979940497693507UL
                  "warm starting alone leaves the #75 checksum bit-identical"

              Expect.equal
                  (Physics.checksum (circle stepConfig))
                  (Physics.checksum (circle noSleep))
                  "and sleeping on top of it changes nothing either"

              Expect.equal
                  (Physics.checksum (advance 600 (dropped stepConfig (Physics.SCircle 0.5) (material 0.0 0.5) 5.0)))
                  (Physics.checksum (advance 600 (dropped noSleep (Physics.SCircle 0.5) (material 0.0 0.5) 5.0)))
                  "still true at 600 ticks, long after the sleeping one has frozen"
          }

          test "the checksum sees neither the sleep flag nor the warm-start cache (R3)" {
              // Two worlds that differ in solver state BY CONSTRUCTION — one has sleeping enabled and has
              // fallen asleep, dropping its cache; the other has the lever off and holds a live one — and
              // whose presentation is then shown to agree independently of the hash, through `manifold`.
              // Only then does an equal checksum say something: that neither the flag, the counter, nor the
              // cache reaches it. That is the property which lets a replay tripwire survive an optimisation.
              let asleep = advance 240 (dropped stepConfig (Physics.SCircle 0.5) (material 0.0 0.5) 5.0)
              let awake = advance 240 (dropped noSleep (Physics.SCircle 0.5) (material 0.0 0.5) 5.0)

              match restingContact asleep, restingContact awake with
              | ValueSome a, ValueSome b ->
                  Expect.equal b.Depth a.Depth "the two worlds present the same pose"
                  Expect.equal b.Normal a.Normal "...down to the contact normal"
              | _ -> failtest "the circle left the floor"

              Expect.equal (Physics.checksum asleep) (Physics.checksum awake) "so they must hash alike"

              // And the eight fields `World` gained carry no weight in the hash of an empty world.
              Expect.equal (Physics.checksum (Physics.empty stepConfig)) 12161962213042174405UL "empty world, unchanged since #75"
          }

          test "a warm world replays bit-identically" {
              // The cache is cross-tick state, and cross-tick state is where a replay diverges. It is held
              // as sorted parallel arrays and merged linearly precisely so that no hash order can leak in.
              let run () = Physics.checksum (advance 600 (stack stepConfig 4))
              Expect.equal (run ()) (run ()) "two runs of a settling stack agree"
          } ]

// =====================================================================================================
// Speculative contacts — fixed-cost CCD (#77)
// =====================================================================================================
//
// The slice's two claims, each asserted through the only public keyhole (`manifold` — there is still no
// position getter, `interpolate` being #78):
//
//   * NO TUNNELING — a fast small circle fired at a thin static wall is stopped AT the wall, at any `dt`.
//     Observed with a BACKSTOP body well beyond the wall: a circle that tunnels reaches it and reports a
//     contact; a circle the wall stops never does. The wall contact is asserted alongside, so "never
//     reached the backstop" cannot pass for the wrong reason — a circle that merely fell short of both.
//
//   * INERT WHEN NOTHING IS SPECULATIVE — the #75/#76 golden checksums, whose scenes carry no fast mover,
//     are unchanged to the bit; and a fast mover is stopped only by what lies in its PATH, never by a wall
//     off to the side. Speculation that fired on an ordinary gravity scene, or on an obstacle not in the
//     way, would move a golden checksum or stop a body it must not touch.
//
// Velocity is imparted the ONE way this surface allows — by GRAVITY, since `addBody` seeds every body at
// rest and nothing else sets a velocity. A large `Gravity` flings a body from rest, so it is the
// projectile launcher these scenes use: horizontal to fire sideways at a wall, steeply vertical to drop
// hard onto a thin floor. The launched speed rises without bound, so within a few dozen ticks the mover
// crosses many times its own radius per step — squarely the regime a discrete-only broad phase tunnels.

[<Tests>]
let speculativeContactTests =
    testList
        "Game.Core Physics speculative contacts / CCD (#77)"
        [

          test "a fast circle fired at a thin wall is stopped at it, not through it — at any dt" {
              // Bodies: 0 = thin wall at x = 5, 1 = backstop at x = 8, 2 = a small circle flung from the
              // origin by horizontal gravity. By the time it reaches the wall it moves far more than its own
              // radius per step, so a discrete-only engine would have it above the wall one tick and past it
              // the next, touching neither. Three `dt`s, because a larger step is a longer un-swept jump and
              // so a harder case — the claim is "at any dt the fixed step uses".
              for dt in [ 1.0 / 30.0; 1.0 / 60.0; 1.0 / 120.0 ] do
                  let cfg = { stepConfig with Gravity = p 300.0 0.0 }

                  let w0 =
                      let w = Physics.empty cfg
                      let struct (_, w) = Physics.addBody Physics.Static (box 0.05 5.0) (material 0.0 0.0) (p 5.0 0.0) w
                      let struct (_, w) = Physics.addBody Physics.Static (box 0.5 5.0) (material 0.0 0.0) (p 8.0 0.0) w
                      let struct (_, w) = Physics.addBody Physics.Dynamic (Physics.SCircle 0.1) (material 0.0 0.0) (p 0.0 0.0) w
                      w

                  let mutable w = w0
                  let mutable hitWall = false
                  let mutable hitBackstop = false

                  for _ in 1..400 do
                      w <- Physics.step w dt
                      if (Physics.manifold w 0 2).IsSome then hitWall <- true
                      if (Physics.manifold w 1 2).IsSome then hitBackstop <- true

                  Expect.isTrue hitWall (sprintf "dt = %f: the circle is caught at the thin wall" dt)
                  Expect.isFalse hitBackstop (sprintf "dt = %f: and never tunnels through to the backstop beyond it" dt)
          }

          test "a fast BOX mover is not swept — the documented circle-only scope, verified as a limit" {
              // The speculative sweep's mover is a CIRCLE by design (`Physics.fs`: "the mover is a
              // CIRCLE"); a fast polygon mover is an explicit heavier follow-up, not swept today. That is a
              // documented SCOPE, not a bug — so it is pinned as a characterization test rather than left
              // unverified: a fast box fired at the same thin wall the circle above is caught at TUNNELS
              // through it, because no speculative contact is minted for it. This test flips the day linear
              // polygon CCD lands, which is exactly when its author should be reminded to revisit the scope.
              //
              // The contrast is the assertion's teeth: circle and box are fired from rest by the identical
              // launcher at the identical geometry, and only the mover's SHAPE differs. A generous wall
              // (half-width 0.4) makes the tunnelling a property of the missing sweep, not of a knife-edge
              // discrete miss the circle would share.
              let cfg = { stepConfig with Gravity = p 400.0 0.0 }

              let fire moverShape =
                  let w =
                      let w = Physics.empty cfg
                      let struct (_, w) = Physics.addBody Physics.Static (box 0.4 5.0) (material 0.0 0.0) (p 6.0 0.0) w
                      let struct (_, w) = Physics.addBody Physics.Static (box 0.5 5.0) (material 0.0 0.0) (p 12.0 0.0) w
                      let struct (_, w) = Physics.addBody Physics.Dynamic moverShape (material 0.0 0.0) (p 0.0 0.0) w
                      w

                  let mutable acc = w
                  let mutable hitWall = false
                  let mutable hitBackstop = false

                  for _ in 1..400 do
                      acc <- Physics.step acc tick
                      if (Physics.manifold acc 0 2).IsSome then hitWall <- true
                      if (Physics.manifold acc 1 2).IsSome then hitBackstop <- true

                  hitWall, hitBackstop

              // Control: the circle mover IS swept — caught at the wall, never reaching the backstop.
              let circleWall, circleBackstop = fire (Physics.SCircle 0.1)
              Expect.isTrue circleWall "control: the swept circle is caught at the wall"
              Expect.isFalse circleBackstop "control: the swept circle never reaches the backstop"

              // The box mover is not swept, so it tunnels the wall and reaches the backstop beyond.
              let _, boxBackstop = fire (box 0.1 0.1)
              Expect.isTrue boxBackstop "a fast box mover tunnels the wall — the documented circle-only sweep scope"
          }

          test "a circle dropped hard onto a thin floor lands on it instead of falling through" {
              // The canonical tunnel, vertically: a small fast body and a floor thinner than one step's
              // fall. Discrete-only, the body is above the floor one tick and below it the next and never
              // contacts it. A catch-floor well below turns "fell through" into an observable — a contact
              // with body 1 that CCD must never let happen.
              let cfg = { stepConfig with Gravity = p 0.0 -1500.0 }

              let w0 =
                  let w = Physics.empty cfg
                  let struct (_, w) = Physics.addBody Physics.Static (box 5.0 0.01) (material 0.0 0.0) (p 0.0 0.0) w
                  let struct (_, w) = Physics.addBody Physics.Static (box 5.0 0.5) (material 0.0 0.0) (p 0.0 -10.0) w
                  let struct (_, w) = Physics.addBody Physics.Dynamic (Physics.SCircle 0.02) (material 0.0 0.0) (p 0.0 6.0) w
                  w

              let mutable w = w0
              let mutable onFloor = false
              let mutable belowFloor = false

              for _ in 1..400 do
                  w <- Physics.step w tick
                  if (Physics.manifold w 0 2).IsSome then onFloor <- true
                  if (Physics.manifold w 1 2).IsSome then belowFloor <- true

              Expect.isTrue onFloor "the circle is caught on the thin floor"
              Expect.isFalse belowFloor "and never reaches the catch-floor below it"
          }

          test "a fast mover is stopped by what lies in its path, and ignores a wall off to the side" {
              // The inert half, in motion: the speculative broad phase must not stop a mover with an
              // obstacle it never sweeps over. The off-axis wall at y = 10 is nowhere near the path along
              // y = 0, so it must never report a contact, while the backstop that IS in the path catches
              // the mover. A speculative pass that queried too wide would stop the mover early, on the wall
              // it passes clear of.
              let cfg = { stepConfig with Gravity = p 300.0 0.0 }

              let w0 =
                  let w = Physics.empty cfg
                  let struct (_, w) = Physics.addBody Physics.Static (box 0.5 0.5) (material 0.0 0.0) (p 4.0 10.0) w
                  let struct (_, w) = Physics.addBody Physics.Static (box 0.05 5.0) (material 0.0 0.0) (p 8.0 0.0) w
                  let struct (_, w) = Physics.addBody Physics.Dynamic (Physics.SCircle 0.1) (material 0.0 0.0) (p 0.0 0.0) w
                  w

              let mutable w = w0
              let mutable hitOffPath = false
              let mutable hitBackstop = false

              for _ in 1..400 do
                  w <- Physics.step w tick
                  if (Physics.manifold w 0 2).IsSome then hitOffPath <- true
                  if (Physics.manifold w 1 2).IsSome then hitBackstop <- true

              Expect.isFalse hitOffPath "the off-path wall never stops the mover"
              Expect.isTrue hitBackstop "and the mover is caught by the backstop that is in its path"
          }

          test "the golden checksums are unchanged: speculation is inert when nothing is fast" {
              // The scenes of #75/#76 carry no fast mover — a box or circle dropped from y = 5 reaches the
              // floor at well under a radius per step — so the speculative pass must produce nothing and
              // leave every bit of their state where #75/#76 recorded it. If the fast-mover threshold ever
              // fired on ordinary falling gravity, all three of these move; that they do not is the
              // bit-for-bit form of "inert when nothing is speculative".
              let boxOnFloor = advance 240 (dropped stepConfig (box 0.5 0.5) (material 0.0 0.5) 5.0)
              let circleOnFloor = advance 240 (dropped stepConfig (Physics.SCircle 0.5) (material 0.0 0.5) 5.0)

              Expect.equal (Physics.checksum (Physics.empty stepConfig)) 12161962213042174405UL "empty world, unchanged since #75"
              Expect.equal (Physics.checksum boxOnFloor) 9427473436406466390UL "the box checksum is untouched by CCD"
              Expect.equal (Physics.checksum circleOnFloor) 12544979940497693507UL "the circle checksum is untouched by CCD"
          }

          test "a scene with speculative contacts replays bit-identically" {
              // CCD adds a broad phase, a narrow phase and cache entries, all cross-tick-adjacent state and
              // so all places a replay can diverge. The speculative pass is built to be as deterministic as
              // the discrete one: a sorted union, a fixed sentinel feature id, no hash-order dependence.
              let run () =
                  let cfg = { stepConfig with Gravity = p 300.0 0.0 }

                  let w0 =
                      let w = Physics.empty cfg
                      let struct (_, w) = Physics.addBody Physics.Static (box 0.05 5.0) (material 0.0 0.0) (p 5.0 0.0) w
                      let struct (_, w) = Physics.addBody Physics.Dynamic (Physics.SCircle 0.1) (material 0.0 0.0) (p 0.0 0.0) w
                      w

                  Physics.checksum (advance 300 w0)

              Expect.equal (run ()) (run ()) "two runs of the same fast shot agree to the bit"
          } ]

// -------------------------------------------------------------------------------------------------
// Presentation interpolation (#78)
//
// Two rules and their totality. POSITION is testable end-to-end through the public API — `addBody`
// places a body exactly, so `prev` and `curr` can be built at chosen points and the blend read back
// off `interpolate`. ROTATION cannot: `Rot` is only ever moved by `step`, and `World` is opaque, so no
// public call sets a body to +3.13 rad. The shortest-arc rule is therefore asserted against the
// `internal` `lerpAngleShortest` it is factored into (reached via InternalsVisibleTo) — the +3.13 to
// -3.13 crossing, the exact endpoints, and that a sub-pi step still matches a naive lerp.
// -------------------------------------------------------------------------------------------------

/// A world of dynamic unit circles at the given origins, in order — so body `i`'s presentation position
/// is exactly `positions.[i]` and its rotation is `0.0` (nothing has stepped).
let private posWorld (positions: Point list) : Physics.World =
    positions
    |> List.map (fun pt -> Physics.Dynamic, Physics.SCircle 0.5, pt)
    |> worldOf 4.0

[<Tests>]
let interpolateTests =
    testList
        "Game.Core Physics presentation interpolation (#78)"
        [

          // -----------------------------------------------------------------------------------------
          // Exact endpoints, over positions built through the public API
          // -----------------------------------------------------------------------------------------

          test "interpolate 0.0 is previous's transforms; interpolate 1.0 is current's" {
              let prev = posWorld [ p 0.0 0.0; p 10.0 -4.0 ]
              let curr = posWorld [ p 2.0 6.0; p 12.0 -1.0 ]

              let at0 = Physics.interpolate 0.0 prev curr
              let at1 = Physics.interpolate 1.0 prev curr

              Expect.equal (at0 |> Array.map (fun t -> t.Position)) [| p 0.0 0.0; p 10.0 -4.0 |] "alpha 0 is previous"
              Expect.equal (at1 |> Array.map (fun t -> t.Position)) [| p 2.0 6.0; p 12.0 -1.0 |] "alpha 1 is current"
          }

          test "the blend is linear at the midpoint, one transform per current body in index order" {
              let prev = posWorld [ p 0.0 0.0; p -2.0 8.0 ]
              let curr = posWorld [ p 4.0 2.0; p 2.0 -8.0 ]

              let mid = Physics.interpolate 0.5 prev curr

              Expect.equal mid.Length 2 "one transform per body"
              Expect.floatClose Accuracy.high mid.[0].Position.X 2.0 "body 0 x halfway"
              Expect.floatClose Accuracy.high mid.[0].Position.Y 1.0 "body 0 y halfway"
              Expect.floatClose Accuracy.high mid.[1].Position.X 0.0 "body 1 x halfway"
              Expect.floatClose Accuracy.high mid.[1].Position.Y 0.0 "body 1 y halfway"
          }

          // -----------------------------------------------------------------------------------------
          // Totality: the clamp, and a body that only current has
          // -----------------------------------------------------------------------------------------

          test "alpha is clamped to [0,1] — out of range and non-finite never extrapolate or throw" {
              let prev = posWorld [ p 0.0 0.0 ]
              let curr = posWorld [ p 10.0 0.0 ]

              let x alpha = (Physics.interpolate alpha prev curr).[0].Position.X

              Expect.equal (x -1.0) 0.0 "alpha below 0 clamps to previous"
              Expect.equal (x 2.0) 10.0 "alpha above 1 clamps to current"
              Expect.equal (x infinity) 10.0 "+infinity clamps to current"
              Expect.equal (x -infinity) 0.0 "-infinity clamps to previous"
              Expect.equal (x nan) 0.0 "NaN, which loses every comparison, resolves to previous not garbage"
          }

          test "a body present in current but not previous is shown at its current transform" {
              // The double buffer can gain a body between the two frames (a spawn). It has no prior pose to
              // blend from, so it must appear where it is now — at every alpha, not just alpha 1.
              let prev = posWorld [ p 0.0 0.0 ]
              let curr = posWorld [ p 4.0 0.0; p 9.0 -3.0 ]

              let mid = Physics.interpolate 0.5 prev curr

              Expect.equal mid.Length 2 "the result covers current's bodies, not previous's"
              Expect.floatClose Accuracy.high mid.[0].Position.X 2.0 "the shared body still blends"
              Expect.equal mid.[1].Position (p 9.0 -3.0) "the new body is at its current position, unblended"
          }

          test "interpolate reads only presentation state — an empty world yields no transforms, never throws" {
              let empty = Physics.empty stepConfig
              Expect.equal (Physics.interpolate 0.5 empty empty) [||] "no bodies, no transforms"
          }

          // -----------------------------------------------------------------------------------------
          // Shortest arc — asserted on the internal blend, since Rot cannot be set through the public API
          // -----------------------------------------------------------------------------------------

          test "rotation takes the short way: +3.13 to -3.13 crosses +pi, not 0" {
              // The delta is -6.26 rad the long way, but +0.023 rad across +pi. A naive lerp spins a turret
              // most of the way round the circle at 60 fps; the shortest arc nudges it a hair past pi.
              let mid = Physics.lerpAngleShortest 3.13 -3.13 0.5

              Expect.floatClose Accuracy.medium mid System.Math.PI "the midpoint sits on +pi"
              Expect.isTrue (mid > 3.13) "it moved UP toward +pi, it did not unwind down through 0"
          }

          test "the short way is symmetric: -3.13 to +3.13 crosses -pi" {
              let mid = Physics.lerpAngleShortest -3.13 3.13 0.5
              Expect.floatClose Accuracy.medium mid -System.Math.PI "the midpoint sits on -pi"
              Expect.isTrue (mid < -3.13) "it moved DOWN toward -pi"
          }

          test "endpoints are exact, even where the short arc wraps a full turn off the naive value" {
              // t=0 is a0 and t=1 is a1 bit-for-bit. The wrap case is the one that needs the special-case:
              // a0 + shortestDelta at t=1 would be +3.153, a full 2pi shy of current's -3.13.
              Expect.equal (Physics.lerpAngleShortest 3.13 -3.13 0.0) 3.13 "t=0 is exactly a0"
              Expect.equal (Physics.lerpAngleShortest 3.13 -3.13 1.0) -3.13 "t=1 is exactly a1, not a turn off it"
          }

          test "a sub-pi step matches a naive lerp — the short way IS the direct way when no wrap is needed" {
              Expect.floatClose Accuracy.high (Physics.lerpAngleShortest 0.0 1.0 0.5) 0.5 "half of a 1 rad turn"
              Expect.floatClose Accuracy.high (Physics.lerpAngleShortest 1.0 2.0 0.25) 1.25 "a quarter of the way"
              Expect.floatClose Accuracy.high (Physics.lerpAngleShortest -0.4 0.4 0.5) 0.0 "straddling 0 goes through 0"
          } ]

// ---------------------------------------------------------------------------------------------------
// The world-build path and the carried broad phase (#94). Two changes with one common evidence bar:
// they must not move a single bit of what the simulation produces.
//
//   * `addBodies` is the O(N) batch build path. The load-bearing claim is EQUIVALENCE: a world built by
//     one `addBodies` and a world built by folding `addBody` over the same bodies are the same world —
//     same indices, same `pairs`, same `checksum`, and the same `checksum` after a shared replay. Anything
//     less would make the fast path a different engine, which is the one thing a perf change must not be.
//
//   * The broad-phase grid now lives ON the `World`, rebuilt by `step` from the poses it integrates. The
//     risk that buys is a STALE grid — one keyed on last tick's poses — so the direct test is that a
//     stepped world's `pairs` reflects where its bodies ARE, not where they started.
let private buildQuads (bodies: (Physics.BodyKind * Physics.Shape * Point) list) =
    bodies |> List.map (fun (k, s, pos) -> k, s, noMaterial, pos)

[<Tests>]
let worldBuildTests =
    testList
        "Game.Core Physics world build and carried broad phase (#94)"
        [ test "addBodies builds the same world as folding addBody — indices, pairs, checksum, and a replay all agree" {
              let prop (raw: (float * float * float * int * int) list) =
                  // The oracle here is `addBody` itself, folded; cap the batch where a fold stays cheap.
                  let quads = raw |> List.truncate 20 |> List.map bodyOf |> buildQuads

                  let folded =
                      quads
                      |> List.fold
                          (fun w (k, s, m, pos) ->
                              let struct (_, w') = Physics.addBody k s m pos w
                              w')
                          (Physics.empty stepConfig)

                  let struct (idxBatch, batched) = Physics.addBodies quads (Physics.empty stepConfig)

                  // Indices are dense and ascending from 0 on a fresh world, exactly as N `addBody` calls assign.
                  let sameIndices = Array.toList idxBatch = [ 0 .. List.length quads - 1 ]
                  let samePairs = Physics.pairs folded = Physics.pairs batched
                  let sameChecksum = Physics.checksum folded = Physics.checksum batched
                  // ...and the two remain identical under the one thing that reads all the derived state: `step`.
                  let sameAfterReplay = Physics.checksum (advance 30 folded) = Physics.checksum (advance 30 batched)

                  sameIndices && samePairs && sameChecksum && sameAfterReplay

              Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)
          }

          test "addBodies assigns dense ascending indices from the world's current body count" {
              // Two bodies already in the world (indices 0 and 1); the batch must continue from 2.
              let w0 =
                  worldOf
                      8.0
                      [ Physics.Static, box 5.0 1.0, p 0.0 0.0
                        Physics.Dynamic, Physics.SCircle 1.0, p 0.0 3.0 ]

              let struct (idx, _) =
                  Physics.addBodies
                      [ Physics.Dynamic, Physics.SCircle 0.5, noMaterial, p 2.0 3.0
                        Physics.Dynamic, Physics.SCircle 0.5, noMaterial, p 4.0 3.0 ]
                      w0

              Expect.equal (Array.toList idx) [ 2; 3 ] "the batch continues the world's index sequence, it does not restart it"
          }

          test "an empty batch is the identity — no indices, and a bit-for-bit unchanged world" {
              let w =
                  worldOf
                      8.0
                      [ Physics.Dynamic, Physics.SCircle 1.0, p 0.0 0.0
                        Physics.Static, box 20.0 1.0, p 0.0 -1.5 ]

              let struct (idx, w') = Physics.addBodies [] w

              Expect.isEmpty idx "nothing added, no indices"
              Expect.equal (Physics.checksum w') (Physics.checksum w) "the world's body state is unchanged"
              Expect.equal (Physics.pairs w') (Physics.pairs w) "and the broad phase it carries is unchanged"
          }

          test "a degenerate body in a batch still takes its index, so later bodies do not shift" {
              // Body 1 has radius 0 — a no-collision input — but it is still indexed, exactly as the single
              // `addBody` path indexes it, so body 2 keeps index 2 rather than sliding to 1.
              let struct (idx, w) =
                  Physics.addBodies
                      [ Physics.Dynamic, Physics.SCircle 1.0, noMaterial, p 0.0 0.0
                        Physics.Dynamic, Physics.SCircle 0.0, noMaterial, p 0.0 0.0
                        Physics.Dynamic, Physics.SCircle 1.0, noMaterial, p 0.5 0.0 ]
                      (Physics.empty (config 8.0))

              Expect.equal (Array.toList idx) [ 0; 1; 2 ] "every body is indexed, degenerate or not"
              Expect.equal (pairList w) [ (0, 2) ] "the zero-radius body pairs with nothing; 0 and 2 still overlap and pair"
          }

          test "the broad phase a stepped world carries reflects the poses step integrated, not its opening ones" {
              // A circle starts far ABOVE the floor, their AABBs disjoint, so the OPENING broad phase reports
              // no pair. It then falls. If `step` refreshes the grid it hands on, `pairs` reports the contact
              // once the circle nears the floor; if `step` left a grid keyed on the opening poses, the pair
              // would never appear however far the circle fell. Two bodies, so `pairs` is `[]` or `[(0,1)]`.
              let w0 =
                  let w = Physics.empty stepConfig
                  let struct (_, w) = Physics.addBody Physics.Static (box 50.0 1.0) (material 0.0 0.0) (p 0.0 0.0) w
                  let struct (_, w) = Physics.addBody Physics.Dynamic (Physics.SCircle 0.5) (material 0.0 0.0) (p 0.0 20.0) w
                  w

              Expect.isEmpty (pairList w0) "far apart at rest, the opening broad phase sees no pair"

              let mutable w = w0
              let mutable sawPair = false

              for _ in 1..120 do
                  w <- Physics.step w tick
                  if pairList w = [ (0, 1) ] then sawPair <- true

              Expect.isTrue sawPair "as the circle nears the floor, the carried broad phase reports the contact — the grid moved with the bodies"
          } ]

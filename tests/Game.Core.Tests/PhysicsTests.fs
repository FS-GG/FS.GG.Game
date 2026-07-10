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
// by the order in which it visits the two contact points; a box at rest for 5000 ticks rotates about
// 0.46°. That residual is a convergence artifact, not a modelling error, and the test
// `the resting tilt is a convergence residual` below pins it as one by showing it vanish (to 4e-16) as
// the iteration count rises. Warm starting (#76) is what removes it at 8 iterations. Asserting zero here
// would be asserting a falsehood.

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
              let tiltAfter iterations =
                  let cfg = { stepConfig with VelocityIterations = iterations }

                  match restingContact (advance 2000 (dropped cfg (box 0.5 0.5) (material 0.0 0.5) 2.0)) with
                  | ValueSome m when m.PointCount = 2 -> abs (m.Points.[0].Y - m.Points.[1].Y)
                  | _ -> failtest "the box did not come to a two-point rest"

              let coarse = tiltAfter 8
              let fine = tiltAfter 64

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
              // Golden values. These are the numbers slices 6-8 must not move for a scene that never
              // sleeps. If warm starting or CCD changes them, that is a deliberate decision to record —
              // not a test to quietly re-baseline.
              //
              // They are FNV-1a over the IEEE-754 bits of Pos/Vel/Rot/AngVel, so they are reproducible on
              // any runtime that agrees on IEEE-754 double arithmetic — which is what `.NET` guarantees on
              // a fixed compiler and ISA. A cross-platform lockstep guarantee needs fixed-point, and is a
              // later ADR'd decision (design §6).
              let boxOnFloor = advance 240 (dropped stepConfig (box 0.5 0.5) (material 0.0 0.5) 5.0)
              let circleOnFloor = advance 240 (dropped stepConfig (Physics.SCircle 0.5) (material 0.0 0.5) 5.0)

              Expect.equal (Physics.checksum (Physics.empty stepConfig)) 12161962213042174405UL "empty world"
              Expect.equal (Physics.checksum boxOnFloor) 12790444109480856124UL "a box at rest after 240 ticks"
              Expect.equal (Physics.checksum circleOnFloor) 12544979940497693507UL "a circle at rest after 240 ticks"
          } ]

module Game.Core.Tests.PhysicsTests

// Physics: the broad phase, and only the broad phase (#74). Three things here are load-bearing, and each
// is asserted the only way that means anything:
//
//   * exactness    — `pairs` has no false negatives (a property test against an O(n²) oracle) and no
//                    false positives (the same oracle, the other direction). A broad phase is normally
//                    allowed a loose superset; this one is not, and the oracle is what holds it to that.
//   * the dilation — `SpatialGrid` buckets by a body's POSITION, so a large body whose origin sits far
//                    outside a small body's box is invisible to a naive query. The regression test puts
//                    the large body at the HIGHER index, because that is the only ordering in which the
//                    `j > i` scan can miss it.
//   * the order    — `(a, b)` ascending, `a < b`, no duplicates. Unsorted pair order is the #1
//                    determinism leak in a physics step, so it is asserted as a contract, not assumed
//                    from the container.
//
// The strict-edge convention is asserted against `Geometry.intersects` directly, so the two can never
// drift apart silently.

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
/// is the case a `reach` computed from a radius rather than from box corners would get wrong.
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

          test "a large body at a HIGHER index is still found — the SpatialGrid dilation" {
              // Body 0's box is [89,91]². Body 1's box is [-100,100]², so the two overlap — but body 1's
              // ORIGIN is (0,0), nowhere near body 0's box. The scan queries body 0's region and takes
              // j > i, so without dilating by `reach` the large body is never a candidate and the pair is
              // silently lost. Ordering matters: swap the two and the naive query would find it anyway.
              let w =
                  worldOf 8.0 [ Physics.Dynamic, Physics.SCircle 1.0, p 90.0 90.0
                                Physics.Dynamic, box 100.0 100.0, p 0.0 0.0 ]

              Expect.equal (pairList w) [ 0, 1 ] "the dilated query must reach the large body's origin"
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

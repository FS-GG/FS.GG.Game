module Game.Harness.Tests.VisibilityPolygonTests

// 2D visibility polygon (work item 022, roadmap 2.4). Harness-staged, float boundary. Properties:
// origin inside, star-shaped, empty-room area ≈ bounds, a wall casts a shadow, reproducibility,
// totality. Ordering is by integer pseudo-angle (no atan2), so the polygon is reproducible.

open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Harness

// Even-odd point-in-polygon over a simple polygon.
let private pointInPolygon (p: Point) (poly: Point list) =
    let arr = List.toArray poly
    let n = arr.Length
    if n < 3 then
        false
    else
        let mutable inside = false
        let mutable j = n - 1
        for i in 0 .. n - 1 do
            let pi = arr.[i]
            let pj = arr.[j]
            if (pi.Y > p.Y) <> (pj.Y > p.Y)
               && p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y) + pi.X then
                inside <- not inside
            j <- i
        inside

// Shoelace area (absolute).
let private area (poly: Point list) =
    let arr = List.toArray poly
    let n = arr.Length
    let mutable s = 0.0
    for i in 0 .. n - 1 do
        let a = arr.[i]
        let b = arr.[(i + 1) % n]
        s <- s + (a.X * b.Y - b.X * a.Y)
    abs s / 2.0

let private room = { X = 0.0; Y = 0.0; Width = 10.0; Height = 10.0 }

[<Tests>]
let tests =
    testList "Game.Harness VisibilityPolygon (022, FR-001..FR-006)" [

        test "empty room: the origin sees the whole room (FR-002/FR-003)" {
            let origin = { X = 5.0; Y = 5.0 }
            let poly = VisibilityPolygon.polygon origin room []
            Expect.isTrue (pointInPolygon origin poly) "origin is inside the polygon"
            Expect.floatClose Accuracy.medium (area poly) 100.0 "empty-room polygon area ≈ the 10×10 bounds"
        }

        test "a wall casts a shadow (FR-004)" {
            // Origin near the bottom; a horizontal wall above it at y=5, x in [3,7].
            let origin = { X = 5.0; Y = 1.0 }
            let wall = ({ X = 3.0; Y = 5.0 }, { X = 7.0; Y = 5.0 })
            let poly = VisibilityPolygon.polygon origin room [ wall ]
            Expect.isTrue (pointInPolygon origin poly) "origin inside"
            // Directly above the wall from the origin ⇒ occluded ⇒ outside the polygon.
            Expect.isFalse (pointInPolygon { X = 5.0; Y = 8.0 } poly) "a point behind the wall is in shadow"
            // Off to the side, the ray misses the wall ⇒ visible ⇒ inside.
            Expect.isTrue (pointInPolygon { X = 1.0; Y = 8.0 } poly) "a point in open view is visible"
            // The shadow shrinks the visible area below the whole room.
            Expect.isLessThan (area poly) 100.0 "the wall removes visible area"
        }

        test "star-shaped: every vertex is visible from the origin (FR-001)" {
            let origin = { X = 5.0; Y = 5.0 }
            let wall = ({ X = 2.0; Y = 7.0 }, { X = 8.0; Y = 7.0 })
            let poly = VisibilityPolygon.polygon origin room [ wall ]
            // A point just short of each vertex, along the ray from origin, must not be occluded — i.e.
            // the segment origin→(0.99·vertex) crosses no interior wall. We check via a fine LOS sample.
            let wallBlocks (p: Point) =
                // does the open segment origin->p cross the wall segment? sample-free analytic test:
                let a = { X = 2.0; Y = 7.0 }
                let b = { X = 8.0; Y = 7.0 }
                let d1x = p.X - origin.X
                let d1y = p.Y - origin.Y
                let sx = b.X - a.X
                let sy = b.Y - a.Y
                let denom = d1x * sy - d1y * sx
                if abs denom < 1e-12 then
                    false
                else
                    let wx = a.X - origin.X
                    let wy = a.Y - origin.Y
                    let t = (wx * sy - wy * sx) / denom
                    let u = (wx * d1y - wy * d1x) / denom
                    t > 1e-6 && t < 1.0 - 1e-6 && u > 1e-6 && u < 1.0 - 1e-6

            Expect.isTrue
                (poly
                 |> List.forall (fun v ->
                     let near = { X = origin.X + (v.X - origin.X) * 0.99; Y = origin.Y + (v.Y - origin.Y) * 0.99 }
                     not (wallBlocks near)))
                "no vertex is occluded by the wall (star-shaped)"
        }

        test "reproducible: identical inputs yield an identical polygon (FR-005)" {
            let origin = { X = 4.0; Y = 3.0 }
            let walls = [ ({ X = 2.0; Y = 6.0 }, { X = 6.0; Y = 6.0 }); ({ X = 7.0; Y = 2.0 }, { X = 7.0; Y = 8.0 }) ]
            Expect.equal
                (VisibilityPolygon.polygon origin room walls)
                (VisibilityPolygon.polygon origin room walls)
                "byte-identical across runs (pseudo-angle order, no atan2)"
        }

        test "totality: degenerate inputs do not throw (FR-006)" {
            // origin outside bounds
            Expect.isTrue (VisibilityPolygon.polygon { X = -5.0; Y = -5.0 } room [] |> List.isEmpty |> not) "origin outside bounds still returns a polygon"
            // empty segments (only bounds)
            Expect.isTrue (VisibilityPolygon.polygon { X = 5.0; Y = 5.0 } room [] |> List.isEmpty |> not) "empty segments ⇒ the bounds polygon"
            // zero-length segment
            let zero = ({ X = 3.0; Y = 3.0 }, { X = 3.0; Y = 3.0 })
            let poly = VisibilityPolygon.polygon { X = 5.0; Y = 5.0 } room [ zero ]
            Expect.isTrue (pointInPolygon { X = 5.0; Y = 5.0 } poly) "a zero-length segment is harmless"
            // origin exactly on a bounds corner
            Expect.isTrue (VisibilityPolygon.polygon { X = 0.0; Y = 0.0 } room [] |> List.isEmpty |> not) "origin on a corner does not throw"
        }
    ]

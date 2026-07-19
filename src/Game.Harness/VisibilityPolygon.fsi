namespace FS.GG.Game.Harness

open FS.GG.Game.Core

/// Public contract module exposed by the FS.GG.Game.Harness package.
/// The 2D **visibility polygon** (roadmap 2.4): the continuous region visible from a light/eye point
/// over a set of wall segments — for light sources, guard vision cones, and fog-of-war over the
/// continuous segment world, complementing the grid shadowcasting in `Fov`/`Los`.
///
/// **It lives in the harness, not Core, because it crosses the float boundary.** Intersection points
/// are `Point` (float, IEEE-reproducible). Ordering is deterministic — endpoints sort by an integer
/// **pseudo-angle** (quadrant + slope, never `atan2`) with a distance tie-break — so the polygon is
/// reproducible for identical inputs. Per roadmap 2.4 this is deliberately staged here; promotion to
/// Core (with exact-rational arithmetic and a full endpoint sweep) is deferred until the numeric
/// contract is proven.
[<RequireQualifiedAccess>]
module VisibilityPolygon =

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// The visibility polygon from `origin` over `segments`, clipped to `bounds`. The four `bounds`
    /// edges are added to the segment set so every ray hits something and the polygon is closed; the
    /// method casts rays from `origin` at each wall endpoint (and a tiny ±rotation, to slip past
    /// corners), keeps the nearest segment hit per ray, and orders the hits by pseudo-angle.
    ///
    /// The result is a `Point list` traced in pseudo-angle order — **star-shaped** about `origin`
    /// (every vertex is the nearest hit along its ray, so nothing occludes it), and it **contains**
    /// `origin`. A wall nearer than the bounds shortens the rays behind it, carving a shadow. Total:
    /// a degenerate `origin` (on/outside `bounds`), an empty `segments` list, or a zero-length segment
    /// returns a polygon (possibly just the bounds) without throwing.
    val polygon: origin: Point -> bounds: Rect -> segments: (Point * Point) list -> Point list

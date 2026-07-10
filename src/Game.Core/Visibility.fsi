namespace FS.GG.Game.Core

/// Public contract module exposed by the FS.GG.Game.Core package.
/// Continuous-space 2-D visibility: exact point-to-point line of sight against arbitrary occluder
/// segments, and the full angular-sweep visibility polygon from a viewpoint.
///
/// This is the **continuous-space** sibling of the grid modules. `Pathfinding` walks `Cell`s; this
/// module answers "can this position see that position, given these walls" without ever snapping to a
/// grid — so it is the one that can answer occlusion between two *moving* bodies, which a tile walk
/// cannot. Promoted from the frozen `visibility` starter fragment in FS.GG.Rendering
/// (`template/fragments/visibility`), which every game that wanted sight had to copy and diverge from.
///
/// Everything here is pure, total (non-finite and degenerate input degrades to a documented value,
/// never an exception), and byte-deterministic: the sweep orders hits with a cross-product angular
/// comparator and an integer-index tiebreak — no `atan2`, no transcendental — and nearest-hit uses a
/// sqrt-free parametric distance. Identical input yields identical output across runs and platforms,
/// so it is safe to call from a replayed simulation step.
///
/// Algorithm reference: https://www.redblobgames.com/articles/visibility/
[<RequireQualifiedAccess>]
module Visibility =

    /// Public contract type exposed by the FS.GG.Game.Core package.
    /// An occluder: the line segment between two `Point`s. Deliberately NOT a look-alike vector type —
    /// it reuses the shared `Point` vocabulary. A zero-length segment (`A = B`) occludes nothing.
    ///
    /// It is scoped to `Visibility` rather than promoted alongside `Point`/`Rect`/`Circle`/
    /// `ConvexPolygon` in `Primitives` because `Visibility` is currently its only consumer. Promote it
    /// when a second module needs a segment/edge vocabulary — `Primitives` is the shared contract
    /// surface, and a type moved out of it later is a breaking change, whereas one moved *into* it is
    /// additive.
    type Segment = { A: Point; B: Point }

    /// Public contract type exposed by the FS.GG.Game.Core package.
    /// The sweep policy knob. `Radius` is the half-extent of the square sight bound centred on the
    /// source: it doubles as the ray **bound** (a ray that hits no occluder terminates on the
    /// `source ± Radius` box) and as the occluder **cull region**, so the two can never disagree.
    /// A non-positive or non-finite `Radius` falls back to `1.0` rather than throwing.
    type Settings = { Radius: float }

    /// Public contract type exposed by the FS.GG.Game.Core package.
    /// The visible region from a viewpoint: an ordered, closed, counter-clockwise ring of `Vertices`
    /// bounded by `Settings.Radius`, and the `Source` it was computed from. Structural equality makes
    /// it golden-testable. A non-finite `Source` yields an empty ring.
    type VisibilityPolygon = { Source: Point; Vertices: Point list }

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// Nearest ray-segment hit: the point struck and the parametric distance `t >= 0` along the ray
    /// `origin + t * dir`. `None` when the ray is parallel to the segment (which includes a zero-length
    /// segment), points away from it, misses it, or any operand is non-finite. Sqrt-free (parametric):
    /// `t` is in units of `dir`, not world distance, so `dir` of length 1 makes it a true distance.
    /// Total — never throws, never returns a NaN coordinate.
    ///
    /// **Overflow-free.** Finite operands whose magnitudes multiply past `Double.MaxValue` saturate the
    /// parametric cross products, even when the intersection they describe is itself an ordinary pair of
    /// doubles. Such a hit is recovered, not dropped: the solve falls back to re-deriving `t` with the
    /// operands rescaled by exact powers of two, which cancel out of the ratio, and anchored at the
    /// segment endpoint nearer `origin`. Whenever the parametrisation **saturates** and the true
    /// intersection is representable, that intersection is returned. Rescaling introduces no rounding of
    /// its own, and the ordinary path is bit-for-bit unchanged, so this is safe to call from a replayed
    /// simulation step. A `t` that is *genuinely* unrepresentable (an `origin` a `Double.MaxValue` away
    /// from the hit) still degrades to `None` rather than to a non-finite value — a missing hit degrades
    /// one ring, where a NaN one poisons every consumer downstream of it.
    ///
    /// This is a guarantee about **overflow**, not about precision. `t` is a ratio of cross products, and
    /// a cross product of near-parallel operands cancels: where `origin` is nearly collinear with the
    /// segment, or the coordinates span many orders of magnitude, the ordinary path can return a `t` whose
    /// relative error far exceeds an ulp. That error is inherent to the parametrisation and is not what
    /// the rescaled fallback addresses.
    val raySegment: origin: Point -> dir: Point -> seg: Segment -> (Point * float) option

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// Exact point-to-point line of sight: is `target` visible from `source` with no segment strictly
    /// between them? This is the "can this body shoot that body" predicate — it takes the occluders as
    /// an argument, so dynamic bodies can be passed as segments alongside static walls.
    ///
    /// Exact: every segment is tested, with no broad-phase cull, so there is no false "visible". A
    /// blocker exactly *at* either endpoint does not block (the strict `0 < t < 1` convention, matched
    /// to `Geometry`'s strict-edge rule). A `source` equal to `target` is always visible. A non-finite
    /// `source` or `target` is never visible. Total — never throws.
    val isVisible: source: Point -> target: Point -> segments: Segment list -> bool

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// The full visibility polygon by angular sweep: clip the occluders to the sight bound, cast a ray at
    /// every clipped endpoint (and one either side of it, to slip past corners), keep the nearest hit per
    /// ray, and order the hits into a closed ring.
    ///
    /// Occluders are **clipped**, not merely kept. Keeping every segment that *intersects* the bound is
    /// what makes a wall spanning clean across it — both ends outside — still occlude; that is the common
    /// case, not the corner case, since walls are routinely longer than a view radius, and a cull that
    /// dropped such a wall would let the viewpoint see straight through it. But the sweep aims its rays
    /// at occluder endpoints, so a wall kept at full length contributes no aim point where it crosses the
    /// bound, and the ring cuts the corner between wall and bound edge. Trimming each occluder to the
    /// bound supplies exactly those two aim points. Rays terminate on the bound regardless, so clipping
    /// cannot change which rays an occluder blocks. An occluder grazing a bound corner clips to zero
    /// length and is dropped — it occludes nothing.
    ///
    /// Pure, deterministic, and total: non-finite or zero-length occluders are discarded (they can never
    /// occlude), a non-positive or non-finite `Settings.Radius` falls back to `1.0`, and a non-finite
    /// `source` yields an empty ring. No returned vertex is ever NaN.
    val polygon: settings: Settings -> source: Point -> segments: Segment list -> VisibilityPolygon

namespace FS.GG.Game.Core

/// Public contract module exposed by the FS.GG.Game.Core package.
/// Axis-aligned bounding-box helpers over the sim `Rect`/`Point` — the collision, containment,
/// and centering surface the game simulation uses instead of hand-rolling its own geometry.
/// Extracted from `FS.GG.UI.Scene.Geometry` (ADR-0022 / P0 Option D) and reimplemented BCL-only
/// over `FS.GG.Game.Core.Rect`/`Point`. All functions are pure and total (NaN-safe; degenerate
/// rects never throw). Convention: `intersects` uses strict edges (touching is NOT overlap);
/// `contains`/`containsPoint` are inclusive of shared edges.
[<RequireQualifiedAccess>]
module Geometry =

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// True when two rectangles overlap on a positive area. Edge- or corner-touching rectangles
    /// (zero-area overlap) are NOT considered intersecting (strict `<`/`>` convention).
    val intersects: a: Rect -> b: Rect -> bool

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// True when `inner` lies entirely within `outer`, inclusive of shared edges.
    val contains: outer: Rect -> inner: Rect -> bool

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// True when `point` lies within `rect`, inclusive of the low and high edges.
    val containsPoint: rect: Rect -> point: Point -> bool

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// The geometric center of a rectangle.
    val center: rect: Rect -> Point

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// Build a rectangle centered on `center` with the given width and height.
    /// Round-trips with `center`: `center (ofCenter c w h) = c`.
    val ofCenter: center: Point -> width: float -> height: float -> Rect

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// Narrow-phase AABB contact manifold. Returns `Some contact` exactly when `intersects a b`
    /// (positive-area overlap, strict edges) and `None` on touch or gap — so `(aabbContact a b)
    /// |> Option.isSome` agrees with `intersects a b` for all inputs. The `Contact.Normal` points
    /// from `a` toward `b` along the minimum-penetration axis and `Contact.Depth` is that
    /// penetration (the minimum translation vector). Pure and total (NaN-safe). Deterministic
    /// tie-breaks, fixed for byte-identical output: equal penetration on both axes resolves to the
    /// X axis; a zero centre-delta on the chosen axis resolves to the +axis direction.
    val aabbContact: a: Rect -> b: Rect -> Contact option

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// Narrow-phase circle–circle contact manifold. Returns `Some contact` exactly when the two
    /// circles overlap on positive area (centre distance strictly less than the radius sum) and
    /// `None` on touch or gap. `Normal` is the unit vector from `a`'s centre toward `b`'s, and
    /// `Depth = (a.Radius + b.Radius) − centreDistance`. The overlap test uses squared distance; the
    /// sole `sqrt` (a correctly-rounded, cross-platform-deterministic IEEE op) builds the manifold on
    /// a hit. Pure and total: a NaN or non-positive radius yields `None`. Coincident centres are the
    /// documented degenerate — `Normal = (1, 0)`, `Depth = a.Radius + b.Radius`.
    val circleContact: a: Circle -> b: Circle -> Contact option

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// Narrow-phase circle–AABB contact manifold. Returns `Some contact` exactly when the circle
    /// overlaps the box on positive area, computed by clamping the circle centre to the box and
    /// comparing squared distance to squared radius; `None` otherwise. As with `aabbContact`,
    /// `Normal` points from the circle toward the box and the separating translation of the circle is
    /// `−Normal × Depth`. When the centre lies inside the box, the box encloses the circle, so
    /// `−Normal` is the outward normal of the least-penetration face (equal-penetration tie-break:
    /// X axis, +bias — identical to `aabbContact`) and `Depth` is that penetration plus the radius.
    /// Pure and total (a NaN or non-positive radius yields `None`).
    val circleAabbContact: c: Circle -> box: Rect -> Contact option

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// True when the swept path of `moving` displaced by `velocity` overlaps `target` anywhere along
    /// the sweep — detects a fast projectile that would tunnel through a thin `target` within one
    /// step. A superset of `intersects` at both sweep endpoints.
    val sweptIntersects: moving: Rect -> velocity: Point -> target: Rect -> bool

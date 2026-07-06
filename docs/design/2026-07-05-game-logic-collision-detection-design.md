# Game logic skill — Collision detection & resolution

> **Provenance (relocated 2026-07-06, ADR-0022 open-decision #3).** This game-logic library design was authored **2026-07-05 in FS-GG/.github** — *before* the FS.GG.Game extraction — and relocated here to live with the code it designs. Where the original homed these primitives in `FS.GG.UI.Canvas` / `FS.GG.UI.Scene` / `FS.GG.Rendering`, that home is now **`FS.GG.Game.Core`** — the BCL-only bottom layer; ADR-0022 P0–P5 moved `Rng`/`FixedStep`/`Pathfinding`/`SpatialGrid` out of `FS.GG.UI.Canvas` and the `Geometry` collision module out of `FS.GG.UI.Scene`. Direct surface references below were updated to `FS.GG.Game.Core.*`; the surrounding design narrative is preserved as the dated record.

- **Date:** 2026-07-05
- **Owner:** `.github` (cross-repo coordination); **implementation home:** `FS.GG.Game` (FS.GG.Game.Core)
- **Status:** Design proposal (pre-ADR). Deepens an existing thin surface into a comprehensive skill.
- **Scope:** The **2D collision detection & resolution** game-logic skill — what
  `FS.GG.Game.Core.Geometry` (today: an AABB type) and the `collision` fragment should grow
  into, in **docs** and **source code**. Targets 2D games (platformers, top-down, tile-based,
  tactics grids) — a **game-logic** collision library, **not** a rigid-body physics engine
  (impulse dynamics is an optional heavier layer).
- **Language/target:** F# on `net10.0`, functional-first, **byte-deterministic** (fixed
  timestep, integer/fixed-point friendly), immutable, Elmish/MVU-friendly.
- **Sibling designs (this batch):** [grids](2026-07-05-game-logic-grids-spatial-partitioning-design.md) ·
  [line of sight](2026-07-05-game-logic-line-of-sight-design.md) ·
  [field of view / visibility](2026-07-05-game-logic-field-of-view-visibility-design.md) ·
  [pathfinding](2026-07-05-game-logic-pathfinding-navigation-design.md).
- **Grounding:** `tetris` (grid cells = collision truth; SRS kick collision test),
  `turn-based-tactics` (discrete knockback/push, wall/unit stop, collision damage),
  platformer-style specs (AABB tile movement).

---

## 1. Design shape: layered, detection separate from response

The skill grows from a single AABB into a **layered surface**, with a hard rule that
**detection is pure and separate from response**:

```
shapes → narrow-phase tests → swept/CCD → broad phase → resolution
         (pure, return manifests as values)            (pure transforms, separate layer)
```

Arcade / kinematic resolution is the **first-class default**; impulse-based physics is an
opt-in advanced module. Keeping detection (`Shape → Shape → Contact option`) apart from
response (`Body → Contact → Body`) makes both independently testable and lets MVU treat a
physics step as a deterministic `world → world` reducer.

---

## 2. Narrow-phase primitive tests

| Test | Cost | Manifold? | Key gotcha |
|---|---|---|---|
| AABB–AABB | O(1) | yes (least-penetration axis) | also the broad-phase workhorse |
| Circle–Circle | O(1) | yes | compare **squared** distances; coincident-center degenerate |
| Circle–AABB | O(1) | yes | clamp center to box; **center-inside** needs a face-normal fallback |
| Point-in-shape | O(1)/O(n) | — | polygon = cross-sign / ray-crossing |
| Ray/segment–AABB | O(1) | hit `t` + face normal | **slab method**; robust inverse-div; parallel-axis branch |
| Ray/segment–Circle | O(1) | hit `t` + normal | quadratic discriminant |
| OBB / convex (**SAT**) | O(n+m) axes | yes (**MTV**) | dedup parallel axes; full-containment fix |
| Capsule | O(1) | yes | closest-point-between-segments + radius |

**AABB–AABB (center/half-extent form gives the MTV directly):** overlap depths
`px = (ha.x+hb.x) − |dx|`, `py = (ha.y+hb.y) − |dy|`; if either ≤ 0 no hit; else the **smaller**
of `px,py` is the minimum translation vector axis and depth. This is SAT specialized to two
axes and yields the contact manifold (normal + penetration) for free.

**Circle–AABB** clamps the circle center to the box and compares distance to radius; the
**center-inside-box** case (`diff = 0`, no direction) is the classic bug — fall back to the
least-penetration box face for the normal.

**Ray/segment–AABB (slab method):** per-axis entry/exit `t` for the two parallel planes,
intersect the intervals (`tmin = max`, `tmax = min`); miss if `tmin > tmax`; the axis that
produced `tmin` gives the face normal. Handle the ray-parallel-to-slab case explicitly (or the
robust `1/d = ±∞` trick).

**SAT + MTV (convex/OBB):** project both shapes onto each edge-normal axis; any gap ⇒ early-out
separated; else track the minimum overlap axis as the MTV; orient it from A→B; **add the
full-containment correction** so a contained shape doesn't pick a too-small overlap; **circle
vs polygon** adds one axis = center→nearest-vertex. **Capsules** reduce to
closest-point-between-features + radius (the reusable `closestPtSegmentSegment` /
`closestPtPointSegment` primitives also power swept spheres and rounded shapes).

---

## 3. Continuous / swept collision (anti-tunneling)

Fast objects skip thin geometry between frames — CCD prevents it.

| Technique | Cost | Rotation? | Best for |
|---|---|---|---|
| **Swept AABB** ✅ arcade workhorse | O(1)/pair | no | platformers, top-down, most arcade cases |
| **Ray/slab TOI** (Minkowski-expanded box + ray) | O(1) | no | bullets; unifies moving-box & ray paths |
| Conservative advancement + GJK | iterative | yes | rotating convex bodies (physics layer) |
| Speculative contacts | O(1)/pair | yes | impulse solvers; cheap CCD; deterministic |
| Amanatides–Woo DDA | O(cells) | n/a | tile raycast / LOS / tile enumeration (shared with LOS sibling) |

**Swept AABB** (the default): compute per-axis entry/exit distances → entry/exit *times*;
`entryTime = max(xEntry,yEntry)`, `exitTime = min(xExit,yExit)`; no hit if
`entryTime > exitTime` or entries out of `[0,1]`; the axis that entered **last** gives the
normal. Response = advance to `pos += v·entryTime`, then **slide** the residual velocity along
the surface (kill the normal component) and **re-sweep** for the remaining time — iterate 2–4
passes so corner slides don't get lost. Swept AABB–AABB is equivalently **ray vs a
Minkowski-expanded AABB** (inflate the static box by the mover's half-extents, shoot a ray
along `v`; slab `t` = time of impact) — a nice unification.

---

## 4. Tile-based collision

- **"Resolve X then Y"** — the dominant simplest-correct pattern: move X, snap out of any solid
  tile (kill `vx`); move Y, snap out (kill `vy`). Resolving both axes together makes entities
  **stick to walls** instead of sliding. Enumerate only the tiles the AABB overlaps
  (`floor(min/tileSize) .. floor(max/tileSize)`, clamped).
- **One-way platforms:** no X collision; block on Y only when the entity was above the surface
  last frame and is moving down; a down-press disables the check to drop through.
- **Slopes:** treat slope tiles as non-solid during X/Y detection, resolve in a post-pass via a
  **slope node** (a point on the hitbox) snapped to the surface `y = tileTop + (nodeX −
  tileLeft)·slope`; keep-grounded pull-down when walking down fast; ceiling slopes deflect
  without stopping fall.
- **Fast movement:** sub-step the move or swept-AABB vs tiles (sub-stepping is the robust
  deterministic default).
- **Grid collision for tactics** (tetris, tactics): no continuous math — occupancy is a
  set/lookup, movement validated per cell, blocking enforced by path/reachability;
  **knockback/push** is a discrete cell displacement stopped by a wall/occupant (matches the
  tactics spec's collision-damage-on-block rules).
- **Order matters (corner-catching):** when a mover overlaps several tiles, resolve in
  **most-overlap-area-first** order — arbitrary order yields wrong normals (a body sliding
  along a floor snags on the seam between two flush tiles / is misreported as a head-on hit).

---

## 5. Broad phase

Cut O(n²) all-pairs to likely pairs before narrow phase.

| Structure | Update | Query | Best when |
|---|---|---|---|
| **Uniform grid / spatial hash** ✅ default | O(1) move / O(n) rebuild | ~O(1) neighbours | many similar-size objects, bounded density |
| Sweep-and-prune | ~O(n) w/ coherence | active-list pairs | frame-to-frame coherent, mostly-static |
| Quadtree | O(n log n) | O(log n) | clustered / varied density |
| BSP / k-d | O(n log n) build | O(log n) | static level geometry |

**Default: uniform spatial hash** (shares the grids sibling's index). Insert each shape into
every overlapping cell; candidate pairs = pairs sharing a cell; **dedup** with a canonical
ordered key `(min(idA,idB), max(idA,idB))` (or emit only from the lowest shared cell) so a
big object spanning cells isn't double-reported. The candidate-pair list must be produced in a
**stable sorted order** regardless of hash iteration (determinism, §7). Grid results are more
cache-stable under small movements than tree results — a good fit for an immutable
rebuild-each-tick design.

---

## 6. Collision response

Two families, kept as separate layers:

### 6.1 Arcade / kinematic (recommended default)
Detect → push out along the MTV / least-penetration axis → zero the velocity component along
the contact normal. **Slide** = keep tangential, kill normal (`v −= (v·n)·n`). **Push-out** =
`normal·penetration` (minus a small **slop** so exactly-touching doesn't jitter).
**Knockback** = displacement impulse along the normal (grid games: N cells until a blocker).
**Stacking** = iterate contacts, deepest first, so pushes don't undo each other.

### 6.2 Impulse-based (optional heavy layer)
From the manifold `{normal n, penetration}`: relative velocity along `n`; if separating, skip;
else impulse `j = −(1+e)·(v·n) / (invMassA+invMassB)`, apply `±j·invMass·n`; **Coulomb
friction** as a tangent impulse clamped to `μ·j`; **Baumgarte/linear positional correction**
with slop+percent to remove sink.

| Aspect | Arcade | Impulse |
|---|---|---|
| State | pos, vel | + mass, restitution, friction, ang.vel |
| Feel | crisp, predictable | physical, bouncy, stackable |
| Determinism | easy | needs fixed iteration count + order |
| Use for | platformers, top-down, tactics | physics toys, ragdolls, crates |

Arcade = detect-and-teleport-out; impulse = solve velocities so next step doesn't penetrate.
Ship arcade first-class, impulse as an advanced module.

---

## 7. Determinism (byte-identical, fixed timestep)

The library's key differentiator.

- **Fixed timestep + accumulator** — never step with variable `dt` (tunneling, blow-ups,
  non-reproducibility): `while acc ≥ dt do world <- step world dt; acc <- acc − dt`; render
  interpolation `alpha = acc/dt` **never feeds the simulation**; clamp `frameTime` (≤ 0.25s) to
  avoid the spiral of death.
- **Deterministic order.** Divergence usually comes from *order*, not math: candidate pairs,
  contacts, and resolution must be processed in a **total, content-derived order** (sort by
  `(idA,idB)` / spatial key), never by hash-map enumeration. Any RNG is explicit, seeded, part
  of world state.
- **Fixed-point for the deterministic tier.** Float is deterministic only on a fixed
  compiler+ISA (x87 vs SSE, `/fp:fast`, FMA contraction, divergent transcendentals). For
  **cross-platform byte-identical** (lockstep, replays, server-verified tactics), the sim core
  uses **integer / Q-format fixed-point** (`Fixed`) — exact and portable; float is the fast
  non-networked option only.
- **Guard NaN/Inf** — branch every divide (`d==0` in slab/circle-AABB), clamp before `sqrt`,
  prefer squared-distance; NaN silently poisons every comparison and determinism.
- **Reproducibility harness** — checksum the world each tick; replaying the same input stream
  must reproduce the checksum sequence (the desync/regression tripwire).

---

## 8. Functional / F# design

```fsharp
type Vec2  = { X: Fixed; Y: Fixed }                     // Fixed = Q-format value type
type Shape =
    | Aabb    of min: Vec2 * max: Vec2                   // grows FS.GG.Game.Core.Geometry
    | Circle  of center: Vec2 * radius: Fixed
    | Capsule of a: Vec2 * b: Vec2 * radius: Fixed
    | Obb     of center: Vec2 * halfExt: Vec2 * rotation: Fixed
    | Convex  of vertices: Vec2[]                        // CCW, convex invariant

type Contact = { Normal: Vec2; Penetration: Fixed; Point: Vec2 }
type Hit     = { Time: Fixed; Normal: Vec2; Point: Vec2 }

val overlap  : Shape -> Shape -> Contact option          // narrow phase, pure
val sweep    : Shape -> Vec2 -> Shape -> Hit option       // move A by delta vs B
val raycast  : Vec2 -> Vec2 -> Fixed -> Shape -> Hit option
```

Detection returns **manifests as values**; response is a separate pure transform
`resolve : Body → Contact → Body` (fold over contacts). Broad phase is an **`IBroadPhase`**
over immutable structures returning **sorted, deduped** index pairs (grid / SAP / quadtree
swappable behind one signature). `[<Struct>]` on `Vec2`/`Fixed`/`Contact` avoids allocation in
hot loops while staying immutable. MVU fit: the whole step is a deterministic reducer; contacts
can also surface as messages (`Collided(a,b,normal)`) for game logic to consume decoupled from
resolution.

---

## 9. Testing

- **Symmetry:** `overlap A B ⇔ overlap B A`; `contact(A,B).Normal = −contact(B,A).Normal`.
- **Separation after resolution:** after push-out, penetration ≤ slop (post-condition on `resolve`).
- **No-tunnelling:** for any velocity, a swept query against a wall reports a hit with `0 ≤ t ≤ 1`
  whenever start and end straddle the wall.
- **Sweep ⊆ static consistency:** if `sweep` says `t<1`, a static overlap at that advanced
  position agrees within epsilon.
- **Ray monotonicity:** ray-AABB `t` == slab `tmin`; near-parallel still classifies correctly.
- **Golden / checksum regression:** record `Contact`/`Hit` outputs and per-tick world
  **checksums** for canonical scenes (the determinism tripwire from §7).
- **Degenerate cases (enumerate explicitly, don't trust random gen):** zero-size AABB,
  zero-radius circle, coincident centers, exactly-touching (penetration 0), circle center
  inside AABB (normal fallback), ray parallel to slab, ray origin inside box, zero-length
  capsule, collinear polygon vertices, entity on a tile seam.

FsCheck property-based testing with shrinking is ideal for these geometric invariants.

---

## 10. API surface (what "comprehensive" means)

```fsharp
// value types: Vec2, Fixed (Q-format), Shape (DU), Aabb helpers (grow Scene.Geometry)
overlap        : Shape -> Shape -> Contact option
contains       : Shape -> Vec2  -> bool
distance       : Shape -> Shape -> Fixed                 // separation (0 if overlapping)
closestPoint   : Shape -> Vec2  -> Vec2
sweep          : Shape -> delta:Vec2 -> Shape -> Hit option
raycast        : origin:Vec2 -> dir:Vec2 -> maxT:Fixed -> Shape -> Hit option
tileRaycast    : origin:Vec2 -> dir:Vec2 -> Grid -> HitCell seq            // Amanatides-Woo
type IBroadPhase = abstract Pairs : Shape[] -> struct(int*int)[]           // sorted, deduped
resolveKinematic : Body -> Contact -> Body                                 // slide / push-out
resolveImpulse   : Body * Body -> Contact -> Body * Body                    // optional
moveAndCollide   : Aabb -> Vec2 -> TileMap -> struct(Aabb * Collisions)     // resolve X then Y
```

`Collisions` flags (`touchedLeft/right/top/bottom`, `grounded`) are what platformer game-logic
consumes.

---

## 11. Pitfalls to design out

| # | Pitfall | Mitigation |
|---|---|---|
| 1 | Floating error at tile seams | resolve X-then-Y, sort tiles by overlap area, integer/fixed tile coords |
| 2 | Corner-catching / internal edges | least-penetration axis + most-overlap-first; skip-internal-edge flags |
| 3 | Ghost collisions on flat multi-tile surfaces | conservative advancement / internal-edge suppression |
| 4 | Double-resolution / order dependence | canonical pair keys + dedup; deterministic sorted resolution; fixed iteration count |
| 5 | Tunnelling | swept AABB / sub-stepping / speculative contacts + fixed timestep |
| 6 | Circle center inside AABB (`diff=0`) | least-penetration face fallback |
| 7 | Coincident centers (circle-circle) | deterministic default axis |
| 8 | NaN poisoning | guard divides/sqrt; squared-distance |
| 9 | Sticking on diagonals | separate axes (X then Y) |
| 10 | Non-deterministic pair iteration order | sorted content-derived order (§7) |

---

## 12. Effort & placement

- **Home:** `FS.GG.Game.Core`, growing `FS.GG.Game.Core.Geometry` into a
  `FS.GG.UI.Scene.Collision` surface (narrow/broad/sweep/resolve); depends *down* only. Broad
  phase reuses the grids sibling's spatial hash.
- **Phasing:** P0 shape DU + narrow-phase (AABB/circle/segment) + `moveAndCollide` tile
  resolution + `Fixed` (M — the core most specs need); P1 swept AABB + broad-phase
  `IBroadPhase` + fixed-timestep harness + golden checksums (M); P2 SAT/OBB/capsule + slopes +
  one-way platforms (M); P3 optional impulse module (M–L).
- **Contract note:** growing the `collision` skill body is a Rendering library-surface bump the
  `.github` skill-registry reconciles; the fixed-point-determinism decision warrants a
  Rendering-local ADR.

---

## 13. Sources

[Metanet — N Tutorial A (SAT) ](http://www.metanetsoftware.com/technique/tutorialA.html) & [B (broad phase)](https://www.metanetsoftware.com/technique/tutorialB.html) ·
[dyn4j — SAT + MTV](https://dyn4j.org/2010/01/sat/) ·
[GameDev.net — Swept AABB detection & response](https://www.gamedev.net/articles/programming/general-and-gameplay-programming/swept-aabb-collision-detection-and-response-r3084/) ·
[tesselode — commented swept AABB (gist)](https://gist.github.com/tesselode/e1bcf22f2c47baaedcfc472e78cac55e) ·
[Christer Ericson — Real-Time Collision Detection](https://realtimecollisiondetection.net/) ·
[Slab method (Wikipedia)](https://en.wikipedia.org/wiki/Slab_method) ·
[Roman Wiche — classic ray-AABB revisited](https://medium.com/@bromanz/another-view-on-the-classic-ray-aabb-intersection-algorithm-for-bvh-traversal-41125138b525) ·
[Amanatides & Woo — fast voxel traversal (PDF)](http://www.cse.yorku.ca/~amana/research/grid.pdf) ·
[Wildbunny — speculative contacts](https://wildbunny.co.uk/blog/2011/03/25/speculative-contacts-an-continuous-collision-engine-approach-part-1/) ·
[Bepu — continuous collision detection](https://docs.bepuphysics.com/ContinuousCollisionDetection.html) ·
[Jonathan Whiting — 2D tilemap collision (X then Y)](https://jonathanwhiting.com/tutorial/collision/) ·
[danjb — tile platformer slope physics](https://danjb.com/game_dev/tilebased_platformer_slopes_2) ·
[undisbeliever — tile collisions (one-way, slopes)](https://undisbeliever.net/blog/20200110-tile-collisions.html) ·
[Geoff Blair — order matters: tile-map collision response](https://www.geoffblair.com/blog/order-matters-tile-map-collision-response/) ·
[Build New Games — broad-phase via spatial partitioning](http://buildnewgames.com/broad-phase-collision-detection/) ·
[Bob Nystrom — Spatial Partition](https://gameprogrammingpatterns.com/spatial-partition.html) ·
[Tuts+ — custom 2D physics: basics & impulse resolution](https://code.tutsplus.com/how-to-create-a-custom-2d-physics-engine-the-basics-and-impulse-resolution--gamedev-6331t) ·
Gaffer On Games — [Fix Your Timestep](https://gafferongames.com/post/fix_your_timestep/), [Floating Point Determinism](https://gafferongames.com/post/floating_point_determinism/), [Deterministic Lockstep](https://gafferongames.com/post/deterministic_lockstep/) ·
[LearnOpenGL — 2D collision detection](https://learnopengl.com/In-Practice/2D-Game/Collisions/Collision-detection) ·
[MDN — 2D collision detection](https://developer.mozilla.org/en-US/docs/Games/Techniques/2D_collision_detection).

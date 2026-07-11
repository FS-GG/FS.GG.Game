// Typecheck fixtures for fs-gg-effects (see scripts/typecheck-md-blocks.fsx).
//
// The recurring world here is a damage `Kind`, a `Unit` target with the stats the stages read, and
// a `pipeline` of stages over the two. Each block is its own module, so the same names are
// re-declared per section rather than shared — a block must typecheck against the world its OWN
// prose describes, not against a world some earlier block set up.

//#block 1
// The floor-erases-the-zero pitfall. Deliberately WRONG arithmetic, but well-typed — the gate reads
// types, and the prose owns the semantics.
let damage = 30.0
let resist = 1.0

//#block 2
type Kind = Frost | Physical
type Unit =
    { Vulnerable: float
      Armor: float
      Resists: Map<string, float> }
    member u.ResistTo(_k: Kind) = 0.0
let isElemental (k: Kind) = k = Frost
let spectre : Unit = { Vulnerable = 0.2; Armor = 5.0; Resists = Map.empty }

//#block 3
type Kind = Frost | Physical
type Slow = { Factor: float }
type Unit =
    { Hp: int
      Slows: Effects.Active<Slow> list }
// `Stage` / `Damage` are top-level types in FS.GG.Game.Core; only `Active` / `Policy` live inside
// the [<RequireQualifiedAccess>] `Effects` module.
let pipeline : Stage<Unit, Kind> list = []
let slowMagnitude (e: Slow) = 1.0 - e.Factor

//#block 4
// `region` is what a region operator (Ballistics.splash / SpatialGrid.queryRadius) returned:
// (target, transport multiplier) pairs.
type Enemy = { Hp: int }
let region : (Enemy * float) list = []

//#block 5
//#rec
type Kind = Frost | Physical
type Unit =
    { Tile: Cell
      ArmoredReduction: float }
let coverOf (_tile: Cell) = 0.25
// Pin the damage `Kind`. The section quotes two stages OUT of a pipeline, and a bare `let cover =
// Effects.gatedBy ...` leaves 'K free, so F# refuses it under the value restriction (FS0030). That
// is an artefact of quoting a fragment, not a defect in the guidance: a Stage only ever exists
// inside a `Stage<'T,'K> list`, which is what fixes 'K. Building that list here — forward-
// referencing the block's own `cover`/`armored`, hence //#rec — restores the context the reader
// has and leaves the stages themselves fully typechecked.
// (A function, not a value: a value here would be a forward *initialisation* cycle rather than a
// mere forward type reference, which `module rec` rejects outright — FS0695.)
let private stagesInAPipeline () : Stage<Unit, Kind> list = [ cover; armored ]

//#block 6
//#skip a RIGHT/WRONG contrast: it binds `let armor` TWICE (the Kind-keyed stage, then the Source-gated mistake), which is a duplicate definition in one module by design. Compiling it would mean restructuring the block — i.e. typechecking something other than what the reader reads.

//#block 7
type Kind = Frost | Physical
type Unit = { Vulnerable: float; Armor: float }
let vulnerableBonus (u: Unit) = u.Vulnerable
let resistOf (_u: Unit) (_k: Kind) = 0.0
let armorOf (u: Unit) (_k: Kind) = u.Armor

//#block 8
// Slow stacking. `Effects.Strongest` takes the magnitude projection; `frost` is the reader's own
// effect payload, and the section's whole point is that its magnitude is INVERTED (a stronger slow
// has the lower factor), so `Factor` is the field the policy reads.
type Slow = { Factor: float }
type Unit = { Slows: Effects.Active<Slow> list }
let frost : Slow = { Factor = 0.65 }
let unit : Unit = { Slows = [] }

//#block 9
type Slow = { Factor: float }
type Unit = { Effects: Effects.Active<Slow> list }
let u : Unit = { Effects = [] }

//#block 10
// Environmental push. Every predicate below is "the only coupling to your world".
type Unit = { Cell: Cell; Hp: int; PushDistance: int }
let unit : Unit = { Cell = { Col = 4; Row = 4 }; Hp = 30; PushDistance = 3 }
let direction : Cell = { Col = 1; Row = 0 }
let inBounds (_c: Cell) = true
let isWall (_c: Cell) = false
let occupied (_c: Cell) = false
let isWater (_c: Cell) = false
let isChasm (_c: Cell) = false
let isLava (_c: Cell) = false
let lavaTick = 5
let die (u: Unit) = u
let collide (u: Unit) (_obstacle: Cell) = u

//#block 11
//#skip the same before/after migration contrast as fs-gg-collision block 4: it binds `let landed` TWICE (removed `Resolution.knockback`, then the `Resolution.push` replacement) — a duplicate definition in one module by design.

//#block 12
//#rec
// Region operator + pipeline: splash decides WHO, the pipeline decides FOR HOW MUCH. As in
// fs-gg-ballistics block 5, the block declares its OWN `Enemy` (storing a `Geometry.Vec2`) and its
// own `simPoint` crossing, so the fixture binds free VALUES only and forward-references the block's
// type via //#rec. Redeclaring `Enemy` here would collide with the block's and mean the gate
// typechecked the fixture's world rather than the skill's.
type Kind = Frost | Physical
let enemies : Enemy list = []
let blast : Geometry.Vec2 = { Vx = 100.0; Vy = 100.0 }
let pipeline : Stage<Enemy, Kind> list = []
let applyDamage (_e: Enemy) (_amount: float) = ()
let applyRider (_e: Enemy) = ()

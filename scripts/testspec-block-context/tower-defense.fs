// Typecheck fixtures for the tower-defense TestSpec (see scripts/typecheck-md-blocks.fsx).
//
// The spec's §5 sketch names a vocabulary of ids, damage types and per-run state that the prose
// describes but never declares in code. Supplied here on block 1; blocks 2-8 inherit them, because a
// cumulative corpus opens its predecessors.
//
// `Difficulty` is NOT here: FS.GG.Game.Core ships it, and the block resolves to the real one — which
// is the whole point of compiling against the built assembly rather than a reconstruction.

//#block 1 "type Vec2 = Geometry.Vec2"
// --- ids ---
type TowerId = TowerId of int
type EnemyId = EnemyId of int
type ProjId = ProjId of int
type MapId = MapId of int

// --- combat vocabulary (§4) ---
type DamageType = Physical | Magic | Energy | Siege
type TowerSpecial =
    | NoSpecial
    | Splash of radius: float
    | SlowOnHit of factor: float * seconds: float
    | Chain of jumps: int
    | PoisonOnHit of dps: float * seconds: float
    | ApplyVulnerable of bonus: float * seconds: float

// --- board + run state (§5) ---
type TileKind = Buildable | Path | Blocked | Spawn | Goal
type TransientFx =
    | Beam of from: Geometry.Vec2 * to_: Geometry.Vec2 * life: float
    | Explosion of at: Geometry.Vec2 * radius: float * life: float
type RngState = { Seed: uint64 }
type RunStats = { Kills: int; Leaks: int; DamageDealt: float }

//#block 7 "| MenuUp | MenuDown              // move cursor (wraps)"
// A DU-CASE CONTINUATION. The prose above this block says "add these cases to your Msg"; the block
// is written as bare `| Case` lines with no `type ... =` header, so it cannot stand alone. The
// fixture supplies the header the prose left implicit, and the block's cases are then compiled
// verbatim below it — which is the point: the case SHAPES (`MenuAdjust of dir:int`) are what a
// reader copies, and they are what this checks.
type MenuMsg =

//#block 8 "acc <- acc + realDt"
// The fixed-step accumulator fragment (§12). The block is a BODY, lifted out of the Tick handler —
// its free names are the handler's locals, so they are bound here. `Model` and `simStep` come from
// the document itself (block 5 declares `Model`; the accumulator is what drives it).
let mutable acc = 0.0
let realDt = 1.0 / 60.0
let dtFixed = 1.0 / 60.0
let maxStepsPerFrame = 5
let model : Model = Unchecked.defaultof<Model>
let simStep (_dt: float) (m: Model) : Model = m

// Typecheck fixtures for the metroidvania TestSpec (see scripts/typecheck-md-blocks.fsx).
//
// The spec's §5 sketch names a vocabulary of ids and small states that the prose describes but never
// declares in code (the reader is expected to define them). They are supplied here, on block 1, and
// every later block inherits them: a cumulative corpus opens its predecessors, so one declaration
// serves the whole document.
//
// //#rec because these forward-reference types the BLOCK declares below them (`EnemyKind`, and the
// `Vec2` abbreviation it introduces) — which is exactly what a recursive module is for.

//#block 1 "type Vec2 = Geometry.Vec2"
//#rec
// --- ids: opaque handles the prose refers to by name ---
type RoomId = RoomId of int
type DoorId = DoorId of int
type BossId = BossId of int
type PickupId = PickupId of int

// --- small states the §4 prose enumerates ---
type AmbushPhase = Lurking | Winding | Leaping | Recovering
type EnemyState = Patrolling | Aggro | Attacking | Stunned | Dying
type PlayerState = Standing | Running | Jumping | Falling | Dashing | WallSliding | Grappling | Hurt
type WallSide = LeftWall | RightWall
type CameraMode = RoomLocked | FollowPlayer | Panning
type Action = Jump | Attack | Dash | Grapple | Interact | OpenMap

// --- room contents ---
type TileLayer = { Tiles: int[,] }
type Door = { Id: DoorId; Leads: RoomId }
type Hazard = { At: Vec2; Damage: int }
type Pickup = { Id: PickupId; At: Vec2 }
type EnemySpawn = { Kind: EnemyKind; At: Vec2 }
type BossState = { Id: BossId; Phase: int; Hp: int }

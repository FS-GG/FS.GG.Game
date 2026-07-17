// Typecheck fixtures for the roguelike-dungeon-crawler TestSpec (see scripts/typecheck-md-blocks.fsx).
//
// The spec's §5 sketch leans on a vocabulary of item/room/run types the prose describes but never
// declares in code. Supplied here on block 1; every later block inherits them, because a cumulative
// corpus opens its predecessors.
//
// These deliberately use `Geometry.Vec2` rather than the block's `Vec2` abbreviation, so the fixture
// needs no forward reference and no //#rec — the abbreviation is transparent, so the doc's
// `Pos: Vec2` and this file's `Geometry.Vec2` are the same type.

//#block 1 "type Vec2 = Geometry.Vec2"
// --- ids ---
type RoomId = RoomId of int
type ItemId = ItemId of int

// --- the player's derived/pickup state (§5.1) ---
type PlayerStats = { Damage: float; FireRate: float; Speed: float; Range: float; Luck: int }
type Health = { Red: int; Soul: int; Black: int }      // half-hearts
type RollState = NotRolling | Rolling of since: float | RollCooldown of until: float
type Currency = { Coins: int; Keys: int; Bombs: int }
type ActiveItem = { Id: ItemId; ChargeNeeded: int }
type Owner = PlayerOwned | EnemyOwned

// --- room contents (§5.3) ---
//
// `EnemyKind` and the four stat fields below are NOT an invention: §5.2 specifies the roster as a
// TABLE (Radius / HP / Speed / Threat / Contact dmg) and §9.2's stats table then names the type in
// prose — `killsByType : Map<EnemyKind, int>` and `deathCause : Enemy of EnemyKind | Trap | Bomb` —
// without any block ever declaring it. §6 leans on the same table again ("enemy threat budget per
// combat room: 6 + 2*floorIndex"), so `Threat` is load-bearing game logic, not decoration.
//
// This fixture's job is to supply what the PROSE describes but no block declares, modelling the
// document's types rather than a friendlier version of them. A 3-field `{ Pos; Hp; Contact }` stand-in
// under-modelled a roster the document specifies in full, and §8.1's Enemy → Token ChannelMap is the
// first block to actually bind against it.
type EnemyKind = Grub | Maggot | Spitter | FlySwarmNode | Charger | Turret | Caster | Brute
type Enemy =
    { Pos: Geometry.Vec2
      Kind: EnemyKind
      Hp: float
      MaxHp: float          // §5.2's HP column; `Hp` alone cannot drive a 0..1 Health fraction
      Threat: int           // §5.2's Threat column, 1..6 — the §6 room-budget currency
      Contact: float }
type Pickup = { Pos: Geometry.Vec2; Item: ItemId }
type Obstacle = { Pos: Geometry.Vec2; Blocking: bool }
type Door = { ToRoom: RoomId; Locked: bool }
type Boss = { Hp: float; Phase: int }
type Particle = { Pos: Geometry.Vec2; Vel: Geometry.Vec2; Life: float }
type FloorTheme = Basement | Caves | Depths | Womb

// --- run + meta (§5.4) ---
type RunSummary = { Floors: int; Kills: int; Seconds: float }
type RunStats = { Kills: int; DamageTaken: float; Seconds: float }
type MetaProfile = { Unlocked: Set<ItemId>; Runs: int }
type Settings = { Volume: float; ScreenShake: bool }
type InputState = { MoveX: float; MoveY: float; AimX: float; AimY: float; Firing: bool }
type TitleCmd = NewRun | Continue | OpenOptions | Quit

// RE-KEYED 5 -> 7: §8.1 inserted TWO blocks ahead of this one — the Enemy → Token ChannelMap and the
// §14 legibility assertion — shifting every later ordinal down by two. 5 was still a valid ordinal in
// a now-7-block document, so only the anchor catches it — see the harness's §1b.
//#block 7 "| MenuUp | MenuDown              // move cursor (wraps)"
// A DU-CASE CONTINUATION. The prose above this block says "add these cases to your Msg"; the block
// is written as bare `| Case` lines with no `type ... =` header, so it cannot stand alone. The
// fixture supplies the header the prose left implicit, and the block's cases are then compiled
// verbatim below it — which is the point: the case SHAPES (`MenuAdjust of dir:int`) are what a
// reader copies, and they are what this checks.
type MenuMsg =

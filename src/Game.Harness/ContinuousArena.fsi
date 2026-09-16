namespace FS.GG.Game.Harness

open FS.GG.Game.Core

/// Neutral arena item semantics used by the runtime qualification product.
[<RequireQualifiedAccess>]
type ContinuousArenaItemKind =
    | Collectible of score: int
    | Hazard of damage: int

/// A trigger volume and its product outcome.
type ContinuousArenaItem =
    {
        Id: string
        Bounds: Rect
        Kind: ContinuousArenaItemKind
    }

/// Complete deterministic configuration for one neutral arena.
type ContinuousArenaConfig =
    {
        PlayerStart: Rect
        Speed: float
        InitialHealth: int
        WinningScore: int
        CollisionCellSize: float
        Obstacles: KinematicCollider list
        Items: ContinuousArenaItem list
    }

/// Terminal state of the representative product.
[<RequireQualifiedAccess>]
type ContinuousArenaOutcome =
    | Running
    | Won
    | Lost

/// Authority state for the representative continuous game.
type ContinuousArenaWorld =
    {
        Player: Rect
        Velocity: Point
        Health: int
        Score: int
        Collected: Set<string>
        Outcome: ContinuousArenaOutcome
    }

/// Stable renderer-facing projection with no simulation authority.
type ContinuousArenaProjection =
    {
        Player: Rect
        Health: int
        Score: int
        Outcome: ContinuousArenaOutcome
    }

/// Pure representative game composed through the standard harness command frontier.
[<RequireQualifiedAccess>]
module ContinuousArena =

    /// Create initial authority state from a configuration.
    val init: config: ContinuousArenaConfig -> ContinuousArenaWorld

    /// Apply one semantic command. Fire restarts a terminal arena.
    val apply: config: ContinuousArenaConfig -> command: Command -> world: ContinuousArenaWorld -> ContinuousArenaWorld

    /// Advance movement, collisions, triggers, health, score and terminal outcomes by one fixed step.
    val step: config: ContinuousArenaConfig -> world: ContinuousArenaWorld -> dt: float -> ContinuousArenaWorld

    /// Adapt the arena to the existing real-input headless harness.
    val playable:
        config: ContinuousArenaConfig -> keymap: Map<'key, Command> -> dt: float -> Playable<ContinuousArenaWorld, 'key>
            when 'key: comparison

    /// Project the authority state for a retained renderer.
    val project: world: ContinuousArenaWorld -> ContinuousArenaProjection

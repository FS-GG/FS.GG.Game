namespace FS.GG.Game.Harness

open System
open FS.GG.Game.Core

[<RequireQualifiedAccess>]
type ContinuousArenaItemKind =
    | Collectible of score: int
    | Hazard of damage: int

type ContinuousArenaItem =
    {
        Id: string
        Bounds: Rect
        Kind: ContinuousArenaItemKind
    }

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

[<RequireQualifiedAccess>]
type ContinuousArenaOutcome =
    | Running
    | Won
    | Lost

type ContinuousArenaWorld =
    {
        Player: Rect
        Velocity: Point
        Health: int
        Score: int
        Collected: Set<string>
        Outcome: ContinuousArenaOutcome
    }

type ContinuousArenaProjection =
    {
        Player: Rect
        Health: int
        Score: int
        Outcome: ContinuousArenaOutcome
    }

[<RequireQualifiedAccess>]
module ContinuousArena =

    let private finite value =
        not (Double.IsNaN value || Double.IsInfinity value)

    let init (config: ContinuousArenaConfig) : ContinuousArenaWorld =
        {
            Player = config.PlayerStart
            Velocity = { X = 0.0; Y = 0.0 }
            Health = max 0 config.InitialHealth
            Score = 0
            Collected = Set.empty
            Outcome =
                if config.InitialHealth > 0 then
                    ContinuousArenaOutcome.Running
                else
                    ContinuousArenaOutcome.Lost
        }

    let apply (config: ContinuousArenaConfig) (command: Command) (world: ContinuousArenaWorld) : ContinuousArenaWorld =
        if command = Command.Fire && world.Outcome <> ContinuousArenaOutcome.Running then
            init config
        elif world.Outcome <> ContinuousArenaOutcome.Running then
            world
        else
            let speed = if finite config.Speed then max 0.0 config.Speed else 0.0

            match command with
            | Command.MoveNorth ->
                { world with
                    Velocity = { X = 0.0; Y = -speed }
                }
            | Command.MoveSouth ->
                { world with
                    Velocity = { X = 0.0; Y = speed }
                }
            | Command.MoveWest ->
                { world with
                    Velocity = { X = -speed; Y = 0.0 }
                }
            | Command.MoveEast ->
                { world with
                    Velocity = { X = speed; Y = 0.0 }
                }
            | Command.Fire
            | Command.Pause -> world

    let private itemCollider (item: ContinuousArenaItem) : KinematicCollider =
        {
            Id = "item:" + item.Id
            Shape = KinematicShape.AxisAlignedBox item.Bounds
            Response = KinematicResponse.Trigger
        }

    let step (config: ContinuousArenaConfig) (world: ContinuousArenaWorld) (dt: float) : ContinuousArenaWorld =
        if world.Outcome <> ContinuousArenaOutcome.Running || not (finite dt) || dt <= 0.0 then
            world
        else
            let itemByCollider =
                config.Items |> List.map (fun item -> "item:" + item.Id, item) |> Map.ofList

            let colliders = config.Obstacles @ (config.Items |> List.map itemCollider)

            let displacement: Point =
                {
                    X = world.Velocity.X * dt
                    Y = world.Velocity.Y * dt
                }

            let result =
                Kinematics.advance
                    config.CollisionCellSize
                    {
                        Bounds = world.Player
                        Displacement = displacement
                    }
                    colliders

            let hitItems =
                result.Hits
                |> List.choose (fun hit -> Map.tryFind hit.ColliderId itemByCollider)
                |> List.distinctBy _.Id

            let mutable score = world.Score
            let mutable health = world.Health
            let mutable collected = world.Collected

            for item in hitItems do
                match item.Kind with
                | ContinuousArenaItemKind.Collectible value when not (collected.Contains item.Id) ->
                    score <- score + max 0 value
                    collected <- collected.Add item.Id
                | ContinuousArenaItemKind.Hazard value -> health <- health - max 0 value
                | _ -> ()

            let outcome =
                if health <= 0 then
                    ContinuousArenaOutcome.Lost
                elif config.WinningScore > 0 && score >= config.WinningScore then
                    ContinuousArenaOutcome.Won
                else
                    ContinuousArenaOutcome.Running

            let velocity =
                {
                    X = result.Displacement.X / dt
                    Y = result.Displacement.Y / dt
                }

            {
                Player = result.Bounds
                Velocity = velocity
                Health = max 0 health
                Score = score
                Collected = collected
                Outcome = outcome
            }

    let playable
        (config: ContinuousArenaConfig)
        (keymap: Map<'key, Command>)
        (dt: float)
        : Playable<ContinuousArenaWorld, 'key> when 'key: comparison =
        {
            Init = init config
            Keymap = keymap
            Apply = apply config
            Step = step config
            Dt = dt
        }

    let project (world: ContinuousArenaWorld) : ContinuousArenaProjection =
        {
            Player = world.Player
            Health = world.Health
            Score = world.Score
            Outcome = world.Outcome
        }

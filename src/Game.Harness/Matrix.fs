namespace FS.GG.Game.Harness

open FS.GG.Game.Core

[<RequireQualifiedAccess>]
type Seat =
    | A
    | B

type MatchSetup<'world, 'view> =
    { Dt: float
      Init: Rng -> 'world
      Observe: Seat -> 'world -> 'view
      Apply: Seat -> Command -> 'world -> 'world
      Step: 'world -> float -> 'world
      IsOver: 'world -> bool
      MaxSteps: int }

type Match<'view> =
    { Seed: uint64
      A: Bot<'view>
      B: Bot<'view> }

[<RequireQualifiedAccess>]
module Matrix =

    // Decorrelate the shared decision stream from the world-init stream so a bot's draws are not
    // correlated with the starting layout, while both stay a pure function of the match seed.
    [<Literal>]
    let private decisionSalt = 0x9E3779B97F4A7C15UL

    let private applySeat
        (setup: MatchSetup<'world, 'view>)
        (seat: Seat)
        (commands: Command list)
        (world: 'world)
        : 'world =
        commands |> List.fold (fun w c -> setup.Apply seat c w) world

    let runMatch (setup: MatchSetup<'world, 'view>) (outcome: 'world -> 'o) (game: Match<'view>) : 'o =
        let mutable world = setup.Init(Rng.ofSeed game.Seed)
        let mutable rng = Rng.ofSeed(game.Seed ^^^ decisionSalt)
        let mutable steps = 0

        while not (setup.IsOver world) && steps < setup.MaxSteps do
            let viewA = setup.Observe Seat.A world
            let viewB = setup.Observe Seat.B world
            let struct (commandsA, rngA) = game.A.Decide viewA rng
            let struct (commandsB, rngB) = game.B.Decide viewB rngA
            rng <- rngB
            world <- applySeat setup Seat.A commandsA world
            world <- applySeat setup Seat.B commandsB world
            world <- setup.Step world setup.Dt
            steps <- steps + 1

        outcome world

    let runMatrix
        (setup: MatchSetup<'world, 'view>)
        (outcome: 'world -> 'o)
        (matches: Match<'view> list)
        : (Match<'view> * 'o) list =
        matches |> List.map (fun game -> (game, runMatch setup outcome game))

    let winRate (isWin: 'o -> bool) (outcomes: 'o list) : float =
        match outcomes with
        | [] -> 0.0
        | _ ->
            let wins = outcomes |> List.filter isWin |> List.length
            float wins / float (List.length outcomes)

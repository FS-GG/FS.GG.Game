/// A minimal, deterministic integer-grid Pong used as the reference sim the harness tests dogfood.
/// Integer-only arithmetic makes every trace a literal golden with no float tolerance. It exercises
/// the real input frontier (raw key -> keymap -> `Command` -> step): the left paddle is driven by
/// `Command.MoveNorth`/`MoveSouth`, the ball integrates one grid cell per fixed step and bounces off
/// the walls and paddles, and a missed ball scores and re-serves from `Rng`. It is a fixture, not part
/// of the package — the package is game-agnostic.
module Game.Harness.Tests.PongSim

open FS.GG.Game.Core
open FS.GG.Game.Harness

[<Literal>]
let W = 16 // grid width (columns 0 .. W-1); columns 0 and W-1 are the paddle/goal columns

[<Literal>]
let H = 12 // grid height (rows 0 .. H-1)

[<Literal>]
let P = 4 // paddle height in rows

[<Literal>]
let WinScore = 3

/// The whole game state. Structural equality (incl. the value `Rng`) is what makes a trace a value.
type Pong =
    { BallX: int
      BallY: int
      BallDX: int // -1 or +1
      BallDY: int // -1 or +1
      LeftY: int // left paddle top row
      RightY: int // right paddle top row
      ScoreL: int
      ScoreR: int
      Rng: Rng
      Tick: int }

let private clampPaddle (y: int) : int = max 0 (min y (H - P))

/// A paddle whose top row is `top` covers `row`?
let private covers (top: int) (row: int) : bool = row >= top && row <= top + P - 1

/// Re-serve from centre toward the side just scored on (the loser receives); the launch direction is
/// drawn from `Rng` and the advanced generator threaded back.
let private serve (towardLeft: bool) (rng: Rng) : struct (int * int * int * int * Rng) =
    let dx = if towardLeft then -1 else 1
    let struct (up, rng') = Rng.nextBool rng
    let dy = if up then -1 else 1
    struct (W / 2, H / 2, dx, dy, rng')

/// The starting world, seeded. Ball at centre; both paddles centred; first launch direction from `Rng`.
let init (rng: Rng) : Pong =
    let struct (up, rng') = Rng.nextBool rng

    { BallX = W / 2
      BallY = H / 2
      BallDX = 1
      BallDY = (if up then -1 else 1)
      LeftY = clampPaddle (H / 2 - P / 2)
      RightY = clampPaddle (H / 2 - P / 2)
      ScoreL = 0
      ScoreR = 0
      Rng = rng'
      Tick = 0 }

/// One whole fixed step. `dt` is ignored — a discrete grid has nothing to interpolate.
let step (w: Pong) (_dt: float) : Pong =
    // vertical move + wall bounce
    let mutable ny = w.BallY + w.BallDY
    let mutable ndy = w.BallDY

    if ny < 0 then
        ny <- 0
        ndy <- 1
    elif ny > H - 1 then
        ny <- H - 1
        ndy <- -1

    let nx = w.BallX + w.BallDX

    if nx <= 0 then
        // arrived at the left paddle column
        if covers w.LeftY ny then
            { w with
                BallX = 1
                BallY = ny
                BallDX = 1
                BallDY = ndy
                Tick = w.Tick + 1 }
        else
            let struct (bx, by, bdx, bdy, rng') = serve true w.Rng

            { w with
                BallX = bx
                BallY = by
                BallDX = bdx
                BallDY = bdy
                ScoreR = w.ScoreR + 1
                Rng = rng'
                Tick = w.Tick + 1 }
    elif nx >= W - 1 then
        if covers w.RightY ny then
            { w with
                BallX = W - 2
                BallY = ny
                BallDX = -1
                BallDY = ndy
                Tick = w.Tick + 1 }
        else
            let struct (bx, by, bdx, bdy, rng') = serve false w.Rng

            { w with
                BallX = bx
                BallY = by
                BallDX = bdx
                BallDY = bdy
                ScoreL = w.ScoreL + 1
                Rng = rng'
                Tick = w.Tick + 1 }
    else
        { w with
            BallX = nx
            BallY = ny
            BallDY = ndy
            Tick = w.Tick + 1 }

/// The keymap the scripted driver folds through: raw string tokens -> device-free `Command`.
let keymap: Map<string, Command> =
    Map
        [ "w", Command.MoveNorth
          "s", Command.MoveSouth
          "space", Command.Fire
          "esc", Command.Pause ]

/// Apply a command to the LEFT paddle only (the single-seat scripted/bot view). Every command case is
/// handled so the match is exhaustive under warnings-as-errors.
let applyLeft (cmd: Command) (w: Pong) : Pong =
    match cmd with
    | Command.MoveNorth -> { w with LeftY = clampPaddle (w.LeftY - 1) }
    | Command.MoveSouth -> { w with LeftY = clampPaddle (w.LeftY + 1) }
    | Command.MoveWest
    | Command.MoveEast
    | Command.Fire
    | Command.Pause -> w

/// The single-seat `Playable` (left paddle), seeded deterministically.
let playable: Playable<Pong, string> =
    { Init = init (Rng.ofSeed 1UL)
      Keymap = keymap
      Apply = applyLeft
      Step = step
      Dt = 1.0 }

/// A seat's view: the ball's row and this seat's paddle top — enough to chase, and it exposes no world.
let observeSeat (seat: Seat) (w: Pong) : struct (int * int) =
    match seat with
    | Seat.A -> struct (w.BallY, w.LeftY)
    | Seat.B -> struct (w.BallY, w.RightY)

/// The left-seat view, for the single-seat bot driver.
let observeLeft (w: Pong) : struct (int * int) = observeSeat Seat.A w

/// Apply a seat's command to that seat's paddle.
let applySeat (seat: Seat) (cmd: Command) (w: Pong) : Pong =
    let dir =
        match cmd with
        | Command.MoveNorth -> -1
        | Command.MoveSouth -> 1
        | Command.MoveWest
        | Command.MoveEast
        | Command.Fire
        | Command.Pause -> 0

    match seat with
    | Seat.A -> { w with LeftY = clampPaddle (w.LeftY + dir) }
    | Seat.B -> { w with RightY = clampPaddle (w.RightY + dir) }

/// A bot that chases the ball, breaking an exact tie with a threaded `Rng` draw (so the draw is
/// deterministic in the seed and visibly consumes the generator).
let chaseBot: Bot<struct (int * int)> =
    { Decide =
        fun (struct (ballY, top)) rng ->
            let centre = top + P / 2

            if ballY < centre then struct ([ Command.MoveNorth ], rng)
            elif ballY > centre then struct ([ Command.MoveSouth ], rng)
            else
                let struct (up, rng') = Rng.nextBool rng
                struct ([ (if up then Command.MoveNorth else Command.MoveSouth) ], rng') }

/// A bot that never moves — the loser in a chase-vs-sitter match.
let sitterBot: Bot<struct (int * int)> = { Decide = fun _ rng -> struct ([], rng) }

/// The two-seat match wiring: first-to-`WinScore`, capped so a non-scoring match still terminates.
let matchSetup: MatchSetup<Pong, struct (int * int)> =
    { Dt = 1.0
      Init = init
      Observe = observeSeat
      Apply = applySeat
      Step = step
      IsOver = fun w -> w.ScoreL >= WinScore || w.ScoreR >= WinScore
      MaxSteps = 5000 }

/// Who won a finished match.
type Winner =
    | LeftWins
    | RightWins
    | Draw

let outcome (w: Pong) : Winner =
    if w.ScoreL > w.ScoreR then LeftWins
    elif w.ScoreR > w.ScoreL then RightWins
    else Draw

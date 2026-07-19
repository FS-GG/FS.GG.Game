module Game.Harness.Tests.BotCombinatorsTests

open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Harness
open Game.Harness.Tests.PongSim

// Pong seat view is struct (ballY, top); chase toward the ball compares ballY to the paddle centre.
let private ballY (struct (b, _): struct (int * int)) : int = b
let private paddleCentre (struct (_, top): struct (int * int)) : int = top + P / 2

[<Tests>]
let tests =
    testList
        "BotCombinators"
        [ testCase "FR-001 sitter issues no command and leaves the generator unchanged"
          <| fun _ ->
              let rng = Rng.ofSeed 5UL
              let struct (cmds, rng') = Bots.sitter.Decide (struct (3, 2)) rng
              Expect.isEmpty cmds "sitter issues nothing"
              Expect.equal rng' rng "sitter does not consume the generator"

          testCase "FR-002 scripted plays its frames in order then idles"
          <| fun _ ->
              let bot = Bots.scripted [ [ Command.MoveNorth ]; [ Command.MoveSouth ]; [] ]
              let step () = let struct (c, _) = bot.Decide (struct (0, 0)) (Rng.ofSeed 1UL) in c
              Expect.equal (step ()) [ Command.MoveNorth ] "frame 0"
              Expect.equal (step ()) [ Command.MoveSouth ] "frame 1"
              Expect.equal (step ()) [] "frame 2"
              Expect.equal (step ()) [] "past the end it idles"

          testCase "FR-003 random threads the caller draw"
          <| fun _ ->
              let draw rng = let struct (b, rng') = Rng.nextBool rng in struct ([ (if b then Command.Fire else Command.Pause) ], rng')
              let bot = Bots.random draw
              let struct (c1, r1) = bot.Decide (struct (0, 0)) (Rng.ofSeed 9UL)
              let struct (cDraw, rDraw) = draw (Rng.ofSeed 9UL)
              Expect.equal c1 cDraw "random returns the draw's commands"
              Expect.equal r1 rDraw "random threads the draw's generator"

          testCase "FR-004 chase issues up/down/idle and is deterministic in (view, seed)"
          <| fun _ ->
              let bot = Bots.chase (ballY, paddleCentre) Command.MoveNorth Command.MoveSouth
              let decide v = let struct (c, _) = bot.Decide v (Rng.ofSeed 0UL) in c
              Expect.equal (decide (struct (0, 4))) [ Command.MoveNorth ] "ball above centre -> up"
              Expect.equal (decide (struct (11, 4))) [ Command.MoveSouth ] "ball below centre -> down"
              Expect.equal (decide (struct (6, 4))) [] "ball on centre -> idle, no draw"
              let struct (_, ra) = bot.Decide (struct (6, 4)) (Rng.ofSeed 0UL)
              Expect.equal ra (Rng.ofSeed 0UL) "chase never consumes the generator"

          testCase "FR-004 chase reproduces chaseBot's non-tie directional behaviour"
          <| fun _ ->
              let bot = Bots.chase (ballY, paddleCentre) Command.MoveNorth Command.MoveSouth
              for v in [ struct (0, 4); struct (2, 4); struct (10, 3); struct (11, 0) ] do
                  let struct (cChase, _) = chaseBot.Decide v (Rng.ofSeed 0UL)
                  let struct (cBots, _) = bot.Decide v (Rng.ofSeed 0UL)
                  Expect.equal cBots cChase (sprintf "same non-tie decision as chaseBot for %A" v)

          testCase "FR-005 greedyToward picks the earliest max-scoring move, or [] when empty"
          <| fun _ ->
              let score _ (m: Command) = if m = Command.MoveSouth then 1.0 else 0.0
              let bot = Bots.greedyToward score [ Command.MoveNorth; Command.MoveSouth ]
              let struct (c, _) = bot.Decide (struct (0, 0)) (Rng.ofSeed 0UL)
              Expect.equal c [ Command.MoveSouth ] "the max-scoring move is chosen"
              // Tie -> earliest in the caller's order.
              let tie = Bots.greedyToward (fun _ _ -> 0.0) [ Command.MoveNorth; Command.MoveSouth ]
              let struct (ct, _) = tie.Decide (struct (0, 0)) (Rng.ofSeed 0UL)
              Expect.equal ct [ Command.MoveNorth ] "a tie resolves to the first move"
              let struct (ce, _) = (Bots.greedyToward score []).Decide (struct (0, 0)) (Rng.ofSeed 0UL)
              Expect.isEmpty ce "an empty move set idles"

          testCase "FR-006 on projects a wider view to the one the policy reads"
          <| fun _ ->
              // A wider view struct (ballY, top, score); the chaser must see only (ballY, top).
              let project (struct (b, t, _): struct (int * int * int)) : struct (int * int) = struct (b, t)
              let inner = Bots.chase (ballY, paddleCentre) Command.MoveNorth Command.MoveSouth
              let outer = Bots.on project inner
              let struct (c, _) = outer.Decide (struct (0, 4, 999)) (Rng.ofSeed 0UL)
              Expect.equal c [ Command.MoveNorth ] "decides on the projection, blind to the extra field"

          testCase "FR-007 Bots.sitter yields match outcomes identical to sitterBot"
          <| fun _ ->
              let outcome (w: Pong) = w.ScoreL - w.ScoreR
              let matchesWith (bot: Bot<struct (int * int)>) =
                  [ for s in 1UL..8UL -> { Seed = s; A = chaseBot; B = bot } ]
              let withSitterBot = Matrix.runMatrix matchSetup outcome (matchesWith sitterBot) |> List.map snd
              let withBotsSitter = Matrix.runMatrix matchSetup outcome (matchesWith Bots.sitter) |> List.map snd
              Expect.equal withBotsSitter withSitterBot "Bots.sitter reproduces sitterBot's outcomes exactly"

          testCase "FR-007 a second toy game reuses Bots.chase/Bots.sitter with zero bespoke bot code"
          <| fun _ ->
              // A trivial second 'game' whose view is a bare int pair (target, self); no bespoke bots.
              let chase = Bots.chase ((fun (struct (t, _)) -> t), (fun (struct (_, s)) -> s)) Command.MoveWest Command.MoveEast
              let struct (c, _) = chase.Decide (struct (2, 5)) (Rng.ofSeed 0UL)
              Expect.equal c [ Command.MoveWest ] "target below self -> west, reused verbatim"
              let struct (cs, _) = (Bots.sitter: Bot<struct (int * int)>).Decide (struct (2, 5)) (Rng.ofSeed 0UL)
              Expect.isEmpty cs "Bots.sitter reused on the second game" ]

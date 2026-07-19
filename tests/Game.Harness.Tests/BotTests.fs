module Game.Harness.Tests.BotTests

open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Harness
open Game.Harness.Tests.PongSim

// A view whose ball row sits exactly on the paddle centre, so chaseBot takes the tie-break branch that
// actually draws from Rng (ballY < centre / > centre never touch the generator).
let private tieView (top: int) : struct (int * int) = struct (top + P / 2, top)

// The single tie-break command for a given seed.
let private tieCommand (seed: uint64) : Command list =
    let struct (cmds, _) = chaseBot.Decide (tieView 0) (Rng.ofSeed seed)
    cmds

[<Tests>]
let tests =
    testList
        "Bot"
        [ testCase "FR-004 a bot is deterministic in (view, seed)"
          <| fun _ ->
              // At a tie the decision consumes Rng, so this exercises the seed-dependent path, not the
              // trivial move-toward-ball one.
              let struct (c1, r1) = chaseBot.Decide (tieView 0) (Rng.ofSeed 42UL)
              let struct (c2, r2) = chaseBot.Decide (tieView 0) (Rng.ofSeed 42UL)
              Expect.equal c1 c2 "same (view, seed) must decide the same commands"
              Expect.equal r1 r2 "same (view, seed) must advance the generator identically"

          testCase "FR-004 a tie-break draw actually consumes the generator"
          <| fun _ ->
              let inRng = Rng.ofSeed 5UL
              let struct (cmds, outRng) = chaseBot.Decide (tieView 0) inRng
              Expect.notEqual outRng inRng "a tie-break must advance (consume) the generator"
              Expect.equal (List.length cmds) 1 "a tie-break still issues exactly one move"

          testCase "FR-004 the seed is load-bearing: different seeds can decide differently"
          <| fun _ ->
              // If the draw were a constant (generator ignored), every seed would tie-break the same
              // way. Across a spread of seeds the command MUST vary, proving the seed drives the output.
              let distinct = [ 0UL..25UL ] |> List.map tieCommand |> List.distinct
              Expect.isGreaterThan
                  (List.length distinct)
                  1
                  "across seeds the tie-break command must vary — the seed is load-bearing"

          testCase "FR-004 a whole bot run is reproducible from its seed"
          <| fun _ ->
              let a = Driver.runBot playable observeLeft chaseBot 99UL 300 Driver.identityFingerprint
              let b = Driver.runBot playable observeLeft chaseBot 99UL 300 Driver.identityFingerprint
              Expect.equal a.Captured b.Captured "same seed => identical decision sequence"
              Expect.isTrue (Trace.equalFrames a.Trace b.Trace) "same seed => identical trace" ]

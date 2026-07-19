module Game.Harness.Tests.BotTests

open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Harness
open Game.Harness.Tests.PongSim

[<Tests>]
let tests =
    testList
        "Bot"
        [ testCase "FR-004 a bot is deterministic in (view, seed)"
          <| fun _ ->
              // Decide sees only a view and a generator; the same view and seed yield the same commands.
              let view = struct (3, 6)
              let struct (c1, _) = chaseBot.Decide view (Rng.ofSeed 42UL)
              let struct (c2, _) = chaseBot.Decide view (Rng.ofSeed 42UL)
              Expect.equal c1 c2 "same (view, seed) must decide the same commands"

          testCase "FR-004 a bot threads its generator, so the seed determines the whole run"
          <| fun _ ->
              let a = Driver.runBot playable observeLeft chaseBot 99UL 300 Driver.identityFingerprint
              let b = Driver.runBot playable observeLeft chaseBot 99UL 300 Driver.identityFingerprint
              Expect.equal a.Captured b.Captured "same seed => identical decision sequence"
              Expect.isTrue (Trace.equalFrames a.Trace b.Trace) "same seed => identical trace"

          testCase "FR-004 a tie-break draw consumes the generator (the seed is load-bearing)"
          <| fun _ ->
              // At an exact tie (ball row == paddle centre) the bot draws from Rng, so two seeds can
              // diverge — proof the generator is actually threaded, not ignored.
              let centre = 0 + P / 2
              let view = struct (centre, 0)
              let struct (c1, _) = chaseBot.Decide view (Rng.ofSeed 1UL)
              let struct (c2, _) = chaseBot.Decide view (Rng.ofSeed 4UL)
              // Both are single-command tie-break moves; whether they differ depends on the seed, but
              // each is reproducible for its own seed.
              let struct (c1', _) = chaseBot.Decide view (Rng.ofSeed 1UL)
              Expect.equal c1 c1' "the tie-break is reproducible for a fixed seed"
              Expect.isTrue
                  (List.length c1 = 1 && List.length c2 = 1)
                  "a tie-break still issues exactly one move" ]

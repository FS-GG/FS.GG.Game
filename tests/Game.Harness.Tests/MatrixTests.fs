module Game.Harness.Tests.MatrixTests

open Expecto
open FS.GG.Game.Harness
open Game.Harness.Tests.PongSim

// A mixed set of matches: chaser-vs-sitter and sitter-vs-chaser across several seeds, so outcomes
// depend on the match content and not just the seed.
let private matches: Match<struct (int * int)> list =
    [ { Seed = 1UL; A = chaseBot; B = sitterBot }
      { Seed = 2UL; A = sitterBot; B = chaseBot }
      { Seed = 3UL; A = chaseBot; B = sitterBot }
      { Seed = 4UL; A = sitterBot; B = chaseBot } ]

[<Tests>]
let tests =
    testList
        "Matrix"
        [ testCase "FR-005 one outcome per match"
          <| fun _ ->
              let results = Matrix.runMatrix matchSetup outcome matches
              Expect.equal (List.length results) (List.length matches) "exactly one outcome per input match"

          testCase "FR-005 the runner is deterministic"
          <| fun _ ->
              let r1 = Matrix.runMatrix matchSetup outcome matches |> List.map snd
              let r2 = Matrix.runMatrix matchSetup outcome matches |> List.map snd
              Expect.equal r1 r2 "the same matrix must produce the same outcomes"

          testCase "FR-005 outcomes are independent of the order matches are supplied"
          <| fun _ ->
              let forward = Matrix.runMatrix matchSetup outcome matches |> List.map snd
              let reversed = Matrix.runMatrix matchSetup outcome (List.rev matches) |> List.map snd
              Expect.equal (List.rev forward) reversed "reversing the input must reverse the output identically"

          testCase "FR-005 a single match runs the same standalone as in a matrix"
          <| fun _ ->
              let m = List.head matches
              let standalone = Matrix.runMatch matchSetup outcome m
              let inMatrix = Matrix.runMatrix matchSetup outcome matches |> List.head |> snd
              Expect.equal standalone inMatrix "runMatch and runMatrix must agree for the same match"

          testCase "FR-005 winRate is the fraction of outcomes satisfying the predicate"
          <| fun _ ->
              // The helper on a hand-built outcome set (independent of sim dynamics).
              let sample = [ LeftWins; RightWins; LeftWins; Draw ]
              Expect.equal (Matrix.winRate (fun o -> o = LeftWins) sample) 0.5 "2 of 4 are LeftWins"
              Expect.equal (Matrix.winRate (fun o -> o = RightWins) sample) 0.25 "1 of 4 are RightWins"
              Expect.equal (Matrix.winRate (fun _ -> true) []) 0.0 "an empty set is 0.0"

          testCase "FR-005 winRate over the matrix outcomes is stable and in [0,1]"
          <| fun _ ->
              let outcomes = Matrix.runMatrix matchSetup outcome matches |> List.map snd
              let wr = Matrix.winRate (fun o -> o = LeftWins) outcomes
              Expect.isTrue (wr >= 0.0 && wr <= 1.0) "a win rate is a fraction"
              let wr2 = Matrix.winRate (fun o -> o = LeftWins) outcomes
              Expect.equal wr wr2 "win rate is a pure function of the outcomes" ]

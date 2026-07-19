module Game.Harness.Tests.PropertiesTests

open Expecto
open FsCheck
open FS.GG.Game.Core
open FS.GG.Game.Harness
open Game.Harness.Tests.PongSim

// The ball is always on the playfield — an invariant the paddle input cannot break.
let private onPlayfield (w: Pong) : bool =
    w.BallX >= 0 && w.BallX < W && w.BallY >= 0 && w.BallY < H

// The ball's velocity is always unit in each axis.
let private unitVelocity (w: Pong) : bool = abs w.BallDX = 1 && abs w.BallDY = 1

[<Tests>]
let tests =
    testList
        "Properties"
        [ testCase "FR-001/002/003 invariants hold at every step across >=1000 generated runs"
          <| fun _ ->
              match Properties.check playable onPlayfield 1UL with
              | PropertyResult.Held runs -> Expect.isGreaterThanOrEqual runs 1000 "at least 1000 runs generated"
              | PropertyResult.Falsified c -> failtestf "playfield invariant must hold, got %A" c

              match Properties.check playable unitVelocity 7UL with
              | PropertyResult.Held _ -> ()
              | PropertyResult.Falsified c -> failtestf "unit-velocity invariant must hold, got %A" c

          testCase "FR-005 generation is deterministic in the seed"
          <| fun _ ->
              let a = Properties.check playable onPlayfield 12345UL
              let b = Properties.check playable onPlayfield 12345UL
              Expect.equal a b "the same seed yields the identical result"

          testCase "FR-004 a broken invariant is falsified and shrunk to a minimal counter-script"
          <| fun _ ->
              // The paddle CAN reach the top, so "LeftY <> 0" is violated; the minimal counter is four
              // MoveNorth frames (LeftY 4 -> 0).
              match Properties.check playable (fun w -> w.LeftY <> 0) 1UL with
              | PropertyResult.Falsified c ->
                  Expect.equal c.Script (List.replicate 4 [ Command.MoveNorth ]) "shrunk to exactly four MoveNorth frames"
                  Expect.equal c.Step 3 "the violation is at the fourth step (index 3)"
                  // The counter-script really does violate the invariant when replayed.
                  let final = Driver.runCommands playable Driver.identityFingerprint c.Script |> Trace.frames |> List.last
                  Expect.equal final.LeftY 0 "replaying the counter-script drives the paddle to the top"
              | PropertyResult.Held _ -> failtest "a reachable-top invariant must be falsified"

          testCase "FR-003/002 an invariant false at Init is reported at Step -1 with an empty script"
          <| fun _ ->
              match Properties.check playable (fun _ -> false) 1UL with
              | PropertyResult.Falsified c ->
                  Expect.equal c.Step -1 "an Init violation is Step -1"
                  Expect.isEmpty c.Script "no input is needed to reach an Init violation"
              | PropertyResult.Held _ -> failtest "an always-false invariant must be falsified"

          testCase "FR-006 defaultConfig runs >= 1000 and a restricted alphabet is honoured"
          <| fun _ ->
              Expect.isGreaterThanOrEqual Properties.defaultConfig.Runs 1000 "default is at least 1000 runs"
              // Restrict to only MoveSouth: the paddle can never reach the top, so LeftY<>0 always holds.
              let cfg = { Properties.defaultConfig with Moves = Some [ [ Command.MoveSouth ] ]; Runs = 200 }
              match Properties.checkWith cfg playable (fun w -> w.LeftY <> 0) 1UL with
              | PropertyResult.Held _ -> ()
              | PropertyResult.Falsified c -> failtestf "MoveSouth cannot reach the top, got %A" c

          testCase "FR-008 FsCheck pairing: playfield invariant holds across >=1000 FsCheck cases"
          <| fun _ ->
              // FsCheck auto-generates the index list; map each index onto a move to build a script.
              let alphabet = [| []; [ Command.MoveNorth ]; [ Command.MoveSouth ] |]

              let prop (indices: int list) =
                  let script =
                      indices |> List.map (fun i -> alphabet.[((i % alphabet.Length) + alphabet.Length) % alphabet.Length])

                  Driver.runCommands playable Driver.identityFingerprint script
                  |> Trace.frames
                  |> List.forall onPlayfield

              Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop) ]

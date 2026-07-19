module Game.Harness.Tests.ExploreTests

open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Harness
open Game.Harness.Tests.PongSim

// A projected fingerprint for Pong that drops the ever-incrementing Tick (and Rng/score), so the
// reachable state space is finite and the BFS dedups — the granularity control DEC-002/§4.1 calls for.
let private pongFp (w: Pong) : int * int * int * int * int =
    (w.BallX, w.BallY, w.BallDX, w.BallDY, w.LeftY)

// A left-paddle deflection: the step function sets BallX=1, BallDX=1 exactly when the ball is deflected
// off the left paddle. On the single-seat fixture the ball goes right forever and re-serves right, so
// this state is genuinely UNREACHABLE — the WI-7 fixture-degeneracy finding, reproduced automatically.
let private deflection (w: Pong) : bool = w.BallX = 1 && w.BallDX = 1

// A reachable, input-REQUIRING goal: the left paddle driven to the top. From LeftY=4 it takes exactly
// four MoveNorth frames, so the shortest witness has depth 4 and is non-empty.
let private paddleAtTop (w: Pong) : bool = w.LeftY = 0

// A tiny toy Playable with a finite reachable space {0,1,2,3}, to exercise NotFound/Truncated cleanly.
let private toy: Playable<int, string> =
    { Init = 0
      Keymap = Map [ "up", Command.MoveNorth ]
      Apply = fun c w -> match c with | Command.MoveNorth -> min 3 (w + 1) | _ -> w
      Step = fun w _ -> w
      Dt = 1.0 }

[<Tests>]
let tests =
    testList
        "Explore"
        [ testCase "FR-001/002 findScript returns the shortest non-synthetic witness reaching a reachable goal"
          <| fun _ ->
              match Explore.findScript playable pongFp paddleAtTop 20 with
              | FindResult.Found w ->
                  Expect.equal w.Depth 4 "driving the paddle from row 4 to the top takes four MoveNorth frames"
                  // Replaying the witness from Init is InputDriven and reaches a goal-satisfying state.
                  let trace = Driver.runCommands playable Driver.identityFingerprint w.Script
                  Expect.isFalse (Trace.isSynthetic trace) "the witness replay is Origin.InputDriven"
                  Expect.isTrue (paddleAtTop (List.last (Trace.frames trace))) "the final state satisfies the goal"
              | other -> failtestf "expected a witness, got %A" other

          testCase "reachable + findScript reproduce the WI-7 fixture degeneracy automatically"
          <| fun _ ->
              // The ball never reaches the left half, so a left-paddle deflection is unreachable.
              Expect.equal (Explore.findScript playable pongFp deflection 300) FindResult.NotFound "the degenerate fixture cannot deflect off the left paddle"
              let frontier = Explore.reachable playable pongFp 300
              let ballEverOnLeftHalf = frontier.States |> Set.exists (fun (bx, _, _, _, _) -> bx < W / 2)
              Expect.isFalse ballEverOnLeftHalf "no reachable state has the ball on the left half — the degenerate frontier"

          testCase "FR-001 findScript witness is the SHORTEST — Init satisfying goal yields depth 0"
          <| fun _ ->
              match Explore.findScript playable pongFp (fun _ -> true) 10 with
              | FindResult.Found w -> Expect.equal w.Depth 0 "a goal true at Init needs no input"
              | other -> failtestf "expected depth-0 witness, got %A" other

          testCase "FR-003 findScript returns NotFound when the frontier is exhausted within the bound"
          <| fun _ ->
              // The toy's reachable space is {0,1,2,3}; value 5 is unreachable and the frontier exhausts.
              let r = Explore.findScript toy id (fun w -> w = 5) 20
              Expect.equal r FindResult.NotFound "an exhausted frontier with no goal is NotFound, not Truncated"

          testCase "FR-004 findScript fails closed with Truncated when the visited cap is hit"
          <| fun _ ->
              let cfg = { Explore.defaultConfig with MaxVisited = 1 }
              match Explore.findScriptWith cfg toy id (fun w -> w = 99) 20 with
              | FindResult.Truncated n -> Expect.isGreaterThan n 0 "Truncated carries the visited count"
              | other -> failtestf "expected Truncated, got %A" other

          testCase "FR-004 findScript fails closed with Truncated when the depth bound cuts the frontier"
          <| fun _ ->
              // A goal ~22 steps away, searched to depth 3: the frontier extends beyond the bound.
              match Explore.findScript playable pongFp (fun w -> w.ScoreL >= 1 || w.ScoreR >= 1) 3 with
              | FindResult.Truncated _ -> ()
              | other -> failtestf "expected Truncated at the depth bound, got %A" other

          testCase "FR-005/006 reachable returns a bounded, deduplicated frontier"
          <| fun _ ->
              let r = Explore.reachable toy id 20
              Expect.equal r.States (Set.ofList [ 0; 1; 2; 3 ]) "the toy reaches exactly {0,1,2,3}"
              Expect.isFalse r.Truncated "a fully-explored small frontier is not truncated"
              // On Pong, the projected frontier is non-empty and finite.
              let p = Explore.reachable playable pongFp 40
              Expect.isGreaterThan (Set.count p.States) 1 "Pong reaches many distinct projected states"

          testCase "FR-005 reachable sets the truncation flag under a tight visited cap"
          <| fun _ ->
              let cfg = { Explore.defaultConfig with MaxVisited = 2 }
              let r = Explore.reachableWith cfg toy id 20
              Expect.isTrue r.Truncated "hitting the visited cap sets the fail-closed flag"

          testCase "FR-007 findScriptWith honours a restricted move alphabet"
          <| fun _ ->
              // Restrict to only the MoveNorth frame: the paddle can still reach the top.
              let cfg = { Explore.defaultConfig with Moves = Some [ [ Command.MoveNorth ] ] }
              match Explore.findScriptWith cfg playable pongFp paddleAtTop 20 with
              | FindResult.Found w ->
                  Expect.equal w.Depth 4 "four MoveNorth frames reach the top"
                  Expect.isTrue (w.Script |> List.forall (fun f -> f = [ Command.MoveNorth ])) "only the restricted move was explored"
              | other -> failtestf "expected a witness under the restricted alphabet, got %A" other

          testCase "FR-007 defaultMoves is the empty frame plus each single keymap command"
          <| fun _ ->
              let moves = Explore.defaultMoves playable
              Expect.contains moves [] "the empty frame is a move"
              Expect.equal (List.length moves) (1 + (playable.Keymap |> Map.toList |> List.map snd |> List.distinct |> List.length)) "empty + one per distinct command" ]

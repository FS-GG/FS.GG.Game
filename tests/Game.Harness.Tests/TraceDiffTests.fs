module Game.Harness.Tests.TraceDiffTests

open Expecto
open FS.GG.Game.Harness
open Game.Harness.Tests.PongSim

// A compact projection for rendering Pong frames.
let private proj (w: Pong) : string =
    sprintf "%d,%d,%d,%d,%d" w.BallX w.BallY w.BallDX w.BallDY w.LeftY

[<Tests>]
let tests =
    testList
        "TraceDiff"
        [ testCase "FR-006 firstDivergence returns None for equal traces"
          <| fun _ ->
              let a = Driver.runScript playable Driver.identityFingerprint [ [ "w" ]; [ "w" ]; [ "s" ] ]
              let b = Driver.runScript playable Driver.identityFingerprint [ [ "w" ]; [ "w" ]; [ "s" ] ]
              Expect.isNone (Trace.firstDivergence a b) "identical runs must not diverge"

          testCase "FR-006 firstDivergence reports the first content-differing step"
          <| fun _ ->
              // 'w' moves the paddle north, 's' south — the worlds differ from step 0 (LeftY).
              let a = Driver.runScript playable Driver.identityFingerprint [ [ "w" ]; [ "w" ] ]
              let b = Driver.runScript playable Driver.identityFingerprint [ [ "s" ]; [ "w" ] ]
              match Trace.firstDivergence a b with
              | Some(i, _, _) -> Expect.equal i 0 "the paddle diverges at the first step"
              | None -> failtest "expected a divergence"

          testCase "FR-006 firstDivergence on int frames: equal prefix + unequal length is None (docs)"
          <| fun _ ->
              let a = Synthetic.trace id [ 1; 2; 3 ]
              let b = Synthetic.trace id [ 1; 2 ]
              Expect.isNone (Trace.firstDivergence a b) "a strict prefix does not diverge in content"
              let c = Synthetic.trace id [ 1; 9; 3 ]
              Expect.equal (Trace.firstDivergence a c) (Some(1, 2, 9)) "content divergence at index 1"

          testCase "FR-007 render is a stable, step-ordered, deterministic dump"
          <| fun _ ->
              let t = Driver.runScript playable Driver.identityFingerprint [ [ "w" ]; [ "s" ] ]
              let r1 = Trace.render proj t
              let r2 = Trace.render proj t
              Expect.equal r1 r2 "render is deterministic"
              let lines = r1.Split('\n')
              Expect.equal lines.Length 2 "one line per frame"
              Expect.stringStarts lines.[0] "0: " "the first line carries step index 0"
              Expect.stringStarts lines.[1] "1: " "the second line carries step index 1"

          testCase "FR-007 render of the empty trace is the empty string"
          <| fun _ ->
              let t = Driver.runScript playable Driver.identityFingerprint []
              Expect.equal (Trace.render proj t) "" "an empty trace renders as the empty string" ]

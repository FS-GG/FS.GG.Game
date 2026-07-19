module Game.Harness.Tests.LawsTests

open System.IO
open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Harness
open Game.Harness.Tests.PongSim

// A couple of sample key scripts over the Pong keymap (moves, an unbound token, an empty frame).
let private scripts: string list list list =
    [ [ [ "w" ]; [ "w" ]; [ "s" ]; []; [ "z" ] ]
      [ [ "s" ]; [ "s" ]; [ "w" ]; [ "w" ] ] ]

// A Playable whose Step is deliberately non-deterministic: a captured counter leaks into the world,
// so two runs of the same script diverge. This is a test-only stand-in for a clock/ambient-poisoned
// Step (the package itself may reference no such thing — DependencyTests enforces it).
let private poisonedPlayable () : Playable<Pong, string> =
    let counter = ref 0

    { playable with
        Step =
            fun w dt ->
                let n = counter.Value
                counter.Value <- n + 1
                { step w dt with Tick = n } }

[<Tests>]
let tests =
    testList
        "Laws"
        [ testCase "FR-001/002/003/004 Laws.check reports every script law passing on PongSim"
          <| fun _ ->
              // The ≤5-line author experience: one call, one report.
              let report = Laws.check playable scripts
              Expect.isTrue (LawReport.allPassed report) (sprintf "all laws must pass; failures: %A" (LawReport.failures report))
              Expect.equal (List.length report.Results) 4 "four script laws are checked"

          testCase "FR-001/008 the determinism law catches a poisoned Step with a divergence step"
          <| fun _ ->
              let report = Laws.check (poisonedPlayable ()) scripts
              let determinism = report.Results |> List.find (fun r -> r.Law = "determinism")
              Expect.isFalse determinism.Passed "a non-deterministic Step must fail the determinism law"
              Expect.isSome determinism.DivergenceStep "the failure must carry a divergence step index"
              Expect.equal determinism.DivergenceStep (Some 0) "the counter leaks from step 0"

          testCase "FR-004 provenance law holds — every trace the runner builds is InputDriven"
          <| fun _ ->
              let report = Laws.check playable scripts
              let provenance = report.Results |> List.find (fun r -> r.Law = "provenance")
              Expect.isTrue provenance.Passed "every driven trace must be Origin.InputDriven"

          testCase "FR-005 matrixOrderIndependent holds on a Pong two-seat matrix"
          <| fun _ ->
              let matches = [ for s in 1UL..5UL -> { Seed = s; A = chaseBot; B = sitterBot } ]
              let outcome (w: Pong) = w.ScoreL - w.ScoreR
              let result = Laws.matrixOrderIndependent matchSetup outcome matches
              Expect.isTrue result.Passed "permuting the matches must permute the outcomes identically"

          testCase "FR-002 a runBot capture replays byte-identically through runCommands"
          <| fun _ ->
              let run = Driver.runBot playable observeLeft chaseBot 7UL 20 Driver.identityFingerprint
              let replay = Driver.runCommands playable Driver.identityFingerprint run.Captured
              Expect.isTrue (Trace.equalFrames run.Trace replay) "captured bot playthrough must replay identically"

          testCase "FR-009 golden helper: absent golden is captured, matching run passes, drift fails"
          <| fun _ ->
              let dir = Path.Combine(Path.GetTempPath(), sprintf "playtest-golden-%d" (abs (hash scripts)))
              let path = Path.Combine(dir, "pong.trace")
              if File.Exists path then File.Delete path
              let rendered =
                  Driver.runScript playable Driver.identityFingerprint (List.head scripts)
                  |> Trace.render (fun w -> sprintf "%d,%d" w.BallX w.BallY)
              // First run (absent golden) captures it from the green run.
              Expect.equal (GoldenFile.check false path rendered) (Ok()) "absent golden is captured"
              // A matching run passes.
              Expect.equal (GoldenFile.check false path rendered) (Ok()) "a matching rendered trace passes"
              // A drifted run fails with a divergence line index.
              let drifted = rendered + "\n99: drift"
              match GoldenFile.check false path drifted with
              | Ok() -> failtest "drift must not pass"
              | Error(i, _, _) -> Expect.isGreaterThanOrEqual i 0 "drift reports a line index"
              File.Delete path ]

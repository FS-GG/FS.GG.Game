namespace FS.GG.Game.Harness

open FS.GG.Game.Core

type LawResult =
    { Law: string
      Passed: bool
      DivergenceStep: int option
      Detail: string }

type LawReport = { Results: LawResult list }

[<RequireQualifiedAccess>]
module LawReport =

    let allPassed (report: LawReport) : bool = report.Results |> List.forall (fun r -> r.Passed)

    let failures (report: LawReport) : LawResult list =
        report.Results |> List.filter (fun r -> not r.Passed)

[<RequireQualifiedAccess>]
module Laws =

    let check (playable: Playable<'world, 'key>) (sampleScripts: 'key list list list) : LawReport =
        let fp = Driver.identityFingerprint

        // determinism: two runs of each script agree, frame for frame.
        let determinism =
            sampleScripts
            |> List.tryPick (fun script ->
                let a = Driver.runScript playable fp script
                let b = Driver.runScript playable fp script
                Trace.firstDivergence a b |> Option.map (fun (i, _, _) -> i))

        let determinismResult =
            match determinism with
            | None ->
                { Law = "determinism"
                  Passed = true
                  DivergenceStep = None
                  Detail = "every script reproduces equal frames across two runs" }
            | Some i ->
                { Law = "determinism"
                  Passed = false
                  DivergenceStep = Some i
                  Detail = sprintf "two runs of a script diverged at step %d" i }

        // replay: resolving a script's keys and running them through runCommands reproduces runScript.
        let replay =
            sampleScripts
            |> List.tryPick (fun script ->
                let viaScript = Driver.runScript playable fp script
                let resolved = script |> List.map (List.choose (Playable.resolve playable))
                let viaCommands = Driver.runCommands playable fp resolved
                Trace.firstDivergence viaScript viaCommands |> Option.map (fun (i, _, _) -> i))

        let replayResult =
            match replay with
            | None ->
                { Law = "replay"
                  Passed = true
                  DivergenceStep = None
                  Detail = "resolved-command replay reproduces runScript for every script" }
            | Some i ->
                { Law = "replay"
                  Passed = false
                  DivergenceStep = Some i
                  Detail = sprintf "runScript and runCommands replay diverged at step %d" i }

        // fixed-step: an n-frame script yields exactly n recorded frames (one whole step per frame).
        let fixedStep =
            sampleScripts
            |> List.tryPick (fun script ->
                let t = Driver.runScript playable fp script
                let frames = List.length (Trace.frames t)
                let inputs = List.length script
                if frames = inputs then None else Some(inputs, frames))

        let fixedStepResult =
            match fixedStep with
            | None ->
                { Law = "fixed-step"
                  Passed = true
                  DivergenceStep = None
                  Detail = "recorded frame count equals input frame count for every script" }
            | Some(inputs, frames) ->
                { Law = "fixed-step"
                  Passed = false
                  DivergenceStep = None
                  Detail = sprintf "an %d-frame script recorded %d frames" inputs frames }

        // provenance: every trace the runner builds is Origin.InputDriven.
        let provenanceOk =
            sampleScripts
            |> List.forall (fun script -> not (Trace.isSynthetic (Driver.runScript playable fp script)))

        let provenanceResult =
            { Law = "provenance"
              Passed = provenanceOk
              DivergenceStep = None
              Detail =
                if provenanceOk then
                    "every driven trace is Origin.InputDriven"
                else
                    "a driven trace was Origin.Synthetic" }

        { Results = [ determinismResult; replayResult; fixedStepResult; provenanceResult ] }

    let matrixOrderIndependent
        (setup: MatchSetup<'world, 'view>)
        (outcome: 'world -> 'o)
        (matches: Match<'view> list)
        : LawResult =
        // Each match's outcome depends only on its own seed, so running the reversed input must yield
        // the reversed outcome list. Compare the forward outcomes to the reversed run's outcomes
        // re-reversed; equality proves no match's outcome depends on position.
        let forward = Matrix.runMatrix setup outcome matches |> List.map snd
        let reversed = Matrix.runMatrix setup outcome (List.rev matches) |> List.map snd
        let passed = forward = List.rev reversed

        { Law = "matrix-order-independence"
          Passed = passed
          DivergenceStep = None
          Detail =
            if passed then
                "permuting the matches permutes the outcomes identically"
            else
                "a match's outcome changed under a permutation of the match set" }

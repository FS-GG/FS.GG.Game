module Game.Harness.Tests.JourneyTests

open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Harness
open FS.GG.Game.Reference

let productionAdapter = Composition.adapter
let productionScript = Composition.script
let runScript adapter script =
    Journey.runScriptWithIdentity
        Composition.inputIdentity
        Composition.terminalPredicateIdentity
        adapter
        script

// Fixtures for Journey.checkActionCoverage (FS.GG.Game#563). Kept separate from the shared
// production composition above: `checkActionCoverage` reports a gap for a single-inhabitant
// action slot regardless of whether the product's own mapper actually contains a dead arm behind
// it, so a fixture proving that has to control cardinality deliberately rather than borrow it.
module ActionCoverageFixtures =
    type Model = { Booted: bool }
    type Menu =
        | Play
        | Debug

    /// Two distinct menu actions: `Play` is wired, `Debug` is an explicit `Unbound` arm — the
    /// mechanism `Game.Harness` already had before #563. Whether the coverage gap is visible
    /// depends only on whether a committed script ever issues `MenuAction Debug`.
    let twoActionAdapter: ProductionJourney<Model, unit, unit, Menu, unit, string, Model> =
        { RouteId = "test/two-action-menu"
          ScenarioId = "action-coverage"
          TestId = "GP-JOURNEY-COVERAGE-001"
          MaxSteps = 8
          Boot = fun () -> { Booted = true }
          MapEvent =
            fun event _ ->
                match event with
                | JourneyEvent.Start -> JourneyDispatch.Mapped [ "start" ]
                | JourneyEvent.MenuAction Play -> JourneyDispatch.Mapped [ "play" ]
                | JourneyEvent.MenuAction Debug -> JourneyDispatch.Unbound "debug menu"
                | _ -> JourneyDispatch.Mapped []
          Update = fun _ model -> model
          FixedTick = id
          ApplyEffectResult = fun _ model -> model
          IsTerminal = fun _ -> false
          Fingerprint = id
          EncodeEvent = sprintf "%A"
          EncodeFingerprint = sprintf "%A" }

    /// Same two-action vocabulary, but `Debug` is wired too — the "full coverage" control case.
    let fullyWiredAdapter =
        { twoActionAdapter with
            MapEvent =
              fun event model ->
                  match event with
                  | JourneyEvent.MenuAction Debug -> JourneyDispatch.Mapped [ "debug" ]
                  | other -> twoActionAdapter.MapEvent other model }

    /// The exact Rogue3 shape (`2026-08-01-Rogue3-12.md` §4.1): a `'menu` type with ONE
    /// inhabitant. Its sole value is wired (`Mapped`), so `MapEvent` never returns `Unbound` for
    /// anything a script can construct — an empirical unbound-tracking check alone reports this
    /// adapter as clean no matter what runs, which is the blind spot #563 exists to close.
    let unitMenuAdapter: ProductionJourney<Model, unit, unit, unit, unit, string, Model> =
        { RouteId = "test/unit-menu"
          ScenarioId = "action-coverage-degenerate"
          TestId = "GP-JOURNEY-COVERAGE-002"
          MaxSteps = 8
          Boot = fun () -> { Booted = true }
          MapEvent =
            fun event _ ->
                match event with
                | JourneyEvent.Start -> JourneyDispatch.Mapped [ "start" ]
                | JourneyEvent.MenuAction() -> JourneyDispatch.Mapped [ "only-action" ]
                | _ -> JourneyDispatch.Mapped []
          Update = fun _ model -> model
          FixedTick = id
          ApplyEffectResult = fun _ model -> model
          IsTerminal = fun _ -> false
          Fingerprint = id
          EncodeEvent = sprintf "%A"
          EncodeFingerprint = sprintf "%A" }

[<Tests>]
let tests =
    testList
        "ProductionJourney"
        [ testCase "boots the shipped reference composition and proves start, pause, progression, sealed-door refusal, and terminal outcome"
          <| fun _ ->
              let run = runScript productionAdapter productionScript
              let frames = Trace.frames run.Trace

              Expect.equal (Trace.origin run.Trace) Origin.ProductionJourney "journey provenance is runner-issued"
              Expect.equal (JourneyReceipt.schemaVersion run.Receipt) 1 "the serialized contract is versioned"
              Expect.equal
                  (JourneyReceipt.runnerIdentity run.Receipt)
                  "FS.GG.Game.Harness.Journey"
                  "the issuing runner is named"
              Expect.equal (JourneyReceipt.origin run.Receipt) Origin.ProductionJourney "the receipt carries the production origin"
              Expect.equal (JourneyReceipt.inputKind run.Receipt) JourneyInputKind.FixedScript "the fixed script route is explicit"
              Expect.equal
                  (JourneyReceipt.inputIdentity run.Receipt)
                  Composition.inputIdentity
                  "the fixed script definition is identified"
              Expect.equal
                  (JourneyReceipt.terminalPredicateIdentity run.Receipt)
                  Composition.terminalPredicateIdentity
                  "the terminal predicate is identified"
              Expect.isFalse
                  (System.String.IsNullOrWhiteSpace(JourneyReceipt.runnerVersion run.Receipt))
                  "the issuing runner version is present"
              Expect.notEqual
                  (JourneyReceipt.initialFingerprintDigest run.Receipt)
                  (JourneyReceipt.terminalFingerprintDigest run.Receipt)
                  "boot and terminal fingerprints identify different lifecycle states"
              Expect.isTrue
                  (JourneyReceipt.terminalPredicateReached run.Receipt)
                  "the receipt records the terminal predicate outcome"
              Expect.equal (JourneyReceipt.result run.Receipt) JourneyResult.Passed "the bounded journey reaches its terminal predicate"
              Expect.equal frames.[0].Screen Composition.Screen.Playing "the real start action leaves the boot menu"
              Expect.equal frames.[1].Screen Composition.Screen.Paused "pause is routed through production mapping"
              Expect.equal frames.[2].Screen Composition.Screen.Playing "resume is routed through production mapping"
              Expect.equal frames.[3].Aim (12, 7) "pointer input traverses the product mapper and update"
              Expect.equal frames.[6].Room Composition.Room.Vault "interaction changes the active room"
              Expect.isTrue frames.[6].DestinationActive "the destination is revealed and activated"
              Expect.equal frames.[6].X 0 "the player is repositioned in the destination"
              Expect.isTrue frames.[6].CameraTransition "room entry begins the camera transition"
              Expect.isFalse frames.[7].CameraTransition "a fixed production tick completes the camera transition"
              Expect.equal frames.[8].Room Composition.Room.Vault "the sealed exit refuses interaction before room clear"
              Expect.equal run.Final.Screen Composition.Screen.Won "the clear-result seam unlocks a terminal outcome"

          testCase "a captured seeded policy replays byte-identically through the shipped production-event route"
          <| fun _ ->
              let mutable index = 0
              let policy =
                  { DecideEvents =
                      fun _ rng ->
                          let event = productionScript.[index]
                          index <- index + 1
                          struct ([ event ], rng) }

              let generated =
                  Journey.runPolicyWithIdentity
                      "boot-to-vault-exit/policy-v1"
                      Composition.terminalPredicateIdentity
                      productionAdapter
                      policy
                      73UL
              let replay = runScript productionAdapter generated.Captured
              Expect.equal
                  (JourneyReceipt.inputKind generated.Receipt)
                  JourneyInputKind.SeededPolicy
                  "a seeded policy is distinct from its fixed-script replay"
              Expect.equal
                  (JourneyReceipt.inputIdentity generated.Receipt)
                  "boot-to-vault-exit/policy-v1"
                  "the seeded policy definition is identified"
              Expect.notEqual
                  (JourneyReceipt.inputDigest generated.Receipt)
                  (JourneyReceipt.inputDigest replay.Receipt)
                  "the seeded-policy definition binding cannot collapse to a caller-authored fixed script"
              Expect.isTrue (Trace.equalFrames generated.Trace replay.Trace) "captured events replay byte-identically"
              Expect.equal
                  (JourneyReceipt.scriptDigest generated.Receipt)
                  (JourneyReceipt.scriptDigest replay.Receipt)
                  "the captured-input digest is stable"

          testCase "a helper can work while an unbound displayed interaction fails production reachability"
          <| fun _ ->
              let helper (model: Composition.Model) =
                  { model with
                      Room = Composition.Room.Vault
                      DestinationActive = true
                      X = 0 }

              let helperState = productionAdapter.Boot() |> helper
              Expect.equal helperState.Room Composition.Room.Vault "the direct helper remains green"

              let productionMap = productionAdapter.MapEvent
              let broken =
                  { productionAdapter with
                      MapEvent =
                        fun event model ->
                            match event with
                            | JourneyEvent.Interact -> JourneyDispatch.Unbound "Interact"
                            | other -> productionMap other model }
              let run = Journey.runScript broken productionScript

              match JourneyReceipt.result run.Receipt with
              | JourneyResult.Failed reason ->
                  Expect.stringContains reason "displayed action 'Interact' is unbound" "failure names the production wiring gap"
              | JourneyResult.Passed -> failtest "an unbound interaction must not mint a passing journey receipt"

          testCase "checkActionCoverage flags an Unbound arm no committed script ever issues"
          <| fun _ ->
              let vocabulary =
                  [ JourneyEvent.MenuAction ActionCoverageFixtures.Play
                    JourneyEvent.MenuAction ActionCoverageFixtures.Debug ]

              let committedScripts =
                  [ [ JourneyEvent.Start; JourneyEvent.MenuAction ActionCoverageFixtures.Play ] ]

              let report =
                  Journey.checkActionCoverage
                      ActionCoverageFixtures.twoActionAdapter
                      vocabulary
                      committedScripts

              Expect.isFalse (ActionCoverageReport.isClean report) "an unreached Unbound arm is a coverage gap"
              Expect.contains
                  report.Gaps
                  (ActionCoverageGap.UnexercisedUnbound("debug menu", "MenuAction Debug"))
                  "the gap names the unbound action and the unreached event"

          testCase "checkActionCoverage stays clean when every declared action is wired and exercised"
          <| fun _ ->
              let vocabulary =
                  [ JourneyEvent.MenuAction ActionCoverageFixtures.Play
                    JourneyEvent.MenuAction ActionCoverageFixtures.Debug ]

              let committedScripts =
                  [ [ JourneyEvent.Start; JourneyEvent.MenuAction ActionCoverageFixtures.Play ]
                    [ JourneyEvent.Start; JourneyEvent.MenuAction ActionCoverageFixtures.Debug ] ]

              let report =
                  Journey.checkActionCoverage
                      ActionCoverageFixtures.fullyWiredAdapter
                      vocabulary
                      committedScripts

              Expect.isTrue (ActionCoverageReport.isClean report) "full coverage over a rich-enough vocabulary stays green"

          testCase "checkActionCoverage flags a single-inhabitant vocabulary slot that empirical Unbound-tracking alone cannot see"
          <| fun _ ->
              // This is the exact Rogue3 shape: 'menu = unit means MapEvent's sole reachable arm is
              // Mapped, so an empirical sweep over what MapEvent RETURNS reports nothing wrong here —
              // demonstrating why the structural DegenerateVocabulary check exists as an independent
              // signal, not a restatement of UnexercisedUnbound.
              let vocabulary = [ JourneyEvent.MenuAction() ]
              let committedScripts = [ [ JourneyEvent.Start; JourneyEvent.MenuAction() ] ]

              let report =
                  Journey.checkActionCoverage
                      ActionCoverageFixtures.unitMenuAdapter
                      vocabulary
                      committedScripts

              Expect.isFalse (ActionCoverageReport.isClean report) "a single-inhabitant action slot is a coverage gap on its own"
              Expect.isEmpty
                  (report.Gaps
                   |> List.choose (function
                       | ActionCoverageGap.UnexercisedUnbound _ as gap -> Some gap
                       | _ -> None))
                  "no Unbound arm was ever returned for the single inhabitant, so empirical tracking alone sees nothing"
              Expect.contains
                  report.Gaps
                  (ActionCoverageGap.DegenerateVocabulary("menu", 1))
                  "the structural check names the degenerate slot and its inhabitant count"

          testCase "exhaustion fails with final-fingerprint and captured-input digests instead of hanging"
          <| fun _ ->
              let run = runScript productionAdapter [ JourneyEvent.Start; JourneyEvent.FixedTick ]

              match JourneyReceipt.result run.Receipt with
              | JourneyResult.Failed reason ->
                  Expect.stringContains reason "terminal predicate not reached" "exhaustion is explicit"
                  Expect.stringContains reason "final fingerprint sha256:" "the final state is identified"
                  Expect.stringContains reason "captured-input sha256:" "the input capture is identified"
              | JourneyResult.Passed -> failtest "a non-terminal prefix must fail"

          testCase "production receipt is opaque and has no public constructor"
          <| fun _ ->
              Expect.isEmpty
                  (typeof<JourneyReceipt>.GetConstructors())
                  "callers cannot construct a receipt or serialize one into accepted provenance"

          testCase "length-framed script digests distinguish embedded newlines from event boundaries"
          <| fun _ ->
              let one =
                  runScript
                      { productionAdapter with EncodeEvent = fun _ -> "a\nb" }
                      [ JourneyEvent.Start ]
              let two =
                  runScript
                      { productionAdapter with
                          EncodeEvent =
                              function
                              | JourneyEvent.Start -> "a"
                              | _ -> "b" }
                      [ JourneyEvent.Start; JourneyEvent.Pause ]

              Expect.notEqual
                  (JourneyReceipt.scriptDigest one.Receipt)
                  (JourneyReceipt.scriptDigest two.Receipt)
                  "one encoded value containing a newline is not the same capture as two values"

          testCase "byte-divergent replay fingerprints produce a different trace binding"
          <| fun _ ->
              let original = runScript productionAdapter productionScript
              let divergent =
                  runScript
                      { productionAdapter with
                          EncodeFingerprint = fun fingerprint -> "divergent:" + sprintf "%A" fingerprint }
                      productionScript

              Expect.notEqual
                  (JourneyReceipt.traceDigest original.Receipt)
                  (JourneyReceipt.traceDigest divergent.Receipt)
                  "serialized trace provenance is bound to the exact encoded replay bytes"

          testCase "a terminal-at-boot adapter cannot mint a passing empty-script receipt"
          <| fun _ ->
              let terminalBoot =
                  { productionAdapter with
                      Boot =
                        fun () ->
                            { productionAdapter.Boot() with
                                Screen = Composition.Screen.Won } }
              let run = Journey.runScript terminalBoot []

              match JourneyReceipt.result run.Receipt with
              | JourneyResult.Failed reason ->
                  Expect.stringContains reason "already satisfied at boot" "the non-journey is refused explicitly"
                  Expect.equal (JourneyReceipt.steps run.Receipt) 0 "no production event was executed"
              | JourneyResult.Passed -> failtest "terminal-at-boot must not mint a passing journey receipt"

          testCase "schema-v1 issuance refuses empty route, scenario, input, test, and terminal identities"
          <| fun _ ->
              let mustThrow name action =
                  Expect.throws action (sprintf "empty %s identity must be refused before execution" name)

              mustThrow "route" (fun () ->
                  Journey.runScriptWithIdentity
                      Composition.inputIdentity
                      Composition.terminalPredicateIdentity
                      { productionAdapter with RouteId = "" }
                      productionScript
                  |> ignore)
              mustThrow "scenario" (fun () ->
                  Journey.runScriptWithIdentity
                      Composition.inputIdentity
                      Composition.terminalPredicateIdentity
                      { productionAdapter with ScenarioId = " " }
                      productionScript
                  |> ignore)
              mustThrow "test" (fun () ->
                  Journey.runScriptWithIdentity
                      Composition.inputIdentity
                      Composition.terminalPredicateIdentity
                      { productionAdapter with TestId = "" }
                      productionScript
                  |> ignore)
              mustThrow "input" (fun () ->
                  Journey.runScriptWithIdentity
                      ""
                      Composition.terminalPredicateIdentity
                      productionAdapter
                      productionScript
                  |> ignore)
              mustThrow "terminal" (fun () ->
                  Journey.runScriptWithIdentity
                      Composition.inputIdentity
                      ""
                      productionAdapter
                      productionScript
                  |> ignore)

          testCase "runScriptWithIdentity's composition-authority diagnostic names the likely cause, not only the mismatch (FS.GG.Game#565)"
          <| fun _ ->
              // productionAdapter's six composition functions are all authored in FS.GG.Game.Reference
              // (Composition.adapter, a separate assembly from this test project). Overriding just one
              // field with a closure written HERE reproduces the exact shape Rogue3 hit: a
              // caller-authored function crossing into an otherwise product-owned composition, which
              // `compositionAuthority` must reject by assembly identity.
              let crossed =
                  { productionAdapter with
                      ApplyEffectResult = fun _ model -> model }

              let thrown =
                  try
                      Journey.runScriptWithIdentity
                          Composition.inputIdentity
                          Composition.terminalPredicateIdentity
                          crossed
                          productionScript
                      |> ignore
                      None
                  with :? System.ArgumentException as ex ->
                      Some ex.Message

              match thrown with
              | None ->
                  failtest "a composition mixing product- and caller-authored functions must be rejected"
              | Some message ->
                  Expect.stringContains
                      message
                      "do not share one assembly authority"
                      "the mismatch is still named by function and assembly identity"
                  Expect.stringContains
                      message
                      "caller-constructed closure crossing into the composition"
                      "the diagnostic also names the likely cause, not only the raw mismatch" ]

module Game.Harness.Tests.JourneyTests

open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Harness
open FS.GG.Game.Reference

let productionAdapter = Composition.adapter
let productionScript = Composition.script

[<Tests>]
let tests =
    testList
        "ProductionJourney"
        [ testCase "boots the shipped reference composition and proves start, pause, progression, sealed-door refusal, and terminal outcome"
          <| fun _ ->
              let run = Journey.runScript productionAdapter productionScript
              let frames = Trace.frames run.Trace

              Expect.equal (Trace.origin run.Trace) Origin.ProductionJourney "journey provenance is runner-issued"
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

              let generated = Journey.runPolicy productionAdapter policy 73UL
              let replay = Journey.runScript productionAdapter generated.Captured
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

          testCase "exhaustion fails with final-fingerprint and captured-input digests instead of hanging"
          <| fun _ ->
              let run = Journey.runScript productionAdapter [ JourneyEvent.Start; JourneyEvent.FixedTick ]

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
                  Journey.runScript
                      { productionAdapter with EncodeEvent = fun _ -> "a\nb" }
                      [ JourneyEvent.Start ]
              let two =
                  Journey.runScript
                      { productionAdapter with
                          EncodeEvent =
                              function
                              | JourneyEvent.Start -> "a"
                              | _ -> "b" }
                      [ JourneyEvent.Start; JourneyEvent.Pause ]

              Expect.notEqual
                  (JourneyReceipt.scriptDigest one.Receipt)
                  (JourneyReceipt.scriptDigest two.Receipt)
                  "one encoded value containing a newline is not the same capture as two values" ]

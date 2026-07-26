module Game.Harness.Tests.JourneyTests

open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Harness

type Screen =
    | Menu
    | Playing
    | Paused
    | Won

type Room =
    | Atrium
    | Vault
    | Exit

type JourneyModel =
    { Screen: Screen
      Room: Room
      X: int
      DestinationActive: bool
      CameraTransition: bool
      VaultClear: bool
      Tick: int }

type Key = Right
type Pointer = Aim of int * int
type MenuAction = NewGame
type EffectResult = VaultEnemiesDefeated

type Message =
    | StartGame
    | MoveRight
    | TryDoor
    | PauseGame
    | ResumeGame
    | AimAt of int * int

let private boot () =
    { Screen = Menu
      Room = Atrium
      X = 0
      DestinationActive = false
      CameraTransition = false
      VaultClear = false
      Tick = 0 }

let private traverseOpenDoor model =
    match model.Room, model.X, model.VaultClear with
    | Atrium, x, _ when x >= 2 ->
        { model with
            Room = Vault
            X = 0
            DestinationActive = true
            CameraTransition = true }
    | Vault, _, true ->
        { model with
            Room = Exit
            X = 0
            DestinationActive = true
            CameraTransition = true }
    | _ -> model

let private update message model =
    match message with
    | StartGame when model.Screen = Menu -> { model with Screen = Playing }
    | MoveRight when model.Screen = Playing -> { model with X = model.X + 1 }
    | TryDoor when model.Screen = Playing -> traverseOpenDoor model
    | PauseGame when model.Screen = Playing -> { model with Screen = Paused }
    | ResumeGame when model.Screen = Paused -> { model with Screen = Playing }
    | AimAt _ -> model
    | _ -> model

let private mapEvent wired event _model =
    match event with
    | JourneyEvent.Start
    | JourneyEvent.MenuAction NewGame -> JourneyDispatch.Mapped [ StartGame ]
    | JourneyEvent.KeyInput(Right, true) -> JourneyDispatch.Mapped [ MoveRight ]
    | JourneyEvent.KeyInput(Right, false) -> JourneyDispatch.Mapped []
    | JourneyEvent.PointerInput(Aim(x, y)) -> JourneyDispatch.Mapped [ AimAt(x, y) ]
    | JourneyEvent.Interact when wired -> JourneyDispatch.Mapped [ TryDoor ]
    | JourneyEvent.Interact -> JourneyDispatch.Unbound "Interact"
    | JourneyEvent.Pause -> JourneyDispatch.Mapped [ PauseGame ]
    | JourneyEvent.Resume -> JourneyDispatch.Mapped [ ResumeGame ]
    | JourneyEvent.FixedTick
    | JourneyEvent.EffectResult _ -> JourneyDispatch.Mapped []

let productionAdapter wired =
    { RouteId = "reference-game/production-composition"
      ScenarioId = "boot-to-vault-exit"
      TestId = "GP-JOURNEY-001"
      MaxSteps = 32
      Boot = boot
      MapEvent = mapEvent wired
      Update = update
      FixedTick =
        fun model ->
            let next =
                { model with
                    CameraTransition = false
                    Tick = model.Tick + 1 }

            if next.Room = Exit then { next with Screen = Won } else next
      ApplyEffectResult =
        fun effect model ->
            match effect with
            | VaultEnemiesDefeated -> { model with VaultClear = true }
      IsTerminal = fun model -> model.Screen = Won
      Fingerprint = id
      EncodeEvent = sprintf "%A"
      EncodeFingerprint = sprintf "%A" }

let productionScript =
    [ JourneyEvent.Start
      JourneyEvent.Pause
      JourneyEvent.Resume
      JourneyEvent.PointerInput(Aim(12, 7))
      JourneyEvent.KeyInput(Right, true)
      JourneyEvent.KeyInput(Right, true)
      JourneyEvent.Interact
      JourneyEvent.FixedTick
      JourneyEvent.Interact // sealed: remains in Vault
      JourneyEvent.EffectResult VaultEnemiesDefeated
      JourneyEvent.Interact
      JourneyEvent.FixedTick ]

[<Tests>]
let tests =
    testList
        "ProductionJourney"
        [ testCase "boots the production composition and proves start, pause, progression, sealed-door refusal, and terminal outcome"
          <| fun _ ->
              let run = Journey.runScript (productionAdapter true) productionScript
              let frames = Trace.frames run.Trace

              Expect.equal (Trace.origin run.Trace) Origin.ProductionJourney "journey provenance is runner-issued"
              Expect.equal (JourneyReceipt.result run.Receipt) JourneyResult.Passed "the bounded journey reaches its terminal predicate"
              Expect.equal frames.[0].Screen Playing "the real start action leaves the boot menu"
              Expect.equal frames.[1].Screen Paused "pause is routed through production mapping"
              Expect.equal frames.[2].Screen Playing "resume is routed through production mapping"
              Expect.equal frames.[6].Room Vault "interaction changes the active room"
              Expect.isTrue frames.[6].DestinationActive "the destination is revealed and activated"
              Expect.equal frames.[6].X 0 "the player is repositioned in the destination"
              Expect.isTrue frames.[6].CameraTransition "room entry begins the camera transition"
              Expect.isFalse frames.[7].CameraTransition "a fixed production tick completes the camera transition"
              Expect.equal frames.[8].Room Vault "the sealed exit refuses interaction before room clear"
              Expect.equal run.Final.Screen Won "the clear-result seam unlocks progression to a terminal outcome"

          testCase "a captured seeded policy replays byte-identically through the production-event route"
          <| fun _ ->
              let mutable index = 0
              let policy =
                  { DecideEvents =
                      fun _ rng ->
                          let event = productionScript.[index]
                          index <- index + 1
                          struct ([ event ], rng) }

              let generated = Journey.runPolicy (productionAdapter true) policy 73UL
              let replay = Journey.runScript (productionAdapter true) generated.Captured
              Expect.isTrue (Trace.equalFrames generated.Trace replay.Trace) "captured events replay byte-identically"
              Expect.equal
                  (JourneyReceipt.scriptDigest generated.Receipt)
                  (JourneyReceipt.scriptDigest replay.Receipt)
                  "the captured-input digest is stable"

          testCase "a helper can work while an unbound displayed interaction fails production reachability"
          <| fun _ ->
              let helperState = { boot () with Screen = Playing; X = 2 } |> traverseOpenDoor
              Expect.equal helperState.Room Vault "the direct helper remains green"

              let run = Journey.runScript (productionAdapter false) productionScript

              match JourneyReceipt.result run.Receipt with
              | JourneyResult.Failed reason ->
                  Expect.stringContains reason "displayed action 'Interact' is unbound" "failure names the production wiring gap"
              | JourneyResult.Passed -> failtest "an unbound interaction must not mint a passing journey receipt"

          testCase "exhaustion fails with final-fingerprint and captured-input digests instead of hanging"
          <| fun _ ->
              let run = Journey.runScript (productionAdapter true) [ JourneyEvent.Start; JourneyEvent.FixedTick ]

              match JourneyReceipt.result run.Receipt with
              | JourneyResult.Failed reason ->
                  Expect.stringContains reason "terminal predicate not reached" "exhaustion is explicit"
                  Expect.stringContains reason "final fingerprint sha256:" "the final state is identified"
                  Expect.stringContains reason "captured-input sha256:" "the input capture is identified"
              | JourneyResult.Passed -> failtest "a non-terminal prefix must fail"

          testCase "receipt JSON binds route, scenario, test, script, trace, result, and maximum"
          <| fun _ ->
              let json = Journey.runScript (productionAdapter true) productionScript |> fun run -> JourneyReceipt.render run.Receipt
              for field in [ "\"kind\":\"production-journey\""; "\"routeId\":"; "\"scenarioId\":"; "\"testId\":"; "\"scriptDigest\":\"sha256:"; "\"traceDigest\":\"sha256:"; "\"integrity\":\"sha256:" ] do
                  Expect.stringContains json field $"receipt includes {field}" ]

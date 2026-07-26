namespace FS.GG.Game.Reference

open FS.GG.Game.Harness

module Composition =
    type Screen =
        | Menu
        | Playing
        | Paused
        | Won

    type Room =
        | Atrium
        | Vault
        | Exit

    type Model =
        { Screen: Screen
          Room: Room
          X: int
          DestinationActive: bool
          CameraTransition: bool
          VaultClear: bool
          Aim: int * int
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

    let boot () =
        { Screen = Menu
          Room = Atrium
          X = 0
          DestinationActive = false
          CameraTransition = false
          VaultClear = false
          Aim = 0, 0
          Tick = 0 }

    let traverseDoor model =
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

    let update message model =
        match message with
        | StartGame when model.Screen = Menu -> { model with Screen = Playing }
        | MoveRight when model.Screen = Playing -> { model with X = model.X + 1 }
        | TryDoor when model.Screen = Playing -> traverseDoor model
        | PauseGame when model.Screen = Playing -> { model with Screen = Paused }
        | ResumeGame when model.Screen = Paused -> { model with Screen = Playing }
        | AimAt(x, y) -> { model with Aim = x, y }
        | _ -> model

    let mapEvent event _model =
        match event with
        | JourneyEvent.Start
        | JourneyEvent.MenuAction NewGame -> JourneyDispatch.Mapped [ StartGame ]
        | JourneyEvent.KeyInput(Right, true) -> JourneyDispatch.Mapped [ MoveRight ]
        | JourneyEvent.KeyInput(Right, false) -> JourneyDispatch.Mapped []
        | JourneyEvent.PointerInput(Aim(x, y)) -> JourneyDispatch.Mapped [ AimAt(x, y) ]
        | JourneyEvent.Interact -> JourneyDispatch.Mapped [ TryDoor ]
        | JourneyEvent.Pause -> JourneyDispatch.Mapped [ PauseGame ]
        | JourneyEvent.Resume -> JourneyDispatch.Mapped [ ResumeGame ]
        | JourneyEvent.FixedTick
        | JourneyEvent.EffectResult _ -> JourneyDispatch.Mapped []

    let fixedTick model =
        let next =
            { model with
                CameraTransition = false
                Tick = model.Tick + 1 }

        if next.Room = Exit then { next with Screen = Won } else next

    let applyEffectResult effect model =
        match effect with
        | VaultEnemiesDefeated -> { model with VaultClear = true }

    let adapter =
        { RouteId = "FS.GG.Game.Reference/Composition"
          ScenarioId = "boot-to-vault-exit"
          TestId = "GP-JOURNEY-001"
          MaxSteps = 32
          Boot = boot
          MapEvent = mapEvent
          Update = update
          FixedTick = fixedTick
          ApplyEffectResult = applyEffectResult
          IsTerminal = fun model -> model.Screen = Won
          Fingerprint = id
          EncodeEvent = sprintf "%A"
          EncodeFingerprint = sprintf "%A" }

    let script: JourneyEvent<Key, Pointer, MenuAction, EffectResult> list =
        [ JourneyEvent.Start
          JourneyEvent.Pause
          JourneyEvent.Resume
          JourneyEvent.PointerInput(Aim(12, 7))
          JourneyEvent.KeyInput(Right, true)
          JourneyEvent.KeyInput(Right, true)
          JourneyEvent.Interact
          JourneyEvent.FixedTick
          JourneyEvent.Interact
          JourneyEvent.EffectResult VaultEnemiesDefeated
          JourneyEvent.Interact
          JourneyEvent.FixedTick ]

    let inputIdentity = "boot-to-vault-exit/fixed-script-v1"
    let terminalPredicateIdentity = "screen-won-v1"

[<Sealed>]
type ProductionJourneyProof() =
    interface IProductionJourneyProofV1 with
        member _.RouteId = Composition.adapter.RouteId
        member _.ScenarioId = Composition.adapter.ScenarioId
        member _.InputIdentity = Composition.inputIdentity
        member _.TerminalPredicateIdentity = Composition.terminalPredicateIdentity
        member _.TestId = Composition.adapter.TestId
        member _.Run() =
            (Journey.runScriptWithIdentity
                Composition.inputIdentity
                Composition.terminalPredicateIdentity
                Composition.adapter
                Composition.script)
                .Receipt

module Program =
    [<EntryPoint>]
    let main _ = 0

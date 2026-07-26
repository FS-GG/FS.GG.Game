namespace FS.GG.Game.Harness

[<RequireQualifiedAccess>]
module ReferenceJourney =
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
          Tick: int }

    type Key = Right
    type Pointer = Aim of int * int
    type MenuAction = NewGame
    type EffectResult = VaultEnemiesDefeated

    let private boot () =
        { Screen = Menu
          Room = Atrium
          X = 0
          DestinationActive = false
          CameraTransition = false
          VaultClear = false
          Tick = 0 }

    let private traverseDoor model =
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
        | "start" when model.Screen = Menu -> { model with Screen = Playing }
        | "right" when model.Screen = Playing -> { model with X = model.X + 1 }
        | "interact" when model.Screen = Playing -> traverseDoor model
        | "pause" when model.Screen = Playing -> { model with Screen = Paused }
        | "resume" when model.Screen = Paused -> { model with Screen = Playing }
        | _ -> model

    let private mapEvent event _model =
        match event with
        | JourneyEvent.Start
        | JourneyEvent.MenuAction NewGame -> JourneyDispatch.Mapped [ "start" ]
        | JourneyEvent.KeyInput(Right, true) -> JourneyDispatch.Mapped [ "right" ]
        | JourneyEvent.KeyInput(Right, false)
        | JourneyEvent.PointerInput(Aim _) -> JourneyDispatch.Mapped []
        | JourneyEvent.Interact -> JourneyDispatch.Mapped [ "interact" ]
        | JourneyEvent.Pause -> JourneyDispatch.Mapped [ "pause" ]
        | JourneyEvent.Resume -> JourneyDispatch.Mapped [ "resume" ]
        | JourneyEvent.FixedTick
        | JourneyEvent.EffectResult _ -> JourneyDispatch.Mapped []

    let adapter =
        { RouteId = "reference-game/production-composition"
          ScenarioId = "boot-to-vault-exit"
          TestId = "GP-JOURNEY-001"
          MaxSteps = 32
          Boot = boot
          MapEvent = mapEvent
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

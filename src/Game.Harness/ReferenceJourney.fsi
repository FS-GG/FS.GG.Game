namespace FS.GG.Game.Harness

/// Shipped reference-game composition used to prove the production-journey boundary itself. Its
/// boot, raw-event map, update, fixed tick, and deterministic effect-result functions are production
/// package code; tests import this composition instead of recreating it.
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

    val adapter:
        ProductionJourney<Model, Key, Pointer, MenuAction, EffectResult, string, Model>

    val script: JourneyEvent<Key, Pointer, MenuAction, EffectResult> list

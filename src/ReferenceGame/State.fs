namespace FS.GG.Game.Reference

/// The reference game's state types, free of the harness so the browser renderers (compiled by Fable) can share them
/// with the .NET composition. `Composition` re-exports each one under its existing name.
module ReferenceState =
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
        {
            Screen: Screen
            Room: Room
            X: int
            DestinationActive: bool
            CameraTransition: bool
            VaultClear: bool
            Aim: int * int
            Tick: int
            /// The selected object (`ReferenceRenderers.objects`), chosen through any backend's input host.
            Selected: string option
        }

module Wasm.Lifecycle.Correspondence.CorrespondenceTypes

open FS.GG.Wasm.Browser

type OriginalInput =
    { Name: string; Request: RequestProjection; Worker: string; Phase: string; Observed: int64 }

type TerminalExpectation =
    { Request: uint64; Reason: string; Outcome: string; Phase: string; Dispatched: bool }

type ModelStep =
    {
        State: HostProjection
        Input: OriginalInput
        Connected: HostProjection
        ConnectedEffects: EffectProjection list
        ConnectedTerminals: TerminalExpectation list
        ConnectedCallbacks: OriginalInput list
        ConnectedBeforeEffects: HostProjection list
        Effects: EffectProjection list
        Terminals: TerminalExpectation list
    }

type ModelTrace =
    {
        Name: string
        SourceSha256: string
        Steps: ModelStep list
    }

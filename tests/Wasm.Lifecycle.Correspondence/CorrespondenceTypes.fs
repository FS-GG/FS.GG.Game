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
        Effects: EffectProjection list
        Terminals: TerminalExpectation list
    }

type ModelTrace =
    {
        Name: string
        SourceSha256: string
        Steps: ModelStep list
    }

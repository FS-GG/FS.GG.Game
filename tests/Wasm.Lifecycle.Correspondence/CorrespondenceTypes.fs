module Wasm.Lifecycle.Correspondence.CorrespondenceTypes

open FS.GG.Wasm.Browser

type ModelStep =
    {
        State: HostProjection
        Effects: EffectProjection list
    }

type ModelTrace =
    {
        Name: string
        SourceSha256: string
        Steps: ModelStep list
    }

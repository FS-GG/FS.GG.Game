module Wasm.Supervisor.Compatibility.CompatibleTypes
open FS.GG.Wasm.Browser
open Wasm.Lifecycle.Correspondence.CorrespondenceTypes

type CompatibleStep = { Model: ModelStep; Limits: RequestLimits; Pure: CompatibilityProjection; Connected: CompatibilityProjection; Commands: (uint64 * RequestLimits) list }
type CompatibleTrace = { Name: string; SourceSha256: string; Steps: CompatibleStep list }

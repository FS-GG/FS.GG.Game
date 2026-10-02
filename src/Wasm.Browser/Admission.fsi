namespace FS.GG.Wasm.Browser

open FS.GG.Wasm.Contracts

type AdmissionIssue =
    | ArtifactTooLarge of maximum: int * actual: int
    | MalformedModule of offset: int * detail: string
    | UnsupportedFeature of detail: string
    | ImportForbidden of moduleName: string * itemName: string
    | StartFunctionForbidden
    | MissingExport of name: string
    | UnexpectedExport of name: string
    | ExportKindMismatch of name: string
    | ExportSignatureMismatch of name: string
    | FunctionCodeCountMismatch
    | MemoryDeclarationInvalid of detail: string
    | TableDeclarationInvalid of detail: string

type AdmissionEvidence =
    { Path: CompatibilityPath
      ArtifactBytes: int
      InitialMemoryPages: uint32
      MaximumMemoryPages: uint32
      ExportNames: string list
      FunctionCount: int }

[<RequireQualifiedAccess>]
module Admission =
    /// Performs bounded structural admission before WebAssembly compilation or
    /// instantiation. Inventory-only compatibility paths always refuse.
    val inspect: ValidatedConfiguration -> byte array -> Result<AdmissionEvidence, AdmissionIssue list>

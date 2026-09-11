namespace FS.GG.Game.Core

type SessionCompatibility =
    { ContractVersion: int
      EngineId: string
      EngineVersion: string
      ProfileId: string
      SchemaId: string
      SchemaVersion: int }

[<RequireQualifiedAccess>]
type SessionCompatibilityIssue =
    | ContractVersion of expected: int * actual: int
    | EngineId of expected: string * actual: string
    | EngineVersion of expected: string * actual: string
    | ProfileId of expected: string * actual: string
    | SchemaId of expected: string * actual: string
    | SchemaVersion of expected: int * actual: int

[<RequireQualifiedAccess>]
type SessionContractIssue =
    | InvalidContractVersion of int
    | MissingEngineId
    | MissingEngineVersion
    | MissingProfileId
    | MissingSchemaId
    | InvalidSchemaVersion of int
    | MissingSessionId
    | MissingInputId

[<RequireQualifiedAccess>]
type SessionSupport =
    | ContractEnvelopeOnly

type SessionInitialization<'configuration> =
    { SessionId: string
      Compatibility: SessionCompatibility
      Configuration: 'configuration }

type SessionInput<'input> =
    { SessionId: string
      InputId: string
      Sequence: uint64
      Value: 'input }

type SessionAdvance =
    { SessionId: string
      StepCount: uint64 }

type SessionProjection<'projection> =
    { SessionId: string
      Revision: uint64
      Value: 'projection }

type SessionSnapshot<'snapshot> =
    { SessionId: string
      Revision: uint64
      Compatibility: SessionCompatibility
      Value: 'snapshot }

type SessionFailure =
    { Code: string
      Message: string }

type SessionContract<'configuration, 'state, 'input, 'projection, 'snapshot> =
    { Initialize: SessionInitialization<'configuration> -> Result<'state, SessionFailure>
      AdmitInput: SessionInput<'input> -> 'state -> Result<'state, SessionFailure>
      Advance: SessionAdvance -> 'state -> Result<'state, SessionFailure>
      Project: 'state -> SessionProjection<'projection>
      Snapshot: 'state -> SessionSnapshot<'snapshot>
      Restore: SessionSnapshot<'snapshot> -> Result<'state, SessionFailure> }

[<RequireQualifiedAccess>]
module SessionCompatibility =
    let private missing (value: string) = System.String.IsNullOrWhiteSpace value

    let validate value =
        [ if value.ContractVersion <= 0 then SessionContractIssue.InvalidContractVersion value.ContractVersion
          if missing value.EngineId then SessionContractIssue.MissingEngineId
          if missing value.EngineVersion then SessionContractIssue.MissingEngineVersion
          if missing value.ProfileId then SessionContractIssue.MissingProfileId
          if missing value.SchemaId then SessionContractIssue.MissingSchemaId
          if value.SchemaVersion <= 0 then SessionContractIssue.InvalidSchemaVersion value.SchemaVersion ]

    let compare expected actual =
        [ if expected.ContractVersion <> actual.ContractVersion then SessionCompatibilityIssue.ContractVersion(expected.ContractVersion, actual.ContractVersion)
          if expected.EngineId <> actual.EngineId then SessionCompatibilityIssue.EngineId(expected.EngineId, actual.EngineId)
          if expected.EngineVersion <> actual.EngineVersion then SessionCompatibilityIssue.EngineVersion(expected.EngineVersion, actual.EngineVersion)
          if expected.ProfileId <> actual.ProfileId then SessionCompatibilityIssue.ProfileId(expected.ProfileId, actual.ProfileId)
          if expected.SchemaId <> actual.SchemaId then SessionCompatibilityIssue.SchemaId(expected.SchemaId, actual.SchemaId)
          if expected.SchemaVersion <> actual.SchemaVersion then SessionCompatibilityIssue.SchemaVersion(expected.SchemaVersion, actual.SchemaVersion) ]

    let isCompatible expected actual = compare expected actual |> List.isEmpty

[<RequireQualifiedAccess>]
module SessionEnvelope =
    let private validateSessionId sessionId =
        if System.String.IsNullOrWhiteSpace sessionId then [ SessionContractIssue.MissingSessionId ] else []

    let validateInitialization (value: SessionInitialization<'configuration>) =
        validateSessionId value.SessionId @ SessionCompatibility.validate value.Compatibility

    let validateInput (value: SessionInput<'input>) =
        [ if System.String.IsNullOrWhiteSpace value.SessionId then SessionContractIssue.MissingSessionId
          if System.String.IsNullOrWhiteSpace value.InputId then SessionContractIssue.MissingInputId ]

    let validateAdvance (value: SessionAdvance) = validateSessionId value.SessionId
    let validateProjection (value: SessionProjection<'projection>) = validateSessionId value.SessionId
    let validateSnapshot (value: SessionSnapshot<'snapshot>) =
        validateSessionId value.SessionId @ SessionCompatibility.validate value.Compatibility

[<RequireQualifiedAccess>]
module SessionSupport =
    let current = SessionSupport.ContractEnvelopeOnly
    let id support =
        match support with
        | SessionSupport.ContractEnvelopeOnly -> "contract-envelope-only"

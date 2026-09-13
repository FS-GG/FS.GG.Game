namespace FS.GG.Game.Core

[<RequireQualifiedAccess>]
type SaveFamily = ProjectDocument | AssetManifest | GameSave | WorkspacePreferences

type SaveIdentity =
    { Family: SaveFamily; EngineId: string; EngineVersion: string; ProfileId: string; SchemaId: string
      SchemaVersion: int; ContentHash: string; AssetHash: string }

type SaveEnvelope<'payload> = { Identity: SaveIdentity; PayloadHash: string; Payload: 'payload }
[<Struct>]
type SaveOperationId = { Generation: uint64; Operation: uint64 }
type SaveMigrationStep<'payload> = { FromVersion: int; ToVersion: int; Apply: 'payload -> Result<'payload,string> }

[<RequireQualifiedAccess>]
type SaveRefusal =
    | InvalidIdentity of string
    | FamilyMismatch of SaveFamily * SaveFamily
    | EngineMismatch of expectedId: string * expectedVersion: string * actualId: string * actualVersion: string
    | ProfileMismatch of string * string
    | SchemaMismatch of string * string
    | ContentHashMismatch of string * string
    | AssetHashMismatch of string * string
    | PayloadHashMismatch of string * string
    | NewerSchema of supported: int * actual: int
    | MissingMigration of fromVersion: int * toVersion: int
    | InvalidMigrationPlan of string
    | MigrationFailed of fromVersion: int * toVersion: int * diagnostic: string
    | OperationSequenceExhausted
    | StaleOperation of expected: SaveOperationId * actual: SaveOperationId
    | OperationFailed of string
    | Inactive

[<RequireQualifiedAccess>]
type SaveMigrationStatus = Idle | Migrating | Ready | Failed of SaveRefusal | Disposed
type SaveMigrationState<'payload> =
    { Expected: SaveIdentity; Generation: uint64; NextOperation: uint64; Status: SaveMigrationStatus
      LastCommitted: SaveEnvelope<'payload> option; Pending: (SaveOperationId * SaveEnvelope<'payload>) option }

[<RequireQualifiedAccess>]
type SaveMigrationObservation<'payload> = Begin of SaveEnvelope<'payload> | StepCommitted of SaveOperationId | StepFailed of SaveOperationId * string | Replace of SaveEnvelope<'payload> | Dispose
[<RequireQualifiedAccess>]
type SaveMigrationEffect<'payload> = PersistStep of SaveOperationId * SaveEnvelope<'payload> | Accepted of SaveEnvelope<'payload> | GenerationInvalidated of uint64 * uint64 | Refused of SaveRefusal | Disposed

[<RequireQualifiedAccess>]
module SaveMigration =
    let private blank value = System.String.IsNullOrWhiteSpace value
    let private validateIdentity identity =
        if blank identity.EngineId then Some(SaveRefusal.InvalidIdentity "engine-id")
        elif blank identity.EngineVersion then Some(SaveRefusal.InvalidIdentity "engine-version")
        elif blank identity.ProfileId then Some(SaveRefusal.InvalidIdentity "profile-id")
        elif blank identity.SchemaId then Some(SaveRefusal.InvalidIdentity "schema-id")
        elif identity.SchemaVersion < 1 then Some(SaveRefusal.InvalidIdentity "schema-version")
        elif blank identity.ContentHash then Some(SaveRefusal.InvalidIdentity "content-hash")
        elif blank identity.AssetHash then Some(SaveRefusal.InvalidIdentity "asset-hash")
        else None

    let initialize expected =
        match validateIdentity expected with
        | Some refusal -> Error refusal
        | None -> Ok { Expected=expected; Generation=0UL; NextOperation=0UL; Status=SaveMigrationStatus.Idle; LastCommitted=None; Pending=None }

    let private compatible expected actual =
        if expected.Family <> actual.Family then Some(SaveRefusal.FamilyMismatch(expected.Family,actual.Family))
        elif expected.EngineId <> actual.EngineId || expected.EngineVersion <> actual.EngineVersion then Some(SaveRefusal.EngineMismatch(expected.EngineId,expected.EngineVersion,actual.EngineId,actual.EngineVersion))
        elif expected.ProfileId <> actual.ProfileId then Some(SaveRefusal.ProfileMismatch(expected.ProfileId,actual.ProfileId))
        elif expected.SchemaId <> actual.SchemaId then Some(SaveRefusal.SchemaMismatch(expected.SchemaId,actual.SchemaId))
        elif expected.ContentHash <> actual.ContentHash then Some(SaveRefusal.ContentHashMismatch(expected.ContentHash,actual.ContentHash))
        elif expected.AssetHash <> actual.AssetHash then Some(SaveRefusal.AssetHashMismatch(expected.AssetHash,actual.AssetHash))
        elif actual.SchemaVersion > expected.SchemaVersion then Some(SaveRefusal.NewerSchema(expected.SchemaVersion,actual.SchemaVersion))
        else None

    let private validatePlan steps =
        let ordered = steps |> List.sortBy (fun step -> step.FromVersion)
        let rec loop prior remaining =
            match remaining with
            | [] -> None
            | step::tail when step.FromVersion < 1 || step.ToVersion <> step.FromVersion + 1 -> Some(SaveRefusal.InvalidMigrationPlan "steps must advance one positive version")
            | step::_ when prior = Some step.FromVersion -> Some(SaveRefusal.InvalidMigrationPlan "duplicate or cyclic source version")
            | step::tail -> loop (Some step.FromVersion) tail
        loop None ordered

    let update hashPayload steps observation state =
        let refuse reason = state, [ SaveMigrationEffect.Refused reason ]
        let start envelope generation effects =
            match validateIdentity envelope.Identity with
            | Some refusal -> { state with Generation=generation; Status=SaveMigrationStatus.Failed refusal; Pending=None }, effects @ [ SaveMigrationEffect.Refused refusal ]
            | None ->
                match compatible state.Expected envelope.Identity with
                | Some refusal -> { state with Generation=generation; Status=SaveMigrationStatus.Failed refusal; Pending=None }, effects @ [ SaveMigrationEffect.Refused refusal ]
                | None when hashPayload envelope.Payload <> envelope.PayloadHash ->
                    let refusal = SaveRefusal.PayloadHashMismatch(envelope.PayloadHash,hashPayload envelope.Payload)
                    { state with Generation=generation; Status=SaveMigrationStatus.Failed refusal; Pending=None }, effects @ [ SaveMigrationEffect.Refused refusal ]
                | None when envelope.Identity.SchemaVersion = state.Expected.SchemaVersion ->
                    let accepted = { envelope with Identity=state.Expected }
                    { state with Generation=generation; Status=SaveMigrationStatus.Ready; LastCommitted=Some accepted; Pending=None }, effects @ [ SaveMigrationEffect.Accepted accepted ]
                | None ->
                    match validatePlan steps with
                    | Some refusal -> { state with Generation=generation; Status=SaveMigrationStatus.Failed refusal; Pending=None }, effects @ [ SaveMigrationEffect.Refused refusal ]
                    | None ->
                        match steps |> List.tryFind (fun step -> step.FromVersion = envelope.Identity.SchemaVersion) with
                        | None ->
                            let refusal=SaveRefusal.MissingMigration(envelope.Identity.SchemaVersion,envelope.Identity.SchemaVersion+1)
                            { state with Generation=generation; Status=SaveMigrationStatus.Failed refusal; Pending=None }, effects @ [ SaveMigrationEffect.Refused refusal ]
                        | Some _ when state.NextOperation=System.UInt64.MaxValue ->
                            let refusal=SaveRefusal.OperationSequenceExhausted
                            { state with Generation=generation; Status=SaveMigrationStatus.Failed refusal; Pending=None }, effects @ [ SaveMigrationEffect.Refused refusal ]
                        | Some step ->
                            match step.Apply envelope.Payload with
                            | Error diagnostic ->
                                let refusal=SaveRefusal.MigrationFailed(step.FromVersion,step.ToVersion,diagnostic)
                                { state with Generation=generation; Status=SaveMigrationStatus.Failed refusal; Pending=None }, effects @ [ SaveMigrationEffect.Refused refusal ]
                            | Ok payload ->
                                let id={ Generation=generation; Operation=state.NextOperation }
                                let migrated={ Identity={ envelope.Identity with SchemaVersion=step.ToVersion }; PayloadHash=hashPayload payload; Payload=payload }
                                { state with Generation=generation; NextOperation=state.NextOperation+1UL; Status=SaveMigrationStatus.Migrating; Pending=Some(id,migrated) }, effects @ [ SaveMigrationEffect.PersistStep(id,migrated) ]
        match state.Status, observation with
        | SaveMigrationStatus.Disposed, SaveMigrationObservation.Dispose -> state, []
        | SaveMigrationStatus.Disposed, _ -> refuse SaveRefusal.Inactive
        | _, SaveMigrationObservation.Dispose -> { state with Status=SaveMigrationStatus.Disposed; Pending=None }, [ SaveMigrationEffect.Disposed ]
        | _, SaveMigrationObservation.Replace envelope ->
            if state.Generation = System.UInt64.MaxValue then refuse (SaveRefusal.InvalidMigrationPlan "generation exhausted")
            else let generation=state.Generation+1UL in start envelope generation [ SaveMigrationEffect.GenerationInvalidated(state.Generation,generation) ]
        | _, SaveMigrationObservation.Begin envelope -> start envelope state.Generation []
        | _, SaveMigrationObservation.StepCommitted id ->
            match state.Pending with
            | Some(expected,envelope) when expected=id ->
                let committed={ state with LastCommitted=Some envelope; Pending=None }
                start envelope state.Generation [] |> fun (next,effects) -> { next with LastCommitted=committed.LastCommitted }, effects
            | Some(expected,_) -> refuse (SaveRefusal.StaleOperation(expected,id))
            | None -> refuse SaveRefusal.Inactive
        | _, SaveMigrationObservation.StepFailed(id,diagnostic) ->
            match state.Pending with
            | Some(expected,_) when expected=id ->
                let refusal=SaveRefusal.OperationFailed diagnostic
                { state with Status=SaveMigrationStatus.Failed refusal; Pending=None }, [ SaveMigrationEffect.Refused refusal ]
            | Some(expected,_) -> refuse (SaveRefusal.StaleOperation(expected,id))
            | None -> refuse SaveRefusal.Inactive

[<RequireQualifiedAccess>]
type AutosaveStatus = Clean | Debouncing | Persisting | Failed of string | Cancelled | Disposed
type AutosaveConfig = { DebounceMilliseconds: uint64 }
type AutosaveState<'value> =
    { Config: AutosaveConfig; Generation: uint64; NextOperation: uint64; Status: AutosaveStatus
      LastCommitted: 'value option; Draft: 'value option; DueAtMilliseconds: uint64 option
      InFlight: (SaveOperationId * 'value) option }
[<RequireQualifiedAccess>]
type AutosaveObservation<'value> = Edit of nowMilliseconds: uint64 * value: 'value | Tick of nowMilliseconds: uint64 | Persisted of SaveOperationId | PersistFailed of SaveOperationId * string | Retry | Cancel | Replace of 'value option | Dispose
[<RequireQualifiedAccess>]
type AutosaveEffect<'value> = Persist of SaveOperationId * 'value | Committed of SaveOperationId * 'value | GenerationInvalidated of uint64 * uint64 | Refused of SaveRefusal | Disposed

[<RequireQualifiedAccess>]
module Autosave =
    let initialize config committed =
        if config.DebounceMilliseconds=0UL then Error(SaveRefusal.InvalidIdentity "debounce-milliseconds")
        else Ok { Config=config;Generation=0UL;NextOperation=0UL;Status=AutosaveStatus.Clean;LastCommitted=committed;Draft=None;DueAtMilliseconds=None;InFlight=None }
    let private dispatch state value =
        if state.NextOperation=System.UInt64.MaxValue then
            state,[AutosaveEffect.Refused SaveRefusal.OperationSequenceExhausted]
        else
            let id={Generation=state.Generation;Operation=state.NextOperation}
            {state with NextOperation=state.NextOperation+1UL;Status=AutosaveStatus.Persisting;Draft=None;InFlight=Some(id,value);DueAtMilliseconds=None},[AutosaveEffect.Persist(id,value)]
    let update observation state =
        let refuse reason=state,[AutosaveEffect.Refused reason]
        match state.Status,observation with
        | AutosaveStatus.Disposed,AutosaveObservation.Dispose -> state,[]
        | AutosaveStatus.Disposed,_ -> refuse SaveRefusal.Inactive
        | _,AutosaveObservation.Dispose -> {state with Status=AutosaveStatus.Disposed;Draft=None;InFlight=None;DueAtMilliseconds=None},[AutosaveEffect.Disposed]
        | _,AutosaveObservation.Replace value ->
            if state.Generation=System.UInt64.MaxValue then refuse (SaveRefusal.InvalidMigrationPlan "generation exhausted")
            else let generation=state.Generation+1UL in {state with Generation=generation;Status=AutosaveStatus.Clean;LastCommitted=value;Draft=None;InFlight=None;DueAtMilliseconds=None},[AutosaveEffect.GenerationInvalidated(state.Generation,generation)]
        | _,AutosaveObservation.Cancel -> {state with Status=AutosaveStatus.Cancelled;Draft=None;InFlight=None;DueAtMilliseconds=None},[]
        | AutosaveStatus.Cancelled,_ -> refuse SaveRefusal.Inactive
        | AutosaveStatus.Persisting,AutosaveObservation.Edit(now,value) ->
            let due=if System.UInt64.MaxValue-now < state.Config.DebounceMilliseconds then System.UInt64.MaxValue else now+state.Config.DebounceMilliseconds
            {state with Draft=Some value;DueAtMilliseconds=Some due},[]
        | _,AutosaveObservation.Edit(now,value) ->
            let due=if System.UInt64.MaxValue-now < state.Config.DebounceMilliseconds then System.UInt64.MaxValue else now+state.Config.DebounceMilliseconds
            {state with Status=AutosaveStatus.Debouncing;Draft=Some value;DueAtMilliseconds=Some due},[]
        | AutosaveStatus.Debouncing,AutosaveObservation.Tick now ->
            match state.DueAtMilliseconds,state.Draft with Some due,Some value when now>=due -> dispatch state value | _ -> state,[]
        | AutosaveStatus.Failed _,AutosaveObservation.Retry -> match state.Draft with Some value -> dispatch state value | None -> refuse SaveRefusal.Inactive
        | AutosaveStatus.Persisting,AutosaveObservation.Persisted id ->
            match state.InFlight with
            | Some(expected,value) when expected=id ->
                let status=if state.Draft.IsSome then AutosaveStatus.Debouncing else AutosaveStatus.Clean
                {state with Status=status;LastCommitted=Some value;InFlight=None},[AutosaveEffect.Committed(id,value)]
            | Some(expected,_) -> refuse (SaveRefusal.StaleOperation(expected,id))
            | None -> refuse SaveRefusal.Inactive
        | AutosaveStatus.Persisting,AutosaveObservation.PersistFailed(id,diagnostic) ->
            match state.InFlight with
            | Some(expected,value) when expected=id ->
                let draft=match state.Draft with Some newer -> Some newer | None -> Some value
                {state with Status=AutosaveStatus.Failed diagnostic;Draft=draft;InFlight=None},[AutosaveEffect.Refused(SaveRefusal.OperationFailed diagnostic)]
            | Some(expected,_) -> refuse (SaveRefusal.StaleOperation(expected,id))
            | None -> refuse SaveRefusal.Inactive
        | _,AutosaveObservation.Persisted id -> refuse (SaveRefusal.StaleOperation({Generation=state.Generation;Operation=state.NextOperation},id))
        | _,AutosaveObservation.PersistFailed(id,_) -> refuse (SaveRefusal.StaleOperation({Generation=state.Generation;Operation=state.NextOperation},id))
        | _ -> state,[]

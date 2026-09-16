module Game.Core.Tests.SaveMigrationTests

open Expecto
open FS.GG.Game.Core

let identity version =
    {
        Family = SaveFamily.GameSave
        EngineId = "arena"
        EngineVersion = "1.0"
        ProfileId = "svg/2"
        SchemaId = "arena-save"
        SchemaVersion = version
        ContentHash = "content-a"
        AssetHash = "assets-a"
    }

let envelope version payload =
    {
        Identity = identity version
        PayloadHash = string payload
        Payload = payload
    }

let steps =
    [
        {
            FromVersion = 1
            ToVersion = 2
            Apply = fun value -> Ok(value + 10)
        }
        {
            FromVersion = 2
            ToVersion = 3
            Apply = fun value -> Ok(value * 2)
        }
    ]

let getOk =
    function
    | Ok value -> value
    | Error error -> failtestf "unexpected error: %A" error

let initialized () =
    SaveMigration.initialize (identity 3) |> getOk

[<Tests>]
let tests =
    testList
        "save migration"
        [
            test "ordered steps commit once and resume from the last committed value" {
                let started, effects =
                    SaveMigration.update string steps (SaveMigrationObservation.Begin(envelope 1 5)) (initialized ())

                let firstId, first =
                    match effects with
                    | [ SaveMigrationEffect.PersistStep(id, value) ] -> id, value
                    | other -> failtestf "%A" other

                Expect.equal first.Identity.SchemaVersion 2 "first adjacent step"

                let secondState, secondEffects =
                    SaveMigration.update string steps (SaveMigrationObservation.StepCommitted firstId) started

                let secondId, second =
                    match secondEffects with
                    | [ SaveMigrationEffect.PersistStep(id, value) ] -> id, value
                    | other -> failtestf "%A" other

                Expect.equal second.Payload 30 "second migration uses committed first result"
                Expect.equal secondState.LastCommitted (Some first) "recovery point advances only after commit"

                let completed, doneEffects =
                    SaveMigration.update string steps (SaveMigrationObservation.StepCommitted secondId) secondState

                Expect.equal
                    doneEffects
                    [ SaveMigrationEffect.Accepted { second with Identity = identity 3 } ]
                    "target is accepted"

                Expect.equal completed.Status SaveMigrationStatus.Ready "migration is complete"

                let unchanged, duplicate =
                    SaveMigration.update string steps (SaveMigrationObservation.StepCommitted secondId) completed

                Expect.equal unchanged completed "duplicate completion preserves accepted value"
                Expect.equal duplicate [ SaveMigrationEffect.Refused SaveRefusal.Inactive ] "step never runs twice"
            }
            test "identity, hash, newer version and migration gaps preserve the prior value" {
                let ready, _ =
                    SaveMigration.update string steps (SaveMigrationObservation.Begin(envelope 3 7)) (initialized ())

                let wrong =
                    { envelope 1 9 with
                        Identity =
                            { identity 1 with
                                ContentHash = "other"
                            }
                    }

                let refused, effects =
                    SaveMigration.update string steps (SaveMigrationObservation.Replace wrong) ready

                Expect.equal refused.LastCommitted ready.LastCommitted "replacement refusal preserves last accepted"

                Expect.equal
                    effects.Head
                    (SaveMigrationEffect.GenerationInvalidated(0UL, 1UL))
                    "replacement invalidates stale work"

                let newer, e2 =
                    SaveMigration.update string steps (SaveMigrationObservation.Begin(envelope 4 9)) ready

                Expect.equal newer.LastCommitted ready.LastCommitted "newer refusal preserves prior"

                Expect.equal
                    e2
                    [ SaveMigrationEffect.Refused(SaveRefusal.NewerSchema(3, 4)) ]
                    "newer schema is explicit"

                let corrupt =
                    { envelope 3 9 with
                        PayloadHash = "bad"
                    }

                let _, e3 =
                    SaveMigration.update string steps (SaveMigrationObservation.Begin corrupt) ready

                Expect.equal
                    e3
                    [ SaveMigrationEffect.Refused(SaveRefusal.PayloadHashMismatch("bad", "9")) ]
                    "payload hash is checked"

                let _, e4 =
                    SaveMigration.update string [ steps.Head ] (SaveMigrationObservation.Begin(envelope 2 1)) ready

                Expect.equal e4 [ SaveMigrationEffect.Refused(SaveRefusal.MissingMigration(2, 3)) ] "skip is refused"
            }
            test "malformed plans and failed steps do not create pending writes" {
                let bad =
                    [
                        {
                            FromVersion = 1
                            ToVersion = 3
                            Apply = Ok
                        }
                    ]

                let state, effects =
                    SaveMigration.update string bad (SaveMigrationObservation.Begin(envelope 1 1)) (initialized ())

                Expect.isNone state.Pending "nothing persists"

                Expect.equal
                    effects
                    [
                        SaveMigrationEffect.Refused(
                            SaveRefusal.InvalidMigrationPlan "steps must advance one positive version"
                        )
                    ]
                    "skip is diagnosed"

                let failing =
                    [
                        {
                            FromVersion = 1
                            ToVersion = 2
                            Apply = fun _ -> Error "bad bytes"
                        }
                    ]

                let _, failed =
                    SaveMigration.update
                        string
                        failing
                        (SaveMigrationObservation.Begin(envelope 1 1))
                        (SaveMigration.initialize (identity 2) |> getOk)

                Expect.equal
                    failed
                    [ SaveMigrationEffect.Refused(SaveRefusal.MigrationFailed(1, 2, "bad bytes")) ]
                    "migration failure is exact"
            }
            test "stale completion, replacement and disposal are generation safe" {
                let started, effects =
                    SaveMigration.update string steps (SaveMigrationObservation.Begin(envelope 1 1)) (initialized ())

                let id =
                    match effects with
                    | [ SaveMigrationEffect.PersistStep(id, _) ] -> id
                    | _ -> failtest "missing write"

                let replaced, replacement =
                    SaveMigration.update string steps (SaveMigrationObservation.Replace(envelope 3 50)) started

                Expect.equal
                    replacement.Head
                    (SaveMigrationEffect.GenerationInvalidated(0UL, 1UL))
                    "generation increments"

                let same, stale =
                    SaveMigration.update string steps (SaveMigrationObservation.StepCommitted id) replaced

                Expect.equal same replaced "late completion is inert"
                Expect.equal stale [ SaveMigrationEffect.Refused SaveRefusal.Inactive ] "late completion refuses"

                let disposed, _ =
                    SaveMigration.update string steps SaveMigrationObservation.Dispose replaced

                let inert, e =
                    SaveMigration.update string steps (SaveMigrationObservation.Begin(envelope 3 2)) disposed

                Expect.equal inert disposed "disposed state is preserved"
                Expect.equal e [ SaveMigrationEffect.Refused SaveRefusal.Inactive ] "post-disposal is explicit"
            }
        ]

[<Tests>]
let autosaveTests =
    testList
        "autosave"
        [
            test "debounce, failure, retry and commit preserve the last accepted value" {
                let initial =
                    Autosave.initialize { DebounceMilliseconds = 10UL } (Some "old") |> getOk

                let edited, _ = Autosave.update (AutosaveObservation.Edit(5UL, "new")) initial
                let early, e0 = Autosave.update (AutosaveObservation.Tick 14UL) edited
                Expect.isEmpty e0 "debounce is bounded"
                let writing, e1 = Autosave.update (AutosaveObservation.Tick 15UL) early

                let id =
                    match e1 with
                    | [ AutosaveEffect.Persist(id, "new") ] -> id
                    | _ -> failtest "missing persist"

                let failed, e2 =
                    Autosave.update (AutosaveObservation.PersistFailed(id, "quota")) writing

                Expect.equal failed.LastCommitted (Some "old") "quota failure preserves last committed"
                Expect.equal e2 [ AutosaveEffect.Refused(SaveRefusal.OperationFailed "quota") ] "failure is observable"
                let retry, e3 = Autosave.update AutosaveObservation.Retry failed

                let retryId =
                    match e3 with
                    | [ AutosaveEffect.Persist(id, "new") ] -> id
                    | _ -> failtest "missing retry"

                let committed, _ = Autosave.update (AutosaveObservation.Persisted retryId) retry
                Expect.equal committed.LastCommitted (Some "new") "retry commits draft"
            }
            test "replacement, cancellation, stale completion and disposal are inert" {
                let initial = Autosave.initialize { DebounceMilliseconds = 1UL } (Some 1) |> getOk
                let edited, _ = Autosave.update (AutosaveObservation.Edit(0UL, 2)) initial
                let writing, effects = Autosave.update (AutosaveObservation.Tick 1UL) edited

                let id =
                    match effects with
                    | [ AutosaveEffect.Persist(id, 2) ] -> id
                    | _ -> failtest "missing persist"

                let replaced, _ = Autosave.update (AutosaveObservation.Replace(Some 3)) writing
                let same, stale = Autosave.update (AutosaveObservation.Persisted id) replaced
                Expect.equal same replaced "stale completion cannot overwrite replacement"

                Expect.isTrue
                    (match stale with
                     | [ AutosaveEffect.Refused(SaveRefusal.StaleOperation _) ] -> true
                     | _ -> false)
                    "stale completion is diagnosed"

                let cancelled, _ = Autosave.update AutosaveObservation.Cancel replaced
                let _, inactive = Autosave.update (AutosaveObservation.Edit(2UL, 4)) cancelled
                Expect.equal inactive [ AutosaveEffect.Refused SaveRefusal.Inactive ] "cancel is terminal"
                let disposed, e = Autosave.update AutosaveObservation.Dispose cancelled
                Expect.equal e [ AutosaveEffect.Disposed ] "dispose is observable"
                let _, after = Autosave.update AutosaveObservation.Retry disposed
                Expect.equal after [ AutosaveEffect.Refused SaveRefusal.Inactive ] "disposed refuses retries"
            }
            test "an edit during persistence queues one later debounced write" {
                let initial =
                    Autosave.initialize { DebounceMilliseconds = 5UL } (Some "old") |> getOk

                let first, _ = Autosave.update (AutosaveObservation.Edit(0UL, "first")) initial
                let writing, effects = Autosave.update (AutosaveObservation.Tick 5UL) first

                let firstId =
                    match effects with
                    | [ AutosaveEffect.Persist(id, "first") ] -> id
                    | _ -> failtest "missing first"

                let queued, _ = Autosave.update (AutosaveObservation.Edit(6UL, "second")) writing
                let accepted, _ = Autosave.update (AutosaveObservation.Persisted firstId) queued
                Expect.equal accepted.LastCommitted (Some "first") "in-flight completion still commits"
                Expect.equal accepted.Status AutosaveStatus.Debouncing "newer edit remains queued"
                let second, e2 = Autosave.update (AutosaveObservation.Tick 11UL) accepted

                Expect.equal
                    e2
                    [ AutosaveEffect.Persist({ Generation = 0UL; Operation = 1UL }, "second") ]
                    "queued write dispatches once"

                Expect.equal second.Status AutosaveStatus.Persisting "second write is owned"
            }
        ]

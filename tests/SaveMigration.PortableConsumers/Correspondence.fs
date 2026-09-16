module SaveMigrationCorrespondence

open FS.GG.Game.Core

let private identity version =
    {
        Family = SaveFamily.GameSave
        EngineId = "arena"
        EngineVersion = "1"
        ProfileId = "svg/2"
        SchemaId = "save"
        SchemaVersion = version
        ContentHash = "content"
        AssetHash = "assets"
    }

let private envelope version payload =
    {
        Identity = identity version
        PayloadHash = string payload
        Payload = payload
    }

let private steps =
    [
        {
            FromVersion = 1
            ToVersion = 2
            Apply = fun value -> Ok(value + 2)
        }
        {
            FromVersion = 2
            ToVersion = 3
            Apply = fun value -> Ok(value * 3)
        }
    ]

let private op =
    function
    | SaveMigrationEffect.PersistStep(id, value) -> Some(id, value)
    | _ -> None

let run () =
    let initial =
        match SaveMigration.initialize (identity 3) with
        | Ok value -> value
        | Error _ -> failwith "init"

    let started, e1 =
        SaveMigration.update string steps (SaveMigrationObservation.Begin(envelope 1 4)) initial

    let id1, value1 = e1 |> List.choose op |> List.head

    let continued, e2 =
        SaveMigration.update string steps (SaveMigrationObservation.StepCommitted id1) started

    let id2, value2 = e2 |> List.choose op |> List.head

    let completed, e3 =
        SaveMigration.update string steps (SaveMigrationObservation.StepCommitted id2) continued

    let auto =
        match Autosave.initialize { DebounceMilliseconds = 5UL } (Some 1) with
        | Ok value -> value
        | Error _ -> failwith "auto"

    let edited, _ = Autosave.update (AutosaveObservation.Edit(10UL, 2)) auto
    let writing, a1 = Autosave.update (AutosaveObservation.Tick 15UL) edited

    let aid =
        match a1 with
        | [ AutosaveEffect.Persist(id, _) ] -> id
        | _ -> failwith "persist"

    let failed, a2 =
        Autosave.update (AutosaveObservation.PersistFailed(aid, "quota")) writing

    let retry, a3 = Autosave.update AutosaveObservation.Retry failed

    let aid2 =
        match a3 with
        | [ AutosaveEffect.Persist(id, _) ] -> id
        | _ -> failwith "retry"

    let saved, a4 = Autosave.update (AutosaveObservation.Persisted aid2) retry

    [
        $"m1={value1.Identity.SchemaVersion}:{value1.Payload}:{id1.Generation}:{id1.Operation}"
        $"m2={value2.Identity.SchemaVersion}:{value2.Payload}:{id2.Generation}:{id2.Operation}"
        $"done={completed.Status}:{e3.Length}:{completed.LastCommitted.Value.Payload}"
        $"auto={failed.Status}:{failed.LastCommitted.Value}:{a2.Length}:{saved.LastCommitted.Value}:{a4.Length}"
    ]
    |> String.concat "\n"
    |> fun value -> value + "\n"

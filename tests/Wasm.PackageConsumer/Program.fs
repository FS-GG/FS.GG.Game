open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Browser

type ConsumerResult =
    {
        Request: string
        State: string
        Phase: string
        Disposition: string
        Reason: string
        HasOutcome: bool
        Output: byte array
        Diagnostic: string
        CleanupDiagnostic: string
        GuestStatus: int
        Dispatched: bool
    }

let private unwrap =
    function
    | Ok value -> value
    | Error value -> failwithf "package API refused: %A" value

let private identity wire = Identity.parse wire |> unwrap

// This fixture calls the installed F# Host. It contains no runtime or guest policy.
let private configuration path digest outputCap deadline =
    let profile =
        if path = "bar-protected" then
            BarProtected
        else
            Sc2ImportedStrict

    let descriptor = Profiles.tryFind profile |> Option.get

    Validation.validateConfiguration
        {
            Path = profile
            ArtifactSha256 = digest
            ConfigurationSha256 = String.replicate 64 "b"
            Limits =
                { descriptor.Limits with
                    MaximumOutputBytes = outputCap
                    MaximumDeadlineMilliseconds = deadline
                }
            Deadline = descriptor.Deadline
            Scheduling = descriptor.Scheduling
            Replacement = descriptor.Replacement
        }
    |> unwrap

let createHost path digest outputCap deadline (transport: HostTransport) receive =
    let profile =
        if path = "bar-protected" then
            BarProtected
        else
            Sc2ImportedStrict

    let configuration = configuration path digest outputCap deadline
    let role = if profile = BarProtected then None else Some Controller
    let settings = HostSettings.create configuration role |> unwrap

    let settle (result: BrowserResult) =
        let state, phase, output, diagnostic, cleanup, status, dispatched =
            match result.Outcome with
            | Some outcome ->
                let state =
                    match outcome.State with
                    | Succeeded -> "succeeded"
                    | GuestRejected _ -> "rejected"
                    | Faulted _ -> "faulted"
                    | TimedOut -> "timeout"
                    | _ -> "stopped"

                let diagnostic =
                    match outcome.State with
                    | Faulted value -> value
                    | _ -> string result.Reason

                state,
                string outcome.Phase,
                outcome.CopiedOutput,
                diagnostic,
                (outcome.CleanupFault |> Option.map _.Diagnostic |> Option.defaultValue ""),
                (match outcome.State with
                 | GuestRejected value -> value
                 | _ -> 0),
                outcome.Dispatch = Dispatched
            | None -> "stopped", "", Array.empty, string result.Reason, "", 0, false

        receive
            {
                Request = string result.Identity.Request
                State = state
                Phase = phase
                Disposition = string result.Disposition
                Reason = result.Reason |> Option.map string |> Option.defaultValue ""
                HasOutcome = result.Outcome.IsSome
                Output = output
                Diagnostic = diagnostic
                CleanupDiagnostic = cleanup
                GuestStatus = status
                Dispatched = dispatched
            }

    Host.CreateConnected(settings, transport, settle) |> unwrap

let load (host: Host) now wire artifact = host.Load(now, identity wire, artifact)

// The cold-start controls use the same real, pre-create enclosing deadline as
// a limited consumer operation. Phase observations cannot renew this budget.
let loadBoundedSc2 (host: Host) (now: int64) wire digest artifact deadline =
    host.SubmitLimited(
        now,
        LimitedLoad(identity wire, configuration "sc2-imported-strict" digest 65536 deadline, artifact),
        { MaximumDeadlineMilliseconds = deadline
          MaximumOutputBytes = 65536
          EnclosingDeadlineMilliseconds = Some(now + int64 deadline) }
    ) |> unwrap

let prepareBoundedSc2 (host: Host) (now: int64) wire transaction digest artifact =
    host.SubmitLimited(
        now,
        LimitedCandidate(identity wire, transaction, Some 1UL,
            configuration "sc2-imported-strict" digest 65536 250, artifact),
        { MaximumDeadlineMilliseconds = 250
          MaximumOutputBytes = 65536
          EnclosingDeadlineMilliseconds = Some(now + 250L) }
    ) |> unwrap

let initialize (host: Host) now wire input =
    host.Initialize(now, identity wire, input)

let invoke (host: Host) now wire input =
    host.Invoke(now, identity wire, Ordinary, input)

let invokeAndRetire (host: Host) now wire input =
    host.SubmitLimited(now, LimitedInvokeAndRetire(identity wire, input),
        { MaximumDeadlineMilliseconds = 250
          MaximumOutputBytes = 65536
          EnclosingDeadlineMilliseconds = None }) |> unwrap

let shutdown (host: Host) now wire = host.Shutdown(now, identity wire)
let dispose (host: Host) now = host.Dispose now

let prepare (host: Host) now wire transaction expected digest artifact =
    host.PrepareConfiguredCandidate(
        now,
        identity wire,
        transaction,
        Some expected,
        configuration "sc2-imported-strict" digest 65536 250,
        artifact
    )

let validateCandidate (host: Host) now transaction generation =
    host.ValidateCandidate(now, transaction, generation)

let abortCandidate (host: Host) now transaction = host.AbortCandidate(now, transaction)

let commitCandidate (host: Host) now transaction expected generation =
    host.CommitCandidate(now, transaction, expected, generation)

let freeze (host: Host) now token = host.Freeze(now, token)
let resume (host: Host) now token = host.Resume(now, token)

type ConsumerProjection =
    {
        Disposed: bool
        ActiveWorker: string
        ActiveGeneration: string
        CandidateWorker: string
        CandidateReady: bool
        Frozen: bool
        InitializedWorkers: string array
        RetiringWorkers: string array
    }

let project (host: Host) =
    let value = host.Projection

    {
        Disposed = value.Disposed
        ActiveWorker = value.ActiveWorker
        ActiveGeneration = string value.ActiveGeneration
        CandidateWorker = value.CandidateWorker
        CandidateReady = value.CandidateReady
        Frozen = value.Frozen
        InitializedWorkers = List.toArray value.InitializedWorkers
        RetiringWorkers = List.toArray value.RetiringWorkers
    }

[<EntryPoint>]
let main _ =
    let wire =
        {
            WorkerInstance = "fresh-worker"
            Request = "1"
            Generation = "1"
        }

    let parsed = identity wire
    let token = OperationToken.create "fresh-package-host" |> unwrap
    printfn "%s:%s:%b" parsed.WorkerInstance (OperationToken.value token) false
    0

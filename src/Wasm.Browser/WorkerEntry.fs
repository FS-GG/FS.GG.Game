namespace FS.GG.Wasm.Browser

open FS.GG.Wasm.Contracts

type WorkerMechanics =
    {
        Sha256Hex: byte array -> Async<Result<string, string>>
        CompileAfterAdmission: byte array -> Async<Result<unit, string>>
        PerformInvocationEffect: InvocationEffect -> Async<Result<InvocationEvent option, string>>
    }

type WorkerMechanicalEffect =
    {
        Kind: string
        Export: string
        Pointer: uint32
        Length: uint32
        Descriptor: uint32
        Bytes: byte array
    }

type WorkerMechanicalReply =
    {
        Pointer: uint32
        MemoryBytes: string
        Status: int
        OutputPointer: uint32
        OutputLength: uint32
        Bytes: byte array
        Diagnostic: string
    }

type WorkerWireMechanics =
    {
        Sha256: byte array -> (string -> unit) -> (string -> unit) -> unit
        Compile: byte array -> string -> string -> (uint32 -> unit) -> (string -> unit) -> unit
        Perform: WorkerMechanicalEffect -> WorkerMechanicalReply
    }

[<RequireQualifiedAccess>]
module WorkerEntry =
    let private failed identity correlation diagnostic =
        [ WorkerFailed(identity, Some correlation, diagnostic) ]

    let inspectAndCompile
        (mechanics: WorkerMechanics)
        (identity: HostIdentity)
        correlation
        (configuration: ValidatedConfiguration)
        (artifact: byte array)
        =
        async {
            match Admission.inspect configuration artifact with
            | Error issues -> return failed identity correlation ($"admission refused: {issues}")
            | Ok _ ->
                match! mechanics.Sha256Hex artifact with
                | Error diagnostic -> return failed identity correlation diagnostic
                | Ok digest ->
                    let expected = (Validation.configuration configuration).ArtifactSha256

                    if digest <> expected then
                        return failed identity correlation "artifact digest mismatch"
                    else
                        match! mechanics.CompileAfterAdmission artifact with
                        | Error diagnostic -> return failed identity correlation diagnostic
                        | Ok() ->
                            return
                                [
                                    PhaseObserved(identity, correlation, Compile)
                                    ArtifactDigestObserved(identity, correlation, digest)
                                ]
        }

    let private driveObserved
        emit
        (mechanics: WorkerMechanics)
        (configuration: ValidatedConfiguration)
        (identity: HostIdentity)
        correlation
        call
        (input: byte array)
        =
        async {
            let mutable state, effects = Invocation.start configuration identity call input
            let observations = ResizeArray<WorkerObservation>()

            let observe value =
                observations.Add value
                emit value

            let mutable stopped = false
            let mutable terminated = false
            let mutable lastPhase = None

            while not stopped && not effects.IsEmpty do
                let effect = effects.Head
                effects <- effects.Tail

                match effect with
                | InvocationCompleted outcome ->
                    observe (
                        if terminated then
                            InvocationTerminated(outcome, correlation)
                        else
                            InvocationObserved(outcome, correlation)
                    )

                    stopped <- true
                | _ ->
                    if effect = TerminateGuest then
                        terminated <- true

                    let phase =
                        match effect with
                        | Allocate(DescriptorAllocation, _) -> Some AllocateDescriptor
                        | Allocate(InputAllocation, _) -> Some AllocateInput
                        | InvokeGuest(InitializeCall, _, _) -> Some Initialize
                        | InvokeGuest(ProcessCall, _, _) -> Some Process
                        | FreeOwned _ -> Some Free
                        | _ -> None

                    match phase with
                    | Some value when lastPhase <> Some value ->
                        observe (PhaseObserved(identity, correlation, value))
                        lastPhase <- Some value
                    | _ -> ()

                    match! mechanics.PerformInvocationEffect effect with
                    | Error diagnostic ->
                        observe (WorkerFailed(identity, Some correlation, diagnostic))
                        stopped <- true
                    | Ok None -> ()
                    | Ok(Some event) ->
                        let next, emitted = Invocation.update event state
                        state <- next
                        effects <- effects @ emitted

            if not stopped && not (Invocation.isTerminal state) then
                observe (WorkerFailed(identity, Some correlation, "invocation mechanics ended before settlement"))

            return List.ofSeq observations
        }

    let driveInvocation mechanics configuration identity correlation call input =
        driveObserved ignore mechanics configuration identity correlation call input

    let private phaseName =
        function
        | Compile -> "compile"
        | Instantiate -> "instantiate"
        | AllocateDescriptor -> "allocate-descriptor"
        | AllocateInput -> "allocate-input"
        | Initialize -> "initialize"
        | Process -> "process"
        | Free -> "free"
        | Shutdown -> "shutdown"

    let private parsePhase =
        function
        | "compile" -> Some Compile
        | "instantiate" -> Some Instantiate
        | "allocate-descriptor" -> Some AllocateDescriptor
        | "allocate-input" -> Some AllocateInput
        | "initialize" -> Some Initialize
        | "process" -> Some Process
        | "free" -> Some Free
        | "shutdown" -> Some Shutdown
        | _ -> None

    let configurationToWire configuration =
        let value = Validation.configuration configuration

        {
            Path =
                (match value.Path with
                 | BarProtected -> "bar-protected"
                 | Sc2ImportedStrict -> "sc2-imported-strict"
                 | Sc2LegacyDirectUrl -> "sc2-legacy-direct-url")
            ArtifactSha256 = value.ArtifactSha256
            ConfigurationSha256 = value.ConfigurationSha256
            MaximumArtifactBytes = string value.Limits.MaximumArtifactBytes
            MaximumMemoryPages = string value.Limits.MaximumMemoryPages
            MaximumInputBytes = string value.Limits.MaximumInputBytes
            MaximumOutputBytes = string value.Limits.MaximumOutputBytes
            MaximumDeadlineMilliseconds = string value.Limits.MaximumDeadlineMilliseconds
            Deadline =
                (match value.Deadline with
                 | PhaseWatchdog -> "phase-watchdog"
                 | EndToEndFromEnqueue -> "end-to-end-from-enqueue")
            Scheduling =
                (match value.Scheduling with
                 | RefuseWhileBusy -> "refuse-while-busy"
                 | BoundedFifo count -> $"bounded-fifo:{count}")
            Replacement =
                (match value.Replacement with
                 | DestructiveLoad -> "destructive-load"
                 | TransactionalCandidateWithRecoveryFreeze -> "transactional-candidate-with-recovery-freeze")
        }

    let commandToWire (configuration: ValidatedConfiguration) (command: WorkerCommand) =
        let kind, bytes =
            match command.Operation with
            | InspectAndCompile(_, artifact) -> "load", artifact
            | InitializeGuest input -> "initialize", input
            | ProcessGuest(_, input) -> "process", input
            | ShutdownGuest -> "shutdown", Array.empty
            | VerifyAbiVersion -> "version", Array.empty
            | DisposeGuest -> "dispose", Array.empty

        {
            Identity = Identity.toWire command.Identity
            Correlation = OperationToken.value command.Correlation
            Kind = kind
            Configuration = configurationToWire configuration
            Bytes = Array.copy bytes
        }

    let private toWire (identity: HostIdentity) correlation observation =
        let empty =
            {
                Identity = Identity.toWire identity
                Correlation = OperationToken.value correlation
                Kind = "failed"
                Phase = "process"
                Digest = ""
                Version = 0u
                State = "faulted"
                Status = 0
                Diagnostic = ""
                CleanupDiagnostic = ""
                Dispatched = false
                Output = Array.empty
            }

        match observation with
        | PhaseObserved(_, _, phase) ->
            { empty with
                Kind = "phase"
                Phase = phaseName phase
            }
        | ArtifactDigestObserved(_, _, digest) ->
            { empty with
                Kind = "digest"
                Digest = digest
            }
        | AbiVersionObserved(_, _, version) ->
            { empty with
                Kind = "loaded"
                Version = version
                Phase = "instantiate"
            }
        | InvocationObserved(outcome, _)
        | InvocationTerminated(outcome, _) ->
            { empty with
                Kind =
                    (match observation with
                     | InvocationTerminated _ -> "outcome-terminated"
                     | _ -> "outcome")
                Phase = phaseName outcome.Phase
                State =
                    (match outcome.State with
                     | Succeeded -> "succeeded"
                     | GuestRejected _ -> "rejected"
                     | TimedOut -> "timeout"
                     | _ -> "faulted")
                Status =
                    (match outcome.State with
                     | GuestRejected status -> status
                     | _ -> 0)
                Diagnostic =
                    (match outcome.State with
                     | Faulted diagnostic -> diagnostic
                     | _ -> "")
                CleanupDiagnostic = outcome.CleanupFault |> Option.map _.Diagnostic |> Option.defaultValue ""
                Dispatched = outcome.Dispatch = Dispatched
                Output = Array.copy outcome.CopiedOutput
            }
        | WorkerFailed(_, _, diagnostic) -> { empty with Diagnostic = diagnostic }
        | _ ->
            { empty with
                Diagnostic = "unsupported worker observation"
            }

    let observationFromWire (wire: WorkerWireObservation) =
        try
            match Identity.parse wire.Identity, OperationToken.create wire.Correlation, parsePhase wire.Phase with
            | Ok identity, Ok correlation, Some phase ->
                match wire.Kind with
                | "phase" -> Ok(PhaseObserved(identity, correlation, phase))
                | "digest" -> Ok(ArtifactDigestObserved(identity, correlation, wire.Digest))
                | "loaded" -> Ok(AbiVersionObserved(identity, correlation, wire.Version))
                | "failed" -> Ok(WorkerFailed(identity, Some correlation, wire.Diagnostic))
                | "outcome"
                | "outcome-terminated" ->
                    let state =
                        match wire.State with
                        | "succeeded" -> Some Succeeded
                        | "rejected" -> Some(GuestRejected wire.Status)
                        | "timeout" -> Some TimedOut
                        | "faulted" -> Some(Faulted wire.Diagnostic)
                        | _ -> None

                    match state with
                    | None -> Error "unknown terminal worker state"
                    | Some state ->
                        let constructor =
                            if wire.Kind = "outcome-terminated" then
                                InvocationTerminated
                            else
                                InvocationObserved

                        Ok(
                            constructor (
                                {
                                    Identity = identity
                                    Phase = phase
                                    State = state
                                    Dispatch = (if wire.Dispatched then Dispatched else NotDispatched)
                                    CleanupFault =
                                        (if wire.CleanupDiagnostic = "" then
                                             None
                                         else
                                             Some
                                                 {
                                                     Phase = Free
                                                     Diagnostic = wire.CleanupDiagnostic
                                                 })
                                    CopiedOutput = Array.copy wire.Output
                                    EffectEligibility = ProductAdapterMustDecide
                                },
                                correlation
                            )
                        )
                | _ -> Error "unknown worker observation"
            | _ -> Error "malformed worker observation identity/correlation/phase"
        with _ ->
            Error "malformed worker observation"

    /// Installs one authoritative F# command receiver in an actual module Worker.
    let createWireRuntime (native: WorkerWireMechanics) emit =
        let mutable loaded: (ValidatedConfiguration * HostIdentity) option = None
        let mutable busy = false

        let perform configuration effect =
            async {
                let abi = (Validation.descriptor configuration).Abi

                let empty =
                    {
                        Kind = ""
                        Export = ""
                        Pointer = 0u
                        Length = 0u
                        Descriptor = 0u
                        Bytes = Array.empty
                    }

                let mechanical =
                    match effect with
                    | Allocate(_, length) ->
                        { empty with
                            Kind = "allocate"
                            Export = abi.AllocateExport
                            Length = length
                        }
                    | ZeroDescriptor span ->
                        { empty with
                            Kind = "zero"
                            Pointer = uint32 span.Pointer
                            Length = uint32 span.Length
                        }
                    | CopyInput(span, bytes) ->
                        { empty with
                            Kind = "copy-input"
                            Pointer = uint32 span.Pointer
                            Bytes = bytes
                        }
                    | InvokeGuest(call, input, descriptor) ->
                        { empty with
                            Kind = "invoke"
                            Export =
                                (if call = InitializeCall then
                                     abi.InitializeExport
                                 else
                                     abi.ProcessExport)
                            Pointer = uint32 input.Pointer
                            Length = uint32 input.Length
                            Descriptor = uint32 descriptor.Pointer
                        }
                    | ReadDescriptor span ->
                        { empty with
                            Kind = "read"
                            Pointer = uint32 span.Pointer
                        }
                    | CopyOutput span ->
                        { empty with
                            Kind = "copy-output"
                            Pointer = uint32 span.Pointer
                            Length = uint32 span.Length
                        }
                    | FreeOwned span ->
                        { empty with
                            Kind = "free"
                            Export = abi.FreeExport
                            Pointer = uint32 span.Pointer
                            Length = uint32 span.Length
                        }
                    | TerminateGuest -> { empty with Kind = "terminate" }
                    | InvocationCompleted _ -> empty

                let reply = native.Perform mechanical

                let memory =
                    match System.UInt64.TryParse reply.MemoryBytes with
                    | true, value when string value = reply.MemoryBytes -> value
                    | _ -> 0UL

                let issue = reply.Diagnostic

                let event =
                    match effect with
                    | Allocate(purpose, _) ->
                        Some(
                            if issue = "" then
                                AllocationReturned(purpose, reply.Pointer, memory)
                            else
                                AllocationTrapped(purpose, issue)
                        )
                    | InvokeGuest _ ->
                        Some(
                            if issue = "" then
                                GuestReturned(reply.Status, memory)
                            else
                                GuestTrapped(issue, memory)
                        )
                    | ReadDescriptor _ ->
                        Some(
                            if issue = "" then
                                DescriptorRead(reply.OutputPointer, reply.OutputLength, memory)
                            else
                                DescriptorReadFailed issue
                        )
                    | CopyOutput _ ->
                        Some(
                            if issue = "" then
                                OutputCopied reply.Bytes
                            else
                                OutputCopyFailed issue
                        )
                    | FreeOwned span ->
                        Some(
                            if issue = "" then
                                FreeReturned(span, memory)
                            else
                                FreeTrapped(span, issue)
                        )
                    | _ -> None

                if issue <> "" && event.IsNone then
                    return Error issue
                else
                    return Ok event
            }

        fun (wire: WorkerWireCommand) ->
            let work =
                async {
                    match
                        Identity.parse wire.Identity,
                        OperationToken.create wire.Correlation,
                        Validation.validateBoundary wire.Configuration
                    with
                    | Ok identity, Ok correlation, Ok configuration ->
                        let observe value =
                            match value with
                            | InvocationTerminated _ -> loaded <- None
                            | InvocationObserved(outcome, _) when
                                outcome.CleanupFault.IsSome
                                || (match outcome.State with
                                    | Faulted _
                                    | TimedOut -> true
                                    | _ -> false)
                                ->
                                loaded <- None
                            | _ -> ()

                            emit (toWire identity correlation value)

                        if busy then
                            observe (WorkerFailed(identity, Some correlation, "worker already executing"))
                        else
                            busy <- true

                            try
                                if wire.Kind = "load" then
                                    observe (PhaseObserved(identity, correlation, Compile))

                                    match Admission.inspect configuration wire.Bytes with
                                    | Error issues ->
                                        observe (
                                            WorkerFailed(identity, Some correlation, $"admission refused: {issues}")
                                        )
                                    | Ok _ ->
                                        let! digest =
                                            Async.FromContinuations(fun (ok, error, _) ->
                                                native.Sha256 wire.Bytes ok (System.Exception >> error))

                                        if digest <> (Validation.configuration configuration).ArtifactSha256 then
                                            observe (
                                                WorkerFailed(identity, Some correlation, "artifact digest mismatch")
                                            )
                                        else
                                            observe (ArtifactDigestObserved(identity, correlation, digest))
                                            observe (PhaseObserved(identity, correlation, Instantiate))
                                            let abi = (Validation.descriptor configuration).Abi

                                            let! version =
                                                Async.FromContinuations(fun (ok, error, _) ->
                                                    native.Compile
                                                        wire.Bytes
                                                        abi.MemoryExport
                                                        abi.VersionExport
                                                        ok
                                                        (System.Exception >> error))

                                            if version <> abi.Version then
                                                observe (
                                                    WorkerFailed(identity, Some correlation, "ABI version mismatch")
                                                )
                                            else
                                                loaded <- Some(configuration, identity)
                                                observe (AbiVersionObserved(identity, correlation, version))
                                else
                                    match loaded with
                                    | Some(current, owner) when
                                        (let ceiling = Validation.configuration current
                                         let selected = Validation.configuration configuration
                                         selected.Limits.MaximumDeadlineMilliseconds > 0
                                         && selected.Limits.MaximumDeadlineMilliseconds <= ceiling.Limits.MaximumDeadlineMilliseconds
                                         && selected.Limits.MaximumOutputBytes > 0
                                         && selected.Limits.MaximumOutputBytes <= ceiling.Limits.MaximumOutputBytes
                                         && { selected with Limits = ceiling.Limits } = ceiling
                                         && { selected.Limits with MaximumDeadlineMilliseconds = ceiling.Limits.MaximumDeadlineMilliseconds; MaximumOutputBytes = ceiling.Limits.MaximumOutputBytes } = ceiling.Limits)
                                        && owner.WorkerInstance = identity.WorkerInstance
                                        && owner.Generation = identity.Generation
                                        ->
                                        let mechanics =
                                            {
                                                Sha256Hex = (fun _ -> async { return Error "already loaded" })
                                                CompileAfterAdmission =
                                                    (fun _ -> async { return Error "already loaded" })
                                                PerformInvocationEffect = perform configuration
                                            }

                                        match wire.Kind with
                                        | "initialize"
                                        | "process" ->
                                            let! _ =
                                                driveObserved
                                                    observe
                                                    mechanics
                                                    configuration
                                                    identity
                                                    correlation
                                                    (if wire.Kind = "initialize" then
                                                         InitializeCall
                                                     else
                                                         ProcessCall)
                                                    wire.Bytes

                                            ()
                                        | "shutdown" ->
                                            observe (PhaseObserved(identity, correlation, Shutdown))

                                            let response =
                                                native.Perform
                                                    {
                                                        Kind = "shutdown"
                                                        Export =
                                                            (Validation.descriptor configuration).Abi.ShutdownExport
                                                        Pointer = 0u
                                                        Length = 0u
                                                        Descriptor = 0u
                                                        Bytes = Array.empty
                                                    }

                                            loaded <- None

                                            let state =
                                                if response.Diagnostic <> "" then
                                                    Faulted response.Diagnostic
                                                elif response.Status = 0 then
                                                    Succeeded
                                                else
                                                    GuestRejected response.Status

                                            observe (
                                                InvocationObserved(
                                                    {
                                                        Identity = identity
                                                        Phase = Shutdown
                                                        State = state
                                                        Dispatch = Dispatched
                                                        CleanupFault = None
                                                        CopiedOutput = Array.empty
                                                        EffectEligibility = ProductAdapterMustDecide
                                                    },
                                                    correlation
                                                )
                                            )
                                        | _ ->
                                            observe (
                                                WorkerFailed(identity, Some correlation, "unsupported worker operation")
                                            )
                                    | _ ->
                                        observe (
                                            WorkerFailed(
                                                identity,
                                                Some correlation,
                                                "worker configuration/generation is not loaded"
                                            )
                                        )
                            with error ->
                                observe (WorkerFailed(identity, Some correlation, error.Message))

                            busy <- false
                    | _ ->
                        emit
                            {
                                Identity = wire.Identity
                                Correlation = wire.Correlation
                                Kind = "failed"
                                Phase = "process"
                                Digest = ""
                                Version = 0u
                                State = "faulted"
                                Status = 0
                                Diagnostic = "malformed command identity/configuration"
                                CleanupDiagnostic = ""
                                Dispatched = false
                                Output = Array.empty
                            }
                }

            Async.StartImmediate work

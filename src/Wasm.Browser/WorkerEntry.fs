namespace FS.GG.Wasm.Browser

open FS.GG.Wasm.Contracts

type WorkerMechanics =
    { Sha256Hex: byte array -> Async<Result<string, string>>
      CompileAfterAdmission: byte array -> Async<Result<unit, string>>
      PerformInvocationEffect: InvocationEffect -> Async<Result<InvocationEvent option, string>> }

[<RequireQualifiedAccess>]
module WorkerEntry =
    let private failed identity correlation diagnostic =
        [ WorkerFailed(identity, Some correlation, diagnostic) ]

    let inspectAndCompile (mechanics: WorkerMechanics) (identity: HostIdentity) correlation (configuration: ValidatedConfiguration) (artifact: byte array) = async {
        match Admission.inspect configuration artifact with
        | Error issues -> return failed identity correlation ($"admission refused: {issues}")
        | Ok _ ->
            match! mechanics.Sha256Hex artifact with
            | Error diagnostic -> return failed identity correlation diagnostic
            | Ok digest ->
                let expected = (Validation.configuration configuration).ArtifactSha256
                if digest <> expected then return failed identity correlation "artifact digest mismatch"
                else
                    match! mechanics.CompileAfterAdmission artifact with
                    | Error diagnostic -> return failed identity correlation diagnostic
                    | Ok () ->
                        return [ PhaseObserved(identity, correlation, Compile); ArtifactDigestObserved(identity, correlation, digest) ] }

    let driveInvocation (mechanics: WorkerMechanics) (configuration: ValidatedConfiguration) (identity: HostIdentity) correlation call (input: byte array) = async {
        let mutable state, effects = Invocation.start configuration identity call input
        let observations = ResizeArray<WorkerObservation>()
        let mutable stopped = false
        let mutable lastPhase = None
        while not stopped && not effects.IsEmpty do
            let effect = effects.Head
            effects <- effects.Tail
            match effect with
            | InvocationCompleted outcome ->
                observations.Add(InvocationObserved(outcome, correlation))
                stopped <- true
            | _ ->
                let phase =
                    match effect with
                    | Allocate(DescriptorAllocation, _) -> Some AllocateDescriptor
                    | Allocate(InputAllocation, _) -> Some AllocateInput
                    | InvokeGuest(InitializeCall, _, _) -> Some Initialize
                    | InvokeGuest(ProcessCall, _, _) -> Some Process
                    | FreeOwned _ -> Some Free
                    | _ -> None
                match phase with
                | Some value when lastPhase <> Some value -> observations.Add(PhaseObserved(identity, correlation, value)); lastPhase <- Some value
                | _ -> ()
                match! mechanics.PerformInvocationEffect effect with
                | Error diagnostic ->
                    observations.Add(WorkerFailed(identity, Some correlation, diagnostic))
                    stopped <- true
                | Ok None -> ()
                | Ok(Some event) ->
                    let next, emitted = Invocation.update event state
                    state <- next
                    effects <- effects @ emitted
        if not stopped && not (Invocation.isTerminal state) then
            observations.Add(WorkerFailed(identity, Some correlation, "invocation mechanics ended before settlement"))
        return List.ofSeq observations }

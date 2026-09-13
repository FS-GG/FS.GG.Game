namespace FS.GG.Game.Core

open System
open System.Text

type NetworkClientBinding =
    { SessionId: string
      ClientId: string
      ReconnectToken: string }

type NetworkInput<'input> =
    { Binding: NetworkClientBinding
      Input: SessionInput<'input> }

type AcceptedNetworkInput<'input> =
    { AcceptedOrder: uint64
      ClientId: string
      Input: SessionInput<'input> }

type NetworkAdmissionState<'input> =
    private
    | NetworkAdmissionState of string * NetworkClientBinding list * Map<string, uint64> * AcceptedNetworkInput<'input> list

[<RequireQualifiedAccess>]
type NetworkAdmissionIssue =
    | MissingSessionId
    | MissingClientId
    | MissingReconnectToken of clientId: string
    | DuplicateClientId of string
    | ClientAlreadyBound of string
    | WrongSession of expected: string * actual: string
    | ClientNotBound of string
    | ReconnectTokenMismatch of clientId: string
    | InvalidInput of SessionContractIssue list
    | InputSessionMismatch of bindingSession: string * inputSession: string
    | DuplicateInputSequence of clientId: string * sequence: uint64
    | StaleInputSequence of clientId: string * accepted: uint64 * candidate: uint64
    | PayloadRefused of code: string

[<RequireQualifiedAccess>]
type NetworkResyncDecision =
    | Current of revision: uint64
    | ReplaySuffix of afterRevision: uint64 * currentRevision: uint64
    | FullSnapshot of currentRevision: uint64 * reason: string
    | ClientAhead of clientRevision: uint64 * currentRevision: uint64

[<RequireQualifiedAccess>]
module NetworkAdmission =
    let private missing value = String.IsNullOrWhiteSpace value

    let private bindingIssues sessionId (binding: NetworkClientBinding) =
        [ if missing binding.ClientId then NetworkAdmissionIssue.MissingClientId
          if missing binding.ReconnectToken then NetworkAdmissionIssue.MissingReconnectToken binding.ClientId
          if binding.SessionId <> sessionId then NetworkAdmissionIssue.WrongSession(sessionId, binding.SessionId) ]

    let create (sessionId: string) (bindings: NetworkClientBinding list) =
        let duplicateIds =
            bindings
            |> List.groupBy _.ClientId
            |> List.choose (fun (clientId, values) -> if values.Length > 1 then Some clientId else None)
            |> List.sort
        let issues =
            [ if missing sessionId then NetworkAdmissionIssue.MissingSessionId
              for binding in bindings do
                  yield! bindingIssues sessionId binding
              for clientId in duplicateIds do NetworkAdmissionIssue.DuplicateClientId clientId ]
        if issues.IsEmpty then Ok(NetworkAdmissionState(sessionId, bindings, Map.empty, [])) else Error issues

    let bind binding (NetworkAdmissionState(sessionId, bindings, sequences, accepted)) =
        let issues =
            [ yield! bindingIssues sessionId binding
              if bindings |> List.exists (fun current -> current.ClientId = binding.ClientId) then
                  NetworkAdmissionIssue.ClientAlreadyBound binding.ClientId ]
        if issues.IsEmpty then
            Ok(NetworkAdmissionState(sessionId, bindings @ [ binding ], sequences, accepted))
        else
            Error issues

    let admit validatePayload (candidate: NetworkInput<'input>) (state: NetworkAdmissionState<'input>) =
        let (NetworkAdmissionState(sessionId, bindings, sequences, accepted)) = state
        if candidate.Binding.SessionId <> sessionId then Error(NetworkAdmissionIssue.WrongSession(sessionId, candidate.Binding.SessionId))
        else
            match bindings |> List.tryFind (fun binding -> binding.ClientId = candidate.Binding.ClientId) with
            | None -> Error(NetworkAdmissionIssue.ClientNotBound candidate.Binding.ClientId)
            | Some expected when expected.ReconnectToken <> candidate.Binding.ReconnectToken ->
                Error(NetworkAdmissionIssue.ReconnectTokenMismatch candidate.Binding.ClientId)
            | Some _ ->
                let envelopeIssues = SessionEnvelope.validateInput candidate.Input
                if not envelopeIssues.IsEmpty then Error(NetworkAdmissionIssue.InvalidInput envelopeIssues)
                elif candidate.Input.SessionId <> candidate.Binding.SessionId then
                    Error(NetworkAdmissionIssue.InputSessionMismatch(candidate.Binding.SessionId, candidate.Input.SessionId))
                else
                    match Map.tryFind candidate.Binding.ClientId sequences with
                    | Some previous when candidate.Input.Sequence = previous ->
                        Error(NetworkAdmissionIssue.DuplicateInputSequence(candidate.Binding.ClientId, candidate.Input.Sequence))
                    | Some previous when candidate.Input.Sequence < previous ->
                        Error(NetworkAdmissionIssue.StaleInputSequence(candidate.Binding.ClientId, previous, candidate.Input.Sequence))
                    | _ ->
                        match validatePayload candidate.Input.Value with
                        | Error code -> Error(NetworkAdmissionIssue.PayloadRefused code)
                        | Ok () ->
                            let acceptedInput =
                                { AcceptedOrder = uint64 accepted.Length
                                  ClientId = candidate.Binding.ClientId
                                  Input = candidate.Input }
                            let next = NetworkAdmissionState(sessionId, bindings, Map.add candidate.Binding.ClientId candidate.Input.Sequence sequences, accepted @ [ acceptedInput ])
                            Ok(next, acceptedInput)

    let accepted (NetworkAdmissionState(_, _, _, values)) = values
    let lastSequence clientId (NetworkAdmissionState(_, _, values, _)) = Map.tryFind clientId values

    let private appendField (builder: StringBuilder) (value: string) =
        builder.Append(value.Length).Append(':').Append(value).Append('|') |> ignore

    let canonicalText encodeInput (NetworkAdmissionState(sessionId, _, _, values)) =
        let builder = StringBuilder()
        builder.Append("fsgg-network-accepted/1|") |> ignore
        appendField builder sessionId
        for value in values do
            builder.Append(value.AcceptedOrder).Append('|') |> ignore
            appendField builder value.ClientId
            appendField builder value.Input.InputId
            builder.Append(value.Input.Sequence).Append('|') |> ignore
            appendField builder (encodeInput value.Input.Value)
        builder.ToString()

    let resync retainedAfterRevision clientRevision currentRevision =
        if clientRevision > currentRevision then NetworkResyncDecision.ClientAhead(clientRevision, currentRevision)
        elif clientRevision = currentRevision then NetworkResyncDecision.Current currentRevision
        else
            match retainedAfterRevision with
            | Some oldest when clientRevision >= oldest -> NetworkResyncDecision.ReplaySuffix(clientRevision, currentRevision)
            | Some oldest -> NetworkResyncDecision.FullSnapshot(currentRevision, $"revision {clientRevision} predates retained suffix {oldest}")
            | None -> NetworkResyncDecision.FullSnapshot(currentRevision, "no retained suffix is available")

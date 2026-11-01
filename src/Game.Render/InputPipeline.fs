namespace FS.GG.Game.Render

// See InputPipeline.fsi and docs/adr/0001-standard-input-pipeline.md (FS.GG.Game#787). Implemented by FS.GG.Game#788;
// the laws are tests/Game.Render.Pipeline.Laws, run on .NET and Fable with identical output.

[<RequireQualifiedAccess>]
type PipelineInput<'Input> =
    | Scene of event: InputEvent
    | Game of input: 'Input

[<RequireQualifiedAccess>]
type GuestIntent<'Local, 'Command> =
    | Local of intent: 'Local
    | Submit of command: 'Command

[<RequireQualifiedAccess>]
type GuestOutcome<'Local, 'Command> =
    | Decided of intents: GuestIntent<'Local, 'Command> list
    | Refused of reason: string
    | Faulted of reason: string
    | TimedOut
    | Cancelled of reason: string

[<RequireQualifiedAccess>]
type GuestReply =
    | Output of bytes: byte array
    | Refused of status: int
    | Faulted of diagnostic: string
    | TimedOut
    | Invalidated
    | Frozen

[<NoEquality; NoComparison>]
type GuestPolicy =
    {
        StartTimeout: int
        Timeout: int
        QueueLimit: int
        Breaker: int
    }

[<RequireQualifiedAccess>]
module GuestPolicy =

    let standard =
        {
            StartTimeout = 10000
            Timeout = 500
            QueueLimit = 16
            Breaker = 3
        }

    let withStartTimeout (milliseconds: int) (policy: GuestPolicy) =
        if milliseconds <= 0 then
            invalidArg (nameof milliseconds) "A start deadline must be greater than zero."

        { policy with
            StartTimeout = milliseconds
        }

    let withTimeout (milliseconds: int) (policy: GuestPolicy) =
        if milliseconds <= 0 then
            invalidArg (nameof milliseconds) "A guest deadline must be greater than zero."

        { policy with Timeout = milliseconds }

    let withQueueLimit (limit: int) (policy: GuestPolicy) =
        if limit < 1 then
            invalidArg (nameof limit) "A guest queue holds at least one input."

        { policy with QueueLimit = limit }

    let withBreaker (failures: int) (policy: GuestPolicy) =
        if failures < 1 then
            invalidArg (nameof failures) "A breaker trips after at least one failure."

        { policy with Breaker = failures }

    let startTimeout (policy: GuestPolicy) = policy.StartTimeout

    let timeout (policy: GuestPolicy) = policy.Timeout

    let queueLimit (policy: GuestPolicy) = policy.QueueLimit

    let breaker (policy: GuestPolicy) = policy.Breaker

[<NoEquality; NoComparison>]
type GuestCall<'Input> =
    {
        Id: uint64
        Sequence: uint64
        Input: PipelineInput<'Input>
        Basis: uint64
    }

[<RequireQualifiedAccess>]
module GuestCall =

    let id (call: GuestCall<'Input>) = call.Id

    let sequence (call: GuestCall<'Input>) = call.Sequence

    let input (call: GuestCall<'Input>) = call.Input

    let basis (call: GuestCall<'Input>) = call.Basis

[<NoEquality; NoComparison>]
type GuestRequest<'Input, 'Client> =
    {
        Call: GuestCall<'Input>
        Client: 'Client
    }

[<RequireQualifiedAccess>]
module GuestRequest =

    let create (call: GuestCall<'Input>) (client: 'Client) = { Call = call; Client = client }

    let call (request: GuestRequest<'Input, 'Client>) = request.Call

    let client (request: GuestRequest<'Input, 'Client>) = request.Client

[<NoEquality; NoComparison>]
type GuestKind<'Input, 'Client, 'Local, 'Command> =
    | Sync of (GuestRequest<'Input, 'Client> -> GuestOutcome<'Local, 'Command>)
    | Async of
        (GuestRequest<'Input, 'Client> -> System.Threading.CancellationToken -> Async<GuestOutcome<'Local, 'Command>>)
    | Bytes of
        encode: (GuestRequest<'Input, 'Client> -> byte array) *
        invoke: (byte array -> (unit -> unit) -> System.Threading.CancellationToken -> Async<GuestReply>) *
        decode: (byte array -> Result<GuestIntent<'Local, 'Command> list, string>)

[<NoEquality; NoComparison>]
type InputGuest<'Input, 'Client, 'Local, 'Command> =
    {
        Kind: GuestKind<'Input, 'Client, 'Local, 'Command>
        Policy: GuestPolicy
        Admit: ('Client -> Result<unit, string>) option
    }

[<RequireQualifiedAccess>]
module InputGuest =

    let sync decide =
        {
            Kind = Sync decide
            Policy = GuestPolicy.standard
            Admit = None
        }

    let ofAsync decide (policy: GuestPolicy) =
        {
            Kind = Async decide
            Policy = policy
            Admit = None
        }

    let ofBytes encode invoke decode (policy: GuestPolicy) =
        {
            Kind = Bytes(encode, invoke, decode)
            Policy = policy
            Admit = None
        }

    let withAdmission admit (guest: InputGuest<'Input, 'Client, 'Local, 'Command>) = { guest with Admit = Some admit }

    let isAsync (guest: InputGuest<'Input, 'Client, 'Local, 'Command>) =
        match guest.Kind with
        | Sync _ -> false
        | Async _
        | Bytes _ -> true

    let policy (guest: InputGuest<'Input, 'Client, 'Local, 'Command>) = guest.Policy

    let run
        (guest: InputGuest<'Input, 'Client, 'Local, 'Command>)
        (request: GuestRequest<'Input, 'Client>)
        (started: unit -> unit)
        (token: System.Threading.CancellationToken)
        : Async<GuestOutcome<'Local, 'Command>> =
        let faulted (error: exn) = GuestOutcome.Faulted error.Message

        let ofReply decode reply =
            match reply with
            | GuestReply.Output bytes ->
                match
                    (try
                        decode bytes
                     with error ->
                         Error error.Message)
                with
                | Ok intents -> GuestOutcome.Decided intents
                | Error reason -> GuestOutcome.Faulted reason
            | GuestReply.Refused status -> GuestOutcome.Refused(sprintf "status %d" status)
            | GuestReply.Faulted diagnostic -> GuestOutcome.Faulted diagnostic
            | GuestReply.TimedOut -> GuestOutcome.TimedOut
            | GuestReply.Invalidated -> GuestOutcome.Cancelled "invalidated"
            | GuestReply.Frozen -> GuestOutcome.Cancelled "frozen"

        let decided () : Async<GuestOutcome<'Local, 'Command>> =
            match guest.Kind with
            | Sync decide ->
                started ()
                async { return decide request }
            | Async decide ->
                started ()
                decide request token
            | Bytes(encode, invoke, decode) ->
                let bytes = encode request

                async {
                    let! reply = invoke bytes started token
                    return ofReply decode reply
                }

        async {
            let admitted =
                match guest.Admit with
                | None -> Choice1Of3()
                | Some admit ->
                    try
                        match admit request.Client with
                        | Ok() -> Choice1Of3()
                        | Error reason -> Choice2Of3 reason
                    with error ->
                        Choice3Of3 error.Message

            match admitted with
            | Choice2Of3 reason -> return GuestOutcome.Refused reason
            | Choice3Of3 reason -> return GuestOutcome.Faulted reason
            | Choice1Of3() ->
                let! outcome =
                    async {
                        try
                            return! decided ()
                        with error ->
                            return faulted error
                    }
                    |> Async.Catch

                return
                    match outcome with
                    | Choice1Of2 outcome when token.IsCancellationRequested ->
                        match outcome with
                        | GuestOutcome.Decided _ -> GuestOutcome.Cancelled "cancelled"
                        | other -> other
                    | Choice1Of2 outcome -> outcome
                    | Choice2Of2 error -> faulted error
        }

type CommandId =
    private
        {
            Client: string
            Sequence: uint64
        }

[<RequireQualifiedAccess>]
module CommandId =

    let client (id: CommandId) = id.Client

    let sequence (id: CommandId) = id.Sequence

    let text (id: CommandId) = sprintf "%s:%d" id.Client id.Sequence

    let tryParse (text: string) : CommandId option =
        if System.String.IsNullOrEmpty text then
            None
        else
            let colon = text.LastIndexOf ':'

            if colon <= 0 then
                None
            else
                let client = text.Substring(0, colon)
                let digits = text.Substring(colon + 1)

                if
                    System.String.IsNullOrWhiteSpace client
                    || digits.Length = 0
                    || not (digits |> Seq.forall (fun c -> c >= '0' && c <= '9'))
                then
                    None
                else
                    match System.UInt64.TryParse digits with
                    | true, sequence when sequence >= 1UL -> Some { Client = client; Sequence = sequence }
                    | _ -> None

[<NoEquality; NoComparison>]
type CommandEnvelope<'Command> =
    {
        Id: CommandId
        Input: uint64 option
        Basis: uint64
        Command: 'Command
    }

[<RequireQualifiedAccess>]
module CommandEnvelope =

    let id (envelope: CommandEnvelope<'Command>) = envelope.Id

    let input (envelope: CommandEnvelope<'Command>) = envelope.Input

    let basis (envelope: CommandEnvelope<'Command>) = envelope.Basis

    let command (envelope: CommandEnvelope<'Command>) = envelope.Command

[<NoEquality; NoComparison>]
type AckKind =
    | Accepted
    | Queued
    | Started
    | Progress of note: string
    | Applied of version: uint64
    | Ended of version: uint64 * outcome: string
    | Cancelled of reason: string
    | Rejected of reason: string
    | Expired

/// Opaque behind the .fsi, so an answer added later is not a breaking change.
[<NoEquality; NoComparison>]
type CommandAck = { Kind: AckKind }

[<RequireQualifiedAccess>]
module CommandAck =

    let accepted = { Kind = AckKind.Accepted }

    let queued = { Kind = AckKind.Queued }

    let started = { Kind = Started }

    let progress (note: string) = { Kind = Progress note }

    let applied (version: uint64) = { Kind = Applied version }

    let ended (version: uint64) (outcome: string) = { Kind = Ended(version, outcome) }

    let cancelled (reason: string) = { Kind = Cancelled reason }

    let rejected (reason: string) = { Kind = Rejected reason }

    let expired = { Kind = Expired }

[<NoEquality; NoComparison>]
type SettlementKind =
    | Pending
    | Accepted
    | Queued
    | Running
    | AppliedAt of version: uint64 * outcome: string option
    | CancelledAfter of reason: string
    | RejectedBefore of reason: string
    | Unconfirmed

/// Opaque behind the .fsi, so a state added later is not a breaking change.
[<NoEquality; NoComparison>]
type CommandSettlement = { State: SettlementKind }

[<RequireQualifiedAccess>]
module CommandSettlement =

    let isPending (settlement: CommandSettlement) =
        match settlement.State with
        | Pending -> true
        | _ -> false

    let isAccepted (settlement: CommandSettlement) =
        match settlement.State with
        | SettlementKind.Accepted
        | SettlementKind.Queued
        | Running -> true
        | _ -> false

    let isQueued (settlement: CommandSettlement) =
        match settlement.State with
        | SettlementKind.Queued -> true
        | _ -> false

    let isRunning (settlement: CommandSettlement) =
        match settlement.State with
        | Running -> true
        | _ -> false

    let appliedAt (settlement: CommandSettlement) =
        match settlement.State with
        | AppliedAt(version, _) -> Some version
        | _ -> None

    let outcome (settlement: CommandSettlement) =
        match settlement.State with
        | AppliedAt(_, outcome) -> outcome
        | _ -> None

    let cancelled (settlement: CommandSettlement) =
        match settlement.State with
        | CancelledAfter reason -> Some reason
        | _ -> None

    let rejected (settlement: CommandSettlement) =
        match settlement.State with
        | RejectedBefore reason -> Some reason
        | _ -> None

    let isUnconfirmed (settlement: CommandSettlement) =
        match settlement.State with
        | Unconfirmed -> true
        | _ -> false

    let isFinal (settlement: CommandSettlement) =
        match settlement.State with
        | Pending
        | SettlementKind.Accepted
        | SettlementKind.Queued
        | Running -> false
        | _ -> true

[<NoEquality; NoComparison>]
type CommandStage<'Server, 'Command> =
    {
        Version: 'Server -> uint64
        Window: int
        Patience: uint64
        Settled: ('Command -> 'Server -> bool) option
        Predict: ('Command -> 'Server -> 'Server) option
    }

[<RequireQualifiedAccess>]
module CommandStage =

    let create (version: 'Server -> uint64) : CommandStage<'Server, 'Command> =
        {
            Version = version
            Window = 8
            Patience = 600UL
            Settled = None
            Predict = None
        }

    let withWindow (limit: int) (stage: CommandStage<'Server, 'Command>) =
        if limit < 1 then
            invalidArg (nameof limit) "A command window holds at least one command."

        { stage with Window = limit }

    let withPatience (patience: uint64) (stage: CommandStage<'Server, 'Command>) =
        if patience < 1UL then
            invalidArg (nameof patience) "Patience is at least one version."

        { stage with Patience = patience }

    let withSettlement settled (stage: CommandStage<'Server, 'Command>) = { stage with Settled = Some settled }

    let withPrediction predict (stage: CommandStage<'Server, 'Command>) = { stage with Predict = Some predict }

[<NoEquality; NoComparison>]
type Displayed<'Server> =
    {
        Authoritative: 'Server
        Shown: 'Server
        Predicted: CommandId list
    }

[<RequireQualifiedAccess>]
module Displayed =

    let authoritative (displayed: Displayed<'Server>) = displayed.Authoritative

    let shown (displayed: Displayed<'Server>) = displayed.Shown

    let predicted (displayed: Displayed<'Server>) = displayed.Predicted

[<RequireQualifiedAccess>]
type PipelineFailure =
    | GuestFaulted of sequence: uint64 * reason: string
    | GuestTimedOut of sequence: uint64
    | GuestHalted of failures: int
    | InputRefused of sequence: uint64 * reason: string
    | InputCancelled of sequence: uint64 * reason: string
    | QueueOverflow of dropped: uint64 list
    | CommandWindowFull of sequence: uint64 option
    | CommandRejected of id: CommandId * reason: string
    | CommandCancelled of id: CommandId * reason: string
    | CommandUnconfirmed of id: CommandId
    | RebindRefused of reason: string

[<RequireQualifiedAccess>]
type PipelineMsg<'Input, 'Server, 'Local, 'Command> =
    | Input of input: PipelineInput<'Input>
    | Submit of command: 'Command
    | StartNext
    | GuestRunning of call: uint64
    | GuestStartDeadline of call: uint64
    | GuestEnded of call: uint64 * outcome: GuestOutcome<'Local, 'Command>
    | GuestDeadline of call: uint64
    | RearmGuest
    | Acknowledged of id: CommandId * ack: CommandAck
    | Authoritative of state: 'Server
    | Heartbeat of version: uint64
    | Rebind of client: string * lastSequence: uint64 option

[<RequireQualifiedAccess>]
type PipelineEffect<'Input, 'Local, 'Command> =
    | StartGuest of call: GuestCall<'Input>
    | CancelGuest of call: uint64
    | ArmStartDeadline of call: uint64 * milliseconds: int
    | ArmDeadline of call: uint64 * milliseconds: int
    | Continue
    | ApplyLocal of intent: 'Local
    | Send of envelope: CommandEnvelope<'Command>
    | Fail of failure: PipelineFailure

[<NoEquality; NoComparison>]
type InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command> =
    {
        Guest: InputGuest<'Input, 'Client, 'Local, 'Command>
        Commands: CommandStage<'Server, 'Command>
    }

[<NoEquality; NoComparison>]
type internal QueueItem<'Input, 'Command> =
    | QueuedInput of sequence: uint64 * input: PipelineInput<'Input>
    | QueuedSubmit of command: 'Command

[<NoEquality; NoComparison>]
type internal Flight =
    {
        Call: uint64
        Sequence: uint64
        Running: bool
    }

[<NoEquality; NoComparison>]
type internal Entry<'Command> =
    {
        Id: CommandId
        Command: 'Command
        State: SettlementKind
        /// Patience counts from here: the basis, or the version heard at the latest running answer.
        Origin: uint64
    }

/// The pipeline state (see InputPipeline.fsi). Every field is internal to the reducer.
[<NoEquality; NoComparison>]
type PipelineModel<'Input, 'Server, 'Local, 'Command> =
    internal
        {
            Client: string
            LastSequence: uint64
            NextInput: uint64
            NextCall: uint64
            Queue: QueueItem<'Input, 'Command> list
            InFlight: Flight option
            /// A call ended and its `Continue` has not come back as `StartNext` yet.
            Draining: bool
            Halted: bool
            Timeouts: int
            Authoritative: 'Server
            AuthVersion: uint64
            Heard: uint64
            Ledger: Entry<'Command> list
            Predict: ('Command -> 'Server -> 'Server) option
        }

[<RequireQualifiedAccess>]
module InputPipeline =

    let create guest commands = { Guest = guest; Commands = commands }

    let guest (pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>) = pipeline.Guest

    let commands (pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>) = pipeline.Commands

    let init
        (pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>)
        (client: string)
        (lastSequence: uint64 option)
        (authoritative: 'Server)
        : PipelineModel<'Input, 'Server, 'Local, 'Command> =
        if System.String.IsNullOrWhiteSpace client then
            invalidArg (nameof client) "A client binding must not be blank."

        let version = pipeline.Commands.Version authoritative

        {
            Client = client
            LastSequence = defaultArg lastSequence 0UL
            NextInput = 1UL
            NextCall = 1UL
            Queue = []
            InFlight = None
            Draining = false
            Halted = false
            Timeouts = 0
            Authoritative = authoritative
            AuthVersion = version
            Heard = version
            Ledger = []
            Predict = pipeline.Commands.Predict
        }

    let private isOpen (state: SettlementKind) =
        match state with
        | Pending
        | SettlementKind.Accepted
        | SettlementKind.Queued
        | Running -> true
        | _ -> false

    /// Kept while open, or while applied at a version the authoritative state has not reached (prediction needs it).
    let private keep (authVersion: uint64) (entry: Entry<'Command>) =
        match entry.State with
        | AppliedAt(version, _) -> version > authVersion
        | state -> isOpen state

    let private prune (model: PipelineModel<'Input, 'Server, 'Local, 'Command>) =
        { model with
            Ledger = model.Ledger |> List.filter (keep model.AuthVersion)
        }

    let private openCount (model: PipelineModel<'Input, 'Server, 'Local, 'Command>) =
        model.Ledger |> List.filter (fun entry -> isOpen entry.State) |> List.length

    /// Settle open commands that have run out of patience. Fails in sequence order.
    let private patience
        (stage: CommandStage<'Server, 'Command>)
        (model: PipelineModel<'Input, 'Server, 'Local, 'Command>)
        =
        let fails = ResizeArray<PipelineFailure>()

        let ledger =
            model.Ledger
            |> List.map (fun entry ->
                if
                    isOpen entry.State
                    && model.Heard >= entry.Origin
                    && model.Heard - entry.Origin >= stage.Patience
                then
                    fails.Add(PipelineFailure.CommandUnconfirmed entry.Id)
                    { entry with State = Unconfirmed }
                else
                    entry)

        { model with Ledger = ledger }, List.ofSeq fails

    /// Allocate the next command id of the current binding.
    let private issue (model: PipelineModel<'Input, 'Server, 'Local, 'Command>) (command: 'Command) =
        let sequence = model.LastSequence + 1UL

        let id =
            {
                Client = model.Client
                Sequence = sequence
            }

        let entry: Entry<'Command> =
            {
                Id = id
                Command = command
                State = Pending
                Origin = model.AuthVersion
            }

        { model with
            LastSequence = sequence
            Ledger = model.Ledger @ [ entry ]
        },
        id

    let private envelope id (input: uint64 option) (basis: uint64) (command: 'Command) : CommandEnvelope<'Command> =
        {
            Id = id
            Input = input
            Basis = basis
            Command = command
        }

    let private isGuestItem (item: QueueItem<'Input, 'Command>) =
        match item with
        | QueuedInput _ -> true
        | QueuedSubmit _ -> false

    /// Refuse every queued guest input (the stage halted or was rebound); queued submits stay.
    let private dropGuestInputs
        (fail: uint64 -> PipelineFailure)
        (model: PipelineModel<'Input, 'Server, 'Local, 'Command>)
        =
        let fails =
            model.Queue
            |> List.choose (fun item ->
                match item with
                | QueuedInput(sequence, _) -> Some(fail sequence)
                | QueuedSubmit _ -> None)

        { model with
            Queue = model.Queue |> List.filter (isGuestItem >> not)
        },
        fails

    /// Start queued work while the stage is idle: submits are sent in order, and the first guest input starts a call.
    let private startNext
        (pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>)
        (model: PipelineModel<'Input, 'Server, 'Local, 'Command>)
        =
        let sends = ResizeArray<PipelineEffect<'Input, 'Local, 'Command>>()
        let fails = ResizeArray<PipelineEffect<'Input, 'Local, 'Command>>()
        let mutable model = model
        let mutable start = []
        let mutable go = model.InFlight.IsNone && not model.Draining

        while go do
            match model.Queue with
            | [] -> go <- false
            | QueuedSubmit command :: rest ->
                model <- { model with Queue = rest }

                if openCount model >= pipeline.Commands.Window then
                    fails.Add(PipelineEffect.Fail(PipelineFailure.CommandWindowFull None))
                else
                    let next, id = issue model command
                    model <- next
                    sends.Add(PipelineEffect.Send(envelope id None model.AuthVersion command))
            | QueuedInput(sequence, input) :: rest ->
                let call: GuestCall<'Input> =
                    {
                        Id = model.NextCall
                        Sequence = sequence
                        Input = input
                        Basis = model.AuthVersion
                    }

                model <-
                    { model with
                        Queue = rest
                        NextCall = model.NextCall + 1UL
                        InFlight =
                            Some
                                {
                                    Call = call.Id
                                    Sequence = sequence
                                    Running = false
                                }
                    }

                start <-
                    [
                        PipelineEffect.StartGuest call
                        PipelineEffect.ArmStartDeadline(call.Id, pipeline.Guest.Policy.StartTimeout)
                    ]

                go <- false

        model, List.ofSeq sends, List.ofSeq fails, start

    /// End the call in flight. The batch carries the decision's effects and ends with `Continue`, never `StartGuest`.
    let private endCall
        (pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>)
        (cancel: bool)
        (outcome: GuestOutcome<'Local, 'Command>)
        (model: PipelineModel<'Input, 'Server, 'Local, 'Command>)
        =
        match model.InFlight with
        | None -> model, []
        | Some flight ->
            let model =
                { model with
                    InFlight = None
                    Draining = true
                }

            let cancels =
                if cancel then
                    [ PipelineEffect.CancelGuest flight.Call ]
                else
                    []

            let halt (model: PipelineModel<'Input, 'Server, 'Local, 'Command>) =
                dropGuestInputs
                    (fun sequence -> PipelineFailure.InputRefused(sequence, "guest halted"))
                    { model with Halted = true }

            let model, intents, fails =
                match outcome with
                | GuestOutcome.Decided intents ->
                    let submits =
                        intents
                        |> List.filter (fun intent ->
                            match intent with
                            | GuestIntent.Submit _ -> true
                            | GuestIntent.Local _ -> false)
                        |> List.length

                    let model = { model with Timeouts = 0 }

                    if submits > 0 && openCount model + submits > pipeline.Commands.Window then
                        model, [], [ PipelineFailure.CommandWindowFull(Some flight.Sequence) ]
                    else
                        let mutable model = model

                        let effects =
                            intents
                            |> List.map (fun intent ->
                                match intent with
                                | GuestIntent.Local local -> PipelineEffect.ApplyLocal local
                                | GuestIntent.Submit command ->
                                    let next, id = issue model command
                                    model <- next
                                    PipelineEffect.Send(envelope id (Some flight.Sequence) model.AuthVersion command))

                        model, effects, []
                | GuestOutcome.Refused reason ->
                    { model with Timeouts = 0 }, [], [ PipelineFailure.InputRefused(flight.Sequence, reason) ]
                | GuestOutcome.Cancelled reason ->
                    model, [], [ PipelineFailure.InputCancelled(flight.Sequence, reason) ]
                | GuestOutcome.Faulted reason ->
                    let model, refused = halt model
                    model, [], PipelineFailure.GuestFaulted(flight.Sequence, reason) :: refused
                | GuestOutcome.TimedOut ->
                    let timeouts = model.Timeouts + 1
                    let model = { model with Timeouts = timeouts }

                    if timeouts >= pipeline.Guest.Policy.Breaker then
                        let model, refused = halt model

                        model,
                        [],
                        PipelineFailure.GuestTimedOut flight.Sequence
                        :: PipelineFailure.GuestHalted timeouts
                        :: refused
                    else
                        model, [], [ PipelineFailure.GuestTimedOut flight.Sequence ]

            model,
            cancels
            @ intents
            @ (fails |> List.map PipelineEffect.Fail)
            @ [ PipelineEffect.Continue ]

    let private current (model: PipelineModel<'Input, 'Server, 'Local, 'Command>) (call: uint64) =
        match model.InFlight with
        | Some flight when flight.Call = call -> Some flight
        | _ -> None

    let private nextInput (model: PipelineModel<'Input, 'Server, 'Local, 'Command>) =
        model.NextInput,
        { model with
            NextInput = model.NextInput + 1UL
        }

    /// Replace a waiting input of the same coalescing kind (Hover or Focus).
    let private coalesces (input: PipelineInput<'Input>) (item: QueueItem<'Input, 'Command>) =
        let kind (input: PipelineInput<'Input>) =
            match input with
            | PipelineInput.Scene event ->
                match InputEvent.action event with
                | InputAction.Hover -> Some 1
                | InputAction.Focus -> Some 2
                | _ -> None
            | PipelineInput.Game _ -> None

        match kind input, item with
        | Some k, QueuedInput(_, waiting) -> kind waiting = Some k
        | _ -> false

    let private andStart
        pipeline
        (model: PipelineModel<'Input, 'Server, 'Local, 'Command>)
        (leading: PipelineEffect<'Input, 'Local, 'Command> list)
        =
        let model, sends, fails, start = startNext pipeline model
        model, leading @ sends @ fails @ start

    let update
        (pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>)
        (msg: PipelineMsg<'Input, 'Server, 'Local, 'Command>)
        (model: PipelineModel<'Input, 'Server, 'Local, 'Command>)
        : PipelineModel<'Input, 'Server, 'Local, 'Command> * PipelineEffect<'Input, 'Local, 'Command> list =
        let stage = pipeline.Commands

        match msg with
        | PipelineMsg.Input input ->
            let sequence, model = nextInput model

            if model.Halted then
                model, [ PipelineEffect.Fail(PipelineFailure.InputRefused(sequence, "guest halted")) ]
            else
                let queue = model.Queue |> List.filter (coalesces input >> not)
                let guestItems = queue |> List.filter isGuestItem |> List.length

                if guestItems >= pipeline.Guest.Policy.QueueLimit then
                    { model with Queue = queue }, [ PipelineEffect.Fail(PipelineFailure.QueueOverflow [ sequence ]) ]
                else
                    andStart
                        pipeline
                        { model with
                            Queue = queue @ [ QueuedInput(sequence, input) ]
                        }
                        []
        | PipelineMsg.Submit command ->
            andStart
                pipeline
                { model with
                    Queue = model.Queue @ [ QueuedSubmit command ]
                }
                []
        | PipelineMsg.StartNext -> andStart pipeline { model with Draining = false } []
        | PipelineMsg.GuestRunning call ->
            match current model call with
            | Some flight when not flight.Running ->
                { model with
                    InFlight = Some { flight with Running = true }
                },
                [ PipelineEffect.ArmDeadline(call, pipeline.Guest.Policy.Timeout) ]
            | _ -> model, []
        | PipelineMsg.GuestStartDeadline call ->
            match current model call with
            | Some flight when not flight.Running -> endCall pipeline true (GuestOutcome.Faulted "start timeout") model
            | _ -> model, []
        | PipelineMsg.GuestDeadline call ->
            match current model call with
            | Some flight when flight.Running -> endCall pipeline true GuestOutcome.TimedOut model
            | _ -> model, []
        | PipelineMsg.GuestEnded(call, outcome) ->
            match current model call with
            | Some _ -> endCall pipeline false outcome model
            | None -> model, []
        | PipelineMsg.RearmGuest ->
            andStart
                pipeline
                { model with
                    Halted = false
                    Timeouts = 0
                }
                []
        | PipelineMsg.Rebind(client, lastSequence) ->
            if System.String.IsNullOrWhiteSpace client then
                model,
                [
                    PipelineEffect.Fail(PipelineFailure.RebindRefused "a client binding must not be blank")
                ]
            else
                let cancels, cancelled =
                    match model.InFlight with
                    | Some flight ->
                        [ PipelineEffect.CancelGuest flight.Call ],
                        [ PipelineFailure.InputCancelled(flight.Sequence, "rebind") ]
                    | None -> [], []

                let model, dropped =
                    dropGuestInputs
                        (fun sequence -> PipelineFailure.InputCancelled(sequence, "rebind"))
                        { model with
                            Client = client
                            LastSequence = max model.LastSequence (defaultArg lastSequence 0UL)
                            InFlight = None
                            Draining = false
                        }

                andStart pipeline model (cancels @ ((cancelled @ dropped) |> List.map PipelineEffect.Fail))
        | PipelineMsg.Acknowledged(id, ack) ->
            match model.Ledger |> List.tryFind (fun entry -> entry.Id = id) with
            | Some entry when isOpen entry.State ->
                let restart state =
                    Some
                        { entry with
                            State = state
                            Origin = model.Heard
                        },
                    []

                let changed, fails =
                    match ack.Kind with
                    | AckKind.Accepted ->
                        let state =
                            match entry.State with
                            | Pending -> SettlementKind.Accepted
                            | other -> other

                        restart state
                    | AckKind.Queued -> restart SettlementKind.Queued
                    | Started
                    | Progress _ -> restart Running
                    | Applied version ->
                        Some
                            { entry with
                                State = AppliedAt(version, None)
                            },
                        []
                    | Ended(version, outcome) ->
                        Some
                            { entry with
                                State = AppliedAt(version, Some outcome)
                            },
                        []
                    | Cancelled reason ->
                        Some
                            { entry with
                                State = CancelledAfter reason
                            },
                        [ PipelineFailure.CommandCancelled(id, reason) ]
                    | Rejected reason ->
                        Some
                            { entry with
                                State = RejectedBefore reason
                            },
                        [ PipelineFailure.CommandRejected(id, reason) ]
                    | Expired -> Some { entry with State = Unconfirmed }, [ PipelineFailure.CommandUnconfirmed id ]

                match changed with
                | Some updated ->
                    prune
                        { model with
                            Ledger = model.Ledger |> List.map (fun e -> if e.Id = id then updated else e)
                        },
                    fails |> List.map PipelineEffect.Fail
                | None -> model, []
            | _ -> model, []
        | PipelineMsg.Authoritative state ->
            let version = stage.Version state

            if version < model.AuthVersion then
                model, []
            else
                let settle (entry: Entry<'Command>) =
                    match stage.Settled with
                    | Some settled when isOpen entry.State && settled entry.Command state ->
                        { entry with
                            State = AppliedAt(version, None)
                        }
                    | _ -> entry

                let model =
                    { model with
                        Authoritative = state
                        AuthVersion = version
                        Heard = max model.Heard version
                        Ledger = model.Ledger |> List.map settle
                    }

                let model, fails = patience stage model
                prune model, fails |> List.map PipelineEffect.Fail
        | PipelineMsg.Heartbeat version ->
            let model, fails =
                patience
                    stage
                    { model with
                        Heard = max model.Heard version
                    }

            prune model, fails |> List.map PipelineEffect.Fail

    let client (model: PipelineModel<'Input, 'Server, 'Local, 'Command>) = model.Client

    let lastSequence (model: PipelineModel<'Input, 'Server, 'Local, 'Command>) = model.LastSequence

    let displayed (model: PipelineModel<'Input, 'Server, 'Local, 'Command>) : Displayed<'Server> =
        match model.Predict with
        | None ->
            {
                Authoritative = model.Authoritative
                Shown = model.Authoritative
                Predicted = []
            }
        | Some predict ->
            let predicted = model.Ledger |> List.filter (keep model.AuthVersion)

            {
                Authoritative = model.Authoritative
                Shown =
                    predicted
                    |> List.fold (fun state entry -> predict entry.Command state) model.Authoritative
                Predicted = predicted |> List.map (fun entry -> entry.Id)
            }

    let unsettled (model: PipelineModel<'Input, 'Server, 'Local, 'Command>) : (CommandId * CommandSettlement) list =
        model.Ledger
        |> List.filter (fun entry -> isOpen entry.State)
        |> List.map (fun entry -> entry.Id, ({ State = entry.State }: CommandSettlement))

    let guestHalted (model: PipelineModel<'Input, 'Server, 'Local, 'Command>) = model.Halted

[<NoEquality; NoComparison>]
type PipelineIo<'Client, 'Local, 'Command> =
    {
        Client: unit -> 'Client
        ApplyLocal: 'Local -> unit
        Send: CommandEnvelope<'Command> -> unit
        Fail: PipelineFailure -> unit
        Schedule: int -> (unit -> unit) -> unit
    }

[<RequireQualifiedAccess>]
module PipelineIo =

    let create client applyLocal send fail schedule =
        {
            Client = client
            ApplyLocal = applyLocal
            Send = send
            Fail = fail
            Schedule = schedule
        }

[<Sealed>]
type PipelineRunner<'Input, 'Client, 'Server, 'Local, 'Command>
    internal
    (
        pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>,
        io: PipelineIo<'Client, 'Local, 'Command>,
        dispatch: PipelineMsg<'Input, 'Server, 'Local, 'Command> -> unit
    ) =
    let mutable calls: (uint64 * System.Threading.CancellationTokenSource) list = []
    let mutable disposed = false

    let forget (id: uint64) =
        calls <- calls |> List.filter (fun (call, _) -> call <> id)

    member internal _.Perform(effect: PipelineEffect<'Input, 'Local, 'Command>) =
        if not disposed then
            match effect with
            | PipelineEffect.StartGuest call ->
                let source = new System.Threading.CancellationTokenSource()
                calls <- (call.Id, source) :: calls
                let request = GuestRequest.create call (io.Client())
                let mutable running = false

                let started () =
                    if not running then
                        running <- true
                        dispatch (PipelineMsg.GuestRunning call.Id)

                let ended outcome =
                    forget call.Id
                    dispatch (PipelineMsg.GuestEnded(call.Id, outcome))

                Async.StartWithContinuations(
                    InputGuest.run pipeline.Guest request started source.Token,
                    ended,
                    (fun error -> ended (GuestOutcome.Faulted error.Message)),
                    (fun _ -> ended (GuestOutcome.Cancelled "cancelled")),
                    source.Token
                )
            | PipelineEffect.CancelGuest id ->
                match calls |> List.tryFind (fun (call, _) -> call = id) with
                | Some(_, source) ->
                    forget id
                    source.Cancel()
                | None -> ()
            | PipelineEffect.ArmStartDeadline(id, milliseconds) ->
                io.Schedule milliseconds (fun () -> dispatch (PipelineMsg.GuestStartDeadline id))
            | PipelineEffect.ArmDeadline(id, milliseconds) ->
                io.Schedule milliseconds (fun () -> dispatch (PipelineMsg.GuestDeadline id))
            | PipelineEffect.Continue -> dispatch PipelineMsg.StartNext
            | PipelineEffect.ApplyLocal local -> io.ApplyLocal local
            | PipelineEffect.Send envelope -> io.Send envelope
            | PipelineEffect.Fail failure -> io.Fail failure

    member internal _.Dispose() =
        if not disposed then
            disposed <- true

            for _, source in calls do
                source.Cancel()

            calls <- []

    interface System.IDisposable with
        member this.Dispose() = this.Dispose()

[<RequireQualifiedAccess>]
module PipelineRunner =

    let create pipeline io dispatch =
        new PipelineRunner<_, _, _, _, _>(pipeline, io, dispatch)

    let perform
        (effect: PipelineEffect<'Input, 'Local, 'Command>)
        (runner: PipelineRunner<'Input, 'Client, 'Server, 'Local, 'Command>)
        =
        runner.Perform effect

    let performAll
        (effects: PipelineEffect<'Input, 'Local, 'Command> list)
        (runner: PipelineRunner<'Input, 'Client, 'Server, 'Local, 'Command>)
        =
        for effect in effects do
            runner.Perform effect

[<Sealed>]
type PipelineHost<'Input, 'Client, 'Server, 'Local, 'Command>
    internal
    (
        pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>,
        route: InputRoute,
        io: PipelineIo<'Client, 'Local, 'Command>,
        initial: PipelineModel<'Input, 'Server, 'Local, 'Command>
    ) as this =
    let mutable model = initial

    let pending =
        System.Collections.Generic.Queue<PipelineMsg<'Input, 'Server, 'Local, 'Command>>()

    let mutable draining = false
    let mutable disposed = false

    let runner =
        new PipelineRunner<_, _, _, _, _>(pipeline, io, (fun msg -> this.Dispatch msg))

    do InputRoute.bind (fun event -> this.Dispatch(PipelineMsg.Input(PipelineInput.Scene event))) route

    member _.Model = model

    member _.Dispatch(msg: PipelineMsg<'Input, 'Server, 'Local, 'Command>) =
        if not disposed then
            pending.Enqueue msg

            if not draining then
                draining <- true

                try
                    while pending.Count > 0 && not disposed do
                        let next, effects = InputPipeline.update pipeline (pending.Dequeue()) model
                        model <- next

                        for effect in effects do
                            runner.Perform effect
                finally
                    draining <- false

    interface System.IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                pending.Clear()
                InputRoute.unbind route
                runner.Dispose()

[<RequireQualifiedAccess>]
module PipelineHost =

    let start pipeline route io client lastSequence authoritative =
        new PipelineHost<_, _, _, _, _>(
            pipeline,
            route,
            io,
            InputPipeline.init pipeline client lastSequence authoritative
        )

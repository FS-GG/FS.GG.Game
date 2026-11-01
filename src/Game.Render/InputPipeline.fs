namespace FS.GG.Game.Render

// See InputPipeline.fsi and docs/adr/0001-standard-input-pipeline.md. This file is the CONTRACT STUB of
// FS.GG.Game#787: builders and accessors are data and work; every function the ADR gives behaviour to raises until
// FS.GG.Game#788 implements it.

module internal PipelineStub =

    let notYet (name: string) : 'T =
        failwithf "%s is a contract stub (FS.GG.Game#787); FS.GG.Game#788 implements it." name

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
        (_: InputGuest<'Input, 'Client, 'Local, 'Command>)
        (_: GuestRequest<'Input, 'Client>)
        (_: unit -> unit)
        (_: System.Threading.CancellationToken)
        : Async<GuestOutcome<'Local, 'Command>> =
        PipelineStub.notYet "InputGuest.run"

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

/// The pipeline state. FS.GG.Game#788 adds the sequences, the call in flight, the queue, the breaker and the ledger.
[<NoEquality; NoComparison>]
type PipelineModel<'Input, 'Server, 'Local, 'Command> =
    {
        Client: string
        LastSequence: uint64
        Authoritative: 'Server
        Halted: bool
    }

[<RequireQualifiedAccess>]
module InputPipeline =

    let create guest commands = { Guest = guest; Commands = commands }

    let guest (pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>) = pipeline.Guest

    let commands (pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>) = pipeline.Commands

    let init
        (_: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>)
        (client: string)
        (lastSequence: uint64 option)
        (authoritative: 'Server)
        : PipelineModel<'Input, 'Server, 'Local, 'Command> =
        if System.String.IsNullOrWhiteSpace client then
            invalidArg (nameof client) "A client binding must not be blank."

        {
            Client = client
            LastSequence = defaultArg lastSequence 0UL
            Authoritative = authoritative
            Halted = false
        }

    let update
        (_: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>)
        (_: PipelineMsg<'Input, 'Server, 'Local, 'Command>)
        (_: PipelineModel<'Input, 'Server, 'Local, 'Command>)
        : PipelineModel<'Input, 'Server, 'Local, 'Command> * PipelineEffect<'Input, 'Local, 'Command> list =
        PipelineStub.notYet "InputPipeline.update"

    let client (model: PipelineModel<'Input, 'Server, 'Local, 'Command>) = model.Client

    let lastSequence (model: PipelineModel<'Input, 'Server, 'Local, 'Command>) = model.LastSequence

    let displayed (model: PipelineModel<'Input, 'Server, 'Local, 'Command>) : Displayed<'Server> =
        {
            Authoritative = model.Authoritative
            Shown = model.Authoritative
            Predicted = []
        }

    let unsettled (_: PipelineModel<'Input, 'Server, 'Local, 'Command>) : (CommandId * CommandSettlement) list = []

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
type PipelineRunner<'Input, 'Client, 'Server, 'Local, 'Command> internal () =
    interface System.IDisposable with
        member _.Dispose() = ()

[<RequireQualifiedAccess>]
module PipelineRunner =

    let create
        (_: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>)
        (_: PipelineIo<'Client, 'Local, 'Command>)
        (_: PipelineMsg<'Input, 'Server, 'Local, 'Command> -> unit)
        : PipelineRunner<'Input, 'Client, 'Server, 'Local, 'Command> =
        PipelineStub.notYet "PipelineRunner.create"

    let perform
        (_: PipelineEffect<'Input, 'Local, 'Command>)
        (_: PipelineRunner<'Input, 'Client, 'Server, 'Local, 'Command>)
        : unit =
        PipelineStub.notYet "PipelineRunner.perform"

[<Sealed>]
type PipelineHost<'Input, 'Client, 'Server, 'Local, 'Command> internal () =

    member _.Model: PipelineModel<'Input, 'Server, 'Local, 'Command> =
        PipelineStub.notYet "PipelineHost.Model"

    member _.Dispatch(_: PipelineMsg<'Input, 'Server, 'Local, 'Command>) : unit =
        PipelineStub.notYet "PipelineHost.Dispatch"

    interface System.IDisposable with
        member _.Dispose() = ()

[<RequireQualifiedAccess>]
module PipelineHost =

    let start
        (_: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>)
        (_: InputRoute)
        (_: PipelineIo<'Client, 'Local, 'Command>)
        (_: string)
        (_: uint64 option)
        (_: 'Server)
        : PipelineHost<'Input, 'Client, 'Server, 'Local, 'Command> =
        PipelineStub.notYet "PipelineHost.start"

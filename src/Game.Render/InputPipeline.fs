namespace FS.GG.Game.Render

// See InputPipeline.fsi and docs/adr/0001-standard-input-pipeline.md. This file is the CONTRACT STUB of
// FS.GG.Game#787: builders and accessors are data and work; every function the ADR gives behaviour to raises until
// FS.GG.Game#788 implements it.

module internal PipelineStub =

    let notYet (name: string) : 'T =
        failwithf "%s is a contract stub (FS.GG.Game#787); FS.GG.Game#788 implements it." name

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
    | Cancelled

[<RequireQualifiedAccess>]
type GuestReply =
    | Output of bytes: byte array
    | Refused of status: int
    | Faulted of diagnostic: string
    | TimedOut
    | Cancelled

[<NoEquality; NoComparison>]
type GuestPolicy = { Timeout: int; QueueLimit: int }

[<RequireQualifiedAccess>]
module GuestPolicy =

    let standard = { Timeout = 500; QueueLimit = 16 }

    let withTimeout (milliseconds: int) (policy: GuestPolicy) =
        if milliseconds <= 0 then
            invalidArg (nameof milliseconds) "A guest deadline must be greater than zero."

        { policy with Timeout = milliseconds }

    let withQueueLimit (limit: int) (policy: GuestPolicy) =
        if limit < 1 then
            invalidArg (nameof limit) "A guest queue holds at least one input."

        { policy with QueueLimit = limit }

    let timeout (policy: GuestPolicy) = policy.Timeout

    let queueLimit (policy: GuestPolicy) = policy.QueueLimit

[<NoEquality; NoComparison>]
type GuestRequest<'Client> =
    {
        Sequence: uint64
        Epoch: string
        Event: InputEvent
        Client: 'Client
        Basis: uint64 option
    }

[<RequireQualifiedAccess>]
module GuestRequest =

    let sequence (request: GuestRequest<'Client>) = request.Sequence

    let epoch (request: GuestRequest<'Client>) = request.Epoch

    let event (request: GuestRequest<'Client>) = request.Event

    let client (request: GuestRequest<'Client>) = request.Client

    let basis (request: GuestRequest<'Client>) = request.Basis

[<NoEquality; NoComparison>]
type GuestKind<'Client, 'Local, 'Command> =
    | Sync of (GuestRequest<'Client> -> GuestOutcome<'Local, 'Command>)
    | Async of (GuestRequest<'Client> -> System.Threading.CancellationToken -> Async<GuestOutcome<'Local, 'Command>>)

[<NoEquality; NoComparison>]
type InputGuest<'Client, 'Local, 'Command> =
    {
        Kind: GuestKind<'Client, 'Local, 'Command>
        Policy: GuestPolicy
        Admit: ('Client -> Result<unit, string>) option
    }

[<RequireQualifiedAccess>]
module InputGuest =

    let sync (decide: GuestRequest<'Client> -> GuestOutcome<'Local, 'Command>) =
        {
            Kind = Sync decide
            Policy = GuestPolicy.standard
            Admit = None
        }

    let async decide (policy: GuestPolicy) =
        {
            Kind = Async decide
            Policy = policy
            Admit = None
        }

    let ofBytes
        (_: GuestRequest<'Client> -> byte array)
        (_: byte array -> System.Threading.CancellationToken -> Async<GuestReply>)
        (_: byte array -> Result<GuestIntent<'Local, 'Command> list, string>)
        (_: GuestPolicy)
        : InputGuest<'Client, 'Local, 'Command> =
        PipelineStub.notYet "InputGuest.ofBytes"

    let withAdmission (admit: 'Client -> Result<unit, string>) (guest: InputGuest<'Client, 'Local, 'Command>) =
        { guest with Admit = Some admit }

    let isAsync (guest: InputGuest<'Client, 'Local, 'Command>) =
        match guest.Kind with
        | Sync _ -> false
        | Async _ -> true

    let policy (guest: InputGuest<'Client, 'Local, 'Command>) = guest.Policy

type CommandId =
    private
        {
            Epoch: string
            Sequence: uint64
        }

[<RequireQualifiedAccess>]
module CommandId =

    let epoch (id: CommandId) = id.Epoch

    let sequence (id: CommandId) = id.Sequence

    let text (id: CommandId) = sprintf "%s:%d" id.Epoch id.Sequence

    let tryParse (text: string) : CommandId option =
        if System.String.IsNullOrEmpty text then
            None
        else
            let colon = text.LastIndexOf ':'

            if colon <= 0 then
                None
            else
                let epoch = text.Substring(0, colon)
                let digits = text.Substring(colon + 1)

                if
                    System.String.IsNullOrWhiteSpace epoch
                    || digits.Length = 0
                    || not (digits |> Seq.forall (fun c -> c >= '0' && c <= '9'))
                then
                    None
                else
                    match System.UInt64.TryParse digits with
                    | true, sequence when sequence >= 1UL -> Some { Epoch = epoch; Sequence = sequence }
                    | _ -> None

[<NoEquality; NoComparison>]
type CommandEnvelope<'Command> =
    {
        Id: CommandId
        Input: uint64
        Basis: uint64 option
        Command: 'Command
    }

[<RequireQualifiedAccess>]
module CommandEnvelope =

    let id (envelope: CommandEnvelope<'Command>) = envelope.Id

    let input (envelope: CommandEnvelope<'Command>) = envelope.Input

    let basis (envelope: CommandEnvelope<'Command>) = envelope.Basis

    let command (envelope: CommandEnvelope<'Command>) = envelope.Command

[<RequireQualifiedAccess>]
type CommandAck =
    | Accepted
    | Applied of version: uint64
    | Rejected of reason: string

[<RequireQualifiedAccess>]
type CommandSettlement =
    | Pending
    | Accepted
    | Applied of version: uint64
    | Rejected of reason: string
    | Unconfirmed

[<NoEquality; NoComparison>]
type CommandStage<'Server, 'Command> =
    {
        Version: 'Server -> uint64
        Window: int
        Settled: ('Command -> 'Server -> bool) option
        Patience: uint64
        Predict: ('Command -> 'Server -> 'Server) option
    }

[<RequireQualifiedAccess>]
module CommandStage =

    let create (version: 'Server -> uint64) : CommandStage<'Server, 'Command> =
        {
            Version = version
            Window = 8
            Settled = None
            Patience = 600UL
            Predict = None
        }

    let withWindow (limit: int) (stage: CommandStage<'Server, 'Command>) =
        if limit < 1 then
            invalidArg (nameof limit) "A command window holds at least one command."

        { stage with Window = limit }

    let withSettlement settled (patience: uint64) (stage: CommandStage<'Server, 'Command>) =
        { stage with
            Settled = Some settled
            Patience = patience
        }

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
    | InputRefused of sequence: uint64 * reason: string
    | QueueOverflow of dropped: int
    | CommandWindowFull of sequence: uint64
    | CommandRejected of id: CommandId * reason: string
    | CommandUnconfirmed of id: CommandId

[<RequireQualifiedAccess>]
type PipelineMsg<'Client, 'Server, 'Local, 'Command> =
    | Input of event: InputEvent * client: 'Client
    | GuestEnded of call: uint64 * outcome: GuestOutcome<'Local, 'Command>
    | GuestDeadline of call: uint64
    | Acknowledged of id: CommandId * ack: CommandAck
    | Authoritative of state: 'Server
    | Reset of epoch: string

[<RequireQualifiedAccess>]
type PipelineEffect<'Client, 'Local, 'Command> =
    | StartGuest of call: uint64 * request: GuestRequest<'Client>
    | CancelGuest of call: uint64
    | ArmDeadline of call: uint64 * milliseconds: int
    | ApplyLocal of intent: 'Local
    | Send of envelope: CommandEnvelope<'Command>
    | Fail of failure: PipelineFailure

[<NoEquality; NoComparison>]
type InputPipeline<'Client, 'Server, 'Local, 'Command> =
    {
        Guest: InputGuest<'Client, 'Local, 'Command>
        Commands: CommandStage<'Server, 'Command>
    }

/// The pipeline state. FS.GG.Game#788 adds the input sequence, the call in flight, the queue and the command ledger.
[<NoEquality; NoComparison>]
type PipelineModel<'Client, 'Server, 'Local, 'Command> =
    {
        Epoch: string
        Authoritative: 'Server
        Halted: bool
    }

[<RequireQualifiedAccess>]
module InputPipeline =

    let create guest commands = { Guest = guest; Commands = commands }

    let guest (pipeline: InputPipeline<'Client, 'Server, 'Local, 'Command>) = pipeline.Guest

    let commands (pipeline: InputPipeline<'Client, 'Server, 'Local, 'Command>) = pipeline.Commands

    let init
        (_: InputPipeline<'Client, 'Server, 'Local, 'Command>)
        (epoch: string)
        (authoritative: 'Server)
        : PipelineModel<'Client, 'Server, 'Local, 'Command> * PipelineEffect<'Client, 'Local, 'Command> list =
        if System.String.IsNullOrWhiteSpace epoch then
            invalidArg (nameof epoch) "A pipeline epoch must not be blank."

        {
            Epoch = epoch
            Authoritative = authoritative
            Halted = false
        },
        []

    let update
        (_: InputPipeline<'Client, 'Server, 'Local, 'Command>)
        (_: PipelineMsg<'Client, 'Server, 'Local, 'Command>)
        (_: PipelineModel<'Client, 'Server, 'Local, 'Command>)
        : PipelineModel<'Client, 'Server, 'Local, 'Command> * PipelineEffect<'Client, 'Local, 'Command> list =
        PipelineStub.notYet "InputPipeline.update"

    let epoch (model: PipelineModel<'Client, 'Server, 'Local, 'Command>) = model.Epoch

    let displayed (model: PipelineModel<'Client, 'Server, 'Local, 'Command>) : Displayed<'Server> =
        {
            Authoritative = model.Authoritative
            Shown = model.Authoritative
            Predicted = []
        }

    let unsettled (_: PipelineModel<'Client, 'Server, 'Local, 'Command>) : (CommandId * CommandSettlement) list = []

    let guestHalted (model: PipelineModel<'Client, 'Server, 'Local, 'Command>) = model.Halted

[<Sealed>]
type PipelineHost<'Client, 'Server, 'Local, 'Command> internal () =

    member _.Model: PipelineModel<'Client, 'Server, 'Local, 'Command> =
        PipelineStub.notYet "PipelineHost.Model"

    member _.Dispatch(_: PipelineMsg<'Client, 'Server, 'Local, 'Command>) : unit =
        PipelineStub.notYet "PipelineHost.Dispatch"

    interface System.IDisposable with
        member _.Dispose() = ()

[<RequireQualifiedAccess>]
module PipelineHost =

    let start
        (_: InputPipeline<'Client, 'Server, 'Local, 'Command>)
        (_: InputRoute)
        (_: string)
        (_: 'Server)
        (_: unit -> 'Client)
        (_: 'Local -> unit)
        (_: CommandEnvelope<'Command> -> unit)
        (_: PipelineFailure -> unit)
        (_: int -> (unit -> unit) -> unit)
        : PipelineHost<'Client, 'Server, 'Local, 'Command> =
        PipelineStub.notYet "PipelineHost.start"

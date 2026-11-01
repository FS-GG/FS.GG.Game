namespace FS.GG.Game.Render

// The standard input pipeline (FS.GG.Game#787, `docs/adr/0001-standard-input-pipeline.md`): an `InputEvent` (stage 1)
// goes to an optional guest (stage 2), whose intents are applied locally or sent to the authority as commands
// (stage 3). The server is the only authority. Contract only: FS.GG.Game#788 implements every function below that the
// ADR gives behaviour to, and until then those functions raise. Builders and accessors are data and work now.

/// Public contract type exposed by the FS.GG.Game.Render package.
/// What a guest decides for one input: a change to client-only state, or a command for the authority.
[<RequireQualifiedAccess>]
type GuestIntent<'Local, 'Command> =
    /// A client-only change (selection, an open menu, a targeting mode), applied by the game at once.
    | Local of intent: 'Local
    /// A request to the authority. Nothing changes authoritatively until the authority says so.
    | Submit of command: 'Command

/// Public contract type exposed by the FS.GG.Game.Render package.
/// How one guest call ended. `Cancelled` and `TimedOut` are also produced by the pipeline itself.
[<RequireQualifiedAccess>]
type GuestOutcome<'Local, 'Command> =
    /// The guest's intents for the input, in order (possibly none).
    | Decided of intents: GuestIntent<'Local, 'Command> list
    /// The guest declined the input by its own rule. Not a fault: the guest stays usable.
    | Refused of reason: string
    /// The guest trapped, or its output could not be decoded. The guest stage is unusable until `PipelineMsg.Reset`.
    | Faulted of reason: string
    /// The call passed its deadline. The guest stage is unusable until `PipelineMsg.Reset`.
    | TimedOut
    /// The call was superseded, reset or disposed before it ended; its late reply is dropped.
    | Cancelled

/// Public contract type exposed by the FS.GG.Game.Render package.
/// The raw reply of a byte-level guest (a WebAssembly module behind `FS.GG.Wasm.Browser`, or any other runtime), before
/// the game's codec decodes it. The ADR maps `FS.GG.Wasm.Contracts.TerminalState` onto it.
[<RequireQualifiedAccess>]
type GuestReply =
    /// The guest's output bytes.
    | Output of bytes: byte array
    /// The guest returned a non-zero status: a refusal by its own rule.
    | Refused of status: int
    /// The guest trapped or the host failed.
    | Faulted of diagnostic: string
    /// The runtime's own deadline passed.
    | TimedOut
    /// The runtime invalidated or froze the call.
    | Cancelled

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// How the pipeline schedules guest calls. One call is in flight at a time; later inputs wait in a bounded queue in
/// order, except that a waiting `Hover` or `Focus` is replaced by a newer one. Built from `GuestPolicy.standard`.
[<NoEquality; NoComparison>]
type GuestPolicy

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module GuestPolicy =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A deadline of 500 ms per call and a queue of 16 waiting inputs.
    val standard: GuestPolicy

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The deadline of one call, in milliseconds (greater than zero). A call past it ends `TimedOut`.
    val withTimeout: milliseconds: int -> policy: GuestPolicy -> GuestPolicy

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// How many inputs may wait behind the call in flight (at least one). One more ends the waiting inputs with a
    /// `PipelineFailure.QueueOverflow`.
    val withQueueLimit: limit: int -> policy: GuestPolicy -> GuestPolicy

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The deadline of one call, in milliseconds.
    val timeout: policy: GuestPolicy -> int

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// How many inputs may wait.
    val queueLimit: policy: GuestPolicy -> int

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// What a guest is asked: one input, its sequence, and the client state the game passed with it. Built by the
/// pipeline only.
[<NoEquality; NoComparison>]
type GuestRequest<'Client>

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module GuestRequest =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The input's sequence: strictly increasing within one pipeline epoch, from 1.
    val sequence: request: GuestRequest<'Client> -> uint64

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The pipeline epoch (`InputPipeline.init`, `PipelineMsg.Reset`).
    val epoch: request: GuestRequest<'Client> -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The input.
    val event: request: GuestRequest<'Client> -> InputEvent

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The client state the game passed with the input (`PipelineMsg.Input`).
    val client: request: GuestRequest<'Client> -> 'Client

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The version of the authoritative state the client was showing, if the authority has sent one.
    val basis: request: GuestRequest<'Client> -> uint64 option

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// Stage 2: turns an input and the client state into intents. A guest is language-neutral: an F# function
/// (`InputGuest.sync`, `InputGuest.async`) or a byte-level module such as a WebAssembly guest (`InputGuest.ofBytes`).
/// A game with no guest uses `InputGuest.sync` over its own mapping. Built by those functions.
[<NoEquality; NoComparison>]
type InputGuest<'Client, 'Local, 'Command>

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module InputGuest =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A synchronous guest: `decide` answers within the input's own dispatch. It has no deadline and cannot be
    /// cancelled, so it must be pure and quick.
    val sync: decide: (GuestRequest<'Client> -> GuestOutcome<'Local, 'Command>) -> InputGuest<'Client, 'Local, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// An asynchronous guest under `policy`: `decide` is started for one input at a time and observes the token,
    /// which the pipeline cancels on a deadline, a reset or disposal.
    val async:
        decide: (GuestRequest<'Client> -> System.Threading.CancellationToken -> Async<GuestOutcome<'Local, 'Command>>) ->
        policy: GuestPolicy ->
            InputGuest<'Client, 'Local, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A byte-level guest under `policy`: `encode` writes the request in the game's own wire format, `invoke` runs the
    /// module (for WebAssembly, an `FS.GG.Wasm.Browser` host call bounded by the policy's deadline) and `decode` reads
    /// its output. A decode `Error` is a `Faulted` outcome.
    val ofBytes:
        encode: (GuestRequest<'Client> -> byte array) ->
        invoke: (byte array -> System.Threading.CancellationToken -> Async<GuestReply>) ->
        decode: (byte array -> Result<GuestIntent<'Local, 'Command> list, string>) ->
        policy: GuestPolicy ->
            InputGuest<'Client, 'Local, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Admit inputs only while `admit client` is `Ok`; an `Error reason` refuses the input before any call (a game
    /// rule such as BAR's 64-unit guest bound, or a guest that is not armed yet).
    val withAdmission:
        admit: ('Client -> Result<unit, string>) ->
        guest: InputGuest<'Client, 'Local, 'Command> ->
            InputGuest<'Client, 'Local, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether the guest answers asynchronously.
    val isAsync: guest: InputGuest<'Client, 'Local, 'Command> -> bool

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The guest's policy (`GuestPolicy.standard` for a synchronous guest, which ignores it).
    val policy: guest: InputGuest<'Client, 'Local, 'Command> -> GuestPolicy

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// The identity of one command: the pipeline epoch and a sequence strictly increasing within it, from 1. It maps one
/// to one onto `FS.GG.Game.Core.SessionInput` (`InputId` is `CommandId.text`, `Sequence` is `CommandId.sequence`), so
/// an authority admits it with `NetworkAdmission.admit`, and the same id is never two commands. Equal, and ordered, by
/// epoch and then sequence. Built by the pipeline or `CommandId.tryParse`.
type CommandId =
    private
        {
            Epoch: string
            Sequence: uint64
        }

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module CommandId =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The epoch the command was issued in.
    val epoch: id: CommandId -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Its sequence within the epoch.
    val sequence: id: CommandId -> uint64

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The wire text `epoch:sequence` (the sequence in decimal).
    val text: id: CommandId -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Read the wire text back: `None` unless it is a non-blank epoch, a colon and a decimal sequence of at least 1.
    val tryParse: text: string -> CommandId option

/// Public contract type exposed by the FS.GG.Game.Render package.
/// One command as the pipeline sends it. Built by the pipeline.
[<NoEquality; NoComparison>]
type CommandEnvelope<'Command>

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module CommandEnvelope =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The command's identity. A resend carries the same id.
    val id: envelope: CommandEnvelope<'Command> -> CommandId

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The sequence of the input that produced it (`GuestRequest.sequence`).
    val input: envelope: CommandEnvelope<'Command> -> uint64

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The authoritative version the input was decided against, if any. An authority may refuse a stale basis.
    val basis: envelope: CommandEnvelope<'Command> -> uint64 option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The command.
    val command: envelope: CommandEnvelope<'Command> -> 'Command

/// Public contract type exposed by the FS.GG.Game.Render package.
/// What the authority answered for one command. An authority answers a duplicate id with the answer it gave first.
[<RequireQualifiedAccess>]
type CommandAck =
    /// Admitted; the effect is not yet in the authoritative state.
    | Accepted
    /// The effect is in the authoritative state from this version on.
    | Applied of version: uint64
    /// Refused, with the authority's reason. Nothing happened.
    | Rejected of reason: string

/// Public contract type exposed by the FS.GG.Game.Render package.
/// Where one command stands, as the pipeline knows it.
[<RequireQualifiedAccess>]
type CommandSettlement =
    /// Sent; no answer yet.
    | Pending
    /// Admitted; no effect seen yet.
    | Accepted
    /// Its effect is in the authoritative state shown (an `Applied` answer reached, or the game's settlement test held).
    | Applied of version: uint64
    /// Refused. Any prediction of it is withdrawn.
    | Rejected of reason: string
    /// The patience ran out (or the epoch ended) without evidence either way. No claim is made that it happened.
    | Unconfirmed

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// Stage 3: how commands are sent and reconciled against the authoritative state of type `'Server`. Built by
/// `CommandStage.create`.
[<NoEquality; NoComparison>]
type CommandStage<'Server, 'Command>

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module CommandStage =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A stage whose authoritative states carry `version state`, which must not decrease: a state older than the one
    /// shown is ignored. At most 8 commands are unsettled at once, and a command still `Pending` or `Accepted` after
    /// 600 newer versions is `Unconfirmed`.
    val create: version: ('Server -> uint64) -> CommandStage<'Server, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// At most `limit` (at least one) commands unsettled at once; a `Submit` beyond it fails with
    /// `PipelineFailure.CommandWindowFull` and is not sent. A strictly sequential game uses 1.
    val withWindow: limit: int -> stage: CommandStage<'Server, 'Command> -> CommandStage<'Server, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Settle from the authoritative state when the authority sends no `Applied` answer: a command is `Applied` at the
    /// first version where `settled command state` holds, and `Unconfirmed` after `patience` newer versions.
    val withSettlement:
        settled: ('Command -> 'Server -> bool) ->
        patience: uint64 ->
        stage: CommandStage<'Server, 'Command> ->
            CommandStage<'Server, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Optional optimistic display: the state shown is `predict` of every `Pending` or `Accepted` command, in sequence
    /// order, over the latest authoritative state, and `Displayed.predicted` names them so the view marks what is not
    /// yet authoritative. It never changes the authoritative state. Without it the state shown IS the authoritative one.
    val withPrediction:
        predict: ('Command -> 'Server -> 'Server) ->
        stage: CommandStage<'Server, 'Command> ->
            CommandStage<'Server, 'Command>

/// Public contract type exposed by the FS.GG.Game.Render package.
/// The state to show: the latest authoritative state, and, with a prediction, the predicted one. Built by the pipeline.
[<NoEquality; NoComparison>]
type Displayed<'Server>

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module Displayed =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The latest authoritative state.
    val authoritative: displayed: Displayed<'Server> -> 'Server

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The state to draw: the authoritative one, or the prediction over it.
    val shown: displayed: Displayed<'Server> -> 'Server

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The commands whose prediction is in `shown`, in sequence order. Empty means `shown` is authoritative.
    val predicted: displayed: Displayed<'Server> -> CommandId list

/// Public contract type exposed by the FS.GG.Game.Render package.
/// A failure the pipeline routes to the game, for display and recovery. Never raised.
[<RequireQualifiedAccess>]
type PipelineFailure =
    /// The guest faulted on input `sequence`; the guest stage waits for `PipelineMsg.Reset`.
    | GuestFaulted of sequence: uint64 * reason: string
    /// The guest passed its deadline on input `sequence`; the guest stage waits for `PipelineMsg.Reset`.
    | GuestTimedOut of sequence: uint64
    /// Input `sequence` was refused before or by the guest (admission, or the guest's own `Refused`).
    | InputRefused of sequence: uint64 * reason: string
    /// The queue was full: `dropped` waiting inputs were ended, none of them was sent.
    | QueueOverflow of dropped: int
    /// A `Submit` found the command window full and was not sent.
    | CommandWindowFull of sequence: uint64
    /// The authority rejected the command.
    | CommandRejected of id: CommandId * reason: string
    /// The command ended `Unconfirmed`.
    | CommandUnconfirmed of id: CommandId

/// Public contract type exposed by the FS.GG.Game.Render package.
/// What the game tells the pipeline.
[<RequireQualifiedAccess>]
type PipelineMsg<'Client, 'Server, 'Local, 'Command> =
    /// An input from the `InputRoute`, with the client state at that moment.
    | Input of event: InputEvent * client: 'Client
    /// A guest call ended (from the effect interpreter).
    | GuestEnded of call: uint64 * outcome: GuestOutcome<'Local, 'Command>
    /// A guest call's deadline passed (from the effect interpreter's timer).
    | GuestDeadline of call: uint64
    /// The authority answered a command.
    | Acknowledged of id: CommandId * ack: CommandAck
    /// A new authoritative state arrived.
    | Authoritative of state: 'Server
    /// Start a new epoch: cancel the call in flight, end the waiting inputs, mark every unsettled command
    /// `Unconfirmed`, and make a faulted guest usable again. For a new session, a reconnect or a re-armed guest.
    | Reset of epoch: string

/// Public contract type exposed by the FS.GG.Game.Render package.
/// What the pipeline asks its interpreter (`PipelineHost`) to do, in order.
[<RequireQualifiedAccess>]
type PipelineEffect<'Client, 'Local, 'Command> =
    /// Start guest call `call` for `request`.
    | StartGuest of call: uint64 * request: GuestRequest<'Client>
    /// Cancel guest call `call`. A reply it still produces is dropped.
    | CancelGuest of call: uint64
    /// Report `GuestDeadline call` after `milliseconds`.
    | ArmDeadline of call: uint64 * milliseconds: int
    /// Apply a client-only intent to the game's own state.
    | ApplyLocal of intent: 'Local
    /// Send a command to the authority.
    | Send of envelope: CommandEnvelope<'Command>
    /// Route a failure to the game.
    | Fail of failure: PipelineFailure

/// Public contract type exposed by the FS.GG.Game.Render package.
/// A game's pipeline: its guest and its command stage. Built by `InputPipeline.create`.
[<NoEquality; NoComparison>]
type InputPipeline<'Client, 'Server, 'Local, 'Command>

/// Public contract type exposed by the FS.GG.Game.Render package.
/// The pipeline's state: the epoch, the input sequence, the guest call in flight and its queue, the commands and their
/// settlement, and the latest authoritative state. Built by `InputPipeline.init`.
[<NoEquality; NoComparison>]
type PipelineModel<'Client, 'Server, 'Local, 'Command>

/// Public contract module exposed by the FS.GG.Game.Render package.
///
/// The pipeline as an Elmish reducer, like `RendererSelection`: `init` and `update` are pure and return effects, which
/// `PipelineHost` (or a game's own `Cmd`) performs. Stage 1 feeds `PipelineMsg.Input`; stage 2 is the guest; stage 3
/// emits `Send` and reconciles answers and authoritative states. The server stays the only authority.
[<RequireQualifiedAccess>]
module InputPipeline =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The pipeline of `guest` and `commands`.
    val create:
        guest: InputGuest<'Client, 'Local, 'Command> ->
        commands: CommandStage<'Server, 'Command> ->
            InputPipeline<'Client, 'Server, 'Local, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The guest.
    val guest: pipeline: InputPipeline<'Client, 'Server, 'Local, 'Command> -> InputGuest<'Client, 'Local, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The command stage.
    val commands: pipeline: InputPipeline<'Client, 'Server, 'Local, 'Command> -> CommandStage<'Server, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The first model, in `epoch` (not blank; a fresh value per session, such as the client binding the authority
    /// issued), showing `authoritative`. No effects.
    val init:
        pipeline: InputPipeline<'Client, 'Server, 'Local, 'Command> ->
        epoch: string ->
        authoritative: 'Server ->
            PipelineModel<'Client, 'Server, 'Local, 'Command> * PipelineEffect<'Client, 'Local, 'Command> list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Apply one message. Contract only: FS.GG.Game#788 implements it, and until then it raises.
    val update:
        pipeline: InputPipeline<'Client, 'Server, 'Local, 'Command> ->
        msg: PipelineMsg<'Client, 'Server, 'Local, 'Command> ->
        model: PipelineModel<'Client, 'Server, 'Local, 'Command> ->
            PipelineModel<'Client, 'Server, 'Local, 'Command> * PipelineEffect<'Client, 'Local, 'Command> list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The current epoch.
    val epoch: model: PipelineModel<'Client, 'Server, 'Local, 'Command> -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The state to show.
    val displayed: model: PipelineModel<'Client, 'Server, 'Local, 'Command> -> Displayed<'Server>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Every command of the current epoch not yet `Applied`, `Rejected` or `Unconfirmed`, in sequence order.
    val unsettled: model: PipelineModel<'Client, 'Server, 'Local, 'Command> -> (CommandId * CommandSettlement) list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether the guest stage is waiting for `PipelineMsg.Reset` after a fault or a timeout.
    val guestHalted: model: PipelineModel<'Client, 'Server, 'Local, 'Command> -> bool

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// Runs `InputPipeline` for a page that is not already an Elmish program, as `RendererDriver` runs renderer selection:
/// it binds an `InputRoute`, starts and cancels guest calls, arms deadlines, and hands `ApplyLocal`, `Send` and `Fail` to
/// the game. Built by `PipelineHost.start`.
[<Sealed>]
type PipelineHost<'Client, 'Server, 'Local, 'Command> =
    interface System.IDisposable

    /// Public contract member exposed by the FS.GG.Game.Render package.
    /// The current model.
    member Model: PipelineModel<'Client, 'Server, 'Local, 'Command>

    /// Public contract member exposed by the FS.GG.Game.Render package.
    /// Apply one message and run its effects. Ignored after `Dispose`.
    member Dispatch: msg: PipelineMsg<'Client, 'Server, 'Local, 'Command> -> unit

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module PipelineHost =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Start `pipeline` in `epoch` on `authoritative`, bound to `route`: each event is dispatched with `client ()`.
    /// `applyLocal`, `send` and `fail` receive the effects of those names; `schedule milliseconds action` runs `action`
    /// once after the delay. Disposing it cancels the call in flight and unbinds `route`. Contract only:
    /// FS.GG.Game#788 implements it, and until then it raises.
    val start:
        pipeline: InputPipeline<'Client, 'Server, 'Local, 'Command> ->
        route: InputRoute ->
        epoch: string ->
        authoritative: 'Server ->
        client: (unit -> 'Client) ->
        applyLocal: ('Local -> unit) ->
        send: (CommandEnvelope<'Command> -> unit) ->
        fail: (PipelineFailure -> unit) ->
        schedule: (int -> (unit -> unit) -> unit) ->
            PipelineHost<'Client, 'Server, 'Local, 'Command>

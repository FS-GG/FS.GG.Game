namespace FS.GG.Game.Render

// The standard input pipeline (FS.GG.Game#787, `docs/adr/0001-standard-input-pipeline.md`): an input (stage 1) goes to
// an optional guest (stage 2), whose intents are applied locally or sent to the authority as commands (stage 3). The
// server is the only authority. Contract only: FS.GG.Game#788 implements every function below that the ADR gives
// behaviour to, and until then those functions raise. Builders and accessors are data and work now.
//
// Open unions: `PipelineInput`, `GuestOutcome`, `PipelineFailure` and `PipelineMsg` may gain cases in a minor version;
// match the first three with a wildcard. `PipelineEffect` may too: a game that interprets effects itself instead of
// through `PipelineRunner` handles new cases on upgrade.

/// Public contract type exposed by the FS.GG.Game.Render package.
/// What enters the guest queue: a scene input from the `InputRoute`, or an input the game reports itself (an ability
/// button, a control group, a takeover or release in a panel).
[<RequireQualifiedAccess>]
type PipelineInput<'Input> =
    /// An input from a backend's input host.
    | Scene of event: InputEvent
    /// An input from the game's own controls.
    | Game of input: 'Input

/// Public contract type exposed by the FS.GG.Game.Render package.
/// What a guest decides for one input: a change to client-only state, or a command for the authority.
[<RequireQualifiedAccess>]
type GuestIntent<'Local, 'Command> =
    /// A client-only change (selection, an open menu, a targeting mode), applied by the game at once.
    | Local of intent: 'Local
    /// A request to the authority. Nothing changes authoritatively until the authority says so.
    | Submit of command: 'Command

/// Public contract type exposed by the FS.GG.Game.Render package.
/// How one guest call ended, as `InputGuest.run` reports it.
[<RequireQualifiedAccess>]
type GuestOutcome<'Local, 'Command> =
    /// The guest's intents for the input, in order (possibly none).
    | Decided of intents: GuestIntent<'Local, 'Command> list
    /// The guest, or its admission, declined the input by its own rule. The guest stays usable.
    | Refused of reason: string
    /// The guest trapped, threw, or its output could not be decoded. The guest stage halts until
    /// `PipelineMsg.RearmGuest`.
    | Faulted of reason: string
    /// The call passed its deadline. Only this input fails; the guest stays usable unless the breaker trips.
    | TimedOut
    /// The runtime or the pipeline ended the call before it decided (invalidated, frozen, rebound or disposed). Only
    /// this input fails.
    | Cancelled of reason: string

/// Public contract type exposed by the FS.GG.Game.Render package.
/// The raw reply of a byte-level guest, before the game's codec decodes it. One case per
/// `FS.GG.Wasm.Contracts.TerminalState`, so nothing is merged here; `InputGuest.run` maps it to a `GuestOutcome`.
[<RequireQualifiedAccess>]
type GuestReply =
    /// `Succeeded`: the guest's output bytes. Decoded by the game's codec; a decode error is `Faulted`.
    | Output of bytes: byte array
    /// `GuestRejected`: a non-zero status, a refusal by the guest's own rule. Becomes `Refused`.
    | Refused of status: int
    /// `Faulted`: a trap or host failure. Becomes `Faulted`.
    | Faulted of diagnostic: string
    /// `TimedOut`: the runtime's own deadline. Becomes `TimedOut`.
    | TimedOut
    /// `Invalidated`: the runtime replaced the guest generation during the call. Becomes `Cancelled "invalidated"`.
    | Invalidated
    /// `Frozen`: the runtime was frozen during the call. Becomes `Cancelled "frozen"`.
    | Frozen

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// How the pipeline schedules guest calls: one call at a time, a bounded queue, a deadline armed when the guest is warm,
/// and a breaker that halts the guest after consecutive failures. Built from `GuestPolicy.standard`.
[<NoEquality; NoComparison>]
type GuestPolicy

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module GuestPolicy =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A 500 ms deadline per call, a queue of 16 waiting inputs and a breaker of 3 consecutive failures.
    val standard: GuestPolicy

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The deadline of one call, in milliseconds (greater than zero), counted from `PipelineMsg.GuestRunning`.
    val withTimeout: milliseconds: int -> policy: GuestPolicy -> GuestPolicy

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// How many inputs may wait behind the call in flight (at least one).
    val withQueueLimit: limit: int -> policy: GuestPolicy -> GuestPolicy

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// How many consecutive `TimedOut` outcomes halt the guest stage (at least one).
    val withBreaker: failures: int -> policy: GuestPolicy -> GuestPolicy

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The deadline of one call, in milliseconds.
    val timeout: policy: GuestPolicy -> int

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// How many inputs may wait.
    val queueLimit: policy: GuestPolicy -> int

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// How many consecutive timeouts halt the guest stage.
    val breaker: policy: GuestPolicy -> int

/// Public contract type exposed by the FS.GG.Game.Render package.
/// One guest call as the pipeline starts it (`PipelineEffect.StartGuest`). Built by the pipeline.
[<NoEquality; NoComparison>]
type GuestCall<'Input>

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module GuestCall =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The call id: unique for the model's lifetime, never reused.
    val id: call: GuestCall<'Input> -> uint64

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The input's sequence: strictly increasing for the model's lifetime, from 1.
    val sequence: call: GuestCall<'Input> -> uint64

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The input.
    val input: call: GuestCall<'Input> -> PipelineInput<'Input>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The version of the authoritative state shown when the call started.
    val basis: call: GuestCall<'Input> -> uint64

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// What a guest is asked: one call and the client state READ WHEN THE CALL STARTS, not when its input was queued. Built
/// by the effect interpreter with `GuestRequest.create`.
[<NoEquality; NoComparison>]
type GuestRequest<'Input, 'Client>

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module GuestRequest =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The request for `call` with the client state at its start.
    val create: call: GuestCall<'Input> -> client: 'Client -> GuestRequest<'Input, 'Client>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The call.
    val call: request: GuestRequest<'Input, 'Client> -> GuestCall<'Input>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The input.
    val input: request: GuestRequest<'Input, 'Client> -> PipelineInput<'Input>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The client state at the call's start.
    val client: request: GuestRequest<'Input, 'Client> -> 'Client

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// Stage 2: turns an input and the client state into intents. Language-neutral: an F# function (`InputGuest.sync`,
/// `InputGuest.ofAsync`) or a byte-level module such as a WebAssembly guest (`InputGuest.ofBytes`). A game with no
/// guest uses `InputGuest.sync` over its own mapping.
[<NoEquality; NoComparison>]
type InputGuest<'Input, 'Client, 'Local, 'Command>

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module InputGuest =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A synchronous guest: pure and quick; it has no deadline.
    val sync:
        decide: (GuestRequest<'Input, 'Client> -> GuestOutcome<'Local, 'Command>) ->
            InputGuest<'Input, 'Client, 'Local, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// An asynchronous F# guest under `policy`. It is warm at once; it observes the token, which the pipeline cancels on
    /// a deadline, a rebind or disposal.
    val ofAsync:
        decide:
            (GuestRequest<'Input, 'Client>
                    -> System.Threading.CancellationToken
                    -> Async<GuestOutcome<'Local, 'Command>>) ->
        policy: GuestPolicy ->
            InputGuest<'Input, 'Client, 'Local, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A byte-level guest under `policy`: `encode` writes the request in the game's own wire format; `invoke bytes
    /// started token` runs the module (for WebAssembly, an `FS.GG.Wasm.Browser` host call) and calls `started ()` once
    /// the module is compiled and instantiated and the call has begun, which arms the deadline; `decode` reads the output.
    val ofBytes:
        encode: (GuestRequest<'Input, 'Client> -> byte array) ->
        invoke: (byte array -> (unit -> unit) -> System.Threading.CancellationToken -> Async<GuestReply>) ->
        decode: (byte array -> Result<GuestIntent<'Local, 'Command> list, string>) ->
        policy: GuestPolicy ->
            InputGuest<'Input, 'Client, 'Local, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Admit a request only while `admit client` is `Ok`; `Error reason` is `Refused reason` before any call (BAR's
    /// 64-unit bound, a guest that is not armed yet).
    val withAdmission:
        admit: ('Client -> Result<unit, string>) ->
        guest: InputGuest<'Input, 'Client, 'Local, 'Command> ->
            InputGuest<'Input, 'Client, 'Local, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether the guest answers asynchronously.
    val isAsync: guest: InputGuest<'Input, 'Client, 'Local, 'Command> -> bool

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The guest's policy.
    val policy: guest: InputGuest<'Input, 'Client, 'Local, 'Command> -> GuestPolicy

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Run one request: admission, then the guest, calling `started ()` when it is warm. Never raises: an exception from
    /// `admit`, `decide`, `encode`, `invoke` or `decode` is `Faulted`, a `GuestReply` maps as its cases say, and a
    /// cancelled token is `Cancelled`. For an Elmish game that interprets `PipelineEffect.StartGuest` in its own `Cmd`.
    /// Contract only: FS.GG.Game#788 implements it, and until then it raises.
    val run:
        guest: InputGuest<'Input, 'Client, 'Local, 'Command> ->
        request: GuestRequest<'Input, 'Client> ->
        started: (unit -> unit) ->
        token: System.Threading.CancellationToken ->
            Async<GuestOutcome<'Local, 'Command>>

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// The identity of one command: the client binding the authority issued (`NetworkClientBinding.ClientId`) and a
/// sequence that rises for that binding across every reconnect and rebind, so an id is never two commands. A game sends
/// it IN the envelope or payload, beside `SessionInput` (whose `InputId` stays the product's own stable identity and
/// whose `Sequence` is `CommandId.sequence`). Equal, and ordered, by client and then sequence. Built by the pipeline or
/// `CommandId.tryParse`.
type CommandId =
    private
        {
            Client: string
            Sequence: uint64
        }

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module CommandId =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The client binding.
    val client: id: CommandId -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The sequence within the binding.
    val sequence: id: CommandId -> uint64

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The wire text `client:sequence` (the sequence in decimal).
    val text: id: CommandId -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Read the wire text back: `None` unless it is a non-blank client, a colon and an ASCII decimal sequence of at
    /// least 1. The last colon splits.
    val tryParse: text: string -> CommandId option

/// Public contract type exposed by the FS.GG.Game.Render package.
/// One command as the pipeline sends it. Built by the pipeline.
[<NoEquality; NoComparison>]
type CommandEnvelope<'Command>

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module CommandEnvelope =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The command's identity.
    val id: envelope: CommandEnvelope<'Command> -> CommandId

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The sequence of the input whose guest decision produced it; `None` for `PipelineMsg.Submit`.
    val input: envelope: CommandEnvelope<'Command> -> uint64 option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The version of the authoritative state shown when it was decided. An authority may refuse a stale basis.
    val basis: envelope: CommandEnvelope<'Command> -> uint64

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The command.
    val command: envelope: CommandEnvelope<'Command> -> 'Command

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// What the authority answered for one command. Opaque so later answers are additive. An authority answers a duplicate
/// or stale id from its answer cache (the last answers per client), and `CommandAck.expired` for an id older than the
/// cache. Built by `CommandAck`'s values.
[<NoEquality; NoComparison>]
type CommandAck

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module CommandAck =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Admitted; no effect yet.
    val accepted: CommandAck

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Admitted and waiting behind other work (an arbiter's queue). Restarts the command's patience.
    val queued: CommandAck

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Running. Restarts the command's patience.
    val started: CommandAck

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Still running, with a progress note. Restarts the command's patience.
    val progress: note: string -> CommandAck

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The effect is in the authoritative state from `version` on.
    val applied: version: uint64 -> CommandAck

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A long-lived command ended at `version` with the authority's `outcome` (untrusted text; display it escaped).
    val ended: version: uint64 -> outcome: string -> CommandAck

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Ended after it was accepted, by a cancel or a pre-emption. It may have partly run; unlike `rejected`.
    val cancelled: reason: string -> CommandAck

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Refused before it ran. Nothing happened.
    val rejected: reason: string -> CommandAck

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The authority no longer holds an answer for this id. The command settles `Unconfirmed`.
    val expired: CommandAck

/// Public contract type exposed by the FS.GG.Game.Render package.
/// Where one command stands, as the pipeline knows it. Opaque so later states are additive; read it through the
/// `CommandSettlement` functions.
[<NoEquality; NoComparison>]
type CommandSettlement

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module CommandSettlement =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Sent, with no answer yet.
    val isPending: settlement: CommandSettlement -> bool

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Accepted, queued or running, with no effect seen yet.
    val isAccepted: settlement: CommandSettlement -> bool

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The version from which its effect is authoritative (`applied`, `ended`, or the game's settlement test).
    val appliedAt: settlement: CommandSettlement -> uint64 option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The outcome of an `ended` command.
    val outcome: settlement: CommandSettlement -> string option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Why it was cancelled after acceptance.
    val cancelled: settlement: CommandSettlement -> string option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Why it was rejected.
    val rejected: settlement: CommandSettlement -> string option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// No evidence either way when patience ran out, or the authority's answer expired. No claim is made that it ran.
    val isUnconfirmed: settlement: CommandSettlement -> bool

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether nothing more is expected for it.
    val isFinal: settlement: CommandSettlement -> bool

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
    /// shown is ignored. At most 8 commands are unsettled at once; patience is 600 versions.
    val create: version: ('Server -> uint64) -> CommandStage<'Server, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// At most `limit` (at least one) commands unsettled at once. A decision whose submits do not all fit applies none
    /// of its intents and fails `CommandWindowFull`. A strictly sequential game uses 1.
    val withWindow: limit: int -> stage: CommandStage<'Server, 'Command> -> CommandStage<'Server, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A command that is not final settles `Unconfirmed` once the highest version heard (states and heartbeats) is
    /// `patience` (at least one) past its basis, or past its latest `queued`, `started` or `progress` answer.
    val withPatience: patience: uint64 -> stage: CommandStage<'Server, 'Command> -> CommandStage<'Server, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Settle from the AUTHORITATIVE state (never the predicted one) when the authority sends no `applied`: a command is
    /// applied at the first authoritative version where `settled command state` holds, including commands of an earlier
    /// binding, until their patience runs out.
    val withSettlement:
        settled: ('Command -> 'Server -> bool) ->
        stage: CommandStage<'Server, 'Command> ->
            CommandStage<'Server, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Optional optimistic display: `Displayed.shown` is `predict` of every command in `Displayed.predicted`, in sequence
    /// order, over the latest authoritative state. A command is predicted while it is pending, accepted or running, and
    /// while it is applied or ended at a version the authoritative state has not reached. It never changes the
    /// authoritative state. Without it nothing is predicted and the state shown IS the authoritative one.
    val withPrediction:
        predict: ('Command -> 'Server -> 'Server) ->
        stage: CommandStage<'Server, 'Command> ->
            CommandStage<'Server, 'Command>

/// Public contract type exposed by the FS.GG.Game.Render package.
/// The state to show. Built by the pipeline.
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
/// A failure the pipeline routes to the game, for display and recovery. Never raised. Open: match with a wildcard.
[<RequireQualifiedAccess>]
type PipelineFailure =
    /// The guest faulted on input `sequence`; the guest stage halts until `PipelineMsg.RearmGuest`.
    | GuestFaulted of sequence: uint64 * reason: string
    /// The guest passed its deadline on input `sequence`; only that input failed.
    | GuestTimedOut of sequence: uint64
    /// `failures` consecutive timeouts tripped the breaker; the guest stage halts until `PipelineMsg.RearmGuest`.
    | GuestHalted of failures: int
    /// Input `sequence` was refused (admission, the guest's own rule, or a halted guest stage).
    | InputRefused of sequence: uint64 * reason: string
    /// Input `sequence` ended before a decision (invalidated, frozen, rebound or disposed).
    | InputCancelled of sequence: uint64 * reason: string
    /// The queue was full; these inputs (the arriving one) were dropped, never sent to the guest.
    | QueueOverflow of dropped: uint64 list
    /// The decision for input `sequence` (`None` for a direct `Submit`) did not fit the command window; none of its
    /// intents was applied.
    | CommandWindowFull of sequence: uint64 option
    /// The authority rejected the command before it ran.
    | CommandRejected of id: CommandId * reason: string
    /// The authority ended the command after accepting it.
    | CommandCancelled of id: CommandId * reason: string
    /// The command settled `Unconfirmed`.
    | CommandUnconfirmed of id: CommandId
    /// A `Rebind` was refused (a blank client).
    | RebindRefused of reason: string

/// Public contract type exposed by the FS.GG.Game.Render package.
/// What the game and the effect interpreter tell the pipeline.
[<RequireQualifiedAccess>]
type PipelineMsg<'Input, 'Server, 'Local, 'Command> =
    /// An input for the guest queue: from the `InputRoute` (`Scene`) or the game's own controls (`Game`).
    | Input of input: PipelineInput<'Input>
    /// A command that skips the guest, sharing the window and the command ids.
    | Submit of command: 'Command
    /// The guest is warm and call `call` has begun; the deadline is armed now.
    | GuestRunning of call: uint64
    /// Call `call` ended.
    | GuestEnded of call: uint64 * outcome: GuestOutcome<'Local, 'Command>
    /// Call `call`'s deadline passed.
    | GuestDeadline of call: uint64
    /// Make a halted guest stage usable again and reset the breaker. Nothing else changes.
    | RearmGuest
    /// The authority answered a command.
    | Acknowledged of id: CommandId * ack: CommandAck
    /// A new authoritative state arrived.
    | Authoritative of state: 'Server
    /// The authority is at `version` with no new state for this client: patience advances, prediction does not.
    | Heartbeat of version: uint64
    /// A new or renewed client binding (reconnect, new session). The call in flight and the queue end
    /// `InputCancelled`; command sequences continue above both the highest issued and `lastSequence` (the authority's
    /// `NetworkAdmission.lastSequence`); earlier commands keep settling by state and patience.
    | Rebind of client: string * lastSequence: uint64 option

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// What the pipeline asks its interpreter to do. The effects of one message come in this order: `CancelGuest`, then
/// `ApplyLocal` and `Send` in intent order, then `Fail`, then `StartGuest` of the next call. `ArmDeadline` follows only
/// `GuestRunning`.
[<RequireQualifiedAccess>]
type PipelineEffect<'Input, 'Local, 'Command> =
    /// Start `call`: build its `GuestRequest` with the client state NOW, and run it (`InputGuest.run`).
    | StartGuest of call: GuestCall<'Input>
    /// Cancel call `call`. Its later reply is dropped.
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
type InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>

/// Public contract type exposed by the FS.GG.Game.Render package.
/// The pipeline's state: the binding and sequences, the call in flight and the queue, the breaker, the commands and
/// their settlement, and the latest authoritative state. Built by `InputPipeline.init`.
[<NoEquality; NoComparison>]
type PipelineModel<'Input, 'Server, 'Local, 'Command>

/// Public contract module exposed by the FS.GG.Game.Render package.
///
/// The pipeline as a pure Elmish reducer, like `RendererSelection`: `init` and `update` return effects, which a
/// `PipelineRunner` (or a game's own `Cmd`) performs. The game's `version`, `settled` and `predict` run inside `update`;
/// an exception from them propagates. The server stays the only authority.
[<RequireQualifiedAccess>]
module InputPipeline =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The pipeline of `guest` and `commands`.
    val create:
        guest: InputGuest<'Input, 'Client, 'Local, 'Command> ->
        commands: CommandStage<'Server, 'Command> ->
            InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The guest.
    val guest:
        pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command> ->
            InputGuest<'Input, 'Client, 'Local, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The command stage.
    val commands: pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command> -> CommandStage<'Server, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The first model for `client` (not blank: the binding the authority issued), continuing above `lastSequence` (the
    /// authority's `NetworkAdmission.lastSequence` for it, `None` for a new binding), showing `authoritative`.
    val init:
        pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command> ->
        client: string ->
        lastSequence: uint64 option ->
        authoritative: 'Server ->
            PipelineModel<'Input, 'Server, 'Local, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Apply one message. Contract only: FS.GG.Game#788 implements it, and until then it raises.
    val update:
        pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command> ->
        msg: PipelineMsg<'Input, 'Server, 'Local, 'Command> ->
        model: PipelineModel<'Input, 'Server, 'Local, 'Command> ->
            PipelineModel<'Input, 'Server, 'Local, 'Command> * PipelineEffect<'Input, 'Local, 'Command> list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The current client binding.
    val client: model: PipelineModel<'Input, 'Server, 'Local, 'Command> -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The highest command sequence issued or announced so far (0 when none).
    val lastSequence: model: PipelineModel<'Input, 'Server, 'Local, 'Command> -> uint64

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The state to show.
    val displayed: model: PipelineModel<'Input, 'Server, 'Local, 'Command> -> Displayed<'Server>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Every command that is not final, of any binding, in sequence order.
    val unsettled: model: PipelineModel<'Input, 'Server, 'Local, 'Command> -> (CommandId * CommandSettlement) list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether the guest stage is halted (a fault, or the breaker) until `PipelineMsg.RearmGuest`.
    val guestHalted: model: PipelineModel<'Input, 'Server, 'Local, 'Command> -> bool

/// Public contract type exposed by the FS.GG.Game.Render package.
/// What a runner hands the game: the client state, and the effects that leave the pipeline. Built by
/// `PipelineIo.create`.
[<NoEquality; NoComparison>]
type PipelineIo<'Client, 'Local, 'Command>

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module PipelineIo =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// `client ()` is read when each guest call starts; `applyLocal`, `send` and `fail` receive the effects of those
    /// names; `schedule milliseconds action` runs `action` once after the delay.
    val create:
        client: (unit -> 'Client) ->
        applyLocal: ('Local -> unit) ->
        send: (CommandEnvelope<'Command> -> unit) ->
        fail: (PipelineFailure -> unit) ->
        schedule: (int -> (unit -> unit) -> unit) ->
            PipelineIo<'Client, 'Local, 'Command>

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// Performs `PipelineEffect`s for a game that runs the reducer itself (an Elmish program: `Cmd.ofEffect (fun dispatch
/// -> PipelineRunner.perform effect runner)`). It holds the cancellation of the call in flight. Built by
/// `PipelineRunner.create`.
[<Sealed>]
type PipelineRunner<'Input, 'Client, 'Server, 'Local, 'Command> =
    interface System.IDisposable

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module PipelineRunner =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A runner for `pipeline` that reports `GuestRunning`, `GuestEnded` and `GuestDeadline` to `dispatch`. Disposing
    /// it cancels the call in flight. Contract only: FS.GG.Game#788 implements it, and until then it raises.
    val create:
        pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command> ->
        io: PipelineIo<'Client, 'Local, 'Command> ->
        dispatch: (PipelineMsg<'Input, 'Server, 'Local, 'Command> -> unit) ->
            PipelineRunner<'Input, 'Client, 'Server, 'Local, 'Command>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Perform one effect. Contract only: FS.GG.Game#788 implements it, and until then it raises.
    val perform:
        effect: PipelineEffect<'Input, 'Local, 'Command> ->
        runner: PipelineRunner<'Input, 'Client, 'Server, 'Local, 'Command> ->
            unit

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// Runs `InputPipeline` for a page that is not an Elmish program, as `RendererDriver` runs renderer selection: the
/// reducer, a `PipelineRunner`, and an `InputRoute` bound to `PipelineMsg.Input (Scene event)`. Built by
/// `PipelineHost.start`.
[<Sealed>]
type PipelineHost<'Input, 'Client, 'Server, 'Local, 'Command> =
    interface System.IDisposable

    /// Public contract member exposed by the FS.GG.Game.Render package.
    /// The current model.
    member Model: PipelineModel<'Input, 'Server, 'Local, 'Command>

    /// Public contract member exposed by the FS.GG.Game.Render package.
    /// Apply one message (a game input, a submit, an answer, a state, a rebind) and run its effects. Ignored after
    /// `Dispose`.
    member Dispatch: msg: PipelineMsg<'Input, 'Server, 'Local, 'Command> -> unit

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module PipelineHost =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Start `pipeline` for `client` above `lastSequence` on `authoritative`, bound to `route`. Disposing it cancels the
    /// call in flight and unbinds `route`. Contract only: FS.GG.Game#788 implements it, and until then it raises.
    val start:
        pipeline: InputPipeline<'Input, 'Client, 'Server, 'Local, 'Command> ->
        route: InputRoute ->
        io: PipelineIo<'Client, 'Local, 'Command> ->
        client: string ->
        lastSequence: uint64 option ->
        authoritative: 'Server ->
            PipelineHost<'Input, 'Client, 'Server, 'Local, 'Command>

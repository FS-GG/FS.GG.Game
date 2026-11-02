namespace FS.GG.Game.Reference

open FS.GG.Game.Core
open FS.GG.Game.Render
open FS.GG.Game.Reference.ReferenceState

/// The reference game played through the standard input pipeline (FS.GG.Game#788, ADR 0001), end to end: an input
/// from any backend's input host goes to an F# guest, the guest's command goes to an authority that admits it with
/// `NetworkAdmission` and answers from a cache, and the authoritative snapshot comes back and is shown. Plain F# with
/// no browser dependency, so the .NET tests and the Fable browser smoke run the same code.
module ReferencePipeline =

    /// A request to the authority.
    [<RequireQualifiedAccess>]
    type Command =
        /// Move the player one column right.
        | Step
        /// Aim at a scene point.
        | AimAt of x: int * y: int

    /// What the authority shows: the game state at a version.
    type Snapshot = { Version: uint64; Model: Model }

    let commandText (command: Command) =
        match command with
        | Command.Step -> "step"
        | Command.AimAt(x, y) -> sprintf "aim %d %d" x y

    /// The authority's rule.
    let applyCommand (command: Command) (model: Model) : Model =
        match command with
        | Command.Step when model.Screen = Playing -> { model with X = model.X + 1 }
        | Command.Step -> model
        | Command.AimAt(x, y) -> { model with Aim = x, y }

    /// The guest, an F# rule: activating the token selects it and steps; activating another object selects it; a press
    /// on empty ground aims there; Cancel clears the selection. Every activation is decided, a repeated one too.
    let decide (input: PipelineInput<unit>) : GuestOutcome<SelectionRequest, Command> =
        match input with
        | PipelineInput.Scene event ->
            match InputEvent.action event, InputEvent.target event, InputEvent.point event with
            | InputAction.Activate, Some "token", _ ->
                GuestOutcome.Decided
                    [
                        GuestIntent.Local(SelectionRequest.Select "token")
                        GuestIntent.Submit Command.Step
                    ]
            | InputAction.Activate, Some id, _ -> GuestOutcome.Decided [ GuestIntent.Local(SelectionRequest.Select id) ]
            | InputAction.Activate, None, Some point ->
                GuestOutcome.Decided [ GuestIntent.Submit(Command.AimAt(int point.X, int point.Y)) ]
            | InputAction.Cancel, _, _ -> GuestOutcome.Decided [ GuestIntent.Local SelectionRequest.Clear ]
            | _ -> GuestOutcome.Decided []
        | PipelineInput.Game() -> GuestOutcome.Decided []

    /// The sync guest (warm at once).
    let guest: InputGuest<unit, Model, SelectionRequest, Command> =
        InputGuest.sync (fun request -> decide (GuestCall.input (GuestRequest.call request)))

    /// The pipeline: the guest, and a command stage over snapshot versions with a window of 8 and the step predicted
    /// (marked) until the authority's snapshot reaches it.
    let pipeline: InputPipeline<unit, Model, Snapshot, SelectionRequest, Command> =
        InputPipeline.create
            guest
            (CommandStage.create (fun (snapshot: Snapshot) -> snapshot.Version)
             |> CommandStage.withPrediction (fun command snapshot ->
                 { snapshot with
                     Model = applyCommand command snapshot.Model
                 }))

    /// The session, binding and input identity the authority admits commands under.
    let session = "reference"
    let inputId = "reference.command"

    /// The authority (the server): admission by `NetworkAdmission` (one rising sequence per client), the authoritative
    /// snapshot, and the answers to the last 64 admitted commands, so a duplicate or stale command is answered from the
    /// cache and never applied twice (ADR 0001, stage 3 duties).
    type Authority =
        {
            Admission: NetworkAdmissionState<Command>
            Snapshot: Snapshot
            Answers: (uint64 * CommandAck) list
        }

    /// How many answers the authority keeps per client.
    let cacheSize = 64

    [<RequireQualifiedAccess>]
    module Authority =

        let binding (client: string) : NetworkClientBinding =
            {
                SessionId = session
                ClientId = client
                ReconnectToken = client + "-token"
            }

        let create (client: string) (model: Model) : Authority =
            match NetworkAdmission.create session [ binding client ] with
            | Ok admission ->
                {
                    Admission = admission
                    Snapshot = { Version = 0UL; Model = model }
                    Answers = []
                }
            | Error issues -> failwithf "the reference authority could not start: %A" issues

        /// The authority's last admitted sequence for `client`, sent on connect.
        let lastSequence (client: string) (authority: Authority) =
            NetworkAdmission.lastSequence client authority.Admission

        /// Receive one command: the authority's next state, its answer, and the new snapshot when it applied.
        let receive (envelope: CommandEnvelope<Command>) (authority: Authority) : Authority * CommandAck * Snapshot option =
            let id = CommandEnvelope.id envelope
            let sequence = CommandId.sequence id

            let candidate: NetworkInput<Command> =
                {
                    Binding = binding (CommandId.client id)
                    Input =
                        {
                            SessionId = session
                            InputId = inputId
                            Sequence = sequence
                            Value = CommandEnvelope.command envelope
                        }
                }

            match NetworkAdmission.admit (fun _ -> Ok()) candidate authority.Admission with
            | Ok(admission, _) ->
                let snapshot =
                    {
                        Version = authority.Snapshot.Version + 1UL
                        Model = applyCommand (CommandEnvelope.command envelope) authority.Snapshot.Model
                    }

                let ack = CommandAck.applied snapshot.Version

                {
                    Admission = admission
                    Snapshot = snapshot
                    Answers = (sequence, ack) :: authority.Answers |> List.truncate cacheSize
                },
                ack,
                Some snapshot
            | Error(NetworkAdmissionIssue.DuplicateInputSequence _)
            | Error(NetworkAdmissionIssue.StaleInputSequence _) ->
                match authority.Answers |> List.tryFind (fun (answered, _) -> answered = sequence) with
                | Some(_, cached) -> authority, cached, None
                | None ->
                    let oldest =
                        authority.Answers |> List.tryLast |> Option.map fst |> Option.defaultValue 0UL

                    if authority.Answers.Length >= cacheSize && sequence < oldest then
                        authority, CommandAck.expired, None
                    else
                        authority, CommandAck.rejected "stale", None
            | Error issue -> authority, CommandAck.rejected (sprintf "%A" issue), None

    /// What the game shows: the displayed snapshot's model (the prediction folded in) with the client's selection.
    let shown (model: PipelineModel<unit, Snapshot, SelectionRequest, Command>) (selection: string option) : Model =
        { (Displayed.shown (InputPipeline.displayed model)).Model with
            Selected = selection
        }

    /// One client and its authority, wired: every command is delivered to the authority through `deliver` (a network:
    /// at once, or later), and its answer and snapshot come back through it. `shown` hears every state the game shows;
    /// `sent` hears every command as it leaves the client.
    [<Sealed>]
    type Session
        (
            client: string,
            start: Model,
            route: InputRoute,
            deliver: (unit -> unit) -> unit,
            schedule: int -> (unit -> unit) -> unit,
            onShown: Model -> unit,
            onSent: CommandEnvelope<Command> -> unit
        ) =
        let mutable authority = Authority.create client start
        let mutable selection: string option = None
        let mutable host: PipelineHost<unit, Model, Snapshot, SelectionRequest, Command> option = None
        let failures = ResizeArray<PipelineFailure>()

        let current () =
            match host with
            | Some running -> shown running.Model selection
            | None -> { start with Selected = selection }

        let show () = onShown (current ())

        let applyLocal (request: SelectionRequest) =
            selection <-
                match request with
                | SelectionRequest.Select id -> Some id
                | SelectionRequest.Clear -> None

            show ()

        let send (envelope: CommandEnvelope<Command>) =
            onSent envelope

            deliver (fun () ->
                let next, ack, snapshot = Authority.receive envelope authority
                authority <- next

                deliver (fun () ->
                    match host with
                    | Some running ->
                        running.Dispatch(PipelineMsg.Acknowledged(CommandEnvelope.id envelope, ack))
                        snapshot |> Option.iter (fun s -> running.Dispatch(PipelineMsg.Authoritative s))
                        show ()
                    | None -> ()))

        let io = PipelineIo.create current applyLocal send failures.Add schedule

        do
            host <-
                Some(
                    PipelineHost.start
                        pipeline
                        route
                        io
                        client
                        (Authority.lastSequence client authority)
                        authority.Snapshot
                )

        /// The authority's current snapshot.
        member _.Authoritative = authority.Snapshot

        /// The pipeline's model.
        member _.Model = host.Value.Model

        /// What the game shows now.
        member _.Shown = current ()

        /// The failures the pipeline routed to the game.
        member _.Failures = List.ofSeq failures

        /// Unbind the route and cancel the call in flight.
        member _.Dispose() =
            host |> Option.iter (fun running -> (running :> System.IDisposable).Dispose())

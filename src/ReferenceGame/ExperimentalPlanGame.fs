namespace FS.GG.Game.Reference

open FS.GG.Game.Core
open FS.GG.UI.Scene
open FS.GG.Game.Render
open FS.GG.Game.Reference.ReferenceState
open FS.GG.Game.Reference.ExperimentalClientPresentation

/// EXPERIMENTAL. NOT SHIPPED. NOT A CONTRACT. (FS.GG.Game#810, GAME-RENDER-02.9)
///
/// ReferenceGame played with a planned move: a press on the token arms a drag, dragging draws a move arrow through the
/// F# guest's `Local`s (`ClientEffect.Plan`), Enter commits the plan and submits `MoveTo`, and the arrow follows the
/// pending command until it settles. The input stays client input -> guest -> server, and also client input -> guest ->
/// client. Plain F# with no browser dependency, so the .NET tests and the Fable browser smoke run the same code.
///
/// The input host (#797) reports presses, hovers and keys only: drag and release arrive as `PipelineInput.Game` from a
/// game-owned listener until the next major version adds continuous input to `InputAction` (the design's request 3b).
module ExperimentalPlanGame =

    /// The game's own continuous input, fed by the page's drag listener (coalesced to one move per animation frame).
    type PlanInput =
        | DragTo of point: Point
        | Release of point: Point * dragged: bool

    /// The game's escape-hatch effects: arm and disarm the drag that started on the token.
    type Custom =
        | Arm
        | Disarm

    /// A request to the authority. It carries the plan id, so the client links the plan to the command.
    type Command = MoveTo of plan: string * x: int * y: int

    /// The authority's state at a version.
    type Snapshot = { Version: uint64; Model: Model }

    /// What the guest reads, and what the backends draw: the shown game, the client's presentation, and the drag arm.
    type Client =
        {
            Model: Model
            Presentation: Presentation
            Armed: bool
        }

    /// The move plan's id (one draft at a time; a committed plan follows its command and frees the id).
    let movePlan = PlanId "move"

    /// The plan a command carries.
    let planOf (MoveTo(plan, _, _)) = Some(PlanId plan)

    /// The column the authority refuses: a wall.
    let blockedColumn = 0

    /// The token column (0 to 6) of a scene x.
    let columnOf (x: int) =
        int (floor ((float x - 16.0) / 32.0)) |> max 0 |> min 6

    let commandText (MoveTo(plan, x, y)) = sprintf "move %s %d %d" plan x y

    /// The authority's rule.
    let applyCommand (MoveTo(_, x, _)) (model: Model) : Model = { model with X = columnOf x }

    let private tokenId = "token"

    let private draftTarget (presentation: Presentation) =
        match draft movePlan presentation with
        | Some entry ->
            entry.Elements
            |> List.tryPick (fun element ->
                match element with
                | Arrow(_, AtPoint point, Move) -> Some point
                | _ -> None)
        | None -> None

    let private arrowTo (point: Point) =
        ClientEffect.Plan(movePlan, [ Arrow(AtObject tokenId, AtPoint point, Move) ])

    let private local effect = GuestIntent.Local effect

    /// The guest, an F# rule.
    /// - A pointer press on the token selects it (primary) and arms the drag; dragging draws the move arrow; release
    ///   keeps it as a draft. A release with no drag only disarms.
    /// - With the token selected and nothing armed, a press on empty ground drafts the arrow there: the single-pointer
    ///   alternative to dragging (WCAG 2.5.7).
    /// - Enter or Space on the token (or the mirror's activation) commits the draft and submits `MoveTo`.
    /// - Escape clears drafts and settled plans first, then the selection. A plan that follows a sent command stays.
    let decide (client: Client) (input: PipelineInput<PlanInput>) : GuestOutcome<ClientEffect<Custom>, Command> =
        let presentation = client.Presentation

        match input with
        | PipelineInput.Scene event ->
            match InputEvent.action event, InputEvent.target event, InputEvent.source event with
            | InputAction.Activate, Some "token", InputSource.Pointer ->
                GuestOutcome.Decided [ local (Select(Primary, [ tokenId ])); local (Custom Arm) ]
            | InputAction.Activate, Some "token", _ ->
                match draftTarget presentation with
                | Some point ->
                    GuestOutcome.Decided
                        [
                            local (Commit(movePlan, ClearWhenShown))
                            GuestIntent.Submit(MoveTo("move", int (round point.X), int (round point.Y)))
                        ]
                | None -> GuestOutcome.Decided [ local (Select(Primary, [ tokenId ])) ]
            | InputAction.Activate, Some id, _ -> GuestOutcome.Decided [ local (Select(Primary, [ id ])) ]
            | InputAction.Activate, None, InputSource.Pointer when
                presentation.Primary = Some tokenId && not client.Armed
                ->
                match InputEvent.point event with
                | Some point -> GuestOutcome.Decided [ local (arrowTo point) ]
                | None -> GuestOutcome.Decided []
            | InputAction.Cancel, _, _ ->
                let clearable =
                    presentation.Plans
                    |> List.exists (fun entry -> entry.Phase = Draft || entry.Phase = Committed || entry.Phase = Done || isFailed entry.Phase)

                if clearable then
                    GuestOutcome.Decided [ local ClearPlans ]
                else
                    GuestOutcome.Decided [ local ClearSelection ]
            | _ -> GuestOutcome.Decided []
        | PipelineInput.Game(DragTo point) when client.Armed -> GuestOutcome.Decided [ local (arrowTo point) ]
        | PipelineInput.Game(Release(point, true)) when client.Armed ->
            GuestOutcome.Decided [ local (arrowTo point); local (Custom Disarm) ]
        | PipelineInput.Game(Release _) when client.Armed -> GuestOutcome.Decided [ local (Custom Disarm) ]
        | PipelineInput.Game _ -> GuestOutcome.Decided []

    /// The sync guest (warm at once).
    let guest: InputGuest<PlanInput, Client, ClientEffect<Custom>, Command> =
        InputGuest.sync (fun request -> decide (GuestRequest.client request) (GuestCall.input (GuestRequest.call request)))

    /// The pipeline: the guest and a command stage over snapshot versions, with no prediction (a plan anchors at the
    /// authoritative token, so a predicted move would not move its own arrow's start: see the design's Q5).
    let pipeline (window: int) : InputPipeline<PlanInput, Client, Snapshot, ClientEffect<Custom>, Command> =
        InputPipeline.create guest (CommandStage.create (fun (snapshot: Snapshot) -> snapshot.Version) |> CommandStage.withWindow window)

    let private session = "reference-plan"
    let private inputId = "reference.plan"

    /// The authority: admission by Game.Core's `NetworkAdmission` (a duplicate or stale id is refused and never applied
    /// twice), `MoveTo` applied at a new version or refused into the wall. It keeps no answer cache: #799's reference
    /// authority covers stage 3's cache duty; this proof's subject is the client side.
    type Authority =
        {
            Admission: NetworkAdmissionState<Command>
            Snapshot: Snapshot
        }

    [<RequireQualifiedAccess>]
    module Authority =

        let private binding (client: string) : NetworkClientBinding =
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
                }
            | Error issues -> failwithf "the plan authority could not start: %A" issues

        let lastSequence (client: string) (authority: Authority) =
            NetworkAdmission.lastSequence client authority.Admission

        /// Receive one command: the next state, the answer, and the new snapshot when it applied.
        let receive (envelope: CommandEnvelope<Command>) (authority: Authority) : Authority * CommandAck * Snapshot option =
            let id = CommandEnvelope.id envelope
            let command = CommandEnvelope.command envelope

            let candidate: NetworkInput<Command> =
                {
                    Binding = binding (CommandId.client id)
                    Input =
                        {
                            SessionId = session
                            InputId = inputId
                            Sequence = CommandId.sequence id
                            Value = command
                        }
                }

            match NetworkAdmission.admit (fun _ -> Ok()) candidate authority.Admission with
            | Ok(admission, _) ->
                let (MoveTo(_, x, _)) = command

                if columnOf x = blockedColumn then
                    { authority with Admission = admission }, CommandAck.rejected "blocked", None
                else
                    let snapshot =
                        {
                            Version = authority.Snapshot.Version + 1UL
                            Model = applyCommand command authority.Snapshot.Model
                        }

                    {
                        Admission = admission
                        Snapshot = snapshot
                    },
                    CommandAck.applied snapshot.Version,
                    Some snapshot
            | Error issue -> authority, CommandAck.rejected (sprintf "%A" issue), None

    // ---------------------------------------------------------------------------------------------------------------
    // Renderers: ReferenceGame's Scene, then the plan pass (bodies -> plans; the host's cues draw above)
    // ---------------------------------------------------------------------------------------------------------------

    let private rectOf (x: float, y: float, w: float, h: float) : Rect = { X = x; Y = y; Width = w; Height = h }

    /// The selectable objects: the reference game's, at the shown state.
    let objects (client: Client) : SelectableObject list = ReferenceRenderers.objects client.Model

    /// An object's frame, for plan anchors.
    let bounds (client: Client) (id: string) : Rect option =
        objects client |> List.tryFind (fun target -> target.Id = id) |> Option.map (fun target -> target.Bounds)

    /// The token's frame.
    let tokenRect (model: Model) : Rect = rectOf (ReferenceRenderers.tokenBounds model)

    /// The selection is the presentation's primary. A `SelectionRequest` (the mirror without a pipeline) sets it.
    let interaction: SceneInteraction<Client> =
        SceneInteraction.create
            objects
            (fun client -> client.Presentation.Primary)
            (fun request client ->
                let effect =
                    match request with
                    | SelectionRequest.Select id -> Select(Primary, [ id ])
                    | SelectionRequest.Clear -> ClearSelection

                { client with
                    Presentation = fst (apply effect client.Presentation)
                })

    /// The Scene: the reference game's bodies (its selection ring included), then the plan pass.
    let view (client: Client) : Scene =
        let game =
            ReferenceRenderers.view
                { client.Model with
                    Selected = client.Presentation.Primary
                }

        {
            Nodes = game.Nodes @ planPass proofInks (bounds client) client.Presentation
        }

    /// The renderer declaration, as `ReferenceRenderers.declare` does for the reference game.
    let declare (svg: Scene -> Result<RendererMount<Scene>, string>) (surfaces: SceneSurface list) : SceneRenderers<Client> =
        surfaces
        |> List.fold (fun renderers surface -> SceneRenderers.withSurface surface renderers) (SceneRenderers.create view svg)

    /// The label of an object, for the plan summary.
    let label (id: string) =
        match id with
        | "token" -> "Player token"
        | "aim" -> "Aim marker"
        | other -> other

    // ---------------------------------------------------------------------------------------------------------------
    // The session: one client, its presentation and its authority
    // ---------------------------------------------------------------------------------------------------------------

    /// One client wired to its authority through `deliver` (a network: now, later, or held by a test). `onShown` hears
    /// every state the game shows; `onSent` hears every command as it leaves; `onAnnounce` hears each polite
    /// announcement (the live region).
    [<Sealed>]
    type Session
        (
            client: string,
            start: Model,
            window: int,
            route: InputRoute,
            deliver: (unit -> unit) -> unit,
            schedule: int -> (unit -> unit) -> unit,
            onShown: Client -> unit,
            onSent: CommandEnvelope<Command> -> unit,
            onAnnounce: string -> unit
        ) =
        let mutable authority = Authority.create client start
        let mutable presentation = ExperimentalClientPresentation.empty
        let mutable armed = false
        let mutable host: PipelineHost<PlanInput, Client, Snapshot, ClientEffect<Custom>, Command> option = None
        let failures = ResizeArray<PipelineFailure>()
        let customs = ResizeArray<Custom>()

        let shownModel () =
            match host with
            | Some running -> (Displayed.shown (InputPipeline.displayed running.Model)).Model
            | None -> start

        let current () =
            {
                Model = shownModel ()
                Presentation = presentation
                Armed = armed
            }

        // Follow the linked plans, then announce. Safe from inside a batch: PipelineHost updates its model before it
        // performs the batch, and a command's Fail never shares a batch with ApplyLocal or Send.
        let show () =
            match host with
            | Some running ->
                let model = running.Model

                presentation <-
                    track (InputPipeline.unsettled model) (Displayed.predicted (InputPipeline.displayed model)) presentation
            | None -> ()

            let next, said = drain presentation
            presentation <- next
            said |> List.iter onAnnounce
            onShown (current ())

        let applyLocal (effect: ClientEffect<Custom>) =
            let next, games = apply effect presentation
            presentation <- next

            for custom in games do
                customs.Add custom

                armed <-
                    match custom with
                    | Arm -> true
                    | Disarm -> false

            show ()

        let send (envelope: CommandEnvelope<Command>) =
            presentation <- sent planOf envelope presentation
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

            show ()

        let fail (failure: PipelineFailure) =
            failures.Add failure
            presentation <- failed failure presentation

        let io = PipelineIo.create current applyLocal send fail schedule

        do
            host <-
                Some(
                    PipelineHost.start
                        (pipeline window)
                        route
                        io
                        client
                        (Authority.lastSequence client authority)
                        authority.Snapshot
                )

        /// Feed the game's own input (drag and release) into the same queue as the input host's events.
        member _.Input(input: PlanInput) =
            host |> Option.iter (fun running -> running.Dispatch(PipelineMsg.Input(PipelineInput.Game input)))
            show ()

        /// Dispatch a pipeline message (a test's rebind, for example).
        member _.Dispatch(msg: PipelineMsg<PlanInput, Snapshot, ClientEffect<Custom>, Command>) =
            host |> Option.iter (fun running -> running.Dispatch msg)
            show ()

        member _.Authoritative = authority.Snapshot
        member _.Model = host.Value.Model
        member _.Shown = current ()
        member _.Presentation = presentation
        member _.Failures = List.ofSeq failures
        member _.Customs = List.ofSeq customs

        member _.Dispose() =
            host |> Option.iter (fun running -> (running :> System.IDisposable).Dispose())

namespace FS.GG.Game.Reference

open FS.GG.UI.Scene
open FS.GG.Game.Render

/// EXPERIMENTAL. NOT SHIPPED. NOT A CONTRACT. (FS.GG.Game#810, GAME-RENDER-02.9)
///
/// The working proof of `docs/roadmaps/game-render-02-client-presentation.md`: the standard client-effect vocabulary a
/// guest returns as its `Local`s, the client-only presentation reducer that applies it, its link to the pipeline's
/// pending commands, a default byte codec for the WebAssembly boundary, and the plan pass drawn in the portable profile.
/// ReferenceGame is not packed (`IsPackable=false`), so nothing here reaches a package. The design proposes opaque types in
/// `FS.GG.Game.Render` once #797-#799 merge; this module uses plain unions so the proof stays small. Delete it when the
/// library version lands.
module ExperimentalClientPresentation =

    // ---------------------------------------------------------------------------------------------------------------
    // The vocabulary (design section "The vocabulary")
    // ---------------------------------------------------------------------------------------------------------------

    /// A plan's id, chosen by the guest. It names a DRAFT: once committed, the plan follows its command, and the id is
    /// free for the next draft.
    type PlanId = PlanId of string

    /// A selection role: the one primary object, or a member of the group.
    type SelectionRole =
        | Primary
        | Group

    /// A highlight role on an object.
    type HighlightRole =
        | Target
        | Valid
        | Invalid

    /// A plan element's style. Every style has its own head shape, so it never rests on hue alone.
    type PlanStyle =
        | Move
        | Attack
        | Support

    /// Where a plan element starts or ends: a scene point, or an object of the frame (it follows that object's bounds).
    type PlanAnchor =
        | AtPoint of Point
        | AtObject of id: string

    /// The family of a placement ghost (mirrors `GlyphFamily` on main, #806).
    type GhostFamily =
        | UnitGhost
        | StructureGhost
        | TerrainGhost
        | ItemGhost
        | MarkerGhost

    /// The identity of a placement ghost (mirrors `FS.GG.Game.Render.Identity` on main, #806, which the #799 stack
    /// this proof sits on does not have yet).
    type GhostIdentity =
        | Own
        | Ally
        | Neutral
        | Enemy
        | Unknown

    /// A placement ghost: the wire subset of a `GlyphSpec` (its anchor in scene units, its footprint in cells), valid or
    /// invalid.
    type Ghost =
        {
            Family: GhostFamily
            Identity: GhostIdentity
            Icon: string option
            Team: int option
            Anchor: Point
            Footprint: Size
            Valid: bool
        }

    /// An area or template shape.
    type AreaShape =
        | Circle of centre: Point * radius: float
        | Box of Rect

    /// One element of a plan overlay.
    type PlanElement =
        | Arrow of from: PlanAnchor * target: PlanAnchor * style: PlanStyle
        | Path of waypoints: PlanAnchor list * style: PlanStyle
        | GhostAt of Ghost
        | Area of shape: AreaShape * style: PlanStyle
        | TargetLine of from: PlanAnchor * target: PlanAnchor
        | Label of anchor: PlanAnchor * text: string

    /// What happens to a plan whose command succeeded once the authoritative state shows it.
    type PlanSettle =
        | ClearWhenShown
        | KeepWhenShown

    /// A client-only change a guest returns as `GuestIntent.Local`. It is never sent to the authority. The game's
    /// `'Local` is `ClientEffect<'Custom>`; `Custom` is the game's escape hatch.
    type ClientEffect<'Custom> =
        | Select of role: SelectionRole * ids: string list
        | Add of role: SelectionRole * ids: string list
        | Remove of role: SelectionRole * ids: string list
        | ClearSelection
        | Highlight of id: string * role: HighlightRole
        | Unhighlight of id: string * role: HighlightRole
        | ClearHighlights
        | Plan of plan: PlanId * elements: PlanElement list
        | Commit of plan: PlanId * settle: PlanSettle
        | ClearPlan of plan: PlanId
        | ClearPlans
        | Custom of value: 'Custom

    // ---------------------------------------------------------------------------------------------------------------
    // The presentation reducer (design section "Lifecycle and semantics")
    // ---------------------------------------------------------------------------------------------------------------

    /// Where a plan is in its life.
    type PlanPhase =
        /// Built by the guest, not committed.
        | Draft
        /// Committed, waiting for its command to leave the client.
        | Committed
        /// Linked to a sent command that has no answer yet.
        | Pending
        /// Linked; the authority accepted it.
        | Accepted
        /// Linked; waiting behind the authority's queue.
        | Queued
        /// Linked; the command has begun.
        | Running
        /// Linked; final and successful, not yet shown by the authoritative state.
        | Applied
        /// Shown, kept because the plan was committed with `KeepWhenShown`.
        | Done
        /// Refused before it ran.
        | Refused of reason: string
        /// Ended after acceptance; it may have partly run.
        | Cancelled of reason: string
        /// Never confirmed (patience ran out, or the answer expired).
        | Unconfirmed

    let isOpen (phase: PlanPhase) =
        match phase with
        | Pending
        | Accepted
        | Queued
        | Running -> true
        | _ -> false

    let isFailed (phase: PlanPhase) =
        match phase with
        | Refused _
        | Cancelled _
        | Unconfirmed -> true
        | _ -> false

    /// Whether the phase follows a command (linked, open or final).
    let isLinkedPhase (phase: PlanPhase) =
        isOpen phase || phase = Applied || phase = Done || isFailed phase

    type PlanEntry =
        {
            Id: PlanId
            Elements: PlanElement list
            Phase: PlanPhase
            Settle: PlanSettle
            Command: CommandId option
            /// Creation order, for drawing and for the summary.
            Order: uint64
        }

    /// The client's presentation state: game-agnostic, client-only, never sent, never read by `predict`.
    type Presentation =
        {
            Primary: string option
            Group: string list
            Highlights: (string * HighlightRole) list
            Plans: PlanEntry list
            Next: uint64
            /// Phase changes to announce politely, oldest first (drained by the host).
            Announcements: string list
            /// Effects that changed nothing, and why, oldest first.
            Diagnostics: string list
        }

    let empty: Presentation =
        {
            Primary = None
            Group = []
            Highlights = []
            Plans = []
            Next = 1UL
            Announcements = []
            Diagnostics = []
        }

    let private styleName (style: PlanStyle) =
        match style with
        | Move -> "Move"
        | Attack -> "Attack"
        | Support -> "Support"

    let private anchorText (label: string -> string) (anchor: PlanAnchor) =
        match anchor with
        | AtPoint p -> sprintf "(%g, %g)" p.X p.Y
        | AtObject id -> label id

    let private phaseText (phase: PlanPhase) =
        match phase with
        | Draft -> "draft"
        | Committed -> "committed"
        | Pending -> "sent"
        | Accepted -> "accepted"
        | Queued -> "queued"
        | Running -> "running"
        | Applied -> "applied"
        | Done -> "done"
        | Refused reason -> "refused: " + reason
        | Cancelled reason -> "cancelled: " + reason
        | Unconfirmed -> "unconfirmed"

    let private elementText (label: string -> string) (element: PlanElement) =
        match element with
        | Arrow(from, target, style) -> sprintf "%s %s to %s" (styleName style) (anchorText label from) (anchorText label target)
        | Path(points, style) -> sprintf "%s along %s" (styleName style) (points |> List.map (anchorText label) |> String.concat ", ")
        | GhostAt ghost -> sprintf "Place at (%g, %g)%s" ghost.Anchor.X ghost.Anchor.Y (if ghost.Valid then "" else ", invalid")
        | Area(_, style) -> sprintf "%s area" (styleName style)
        | TargetLine(from, target) -> sprintf "Target %s from %s" (anchorText label target) (anchorText label from)
        | Label(_, text) -> text

    /// The text summary of one plan, for the mirror and the live region.
    let describe (label: string -> string) (entry: PlanEntry) =
        let body =
            match entry.Elements with
            | [] -> "Empty plan"
            | elements -> elements |> List.map (elementText label) |> String.concat "; "

        sprintf "%s, %s" body (phaseText entry.Phase)

    /// The text summary of every plan, in creation order.
    let summary (label: string -> string) (presentation: Presentation) =
        presentation.Plans |> List.sortBy (fun entry -> entry.Order) |> List.map (describe label)

    let private announce (text: string) (presentation: Presentation) =
        { presentation with
            Announcements = presentation.Announcements @ [ text ]
        }

    let private diagnose (text: string) (presentation: Presentation) =
        { presentation with
            Diagnostics = presentation.Diagnostics @ [ text ]
        }

    let private planName (PlanId name) = name

    let private distinct (ids: string list) = ids |> List.distinct

    let private applySelection (role: SelectionRole) (change: string list -> string list) (presentation: Presentation) =
        match role with
        | Primary ->
            let next = change (Option.toList presentation.Primary)

            match next with
            | []
            | [ _ ] ->
                { presentation with
                    Primary = List.tryHead next
                }
            | _ -> diagnose "the primary selection holds at most one object" presentation
        | Group ->
            { presentation with
                Group = change presentation.Group |> distinct
            }

    /// Plans the client may remove: drafts, unsent commits, and settled plans the player has seen. Never a plan that
    /// follows an open or applied command: that overlay is a view of the command and ends when the command does.
    let private removable (entry: PlanEntry) =
        match entry.Phase with
        | Draft
        | Committed
        | Done -> true
        | phase -> isFailed phase

    /// Apply one effect. Returns the new presentation and the game's own `Custom` effects, in order.
    let apply (effect: ClientEffect<'Custom>) (presentation: Presentation) : Presentation * 'Custom list =
        match effect with
        | Select(role, ids) -> applySelection role (fun _ -> distinct ids) presentation, []
        | Add(role, ids) ->
            match role with
            | Primary -> applySelection role (fun current -> if ids.IsEmpty then current else distinct ids) presentation, []
            | Group -> applySelection role (fun current -> current @ ids) presentation, []
        | Remove(role, ids) ->
            applySelection role (List.filter (fun id -> not (List.contains id ids))) presentation, []
        | ClearSelection -> { presentation with Primary = None; Group = [] }, []
        | Highlight(id, role) ->
            if presentation.Highlights |> List.contains (id, role) then
                presentation, []
            else
                { presentation with
                    Highlights = presentation.Highlights @ [ id, role ]
                },
                []
        | Unhighlight(id, role) ->
            { presentation with
                Highlights = presentation.Highlights |> List.filter ((<>) (id, role))
            },
            []
        | ClearHighlights -> { presentation with Highlights = [] }, []
        | Plan(plan, elements) ->
            // A new draft replaces the refusal or completion of the same id the player has already seen.
            let kept =
                presentation.Plans
                |> List.filter (fun entry -> not (entry.Id = plan && (isFailed entry.Phase || entry.Phase = Done)))

            match kept |> List.tryFind (fun entry -> entry.Id = plan && entry.Phase = Draft) with
            | Some _ ->
                { presentation with
                    Plans =
                        kept
                        |> List.map (fun entry ->
                            if entry.Id = plan && entry.Phase = Draft then
                                { entry with Elements = elements }
                            else
                                entry)
                },
                []
            | None ->
                let entry =
                    {
                        Id = plan
                        Elements = elements
                        Phase = Draft
                        Settle = ClearWhenShown
                        Command = None
                        Order = presentation.Next
                    }

                { presentation with
                    Plans = kept @ [ entry ]
                    Next = presentation.Next + 1UL
                }
                |> announce (sprintf "Plan %s drafted" (planName plan)),
                []
        | Commit(plan, settle) ->
            match presentation.Plans |> List.tryFind (fun entry -> entry.Id = plan && entry.Phase = Draft) with
            | None -> diagnose (sprintf "commit %s: no draft with that id" (planName plan)) presentation, []
            | Some draft ->
                let replaced =
                    presentation.Plans |> List.exists (fun entry -> entry.Id = plan && entry.Phase = Committed)

                let plans =
                    presentation.Plans
                    |> List.filter (fun entry -> not (entry.Id = plan && entry.Phase = Committed))
                    |> List.map (fun entry ->
                        if obj.ReferenceEquals(entry, draft) then
                            { entry with
                                Phase = Committed
                                Settle = settle
                            }
                        else
                            entry)

                let next = { presentation with Plans = plans }

                (if replaced then
                     diagnose (sprintf "commit %s replaced a commit whose command never left" (planName plan)) next
                 else
                     next),
                []
        | ClearPlan plan ->
            let before = presentation.Plans.Length

            let plans =
                presentation.Plans |> List.filter (fun entry -> not (entry.Id = plan && removable entry))

            let next = { presentation with Plans = plans }

            (if plans.Length < before then
                 announce (sprintf "Plan %s cleared" (planName plan)) next
             else
                 next),
            []
        | ClearPlans ->
            let before = presentation.Plans.Length
            let plans = presentation.Plans |> List.filter (removable >> not)
            let next = { presentation with Plans = plans }
            (if plans.Length < before then announce "Plans cleared" next else next), []
        | Custom value -> presentation, [ value ]

    /// Apply effects in order.
    let applyAll (effects: ClientEffect<'Custom> list) (presentation: Presentation) : Presentation * 'Custom list =
        effects
        |> List.fold
            (fun (current, customs) effect ->
                let next, more = apply effect current
                next, customs @ more)
            (presentation, [])

    /// A command left the client (the game's `io.send`). `planOf` reads the plan id the command carries; the oldest
    /// committed plan with that id, or else its draft, now follows the command.
    let sent (planOf: 'Command -> PlanId option) (envelope: CommandEnvelope<'Command>) (presentation: Presentation) =
        match planOf (CommandEnvelope.command envelope) with
        | None -> presentation
        | Some plan ->
            let candidate =
                presentation.Plans
                |> List.filter (fun entry -> entry.Id = plan && entry.Phase = Committed)
                |> List.sortBy (fun entry -> entry.Order)
                |> List.tryHead
                |> Option.orElse (
                    presentation.Plans |> List.tryFind (fun entry -> entry.Id = plan && entry.Phase = Draft)
                )

            match candidate with
            | None -> diagnose (sprintf "command for plan %s: no plan to link" (planName plan)) presentation
            | Some chosen ->
                { presentation with
                    Plans =
                        presentation.Plans
                        |> List.map (fun entry ->
                            if obj.ReferenceEquals(entry, chosen) then
                                { entry with
                                    Phase = Pending
                                    Command = Some(CommandEnvelope.id envelope)
                                }
                            else
                                entry)
                }
                |> announce (sprintf "Plan %s sent" (planName plan))

    let private finalise (id: CommandId) (phase: PlanPhase) (text: string) (presentation: Presentation) =
        let mutable changed = false

        let plans =
            presentation.Plans
            |> List.map (fun entry ->
                if entry.Command = Some id && (isOpen entry.Phase || entry.Phase = Applied) then
                    changed <- true
                    { entry with Phase = phase }
                else
                    entry)

        if changed then
            announce text { presentation with Plans = plans }
        else
            presentation

    /// A failure the pipeline routed to the game (`io.fail`). A refused, cancelled or unconfirmed command ends its plan
    /// in that phase; the first final outcome wins.
    let failed (failure: PipelineFailure) (presentation: Presentation) =
        match PipelineFailure.command failure with
        | None -> presentation
        | Some id ->
            let reason = PipelineFailure.reason failure |> Option.defaultValue ""

            if PipelineFailure.isCommandRejected failure then
                finalise id (Refused reason) ("Plan refused: " + reason) presentation
            elif PipelineFailure.isCommandCancelled failure then
                finalise id (Cancelled reason) ("Plan cancelled: " + reason) presentation
            elif PipelineFailure.isCommandUnconfirmed failure then
                finalise id Unconfirmed "Plan unconfirmed" presentation
            else
                presentation

    let private openPhase (settlement: CommandSettlement) =
        if CommandSettlement.isQueued settlement then Queued
        elif CommandSettlement.isRunning settlement then Running
        elif CommandSettlement.isAccepted settlement then Accepted
        else Pending

    /// Follow every linked plan's command, from the pipeline's view after a batch's effects were performed: open commands
    /// (`unsettled`), commands applied but not yet shown (`predicted`), and otherwise shown. A command that leaves both
    /// without a routed failure was applied and is shown: its plan is cleared, or kept as `Done`.
    let track (unsettled: (CommandId * CommandSettlement) list) (predicted: CommandId list) (presentation: Presentation) =
        presentation.Plans
        |> List.fold
            (fun (current: Presentation) entry ->
                match entry.Command with
                | Some id when isOpen entry.Phase || entry.Phase = Applied ->
                    let replace phase =
                        { current with
                            Plans =
                                current.Plans
                                |> List.map (fun other ->
                                    if obj.ReferenceEquals(other, entry) then { other with Phase = phase } else other)
                        }

                    match unsettled |> List.tryFind (fun (open', _) -> open' = id) with
                    | Some(_, settlement) ->
                        let phase = openPhase settlement
                        if phase = entry.Phase then current else replace phase
                    | None when predicted |> List.contains id ->
                        if entry.Phase = Applied then current else replace Applied
                    | None ->
                        match entry.Settle with
                        | ClearWhenShown ->
                            { current with
                                Plans = current.Plans |> List.filter (fun other -> not (obj.ReferenceEquals(other, entry)))
                            }
                            |> announce (sprintf "Plan %s done" (planName entry.Id))
                        | KeepWhenShown ->
                            replace Done |> announce (sprintf "Plan %s done" (planName entry.Id))
                | _ -> current)
            presentation

    /// Take the announcements out (the host writes them to its polite live region).
    let drain (presentation: Presentation) : Presentation * string list =
        { presentation with Announcements = [] }, presentation.Announcements

    /// The draft with id `plan`, if any.
    let draft (plan: PlanId) (presentation: Presentation) =
        presentation.Plans |> List.tryFind (fun entry -> entry.Id = plan && entry.Phase = Draft)

    // ---------------------------------------------------------------------------------------------------------------
    // The default codec (design section "Wire format")
    // ---------------------------------------------------------------------------------------------------------------

    /// The standard default codec for `GuestIntent<ClientEffect<'Custom>, 'Command> list` across the WebAssembly boundary.
    /// Little-endian; strings are a u16 byte length and UTF-8; coordinates are i32 fixed point with 8 fraction bits
    /// (Q24.8). The game supplies the command and custom codecs.
    module Codec =

        /// The format version.
        let version = 1uy

        /// The most ids, elements, waypoints or intents in one list.
        let maxItems = 1024

        /// The longest string, in UTF-8 bytes.
        let maxString = 1024

        /// The largest command or custom payload, in bytes.
        let maxPayload = 65536

        /// The largest coordinate magnitude, in scene units: (2^31 - 1) / 256, rounded down.
        let maxCoordinate = 8388607.0

        /// The largest fixed-point magnitude the format carries: `maxCoordinate x 256`.
        let maxFixed = 2147483392.0

        type private Writer() =
            let bytes = ResizeArray<byte>()
            member _.U8(value: int) = bytes.Add(byte (value &&& 0xFF))

            member this.U16(value: int) =
                this.U8 value
                this.U8(value >>> 8)

            member this.I32(value: int) =
                this.U8 value
                this.U8(value >>> 8)
                this.U8(value >>> 16)
                this.U8(value >>> 24)

            member _.Raw(data: byte array) = bytes.AddRange data
            member _.ToArray() = bytes.ToArray()

        exception private OutOfRange of string

        let private fail fmt = Printf.kprintf (fun text -> raise (OutOfRange text)) fmt

        let private writeString (w: Writer) (what: string) (text: string) =
            if obj.ReferenceEquals(text, null) then fail "%s is null" what
            let data = System.Text.Encoding.UTF8.GetBytes text

            if data.Length = 0 then fail "%s is empty" what
            if data.Length > maxString then fail "%s is %d bytes, over %d" what data.Length maxString
            w.U16 data.Length
            w.Raw data

        /// Text that may be empty (an icon name or a label).
        let private writeText (w: Writer) (what: string) (text: string) =
            if obj.ReferenceEquals(text, null) then fail "%s is null" what
            let data = System.Text.Encoding.UTF8.GetBytes text
            if data.Length > maxString then fail "%s is %d bytes, over %d" what data.Length maxString
            w.U16 data.Length
            w.Raw data

        let private writeCount (w: Writer) (what: string) (count: int) =
            if count > maxItems then fail "%s has %d items, over %d" what count maxItems
            w.U16 count

        /// A coordinate in Q24.8: refused unless finite and within `maxCoordinate`; `Floor(v x 256 + 0.5)`.
        let quantise (value: float) : int =
            if System.Double.IsNaN value || System.Double.IsInfinity value then fail "coordinate %g is not finite" value
            let fixedPoint = floor (value * 256.0 + 0.5)
            if abs fixedPoint > maxFixed then fail "coordinate %g is beyond %g" value maxCoordinate
            int fixedPoint

        let private writeCoordinate (w: Writer) (value: float) = w.I32(quantise value)

        let private writePoint (w: Writer) (point: Point) =
            writeCoordinate w point.X
            writeCoordinate w point.Y

        let private writeAnchor (w: Writer) (anchor: PlanAnchor) =
            match anchor with
            | AtPoint point ->
                w.U8 0
                writePoint w point
            | AtObject id ->
                w.U8 1
                writeString w "object id" id

        let private writeStyle (w: Writer) (style: PlanStyle) =
            w.U8(
                match style with
                | Move -> 0
                | Attack -> 1
                | Support -> 2
            )

        let private writeRole (w: Writer) (role: SelectionRole) =
            w.U8(
                match role with
                | Primary -> 0
                | Group -> 1
            )

        let private writeIds (w: Writer) (ids: string list) =
            writeCount w "id list" ids.Length
            ids |> List.iter (writeString w "object id")

        let private writeElement (w: Writer) (element: PlanElement) =
            match element with
            | Arrow(from, target, style) ->
                w.U8 1
                writeAnchor w from
                writeAnchor w target
                writeStyle w style
            | Path(points, style) ->
                if points.Length < 2 then fail "a path has %d waypoints, under 2" points.Length
                w.U8 2
                writeCount w "path" points.Length
                points |> List.iter (writeAnchor w)
                writeStyle w style
            | GhostAt ghost ->
                w.U8 3

                w.U8(
                    match ghost.Family with
                    | UnitGhost -> 0
                    | StructureGhost -> 1
                    | TerrainGhost -> 2
                    | ItemGhost -> 3
                    | MarkerGhost -> 4
                )

                w.U8(
                    match ghost.Identity with
                    | Own -> 0
                    | Ally -> 1
                    | Neutral -> 2
                    | Enemy -> 3
                    | Unknown -> 4
                )

                match ghost.Icon with
                | Some "" -> fail "a ghost icon is empty (use None)"
                | _ -> ()

                writeText w "icon" (ghost.Icon |> Option.defaultValue "")

                match ghost.Team with
                | None -> w.U8 0
                | Some team ->
                    if team < 0 || team > 0xFFFF then fail "team %d is outside 0..65535" team
                    w.U8 1
                    w.U16 team

                writePoint w ghost.Anchor

                if
                    ghost.Footprint.Width < 1
                    || ghost.Footprint.Height < 1
                    || ghost.Footprint.Width > 0xFFFF
                    || ghost.Footprint.Height > 0xFFFF
                then
                    fail "a ghost footprint must be 1 to 65535 cells each way"

                w.U16 ghost.Footprint.Width
                w.U16 ghost.Footprint.Height
                w.U8(if ghost.Valid then 1 else 0)
            | Area(shape, style) ->
                w.U8 4

                match shape with
                | Circle(centre, radius) ->
                    if not (radius > 0.0) then fail "a circle needs a positive radius"
                    let wireRadius = quantise radius
                    if wireRadius <= 0 then fail "a circle needs a positive radius after quantisation"
                    w.U8 0
                    writePoint w centre
                    w.I32 wireRadius
                | Box rect ->
                    if rect.Width < 0.0 || rect.Height < 0.0 then fail "a box needs a non-negative size"
                    w.U8 1
                    writeCoordinate w rect.X
                    writeCoordinate w rect.Y
                    writeCoordinate w rect.Width
                    writeCoordinate w rect.Height

                writeStyle w style
            | TargetLine(from, target) ->
                w.U8 5
                writeAnchor w from
                writeAnchor w target
            | Label(anchor, text) ->
                w.U8 6
                writeAnchor w anchor
                writeText w "label" text

        let private writePayload (w: Writer) (what: string) (data: byte array) =
            if obj.ReferenceEquals(data, null) then fail "%s is null" what
            if data.Length > maxPayload then fail "%s is %d bytes, over %d" what data.Length maxPayload
            w.I32 data.Length
            w.Raw data

        let private writeEffect (custom: 'Custom -> byte array) (w: Writer) (effect: ClientEffect<'Custom>) =
            match effect with
            | Select(role, ids) ->
                w.U8 0x01
                writeRole w role
                writeIds w ids
            | Add(role, ids) ->
                w.U8 0x02
                writeRole w role
                writeIds w ids
            | Remove(role, ids) ->
                w.U8 0x03
                writeRole w role
                writeIds w ids
            | ClearSelection -> w.U8 0x04
            | Highlight(id, role) ->
                w.U8 0x10
                writeString w "object id" id

                w.U8(
                    match role with
                    | Target -> 0
                    | Valid -> 1
                    | Invalid -> 2
                )
            | Unhighlight(id, role) ->
                w.U8 0x11
                writeString w "object id" id

                w.U8(
                    match role with
                    | Target -> 0
                    | Valid -> 1
                    | Invalid -> 2
                )
            | ClearHighlights -> w.U8 0x12
            | Plan(PlanId plan, elements) ->
                w.U8 0x20
                writeString w "plan id" plan
                writeCount w "plan" elements.Length
                elements |> List.iter (writeElement w)
            | Commit(PlanId plan, settle) ->
                w.U8 0x21
                writeString w "plan id" plan

                w.U8(
                    match settle with
                    | ClearWhenShown -> 0
                    | KeepWhenShown -> 1
                )
            | ClearPlan(PlanId plan) ->
                w.U8 0x22
                writeString w "plan id" plan
            | ClearPlans -> w.U8 0x23
            | Custom value ->
                w.U8 0x7F
                writePayload w "custom payload" (custom value)

        /// Encode a guest's intents: version, u16 count, then each intent (0 = Local and its effect, 1 = Submit and its
        /// u32-length command bytes). `Error` names the first value outside the accepted range.
        let encode
            (custom: 'Custom -> byte array)
            (command: 'Command -> byte array)
            (intents: GuestIntent<ClientEffect<'Custom>, 'Command> list)
            : Result<byte array, string> =
            try
                let w = Writer()
                w.U8(int version)
                writeCount w "intent list" intents.Length

                for intent in intents do
                    match intent with
                    | GuestIntent.Local effect ->
                        w.U8 0
                        writeEffect custom w effect
                    | GuestIntent.Submit value ->
                        w.U8 1
                        writePayload w "command" (command value)

                Ok(w.ToArray())
            with OutOfRange reason ->
                Error reason

        exception private Malformed of string

        type private Reader(data: byte array) =
            let mutable offset = 0
            member _.Offset = offset
            member _.Remaining = data.Length - offset

            member _.U8() =
                if offset >= data.Length then raise (Malformed(sprintf "truncated at byte %d" offset))
                let value = int data.[offset]
                offset <- offset + 1
                value

            member this.U16() =
                let low = this.U8()
                let high = this.U8()
                low ||| (high <<< 8)

            member this.I32() =
                let b0 = this.U8()
                let b1 = this.U8()
                let b2 = this.U8()
                let b3 = this.U8()
                b0 ||| (b1 <<< 8) ||| (b2 <<< 16) ||| (b3 <<< 24)

            member _.Bytes(count: int) =
                if count < 0 || count > data.Length - offset then
                    raise (Malformed(sprintf "truncated at byte %d" offset))

                let slice = Array.sub data offset count
                offset <- offset + count
                slice

        let private malformed fmt = Printf.kprintf (fun text -> raise (Malformed text)) fmt

        let private readText (r: Reader) (what: string) (allowEmpty: bool) =
            let start = r.Offset
            let length = r.U16()
            if length > maxString then malformed "%s at byte %d is %d bytes, over %d" what start length maxString
            if length = 0 && not allowEmpty then malformed "%s at byte %d is empty" what start
            let data = r.Bytes length
            let text = System.Text.Encoding.UTF8.GetString data

            // Invalid UTF-8 decodes to replacement characters on both runtimes; re-encoding exposes it.
            if System.Text.Encoding.UTF8.GetBytes text <> data then
                malformed "%s at byte %d is not valid UTF-8" what start

            text

        let private readCount (r: Reader) (what: string) =
            let start = r.Offset
            let count = r.U16()
            if count > maxItems then malformed "%s at byte %d has %d items, over %d" what start count maxItems
            count

        let private readCoordinate (r: Reader) =
            let start = r.Offset
            let value = r.I32()

            // Exactly the range the encoder writes, so every accepted encoding re-encodes to the same bytes.
            if abs (float value) > maxFixed then malformed "coordinate at byte %d is out of range" start

            float value / 256.0

        let private readPoint (r: Reader) : Point =
            let x = readCoordinate r
            let y = readCoordinate r
            { X = x; Y = y }

        let private readAnchor (r: Reader) =
            let start = r.Offset

            match r.U8() with
            | 0 -> AtPoint(readPoint r)
            | 1 -> AtObject(readText r "object id" false)
            | tag -> malformed "unknown anchor tag %d at byte %d" tag start

        let private readStyle (r: Reader) =
            let start = r.Offset

            match r.U8() with
            | 0 -> Move
            | 1 -> Attack
            | 2 -> Support
            | tag -> malformed "unknown style %d at byte %d" tag start

        let private readRole (r: Reader) =
            let start = r.Offset

            match r.U8() with
            | 0 -> Primary
            | 1 -> Group
            | tag -> malformed "unknown selection role %d at byte %d" tag start

        let private readHighlight (r: Reader) =
            let start = r.Offset

            match r.U8() with
            | 0 -> Target
            | 1 -> Valid
            | 2 -> Invalid
            | tag -> malformed "unknown highlight role %d at byte %d" tag start

        let private readIds (r: Reader) =
            let count = readCount r "id list"
            List.init count (fun _ -> readText r "object id" false)

        let private readFlag (r: Reader) (what: string) =
            let start = r.Offset

            match r.U8() with
            | 0 -> false
            | 1 -> true
            | value -> malformed "%s at byte %d is %d, not 0 or 1" what start value

        let private readElement (r: Reader) =
            let start = r.Offset

            match r.U8() with
            | 1 ->
                let from = readAnchor r
                let target = readAnchor r
                Arrow(from, target, readStyle r)
            | 2 ->
                let count = readCount r "path"
                if count < 2 then malformed "a path at byte %d has %d waypoints, under 2" start count
                let points = List.init count (fun _ -> readAnchor r)
                Path(points, readStyle r)
            | 3 ->
                let familyAt = r.Offset

                let family =
                    match r.U8() with
                    | 0 -> UnitGhost
                    | 1 -> StructureGhost
                    | 2 -> TerrainGhost
                    | 3 -> ItemGhost
                    | 4 -> MarkerGhost
                    | tag -> malformed "unknown ghost family %d at byte %d" tag familyAt

                let identityAt = r.Offset

                let identity =
                    match r.U8() with
                    | 0 -> Own
                    | 1 -> Ally
                    | 2 -> Neutral
                    | 3 -> Enemy
                    | 4 -> Unknown
                    | tag -> malformed "unknown ghost identity %d at byte %d" tag identityAt

                let icon =
                    match readText r "icon" true with
                    | "" -> None
                    | name -> Some name

                let team = if readFlag r "team flag" then Some(r.U16()) else None
                let anchor = readPoint r
                let width = r.U16()
                let height = r.U16()

                if width < 1 || height < 1 then
                    malformed "a ghost footprint at byte %d is not positive" start

                GhostAt
                    {
                        Family = family
                        Identity = identity
                        Icon = icon
                        Team = team
                        Anchor = anchor
                        Footprint = { Width = width; Height = height }
                        Valid = readFlag r "ghost validity"
                    }
            | 4 ->
                let shapeAt = r.Offset

                let shape =
                    match r.U8() with
                    | 0 ->
                        let centre = readPoint r
                        let radius = readCoordinate r
                        if radius <= 0.0 then malformed "a circle at byte %d has no positive radius" shapeAt
                        Circle(centre, radius)
                    | 1 ->
                        let x = readCoordinate r
                        let y = readCoordinate r
                        let width = readCoordinate r
                        let height = readCoordinate r

                        if width < 0.0 || height < 0.0 then
                            malformed "a box at byte %d has a negative size" shapeAt

                        Box
                            {
                                X = x
                                Y = y
                                Width = width
                                Height = height
                            }
                    | tag -> malformed "unknown area shape %d at byte %d" tag shapeAt

                Area(shape, readStyle r)
            | 5 ->
                let from = readAnchor r
                TargetLine(from, readAnchor r)
            | 6 ->
                let anchor = readAnchor r
                Label(anchor, readText r "label" true)
            | tag -> malformed "unknown plan element tag %d at byte %d" tag start

        let private readPayload (r: Reader) (what: string) =
            let start = r.Offset
            let length = r.I32()
            if length < 0 || length > maxPayload then malformed "%s at byte %d is %d bytes, outside 0..%d" what start length maxPayload
            r.Bytes length

        let private readEffect (custom: byte array -> Result<'Custom, string>) (r: Reader) : ClientEffect<'Custom> =
            let start = r.Offset

            match r.U8() with
            | 0x01 ->
                let role = readRole r
                Select(role, readIds r)
            | 0x02 ->
                let role = readRole r
                Add(role, readIds r)
            | 0x03 ->
                let role = readRole r
                Remove(role, readIds r)
            | 0x04 -> ClearSelection
            | 0x10 ->
                let id = readText r "object id" false
                Highlight(id, readHighlight r)
            | 0x11 ->
                let id = readText r "object id" false
                Unhighlight(id, readHighlight r)
            | 0x12 -> ClearHighlights
            | 0x20 ->
                let plan = readText r "plan id" false
                let count = readCount r "plan"
                Plan(PlanId plan, List.init count (fun _ -> readElement r))
            | 0x21 ->
                let plan = readText r "plan id" false
                let settleAt = r.Offset

                let settle =
                    match r.U8() with
                    | 0 -> ClearWhenShown
                    | 1 -> KeepWhenShown
                    | tag -> malformed "unknown settle policy %d at byte %d" tag settleAt

                Commit(PlanId plan, settle)
            | 0x22 -> ClearPlan(PlanId(readText r "plan id" false))
            | 0x23 -> ClearPlans
            | 0x7F ->
                match custom (readPayload r "custom payload") with
                | Ok value -> Custom value
                | Error reason -> malformed "custom payload at byte %d: %s" start reason
            | tag -> malformed "unknown effect tag 0x%02x at byte %d" tag start

        /// Decode a guest's output. Every byte must be consumed: a short, long or unknown encoding is an `Error` (which
        /// the pipeline treats as `Faulted`, halting the guest stage). Never raises.
        let decode
            (custom: byte array -> Result<'Custom, string>)
            (command: byte array -> Result<'Command, string>)
            (data: byte array)
            : Result<GuestIntent<ClientEffect<'Custom>, 'Command> list, string> =
            if obj.ReferenceEquals(data, null) then
                Error "no output"
            else
                try
                    let r = Reader data
                    let found = r.U8()

                    if found <> int version then malformed "format version %d, expected %d" found (int version)

                    let count = readCount r "intent list"

                    let intents =
                        List.init count (fun _ ->
                            let start = r.Offset

                            match r.U8() with
                            | 0 -> GuestIntent.Local(readEffect custom r)
                            | 1 ->
                                match command (readPayload r "command") with
                                | Ok value -> GuestIntent.Submit value
                                | Error reason -> malformed "command at byte %d: %s" start reason
                            | kind -> malformed "unknown intent kind %d at byte %d" kind start)

                    if r.Remaining <> 0 then malformed "%d trailing bytes after byte %d" r.Remaining r.Offset

                    Ok intents
                with
                | Malformed reason -> Error reason
                | error -> Error("decoder fault: " + error.Message)

        /// Lower-case hex of `data`, for golden vectors.
        let hex (data: byte array) =
            data |> Array.map (fun b -> sprintf "%02x" b) |> String.concat ""

    // ---------------------------------------------------------------------------------------------------------------
    // The plan pass in the portable profile (design section "Drawing"): opaque rectangles only
    // ---------------------------------------------------------------------------------------------------------------

    /// The plan inks. In the library these are the theme aliases `plan.move`, `plan.attack`, `plan.support`,
    /// `plan.invalid` and `plan.keyline` (#795 derives them); the proof's values are fixed and not themed.
    type PlanInks =
        {
            Move: Color
            Attack: Color
            Support: Color
            Invalid: Color
            Keyline: Color
            /// The dot size and sampling step, in scene units.
            Dot: float
            /// The gap between an object's bounds and a plan that ends at it.
            Gap: float
        }

    let private rgb red green blue =
        {
            Red = red
            Green = green
            Blue = blue
            Alpha = 255uy
        }

    /// The proof's inks, chosen against ReferenceGame's grounds: each ink is at least 7:1 on the keyline.
    let proofInks: PlanInks =
        {
            Move = rgb 120uy 230uy 140uy
            Attack = rgb 255uy 170uy 60uy
            Support = rgb 120uy 200uy 255uy
            Invalid = rgb 255uy 110uy 110uy
            Keyline = rgb 12uy 12uy 20uy
            Dot = 4.0
            Gap = 2.0
        }

    /// Whether sample `index` of a shaft is drawn in `phase`: the phase is the pattern, never the hue alone.
    let drawn (phase: PlanPhase) (index: int) =
        match phase with
        | Draft
        | Committed -> index % 2 = 0
        | Pending
        | Accepted
        | Queued
        | Running -> index % 3 <> 2
        | Applied
        | Done -> true
        | Refused _
        | Cancelled _
        | Unconfirmed -> index % 2 = 0

    let private centre (rect: Rect) =
        {
            X = rect.X + rect.Width / 2.0
            Y = rect.Y + rect.Height / 2.0
        }

    /// Where a straight segment from `inside`'s centre towards `toward` leaves `inside`'s bounds, plus `gap`.
    let private exitPoint (gap: float) (inside: Rect) (toward: Point) =
        let c = centre inside
        let dx = toward.X - c.X
        let dy = toward.Y - c.Y
        let length = sqrt (dx * dx + dy * dy)

        if length < 1e-9 then
            c
        else
            let tx = if abs dx < 1e-9 then infinity else inside.Width / 2.0 / abs dx
            let ty = if abs dy < 1e-9 then infinity else inside.Height / 2.0 / abs dy
            let t = min tx ty
            let edge = t * length + gap

            {
                X = c.X + dx / length * edge
                Y = c.Y + dy / length * edge
            }

    let private resolve (bounds: string -> Rect option) (anchor: PlanAnchor) (other: Point option) =
        match anchor with
        | AtPoint point -> Some point
        | AtObject id ->
            bounds id
            |> Option.map (fun rect ->
                match other with
                | Some toward -> exitPoint proofInks.Gap rect toward
                | None -> centre rect)

    let private anchorCentre (bounds: string -> Rect option) (anchor: PlanAnchor) =
        match anchor with
        | AtPoint point -> Some point
        | AtObject id -> bounds id |> Option.map centre

    /// The sample points along a polyline, one every `step` units from its start.
    let samples (step: float) (points: Point list) =
        [
            match points with
            | [] -> ()
            | first :: _ ->
                yield first

                let mutable carried = 0.0

                for a, b in List.pairwise points do
                    let dx = b.X - a.X
                    let dy = b.Y - a.Y
                    let length = sqrt (dx * dx + dy * dy)
                    let mutable along = step - carried

                    while along <= length + 1e-9 do
                        yield
                            {
                                X = a.X + dx / length * along
                                Y = a.Y + dy / length * along
                            }

                        along <- along + step

                    carried <- length - (along - step)
        ]

    let private dot (inks: PlanInks) (ink: Color) (p: Point) =
        let d = inks.Dot
        let k = d + 2.0

        [
            SceneNode.Rectangle((floor (p.X - k / 2.0 + 0.5), floor (p.Y - k / 2.0 + 0.5), k, k), inks.Keyline)
            SceneNode.Rectangle((floor (p.X - d / 2.0 + 0.5), floor (p.Y - d / 2.0 + 0.5), d, d), ink)
        ]

    let private styleInk (inks: PlanInks) (style: PlanStyle) =
        match style with
        | Move -> inks.Move
        | Attack -> inks.Attack
        | Support -> inks.Support

    /// The head at `tip`, pointing along (ux, uy): a chevron for Move, a reticle for Attack, a plus for Support, and a
    /// cross for a failed plan. Keylines first, then inks, so heads never lose their keyline to a neighbouring dot.
    let private head (inks: PlanInks) (ink: Color) (phase: PlanPhase) (style: PlanStyle) (tip: Point) (ux: float, uy: float) =
        let s = inks.Dot * 1.5
        let at dx dy = { X = tip.X + dx; Y = tip.Y + dy }

        let points =
            if isFailed phase then
                [ tip; at s s; at -s s; at s -s; at -s -s ]
            else
                match style with
                | Move ->
                    // The tip, and two barbs back along the shaft and out to each side.
                    [ tip; at (-ux * s - uy * s) (-uy * s + ux * s); at (-ux * s + uy * s) (-uy * s - ux * s) ]
                | Attack -> [ at s 0.0; at -s 0.0; at 0.0 s; at 0.0 -s ]
                | Support -> [ tip; at s 0.0; at -s 0.0; at 0.0 s; at 0.0 -s ]

        let all = points |> List.map (dot inks ink)
        (all |> List.map List.head) @ (all |> List.map (List.item 1))

    let private shaft (inks: PlanInks) (ink: Color) (phase: PlanPhase) (style: PlanStyle) (points: Point list) =
        let marks = samples inks.Dot points |> List.indexed |> List.filter (fun (i, _) -> drawn phase i) |> List.map snd

        let tail =
            match List.rev points with
            | tip :: before :: _ ->
                let dx = tip.X - before.X
                let dy = tip.Y - before.Y
                let length = max 1e-9 (sqrt (dx * dx + dy * dy))
                head inks ink phase style tip (dx / length, dy / length)
            | _ -> []

        let dots = marks |> List.map (dot inks ink)
        (dots |> List.collect (List.take 1)) @ (dots |> List.collect (List.skip 1)) @ tail

    /// The proof's cell size, in scene units (ReferenceGame's columns).
    let cell = 32.0

    /// The plan pass of one plan. `bounds` gives an object's frame (authoritative, so a predicted move does not drag its
    /// own arrow's start). An element whose object is missing draws nothing.
    let drawPlan (inks: PlanInks) (bounds: string -> Rect option) (entry: PlanEntry) : SceneNode list =
        let failed = isFailed entry.Phase
        let inkOf style = if failed then inks.Invalid else styleInk inks style

        entry.Elements
        |> List.collect (fun element ->
            match element with
            | Arrow(from, target, style) ->
                match anchorCentre bounds from, anchorCentre bounds target with
                | Some fromCentre, Some targetCentre ->
                    match resolve bounds from (Some targetCentre), resolve bounds target (Some fromCentre) with
                    | Some a, Some b -> shaft inks (inkOf style) entry.Phase style [ a; b ]
                    | _ -> []
                | _ -> []
            | TargetLine(from, target) ->
                match anchorCentre bounds from, anchorCentre bounds target with
                | Some fromCentre, Some targetCentre ->
                    match resolve bounds from (Some targetCentre), resolve bounds target (Some fromCentre) with
                    | Some a, Some b -> shaft inks (inkOf Attack) entry.Phase Attack [ a; b ]
                    | _ -> []
                | _ -> []
            | Path(waypoints, style) ->
                let points = waypoints |> List.choose (anchorCentre bounds)

                if points.Length = waypoints.Length then
                    shaft inks (inkOf style) entry.Phase style points
                else
                    []
            | Area(shape, style) ->
                let outline =
                    match shape with
                    | Box rect ->
                        let corner x y = { X = x; Y = y }

                        [
                            corner rect.X rect.Y
                            corner (rect.X + rect.Width) rect.Y
                            corner (rect.X + rect.Width) (rect.Y + rect.Height)
                            corner rect.X (rect.Y + rect.Height)
                            corner rect.X rect.Y
                        ]
                    | Circle(c, r) ->
                        [
                            for i in 0..24 ->
                                let a = float i / 24.0 * 2.0 * System.Math.PI
                                { X = c.X + r * cos a; Y = c.Y + r * sin a }
                        ]

                let marks = samples inks.Dot outline |> List.indexed |> List.filter (fun (i, _) -> drawn entry.Phase i)
                let dots = marks |> List.map (snd >> dot inks (inkOf style))
                (dots |> List.collect (List.take 1)) @ (dots |> List.collect (List.skip 1))
            | GhostAt ghost ->
                let w = float ghost.Footprint.Width * cell
                let h = float ghost.Footprint.Height * cell
                let x = ghost.Anchor.X - w / 2.0
                let y = ghost.Anchor.Y - h / 2.0
                let ink = if ghost.Valid && not failed then inks.Move else inks.Invalid

                let outline =
                    [
                        { X = x; Y = y }
                        { X = x + w; Y = y }
                        { X = x + w; Y = y + h }
                        { X = x; Y = y + h }
                        { X = x; Y = y }
                    ]

                let dots = samples inks.Dot outline |> List.map (dot inks ink)

                let cross =
                    if ghost.Valid then
                        []
                    else
                        head inks inks.Invalid (Refused "") Move ghost.Anchor (1.0, 0.0)

                (dots |> List.collect (List.take 1)) @ (dots |> List.collect (List.skip 1)) @ cross
            | Label(anchor, _) ->
                // The portable profile draws a label's marker only; its text is in the mirror summary.
                match anchorCentre bounds anchor with
                | Some p -> dot inks inks.Move p
                | None -> [])

    /// The plan pass: every plan, in creation order.
    let planPass (inks: PlanInks) (bounds: string -> Rect option) (presentation: Presentation) : SceneNode list =
        presentation.Plans |> List.sortBy (fun entry -> entry.Order) |> List.collect (drawPlan inks bounds)

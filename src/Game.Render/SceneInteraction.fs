namespace FS.GG.Game.Render

open FS.GG.UI.Scene

[<RequireQualifiedAccess>]
type SelectableShape =
    | Box
    | Ellipse

type SelectableObject =
    {
        Id: string
        Label: string
        Bounds: Rect
        Shape: SelectableShape
    }

[<RequireQualifiedAccess>]
type SelectionRequest =
    | Select of objectId: string
    | Clear

[<NoComparison>]
type InteractiveScene =
    {
        Scene: Scene
        Objects: SelectableObject list
        Selected: string option
    }

[<RequireQualifiedAccess>]
type InteractionKey =
    | FocusNext
    | FocusPrevious
    | Activate
    | Clear

[<Sealed>]
type ObjectAccess internal (keyboard: bool, group: string option) =
    member internal _.Keyboard = keyboard
    member internal _.Group = group

[<RequireQualifiedAccess>]
module ObjectAccess =

    let full = ObjectAccess(true, None)

    let pointerOnly = ObjectAccess(false, None)

    let grouped (label: string) =
        if System.String.IsNullOrWhiteSpace label then
            invalidArg (nameof label) "A group label must not be blank."

        ObjectAccess(true, Some label)

    let isKeyboardReachable (access: ObjectAccess) = access.Keyboard

    let group (access: ObjectAccess) = access.Group

[<Sealed>]
type InteractionCues internal (focused: string option, hovered: string option, focusVisible: bool) =
    member internal _.Focused = focused
    member internal _.Hovered = hovered
    member internal _.FocusVisible = focusVisible

[<RequireQualifiedAccess>]
module InteractionCues =

    let none = InteractionCues(None, None, false)

    let create focused hovered focusVisible =
        InteractionCues(focused, hovered, focusVisible)

    let focused (cues: InteractionCues) = cues.Focused

    let hovered (cues: InteractionCues) = cues.Hovered

    let focusVisible (cues: InteractionCues) = cues.FocusVisible

/// Opaque behind the .fsi: built by `SceneInteraction.create` only, so a field added later is not a breaking change.
[<NoEquality; NoComparison>]
type SceneInteraction<'State> =
    {
        Objects: 'State -> SelectableObject list
        Selected: 'State -> string option
        Select: SelectionRequest -> 'State -> 'State
        /// FS.GG.Game#784: `None` is `ObjectAccess.full` for every object.
        Access: (SelectableObject -> ObjectAccess) option
        /// FS.GG.Game#787: `None` is `SceneInteraction.standardCues`.
        Cues: (InteractionCues -> SelectableObject list -> Scene) option
    }

[<Sealed>]
type SelectionRoute internal () =
    let mutable handler: (SelectionRequest -> unit) option = None

    member internal _.Bind(next: SelectionRequest -> unit) = handler <- Some next
    member internal _.Unbind() = handler <- None

    member internal _.Report(request: SelectionRequest) =
        match handler with
        | Some deliver -> deliver request
        | None -> ()

[<RequireQualifiedAccess>]
module SelectionRoute =

    let create () = SelectionRoute()

    let bind (handler: SelectionRequest -> unit) (route: SelectionRoute) = route.Bind handler

    let unbind (route: SelectionRoute) = route.Unbind()

    let report (request: SelectionRequest) (route: SelectionRoute) = route.Report request

[<RequireQualifiedAccess>]
module SceneInteraction =

    let create objects selected select : SceneInteraction<'State> =
        {
            Objects = objects
            Selected = selected
            Select = select
            Access = None
            Cues = None
        }

    let objects (interaction: SceneInteraction<'State>) (state: 'State) = interaction.Objects state

    let selected (interaction: SceneInteraction<'State>) (state: 'State) = interaction.Selected state

    let apply (interaction: SceneInteraction<'State>) (request: SelectionRequest) (state: 'State) =
        interaction.Select request state

    let frame (view: 'State -> Scene) (interaction: SceneInteraction<'State>) (state: 'State) : InteractiveScene =
        {
            Scene = view state
            Objects = interaction.Objects state
            Selected = interaction.Selected state
        }

    let private finite (value: float) =
        not (System.Double.IsNaN value || System.Double.IsInfinity value)

    let problems (objects: SelectableObject list) : string list =
        [
            for index, target in List.indexed objects do
                if System.String.IsNullOrWhiteSpace target.Id then
                    yield sprintf "object %d has a blank id" index

                if System.String.IsNullOrWhiteSpace target.Label then
                    yield sprintf "object %d (%s) has a blank label" index target.Id

                let bounds = target.Bounds

                if
                    not (
                        finite bounds.X
                        && finite bounds.Y
                        && finite bounds.Width
                        && finite bounds.Height
                    )
                then
                    yield sprintf "object %d (%s) has bounds that are not finite" index target.Id
                elif bounds.Width < 0.0 || bounds.Height < 0.0 then
                    yield sprintf "object %d (%s) has a negative size" index target.Id

            for id, count in objects |> List.countBy (fun target -> target.Id) do
                if count > 1 && not (System.String.IsNullOrWhiteSpace id) then
                    yield sprintf "the id %s is used by %d objects" id count
        ]

    let contains (point: Point) (target: SelectableObject) : bool =
        let bounds = target.Bounds

        match target.Shape with
        | SelectableShape.Box ->
            point.X >= bounds.X
            && point.X <= bounds.X + bounds.Width
            && point.Y >= bounds.Y
            && point.Y <= bounds.Y + bounds.Height
        | SelectableShape.Ellipse ->
            let radiusX = bounds.Width / 2.0
            let radiusY = bounds.Height / 2.0

            if radiusX <= 0.0 || radiusY <= 0.0 then
                false
            else
                let dx = (point.X - (bounds.X + radiusX)) / radiusX
                let dy = (point.Y - (bounds.Y + radiusY)) / radiusY
                dx * dx + dy * dy <= 1.0

    let hitTest (point: Point) (objects: SelectableObject list) : string option =
        objects
        |> List.rev
        |> List.tryFind (contains point)
        |> Option.map (fun target -> target.Id)

    let press (point: Point) (frame: InteractiveScene) : SelectionRequest option =
        match hitTest point frame.Objects with
        | Some id when frame.Selected <> Some id -> Some(SelectionRequest.Select id)
        | _ -> None

    let key (key: InteractionKey) (focused: string option) (frame: InteractiveScene) =
        let ids = frame.Objects |> List.map (fun target -> target.Id)

        let focused = focused |> Option.filter (fun id -> List.contains id ids)

        let index = focused |> Option.bind (fun id -> List.tryFindIndex ((=) id) ids)

        let count = ids.Length

        match key with
        | InteractionKey.FocusNext when count > 0 ->
            let next =
                match index with
                | Some current -> (current + 1) % count
                | None -> 0

            Some ids.[next], None
        | InteractionKey.FocusPrevious when count > 0 ->
            let previous =
                match index with
                | Some current -> (current + count - 1) % count
                | None -> count - 1

            Some ids.[previous], None
        | InteractionKey.FocusNext
        | InteractionKey.FocusPrevious -> None, None
        | InteractionKey.Activate ->
            match focused with
            | Some id when frame.Selected <> Some id -> focused, Some(SelectionRequest.Select id)
            | _ -> focused, None
        | InteractionKey.Clear ->
            match frame.Selected with
            | Some _ -> focused, Some SelectionRequest.Clear
            | None -> focused, None

    let withAccess (access: SelectableObject -> ObjectAccess) (interaction: SceneInteraction<'State>) =
        { interaction with
            Access = Some access
        }

    let access (interaction: SceneInteraction<'State>) (target: SelectableObject) =
        match interaction.Access with
        | Some declared -> declared target
        | None -> ObjectAccess.full

    /// Contract only (FS.GG.Game#787); FS.GG.Game#788 implements the drawing.
    let standardCues (_: InteractionCues) (_: SelectableObject list) : Scene =
        failwith "SceneInteraction.standardCues is a contract stub; FS.GG.Game#788 implements it."

    let withCues (draw: InteractionCues -> SelectableObject list -> Scene) (interaction: SceneInteraction<'State>) =
        { interaction with Cues = Some draw }

    let cues (interaction: SceneInteraction<'State>) =
        match interaction.Cues with
        | Some draw -> draw
        | None -> standardCues

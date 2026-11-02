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
type ObjectAccess internal (keyboard: bool, group: string option, cell: (int * int) option) =
    member internal _.Keyboard = keyboard
    member internal _.Group = group
    member internal _.Cell = cell

[<RequireQualifiedAccess>]
module ObjectAccess =

    let full = ObjectAccess(true, None, None)

    let pointerOnly = ObjectAccess(false, None, None)

    let private checkLabel (value: string) =
        if System.String.IsNullOrWhiteSpace value then
            invalidArg "label" "A group label must not be blank."

        value

    let grouped (label: string) =
        ObjectAccess(true, Some(checkLabel label), None)

    let cell (label: string) (row: int) (column: int) =
        if row < 0 || column < 0 then
            invalidArg "row" "A cell's row and column are zero or more."

        ObjectAccess(true, Some(checkLabel label), Some(row, column))

    let isKeyboardReachable (access: ObjectAccess) = access.Keyboard

    let group (access: ObjectAccess) = access.Group

    let cellOf (access: ObjectAccess) = access.Cell

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

[<Sealed>]
type CueContext internal (cues: InteractionCues, objects: SelectableObject list, cssPerUnit: float) =
    member internal _.Cues = cues
    member internal _.Objects = objects
    member internal _.CssPerUnit = cssPerUnit

[<RequireQualifiedAccess>]
module CueContext =

    let create (cues: InteractionCues) (objects: SelectableObject list) (cssPerUnit: float) =
        if
            System.Double.IsNaN cssPerUnit
            || System.Double.IsInfinity cssPerUnit
            || cssPerUnit <= 0.0
        then
            invalidArg (nameof cssPerUnit) "The scale must be finite and greater than zero."

        CueContext(cues, objects, cssPerUnit)

    let cues (context: CueContext) = context.Cues

    let objects (context: CueContext) = context.Objects

    let cssPerUnit (context: CueContext) = context.CssPerUnit

/// Opaque behind the .fsi: built by `SceneInteraction.create` only, so a field added later is not a breaking change.
/// See SceneInteraction.fsi: what an input host needs from the interaction besides each frame (FS.GG.Game#788).
[<Sealed>]
type InputHostSpec internal (access: SelectableObject -> ObjectAccess, cues: CueContext -> SceneNode list) =
    member internal _.Access = access
    member internal _.Cues = cues

[<NoEquality; NoComparison>]
type SceneInteraction<'State> =
    {
        Objects: 'State -> SelectableObject list
        Selected: 'State -> string option
        Select: SelectionRequest -> 'State -> 'State
        /// FS.GG.Game#784: `None` is `ObjectAccess.full` for every object.
        Access: (SelectableObject -> ObjectAccess) option
        /// FS.GG.Game#787: `None` is `SceneInteraction.standardCues`.
        Cues: (CueContext -> SceneNode list) option
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

    let private ringOuter: Color =
        {
            Red = 16uy
            Green = 16uy
            Blue = 16uy
            Alpha = 255uy
        }

    let private ringInner: Color =
        {
            Red = 255uy
            Green = 255uy
            Blue = 255uy
            Alpha = 255uy
        }

    /// Four filled rectangles framing (x, y, w, h) from outside, `band` wide.
    let private frameOf (x: float) (y: float) (w: float) (h: float) (band: float) (fill: Color) =
        [
            SceneNode.Rectangle((x - band, y - band, w + 2.0 * band, band), fill)
            SceneNode.Rectangle((x - band, y + h, w + 2.0 * band, band), fill)
            SceneNode.Rectangle((x - band, y, band, h), fill)
            SceneNode.Rectangle((x + w, y, band, h), fill)
        ]

    /// The standard ring (ADR 0001): the light inner band hugs the bounds, the dark outer band surrounds it; each is
    /// 2 CSS px at the context's scale.
    let standardCues (context: CueContext) : SceneNode list =
        match InteractionCues.focused context.Cues with
        | Some id when InteractionCues.focusVisible context.Cues ->
            match context.Objects |> List.tryFind (fun target -> target.Id = id) with
            | Some target ->
                let band = 2.0 / context.CssPerUnit
                let b = target.Bounds

                frameOf (b.X - band) (b.Y - band) (b.Width + 2.0 * band) (b.Height + 2.0 * band) band ringOuter
                @ frameOf b.X b.Y b.Width b.Height band ringInner
            | None -> []
        | _ -> []

    let withCues (draw: CueContext -> SceneNode list) (interaction: SceneInteraction<'State>) =
        { interaction with Cues = Some draw }

    let cues (interaction: SceneInteraction<'State>) =
        match interaction.Cues with
        | Some draw -> draw
        | None -> standardCues

    let inputHost (interaction: SceneInteraction<'State>) =
        InputHostSpec(access interaction, cues interaction)

[<RequireQualifiedAccess>]
module InputHostSpec =

    let standard = InputHostSpec((fun _ -> ObjectAccess.full), SceneInteraction.standardCues)

    let create (access: SelectableObject -> ObjectAccess) (cues: CueContext -> SceneNode list) =
        InputHostSpec(access, cues)

    let access (spec: InputHostSpec) (target: SelectableObject) = spec.Access target

    let cues (spec: InputHostSpec) = spec.Cues

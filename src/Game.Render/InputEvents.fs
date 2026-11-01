namespace FS.GG.Game.Render

open FS.GG.UI.Scene

[<RequireQualifiedAccess>]
type InputSource =
    | Pointer
    | Keyboard
    | Mirror

type InputModifiers =
    {
        Shift: bool
        Control: bool
        Alt: bool
        Meta: bool
    }

[<RequireQualifiedAccess>]
module InputModifiers =

    let none =
        {
            Shift = false
            Control = false
            Alt = false
            Meta = false
        }

[<RequireQualifiedAccess>]
type InputAction =
    | Activate
    | ActivateSecondary
    | Hover
    | Focus
    | Cancel

/// Private representation behind the .fsi: structural equality, and a field added later is not a breaking change.
type InputEvent =
    private
        {
            Source: InputSource
            Action: InputAction
            Target: string option
            Point: Point option
            Modifiers: InputModifiers
            Repeat: bool
        }

[<RequireQualifiedAccess>]
module InputEvent =

    let create source action target point modifiers repeat =
        {
            Source = source
            Action = action
            Target = target
            Point = point
            Modifiers = modifiers
            Repeat = repeat
        }

    let source (event: InputEvent) = event.Source

    let action (event: InputEvent) = event.Action

    let target (event: InputEvent) = event.Target

    let point (event: InputEvent) = event.Point

    let modifiers (event: InputEvent) = event.Modifiers

    let isRepeat (event: InputEvent) = event.Repeat

    let toSelectionRequest (frame: InteractiveScene) (event: InputEvent) : SelectionRequest option =
        if event.Repeat then
            None
        else
            match event.Action, event.Target with
            | InputAction.Activate, Some id when
                frame.Selected <> Some id
                && frame.Objects |> List.exists (fun target -> target.Id = id)
                ->
                Some(SelectionRequest.Select id)
            | InputAction.Cancel, _ when frame.Selected.IsSome -> Some SelectionRequest.Clear
            | _ -> None

[<Sealed>]
type InputRoute internal () =
    let mutable handler: (InputEvent -> unit) option = None

    member internal _.Bind(next: InputEvent -> unit) = handler <- Some next
    member internal _.Unbind() = handler <- None

    member internal _.Report(event: InputEvent) =
        match handler with
        | Some deliver -> deliver event
        | None -> ()

[<RequireQualifiedAccess>]
module InputRoute =

    let create () = InputRoute()

    let bind (handler: InputEvent -> unit) (route: InputRoute) = route.Bind handler

    let unbind (route: InputRoute) = route.Unbind()

    let report (event: InputEvent) (route: InputRoute) = route.Report event

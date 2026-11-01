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

/// Opaque behind the .fsi, so a field added later is not a breaking change.
[<Sealed>]
type InputEvent
    internal
    (
        source: InputSource,
        action: InputAction,
        target: string option,
        point: Point option,
        modifiers: InputModifiers,
        repeat: bool
    ) =
    member internal _.Source = source
    member internal _.Action = action
    member internal _.Target = target
    member internal _.Point = point
    member internal _.Modifiers = modifiers
    member internal _.Repeat = repeat

[<RequireQualifiedAccess>]
module InputEvent =

    let create source action target point modifiers repeat =
        InputEvent(source, action, target, point, modifiers, repeat)

    let source (event: InputEvent) = event.Source

    let action (event: InputEvent) = event.Action

    let target (event: InputEvent) = event.Target

    let point (event: InputEvent) = event.Point

    let modifiers (event: InputEvent) = event.Modifiers

    let isRepeat (event: InputEvent) = event.Repeat

    /// Contract only (FS.GG.Game#787); FS.GG.Game#788 implements it.
    let toSelectionRequest (_: InteractiveScene) (_: InputEvent) : SelectionRequest option =
        failwith "InputEvent.toSelectionRequest is a contract stub; FS.GG.Game#788 implements it."

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

namespace FS.GG.Game.Render

/// Public contract type exposed by the FS.GG.Game.Render package.
/// Where an input came from (stage 1 of the standard input pipeline, `docs/adr/0001-standard-input-pipeline.md`). Pen
/// and touch arrive as `Pointer`. The set is closed: a new device is a major change.
[<RequireQualifiedAccess>]
type InputSource =
    /// A pointer press or move on the backend's output.
    | Pointer
    /// A key pressed while the backend's output (or its mirror) has the keyboard focus.
    | Keyboard
    /// A control of the accessible mirror (a button activated by a screen reader, a switch or a click).
    | Mirror

/// Public contract type exposed by the FS.GG.Game.Render package.
/// The modifier keys held during an input. The set is the DOM's and is closed.
type InputModifiers =
    {
        /// Shift.
        Shift: bool
        /// Control.
        Control: bool
        /// Alt (Option on macOS).
        Alt: bool
        /// Meta (Command on macOS, the Windows key elsewhere).
        Meta: bool
    }

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module InputModifiers =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// No modifier held.
    val none: InputModifiers

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// What the player did. Unlike `SelectionRequest`, an action is reported whether or not it changes the selection: a
/// second activation of the selected object is an `Activate` too (FS.GG.Game#771).
///
/// **Extension policy:** this union is CLOSED (FS.GG.Game#788 union policy). A new case, such as continuous input (drag,
/// wheel, press and release), is a major version change, so a game may match it exhaustively.
[<RequireQualifiedAccess>]
type InputAction =
    /// The primary activation: a primary pointer press, Enter or Space on the focused object, or a mirror button. A
    /// pointer press where no object is still activates, with no target and the press point.
    | Activate
    /// The secondary activation: a secondary pointer press (the browser's context menu is suppressed on the surface),
    /// a touch long-press (held 500 ms, moving less than 10 CSS pixels; source `Pointer`), or the ContextMenu key or
    /// Shift+F10 on the focused object.
    | ActivateSecondary
    /// The pointer now hovers the target (no target: no object). Reported only when the hovered object changes.
    | Hover
    /// The keyboard focus moved to the target (an arrow key, Tab into a mirror entry). No target: the surface itself.
    | Focus
    /// Escape, on the surface or on any mirror entry.
    | Cancel

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// One input, as every backend's input host reports it: the same pointer, keyboard or mirror input yields the same
/// event on SVG, Pixi and Babylon. Equal when every field is. Built by `InputEvent.create`.
type InputEvent =
    private
        {
            Source: InputSource
            Action: InputAction
            Target: string option
            Point: FS.GG.UI.Scene.Point option
            Modifiers: InputModifiers
            Repeat: bool
        }

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module InputEvent =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// An event. `target` is the object id the input is on (the topmost hit for a pointer, the focused object for a
    /// key, the entry's object for the mirror); `point` is the scene point of a pointer input; `repeat` is true for a
    /// key the operating system repeats while it is held.
    val create:
        source: InputSource ->
        action: InputAction ->
        target: string option ->
        point: FS.GG.UI.Scene.Point option ->
        modifiers: InputModifiers ->
        repeat: bool ->
            InputEvent

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Where the input came from.
    val source: event: InputEvent -> InputSource

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// What the player did.
    val action: event: InputEvent -> InputAction

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The object the input is on, if any.
    val target: event: InputEvent -> string option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The scene point of a pointer input.
    val point: event: InputEvent -> FS.GG.UI.Scene.Point option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The modifier keys held.
    val modifiers: event: InputEvent -> InputModifiers

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether the operating system repeated the key.
    val isRepeat: event: InputEvent -> bool

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The `SelectionRequest` a game without a pipeline hears for `event` on `frame`, so the existing `SelectionRoute`
    /// keeps its meaning: an unrepeated `Activate` of an object that is not selected asks to select it, an unrepeated
    /// `Cancel` with a selection asks to clear it, and anything else asks for nothing. A press on empty ground (an
    /// `Activate` with no target) asks for nothing, as a press where no object is always has on every backend
    /// (`SceneInteraction.press`); only `Cancel` clears.
    val toSelectionRequest: frame: InteractiveScene -> event: InputEvent -> SelectionRequest option

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// Where the input hosts of a game's backends send every `InputEvent`, the input counterpart of `SelectionRoute`:
/// created first, given to the renderers, and bound to the game (or its `InputPipeline`) when it runs. Events reported
/// while nothing is bound are dropped. Built by `InputRoute.create`.
[<Sealed>]
type InputRoute

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module InputRoute =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A route with nothing bound.
    val create: unit -> InputRoute

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Send the route's events to `handler` from now on, replacing whatever was bound.
    val bind: handler: (InputEvent -> unit) -> route: InputRoute -> unit

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Send nothing anywhere from now on.
    val unbind: route: InputRoute -> unit

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Report `event` to the bound handler, if any. Input hosts call it from their own event handlers, never from
    /// `Mount`, `Present` or `Dispose`.
    val report: event: InputEvent -> route: InputRoute -> unit

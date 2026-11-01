namespace FS.GG.Game.Render

/// Public contract type exposed by the FS.GG.Game.Render package.
/// The hit region of a selectable object inside its bounds.
[<RequireQualifiedAccess>]
type SelectableShape =
    /// The whole bounds rectangle.
    | Box
    /// The ellipse inscribed in the bounds.
    | Ellipse

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// One object of the game that a player can select, as every backend must offer it: through a pointer on its hit
/// region, through the keyboard, and as one entry of the accessible mirror. `Bounds` is in scene coordinates (the
/// space the `FS.GG.UI.Scene.Scene` is drawn in, y down).
type SelectableObject =
    {
        /// The game's identity for the object. Not blank, and unique among the objects of one state.
        Id: string
        /// The accessible name: the text of its mirror entry. Not blank.
        Label: string
        /// Where the object is, in scene coordinates. Finite, with a width and height that are not negative.
        Bounds: FS.GG.UI.Scene.Rect
        /// The hit region inside `Bounds`.
        Shape: SelectableShape
    }

/// Public contract type exposed by the FS.GG.Game.Render package.
/// What a player asked of the game through a backend's input host. A host asks only for a change: it does not
/// ask to select the object that is already selected, or to clear a selection that is already empty.
[<RequireQualifiedAccess>]
type SelectionRequest =
    /// Select the object with this id.
    | Select of objectId: string
    /// Select nothing.
    | Clear

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// One presented frame of an interactive surface: the Scene to draw, the selectable objects of the same state
/// (in painting order, so the LAST object containing a point is the one on top), and the game's current selection.
/// The selection is the game's, never the backend's: a backend shows it and asks to change it, and it survives
/// every backend switch because it is part of the game state.
[<NoComparison>]
type InteractiveScene =
    {
        /// What to draw.
        Scene: FS.GG.UI.Scene.Scene
        /// The selectable objects, topmost last.
        Objects: SelectableObject list
        /// The id of the selected object, if any.
        Selected: string option
    }

/// Public contract type exposed by the FS.GG.Game.Render package.
/// A keyboard intent. Every backend maps the same keys, those of the retained SVG surface: an arrow right or down
/// focuses the next object, an arrow left or up the previous one, Enter or Space selects the focused object and
/// Escape clears the selection.
[<RequireQualifiedAccess>]
type InteractionKey =
    /// Focus the next object (the first when none is focused), wrapping around.
    | FocusNext
    /// Focus the previous object (the last when none is focused), wrapping around.
    | FocusPrevious
    /// Select the focused object.
    | Activate
    /// Clear the selection.
    | Clear

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// How a game is played through selection on every backend: the selectable objects of a state, the state's current
/// selection and how a `SelectionRequest` changes the state (the hit test mapped to game selection). Built by
/// `SceneInteraction.create`.
[<NoEquality; NoComparison>]
type SceneInteraction<'State>

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// Where the input hosts of a game's backends send a `SelectionRequest`. The adapters are built with the registry,
/// before the game loop that must hear the requests exists, so a route is created first, given to
/// `SceneRenderers.withInteraction`, and bound to the game when it runs. Requests reported while nothing is bound are
/// dropped. Built by `SelectionRoute.create`.
[<Sealed>]
type SelectionRoute

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module SelectionRoute =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A route with nothing bound.
    val create: unit -> SelectionRoute

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Send the route's requests to `handler` from now on, replacing whatever was bound.
    val bind: handler: (SelectionRequest -> unit) -> route: SelectionRoute -> unit

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Send nothing anywhere from now on.
    val unbind: route: SelectionRoute -> unit

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Report `request` to the bound handler, if any. Input hosts call it from their own event handlers, never from
    /// `Mount`, `Present` or `Dispose`.
    val report: request: SelectionRequest -> route: SelectionRoute -> unit

/// Public contract module exposed by the FS.GG.Game.Render package.
///
/// The interaction contract every interactive surface implements (GAME-RENDER-ADOPT, FS.GG.Game#751), so a game whose
/// play is selection stays playable on SVG, Pixi and Babylon alike:
///
/// - **hit test**: a primary pointer press on the topmost object containing the point asks to select it; a press
///   where no object is asks for nothing;
/// - **keyboard**: the surface is focusable and maps the keys of `InteractionKey` through `SceneInteraction.key`;
/// - **accessible mirror**: an HTML group (`data-scene-controls`) holding one button per selectable object, in
///   order, labelled with `SelectableObject.Label`, `aria-pressed` for the selected one, and a polite status naming
///   the selection; activating a button asks to select its object;
/// - **input-host disposal**: the mount's `Dispose` removes every listener the input host added and the mirror.
///
/// `RendererConformance.withInteraction` checks all four on every backend.
[<RequireQualifiedAccess>]
module SceneInteraction =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Declare a game's interaction: `objects state` are the selectable objects of a state, topmost last;
    /// `selected state` is the id of its selected object; `select request state` is the state after the player's
    /// request, the game's own selection rule.
    val create:
        objects: ('State -> SelectableObject list) ->
        selected: ('State -> string option) ->
        select: (SelectionRequest -> 'State -> 'State) ->
            SceneInteraction<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The selectable objects of `state`, topmost last.
    val objects: interaction: SceneInteraction<'State> -> state: 'State -> SelectableObject list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The id of `state`'s selected object.
    val selected: interaction: SceneInteraction<'State> -> state: 'State -> string option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The state after `request`, by the game's own rule.
    val apply: interaction: SceneInteraction<'State> -> request: SelectionRequest -> state: 'State -> 'State

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The frame an interactive surface presents for `state`: `view state` with the state's objects and selection.
    val frame:
        view: ('State -> FS.GG.UI.Scene.Scene) ->
        interaction: SceneInteraction<'State> ->
        state: 'State ->
            InteractiveScene

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Every problem with `objects` (blank or repeated ids, blank labels, bounds that are not finite or have a
    /// negative size), in order; empty when they are valid. An interactive surface refuses to present invalid
    /// objects, and the conformance kit fails a game that declares them.
    val problems: objects: SelectableObject list -> string list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether `point` (scene coordinates) is inside the object's hit region, edges included.
    val contains: point: FS.GG.UI.Scene.Point -> target: SelectableObject -> bool

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The reference hit test: the id of the topmost (last) object whose hit region contains `point`. A backend's
    /// own hit test must agree with it on the points the conformance kit presses.
    val hitTest: point: FS.GG.UI.Scene.Point -> objects: SelectableObject list -> string option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The request a pointer press at `point` makes on `frame`: `Select` of the object hit, unless it is already
    /// selected; nothing when no object is hit.
    val press: point: FS.GG.UI.Scene.Point -> frame: InteractiveScene -> SelectionRequest option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The keyboard reducer every canvas input host shares: from the focused object (dropped when `frame` no longer
    /// has it), the new focus and the request the key makes. `FocusNext` and `FocusPrevious` move the focus with the
    /// retained SVG surface's wrap-around order; `Activate` selects the focused object unless it is already selected;
    /// `Clear` clears a selection that is not empty.
    val key:
        key: InteractionKey ->
        focused: string option ->
        frame: InteractiveScene ->
            string option * SelectionRequest option

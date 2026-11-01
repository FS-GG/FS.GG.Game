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
/// How one selectable object is offered to the keyboard and to the accessible mirror (FS.GG.Game#784, ADR
/// `docs/adr/0001-standard-input-pipeline.md`). Every object stays reachable by a pointer. Built by `ObjectAccess.full`,
/// `ObjectAccess.pointerOnly` and `ObjectAccess.grouped`; a game that declares nothing gets `full` for every object.
///
/// Contract only: FS.GG.Game#788 implements the mirror and keyboard behaviour.
[<Sealed>]
type ObjectAccess

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module ObjectAccess =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Pointer, a stop in the keyboard focus order and its own mirror entry: today's behaviour, and the default.
    val full: ObjectAccess

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Pointer only: no keyboard focus stop and no mirror entry. For objects whose meaning is reachable another way, such
    /// as a tile of a large grid that the game also offers as a typed command.
    val pointerOnly: ObjectAccess

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A member of the group named `label` (not blank). On a `listbox` surface the group's options are nested in a
    /// `role=group` labelled `label`; on a `grid` surface (when any cell group exists) the group is a labelled `rowgroup`
    /// of single-cell rows. Arrow keys move between members in object order, Page Up and Page Down between groups.
    val grouped: label: string -> ObjectAccess

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A member of the group named `label` at (`row`, `column`), both zero or more. When any cell group exists the
    /// surface is one `role=grid`; each cell group is a labelled `rowgroup` of `row`s and `gridcell`s, and arrow keys
    /// move in two dimensions inside it.
    val cell: label: string -> row: int -> column: int -> ObjectAccess

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether the object has a keyboard focus stop and a mirror entry (its own or its group's).
    val isKeyboardReachable: access: ObjectAccess -> bool

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The group's label, when the object is grouped or a cell.
    val group: access: ObjectAccess -> string option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The (row, column) of a cell.
    val cellOf: access: ObjectAccess -> (int * int) option

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// What an input host shows about the player's attention: the object with the keyboard focus, the object under the
/// pointer, and whether focus must be visibly drawn (keyboard or mirror focus, not a pointer press). It is the input
/// host's state, not game state: it is not saved and does not survive a backend switch. But it is DRAWN: every
/// interactive backend draws a focus cue for the focused object, so a sighted keyboard player never navigates blind
/// (FS.GG.Game#787). Built by `InteractionCues.create`.
[<Sealed>]
type InteractionCues

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module InteractionCues =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// No focus, no hover.
    val none: InteractionCues

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The cues for a focused object, a hovered object and whether focus is drawn.
    val create: focused: string option -> hovered: string option -> focusVisible: bool -> InteractionCues

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The id of the object with the keyboard focus, if any.
    val focused: cues: InteractionCues -> string option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The id of the object under the pointer, if any.
    val hovered: cues: InteractionCues -> string option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether the focus cue is drawn: true after keyboard or mirror navigation, false after a pointer press.
    val focusVisible: cues: InteractionCues -> bool

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// What a cue layer draws from: the cues, the frame's objects, and the presented scale in CSS pixels per scene unit,
/// so a layer can keep its bands a fixed on-screen width. Built by the input host (`CueContext.create`).
[<Sealed>]
type CueContext

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module CueContext =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A context. `cssPerUnit` is finite and greater than zero.
    val create: cues: InteractionCues -> objects: SelectableObject list -> cssPerUnit: float -> CueContext

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The cues.
    val cues: context: CueContext -> InteractionCues

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The frame's objects, topmost last.
    val objects: context: CueContext -> SelectableObject list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// CSS pixels per scene unit at the presented size.
    val cssPerUnit: context: CueContext -> float

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

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Declare how each object is offered to the keyboard and the mirror (FS.GG.Game#784), replacing any declaration
    /// made before. Objects the function is not asked about keep `ObjectAccess.full`.
    val withAccess:
        access: (SelectableObject -> ObjectAccess) -> interaction: SceneInteraction<'State> -> SceneInteraction<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// How `target` is offered: the declared access, or `ObjectAccess.full`.
    val access: interaction: SceneInteraction<'State> -> target: SelectableObject -> ObjectAccess

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Draw the cues with the game's own layer (such as the glyph layer's themed cues, FS.GG.Game#789) instead of
    /// `SceneInteraction.standardCues`. The nodes are drawn above the frame's Scene on every backend, unlit on Babylon.
    val withCues:
        draw: (CueContext -> FS.GG.UI.Scene.SceneNode list) ->
        interaction: SceneInteraction<'State> ->
            SceneInteraction<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The cue layer in force: the game's (`withCues`) or `SceneInteraction.standardCues`.
    val cues: interaction: SceneInteraction<'State> -> (CueContext -> FS.GG.UI.Scene.SceneNode list)

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The standard cue layer: when focus is visible, a two-tone ring (a dark outer and a light inner band, each at least
    /// 2 CSS pixels at the context's scale) drawn as filled rectangles just outside the focused object's bounds, so it
    /// contrasts with any background and every backend can draw it; nothing for hover. Contract only: FS.GG.Game#788
    /// implements it, and until then it raises.
    val standardCues: context: CueContext -> FS.GG.UI.Scene.SceneNode list

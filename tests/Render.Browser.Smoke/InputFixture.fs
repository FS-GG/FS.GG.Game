module FS.GG.Game.Render.Browser.Smoke.InputFixture

// FS.GG.Game#788 PR C: a board that exercises every kind of ObjectAccess for the kit's `input` section, and a reaction
// that answers through the standard input pipeline with an ASYNCHRONOUS F# guest, so I12 waits for a selection the
// game answers later.
//
// - "a": full, ungrouped;
// - "u1", "u2": grouped "Units";
// - "t00", "t02", "t10", "t12": cells of "Map" with an empty column 1 (sparse);
// - "p": pointer only.

open Browser.Dom
open Fable.Core
open FS.GG.Game.Render
open FS.GG.UI.Scene

type Board = { Tick: int; Selected: string option }

let width = 240.0
let height = 160.0

let private rgb red green blue =
    {
        Red = red
        Green = green
        Blue = blue
        Alpha = 255uy
    }

let background = rgb 24uy 40uy 72uy
let tileColour = rgb 200uy 120uy 40uy
let selectedColour = rgb 250uy 210uy 60uy
/// The tick marker's colour: one per tick, so a backend that shows another state is told apart.
let tickColour (tick: int) = rgb 40uy (byte (60 + 40 * (tick % 4))) 90uy

let private tile id label x y : SelectableObject =
    {
        Id = id
        Label = label
        Bounds = { X = x; Y = y; Width = 30.0; Height = 30.0 }
        Shape = SelectableShape.Box
    }

let objects (_: Board) : SelectableObject list =
    [
        tile "a" "Anchor" 10.0 10.0
        tile "u1" "Scout" 60.0 10.0
        tile "u2" "Archer" 110.0 10.0
        tile "t00" "Tile 0 0" 10.0 60.0
        tile "t02" "Tile 0 2" 110.0 60.0
        tile "t10" "Tile 1 0" 10.0 110.0
        tile "t12" "Tile 1 2" 110.0 110.0
        tile "p" "Portal" 180.0 60.0
    ]

let access (target: SelectableObject) : ObjectAccess =
    match target.Id with
    | "u1"
    | "u2" -> ObjectAccess.grouped "Units"
    | "t00" -> ObjectAccess.cell "Map" 0 0
    | "t02" -> ObjectAccess.cell "Map" 0 2
    | "t10" -> ObjectAccess.cell "Map" 1 0
    | "t12" -> ObjectAccess.cell "Map" 1 2
    | "p" -> ObjectAccess.pointerOnly
    | _ -> ObjectAccess.full

let select (request: SelectionRequest) (board: Board) : Board =
    match request with
    | SelectionRequest.Select id -> { board with Selected = Some id }
    | SelectionRequest.Clear -> { board with Selected = None }

let interaction: SceneInteraction<Board> =
    SceneInteraction.create objects (fun board -> board.Selected) select
    |> SceneInteraction.withAccess access

let view (board: Board) : Scene =
    {
        Nodes =
            [
                yield SceneNode.Rectangle((0.0, 0.0, width, height), background)
                yield SceneNode.Rectangle((200.0, 130.0, 30.0, 20.0), tickColour board.Tick)

                for target in objects board do
                    let b = target.Bounds

                    yield
                        SceneNode.Rectangle(
                            (b.X, b.Y, b.Width, b.Height),
                            (if board.Selected = Some target.Id then selectedColour else tileColour)
                        )
            ]
    }

[<Emit("setTimeout($1, $0)")>]
let private after (_milliseconds: int) (_action: unit -> unit) : unit = jsNative

/// The game's reaction for the kit: every event goes into an `InputPipeline` whose guest is asynchronous (it answers 15
/// ms later), and the decision's `Local` (the new selection) is applied to the kit's host through `apply`.
let pipelineReaction () : ((Board -> Board) -> unit) -> InputEvent -> unit =
    let mutable current: ((Board -> Board) -> unit) option = None

    let guest: InputGuest<unit, unit, string option, unit> =
        InputGuest.ofAsync
            (fun request _ ->
                async {
                    do! Async.Sleep 15

                    return
                        match GuestCall.input (GuestRequest.call request) with
                        | PipelineInput.Scene event ->
                            match InputEvent.action event, InputEvent.target event with
                            | InputAction.Activate, Some id -> GuestOutcome.Decided [ GuestIntent.Local(Some id) ]
                            | InputAction.Cancel, _ -> GuestOutcome.Decided [ GuestIntent.Local None ]
                            | _ -> GuestOutcome.Decided []
                        | PipelineInput.Game() -> GuestOutcome.Decided []
                })
            GuestPolicy.standard

    let pipeline = InputPipeline.create guest (CommandStage.create (fun (version: uint64) -> version))

    let io =
        PipelineIo.create
            ignore
            (fun local ->
                current
                |> Option.iter (fun apply -> apply (fun board -> { board with Selected = local })))
            ignore
            ignore
            after

    // The kit binds its own route; the pipeline is fed directly.
    let host = PipelineHost.start pipeline (InputRoute.create ()) io "fixture" None 0UL

    fun apply event ->
        current <- Some apply
        host.Dispatch(PipelineMsg.Input(PipelineInput.Scene event))

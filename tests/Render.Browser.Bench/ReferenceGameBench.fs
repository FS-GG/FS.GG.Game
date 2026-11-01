module FS.GG.Game.Render.Bench.ReferenceGameBench

// The ReferenceGame plug-in: the REAL `FS.GG.Game.Reference.ReferenceState.Model` state type (src/ReferenceGame/State.fs)
// and its own `update`, `fixedTick` and `applyEffectResult` (src/ReferenceGame/Program.fs; both are compiled into this
// page), driven by a fixed 120-frame script that walks the reference journey (start, move, door, vault cleared, exit,
// won) and restarts.

open FS.GG.Game.Reference
open FS.GG.Game.Reference.ReferenceState
open FS.GG.Game.Reference.Composition
open FS.GG.UI.Scene

/// The benchmark canvas, in scene units (one unit is one CSS pixel on every backend).
let width = 960.0
let height = 540.0

let private color r g b : Color =
    {
        Red = byte r
        Green = byte g
        Blue = byte b
        Alpha = 255uy
    }

let private roomTint (room: Room) =
    match room with
    | Atrium -> 40, 90, 140
    | Vault -> 140, 60, 40
    | Exit -> 50, 140, 70

/// The aim point the script moves around an ellipse, one step per frame.
let private aimAt (frame: int) =
    let angle = float frame * 0.05
    int (width / 2.0 + 300.0 * cos angle), int (height / 2.0 + 200.0 * sin angle)

/// One frame of the reference journey: aim moves every frame; every 120 frames the player moves twice, opens the
/// vault, clears it, leaves through the exit and wins; the won game restarts with its tick count kept, so the
/// script never ends.
let step (frame: int) (model: Model) : Model =
    let aimed = update (AimAt(aimAt frame)) model

    let scripted =
        match frame % 120 with
        | 30
        | 60 -> update MoveRight aimed
        | 90 -> update TryDoor aimed
        | 100 -> applyEffectResult VaultEnemiesDefeated aimed
        | 110 -> update TryDoor aimed
        | _ -> aimed

    let ticked = fixedTick scripted

    if ticked.Screen = Won then
        update StartGame { boot () with Tick = ticked.Tick }
    else
        ticked

let init () = update StartGame (boot ())

/// Background, player and aim marker: the three nodes every scene draws on top of its `size - 3` body nodes.
let private frame (model: Model) (body: SceneNode list) : Scene =
    let r, g, b = roomTint model.Room
    let ax, ay = model.Aim

    {
        Nodes =
            [
                yield SceneNode.Rectangle((0.0, 0.0, width, height), color (r / 4) (g / 4) (b / 4))
                yield! body
                yield
                    SceneNode.Circle(
                        { X = 60.0 + 120.0 * float model.X; Y = height - 60.0 },
                        24.0,
                        color 240 220 80
                    )
                yield
                    SceneNode.FilledEllipse(
                        {
                            X = float ax - 10.0
                            Y = float ay - 6.0
                            Width = 20.0
                            Height = 12.0
                        },
                        color 250 250 250
                    )
            ]
    }

/// Grid placement for `count` cells over the canvas.
let private grid (count: int) =
    let n = max 1 count
    let cols = max 1 (int (ceil (sqrt (float n * width / height))))
    let rows = max 1 ((n + cols - 1) / cols)
    cols, width / float cols, height / float rows

/// `room-tiles`: a static floor of `size - 3` tiles tinted by the room. Between room changes only the aim marker (and
/// at the scripted steps the player) changes, so this measures the cost of a mostly unchanged retained scene.
let tiles (size: int) (model: Model) : Scene =
    let count = max 0 (size - 3)
    let cols, cw, ch = grid count
    let r, g, b = roomTint model.Room

    let body =
        [
            for i in 0 .. count - 1 do
                let x = float (i % cols) * cw
                let y = float (i / cols) * ch
                let shade = 30 * ((i * 7) % 5)
                SceneNode.Rectangle((x + 0.5, y + 0.5, max 0.5 (cw - 1.0), max 0.5 (ch - 1.0)), color (r + shade) (g + shade) (b + shade))
        ]

    frame model body

/// `room-swarm`: `size - 3` particles that all move every frame (each orbits its own anchor with the tick), so every
/// node changes on every Present. This measures the full update path at the requested size.
let swarm (size: int) (model: Model) : Scene =
    let count = max 0 (size - 3)
    let cols, cw, ch = grid count
    let t = float model.Tick
    let radius = max 1.0 (min cw ch * 0.35)

    let body =
        [
            for i in 0 .. count - 1 do
                let ax = (float (i % cols) + 0.5) * cw
                let ay = (float (i / cols) + 0.5) * ch
                let phase = t * 0.07 + float i * 0.61803398875
                let x = ax + radius * cos phase
                let y = ay + radius * sin phase
                SceneNode.Circle({ X = x; Y = y }, radius, color (80 + (i * 37) % 170) (80 + (i * 53) % 170) 200)
        ]

    frame model body

let game: BenchGame<Model> =
    {
        Id = "reference-game"
        StateType = "FS.GG.Game.Reference.ReferenceState.Model"
        Width = width
        Height = height
        Init = init
        Step = step
        Scenes =
            [
                {
                    Id = "room-tiles"
                    Description = "static tiled floor tinted by room; only the aim marker and player move"
                    View = tiles
                }
                {
                    Id = "room-swarm"
                    Description = "every body node orbits its anchor and changes on every frame"
                    View = swarm
                }
            ]
    }

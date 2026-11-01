namespace FS.GG.Game.Reference

open FS.GG.Game.Render
open FS.GG.UI.Scene
open FS.GG.Game.Reference.ReferenceState

/// The reference game's renderers (GAME-RENDER-ADOPT-01.1, FS.GG.Game#745): one Scene description of
/// `ReferenceState.Model` that the SVG, Pixi and Babylon surfaces all draw. It uses only `SceneNode` data, so the same
/// file compiles for .NET and, through Fable, for the browser host.
module ReferenceRenderers =

    /// Scene size in pixels.
    let width = 240.0
    let height = 120.0

    let private rgb red green blue =
        {
            Red = red
            Green = green
            Blue = blue
            Alpha = 255uy
        }

    /// The room fills the scene.
    let roomColor (room: Room) : Color =
        match room with
        | Atrium -> rgb 30uy 60uy 120uy
        | Vault -> rgb 90uy 30uy 30uy
        | Exit -> rgb 20uy 100uy 40uy

    /// The player token's colour says which screen is up.
    let tokenColor (screen: Screen) : Color =
        match screen with
        | Menu -> rgb 160uy 160uy 160uy
        | Playing -> rgb 230uy 200uy 40uy
        | Paused -> rgb 200uy 100uy 220uy
        | Won -> rgb 250uy 250uy 250uy

    /// The token is a 24-pixel square in the middle band, one 32-pixel column per step of `X` (clamped to the scene).
    let tokenBounds (model: Model) : float * float * float * float =
        let column = model.X |> max 0 |> min 6
        16.0 + 32.0 * float column, 48.0, 24.0, 24.0

    /// The aim marker: an 8-pixel circle at the aim point, clamped to the scene.
    let aimCenter (model: Model) : float * float =
        let x, y = model.Aim
        float x |> max 4.0 |> min (width - 4.0), float y |> max 4.0 |> min (height - 4.0)

    /// The colour of the ring drawn around the selected object.
    let selectionColor: Color = rgb 255uy 140uy 0uy

    /// The selectable objects (FS.GG.Game#751): the player token and, on top of it, the aim marker.
    let objects (model: Model) : SelectableObject list =
        let x, y, w, h = tokenBounds model
        let aimX, aimY = aimCenter model

        [
            {
                Id = "token"
                Label = "Player token"
                Bounds = { X = x; Y = y; Width = w; Height = h }
                Shape = SelectableShape.Box
            }
            {
                Id = "aim"
                Label = "Aim marker"
                Bounds =
                    {
                        X = aimX - 4.0
                        Y = aimY - 4.0
                        Width = 8.0
                        Height = 8.0
                    }
                Shape = SelectableShape.Ellipse
            }
        ]

    /// The reference game's selection rule: an object of the current state becomes the selection, Clear empties it,
    /// and a request for an id the state does not have changes nothing.
    let select (request: SelectionRequest) (model: Model) : Model =
        match request with
        | SelectionRequest.Clear -> { model with Selected = None }
        | SelectionRequest.Select id when objects model |> List.exists (fun target -> target.Id = id) ->
            { model with Selected = Some id }
        | SelectionRequest.Select _ -> model

    /// The reference game's interaction: selecting its objects on any backend.
    let interaction: SceneInteraction<Model> =
        SceneInteraction.create objects (fun model -> model.Selected) select

    /// The shared Scene description of the reference game's state. The selected object gets a ring behind it.
    let view (model: Model) : Scene =
        let aimX, aimY = aimCenter model
        let x, y, w, h = tokenBounds model

        let ring =
            match model.Selected with
            | Some "token" -> [ SceneNode.Rectangle((x - 3.0, y - 3.0, w + 6.0, h + 6.0), selectionColor) ]
            | Some "aim" -> [ SceneNode.Circle({ X = aimX; Y = aimY }, 7.0, selectionColor) ]
            | _ -> []

        let aimRing, tokenRing =
            match model.Selected with
            | Some "aim" -> ring, []
            | _ -> [], ring

        {
            Nodes =
                [
                    yield SceneNode.Rectangle((0.0, 0.0, width, height), roomColor model.Room)
                    yield! tokenRing
                    yield SceneNode.Rectangle(tokenBounds model, tokenColor model.Screen)
                    yield! aimRing
                    yield SceneNode.Circle({ X = aimX; Y = aimY }, 4.0, rgb 255uy 255uy 255uy)
                ]
        }

    /// The reference game's renderer declaration: SVG over `svg` plus every other surface given (an SVG surface among
    /// them replaces the one over `svg`). Add `SceneRenderers.withInteraction interaction route` to make it playable
    /// by selection on every backend.
    let declare
        (svg: Scene -> Result<RendererMount<Scene>, string>)
        (surfaces: SceneSurface list)
        : SceneRenderers<Model> =
        surfaces
        |> List.fold (fun renderers surface -> SceneRenderers.withSurface surface renderers) (SceneRenderers.create view svg)

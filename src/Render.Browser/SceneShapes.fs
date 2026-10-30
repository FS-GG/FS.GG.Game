namespace FS.GG.Game.Render.Browser

open FS.GG.UI.Scene

/// The projection of a `Scene` onto flat shapes that the canvas adapters (Babylon, Pixi) share, so each draws the
/// same game state the SVG adapter draws.
module internal SceneShapes =

    type Kind =
        | Flat
        | Round

    /// One drawable: where it sits in scene coordinates (y down, like the SVG) and what colour it is.
    type Shape =
        {
            Kind: Kind
            X: float
            Y: float
            Width: float
            Height: float
            Fill: Color
        }

    let rec collect (dx: float, dy: float) (scene: Scene) (shapes: ResizeArray<Shape>) : int =
        let mutable unsupported = 0

        for node in scene.Nodes do
            match node with
            | SceneNode.Empty -> ()
            | SceneNode.Group inner ->
                for s in inner do
                    unsupported <- unsupported + collect (dx, dy) s shapes
            | SceneNode.Translate((ox, oy), inner) ->
                unsupported <- unsupported + collect (dx + ox, dy + oy) inner shapes
            | SceneNode.Rectangle((x, y, w, h), fill) ->
                shapes.Add
                    {
                        Kind = Flat
                        X = x + dx
                        Y = y + dy
                        Width = w
                        Height = h
                        Fill = fill
                    }
            | SceneNode.FilledEllipse(bounds, fill) ->
                shapes.Add
                    {
                        Kind = Round
                        X = bounds.X + dx
                        Y = bounds.Y + dy
                        Width = bounds.Width
                        Height = bounds.Height
                        Fill = fill
                    }
            | SceneNode.Circle(center, radius, fill) ->
                shapes.Add
                    {
                        Kind = Round
                        X = center.X - radius + dx
                        Y = center.Y - radius + dy
                        Width = 2.0 * radius
                        Height = 2.0 * radius
                        Fill = fill
                    }
            | _ -> unsupported <- unsupported + 1

        unsupported

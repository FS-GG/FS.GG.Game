namespace FS.GG.Game.Render.Browser

open FS.GG.UI.Scene

/// The projection of a `Scene` onto flat shapes that the canvas adapters (Babylon, Pixi) share, so each draws the
/// same game state the SVG adapter draws.
module internal SceneShapes =

    /// The form a shape takes: a rectangle, or an ellipse inscribed in its bounds.
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

    /// Append the shapes of `scene`, offset by `(dx, dy)`, in painting order. `Rectangle`, `FilledEllipse` and
    /// `Circle` become shapes; `Group`, `Translate` and `Empty` are structural. Returns how many nodes have no
    /// shape and were skipped.
    val collect: dx: float * dy: float -> scene: Scene -> shapes: ResizeArray<Shape> -> int

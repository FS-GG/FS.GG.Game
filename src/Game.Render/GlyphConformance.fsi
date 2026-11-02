namespace FS.GG.Game.Render

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// One case of the kit's glyph section (ADR GAME-RENDER-02.4 D6, D10 and D11; FS.GG.Game#789): the scenes a backend
/// presents in order, the pixels the last one must show, and the controls, each a set of pixels that what it draws
/// must NOT all show. Built by `GlyphConformance.cases`.
[<Sealed>]
type GlyphCase =

    /// What the case checks.
    member Name: string

    /// The canvas width, in scene units.
    member Width: float

    /// The canvas height, in scene units.
    member Height: float

    /// The scenes to present, in order; the pixels are read after the last.
    member Scenes: FS.GG.UI.Scene.Scene list

    /// The pixels the last scene must show (`ScenePixels.identityExpectations`).
    member Expected: PixelExpectation list

    /// Each control's name and pixels: a backend that drew the last scene must fail at least one pixel of each.
    member Controls: (string * PixelExpectation list) list

    /// Each drawn control's name, scene, own pixels and modelled difference: a backend draws the control scene,
    /// waits (with retries) until it shows the control's own pixels, so the read is never of a stale frame, and then at
    /// least the modelled number of this case's `Expected` pixels must differ on it.
    member DrawnControls: (string * FS.GG.UI.Scene.Scene * PixelExpectation list * int) list

/// Public contract module exposed by the FS.GG.Game.Render package.
///
/// The kit's glyph section (ADR D6): cases every backend must pass, run by a game's or the smoke's pixel probe.
[<RequireQualifiedAccess>]
module GlyphConformance =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The cases, for `themes` (the first is the subject; the second, when given, is the theme-swap control and the B
    /// of the switch; a theme derived from the first is used otherwise):
    /// - the starter set (`Glyphs.samples`) in each theme, against the same set in the next theme;
    /// - each of the five identity frames alone, monochrome at 24 units, against each other frame (20 ordered pairs),
    ///   then again with team badges inside the frames;
    /// - each of the ten badge digits alone against the other nine (90 ordered pairs);
    /// - the five frames in their allegiance colours, for the box-edge pixels just inside and outside each edge;
    /// - D9's dense board (32-unit tiles, 24-unit frames with badges inside, status bars above), against the same board
    ///   drawn bodies first;
    /// - the theme switch A, B, A with the focus ring of the cue layer showing, which must show A's pixels again,
    ///   against B's;
    /// - D12's team-badge check: two teams that share an allegiance in a monochrome theme, against each other;
    /// - erratum E1: team 0 on the own allegiance (one colour in `classic`) shows a visible ring, against the unit drawn
    ///   with no team;
    /// - erratum E6: a unit and a structure of the same identity, size and badge, monochrome and with no icon, against
    ///   each other.
    /// - a hostile structure's foot band, which must show its team colour, against the structure with no team.
    /// The dense board, the ring, the family and the foot-band cases also carry drawn controls.
    val cases: themes: Theme list -> GlyphCase list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A monochrome theme derived from `theme`: every team and allegiance in one grey, so only frame shape and badge
    /// tell glyphs apart (D12).
    val monochrome: theme: Theme -> Theme

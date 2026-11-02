namespace FS.GG.Game.Render

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// The five identities of ADR GAME-RENDER-02.4 D1 (FS.GG.Game#789). The frame shape of each follows from it in the
/// glyph library, after APP-6: a theme chooses identity colours, never shapes.
[<RequireQualifiedAccess>]
type Identity =
    /// The viewer's own side: the APP-6 friend rectangle.
    | Own
    /// A friend that is not the viewer's: the friend rectangle with holes at the quarter points of its long edges.
    | Ally
    /// The APP-6 neutral square.
    | Neutral
    /// The APP-6 hostile diamond, stepped.
    | Enemy
    /// The APP-6 unknown quatrefoil, stepped.
    | Unknown

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// An OKLCH colour, the polar form of OKLab as CSS Color 4 defines it: lightness `L` from 0 to 1, chroma `C` from 0,
/// and hue `H` in degrees from 0 to 360. Hue is 0 when chroma is 0.
type Oklch =
    {
        /// Lightness, 0 (black) to 1 (white).
        L: float
        /// Chroma, from 0 (grey).
        C: float
        /// Hue in degrees, 0 to 360.
        H: float
    }

/// Public contract module exposed by the FS.GG.Game.Render package.
///
/// Deterministic colour arithmetic for themes (ADR D11): the same bytes on .NET and through Fable. Every derived colour
/// is gamut-mapped as CSS Color 4 does, then quantised by clamping `v = channel x 255` to [0, 255] and taking
/// `Floor(v + 0.5)`, never `Floor(v)` and never `Math.Round`. Contrast is computed on the 8-bit values through a
/// committed 256-entry sRGB-to-linear table, with no power function at validation time.
[<RequireQualifiedAccess>]
module ThemeColour =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The 8-bit value of a channel from 0 to 1: clamped to [0, 255] after scaling, then `Floor(v + 0.5)`. NaN is 0.
    val quantise: channel: float -> byte

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The linear-light value of an 8-bit sRGB channel, read from the committed table.
    val linear: channel: byte -> float

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// WCAG 2.x relative luminance of the colour's 8-bit channels, through the committed table.
    val luminance: colour: FS.GG.UI.Scene.Color -> float

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// WCAG 2.x contrast ratio of two colours, from 1 to 21, on their 8-bit channels.
    val contrast: a: FS.GG.UI.Scene.Color -> b: FS.GG.UI.Scene.Color -> float

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The OKLCH value of an 8-bit sRGB colour (its alpha is ignored).
    val toOklch: colour: FS.GG.UI.Scene.Color -> Oklch

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The opaque 8-bit sRGB colour of an OKLCH value: chroma is reduced at constant lightness and hue until the colour
    /// is in sRGB within the OKLab just-noticeable difference (0.02), as CSS Color 4 maps gamut, then each channel is
    /// quantised. Every 8-bit colour survives `toOklch` then `ofOklch` unchanged.
    val ofOklch: colour: Oklch -> FS.GG.UI.Scene.Color

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// `#rrggbb`, lower case, of the colour's 8-bit channels (its alpha is not written).
    val hex: colour: FS.GG.UI.Scene.Color -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The opaque colour of `#rrggbb` (either case), or `None` for anything else.
    val ofHex: text: string -> FS.GG.UI.Scene.Color option

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// The seed layer of a theme (ADR D4): intent, a small closed set of named colours and numbers. Built from
/// `ThemeSeed.classic` with `ThemeSeed.colour`, `ThemeSeed.number` and `ThemeSeed.named`.
[<Sealed>]
type ThemeSeed

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module ThemeSeed =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The seed colour names: `ground`, `ink`, `allegiance.own`, `allegiance.ally`, `allegiance.neutral`,
    /// `allegiance.enemy`, `allegiance.unknown`, `team.0` to `team.7` (the teams are `team.0` up to the first one
    /// missing), `accent.selection`, `accent.marker`, `status.good`, `status.warn` and `status.bad`.
    val colourNames: string list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The seed number names: `contrastTarget` (the floor against the ground, 3 or more), `cell` (scene units),
    /// `strokeBase` (the band width, scene units), `gap` (scene units), `density` (a multiplier of `cell`),
    /// `minCssPerUnit` (the smallest presented scale the theme supports) and `hueCapacity.0` to `hueCapacity.3` (the
    /// usable team hues at each size step, at most 8).
    val numberNames: string list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The seed of the `classic` preset: a dark ground, white ink and eight team hues.
    val classic: ThemeSeed

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The seed with the theme name `name`.
    val named: name: string -> seed: ThemeSeed -> ThemeSeed

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The seed with colour `name` set to `colour` (a name outside `colourNames` is refused by `Theme.create`).
    val colour: name: string -> colour: FS.GG.UI.Scene.Color -> seed: ThemeSeed -> ThemeSeed

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The seed with number `name` set to `value` (a name outside `numberNames` is refused by `Theme.create`).
    val number: name: string -> value: float -> seed: ThemeSeed -> ThemeSeed

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The seed with colour `name` removed (a team past the first missing one is not a team).
    val without: name: string -> seed: ThemeSeed -> ThemeSeed

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The theme name.
    val name: seed: ThemeSeed -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The colour named `name`, if the seed sets it.
    val tryColour: name: string -> seed: ThemeSeed -> FS.GG.UI.Scene.Color option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The number named `name`, if the seed sets it.
    val tryNumber: name: string -> seed: ThemeSeed -> float option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Every colour the seed sets, by name, in name order.
    val colours: seed: ThemeSeed -> (string * FS.GG.UI.Scene.Color) list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Every number the seed sets, by name, in name order.
    val numbers: seed: ThemeSeed -> (string * float) list

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// The map layer of a theme (ADR D4): named colours and numbers that algorithms derive from the seed.
[<Sealed>]
type ThemeTokens

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module ThemeTokens =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// No tokens.
    val empty: ThemeTokens

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The colour token `name`, if set.
    val tryColour: name: string -> tokens: ThemeTokens -> FS.GG.UI.Scene.Color option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The number token `name`, if set.
    val tryNumber: name: string -> tokens: ThemeTokens -> float option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The tokens with colour `name` set to `colour`.
    val withColour: name: string -> colour: FS.GG.UI.Scene.Color -> tokens: ThemeTokens -> ThemeTokens

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The tokens with number `name` set to `value`.
    val withNumber: name: string -> value: float -> tokens: ThemeTokens -> ThemeTokens

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Every token name, in name order.
    val names: tokens: ThemeTokens -> string list

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// A theme algorithm (ADR D4): `seed -> map -> map`, refining what the earlier algorithms of the list produced. Built by
/// `ThemeAlgorithm.create`; `ThemeAlgorithm.standard` is the base every theme starts from.
[<Sealed>]
type ThemeAlgorithm

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module ThemeAlgorithm =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// An algorithm named `name` (the name is what DTCG export records and import looks up).
    val create: name: string -> refine: (ThemeSeed -> ThemeTokens -> ThemeTokens) -> ThemeAlgorithm

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The algorithm's name.
    val name: algorithm: ThemeAlgorithm -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The base algorithm, named `standard`: it copies the seed's colours into the map and derives the rest in OKLab
    /// (the unit body, the dark or light pole opposite the ink for badge, icon, track and inner focus ink, hover,
    /// stale and the terrain kinds), and the sizes (`cell` times `density`, the band, gap, bracket and hover widths).
    val standard: ThemeAlgorithm

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// One value set after the algorithms run: a map token or an alias, by name (ADR D4; a player's team-colour override
/// is `ThemeOverride.colour "glyph.team.N"`). Built by `ThemeOverride.colour` and `ThemeOverride.number`.
[<Sealed>]
type ThemeOverride

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module ThemeOverride =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Set the colour of the alias or map token `token`.
    val colour: token: string -> colour: FS.GG.UI.Scene.Color -> ThemeOverride

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Set the number of the alias or map token `token`.
    val number: token: string -> value: float -> ThemeOverride

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// Why a theme cannot be built: the token and the rule it breaks.
[<Sealed>]
type ThemeViolation =

    /// The token (seed, map or alias name) that breaks the rule.
    member Token: string

    /// The rule and the measured value.
    member Reason: string

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// A resolved, validated theme (ADR D4): seed, map and alias tokens. Glyph drawing reads its alias tokens only. A theme
/// is a view input, never game state: switching themes re-renders the next frame from the same state. Built by
/// `Theme.create`, `Theme.ofDtcg` or a preset (`Theme.classic`).
[<Sealed>]
type Theme

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module Theme =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Resolve and validate a theme: run `algorithms` over the seed in order, apply `overrides`, read the aliases, then
    /// check every rule. The theme, or every violation:
    /// - an unknown seed, map or alias name, a missing colour, a colour with alpha below 255 (alpha waits for Babylon and
    ///   the kit, Q5), or a number out of range;
    /// - the WCAG 2.x contrast floor (`contrastTarget`, at least 3:1) against `surface.ground` for every team and
    ///   allegiance colour, `glyph.unit.body`, `glyph.stale`, `glyph.selection`, `glyph.marker`, `glyph.path`,
    ///   `cue.focus.outer` and `cue.hover`;
    /// - 3:1 between colours that touch: `cue.focus.inner` on `cue.focus.outer`; `glyph.badge.ink` on the body and on
    ///   every team and allegiance colour; `glyph.icon.ink` on the body and every allegiance colour; every team colour
    ///   on the body (the team mark sits between fill and body); every status fill on `status.track`.
    val create:
        seed: ThemeSeed -> algorithms: ThemeAlgorithm list -> overrides: ThemeOverride list -> Result<Theme, ThemeViolation list>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The theme with `overrides` applied after its own, validated again (a player's team-colour override: an override
    /// that fails is refused with its reasons).
    val withOverrides: overrides: ThemeOverride list -> theme: Theme -> Result<Theme, ThemeViolation list>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The `classic` preset: dark, the default (Q10). Its resolved values are baked at build time, and a test compares
    /// them with a fresh derivation from `ThemeSeed.classic`.
    val classic: Theme

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The shipped presets, `classic` first. FS.GG.Game#795 adds `light`, `highContrast`, `colourBlindSafe` and
    /// `monochrome`.
    val presets: Theme list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The theme's name.
    val name: theme: Theme -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The seed the theme was resolved from.
    val seed: theme: Theme -> ThemeSeed

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The map tokens the algorithms and overrides produced.
    val tokens: theme: Theme -> ThemeTokens

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The names of the algorithms, in order.
    val algorithms: theme: Theme -> string list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The alias names of this theme, in order: `surface.ground`, `glyph.unit.body`, `glyph.allegiance.*`,
    /// `glyph.team.0` to the last team, `glyph.badge.ink`, `glyph.icon.ink`, `glyph.selection`, `glyph.marker`,
    /// `glyph.path`, `glyph.stale`, `cue.focus.outer`, `cue.focus.inner`, `cue.hover`, `status.*`, `terrain.*` (open,
    /// blocked, difficult, water, void), then the numbers `size.cell`, `stroke.outline`, `stroke.gap`,
    /// `stroke.bracket` and `stroke.hover`. The set is closed: it grows only through the ADR's own review.
    val aliases: theme: Theme -> string list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The colour of alias `alias`, if it is a colour alias of the theme.
    val tryColour: alias: string -> theme: Theme -> FS.GG.UI.Scene.Color option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The number of alias `alias`, if it is a number alias of the theme.
    val tryNumber: alias: string -> theme: Theme -> float option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The colour of `identity` (`glyph.allegiance.*`).
    val allegiance: identity: Identity -> theme: Theme -> FS.GG.UI.Scene.Color

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// How many teams the theme colours (`glyph.team.0` onwards).
    val teamCount: theme: Theme -> int

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The smallest presented scale, in CSS px per scene unit, the theme supports (default 1). The cue reserve is
    /// sized for it.
    val minCssPerUnit: theme: Theme -> float

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// How many team hues are usable at `sizeStep` (ADR D12): the theme's capacity for that step, the last step's for a
    /// larger one and the first's for a negative one. At most 8.
    val hueCapacity: theme: Theme -> sizeStep: int -> int

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Every colour alias as `#rrggbb`, in alias order: for parts of a game the Scene does not draw.
    val hex: theme: Theme -> (string * string) list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Every alias as a CSS custom property, in alias order: `--fsgg-` and the alias with dots as hyphens, and a value
    /// of `#rrggbb` for a colour or a plain number for a size. The colours equal the Scene's.
    val cssVariables: theme: Theme -> (string * string) list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The theme as a DTCG 2025.10 token file: the seed (group `seed`) and every resolved alias (group `alias`), each
    /// colour as `{colorSpace: "oklch", components, hex}`, each number as a number, with the theme's name and algorithm
    /// names under the `$extensions` key `gg.fs.theme`. The text is the same bytes on .NET and through Fable.
    val toDtcg: theme: Theme -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A DTCG 2025.10 resolver document over `themes`: a `theme` modifier with one context per theme file
    /// (`<name>.tokens.json`, the first by default) and a `density` modifier (`regular`, the default, and `compact`,
    /// which sets the seed's `density` to 0.75), resolved in that order.
    val dtcgResolver: themes: Theme list -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Read a DTCG token file written by `toDtcg` (or edited from one): group `seed` becomes the seed and group `alias`
    /// becomes alias overrides, so deleting the alias group derives every alias again from the edited seed. A colour
    /// with a `hex` is read from the hex, never from its components. The algorithms are looked up by name in
    /// `algorithms`. Export then import gives equal resolved hex for every alias.
    val ofDtcg: algorithms: ThemeAlgorithm list -> json: string -> Result<Theme, ThemeViolation list>

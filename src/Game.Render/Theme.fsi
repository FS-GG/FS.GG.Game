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
    /// The seed of the `classic` preset: a dark ground, white ink and eight team hues distinct from every allegiance
    /// colour. Provisional: FS.GG.Game#795's derivation is the source of truth for every preset, `classic` included,
    /// and replaces this seed's values when it lands.
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

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module ThemeViolation =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A violation of `token`, for a `ThemeCheck` to report.
    val create: token: string -> reason: string -> ThemeViolation

/// The resolved colours glyph drawing reads (internal: the library's own view of the alias layer).
[<NoComparison>]
type internal Palette =
    {
        Ground: FS.GG.UI.Scene.Color
        Body: FS.GG.UI.Scene.Color
        Own: FS.GG.UI.Scene.Color
        Ally: FS.GG.UI.Scene.Color
        Neutral: FS.GG.UI.Scene.Color
        Enemy: FS.GG.UI.Scene.Color
        Unknown: FS.GG.UI.Scene.Color
        Teams: FS.GG.UI.Scene.Color list
        BadgeInk: FS.GG.UI.Scene.Color
        IconInk: FS.GG.UI.Scene.Color
        Selection: FS.GG.UI.Scene.Color
        Marker: FS.GG.UI.Scene.Color
        Path: FS.GG.UI.Scene.Color
        Stale: FS.GG.UI.Scene.Color
        FocusOuter: FS.GG.UI.Scene.Color
        FocusInner: FS.GG.UI.Scene.Color
        Hover: FS.GG.UI.Scene.Color
        Good: FS.GG.UI.Scene.Color
        Warn: FS.GG.UI.Scene.Color
        Bad: FS.GG.UI.Scene.Color
        Track: FS.GG.UI.Scene.Color
        Keyline: FS.GG.UI.Scene.Color
        ModifierInk: FS.GG.UI.Scene.Color
        TerrainOpen: FS.GG.UI.Scene.Color
        TerrainBlocked: FS.GG.UI.Scene.Color
        TerrainDifficult: FS.GG.UI.Scene.Color
        TerrainWater: FS.GG.UI.Scene.Color
        TerrainVoid: FS.GG.UI.Scene.Color
        Cell: float
        Band: float
        Gap: float
        Bracket: float
        HoverWidth: float
        MinCssPerUnit: float
        HueCapacity: int list
    }

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// A resolved, validated theme (ADR D4): seed, map and alias tokens. Glyph drawing reads its alias tokens only. A theme
/// is a view input, never game state: switching themes re-renders the next frame from the same state. Built by
/// `Theme.create`, `Theme.ofDtcg` or a preset (`Theme.classic`).
[<Sealed>]
type Theme =

    /// The resolved aliases the glyph library reads.
    member internal Palette: Palette

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// A named validation rule beyond the built-in ones, such as FS.GG.Game#795's ΔE2000 distinctness and colour-vision
/// checks: it reads a theme the built-in rules accept and returns what it breaks. Built by `ThemeCheck.create`; added
/// by `Theme.validate`, and run again by `Theme.withOverrides`.
and [<Sealed>] ThemeCheck

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module ThemeCheck =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A check named `name` (a check that throws is reported as a violation of `name`).
    val create: name: string -> run: (Theme -> ThemeViolation list) -> ThemeCheck

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The check's name.
    val name: check: ThemeCheck -> string

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module Theme =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Resolve and validate a theme: run `algorithms` over the seed in order, apply `overrides`, read the aliases, then
    /// check every rule. The theme, or every violation:
    /// - an unknown seed, map or alias name, a missing colour, a colour with alpha below 255 (alpha waits for Babylon and
    ///   the kit, Q5), or a number out of range;
    /// - the WCAG 2.x contrast floor (`contrastTarget`, at least 3:1) against `surface.ground` for every allegiance
    ///   colour, `glyph.unit.body`, `glyph.stale`, `glyph.selection`, `glyph.marker`, `glyph.path`, `cue.focus.outer`,
    ///   `cue.hover` and `status.keyline`;
    /// - a 3:1 legibility floor against `surface.ground` for every team colour, which never touches the ground (ADR
    ///   erratum E7, amendment A1): it is held to 3:1 against what it touches instead, below, whatever `contrastTarget`;
    /// - 3:1 between colours that touch: `cue.focus.inner` on `cue.focus.outer`; `glyph.badge.ink` on the body and on
    ///   every team and allegiance colour; `glyph.icon.ink` on the body and every allegiance colour; every team colour
    ///   on the body (the team ring's layout, ADR erratum E1: from the edge in, the frame in its identity colour, a
    ///   keyline in the body colour that also lines Ally's holes, the ring in the team colour, then the body fill, so
    ///   the ring shares an edge only with the body colour, never with the ground or the identity colour); every
    ///   status fill on `status.track`; `status.track`
    ///   on `status.keyline`; `glyph.modifier` on the body and on every team and allegiance colour.
    val create:
        seed: ThemeSeed -> algorithms: ThemeAlgorithm list -> overrides: ThemeOverride list -> Result<Theme, ThemeViolation list>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The theme with `overrides` applied after its own, validated again by the built-in rules and every check the
    /// theme carries; an override that fails is refused with its reasons. For a game's own adjustments: a player's
    /// colour goes through `withTeamColour`. The floors (`contrastTarget`, `minCssPerUnit`, `hueCapacity.*`) are the
    /// seed's, and an override of one is refused here and in `create`, so no override can lower a floor.
    val withOverrides: overrides: ThemeOverride list -> theme: Theme -> Result<Theme, ThemeViolation list>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A player's team colour (XAG 103): `glyph.team.<team>` set to `colour` and the theme validated again by the
    /// built-in rules and every carried check, or refused with the reasons. Only an existing team's colour can be set;
    /// a player has no path to any other token, and none to a floor.
    val withTeamColour: team: int -> colour: FS.GG.UI.Scene.Color -> theme: Theme -> Result<Theme, ThemeViolation list>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The theme validated again with `checks` added after the checks it already carries (ADR D12: the validation
    /// entry point FS.GG.Game#795's distinctness and colour-vision checks join). The theme it returns carries them, so
    /// `withOverrides` runs them too: `Theme.create seed algorithms overrides |> Result.bind (Theme.validate checks)`.
    val validate: checks: ThemeCheck list -> theme: Theme -> Result<Theme, ThemeViolation list>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The names of the checks the theme carries beyond the built-in rules, in order.
    val checks: theme: Theme -> string list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The `classic` preset: dark, the default (Q10). Its resolved values are baked at build time, and a test compares
    /// them with a fresh derivation from `ThemeSeed.classic`. A provisional stand-in: it is regenerated from
    /// FS.GG.Game#795's derivation, the source of truth for every preset, when that lands.
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
    /// `glyph.team.0` to the last team, `glyph.badge.ink`, `glyph.icon.ink`, `glyph.modifier`, `glyph.selection`,
    /// `glyph.marker`, `glyph.path`, `glyph.stale`, `cue.focus.outer`, `cue.focus.inner`, `cue.hover`, `status.*`
    /// (good, warn, bad, track, keyline), `terrain.*` (open, blocked, difficult, water, void), then the numbers
    /// `size.cell`, `stroke.outline`, `stroke.gap`, `stroke.bracket` and `stroke.hover`. The set is closed: it grows only through the ADR's own review.
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
    /// The theme as a DTCG 2025.10 token file: the seed (group `seed`) and every resolved colour alias (group `alias`),
    /// each colour as `{colorSpace: "oklch", components, hex}`, each seed number as a number, with the theme's name,
    /// algorithm names and number overrides under the `$extensions` key `gg.fs.theme`. Size aliases are not written:
    /// they derive from the seed. The text is the same bytes on .NET and through Fable.
    val toDtcg: theme: Theme -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A DTCG resolver document over `themes`, version `2025-10-01` (the Resolver Module gives 2025-10-01 in its
    /// property table and 2025-11-01 in section 4.1.2; FS.GG.Game#789 and #805 use 2025-10-01). Two or more themes are a
    /// `theme` modifier with one context per theme file (`<name>.tokens.json`, the first by default); one theme is a
    /// set, since a modifier should have two or more contexts. A `density` modifier follows (`regular`, the default,
    /// and `compact`, which sets the seed's `density` to 0.75; `ofDtcg` derives the sizes again from the seed, so the
    /// context changes them).
    val dtcgResolver: themes: Theme list -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Read a DTCG token file written by `toDtcg` (or edited from one). This is an **author** path, for a game or a
    /// designer; a player changes a team colour through `withTeamColour`. Group `seed` becomes the seed, and the colours
    /// of group `alias` become alias overrides, so deleting them derives every colour again from the edited seed. Sizes
    /// always derive again from the seed: a number in the alias group is refused, and a size the theme overrides travels
    /// in the extension's `numberOverrides`. A token's `$type` may be its own or its nearest group's, and a `$value` may
    /// be a whole-value reference such as `"{seed.team.0}"`, followed through the document; a missing target, a cycle,
    /// a type mismatch or a chain of more than 64 links is refused. A JSON-pointer `$ref` is not supported and is
    /// refused. A colour with alpha other than 1 is refused (Q5). A colour with a `hex` is read from the hex, never from
    /// its components. Nesting deeper than 128 levels is refused. The algorithms are looked up by name in `algorithms`.
    /// Export then import gives equal resolved hex for every alias.
    val ofDtcg: algorithms: ThemeAlgorithm list -> json: string -> Result<Theme, ThemeViolation list>

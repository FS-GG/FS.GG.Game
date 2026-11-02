# GAME-RENDER-02.8 — Theme machinery: game-state type to glyph symbology, with derived, selectable themes (ADR)

Status: **proposed**. This is the design PR for FS.GG.Game#795. It adds an experimental proof and no shipped API.
Route: routine. Parent Outcome: FS-GG/.github#5218 (GAME-RENDER-02).

This ADR is a **companion to the glyph ADR**, [`game-render-02-glyphs.md`](game-render-02-glyphs.md) (#789, accepted
by PR #794 at `a4c75efa`). The glyph ADR fixes the slots, the identity frames, the token layers, the algorithm
signature, the alias names, `Theme` as a Scene input, and colour determinism (its D1, D4, D11). Its D12 sketches the
`Symbology<'Entity>` builder and the build-time rules, and its scope split gives #795:
- the encoding grammar;
- the palette algorithms;
- the presets;
- the distinctness, colour-vision, redundancy and capacity checks.

This ADR decides those four, and nothing the glyph ADR already decides.

**Inputs:**
- The prior-art research on #795 (issuecomment-6564559497).
- Root's correction (issuecomment-6564999179): quantise half up, `Floor(x + 0.5)`, not `Floor`.
- Root's #794 merge comment: the icon slot is unavailable while a mandatory badge occupies a 22–39 unit frame, so the
  builder must fail at build time.
- Root's decisions on the glyph ADR's questions (#789): **Q6**, no Scene text in v1; **Q11**, the ΔE thresholds are
  provisional and calibrated here; **Q12**, presets are baked.

**Proof:** `FS.GG.Game.Reference.Experimental` (not packed):
- `ExperimentalColour.fs`: the colour science;
- `ExperimentalThemes.fs`: seeds, algorithms, aliases, validation, presets, DTCG and switching;
- `ExperimentalSymbology.fs`: the grammar, a stand-in `GlyphSpec` and its drawing, and the worked example;
- `ExperimentalPresetData.fs`: the baked presets.

The proof uses #789's type names so that adopting the implementation is a rename. The #789 implementation owner was
asked to confirm them in issuecomment-6566692636.

## Problem

A game today maps its state to pixels by hand: colours chosen per node, no rule that ordered data is not drawn as hue,
no check that two teams stay apart for a colour-blind player, and one palette per game. The glyph ADR gives every game
the same slots and tokens. A game still needs:
- a way to say **what each field of its state means**, and have the library choose the drawing;
- **themes it can switch between** that are guaranteed legible.

## Decisions

### T1. A typed encoding grammar, `Symbology<'Entity>`

A game declares, for one entity type, which field goes on which glyph slot, through a **measure**: the field's level of
organisation, after Bertin, Mackinlay's ranking and Munzner's channels. The theme supplies the ranges, the colours and
the icon set, as Vega-Lite's `config.range` does. The output is a `GlyphSpec`, the glyph ADR's D1 target.

```fsharp
// The worked example (ExperimentalSymbology.fs, module ArenaExample): the template's arena players.
let roster = [ Own; Enemy; Enemy; Enemy ]            // the game's teams and their allegiances
Symbology.create<Player> "unit" 24.0
|> Encode.identity "allegiance" (fun p -> p.Allegiance) Frame          // shape + colour
|> Encode.team     "team"       (fun p -> p.Team) roster TeamMark       // colour + mandatory badge (shared Enemy)
|> Encode.fraction "health"     (fun p -> p.Health) (Amplifier Below)   // bar length
|> Encode.ordinal  "rank"       (fun p -> p.Rank) [ Rookie; Veteran; Elite ] (Amplifier Above)  // pips
|> Encode.flag     "carrying"   (fun p -> p.Carrying) (Amplifier Corner)
|> Encode.label    "name"       (fun p -> p.Name)                       // mirror label only (Q6)
|> Symbology.build themes                                               // Result<Symbology<Player>, SymbologyError list>
```

- **The combinators:** `Encode.identity`, `nominal`, `ordinal`, `fraction`, `angle`, `flag`, `label` and `team`.
  - `nominal` and `ordinal` take the field's closed list of cases or levels, so the domain is known without
    reflection, which Fable does not support.
  - `team` takes the game's **roster**, the allegiance of each team. Whether two teams share an allegiance, which
    makes the badge mandatory, then depends on the game, not on which units are visible (glyph ADR D1).
- **Each encoding names its field.** F# cannot read an accessor's name without quotations, and Fable does not
  support quotations, so the name is an argument (Q-T2).
- **The slots** are the glyph ADR's D1 slots. `Fill` is the frame's hue and `FillStep` its lightness. `SizeStep` is the
  frame size, an area channel. `Label` is the accessible mirror label, because Q6 rules out Scene text.
- **The mirror label is generated** from the encodings, for example `own, team 1, health 90%, rank 3, carrying, Ada`.
  It names identity and team in every case, as glyph ADR D1 requires.
- **Build against the selectable themes.** `Symbology.build themes draft` checks the theme-dependent rules (hue
  capacity, and whether each theme carries the game's team colours) against every theme the player may choose. A
  game therefore cannot ship a theme that breaks its own symbology.
- **The portable proof draws the team mark as the frame's colour**, with the badge inside the frame. That is the
  #789 proof's dense-board drawing. #789 owns the ring and band geometry (Q-T3).

**Measures and slots:**

| Measure | Allowed slots | Refused (typed error) |
|---|---|---|
| identity | `Frame` | anywhere else: `IdentityNotOnFrame` |
| nominal | `Icon` (at most 4 icons in the proof's set); `Fill` with the same field also on `Icon` | `Fill` alone: `CategoricalHueWithoutRedundancy`; more cases than a theme's hue capacity: `OverCapacity` |
| ordinal | `Amplifier Above` (pips, at most 5), `SizeStep` and `FillStep` (at most 4) | `Fill`: `HueOnlyOrderedData` |
| fraction | `Amplifier Below` (a bar) | `SizeStep`: `FractionAsArea`; `Fill`: `HueOnlyOrderedData` |
| angle | `Amplifier Facing`, in the stroked profile | in the portable profile: `NeedsStrokedProfile`; `Fill`: `HueOnlyOrderedData` |
| flag | `Modifier`, `Amplifier Corner` | anywhere else: `FlagNotSelective` |
| label | `Label` | — |
| team | `TeamMark` (at most 9 teams: one digit, never wrapped) | more: `OverCapacity` |

### T2. The build-time rules, as typed errors that say what to change

`Symbology.build` returns **every** broken rule at once. `Symbology.explain` turns each into the change the author
makes. The rules are the glyph ADR's D12, plus the icon-slot rule from Root's #794 merge comment:

| Rule | Error | What `explain` tells the author |
|---|---|---|
| 1. No hue-only ordered data | `HueOnlyOrderedData (field, measure)` | use a bar, pips, `SizeStep` or `FillStep` |
| 2. Categorical hue needs a redundant channel | `CategoricalHueWithoutRedundancy field` | also encode the field on `Slot.Icon` |
| 3. Capacity | `OverCapacity (field, slot, n, capacity, theme)` | group the values, or use the icon; names the theme whose hue capacity is short |
| 4. Fractions are length | `FractionAsArea field` | use `Amplifier Below` |
| 5. Flags are selective | `FlagNotSelective (field, slot)` | use `Modifier` or `Amplifier Corner` |
| 6. Identity double-encoded | `IdentityNotOnFrame`, `IdentityBelowMinimumWithoutLabel` | put identity on `Frame`; below 12 units, add `Encode.label` |
| 6. Team badge mandatory when allegiances are shared | `TeamBadgeBelowMinimumWithoutLabel (field, frame, allegiance)` | frames of at least 22 units, distinct allegiances, or a label |
| Icon slot unavailable | `IconSlotTakenByBadge (icon, team, frame, allegiance)` | frames of at least 40 units, distinct allegiances, or move the field to `SizeStep`, `Modifier` or the label |
| One field per slot | `SlotTaken`, `DuplicateField` | move one field |
| The themes carry the teams | `ThemeRefuses (theme, reason)` | the theme's own violation, explained |

**The icon slot, exactly.** A mandatory badge sits at the inner centre of frames from 22 units (glyph ADR D1). If any
size the symbology can draw falls in **22–39 units**, including through a `SizeStep`, an icon encoding is refused.
From 40 units (`Symbology.sideBySideFrame`, the glyph ADR's Q15 default) the icon sits left and the badge right. The
proof's test places both wholly on frame ink, with a 1-unit margin, in all five frames at 40 units. #789 measures the
threshold per frame (Q15).

The worked example's first draft also put the role on the icon. The build refuses it with exactly one error:

> role is on Slot.Icon, but two or more teams of team share the enemy allegiance, so the mandatory team badge takes
> the inner centre of a 24-unit frame and the icon slot is unavailable from 22 to 39 units. Use frames of at least 40,
> give each team its own allegiance, or move role to Slot.SizeStep (if ordered), Slot.Modifier (if a flag) or the
> label.

**Fallbacks are notes, not errors.** When the game has more teams than a theme's hue capacity, team colour falls back
to allegiance colouring in that theme, and team is read from the badge and the frame (glyph ADR D1). The worked
example's four teams exceed `highContrast` (capacity 3) and `monochrome` (0), and `build` returns those two notes.

### T3. Theme derivation: seed, Leonardo-style ramps, a lean alias set

**Seed** (the `Seed` record) holds the intent:
- `Ground`;
- a key chroma;
- the five allegiance hues and the team hues (OKLCH h);
- the selection, marker and status hues;
- `ContrastTarget`;
- `Cell`, `StrokeBase`, `Gap` and `Frame`.

**Map.** An algorithm list expands the seed into map tokens: parameters, plus one **12-step ramp per hue**.
- **Solving a step.** Each step is solved for a target WCAG 2 contrast ratio against the ground: the colour of that
  hue and chroma, **nearest the ground's lightness**, whose 8-bit contrast meets the target. This is Leonardo's idea
  (Apache-2.0; no code ported).
- **How it is found.** OKLCH lightness is bisected 48 times, and each candidate is evaluated as it is drawn:
  1. CSS Color 4 gamut mapping (binary search on chroma, ΔEOK JND 0.02);
  2. then clamp and `Floor(v × 255 + 0.5)`;
  3. then contrast through the committed 256-entry sRGB→linear table.
- **The step jobs.** The ratios follow Radix: steps 1–2 are ground tints, 3–5 states, 6–8 borders, 9–12 solid fills
  and marks. The dark targets are 1.08 to 11:1; `highContrast` uses 1.2 to 16:1.

**Aliases.** A closed set of **28 alias names** maps into the map. Glyph code reads only these:
- `ground`;
- `glyph.unit.body`, `glyph.badge.ink` and `glyph.icon.ink`;
- `glyph.allegiance.{own,ally,neutral,enemy,unknown}`;
- `glyph.team.0`–`7`;
- `glyph.selection`, `glyph.marker` and `glyph.modifier`;
- `cue.focus.outer`, `cue.focus.inner` and `cue.hover`;
- `status.keyline`, `status.track` and `status.good/warn/bad`.

Five of these are not in the glyph ADR's list: the badge and icon ink, the status track and keyline, and the modifier.
#795 asks #789 to add them.

**Alternating bands.** Team N takes ramp step `TeamSteps[N mod 2]`, which alternates light and dark bands. Teams then
differ in lightness as well as hue, as the Game Accessibility Guidelines advise ("a light/dark split for teams").

### T4. Composable algorithms, ending in a deterministic repair

An algorithm is `{ Name; Apply: Seed -> MapTokens -> MapTokens }` (glyph ADR D4). A list composes, and each algorithm
re-derives the ramps after changing parameters.

| Algorithm | What it does |
|---|---|
| `base` | the map a seed gives; dark polarity if the ground's OKLCH L is under 0.6 |
| `dark` | a dark ground (the seed's, if already dark), near-black ink on fills, ramps lighter than the ground |
| `light` | a near-white ground of the seed's hue, white ink on fills, ramps darker than the ground |
| `highContrast` | a black ground, a 7:1 floor (provisional), steeper ramps, stroke and gap +1, hue ceiling 4/5/6 |
| `colourBlindSafe` | Okabe–Ito hue angles (taken from their published sRGB values) for teams, allegiances, status, selection and marker; hue ceiling 4/5/6 |
| `monochrome` | every hue removed; colour carries no category; hue capacity 0 |
| `repair` | the check-and-repair pass (below) |

**`repair` chooses lightness, and hue only for three cues.**
- **Lightness steps.** Over the solved ramps, it picks the ramp step of every allegiance, status level and team band.
  The choice maximises the smallest distinctness margin (ΔE2000 minus its threshold, under normal vision and every
  simulation), while each step keeps the contrast floor against the ground and the on-fill ink.
- **Hue, for selection, marker and hover only.** These are drawn over fills, so each must stand apart from every fill.
  The seed's hue is tried first, at every step. Only if no step reaches the threshold are the twelve 30° hues tried in
  order (Q-T4).
- **Deterministic.** Candidates are enumerated in a fixed order and the first best wins, so .NET and Fable choose the
  same steps. The smoke checks this byte for byte.
- **Failure is named.** Whatever still fails is reported by validation, naming the pair, and the theme cannot be
  built.

### T5. Validation: contrast on 8-bit values, ΔE2000 under Machado simulation, measured capacity

`Themes.validateFor teams theme` returns every violation, and `Themes.create` refuses a theme that has any.

**Contrast** (WCAG 2, SC 1.4.11, on the 8-bit values through the committed table):
- every alias drawn on the ground meets the floor: teams, allegiances, body, selection, marker, modifier, the outer
  focus tone, hover and the status keyline;
- each touching pair meets it too:
  - the badge and icon ink on every team and allegiance fill;
  - the inner focus tone on the outer tone and on every fill it rings;
  - each status fill on the track.

**Distinctness** applies when colour carries categories, so never in `monochrome`.
- **Which pairs.** It checks, within each category:
  - teams pairwise;
  - allegiances pairwise;
  - status levels pairwise.
- **Thresholds.** Each pair needs CIEDE2000 **≥ 12 with normal vision** and **≥ 8 under Machado protan, deutan and
  tritan simulation at severity 1.0**, simulated in linear RGB.
- **Cues.** Selection, the outer focus tone and hover are checked against every team and allegiance colour (≥ 12,
  normal vision), because they are drawn over them.
- **Lightness.** Under each simulation, the first two teams (the two-team default) differ by **≥ 0.08 in OKLab L**.

**Hue capacity is measured, not declared.**
- `Themes.hueCapacity theme frameUnits` is the largest n, up to the theme's ceiling for that size, whose first n team
  colours pass every distinctness rule. It is 0 in `monochrome`.
- The ceilings are 6, 7 and 8 for frames under 24 units, 24–39, and 40 and over (Munzner's 6–12 bins, fewer at small
  sizes); `highContrast` and `colourBlindSafe` use 4, 5 and 6.
- A theme therefore cannot claim more team hues than it separates.

**Measured on the five presets** (`ExperimentalThemesTests.fs`, "calibration evidence"):

| Preset | Hue capacity (24 units) | Teams: min ΔE normal / simulated | Allegiances: min ΔE normal / simulated | Min contrast on ground |
|---|---|---|---|---|
| `classic` | 5 | 20.3 / 8.5 | 25.4 / 16.2 | 3.20 |
| `light` | 5 | 20.4 / 8.4 | 25.5 / 14.1 | 3.21 |
| `highContrast` | 3 | 35.4 / 8.3 | 27.1 / 12.7 | 7.20 |
| `colourBlindSafe` | 4 | 38.3 / 8.4 | 27.9 / 17.1 | 3.20 |
| `monochrome` | 0 | not checked (shape and badge carry identity and team) | — | 7.56 (one grey) |

In every coloured preset the binding constraint is the simulated threshold (8). It, not normal vision, decides each
preset's hue capacity.

**How the thresholds are calibrated (glyph ADR Q11).** 12, 8 and 0.08 ship as provisional constants
(`Themes.thresholds`), and the PR does not claim they are right. The calibration that replaces them has three steps:
1. **Size.** Stone, Szafir and Setlur's model makes the noticeable difference grow as marks shrink. Compute the glyph's
   visual angle at the theme's `minCssPerUnit` and a stated viewing distance, for the 24-unit default frame and the
   12-unit minimum, and from it a threshold per size step. The size-step ceilings above then become thresholds.
2. **Players.** Run a forced-choice test with players, including at least one of each dichromacy, on the worked
   example in each preset: "same team or different?" for each team pair, at 12, 24 and 40 units. Simulation is
   validation, not a substitute for this.
3. **Commit.** Record the per-size thresholds as data, re-run `repair` and the bake, and let the measured capacities
   change in the same PR.

The table above is the baseline that calibration changes.

### T6. Presets and the player override

| Preset | Recipe | What it is |
|---|---|---|
| `classic` (default, glyph ADR Q10) | `[base; dark; repair]` | the #789 proof's ground (34, 40, 49); team hues Tableau-style, blue then orange (never red against green) |
| `light` | `[base; light; repair]` | FourD's look |
| `highContrast` | `[base; dark; highContrast; repair]` | 7:1, black ground, thicker strokes and gaps |
| `colourBlindSafe` | `[base; dark; colourBlindSafe; repair]` | Okabe–Ito hues, lightness solved for the ground (their blue, 2.86:1 on `classic`'s ground, is lifted) |
| `monochrome` | `[base; dark; monochrome; repair]` | no hue; identity by frame shape, team by badge |

- **Baked (glyph ADR Q12).** `bake.fsx` writes every alias's hex per preset to `ExperimentalPresetData.fs`. A .NET test
  and the browser smoke derive the presets again and must match byte for byte.
  - The bake also writes the DTCG token files, the Resolver document and `presets.css` into
    [`game-render-02-themes/`](game-render-02-themes/).
  - A change to an algorithm regenerates them in the same PR. A test fails if they are stale.
- **The player's team-colour override.** `Themes.overrideTeam teams index colour theme` sets `glyph.team.N` at the
  alias layer, then validates the theme again for the game's team count (XAG 103: presets plus free choice).
  - An override that fails is refused with its reasons. The test refuses a team on the ground's colour (below the
    floor) and a team equal to another (not distinct), and accepts a distinct valid colour.
- **A game adds its own preset** as a seed plus an algorithm list, or as a DTCG file imported onto a preset.

### T7. DTCG 2025.10, CSS, and runtime switching

- **`Themes.toDtcg`** writes every alias as a DTCG colour token:
  - `{"colorSpace": "oklch", "components": [L, C, h], "alpha": 1, "hex": …}`, with groups following the dotted alias
    path;
  - the size tokens as `dimension`s;
  - a `$extensions."gg.fs.theme"` block naming the algorithms and the floor.
- **`Themes.ofDtcg base text`** imports a token file onto a base theme as alias overrides, taking `hex` (glyph ADR
  D11). It validates the result, refuses unknown aliases by name, and refuses failing colours with their violations.
  Export followed by import gives equal resolved hex for every alias of every preset, on .NET and under Fable.
- **`Themes.resolverDocument`** writes a DTCG Resolver document with a `theme` modifier whose contexts are the preset
  token files, `default: classic` (Q-T9).
- **`Themes.cssVariables`** writes one `[data-theme=…]` block per theme. The smoke checks that every colour the worked
  example draws is one of its preset's CSS variables.
- **Runtime switching.** `Themes.switchOf theme` returns `{ Read: unit -> Theme; Select: Theme -> unit }`.
  - The game's view takes the reader, `view : (unit -> Theme) -> ArenaState -> Scene`, and the glyph ADR's cue layer
    takes the same reader (D10).
  - The theme is never in the game state. A switch keeps the state, the selection and the focus, and the next Present
    draws the new theme.
  - Classic → every other preset → classic gives classic's decided pixels again, on .NET and on all three backends
    (below).

### T8. Kit checks for themes and symbology

These go to the kit's glyph section (glyph ADR D6) when #789's types land. The proof runs each one now.

1. **Determinism.** Fable derives every preset alias's hex exactly as .NET baked it, and the full 8-bit OKLCH round
   trip has 0 failures under both runtimes' maths. Windows .NET is #789's D11 check.
2. **Theme validation**, for every preset and override: contrast, distinctness, the lightness gap and opacity, as in T5.
3. **The symbology rules** (T2), under .NET and Fable, which must give the same typed errors.
4. **Pixels per preset per backend.** Every pixel `ScenePixels` decides for the worked example matches on SVG, Pixi and
   Babylon. A theme-swap control (classic drawn, light read) must not match.
5. **Switch round trip.** On one mount, through the reader, with one game state: classic → each preset → classic.
6. **Monochrome redundancy.** In `monochrome`, a glyph and the same glyph of another team that shares its allegiance
   (badge), or of another identity (frame), differ in drawn decided pixels on every backend.
7. **Exports.** DTCG round trip, and the CSS variables equal the Scene's colours.

### T9. Licensing

No third-party code is ported. The proof implements published formulas and uses published constants, each cited in
the source:
- the OKLab matrices (Ottosson 2020; CSS Color 4);
- CSS Color 4 gamut mapping;
- the WCAG 2 relative luminance;
- CIEDE2000 (Sharma, Wu and Dalal 2005), tested against their published pairs;
- the Machado 2009 severity-1.0 matrices;
- the Okabe–Ito sRGB values.

The ideas taken are layering and composition (Ant Design, MIT), solving for target contrast (Leonardo, Apache-2.0),
and step jobs (Radix, MIT). ColorBrewer, material-color-utilities and culori were consulted for approach only. If an
implementation slice ports code from any of them, it keeps the NOTICE and licence header, as the glyph ADR's licensing
table requires. **No `apca-w3` code or constants**; WCAG 2 is the gate.

## The proof (experimental)

The proof does not constrain the API.

- **.NET tests** (`tests/Game.Render.Tests/ExperimentalThemesTests.fs`): **23**, all passing.
  - **Colour science:**
    - OKLab against published values;
    - the committed table against the transfer function;
    - the full 16,777,216-colour OKLCH round trip;
    - CIEDE2000 against six Sharma pairs;
    - Machado invariants;
    - gamut mapping and contrast solving.
  - **Themes:**
    - the five presets validate, with measured capacities 5/5/3/4/0;
    - the calibration table;
    - the baked data is current;
    - DTCG round trip and refusals;
    - CSS;
    - the player override;
    - runtime switching;
    - monochrome redundancy.
  - **Grammar:**
    - each D12 rule and the icon-slot rule (22, 24, 32 and 39 refused; 40 and unshared accepted; a size step
      reaching into the band refused);
    - slot conflicts;
    - the stroked-profile gate;
    - the 40-unit side-by-side layout;
    - the worked example and its mirror labels.
- **Quantisation, measured:**
  - with `Floor(v + 0.5)`, **0** of 16,777,216 colours fail the OKLCH round trip;
  - with `Floor`, **16,776,961** fail (100.0 %) in this implementation. The #794 review measured 94.8 % in its
    implementation; which colours fail depends on the direction of the last-ULP error, but `Floor` is unusable either
    way.
- **Browser smoke** (`runThemeProof` in `tests/Render.Browser.Smoke/Program.fs`) adds **29 checks**, and
  `MIN_CHECKS` goes from 511 to **541**. Main already runs 512, so the old floor was one short.
  - **Under Fable,** in the browser:
    - the five presets derive to exactly the 140 alias hex values .NET baked;
    - 636,056 sampled colours (every third value per channel) round-trip through OKLCH with half up. The full
      16,777,216 run in .NET, because under the browser's maths they took most of the smoke's 180 s budget;
    - the grammar accepts the worked example and refuses the role icon with the same typed error as .NET;
    - DTCG round-trips every preset;
    - every colour drawn is one of the preset's CSS variables.
  - **On SVG, Pixi and Babylon each:**
    - the worked example draws all **736** decided pixels in each of the five presets;
    - the classic-against-light swap control does not match;
    - classic → each preset → classic, through the reader with one state, shows each preset and then classic again;
    - in `monochrome`, a team-2 enemy differs in drawn decided pixels from the team-3 and team-4 enemies (badges) and
      from the own frame.
  - **Runs.** Both runs used Chrome for Testing 153.0.8010.12 (Playwright chromium-1243) through `fdev-chromium-env`,
    and the capacity probe answered `admit` before each. The software run used SwiftShader. The GPU run used the fdev GPU (ANGLE over Vulkan, RADV).
    Each run passed **541** checks, all 29 of the #795 checks among them.
  - **Cost.** The whole #795 section took about 11 s of the software run: 0.5 s to derive five presets under Fable and
    4.2 s for the sampled round trip.
  - **Screenshots** are in [`game-render-02-themes/`](game-render-02-themes/), named `themes-<preset>-<backend>.png`
    (GPU run), plus `themes-<preset>-babylon-software.png`.
    - Babylon shows about 0.75 of each colour until #786, as in the glyph proof.
    - The SVG screenshots also catch the SVG input host's "No object selected" status line, which the renderer does
      not draw.

## Open questions, with recommended defaults

None blocks the implementation.

- **Q-T1. The thresholds** (ΔE 12 normal, 8 simulated, OKLab ΔL 0.08 for the first two teams).
  - *Default:* ship them provisional and calibrate as T5 states.
  - *Trade-off:* until then, a pair that passes may still be hard to tell apart at 12 units. Pairs at the floor are
    the ones to test.
- **Q-T2. Field names as strings.**
  - *Default:* each `Encode` takes the field's name, used in errors and labels.
  - *Alternative:* typed field keys generated from the record, which would need a source generator. Quotations are
    not Fable-safe.
  - *Trade-off:* a misspelt name makes an error message wrong, never a drawing.
- **Q-T3. The team mark's drawing.**
  - *Default:* the portable proof colours the whole frame with the team colour and puts the badge inside it, as #789's
    dense board does. #789 decides the ring or band geometry. The grammar only needs `TeamMark` to exist and `Fill` to
    conflict with it.
  - *Trade-off:* with team on the frame colour, identity's colour is the team's, and identity rests on shape.
- **Q-T4. `repair` may change the hue of selection, marker and hover.**
  - *Default:* yes, only those three, and only when the seed's hue fails at every step.
    - In `highContrast`, selection moves to hue 0 (rose), because yellow cannot stand 12 apart from the unknown
      allegiance at 7:1.
    - Hover then lands on the same hue one step lighter. Selection and hover are not required to differ, because one
      is brackets and the other an outline.
  - *Alternative:* fail the theme and let its author choose.
- **Q-T5. Building against the selectable themes.**
  - *Default:* `Symbology.build themes`. A DTCG file or a custom seed imported at run time is checked with
    `validateFor` for the game's team count before it can be selected. A symbology that fails against it refuses the
    theme rather than drawing it.
  - *Trade-off:* a game that lets players load arbitrary themes must handle that refusal in its UI.
- **Q-T6. Team colour beyond capacity.**
  - *Default:* fall back to allegiance colouring (glyph ADR D1). This is a note, not an error, because the badge and the
    frame still carry team.
  - *Alternative:* fail the build when the roster exceeds the smallest selectable capacity. That would forbid
    `monochrome` for every multi-team game.
- **Q-T7. Hue ceilings per size** (6/7/8, and 4/5/6 for `highContrast` and `colourBlindSafe`).
  - *Default:* keep them as the cap on the measured capacity until calibration (Q-T1) gives per-size thresholds.
- **Q-T8. Where the grammar lives.**
  - *Default:* `FS.GG.Game.Render.Glyphs`, as `Symbology` and `Encode` modules beside `GlyphSpec`, in the Fable view.
    `Themes` algorithms and presets live beside #789's `Theme`.
- **Q-T9. The DTCG Resolver version string.**
  - The 2025.10 Resolver report gives both `2025-10-01` and `2025-11-01` for `version`. The proof writes `2025-10-01`.
  - *Default:* follow whichever the report's errata settle on, and keep the token files themselves version-free.
- **Q-T10. More than 9 teams.**
  - *Default:* refused (`OverCapacity`, 9) until #789 ships the two-digit badge. Team numbers are never wrapped (Root's
    #794 merge comment).

## Implementation plan (after acceptance)

1. **#789's implementation**: slots, `GlyphSpec`, `Theme`, the alias set including the five #795 names, and D11.
2. **#795 on top of it, in FS.GG.Game.Render:**
   - `Symbology` and `Encode`;
   - the colour module;
   - the algorithms and `repair`;
   - the presets as baked data;
   - `validateFor`, `hueCapacity`, `overrideTeam`, DTCG and CSS;
   - the theme switch;
   - the kit checks of T8, in the kit's glyph section.

   The experimental modules here then go away.
3. **Template adoption** (FS.GG.Templates, `fs-gg-fable-game`): the arena's players and features as symbologies, the
   worked example in the template docs, and the preset picker. One game is migrated (RuneScape, glyph ADR D9).

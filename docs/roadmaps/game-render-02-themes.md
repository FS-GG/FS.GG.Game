# GAME-RENDER-02.8 — Theme machinery: game-state type to glyph symbology, with derived, selectable themes (ADR)

Status: **proposed**. This is the design PR for FS.GG.Game#795. It adds an experimental proof and no shipped API.
Route: routine. Parent Outcome: FS-GG/.github#5218 (GAME-RENDER-02).

This ADR is a **companion to the glyph ADR**, [`game-render-02-glyphs.md`](game-render-02-glyphs.md) (#789, accepted
by PR #794 at `a4c75efa`, with errata E1–E5 from its implementation). The glyph ADR fixes the slots, the identity
frames, the token layers, the algorithm signature, the alias names, `Theme` as a Scene input, and colour determinism
(its D1, D4, D11). Its D12 sketches the `Symbology<'Entity>` builder and the build-time rules. Its scope split gives
#795:
- the encoding grammar;
- the palette algorithms;
- the presets;
- the distinctness, colour-vision, redundancy and capacity checks.

## Ownership, as Root ruled on #789

| #802 (the #789 implementation) owns | #795 supplies |
|---|---|
| `Theme`, `ThemeSeed`, `ThemeTokens`, `ThemeAlgorithm`, `ThemeOverride`, `ThemeViolation`, `ThemeCheck` | `ThemeAlgorithm` values: the palette algorithms and `repair` (T3, T4) |
| `Theme.create`, `Theme.validate [ThemeCheck]` and the built-in rules | `ThemeCheck` values: ΔE2000, colour vision, the first-two-team lightness gap, status meaning, capacity (T5) |
| `withOverrides` and `withTeamColour` | the five preset **seeds** and algorithm lists (T6) |
| DTCG and CSS export and import, `dtcgResolver` | the encoding grammar, `Symbology<'Entity>` and `Encode` (T1, T2) |
| `ThemeColour`: OKLCH, gamut mapping, half-up quantisation, the sRGB table, contrast, hex | CIELAB, CIEDE2000, Machado simulation and Leonardo-style solving, which `ThemeColour` does not ship |

**Root's ruling on the presets:** #795's derivation is the **source of truth for every preset, `classic` included**.
#802's baked `classic` (`src/Game.Render/ThemeBaked.fs`, merged at `8f9ced1`) is a stand-in, regenerated from #795's
derivation when #795's implementation lands.
- **Today** the two differ in 27 of the 35 colour aliases. For example, `glyph.team.1` is `#b15900` here and `#e56c03` in
  #802.
- **At the review** (`9de9044e`), #802's `classic` set each team colour equal to an allegiance colour (`#eb5046`, the
  enemy's). The #802 review flagged it, because the ring vanished at 1.00:1 before erratum E1's keyline.

**Inputs:**
- The prior-art research on #795 (issuecomment-6564559497).
- Root's correction (issuecomment-6564999179): quantise half up, `Floor(x + 0.5)`, not `Floor`.
- Root's #794 merge comment: the icon slot is unavailable while a mandatory badge occupies a 22–39 unit frame, so the
  builder must fail at build time.
- Root's decisions on the glyph ADR's questions (#789): **Q6**, no Scene text in v1; **Q11**, the ΔE thresholds are
  provisional and calibrated here; **Q12**, presets are baked.
- The #789 owner's answer on names (#789 issuecomment-6567349935), #802's `Theme.fsi` (`9de9044e`, merged at
  `8f9ced1`), part 3's `Glyphs.fsi` (`a567a25`), and the glyph ADR's errata E1 (the team ring's layout) to E5.
- The independent review of #805 at `3ddb9ee7` (REQUEST_CHANGES), answered below.

**Proof:** `FS.GG.Game.Reference.Experimental`, which is not packed.
- #802 (`8f9ced1`), #789's part 3 (#806, `a567a25`), #807 and #808 merged to main while this revision was under way, and
  this branch merges them. #808 retired the #789 proof's `ExperimentalGlyphs`, so the proof now uses the shipped library
  for identity, glyph specs and drawing.
- The **drawing** is the library's. `Symbology.glyph` builds a `FS.GG.Game.Render.GlyphSpec` through its builders, and
  `Glyphs.scene` draws it with erratum E1's layout, in a #802 `Theme`.
- `Themes.toRenderTheme` builds that #802 `Theme` from this proof's derived colours:
  - it starts from `ThemeSeed.classic`, sets all 35 colour aliases by `ThemeOverride.colour`, and sets `hueCapacity.N` to
    the measured capacities;
  - #802's built-in rules accept all five presets;
  - until amendment A1 is decided (Q-T11), the bridge passes `contrastTarget` 3, and the proof's own `builtInRules`
    validate `highContrast`'s 7:1.
- The **theme-side** types remain stand-ins, each marked in its source as replaced by #802. The swap is the first step of
  #795's implementation PR, not this design PR.

| Proof stand-in | Replaced by |
|---|---|
| `Colour.oklchOf`, `ofOklch`, `gamutMap`, `quantise`, `srgbToLinear`, `contrast`, `hex`, `ofHex` | `ThemeColour.toOklch`, `ofOklch`, `quantise`, `linear`, `contrast`, `hex`, `ofHex` |
| `Themes.Seed` | `ThemeSeed` plus algorithm parameters closed over by `ThemeAlgorithm.create` |
| `Themes.MapTokens`, `Themes.Algorithm` | `ThemeTokens`, `ThemeAlgorithm` |
| `Themes.Theme`, `create`, `builtInRules`, `ThemeViolation` | `Theme`, `Theme.create … \|> Result.bind (Theme.validate checks)`, #802's built-in rules, `ThemeViolation.create` |
| `Themes.overrideTeam` | `Theme.withTeamColour` |
| `Themes.hexes`, `cssVariables`, `toDtcg`, `ofDtcg`, `resolverDocument` | `Theme.hex`, `cssVariables`, `toDtcg`, `ofDtcg`, `dtcgResolver` |
| `Themes.toRenderTheme` (the bridge to the library) | nothing: #795's presets are built on #802 directly |

Already the library's, not stand-ins:
- `Identity`;
- `GlyphSpec` (`team`, `badged`, `allegianceColouring`, `sizeStep`, `fillStep`, `modifier`, `amplifier`);
- `Glyph.ofSpec`, `Glyphs.draw` and `Glyphs.scene`;
- the frame constants `minBadgeFrame` (22), `minIdentityFrame` (12) and `minIconAndBadgeFrame` (40);
- the icon set: infantry, armour, artillery, support and structure.

The proof's alias set **is** #802's: the 35 colour aliases of `Theme.aliases`, from `surface.ground` to
`terrain.void`. Its built-in rules are #802's, with one stated deviation, amendment A1 (T5, Q-T11).

## Problem

A game today maps its state to pixels by hand. It chooses colours per node, has no rule against drawing ordered data as
hue, has no check that two teams stay apart for a colour-blind player, and keeps one palette. The glyph ADR gives every
game the same slots and tokens. A game still needs:
- a way to say **what each field of its state means**, and have the library choose the drawing;
- **themes it can switch between** that are guaranteed legible.

## Decisions

### T1. A typed encoding grammar, `Symbology<'Entity>`

A game declares, for one entity type, which field goes on which glyph slot, through a **measure**: the field's level of
organisation, after Bertin, Mackinlay's ranking and Munzner's channels. The theme supplies the ranges, the colours and
the icon set, as Vega-Lite's `config.range` does. The output is a `GlyphSpec`, the glyph ADR's D1 target.

```fsharp
// The worked example (ExperimentalSymbology.fs, module ArenaExample): the template's arena players.
let roster = [ Own; Enemy; Enemy; Enemy ]            // the game's teams and their allegiances (Identity.Own … on #802)
Symbology.create<Player> "unit" 24.0
|> Encode.identity "allegiance" (fun p -> p.Allegiance) Frame          // shape + colour
|> Encode.team     "team"       (fun p -> p.Team) roster TeamMark       // ring + mandatory badge (shared Enemy)
|> Encode.fraction "health"     (fun p -> p.Health) (Amplifier Below)   // bar length
|> Encode.ordinal  "rank"       (fun p -> p.Rank) [ Rookie; Veteran; Elite ] (Amplifier Above)  // pips
|> Encode.flag     "carrying"   (fun p -> p.Carrying) Modifier            // a corner notch
|> Encode.label    "name"       (fun p -> p.Name)                       // mirror label only (Q6)
|> Symbology.build themes                                               // Result<Symbology<Player>, SymbologyError list>
// per entity: Symbology.spec players anchor player : Result<Encoded, SymbologyError list>
//             Symbology.glyph teamColours selected encoded : FS.GG.Game.Render.Glyph
```

- **The combinators:** `Encode.identity`, `nominal`, `ordinal`, `fraction`, `angle`, `flag`, `label` and `team`.
  - `nominal` and `ordinal` take the field's closed list of cases or levels, so the domain is known without
    reflection, which Fable does not support.
  - `team` takes the game's **roster**, the allegiance of each team. Whether two teams share an allegiance, which
    makes the badge mandatory, then depends on the game, not on which units are visible (glyph ADR D1).
  - **`team` requires `identity` on `Frame`**, because team is double-encoded with the allegiance frame (D12 rule 6).
- **Build time and run time.**
  - `Symbology.build` checks the declarations.
  - `Symbology.spec` checks each entity's values. A case, level, fraction or team outside its declared domain is a
    typed `ValueOutOfRange`, never case 0, and team numbers never wrap. An entity whose identity is not its team's
    allegiance in the roster is a typed `RosterMismatch`.
- **What the grammar trusts (the #805 review's N5).** Fable cannot read an accessor's name or body, so the grammar
  trusts the author's declarations:
  - the measure (`nominal` or `ordinal`);
  - the field name: two encodings with one name are one field, so rule 2's redundancy is checked by name;
  - ordered data declared `nominal` is not caught.

  What it does check is every value against its declaration at run time, and every identity against the roster.
- **The slots** are the glyph ADR's D1 slots:
  - `Fill` is the frame interior's hue, drawn as the ring when there is no team.
  - `FillStep` is its lightness, drawn by the library's `GlyphSpec.fillStep`.
  - `SizeStep` is the frame size, an area channel. The levels map to the library's size steps with the top level at step
    3, the default frame. At a 24-unit default, three levels draw 18, 21 and 24 units (`floor (frame / 0.75 x (0.5 +
    step / 12))`).
  - Flags on `Modifier` are the library's corner notches. The design also accepts `Amplifier Corner`; the proof refuses
    it (`SlotNotInProof`), because a corner badge needs an icon per flag.
  - `Label` is the accessible mirror label, because Q6 rules out Scene text.
- **The mirror label is generated** from the encodings, for example `own, team 1, health 90%, rank 3, carrying, Ada`.
  It names identity and team in every case, as glyph ADR D1 requires.
- **Build against the selectable themes.**
  - `Symbology.build themes draft` checks the theme-dependent rules (hue capacity at the **smallest** frame the
    symbology can draw, and whether each theme carries the game's teams) against every theme the player may choose.
  - Whether a scene shows team colours, or falls back to allegiance colouring, is decided **once per scene and theme**
    (`Symbology.teamColouring`), never per glyph. A size step therefore never mixes the two.
- **The library draws erratum E1's layout.** From the edge in:
  - the identity frame, in its identity colour;
  - a keyline in the body colour;
  - the ring in the team colour, or in the categorical `Fill` hue;
  - the body fill;
  - the badge or icon on top.

  The library decides the badge per glyph from its own frame size, after any size step. Below 22 units it omits the
  badge and reports a diagnostic.

**Measures and slots** (the allowed slots are what `WrongSlot` names):

| Measure | Allowed slots | Refused (typed error) |
|---|---|---|
| identity | `Frame` | anywhere else: `IdentityNotOnFrame` |
| nominal | `Icon` (at most 5, the library's icon set); `Fill` with the same field also on `Icon` | `Fill` alone: `CategoricalHueWithoutRedundancy`; more cases than a theme's hue capacity: `OverCapacity` |
| ordinal | `Amplifier Above` (pips, at most 5), `SizeStep` and `FillStep` (at most 4) | `Fill`: `HueOnlyOrderedData` |
| fraction | `Amplifier Below` (a bar) | `SizeStep`: `FractionAsArea`; `Fill`: `HueOnlyOrderedData` |
| angle | `Amplifier Facing`, in the stroked profile | in the portable profile: `NeedsStrokedProfile`; `Fill`: `HueOnlyOrderedData` |
| flag | `Modifier` (corner notches); `Amplifier Corner` in the design, not drawn by the proof | anywhere else: `FlagNotSelective` |
| label | `Label` | — |
| team | `TeamMark` (at most 9 teams: one digit, never wrapped; two digits from part 3's 33 units) | more: `OverCapacity`; without identity on `Frame`: `TeamWithoutIdentity` |

### T2. The build-time rules, as typed errors that say what to change

`Symbology.build` returns **every** broken rule at once, de-duplicated. `Symbology.explain` turns each into the change
the author makes. The rules are the glyph ADR's D12, plus the icon-slot rule from Root's #794 merge comment:

| Rule | Error | What `explain` tells the author |
|---|---|---|
| 1. No hue-only ordered data | `HueOnlyOrderedData (field, measure)` | use a bar, pips or `SizeStep` |
| 2. Categorical hue needs a redundant channel | `CategoricalHueWithoutRedundancy field` | also encode the field on `Slot.Icon` |
| 3. Capacity | `OverCapacity (field, slot, n, capacity, theme)` | group the values, or use the icon; names the theme whose hue capacity is short |
| 4. Fractions are length | `FractionAsArea field` | use `Amplifier Below` |
| 5. Flags are selective | `FlagNotSelective (field, slot)` | use `Modifier` or `Amplifier Corner` |
| 6. Identity double-encoded | `IdentityNotOnFrame`, `IdentityBelowMinimumWithoutLabel` | put identity on `Frame`; below 12 units, add `Encode.label` |
| 6. Team double-encoded | `TeamWithoutIdentity field` | add `Encode.identity` on `Frame` |
| 6. Team badge mandatory when allegiances are shared | `TeamBadgeBelowMinimumWithoutLabel (field, frame, allegiance)` | frames of at least 22 units, distinct allegiances, or a label |
| Icon slot unavailable | `IconSlotTakenByBadge (icon, team, frame, allegiance)` | frames of at least 40 units, distinct allegiances, or move the field to `SizeStep`, `Modifier` or the label |
| One field per slot | `SlotTaken`, `DuplicateField` | move one field |
| The slot exists | `WrongSlot (field, measure, slot, allowed)` | names the allowed slots |
| The proof draws it | `SlotNotInProof (field, slot)` | a corner flag needs an icon; use `Modifier` |
| The themes carry the teams | `ThemeRefuses (theme, reason)` | the theme's own violation, explained |
| Run time (`spec`) | `ValueOutOfRange (field, value, domain)`, `RosterMismatch (team field, team, roster allegiance, entity identity)` | add the value to the declaration, or fix the state or the roster |

**The icon slot, exactly.** A mandatory badge sits at the inner centre of frames from 22 units (glyph ADR D1). If any
size the symbology can draw falls in **22–39 units**, including through a `SizeStep`, an icon encoding is refused.
From 40 units (part 3's `minIconAndBadgeFrame`, the glyph ADR's Q15) the icon sits left and the badge right. Part 3's
`Glyphs.iconSlotAvailable` agrees, and its tests check every identity from 40 units.

The worked example's first draft also put the role on the icon. The build refuses it with exactly one error:

> role is on Slot.Icon, but two or more teams of team share the enemy allegiance, so the mandatory team badge takes
> the inner centre of a 24-unit frame and the icon slot is unavailable from 22 to 39 units. Use frames of at least 40,
> give each team its own allegiance, or move role to Slot.SizeStep (if ordered), Slot.Modifier (if a flag) or the
> label.

**Fallbacks are notes, not errors.** When the game has more teams than a theme's hue capacity at its smallest frame,
team colour falls back to allegiance colouring in that theme, and team is read from the badge and the frame (glyph
ADR D1). On #789's part 3 this is `GlyphSpec.allegianceColouring`. The worked example's four teams exceed
`colourBlindSafe` (capacity 3) and `monochrome` (0), and `build` returns those two notes.

### T3. Theme derivation as `ThemeAlgorithm`s on #802's types

#795 defines no theme type. Its algorithms are `ThemeAlgorithm.create name (fun seed tokens -> …)`. Each reads
`ThemeSeed` colours and numbers, and writes `ThemeTokens` through `ThemeTokens.withColour` and `withNumber`. All colour
arithmetic goes through `ThemeColour`. #795 adds only what `ThemeColour` lacks:
- CIELAB and CIEDE2000;
- Machado simulation;
- `solve`, the Leonardo-style contrast solver below.

**Parameters live in the algorithm, not the seed.** #802 keeps `ThemeSeed` closed, so these are closed over by the
algorithm:
- chroma;
- the twelve step ratios;
- the team-band candidates.

DTCG export records algorithm **names** only, so each configuration that must round-trip gets its own name, for
example `fsgg.leonardo.dark`.

**Ramps.** One 12-step ramp per hue. Each step is the colour of that hue and chroma, **nearest the ground's lightness**,
whose 8-bit contrast against the ground meets the step's target ratio. This is Leonardo's idea (Apache-2.0; no code
ported).
- **How a step is found.** OKLCH lightness is bisected 48 times, each candidate evaluated as it is drawn:
  1. CSS Color 4 gamut mapping (`ThemeColour.ofOklch`);
  2. half-up quantisation;
  3. contrast through the committed table.
- **The step jobs** follow Radix: steps 1–2 are ground tints, 3–5 states, 6–8 borders, 9–12 solid fills and marks. The
  dark targets are 1.08 to 11:1; `highContrast` uses 1.2 to 16:1.

**Team bands, inside E1's window.** Team N is solved for the contrast ratio `TeamBands[N mod 2]` against the ground,
alternating a light and a dark band (the Game Accessibility Guidelines' light/dark split for teams). Under erratum E1
the ring sits on the body, so each team colour must lie in a window:
- at least 3:1 against the body (#802's rule);
- at least 3:1 against the ground (amendment A1, T5).

On `classic`'s ground (34, 40, 49) with its white body, that window is ground ratios 3.0 to 4.95. `repair` picks the
two bands from candidates between 3.02 and 6.6.

**The tokens #795 writes.** These are #802's 35 colour aliases:
- `glyph.unit.body` and `status.keyline` are the light pole (white) on a dark ground and the dark pole on a light one,
  as #802's standard algorithm derives them;
- the badge ink, icon ink, `glyph.modifier`, `status.track` and `cue.focus.inner` are the opposite pole;
- allegiances, status levels, selection, marker (and `glyph.path`), hover and stale are ramp steps;
- the terrain kinds are low-chroma ground tints;
- `cue.focus.outer` is the ramp step that contrasts most with the ground (erratum E2).

### T4. Composable algorithms, ending in a deterministic `repair`

| Algorithm | What it does |
|---|---|
| `base` | after `ThemeAlgorithm.standard`: the ramps and team bands a seed gives, dark polarity if the ground's OKLCH L is under 0.6 |
| `dark` | a dark ground (the seed's, if already dark), a white body, near-black ink on fills |
| `light` | a near-white ground of the seed's hue, a near-black body, white ink on fills |
| `highContrast` | a black ground, a 7:1 `contrastTarget` (provisional), steeper ramps, stroke and gap +1, hue ceiling 4/5/6 |
| `colourBlindSafe` | Okabe–Ito hue angles (from their published sRGB values) for teams, allegiances, status, selection and marker; hue ceiling 4/5/6 |
| `monochrome` | every hue removed; colour carries no category; hue capacity 0 |
| `repair` | the deterministic check-and-repair pass (below) |

**`repair` chooses lightness, and hue only for three cues.**
- **Lightness.** Over the solved ramps it picks:
  - each allegiance's step;
  - each status level's step;
  - the two team bands;
  - the selection, marker and hover steps.
- **The objective.** Maximise the smallest distinctness margin (ΔE2000 minus its threshold, under normal vision and
  every simulation), while every choice keeps #802's built-in contrast rules (with A1) and status meaning.
- **Status meaning (the #805 review's N12).** Bad stays red (h −20° to 49°), warn stays amber (50° to 110°), good stays
  green (120° to 200°). Each needs chroma ≥ 0.08, so it is not a pastel or a grey. Warn's OKLab L must be at least 0.05
  above bad's, because amber reads lighter than red.
- **Hue, for selection, marker and hover only.** These are drawn over fills. The seed's hue is tried first, from the
  strongest step down. Only if no step reaches ΔE 12 against every fill are the twelve 30° hues tried, in order (Q-T4).
- **Deterministic.** Candidates are enumerated in a fixed order and the first best wins. The smoke checks that .NET and
  Fable choose the same values, byte for byte.

### T5. Validation: #802's built-in rules, then #795's `ThemeCheck`s

A preset is
`Theme.create seed algorithms [] |> Result.bind (Theme.validate (Checks.forTeams teams))`.
The theme carries its checks, so `withTeamColour` and `withOverrides` run them again.

**#802's built-in rules.** The proof's `builtInRules` is a stand-in for these:
- **Ground floor.** `contrastTarget` against the ground for allegiances, body, stale, selection, marker, path, the outer
  focus tone, hover and `status.keyline`.
- **Touching pairs, at 3:1:**
  - the inner focus tone on the outer one;
  - the badge ink and the modifier on the body and on every team and allegiance colour;
  - the icon ink on the body and every allegiance colour;
  - every team colour on the body;
  - every status fill on the track;
  - the track on the keyline.
- **Tokens:** opaque, and only known aliases.

**Amendment A1, proposed to #802 (Q-T11).** #802 holds every team colour to `contrastTarget` against the ground. Under
E1 the ring never touches the ground: it sits between a body-colour keyline and the body fill.
- With a 7:1 floor, a black ground and a white body, a team colour would need luminance ≥ 0.30 (7:1 on the ground) and
  ≤ 0.30 (3:1 on the body). Only 0.30 exactly satisfies both, so `highContrast` cannot have two distinct teams.
- A1 holds team colours to **3:1** against the ground, like every other touching pair. At a 3:1 floor it is #802's rule
  unchanged, so `classic`, `light`, `colourBlindSafe` and `monochrome` pass #802 as it stands. `highContrast` passes
  only with A1.

**#795's checks** (`Themes.checks teams`, each a future `ThemeCheck`):

| Check | Rule |
|---|---|
| `distinct.deltaE2000` | the game's teams, allegiances and status levels pairwise: CIEDE2000 **≥ 12** with normal vision, **≥ 8** under Machado protan, deutan and tritan at severity 1.0, simulated in linear RGB |
| `distinct.cues` | selection, the outer focus tone and hover against every team and allegiance colour, ≥ 12 with normal vision |
| `lightness.firstTwoTeams` | the first two teams differ by ≥ 0.08 in OKLab L under every simulation (D12 as amended, below) |
| `status.meaning` | the hue bands, chroma and amber-over-red rule of T4 |
| `cue.innerOnFills` | the inner focus tone at 3:1 on every team and allegiance colour it rings |
| `capacity.declared` (on #802) | a seed's `hueCapacity.N` must not exceed the capacity measured below |

`monochrome` skips the colour checks, because colour carries no category there.

**Amendment to glyph ADR D12 (Root's ruling on the #805 review, B3).**
- **The first two teams.** The OKLab lightness gap (≥ 0.08 under every simulation) is required for the first two teams,
  the two-team default.
- **Teams beyond the first two** carry identity through the **allegiance frame plus the mandatory team badge**.
  Same-band pairs (teams 0 and 2, and so on) may therefore share lightness. They are still held to the ΔE2000 rules.
- **Below the 22-unit badge minimum,** same-allegiance teams beyond the first two rely on hue and the mirror label.
  That is the trade-off #794 already accepted.

The measured capacities below stand on this amendment.

**Hue capacity is measured, not declared.**
- The proof's `hueCapacity theme frameUnits` is the largest n, up to the theme's ceiling for that size, whose first n
  team colours pass every distinctness rule. It is 0 in `monochrome`.
- The ceilings are 6, 7 and 8 for frames under 24 units, 24–39, and 40 and over; `highContrast` and `colourBlindSafe`
  use 4, 5 and 6.
- On #802 the preset seeds set `hueCapacity.0` to `hueCapacity.3` to these measured values. `capacity.declared` refuses
  a seed that declares more than it separates. #802 has four size steps and the proof three buckets (Q-T13).

**Measured on the five presets.** These are the values `ExperimentalThemesTests.fs` asserts, row for row ("calibration
evidence"):

| Preset | Hue capacity (24 units) | Teams: min ΔE normal / simulated | Allegiances: min ΔE normal / simulated | Min allegiance contrast on ground |
|---|---|---|---|---|
| `classic` | 5 | 20.0 / 8.4 | 25.4 / 16.2 | 3.20 |
| `light` | 5 | 20.3 / 8.4 | 25.5 / 14.1 | 3.21 |
| `highContrast` | 5 (4 under 24 units) | 19.9 / 8.3 | 24.3 / 11.1 | 7.21 |
| `colourBlindSafe` | 3 | 37.8 / 8.0 | 27.9 / 17.1 | 3.22 |
| `monochrome` | 0 | not checked (shape and badge carry identity and team) | — | 7.56 (one grey) |

In every coloured preset the binding constraint is the simulated threshold (8). It, not normal vision, decides each
preset's hue capacity.

**How the thresholds are calibrated (glyph ADR Q11).** 12, 8 and 0.08 ship as provisional constants
(`Themes.thresholds`), and this ADR does not claim they are right. The calibration that replaces them has three steps:
1. **Size.** Stone, Szafir and Setlur's model makes the noticeable difference grow as marks shrink. Compute the glyph's
   visual angle at the theme's `minCssPerUnit` and a stated viewing distance, for the 24-unit default frame and the
   12-unit minimum, and from it a threshold per size step.
2. **Players.** Run a forced-choice test with players, including at least one of each dichromacy, on the worked
   example in each preset: "same team or different?" for each team pair, at 12, 24 and 40 units. Simulation is
   validation, not a substitute for this.
3. **Commit.** Record the per-size thresholds as data, re-run `repair` and the bake, and let the measured capacities
   change in the same PR.

### T6. Presets and the player override

| Preset | Recipe (after `ThemeAlgorithm.standard`) | What it is |
|---|---|---|
| `classic` (default, glyph ADR Q10) | `[base; dark; repair]` | the #789 proof's ground (34, 40, 49); team hues Tableau-style, blue then orange (never red against green) |
| `light` | `[base; light; repair]` | FourD's look |
| `highContrast` | `[base; dark; highContrast; repair]` | 7:1 (provisional), black ground, thicker strokes and gaps; needs A1 |
| `colourBlindSafe` | `[base; dark; colourBlindSafe; repair]` | Okabe–Ito hues, lightness solved for the ground |
| `monochrome` | `[base; dark; monochrome; repair]` | no hue; identity by frame shape, team by badge |

- **Baked (glyph ADR Q12).** `bake.fsx` writes all 35 colour aliases per preset to `ExperimentalPresetData.fs`. A .NET
  test and the browser smoke derive them again and must match byte for byte.
  - The bake also writes the DTCG token files, the Resolver document and `presets.css` into
    [`game-render-02-themes/`](game-render-02-themes/), and a test fails if they are stale.
  - When #795's implementation lands, this baked data replaces #802's `ThemeBaked.fs` for `classic` (Root's ruling)
    and adds the other four.
- **The player's team-colour override** is `Theme.withTeamColour team colour theme` (glyph ADR erratum E4).
  - Only one of the game's teams can be set: the proof refuses a team outside the game (`TeamOutsideGame`).
  - The theme is validated again by the built-in rules and every carried check, for the game's team count (XAG 103:
    presets plus free choice).
  - A failing override is refused with its reasons. The test refuses a team on the ground's colour (below the floor),
    a team equal to another (not distinct), and a team outside the game, and accepts a valid colour.
- **A game adds its own preset** as a seed plus an algorithm list, or as a DTCG file through `Theme.ofDtcg`.

### T7. DTCG, CSS and runtime switching

- **DTCG and CSS belong to #802:** `Theme.toDtcg`, `ofDtcg`, `dtcgResolver` (version `2025-10-01`, erratum E5) and
  `cssVariables`. #795 adds no second export path. The proof's stand-ins write the same shapes so the round trip can be
  tested before #802 merges:
  - colour tokens as `{"colorSpace": "oklch", "components": [L, C, h], "alpha": 1, "hex": …}`;
  - CSS properties named `--fsgg-<alias with hyphens>`.
- **What #795 needs of an import.**
  - A colour with alpha below 1 is refused (glyph ADR Q5).
  - A colour is read from its hex (D11). The proof refuses a colour token without a hex, or with a malformed one,
    rather than dropping it.
  - #802 reads the components when there is no hex (Q-T12).
- **Runtime switching is #795's.**
  - `Themes.switchOf theme` returns `{ Read: unit -> Theme; Select: Theme -> unit }`.
  - The game's view takes the reader, `view : (unit -> Theme) -> ArenaState -> Scene`, and the glyph ADR's cue layer
    takes the same reader (D10).
  - The theme is never game state. A switch keeps the state, the selection and the focus, and the next Present draws
    the new theme.
  - Classic → every other preset → classic gives classic's decided pixels again, on .NET and on all three backends.

### T8. Kit checks for themes and symbology

These go to the kit's glyph section (glyph ADR D6) when #789's types land. The proof runs each one now.
1. **Determinism.**
   - Fable derives every preset alias's hex exactly as .NET baked it.
   - An OKLCH grid of 157,560 colours gives the same digest under .NET and Fable.
   - The full 8-bit OKLCH round trip has 0 failures under both runtimes' maths.
   - Windows .NET is #789's D11 check.
2. **Theme validation**, for every preset and override: the built-in rules and #795's checks, as in T5.
3. **The symbology rules** (T2), under .NET and Fable, which must give the same typed errors.
4. **Pixels per preset per backend.** Every pixel `ScenePixels` decides for the worked example matches on SVG, Pixi and
   Babylon. A theme-swap control (classic drawn, light read) must not match.
5. **Switch round trip.** On one mount, through the reader, with one game state: classic → each preset → classic.
6. **Monochrome redundancy.** Each case first waits until its own drawing shows all its decided pixels (a positive
   control). Then a glyph must differ in drawn decided pixels from:
   - the same glyph of another team that shares its allegiance (badge);
   - another identity, with and without a team (frame and badge, and frame only).
7. **Exports.** DTCG round trip, and the CSS variables equal the Scene's colours.

### T9. Licensing

No third-party code is ported. The proof implements published formulas and uses published constants, each cited in
the source:
- **OKLab:** Ottosson's direct linear-sRGB matrices (2020). CSS Color 4 defines OKLab through XYZ D65 with its own
  matrices, and the two paths differ by at most 1.2e-4 in an OKLab component over the 8-bit colours (measured by the
  #805 review). Hue, the polar form and gamut mapping follow CSS Color 4.
- **Contrast:** the WCAG 2 relative luminance.
- **CIEDE2000:** Sharma, Wu and Dalal (2005), tested against their published pairs.
- **Colour vision:** the Machado 2009 severity-1.0 matrices.
- **Palette:** the Okabe–Ito sRGB values.

The ideas taken are layering and composition (Ant Design, MIT), solving for target contrast (Leonardo, Apache-2.0),
and step jobs (Radix, MIT). ColorBrewer, material-color-utilities and culori were consulted for approach only. If an
implementation slice ports code from any of them, it keeps the NOTICE and licence header, as the glyph ADR's licensing
table requires. **No `apca-w3` code or constants**; WCAG 2 is the gate.

## Determinism, measured

- **Quantisation.**
  - With `Floor(v + 0.5)`, **0** of 16,777,216 colours fail the OKLCH round trip, under .NET and under the browser's
    maths.
  - With `Floor`, **16,776,961** fail (100.0 %) in this implementation. The #794 review measured 94.8 % in its own;
    which colours fail depends on the direction of the last-ULP error, but `Floor` is unusable either way.
- **Cube roots.** The #805 review measured the cube-root inputs of every 8-bit colour:
  - about **58 %** differ between .NET's `Math.Cbrt` and Node's `Math.cbrt`, by 1–3 ULP;
  - every output byte is still identical, because half-up quantisation absorbs the difference.

  Fable has no mapping for `System.Math.Cbrt`, so the proof emits `Math.cbrt` under Fable. The smoke keeps two
  byte-identity checks: the full 8-bit domain, and the OKLCH grid digest.

## The proof (experimental)

The proof does not constrain the API.

- **.NET tests** (`tests/Game.Render.Tests/ExperimentalThemesTests.fs`): **28**, all passing.
  - **Colour science:**
    - OKLab against published values;
    - the committed table;
    - the full OKLCH round trip;
    - CIEDE2000 against six Sharma pairs;
    - Machado invariants;
    - gamut mapping and solving.
  - **Themes:**
    - the five presets validate, with measured capacities 5/5/5/3/0 at 24 units;
    - the calibration table, asserted row for row;
    - the baked data and the grid digest are current;
    - DTCG round trip, and the refusal of an unknown alias, alpha, a missing hex and a malformed hex;
    - CSS;
    - the player override, including a team outside the game;
    - icon ink and status meaning;
    - runtime switching;
    - monochrome redundancy.
  - **Grammar:**
    - each D12 rule and the icon-slot rule (22, 24, 32 and 39 refused; 40 and unshared accepted; a size step reaching
      into the band refused);
    - the review's two B2 drafts: team without identity, refused; and a roster that disagrees with an entity, a typed
      `RosterMismatch`;
    - out-of-range values, never-wrapping teams, `FillStep` accepted, a corner flag refused, and the badge per size step:
      the library omits it at 18 and 21 units and draws it at 24;
    - capacity at the smallest frame, and team colouring per scene;
    - `WrongSlot` naming the allowed slots;
    - the worked example and its mirror labels.
- **Browser smoke** (`runThemeProof` in `tests/Render.Browser.Smoke/Program.fs`) adds **30 checks**, and `MIN_CHECKS`
  goes from main's 578 to **608**.
  - **Under Fable,** in the browser:
    - the five presets derive to exactly the 175 alias hex values .NET baked;
    - all 16,777,216 colours round-trip, in a plain loop outside the async block (3.6 s in the software run);
    - the OKLCH grid digest equals .NET's;
    - the grammar refuses the role icon with the same typed error as .NET;
    - DTCG round-trips every preset;
    - every colour drawn is one of the preset's CSS variables.
  - **On SVG, Pixi and Babylon each:**
    - the worked example draws all **1236** decided pixels in each of the five presets;
    - the swap control does not match;
    - classic → each preset → classic shows each preset and then classic again;
    - five monochrome cases, each with its positive control, differ in drawn decided pixels: two badge pairs, a frame
      and badge pair, and two frame-only pairs.
  - **Runs.** Both runs used Chrome for Testing 153.0.8010.12 (Playwright chromium-1243) through `fdev-chromium-env`,
    and the capacity probe answered `admit` before each.
    - The software run used SwiftShader, and the GPU run the fdev GPU (ANGLE over Vulkan, RADV).
    - Merged with main, each passed **635** checks, all 30 of the #795 checks among them.
    - The whole #795 section took about 7 s of the software run, 3.6 s of it the full round trip.
  - **Screenshots** are in [`game-render-02-themes/`](game-render-02-themes/), named `themes-<preset>-<backend>.png`
    (GPU run), plus `themes-<preset>-babylon-software.png`.
    - Babylon shows about 0.75 of each colour until #786, as in the glyph proof.
    - The SVG screenshots also catch the SVG input host's "No object selected" status line, which the renderer does
      not draw.

## Open questions, with recommended defaults

None blocks the implementation.

- **Q-T1. The thresholds** (ΔE 12 normal, 8 simulated, OKLab ΔL 0.08 for the first two teams).
  - *Default:* ship them provisional and calibrate as T5 states.
  - *Trade-off:* until then, a pair that passes may still be hard to tell apart at 12 units. `colourBlindSafe`'s teams
    sit at 8.0 simulated, at the floor.
- **Q-T2. Field names and measures are trusted.**
  - *Default:* each `Encode` takes the field's name, and the grammar trusts it and the declared measure (N5).
    Consequences:
    - two encodings with the same name and different accessors pass rule 2 as one field;
    - ordered data declared `nominal` is not caught;
    - an out-of-range value is caught at run time by `spec`.
  - *Alternative:* typed field keys from a source generator (quotations are not Fable-safe), which would let the grammar
    see the accessor.
- **Q-T3. The team mark's geometry.** Settled by glyph ADR erratum E1: frame, keyline, ring, body. The library draws it.
- **Q-T4. `repair` may move only the selection, marker and hover hue.**
  - *Default:* yes, only those three, and only when the seed's hue fails at every step. In `highContrast` the yellow
    selection cannot stand 12 apart from the unknown allegiance at 7:1, so it moves to hue 0.
  - *Alternative:* fail the theme and let its author choose.
- **Q-T5. Building against the selectable themes.**
  - *Default:* a theme imported at run time is validated for the game's team count before it can be selected, and a
    symbology that fails against it refuses the theme.
  - *Trade-off:* a game that lets players load arbitrary themes handles that refusal in its UI.
- **Q-T6. Team colour beyond capacity.**
  - *Default:* fall back to allegiance colouring, decided per scene, as a note.
  - *Alternative:* an error, which would forbid `monochrome` for every multi-team game.
- **Q-T7. Hue ceilings per size** (6/7/8, and 4/5/6 for `highContrast` and `colourBlindSafe`).
  - *Default:* keep them as a cap on the measured capacity until calibration gives per-size thresholds.
- **Q-T8. Where #795's code lives.**
  - *Default:* `Symbology` and `Encode` in `FS.GG.Game.Render`, beside part 3's `GlyphSpec`, in the Fable view.
  - #795's `ThemeAlgorithm` and `ThemeCheck` values, and the preset seeds, live beside #802's `Theme`, in a
    `ThemePresets` module, with the baked data replacing `ThemeBaked.fs`.
- **Q-T9. The DTCG Resolver version.** Settled by erratum E5: `2025-10-01`.
- **Q-T10. More than 9 teams.**
  - *Default:* refused in the proof. On #789's part 3, two-digit badges from 33 units (`minTwoDigitBadgeFrame`); team
    numbers never wrap.
- **Q-T11. Amendment A1, the team floor against the ground** (T5).
  - *Default:* hold team colours to 3:1 against the ground and at least 3:1 on the body, since under E1 the ring never
    touches the ground. #802's built-in rule changes for `contrastTarget` above 3.
  - *Alternative:* keep #802's rule, and `highContrast` has no team colours (capacity 1, allegiance colouring
    throughout).
  - Raised with the #789 owner on #789.
- **Q-T12. A DTCG colour without a hex.**
  - *Default:* #802 refuses it, as the proof does. Reading components instead runs OKLCH maths at import time, which D11
    keeps on the derivation side.
  - *Alternative:* #802's current behaviour, quantising the components through `ThemeColour`.
- **Q-T13. Capacity size steps.** #802 has four `hueCapacity` size steps; the proof measures three frame buckets.
  - *Default:* map the seed's `hueCapacity.N` to the bucket of size step N's smallest frame, and measure per step when
    #795's implementation lands.

## Implementation plan (after acceptance)

1. **#802 and #789's part 3, merged** (`8f9ced1`, `a567a25`; the #789 proof retired by #808): `Theme` and its API, the
   alias set, D11, `GlyphSpec`, the drawing (E1) and the named frame constants. Amendment A1 and Q-T12 remain to be
   decided with the #789 owner.
2. **#795 on top of them, in FS.GG.Game.Render:**
   - `Symbology` and `Encode`, with `spec`'s run-time checks;
   - CIELAB, CIEDE2000, Machado and `solve`, beside `ThemeColour`;
   - the `ThemeAlgorithm` values and `repair`;
   - the `ThemeCheck` values, with `capacity.declared`;
   - the five preset seeds and their baked data, regenerating `ThemeBaked.fs`;
   - the theme reader;
   - the kit checks of T8, in the kit's glyph section.

   The experimental modules here then go away.
3. **Template adoption** (FS.GG.Templates, `fs-gg-fable-game`): the arena's players and features as symbologies, the
   worked example in the template docs, and the preset picker. One game is migrated (RuneScape, glyph ADR D9).

# GAME-RENDER-ADOPT-01.2 — Register a ready SVG adapter and a retained-scene SVG adapter

Status: source slice delivered in the routine change for FS.GG.Game#750. The held remainder is publication through
the canonical release route, as for GAME-RENDER-ADOPT-01.1.
Route: routine. Parent Outcome: FS-GG/.github#5179. Builds on `game-render-adopt-01.md` (the authoring kit, #745).
Found by the consumers' adoption: FourD (`GAME-RENDER-ADOPT-01.4`, gap 2) and BAR (`GAME-RENDER-ADOPT-01.8`, gap 3).

## Goal

A game that already owns a retained SVG surface registers it as the default `Svg` adapter without a refusing
placeholder. The surface keeps its selectable objects, accessible labels, camera and input host, and the conformance
kit holds it like any other adapter.

## The gap

`RendererRegistry.svg` and `SceneSurface` take `view: 'State -> Scene` and a `Scene -> ...` mount, and the retained
`SvgSurface` wraps the whole `Scene` as one non-selectable object. FourD and BAR draw a `RetainedScene` (a layer
per panel, one selectable object per cell or unit). They had to pass a refusing placeholder to `RendererRegistry.svg`
and `register` their own adapter over it at once.

## Delivered

### `FS.GG.Game.Render` (additive)

- `RendererRegistry.create : RendererAdapter<'State> -> Result<RendererRegistry<'State>, string>`: a registry whose
  default is the ready adapter. `Error` (nothing built) for an adapter that is not `Svg`, because only SVG can be
  the default. `register` adds the canvas adapters as before.
- `RendererConformance.run` builds its instrumented copy of the registry from the instrumented default adapter with
  `create`, so the kit has no placeholder of its own and a registry built from a ready adapter is held to every
  check.

### Browser host (`src/Render.Browser`, not packed)

`RetainedSvgSurface` is the retained-scene SVG adapter for a state that projects onto a `RetainedScene`. It is an
opaque type built by `create container label size scene` and adjusted by `withSelection selected onSelect` and
`withInput attach`; `adapter` and `registry` produce the ready adapter and its registry.

| Concern | Rule |
|---|---|
| Revision | The adapter assigns the next `RetainedScene.Revision` (the first is 1), whatever the game's scene says. A presented scene is never rejected as stale. |
| Camera | The user's pan and zoom survive each present: the mounted host's camera replaces the projected scene's. |
| Selection | After each scene the surface's selection follows `selected state`. An unknown or non-selectable id leaves it unchanged. A selection the user makes is reported once through `onSelect`; synchronisation from `Mount` or `Present` is never reported, so a game may dispatch from `onSelect`. |
| Input | `withInput` attaches the game's input host at each mount and releases it with the mount, before the SVG host. A refusal fails the mount and disposes the SVG host just mounted. |
| Throwing game code | A `selected` that throws while the mount synchronises its first selection fails the mount: the input host and then the SVG host are disposed and the mount is an `Error`, so a retry cannot accumulate roots. |
| Viewport | The SVG host's viewport is fixed at mount. `size` is read at `Mount`; a `Present` whose size differs raises. |

## Evidence

- `tests/Game.Render.Tests/RendererRegistryTests.fs` (6 tests): `create` registers a ready adapter and the host
  mounts it with the state itself; `create` refuses Pixi and Babylon and builds nothing; other adapters register on
  top; a created registry passes the kit alone (13 checks) and with registered Pixi and Babylon adapters; two
  negative controls (an adapter that ignores `Present`, an adapter that never mounts) fail the sections that guard
  them. `Game.Render.Tests` has 74 tests.
- `tests/Render.Browser.Smoke/RetainedSurface.fs` runs in the real browser: 35 checks (it was 320 in all, now 355).
  The retained board has four objects, three selectable with labels, a camera and an input host. It checks the
  registration, identities, labels, input attach and release, selection from the state, the camera surviving a
  present, adapter-owned revisions, a user selection reported once, silent clearing, an unknown id, refusal of an
  undeclared backend, disposal, a refused input host, a `selected` that throws at mount (both hosts released, the mount is an `Error`, and five retries leave no SVG root or input host behind), and the kit's 13 checks on the registry.
- Mutations (see the PR body): `create` accepting any backend fails the refusal test; losing the camera on present
  and reporting synchronised selections as the user's each fail the smoke check that guards them.

## Compatibility

Additions only: one function in `FS.GG.Game.Render` (the member baseline gains one line) and one new module in the
unpacked browser host. No signature changed.

## Held remainder

- Publication of `FS.GG.Game.Render` and the browser host, so FourD and BAR can delete their pinned copies.
- The consumers' adoption of `create` and `RetainedSvgSurface` (FourD#56 follow-up, BAR #5180 follow-up).
- Viewport change and a failing `Present`: GAME-RENDER-ADOPT-01.3 (FS.GG.Game#752).

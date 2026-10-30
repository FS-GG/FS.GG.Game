# GAME-RENDER-MULTI-01.4 — Elmish renderer selection in Game.Render

Status: Elmish switching core and SVG default delivered in the routine change for FS.GG.Game#730. Pixi
and Babylon adapters and the three-backend browser test remain open, waiting on the bindings.
Route: routine. Parent Outcome: FS-GG/.github#4930.

## Delivered in this slice

`FS.GG.Game.Render` gains `RendererSelection.fsi` with an additive public surface. The surface
baselines are refreshed deliberately, and existing `Adapter` and `DefaultKeymap` members are
unchanged.

- `RendererBackend` (`Svg | Pixi | Babylon`) with stable tokens and `defaultBackend = Svg`.
- `RendererAdapter<'State>` is the extension point: a backend id and a `Mount` returning
  `RendererMount<'State>` (`Present`, `Dispose`) or an error. `RendererAdapter.svg` wraps a
  state-to-Scene view and a host Scene surface.
- `RendererRegistry<'State>` is opaque and keyed by the state type parameter. It is constructible
  only through `RendererRegistry.svg`, so the default always has an adapter. `register`, `tryFind`,
  `isAvailable` and `available` complete it.
- `RendererModel<'State>`, `RendererMsg<'State>` (`SelectBackend`, `StateChanged`, `MountFailed`) and
  `RendererEffect<'State>` (`Mount`, `Present`, `Unmount`), with pure `RendererSelection.init` and
  `RendererSelection.update`.
- `RendererHost<'State>` is the effect interpreter. It allows at most one mounted backend, disposes
  each mount exactly once (including on a `Mount` with no prior `Unmount`), reports adapter errors
  and exceptions as `MountFailed`, and becomes inert after disposal.

The package takes no new dependency. Elmish wiring stays with the host, which maps effects to a
command, so this core needs no `Fable.Elmish` package edge.

Evidence: `tests/Game.Render.Tests/RendererSelectionTests.fs` (15 tests) covers registry
availability, state-preserving switches, refusal of unavailable backends, exactly-once disposal,
fallback after a mount failure, stale failures, and SVG output identical to the existing `Adapter`
Scene. The existing Adapter, keymap and portal presentation tests are unchanged.

## Remaining for #730

- Pixi and Babylon `RendererAdapter` values for at least one shared game state. These need the
  FS.GG.Templates#808 (PixiJS) and #809 (Babylon) bindings.
- A browser test proving that all three backends render and switch. This needs a Fable-consumable view
  of `FS.GG.Game.Render`, which is not packaged today, or a host-side composition that owns it.

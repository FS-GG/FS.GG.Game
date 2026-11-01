# GAME-RENDER-ADOPT-01.3 — Viewport-change and failing-Present semantics in the adapter lifecycle

Status: source slice delivered in the routine change for FS.GG.Game#752. The held remainder is publication through
the canonical release route, as for GAME-RENDER-ADOPT-01.1.
Route: routine. Parent Outcome: FS-GG/.github#5179. Builds on `game-render-adopt-01.md` (the kit, #745) and
`game-render-adopt-02.md` (the ready and retained-scene adapters, #750).
Found by the consumers' adoption: FourD (`GAME-RENDER-ADOPT-01.4`, gaps 4 and 5; FourD#56's review found the blank map)
and BAR.

## The gaps

1. `Present: 'State -> unit` cannot express a changed viewport and the SVG host's viewport is fixed at mount, so each
   game remounted inside its own `Present`.
2. A throwing `Present` on the DEFAULT backend released the mount and left `Status = Failed` with nothing mounted,
   and `StateChanged` was then ignored: a blank page with no way back but re-selecting SVG. The pre-adoption surface
   propagated the exception.

## Decisions

### Failing `Present`: contained, with a defined recovery

The alternative was to propagate. Propagation cannot be made safe in the Elmish model (`update` is pure and the host
contains every adapter exception), so the exception is contained and the recovery is defined:

| Event | Result |
|---|---|
| `Present` of a canvas backend throws | The host releases that mount and reports `PresentFailed`. The model mounts SVG with the latest state (as before); `Refusal` names the cause. |
| `Present` of the default backend throws | The host releases the mount and reports `PresentFailed`. The model mounts the default again with the latest state under a new attempt; `Refusal` names the cause. One live mount remains. |
| That remount fails too | `Status = Failed` with the cause, nothing mounted or held. |
| `StateChanged` while `Failed` | The selected backend is mounted again with that state. A failure is never final; before, the state was stored and nothing happened. |

The retry is bounded by the game's own state flow: one mount attempt per state, never a loop inside `update`. A game
whose mount fails for every state attempts it once per state; it can stop by not dispatching states.

`RendererMsg.PresentFailed of backend * attempt * reason` is a new union case (additive). The host used to report a
failing `Present` as `MountFailed`; the model needs to tell them apart, because a mount that failed after being
accepted is remounted while a mount that never started is not. A stale `PresentFailed` (another attempt, or a backend
that is not selected) is ignored.

### Viewport: the remount rule, declared on the registry

`RendererRegistry.withViewport backend viewport` (with `tryViewport`) declares that `backend`'s mount is fixed to
`viewport state`, `(width, height)` in pixels. In `RendererSelection.update`, when the selected backend is active and
the viewport of the new state differs from the previous state's, the model emits `Unmount` then `Mount` under a new
attempt instead of `Present`. The remount is not a failure: `Refusal` is untouched. A `NaN` component equals another
`NaN`, so an invalid viewport cannot remount on every state.

Why not a resize hook: `RendererMount` is a public record, so a `Resize` field would break every adapter. The rule
needs no adapter change, composes with the failure recovery, and a game whose viewport comes from the window keeps it in
its state (the Elmish way). An adapter that resizes itself in place declares nothing and is checked by reading its
viewport back. In place resizing of Pixi and Babylon (cheaper than a new context) is a candidate if a game measures a
need. A remount starts from the scene's own camera and attaches the input host again.

`RetainedSvgSurface.registry` (#750) declares its `size` this way. Without the declaration the stale size reaches
`Present`, which raises, and the failure recovery above mounts the surface again: it still works, with a recorded
refusal.

## The kit enforces both

New sections in `RendererConformance`:

- **present failure**, for every backend, with no subject setup: the kit wraps the adapter and makes its next
  `Present` throw (the adapter's code is not edited). Canvas backend: falls back to SVG with the latest state, the
  failed mount is disposed once and released, SVG shows the state. Default backend: remounted with the latest state,
  one live mount remains, the cause is in `Refusal`; then the remount fails too (the kit also fails the next `Mount`):
  `Failed` with the cause, nothing mounted or held; the next state mounts and shows. 3 checks per canvas backend,
  5 for SVG.
- **viewport**, when the subject calls `RendererConformance.withViewport expected actual` (`BrowserConformance.svgViewport`
  reads the SVG root): at least two distinct viewports among the states; every state, then back again, is presented at
  its own viewport as read from the backend; one live mount remains and every superseded one was disposed once; and,
  where the registry declares the viewport, the mount is replaced exactly when it changes. A fixed-viewport adapter
  with no declaration fails the read-back, which is the check that finds the FourD stale-viewport bug.

The browser kit returns 73 results for three backends (it was 62), the in-process kit 76 (it was 65).

## Evidence

- `Game.Render.Tests`: 88 tests (was 74). Selection: default and canvas `PresentFailed`, a remount that also fails and
  the next state recovering, stale `PresentFailed`, a state change while `Failed`, the viewport rule (same viewport
  presents in place, a change remounts, per backend, `NaN`, `register` keeps the declaration). Kit: the present
  failure section for each backend, a canvas that leaks a failed mount, a host with the pre-adoption behaviour; the
  viewport lifecycle: declared and in-place conform; undeclared, wrongly declared and single-viewport subjects fail.
- Browser smoke: 391 checks (was 352): the kit's present failure on the real SVG, Pixi and Babylon (reference game
  and generated sample), and a retained board whose size changes (declared: remount at the new size without a
  failure, input host reattached, in place for the same size; undeclared: contained failure that mounts again), with
  the kit's viewport section.
- Mutations: see the PR body.

## Compatibility

Signatures: additions only (`RendererMsg.PresentFailed`, `RendererRegistry.withViewport` and `tryViewport`,
`RendererConformance.withViewport`, `BrowserConformance.svgViewport`). Behaviour: a throwing `Present` is reported
as `PresentFailed` rather than `MountFailed`, and a state change while `Failed` mounts again. A consumer that matches
`RendererMsg` exhaustively gets an incomplete-match warning, and one that observed `Failed` after a throwing default
`Present` now sees `Active` with `Refusal` set. Under SemVer a minor change, to be set by the release route.

## Held remainder

- Publication, and FourD and BAR deleting their remount-inside-`Present` and pinned copies once published.
- In-place resize for the canvas adapters, if a game needs it.
- The Scene path (`SceneSurface`) has no viewport: its surfaces are created at one size by `BrowserRenderers`.

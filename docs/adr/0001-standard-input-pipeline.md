# FS.GG.Game ADR 0001: Standard input pipeline

- **Status:** Accepted on merge. Delivery FS.GG.Game#787 (GAME-RENDER-02.1), Outcome FS-GG/.github#5218.
- **Date:** 2026-11-01
- **Affects:** FS.GG.Game (`FS.GG.Game.Render`, `FS.GG.Game.Render.Browser`). Consumers that migrate: FS.GG.Templates
  (`fs-gg-fable-game`), FS.GG.SC2.Client, FSBarV2, FS.GG.RuneScape.
- **Cites:** organization [ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md)
  (Game.Render is the render edge) and
  [ADR-0028](https://github.com/FS-GG/.github/blob/main/docs/adr/0028-keyboard-input-config-mechanism-policy-boundary.md)
  (keyboard mechanism in Rendering, policy in Game). Neither boundary moves: the whole contract is owned by FS.GG.Game,
  and the authority each game talks to stays the game's.
- **Implementation:** FS.GG.Game#788. This ADR ships the typed contract only; every function with behaviour raises
  until #788 implements it.

## Context

GAME-RENDER-01 (FS-GG/.github#5179) made selection work the same on SVG, Pixi and Babylon (`SceneInteraction`,
`SelectionRoute`, the kit's `interaction` section). It stops at "select this object". The three products that use it
each wire the rest by hand, in three different ways.

| Product | Today's wiring | What it had to build itself |
|---|---|---|
| **SC2.Client** (`docs/GAME-RENDER-ADOPT-01.7.md`, `Tactical.fs` at `c1e67cd`) | A canvas press or key becomes `SelectionRequest.Select tag`, which the page answers with `activateUnit`. That encodes the input for a Rust WASM guest, which answers later with the new selection and possibly a command. | Source and modifiers, read from the last DOM event in the capture phase, because `SelectionRequest` has neither. A serial guest queue, a guest epoch that drops late replies after a reset, "poisoned" admission after a fault, an input-sequence fence that discards stale results, command ids namespaced by authority generation, one pending command at a time, and a fresh observation required after each command. No guest deadline. The kit's `interaction` section is not declared, because it assumes the library's SVG host and a synchronous `select` (#772). |
| **Template `fs-gg-fable-game`** (`Client/Stage.fs`, `Client/App.fs` at FS.GG.Templates `858b624`, #842) | `Select id` becomes `CellClicked`, which sends an `InputMessage` with a per-page `Sequence` straight to the SignalR authority. `Clear` becomes `SelectionCleared`. | No guest. No acknowledgement: a send failure is ignored. A predicted `PreviewPath` that the next snapshot clears, unmarked. A snapshot older than the shown tick is dropped. Clicking the selected cell again sends nothing, because `SelectionRequest` reports only changes. Focus moves with the arrow keys, but nothing draws it (#787 review comment N4). The server admits inputs with `NetworkAdmission`. |
| **FSBarV2** (`docs/GAME-RENDER-ADOPT-01.9.md` at `f59c886`) | Selection works on all three backends. The live path is not connected. The old preview client (`runtime.js`) runs a WASM guest through `FS.GG.Wasm.Browser`'s supervisor. | A 64-unit guest preview bound kept separate from the render limit (more units render whole but are "not guest-previewable"). A serial guest queue that disarms on overflow. Escape disarms the guest. It builds the Pixi surface by hand to route late failures, because `LateFailureRoute` cannot be bound without `RendererDriver` (#768). |

**FS.GG.RuneScape** (`docs/FRONTEND.md`, #13, merged 2026-11-01; Delivery #20) adds four needs. It needs secondary
activation for a context menu. It needs repeated activation, to walk to the same tile twice. It needs a mirror that
does not create 225 Tab stops for a 15 x 15 grid (#784). It also distinguishes a dispatched command from a proven
effect: a walk is proven by arrival, and unproven after 200 ticks. Delivery #20 routes human commands through an
arbiter with submit, hold, cancel and release.

`FS.GG.Game.Core` already holds the authority half: `SessionInput` (session, input id, sequence, value) and
`NetworkAdmission.admit`, which orders inputs and refuses a duplicate or stale per-client sequence.
`FS.GG.Wasm.Browser` already runs a guest under a deadline and reports `TerminalState` (`Succeeded`, `GuestRejected`,
`Faulted`, `TimedOut`, `Invalidated`, `Frozen`). What is missing is the client contract that joins them.

## Decision

Every game's input runs through one pipeline of three stages, typed in `FS.GG.Game.Render`.

```text
 SVG / Pixi / Babylon input host                 game                         authority (server)
 ───────────────────────────────                 ────                         ──────────────────
 pointer, keyboard, mirror ──InputEvent──▶ InputRoute ──▶ InputPipeline ──Send──▶ admit by CommandId
       ▲ draws focus cue                                    │ stage 2: guest                 │
       │ (InteractionCues)                                  │   (F#, or WASM bytes)          │
       └──────────────── Displayed (authoritative ◀── Authoritative / Acknowledged ◀────────┘
                                   + marked prediction)
```

The reducer is pure Elmish, like `RendererSelection`: `InputPipeline.init` and `update` return `PipelineEffect`s, and
`PipelineHost` performs them for a page that is not an Elmish program. A game that interprets the effects itself
accepts that a new effect case is a minor-version addition.

### 1. Input events (stage 1)

`InputEvent` (opaque) carries:

- **`source`**: `Pointer`, `Keyboard` or `Mirror` (pen and touch arrive as `Pointer`);
- **`action`**: one of
  - `Activate`: primary pointer press, Enter or Space on the focused object, or a mirror button;
  - `ActivateSecondary`: secondary press, the ContextMenu key or Shift+F10; the browser menu is suppressed on the
    surface;
  - `Hover`: reported only when the hovered object changes;
  - `Focus`: arrow keys, or Tab into the mirror;
  - `Cancel`: Escape;
- **`target`**: the object id, or none for a press on empty ground. The ground press keeps its `point` in scene
  coordinates, for SC2's ground click and BAR's ground target;
- **`modifiers`**: Shift, Control, Alt and Meta, all four always;
- **`repeat`**: true for an operating-system key repeat.

Every activation is reported, including a second activation of the selected object. Every backend reports the same
event for the same input, so a game never reads DOM events itself. Input hosts report to an `InputRoute`, created
before the game loop and bound later, like `SelectionRoute`.

**Focus and hover are drawable state.** The input host keeps `InteractionCues`: the focused object, the hovered object
and whether focus is visible. Focus is visible after keyboard or mirror navigation, and not after a pointer press. The
cues are not game state. They are not saved, and they do not survive a backend switch, but the selection does. Every
interactive backend draws the interaction's cue layer above the frame's Scene. The default is
`SceneInteraction.standardCues`: a two-tone ring of filled rectangles just outside the focused object's bounds, a dark
outer band and a light inner band, each at least 2 scene units. Canvas backends draw filled shapes only (#773), and
the two tones keep at least 3:1 contrast on any background (WCAG 2.2 focus appearance). A game replaces the cue layer
with `SceneInteraction.withCues`. The game also hears `Focus` and `Hover` events, so it can mirror them in its own state
if it wants to.

**Compatibility.** `SelectionRequest`, `SelectionRoute` and `SceneRenderers.withInteraction` keep their meaning. #788
re-implements them over the input host through `InputEvent.toSelectionRequest`, which applies the old change-only
rule. Adding cases to `SelectionRequest` was rejected: the template and BAR match it exhaustively.

### 2. Guest stage (stage 2)

A guest turns one input plus the client state into intents: `GuestIntent.Local` for client-only changes (selection, an
open menu, a targeting mode) or `GuestIntent.Submit` for a command. A guest is language-neutral:

| Constructor | For | Deadline and cancellation |
|---|---|---|
| `InputGuest.sync` | an F# rule, including "no guest": the template's mapping is a sync guest | none; must be pure and quick |
| `InputGuest.async` | an F# rule that awaits something | the pipeline's deadline; it observes a `CancellationToken` |
| `InputGuest.ofBytes` | WebAssembly through `FS.GG.Wasm.Browser`, or any byte-level runtime | the pipeline's deadline is the outer bound; the runtime's own deadline is inside it |

The guest stage follows these rules. They are SC2's and BAR's hand-built rules, made common.

1. **One call in flight.** Every input gets a `sequence`, strictly increasing within an epoch, starting at 1. Inputs
   wait in order behind the call in flight. A waiting `Hover` or `Focus` is replaced by a newer one. Activations are
   never coalesced.
2. **Bounded queue.** The default holds 16 inputs (`GuestPolicy.withQueueLimit`). One more ends every waiting input
   with `PipelineFailure.QueueOverflow`, and none of them is sent. BAR's queue also ends its waiting inputs on overflow,
   but it disarms the guest too; here the guest stays armed.
3. **Deadline.** The default is 500 ms per call (`GuestPolicy.withTimeout`). Past it, the call is cancelled and ends
   `TimedOut`.
4. **Cancellation.** A call is cancelled by its deadline, by `PipelineMsg.Reset` or by disposal. A cancelled call's
   late reply is dropped by its call id, which generalizes SC2's guest epoch.
5. **Failure routing.** Every failure becomes a `PipelineEffect.Fail` with a `PipelineFailure`, and is never raised.
   - `Faulted` (a trap, or output the codec cannot decode) and `TimedOut` halt the guest stage. Inputs are then refused
     until `Reset` re-arms it, which matches SC2's poisoned admission and BAR's disarm. A WebAssembly runtime
     terminates a guest it timed out anyway.
   - `Refused`, by the guest's own rule or by `InputGuest.withAdmission` (BAR's 64-unit bound, "not armed yet"), fails
     only that input.
6. **Renderer late failures (#768).** These are the other asynchronous failure a page must route. A host that does not
   use `RendererDriver` binds them with `BrowserRenderers.bind`, under the same live-mount rule as
   `RendererDriver.failBackend`, and dispatches `RendererMsg.MountFailed` for its current attempt. One route per failure
   kind, each bindable without the driver, is the general rule. That subsumes #768.

`GuestReply` maps `FS.GG.Wasm.Contracts.TerminalState` one to one:

| `TerminalState` | `GuestReply` |
|---|---|
| `Succeeded` | `Output` of `CopiedOutput` |
| `GuestRejected status` | `Refused status` |
| `Faulted d` | `Faulted d` |
| `TimedOut` | `TimedOut` |
| `Invalidated` or `Frozen` | `Cancelled` |

The game owns the wire codec (`encode` and `decode`), as SC2 owns its SC3G bytes and BAR its protobuf.
`FS.GG.Game.Render` does not reference `FS.GG.Wasm.*`. They ship on separate release trains, and the bridge is one
`invoke` function the game writes over `Host` (see Q3).

### 3. Command stage (stage 3)

- **Identity.** A `CommandId` is `epoch:sequence`. The epoch is fresh per session; use the client binding the authority
  issued, as SC2 already does with its authority generation. The sequence strictly increases from 1 within the epoch,
  and the same id is never two commands. It maps one to one onto `SessionInput`: `InputId` is the text, and `Sequence`
  is the sequence.
- **Idempotency.** This is an authority obligation. The authority admits with `NetworkAdmission.admit`. It answers a
  duplicate id with the answer it gave first, and never applies the command twice. A resend reuses the id. The
  pipeline never resends on its own.
- **Acknowledgement.** The authority answers `CommandAck.Accepted` (admitted, effect pending), `Applied version` (the
  effect is in the authoritative state from that version on) or `Rejected reason`.
- **Reconciliation.** The pipeline keeps a ledger of `CommandSettlement`s: `Pending`, `Accepted`, `Applied`, `Rejected`
  or `Unconfirmed`.
  - **Versions.** Authoritative states carry a non-decreasing version (`CommandStage.create version`). An older state is
    ignored, which is the template's tick rule.
  - **Settlement without `Applied` acks.** An authority that never sends `Applied` (RuneScape's rs-sdk confirms
    dispatch, not effect) declares `withSettlement settled patience`. A command is then `Applied` at the first version
    where the game's test holds, such as arrival on the tile.
  - **Patience.** A command is `Unconfirmed` after `patience` newer versions without evidence either way. Patience counts
    versions, not wall time, so reconciliation is deterministic and replayable. The default is 600.
  - **Reset.** `Reset` marks every unsettled command `Unconfirmed`.
- **Window.** The default allows 8 unsettled commands (`withWindow`). SC2 uses 1. A `Submit` beyond the window fails
  with `CommandWindowFull` and is not sent.
- **Authority.** The server is the only authority. `Displayed.authoritative` is only ever a state the authority sent.
  **Optimistic display is optional and marked.** `withPrediction predict` makes `Displayed.shown` the prediction of
  every `Pending` or `Accepted` command over the latest authoritative state. It is recomputed on every authoritative
  state and withdrawn on `Rejected`. `Displayed.predicted` names the commands in it, so the view can mark what is not
  authoritative yet. The template's `PreviewPath` is such a prediction. Without `withPrediction`, `shown` is
  `authoritative`.

### 4. Accessibility

- **Mirror for large grids (#784).** Objects declare `ObjectAccess` with `SceneInteraction.withAccess`:
  - `full` is the default and today's behaviour;
  - `pointerOnly` objects have no focus stop and no mirror entry;
  - `grouped label` places the object inside a `role=group` element labelled `label`.

  The whole mirror is **one Tab stop**, with a roving tabindex. Arrow keys move through the keyboard-reachable entries
  in object order, Home and End jump to the ends, and Page Down and Page Up jump between groups. A 15 x 15 grid
  declared `grouped "Map tiles"` is one Tab stop, not 225.
- **Focus drawing.** See stage 1. The mirror stays visually hidden even when it has focus, so the layout does not
  expand. The drawn cue on the surface is the visible focus indicator.
- **Output role.** Every backend's focusable output (the SVG root or the canvas) has `role=group` and the options label.
- **Escape.** Escape is always `Cancel`, on the surface and on every mirror entry, and it never moves focus. The game
  or its guest decides what Cancel means, such as leaving a targeting mode or closing a menu. Without a pipeline, the
  route clears a non-empty selection. Keys the input host reports (arrows, Enter, Space, Escape, ContextMenu, Shift+F10)
  stop at the host and do not reach page-level bindings, which also decides the key-containment half of #773.

### 5. Gap mapping

| Gap | Decision | State |
|---|---|---|
| **#771**: source and modifiers; repeated and secondary activation | `InputEvent` (source, all modifiers, `Activate` always reported, `ActivateSecondary`, `repeat`), `InputRoute`, `SceneRenderers.withInput` | **Satisfied by the contract.** Closes when #788 implements it and the kit's `input` section covers it. |
| **#772**: kit for hybrid pages and async selection | `RendererConformance.withOwnInputHost backend` (the game's own SVG map is checked through its probe and its events, not the library's markup); `RendererConformance.withInput` checks a selection answered later through the pipeline, within the subject's `withWaitUntil` | **Satisfied by the contract.** Closes with #788; SC2 then declares the section. |
| **#768**: late-failure routing without `RendererDriver` | `BrowserRenderers.bind`; pipeline failures are `PipelineEffect.Fail` | **Satisfied by the contract (subsumed).** Closes with #788. |
| **#783**: kit probes on object edges | Not a pipeline concern. Recorded here for the `input` section: hit regions are closed (`SceneInteraction.contains` counts edges as inside, as documented), and kit probes sample at least 1 scene unit inside or outside every edge, because backends pick at sub-pixel precision and an exact edge sample cannot be agreed | **Left open.** It stays its own kit Delivery and can land before #788. |
| **#784**: pointer-only and grouped objects | `ObjectAccess`, `SceneInteraction.withAccess`, the one-Tab-stop mirror | **Satisfied by the contract.** Closes with #788. |
| #787 comment N4: invisible keyboard focus | `InteractionCues`, `standardCues`, `withCues` | Satisfied by the contract; implemented in #788. |
| #787 comment N5: 240 Tab stops, mirror expands on focus, no `role=group` on SVG, Escape on a mirror button | One Tab stop, a hidden mirror, `role=group` on every output, Escape is `Cancel` everywhere | Satisfied by the contract; implemented in #788. |

### 6. Kit specification

The conformance kit gains an `input` section, declared with `RendererConformance.withInput interaction route probe
events`. It runs on every offered backend with real (trusted) input, as `interaction` does. `withOwnInputHost` exempts
a backend from markup checks I9 and I10 only.

| Id | Requirement |
|---|---|
| I1 | A primary press on each pointer-reachable object reports `Activate`, `Pointer`, that target and a point inside it. A press on empty ground reports `Activate` with no target and the press point. |
| I2 | A second press on the selected object reports `Activate` again. |
| I3 | A secondary press reports `ActivateSecondary`, and the page's context menu does not open. The ContextMenu key and Shift+F10 on the focused object report `ActivateSecondary`, `Keyboard`. |
| I4 | Shift, Control, Alt and Meta held during a press or key arrive in `modifiers`. Each is checked alone, and none is reported unheld. |
| I5 | Enter and Space on the focused object report `Activate`, `Keyboard`. A held key reports `repeat` true after the first event. |
| I6 | Arrow keys report `Focus` with the next or previous keyboard-reachable object, skipping `pointerOnly` objects. |
| I7 | A pointer move across objects reports `Hover` once per change of hovered object, and once with no target when leaving. |
| I8 | **Focus cue:** after keyboard focus, the pixels just outside the focused object's bounds differ from the same frame unfocused, on every backend (the `pixels` probe). After a pointer press they do not. A game's `withCues` layer replaces the standard one. |
| I9 | **Mirror:** exactly one Tab stop; `pointerOnly` objects are absent; each group is one `role=group` element with its label; activating an entry reports `Activate`, `Mirror`; the mirror's bounding box does not change when it gains focus. |
| I10 | The output has `role=group` and the label on every backend. |
| I11 | Escape on the surface and on a mirror entry reports `Cancel`, and focus stays where it was. Reported keys do not reach a page-level listener. |
| I12 | A selection the game answers later (an async guest) is shown within the subject's wait, and survives a backend switch. |
| I13 | Switching away and disposing the host release the input host (the census returns to its value before). |
| I14 | Kit probes never sample within 1 scene unit of an object edge (#783). |

Pipeline laws run in process, on .NET and on emitted Fable with equal counts, with a recording guest and a fake
authority:

| Id | Law |
|---|---|
| P1 | Input sequences and command sequences strictly increase within an epoch. |
| P2 | At most one guest call is in flight. |
| P3 | A deadline ends the call `TimedOut`, emits `CancelGuest` and halts the guest stage until `Reset`. |
| P4 | A reply for a cancelled or timed-out call changes nothing. |
| P5 | `Reset` cancels the call in flight, ends the queue and marks unsettled commands `Unconfirmed`. |
| P6 | Queue overflow fails every waiting input, and no waiting input is sent. Hover and Focus are coalesced; activations are not. |
| P7 | Without prediction, `shown` equals `authoritative` in every model. With it, a `Rejected` command leaves `shown`, and `predicted` is empty exactly when `shown` equals `authoritative`. |
| P8 | An authoritative state with a lower version than the one shown is ignored. |
| P9 | A command with no evidence after `patience` versions is `Unconfirmed`, never `Applied`. |
| P10 | A duplicate `Acknowledged` for one id is idempotent. |
| P11 | A `Submit` beyond the window is not sent and fails `CommandWindowFull`. |

### 7. How guests are built and tested

- **F# guests** (`sync`, `async`) are ordinary functions. They are tested in process against the reducer, on .NET and
  Fable, with the P laws.
- **WebAssembly guests** use the shared guest SDK (`docs/wasm/sdk.md`):
  - **Rust:** Rust and Cargo 1.90.0, target `wasm32-unknown-unknown`.
  - **C:** WASI SDK 34.0.

  Both emit freestanding core modules, and `FS.GG.Wasm.Browser` runs them in a Worker. **Rust guests build in CI only:**
  fdev has no Rust toolchain, so no local step may require `cargo`. The CI job builds the module and hands it to the
  browser tests as a build artifact. Built `.wasm` files are not committed.
- **Correspondence.** A game commits golden request and reply vectors (base64, like SC2's `fixtures/*-guest-*.base64`)
  for its codec. Locally, a recording F# stand-in guest answers from the vectors through the same `InputGuest.ofBytes`
  codec. In CI, the real module must produce the same replies for the same requests. Local runs prove the codec and the
  pipeline; CI proves the module. Containment (trap, infinite loop, oversize output) stays `FS.GG.Wasm.Browser`'s
  qualified behaviour and is not re-tested per game.

### 8. Migration

Each migration is its own Delivery after #788 publishes. None blocks on another.

- **Template `fs-gg-fable-game`** (first, because it defines "standard"):
  - `Stage.fs` binds an `InputRoute` to `PipelineHost` with a sync guest: `Activate` on a cell produces
    `[Local (Select cell); Submit (MoveTo cell)]`, and `Cancel` produces `Local Clear`. Re-clicking a cell moves again.
  - `InputMessage` carries the `CommandId`. The server already admits with `NetworkAdmission`; it starts answering
    `Applied tick` or `Rejected`.
  - `PreviewPath` becomes `withPrediction` and is drawn marked.
  - Tiles are `grouped "Arena"`.
  - Outcome #5218 requires a guest, so the template also ships a WebAssembly guest of the same rule, built in CI, behind
    the same codec.
- **SC2.Client:**
  - `activateUnit` becomes an `ofBytes` guest over the existing SC3G codec and Rust guest.
  - The capture-phase source and modifier hack, the guest queue, the epoch, poisoning and the input-sequence fence are
    removed in favour of the pipeline's.
  - `commandPending` becomes `withWindow 1`, and the fresh-observation rule becomes the guest's own refusal.
  - `withOwnInputHost Svg` lets the kit's `interaction` and `input` sections run (#772).
- **FSBarV2:** when the live path connects, its existing WASM guest becomes an `ofBytes` guest over its protobuf codec.
  The 64-unit bound becomes `withAdmission` over `BarGameState.guestPreviewable`. Escape is `Cancel`, whose meaning
  (disarm) is BAR's. `BrowserRenderers.bind` replaces the hand-built Pixi surface (#768).
- **FS.GG.RuneScape:**
  - `ActivateSecondary` opens the option menu, and a repeated `Activate` walks to the same tile.
  - Tiles are `grouped "Map tiles"` (#784).
  - Delivery #20's arbiter verbs are `Submit` commands with `CommandId`s.
  - Its dispatched, proven, unproven and refused states become `Accepted`, then `Applied` or `Unconfirmed` through
    `withSettlement` (arrival, patience 200), or `Rejected`.

## Consequences

- One contract replaces three hand-built pipelines. A new game declares a guest and a command stage instead of reading
  DOM events and building queues.
- **Additive.** No existing type or function changes. New public types are opaque (built and read through modules), so
  #788 and later work can add to them without a break. The exceptions are the small closed vocabularies
  (`InputSource`, `InputModifiers`, `InputAction`, `GuestIntent`, `GuestOutcome`, `GuestReply`, `CommandAck`,
  `CommandSettlement`, `PipelineFailure`, `PipelineMsg`, `PipelineEffect`). The surface baselines record exactly
  additions.
- Until #788 merges, the stubs raise with a message naming FS.GG.Game#788, and a test pins that.
- Authorities that adopt the pipeline take on the idempotency obligation in stage 3.
- The organization boundaries of ADR-0022 and ADR-0028 are unchanged: key policy stays in Game, and `Game.Render`
  gains no dependency.

## Open questions for the owner

Each has a recommended default. Work proceeds on the default unless the owner chooses otherwise.

| # | Question | Recommended default |
|---|---|---|
| Q1 | When do #771, #772, #768 and #784 close? Their Done-when asks for behaviour and kit coverage. | They stay open, linked to #788, and close on #788's merge. #783 stays an independent kit Delivery. |
| Q2 | May an FS.GG.Game release ship these stubs before #788? | No: #788 lands before the next release. If a release is urgent first, ship it with the stubs listed in the changelog as raising. The API is additive, so existing behaviour does not change. |
| Q3 | Where does the WebAssembly bridge (`invoke` over `FS.GG.Wasm.Browser.Host`) live? | In each game (about 20 lines) and in the template sample, with no new package. Promote it to an `FS.GG.Game.Render.Wasm` package once two products carry the same bridge. |
| Q4 | Default guest deadline and command window. | 500 ms per call, 16 waiting inputs, 8 unsettled commands, patience 600 versions. All can be overridden. |
| Q5 | Should keyboard focus survive a backend switch? | No. Focus is input-host state; the selection, which is game state, survives. The renderer selector holds focus while switching anyway. |
| Q6 | Should this be an organization ADR? | No. The contract and its owner are both FS.GG.Game, and no repository boundary moves. Promote it only if the guest byte ABI becomes shared across repositories. |

## Alternatives considered

- **Add `SelectionRequest` cases** (activate again, secondary). This breaks exhaustive matches in the template and
  BAR, and it still carries no source or modifiers. Rejected for the opaque `InputEvent` beside it.
- **Make `SceneInteraction.select` asynchronous.** This puts authority and latency inside the selection rule, and still
  has no command identity. Rejected: selection stays a local intent, and authority stays with stage 3.
- **Type the guest stage against `FS.GG.Wasm.Browser`.** This would make `Game.Render` depend on a package released on
  a separate train (`wasm/v*`), and it excludes F# guests. Rejected for the byte-level `ofBytes` with a one-to-one
  `TerminalState` mapping.
- **Wall-clock acknowledgement timeouts.** These are not replayable, and they disagree across slow and fast machines.
  Rejected for patience counted in authoritative versions. The guest deadline is the one wall-clock bound, because a
  guest call is local computation.
- **Put focus in every game's model.** Every game would re-implement the same ring, and the template's renderers show
  that it does not happen. Rejected as the default; kept as an option through `Focus` events and `withCues`.
- **Client-side authority with later sync.** This contradicts "the server stays the only authority". Rejected;
  prediction is a marked display overlay that never touches the authoritative state.

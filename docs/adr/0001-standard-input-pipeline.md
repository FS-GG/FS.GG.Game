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
- **Revision:** revised after the independent review of `7ba30e53` (findings B1–B6 and N1–N9), and again after the
  delta review of `f0561a79` (R1–R3 and seven smaller items). The PR body lists the change for each finding.

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

`FS.GG.Game.Core` holds part of the authority half, and its limits shape stage 3:

- **`SessionInput`** carries `SessionId`, `InputId`, `Sequence` and `Value`. `InputId` is a stable, namespaced product
  identity (such as `arena.move`), not a per-command id.
- **`NetworkAdmission.admit`** orders inputs, keeping one rising sequence cursor per `ClientId`
  (`NetworkAdmission.lastSequence`). It refuses a `DuplicateInputSequence` or a `StaleInputSequence`. It stores no
  answers, so it cannot say what happened to a duplicate.

`FS.GG.Wasm.Browser` runs a guest in a Worker. A startup bound applies until the Worker is ready (`WorkerStartTimeout`,
5,000 ms by default), and a policy deadline applies after that. It reports a `TerminalState`: `Succeeded`,
`GuestRejected`, `Faulted`, `TimedOut`, `Invalidated` or `Frozen`. What is missing is the client contract that joins
input, guest and authority.

## Decision

Every game's input runs through one pipeline of three stages, typed in `FS.GG.Game.Render`.

```text
 SVG / Pixi / Babylon input host            game                              authority (server)
 ───────────────────────────────            ────                              ──────────────────
 pointer, keyboard, mirror ─InputEvent─▶ InputRoute ─Input(Scene)─▶ ┐
 game's own controls ──────────────────────────────── Input(Game) ─▶ ├ InputPipeline ─Send─▶ admit by ClientId+sequence,
 direct commands ─────────────────────────────────────── Submit ───▶ ┘  stage 2: guest      answer from cache
       ▲ draws focus cue (InteractionCues)                              (F#, or WASM bytes)          │
       └──────────── Displayed (authoritative ◀── Authoritative / Heartbeat / Acknowledged ◀────────┘
                                + marked prediction)
```

The reducer is pure Elmish, like `RendererSelection`. `InputPipeline.init` and `update` return `PipelineEffect`s, and
three hosts can run them:

- **`PipelineRunner`** performs them for an Elmish game, as `Cmd.ofEffect (fun _ -> PipelineRunner.perform effect
  runner)`. It holds the cancellation of the call in flight. BAR is this case.
- **`PipelineHost`** does it for a page that is not an Elmish program.
- **`InputGuest.run`** runs a single guest call, for a game that interprets `StartGuest` itself.

The game's `version`, `settled` and `predict` functions run inside `update`, and an exception from them propagates.

**Open unions.** `InputAction`, `PipelineInput`, `GuestOutcome`, `PipelineFailure` and `PipelineMsg` may gain cases in
a minor version, so match the first four with a wildcard. `PipelineEffect` may also gain cases. A game that interprets
effects itself, rather than through `PipelineRunner`, handles the new cases on upgrade. The answer and settlement types
(`CommandAck`, `CommandSettlement`) are opaque, so later states are additive.

### 1. Input events (stage 1)

`InputEvent` has structural equality and a private representation. It carries:

- **`source`**: `Pointer`, `Keyboard` or `Mirror`. Pen and touch arrive as `Pointer`.
- **`action`**:
  - `Activate`: a primary press, Enter or Space on the focused object, or a mirror option.
  - `ActivateSecondary`: a secondary press, a **touch long-press** (held 500 ms while moving less than 10 CSS px), the
    ContextMenu key, or Shift+F10. The browser menu is suppressed on the surface.
  - `Hover`: reported only when the hovered object changes.
  - `Focus`: the arrow keys.
  - `Cancel`: Escape.
- **`target`**: the object, or none for a press on empty ground. A ground press keeps its scene `point`.
- **`modifiers`**: Shift, Control, Alt and Meta, all four always present.
- **`repeat`**: true for an operating-system key repeat.

Every activation is reported, including a second activation of the selected object.

**Extension policy (`InputAction`).** The union is open. Continuous input (drag, wheel, press and release) will arrive
as new cases in a minor version. Consumers match with a wildcard.

**Game input.** Ability buttons, control groups, a submit button, and RuneScape's takeover, release and reacquire are
not scene objects. They enter the same queue as `PipelineMsg.Input (PipelineInput.Game input)`, so the guest decides
them with the same ordering and failure rules. `PipelineMsg.Submit command` is for a game that needs no decision.
It is an **ordered queue item that skips the guest**: it waits behind the call in flight and the earlier queued
inputs, never overtaking them, and is sent without a guest call when it reaches the front. It shares the command
window and ids. A `Submit` is accepted while the guest stage is halted, because it needs no guest; a halted stage
refuses only guest inputs.

**Focus and hover are drawable state.** The input host keeps `InteractionCues`: the focused object, the hovered
object, and whether focus is visible. Focus is visible after keyboard navigation and not after a pointer press. The
cues are not game state. They are not saved and do not survive a backend switch; the selection does survive.

Every interactive backend draws the interaction's cue layer, `CueContext -> SceneNode list`, above the frame's Scene.
On Babylon it is drawn unlit (#786). The default is `SceneInteraction.standardCues`: a two-tone ring of filled
rectangles just outside the focused object's bounds.

- The ring has a dark outer band and a light inner band. Each band is **at least 2 CSS px at the presented scale**:
  `CueContext.cssPerUnit` gives the scale, and the bands are `2 / cssPerUnit` scene units or more. Cue widths are
  specified in CSS px. `CueContext` carries no device pixel ratio; the backend's own DPR handling maps CSS px to
  device pixels.
- The tones keep at least 3:1 contrast (WCAG 2.2 focus appearance).

A game replaces the default with `SceneInteraction.withCues`, for example with the glyph layer's themed cues (#789).
That layer's default `cues` draws focus only, like `standardCues`. A hover outline is opt-in, through its
`cuesHover`.

**Theme switches reach the cue layer through the glyph side, not through `CueContext`.** A layer registered once with
`withCues` is fixed when the registry is built, so the glyph layer's cues take a theme reader (`unit -> Theme`) rather
than a theme value. The cue layer reads it on every draw, and a runtime theme switch is drawn at the next present or
cue change. `CueContext` stays as it is (cues, objects, `cssPerUnit`), and #793 adds no theme or view field. This
split, with the node-list type and `CueContext` itself, is agreed with the #789 owner.

**Compatibility.** `SelectionRequest`, `SelectionRoute` and `SceneRenderers.withInteraction` keep their meaning.
#788 re-implements them over the input host through `InputEvent.toSelectionRequest`.

### 2. Guest stage (stage 2)

A guest turns one input, plus the client state, into intents. An intent is `GuestIntent.Local` (a client-only change)
or `GuestIntent.Submit` (a command). Guests are language-neutral:

- **`InputGuest.sync`** (an F# rule). This includes "no guest": the template's mapping is a sync guest.
- **`ofAsync`** (an F# rule that awaits).
- **`ofBytes`** (WebAssembly through `FS.GG.Wasm.Browser`, or another byte-level runtime). The game owns the codec.
  `Game.Render` takes no Wasm dependency.

The guest stage follows these rules:

1. **Order and identity.** Input sequences rise from 1 for the model's lifetime. Call ids are separate, never repeat,
   and are never reset, not even by `Rebind`.
2. **One call in flight.** Inputs wait in order behind it. A newer `Hover` or `Focus` removes the waiting one of the
   same kind, and the newer one joins the tail. Activations and `Game` inputs are never coalesced.
3. **The queue is bounded.** The default limit is 16. Coalescing happens first. If the queue is still full, the
   **arriving** input is dropped, never sent, and fails as `QueueOverflow [its sequence]`. The waiting inputs stay.
4. **Client state is read when the call starts.** `StartGuest` carries a `GuestCall` (id, sequence, input, basis). The
   interpreter builds the `GuestRequest` with `client ()` at that moment, so a queued input sees the state its call
   starts in, not the state it was queued in. `client ()` must return the latest state the game has applied; for an
   Elmish program, that is a reference its `update` sets.

   **The next call starts only after the previous decision's `Local`s are applied.** When a call ends, its batch
   carries the decision's `ApplyLocal`s and ends with `PipelineEffect.Continue`, never with `StartGuest`. A runner
   performs `ApplyLocal` by dispatching the game's message, and `Continue` by dispatching `PipelineMsg.StartNext`
   through the same dispatch. Elmish's FIFO queue therefore runs the game's `Local` messages before `StartNext`
   starts the next call. A batch never carries both `ApplyLocal` and `StartGuest`. `PipelineHost` applies `Local`s
   synchronously and follows the same rule.
5. **Two deadlines.** `StartGuest` comes with `ArmStartDeadline`. The start deadline defaults to 10,000 ms
   (`withStartTimeout`), which covers the Wasm Worker's 5,000 ms startup bound plus compilation. `ofBytes`'s `invoke`
   calls `started ()` once the module is compiled and instantiated and the call has begun. The runner then dispatches
   `GuestRunning`, which emits `ArmDeadline` with the call deadline (default 500 ms). `ofAsync` guests are warm at
   once.
   - An `invoke` that never starts reaches `GuestStartDeadline` first. The call ends `Faulted "start timeout"` and
     the stage halts.
   - A started call that never ends reaches `GuestDeadline` and ends `TimedOut`.
   - Either way the call is cancelled and abandoned, and its late reply is dropped. A start deadline that arrives
     after `GuestRunning` is ignored.
6. **Recovery is separate from binding.**
   - `TimedOut` fails only that input (`GuestTimedOut`). The guest stays usable, and a WASM runtime reloads its Worker
     for the next call.
   - A breaker halts the guest stage after N consecutive timeouts (default 3) and fails `GuestHalted`. Any decided or
     refused call resets the count.
   - `Faulted` (a trap, output that cannot be decoded, or an exception) halts at once.
   - `PipelineMsg.RearmGuest` makes the guest usable again and resets the breaker. Nothing else changes.
   - While the stage is halted, inputs are refused (`InputRefused`), not queued.
7. **Exceptions become outcomes.** `InputGuest.run` never raises. An exception in `admit`, `decide`, `encode`,
   `invoke` or `decode` becomes `Faulted`. An admission `Error` becomes `Refused` before any call. A cancelled token
   becomes `Cancelled`.
8. **Escape does not cancel a call.** A `Cancel` input is queued like any input and never cancels the call in flight.
   The guest decides what Cancel means.
9. **Decisions are atomic.** The command window is checked for all the `Submit`s of one decision before any effect. If
   they do not all fit, **none** of the decision's intents applies, including its `Local`s, and it fails
   `CommandWindowFull`.
10. **Effect order.** The effects of one message come in this order: `CancelGuest`, then `ApplyLocal` and `Send` in
    intent order, then `Fail`, then either `Continue` (after a call ended) or `StartGuest` with `ArmStartDeadline`
    (when an input or `StartNext` finds the stage idle).

**WebAssembly mapping.** `GuestReply` has one case per `TerminalState`, so `GuestReply` itself loses nothing:

| `TerminalState` | `GuestReply` | `GuestOutcome` | Effect on the stage |
|---|---|---|---|
| `Succeeded` | `Output bytes` | decoded intents, or `Faulted` if decoding fails | none, or halt |
| `GuestRejected s` | `Refused s` | `Refused "status s"` | none |
| `Faulted d` | `Faulted d` | `Faulted d` | halt |
| `TimedOut` | `TimedOut` | `TimedOut` | breaker counts |
| `Invalidated` | `Invalidated` | `Cancelled "invalidated"` | only that input fails (`InputCancelled`) |
| `Frozen` | `Frozen` | `Cancelled "frozen"` | only that input fails |

The only loss is in `GuestOutcome`, where `Invalidated` and `Frozen` share `Cancelled`. They stay distinct by their
reason string. If the runtime ends the call in flight because of an `Invalidated` replacement or a `Frozen` host, that
input fails `InputCancelled` and the next queued input starts. #788 adds a central golden-vector test: one vector per
`TerminalState`, run through `ofBytes` and `InputGuest.run`, in a test project that references both packages.

**Renderer late failures (#768)** are a separate binding with the same rule, that each asynchronous failure route can
be bound without a driver. `BrowserRenderers.bindTo model dispatch route` lets a host that runs `RendererSelection`
itself receive `RendererMsg.MountFailed` for `model ()`'s current attempt. It fires only while that backend is
selected and active, the rule of `RendererDriver.failBackend`.

### 3. Command stage (stage 3)

- **Identity.**
  - A `CommandId` is `client:sequence`. `client` is the binding the authority issued (`NetworkClientBinding.ClientId`).
  - The sequence **rises for that binding across every reconnect and `Rebind`**. `init` and `Rebind` take the
    authority's `NetworkAdmission.lastSequence` and continue above the higher of it and the highest sequence issued,
    so no id is ever two commands.
  - The game carries the `CommandId` in its envelope or payload and maps `Sequence = CommandId.sequence` onto
    `SessionInput`. `InputId` stays the product's own identity.
- **Authority duties.**
  - Admit with `NetworkAdmission.admit`.
  - Keep an **answer cache of the last W answers per client**. W is at least the client's window; 64 is recommended.
  - Answer a duplicate or stale id that is in the cache with the cached answer, never applying the command twice.
  - Answer `rejected "stale"` for a stale id inside the cache window that it never admitted. That is truthful: the
    command never ran.
  - Answer `CommandAck.expired` for an id older than the cache.
  - **Transport ordering assumption.** One binding's commands arrive in sequence order, as on one WebSocket or
    SignalR connection. If a transport reorders them, a lower id that arrives late is refused as stale
    (`rejected "stale"`) and never runs. It is never applied out of order.
  - Send `lastSequence` (its `NetworkAdmission.lastSequence` for the binding) on every connect and reconnect. That
    covers a page reload that reuses the same `ClientId`.
  - Send a **version heartbeat** (`PipelineMsg.Heartbeat`) when the version advances without a new state for the
    client, so patience advances.
  - Send a running answer (`progress`, `queued` or `started`) for a long-lived command more often than every
    `patience` versions, so a command that is still legitimately running is never settled `Unconfirmed`. RuneScape's
    `hold` is the case. This keeps one patience per stage rather than one per command.
  - The pipeline never resends. After a reconnect, unsettled commands settle by state, an answer or patience.
- **Answers** (`CommandAck`) are opaque values. Running answers restart the command's patience; final answers settle
  it:

  | Answer | Kind | Meaning |
  |---|---|---|
  | `accepted` | running | admitted, effect pending |
  | `queued` | running | waiting behind an arbiter's queue |
  | `started` | running | the command has begun |
  | `progress note` | running | still running |
  | `applied v` | final | the effect is authoritative from version v |
  | `ended v outcome` | final | a long-lived command finished at v |
  | `cancelled reason` | final | ended after acceptance; it **may have partly run**, unlike `rejected` |
  | `rejected reason` | final | refused before it ran; nothing happened |
  | `expired` | final | settles as unconfirmed |

  `CommandSettlement` is opaque, read through `isPending`, `isAccepted` (accepted, queued or running), `isQueued`,
  `isRunning`, `appliedAt`, `outcome`, `cancelled`, `rejected`, `isUnconfirmed` and `isFinal`.

  **The first final answer wins.** Once a command is final, later answers for it are ignored and never reopen it,
  including a reordered `started` that arrives after `applied`. Running answers move it between accepted, queued and
  running while it is not final. Answers for unknown ids are ignored.
- **Reconciliation.**
  - Authoritative states carry a non-decreasing version. An older state is ignored.
  - Each command's `basis` is the version of the authoritative state shown when it was decided. The basis is never
    optional, because `init` requires a state.
  - **Patience** is the difference between the highest version heard (from states and heartbeats) and the command's
    basis, or its latest running answer. A command that is not final when the difference reaches patience (default
    600, at least 1) settles `Unconfirmed`.
  - `withSettlement settled` reads the **authoritative** state, never the predicted one. It applies a command at the
    first authoritative version where the test holds, including commands of an earlier binding, until their patience
    runs out.
- **Window.** The default is 8 unsettled commands; SC2 uses 1. The check is atomic per decision, as in stage 2.
- **Authority.** The server is the only authority. **Optimistic display is optional and marked.**
  - `withPrediction predict` shows `predict` folded over `Displayed.predicted`, in sequence order, on the latest
    authoritative state.
  - A command is predicted while it is pending or running, and while it is applied or ended at a version **the
    authoritative state has not reached yet**. That keeps the prediction until its effect is visible, so it does not
    flicker.
  - `rejected`, `cancelled` and `expired` withdraw it.
  - Without `withPrediction`, `predicted` is empty and `shown` is `authoritative`.

### 4. Accessibility

- **One Tab stop.** The surface (the SVG root or the canvas) is the single Tab stop, labelled by the options label.
  Its role depends on the scene:
  - **No cell group:** the surface is a `role=listbox`. Each keyboard-reachable object is a `role=option` with
    `aria-selected`. A `grouped` set is nested in a labelled `role=group`.
  - **Any cell group (a mixed scene):** the surface is ONE `role=grid`.
    - Each cell group is a labelled `rowgroup` of `row`s and `gridcell`s, at its declared rows and columns.
    - Every other object, or `grouped` set, is a labelled `rowgroup` whose rows each hold a single `gridcell`.
    - `aria-selected` is on the cells.
  - **Ownership.** The mirror's options or cells belong to the surface. For an SVG root, the surface owns them with
    `aria-owns`. For a canvas, they are the canvas's fallback content, its DOM children. Either way, the surface's
    `aria-activedescendant` names the active one.
  - **Keys.** Arrow keys move the active option or cell (in two dimensions inside a cell group). Home and End jump to
    the ends, and Page Up and Page Down move between groups. Enter, Space and the secondary keys act on the active
    item.
  - **Hidden mirror.** The mirror stays visually hidden and never expands the layout. The drawn focus ring is the
    visible indicator. #788 may also place each hidden option transparently over its object's bounds, with
    `pointer-events: none`, so that screen-reader highlight boxes line up with the drawing.
- **Large grids (#784).** `ObjectAccess` declares how each object is reached:
  - `full` is the default.
  - `pointerOnly` objects have no option.
  - `grouped label` places the option in a labelled group.
  - `cell label row column` places it in a grid group. When every member of a group declares a cell, the group is a
    `role=grid` of rows, and the arrow keys move in two dimensions.

  A 15 x 15 map declared as cells is one Tab stop with 2D arrow navigation.
- **Escape** is always `Cancel`, on the surface and on every option, and it never moves focus. Without a pipeline, it
  clears a non-empty selection. Keys the host reports (arrows, Enter, Space, Escape, ContextMenu, Shift+F10, Home, End,
  Page Up and Page Down) stop at the host and do not reach page bindings. This also decides #773's key containment.

### 5. Gap mapping

| Gap | Decision | State |
|---|---|---|
| **#771**: source and modifiers; repeated and secondary activation | `InputEvent` (source, all modifiers, `Activate` always reported, `ActivateSecondary` including touch long-press, `repeat`), `InputRoute`, `SceneRenderers.withInput` | **Satisfied by the contract.** Finished when #788 implements it with the `input` section. |
| **#772**: kit for hybrid pages and async selection | `RendererConformance.withOwnInputHost backend`, and a selection answered later through the pipeline (I12) | **Satisfied by the contract.** Finished by #788; SC2 then declares the section. |
| **#768**: late-failure routing without `RendererDriver` | `BrowserRenderers.bindTo model dispatch route`, covered by I15 | **Satisfied by the contract.** Finished by #788. |
| **#783**: kit probes on object edges | Not a pipeline concern. Recorded for I14: hit regions are closed, and probes stay at least 1 CSS px inside or outside each edge at the presented scale. Edge probes skip objects smaller than 2 units in either dimension. | **Left open.** It stays its own kit Delivery. |
| **#784**: pointer-only and grouped objects | `ObjectAccess` (`pointerOnly`, `grouped`, `cell`), the listbox or grid surface | **Satisfied by the contract.** Finished by #788. |
| #787 comment N4: invisible keyboard focus | `InteractionCues`, `CueContext`, `standardCues`, `withCues` | Satisfied; implemented in #788. |
| #787 comment N5: Tab stops, mirror expansion, `role=group` on SVG, Escape on a mirror button | The single-Tab-stop listbox or grid with `aria-activedescendant`, a hidden mirror, Escape is `Cancel` | Satisfied; implemented in #788. |

### 6. Kit specification

The kit gains an `input` section, declared with `RendererConformance.withInput interaction route probe events`. It
runs on every offered backend with real input. `withOwnInputHost` exempts a backend from the markup checks I9, I10 and
I16 only.

| Id | Requirement |
|---|---|
| I1 | A primary press on each pointer-reachable object reports `Activate`, `Pointer`, that target and a point inside it. A press on empty ground reports no target and the press point. |
| I2 | A second press on the selected object reports `Activate` again. |
| I3 | A secondary press, and a touch long-press, report `ActivateSecondary`, and the page's context menu does not open. The ContextMenu key and Shift+F10 report `ActivateSecondary`, `Keyboard`. |
| I4 | Each of Shift, Control, Alt and Meta, held alone, arrives in `modifiers`. None is reported unheld. |
| I5 | Enter and Space report `Activate`, `Keyboard`. A held key reports `repeat` true after the first event. |
| I6 | Arrow keys report `Focus` with the next keyboard-reachable object, skipping `pointerOnly` objects. In a cell group they move in two dimensions. |
| I7 | A pointer move reports `Hover` once per change of hovered object, and once with no target when leaving. |
| I8 | **Focus ring, against a hover-only frame.** With the pointer hovering object A and keyboard focus on B, the pixels just outside B's bounds differ from the frame where only A is hovered. After a pointer press on B (focus not visible) they equal the hover-only frame. Each band is at least 2 CSS px wide. This holds for `standardCues` and for a game's `withCues` layer. |
| I9 | **Surface:** exactly one Tab stop. Without a cell group it is a labelled `role=listbox` of `role=option`s, with `grouped` sets in labelled `role=group`s. With any cell group it is one labelled `role=grid`: cell groups are labelled `rowgroup`s of `row`s and `gridcell`s, and other objects and groups are labelled `rowgroup`s of single-cell rows. `aria-selected` is on options or cells. `aria-activedescendant` names the focused object's item. The items are owned by the surface, through `aria-owns` (SVG) or as fallback content (canvas). `pointerOnly` objects have no item. |
| I10 | The mirror's bounding box does not change when the surface gains focus. Activating an option with a screen-reader click reports `Activate`, `Mirror`. |
| I11 | Escape on the surface reports `Cancel`, and focus does not move. Reported keys do not reach a page-level listener. |
| I12 | A selection the game answers later (an async guest) is shown within the subject's wait, and survives a backend switch. |
| I13 | Switching away and disposing release the input host. |
| I14 | Kit probes never sample within 1 CSS px of an object edge at the presented scale. Edge probes skip objects smaller than 2 units (#783). |
| I15 | A host bound with `BrowserRenderers.bindTo`, not `RendererDriver`, falls back to SVG on Pixi's adapter late failure. A failure of a mount already disposed is not reported (#768). |
| I16 | The SVG root and each canvas present the same surface roles and labels. |

The pipeline laws run in process, on .NET and on emitted Fable with equal counts. They use a recording guest, a fake
runtime and a fake authority.

| Id | Law |
|---|---|
| P1 | Input sequences rise from 1 for the model's lifetime. Call ids never repeat. No `CommandId` is issued twice, whatever `Rebind`s occur. A `Rebind` continues above the higher of `lastSequence` and the highest sequence issued. |
| P2 | At most one guest call is in flight. |
| P3 | `StartGuest` comes with `ArmStartDeadline`. `ArmDeadline` follows only `GuestRunning`. A start deadline before `GuestRunning` ends the call `Faulted "start timeout"` and halts the stage; one after `GuestRunning` is ignored. A call deadline after `GuestRunning` ends it `TimedOut`. |
| P4 | `TimedOut` fails only that input. N consecutive timeouts halt the stage with `GuestHalted N`. A decided or refused call resets the count. `Faulted` halts at once. `RearmGuest` unhalts and changes nothing else. |
| P5 | A reply for a cancelled, timed-out or superseded call changes nothing. |
| P6 | A `Rebind` ends the call in flight and the queue with `InputCancelled`. Unsettled commands of the earlier binding keep settling by state and patience. A blank client fails `RebindRefused` and changes nothing. |
| P7 | The guest's `client` is read when its call starts: an input queued at state s1 and started at s2 sees s2. |
| P7b | Under `PipelineRunner` with an Elmish FIFO dispatch, the next call's request reflects every `ApplyLocal` of the previous decision. A batch that carries `ApplyLocal` ends with `Continue`, never with `StartGuest`. |
| P8 | A newer `Hover` or `Focus` replaces the waiting one of its kind at the tail. Activations and `Game` inputs keep arrival order. |
| P9 | A full queue drops the arriving input, fails `QueueOverflow [its sequence]`, and keeps the waiting inputs. |
| P10 | The effects of one message are in the declared order. |
| P10b | `Submit` keeps queue order: it never overtakes the call in flight or an earlier queued input, and it is sent without a guest call at the front. It is accepted while the guest stage is halted; guest inputs are refused then. |
| P11 | A decision whose submits do not fit the window applies none of its intents, `Local`s included, and fails `CommandWindowFull`. |
| P12 | A `Cancel` input during an async call does not cancel that call. |
| P13 | An exception in `admit`, `decide`, `encode`, `invoke` or `decode` becomes `Faulted`. An admission `Error` becomes `Refused` without a call. |
| P14 | Prediction. `predicted` is exactly the commands that are pending or running, plus those applied or ended at a version above the authoritative state's version, in sequence order. `shown` equals `predict` folded over `predicted` on `authoritative`. Without prediction, `predicted` is empty and `shown` is `authoritative`. |
| P15 | An authoritative state older than the one shown is ignored. |
| P16 | A command is `Unconfirmed` exactly when it is not final and the highest version heard minus its basis (or its latest running answer) reaches patience. A heartbeat advances patience and never prediction. |
| P17 | `settled` is evaluated on authoritative states only, and applies a command at the first version where it holds. |
| P18 | A repeated answer for one id is idempotent. `expired` settles `Unconfirmed`, `cancelled` is distinct from `rejected`, and both withdraw prediction. |
| P18b | The first final answer wins. A later answer for a final command (including `started` after `applied`) changes nothing. An answer for an unknown id changes nothing. `queued` and `started` while not final restart patience. |
| P19 | `CommandStage.withPatience 0`, `withWindow 0`, `GuestPolicy.withBreaker 0`, `withStartTimeout 0` and a blank `init` client are refused. |
| P20 | Every `TerminalState` golden vector maps as the WebAssembly table says. |

### 7. How guests are built and tested

- **F# guests** (`sync` and `ofAsync`) are ordinary functions. They are tested in process against the reducer, on
  .NET and on Fable, with the P laws.
- **WebAssembly guests** use the shared guest SDK (`docs/wasm/sdk.md`): Rust 1.90.0 for `wasm32-unknown-unknown`, or
  WASI SDK 34.0 for C. `FS.GG.Wasm.Browser` runs them in a Worker.
  - **Rust guests build in CI only.** fdev has no Rust toolchain, so no local step may require `cargo`.
  - The CI job builds the module and hands it to the browser tests as a build artifact. Built `.wasm` files are not
    committed.
- **Correspondence.** A game commits golden request and reply vectors for its codec.
  - Locally, a recording F# stand-in answers from the vectors through the same `ofBytes` codec.
  - In CI, the real module must produce the same replies.
  - Containment (traps, loops, oversize output) is `FS.GG.Wasm.Browser`'s qualified behaviour and is not re-tested per
    game. P20 is the one central mapping test.

### 8. Migration

Each migration is its own Delivery after #788 publishes.

- **Template `fs-gg-fable-game`** (first).
  - Both the DOM grid and the renderer stage report to one `InputRoute`.
  - A sync guest decides `Activate` on a cell as `[Local (Select cell); Submit (MoveTo cell)]`, and `Cancel` as
    `Local Clear`. Re-clicking a cell moves again.
  - `InputMessage` carries the `CommandId`. The server keeps the answer cache and answers `applied tick` or
    `rejected`. It sends the binding's `lastSequence` on connect and on reconnect, so a page reload with the same
    `ClientId` continues above it.
  - `PreviewPath` becomes `withPrediction`, drawn marked.
  - Cells are `cell "Arena" row column`.
  - A WebAssembly guest of the same rule, built in CI, is the guest Outcome #5218 requires.
- **SC2.Client.**
  - `activateUnit` becomes an `ofBytes` guest over the existing codec and Rust guest.
  - The ability buttons, control groups and submit become `Game` inputs.
  - The capture-phase source hack, guest queue, epoch, poisoning and sequence fence are removed.
  - `commandPending` becomes `withWindow 1`. The fresh-observation rule becomes the guest's own refusal.
  - `withOwnInputHost Svg` lets the kit run (#772).
- **FSBarV2.** When the live path connects:
  - its guest becomes an `ofBytes` guest run by `PipelineRunner` from its Elmish `Cmd`;
  - the 64-unit bound becomes `withAdmission`;
  - Escape is `Cancel`, and disarming is BAR's meaning of it;
  - `BrowserRenderers.bindTo` replaces the hand-built Pixi surface (#768).
- **FS.GG.RuneScape.**
  - `ActivateSecondary` opens the option menu, and a repeated `Activate` walks again.
  - Tiles are cells (#784).
  - Takeover, release, reacquire, cancel and cancel-all are `Game` inputs or `Submit`s.
  - FS.GG.RuneScape decision 0002 (merged at `1a0c532b`) makes pre-emption suspend and resume. So a pre-empted
    command is answered `queued` and its resumption `started`, and both restart patience. `ended` reports
    completion, `cancelled` an explicit cancel, and `rejected` a refusal.
  - The arbiter sends `progress` for a `hold` more often than patience.
  - "Proven by arrival" is `withSettlement` with patience 200.

## Implementation decisions (FS.GG.Game#788)

These settle details the contract left open. The laws in `tests/Game.Render.Pipeline.Laws` pin them.

- **Queued `Submit`s do not count toward the guest queue limit.** They never overflow; the command window bounds
  them.
- **A halted stage keeps its `Submit`s.** When the stage halts, queued guest inputs fail
  `InputRefused "guest halted"`, and queued `Submit`s stay and are sent in order.
- **`Unconfirmed` is final.** A late positive answer (`applied`, `ended`) for an `Unconfirmed` command does not
  upgrade it (first final answer wins, P18b). The authoritative state remains the evidence of what happened, and a
  game that needs the effect reads it there.
- **Ledger pruning.** A final command leaves the ledger once nothing depends on it. A command applied or ended at a
  version the authoritative state has not reached stays, for prediction. Answers for a pruned id are ignored, which is
  the same as first-final-wins.
- **Elmish.** `PipelineRunner.performAll effects runner` performs one `update`'s effects in order. An Elmish program
  runs it inside one `Cmd.ofEffect`, never split across `Cmd.batch` entries or run through `Cmd.OfAsync` or
  `Cmd.OfPromise`, so `ApplyLocal` messages are dispatched before `StartNext` (P7b).

## Consequences

- One contract replaces three hand-built pipelines.
- **The change is additive.** No existing type or function changes, and the surface baselines record additions only.
  New records are opaque or have private representations, and the answer and settlement types are opaque. The open
  unions and their wildcard rule are stated above.
- Until #788 merges, every stub raises with a message naming FS.GG.Game#788, and tests pin that (the .NET tests, and
  the browser smoke for `bindTo`).
- Authorities that adopt the pipeline take on the answer-cache and heartbeat duties in stage 3. Game.Core does not
  provide the cache today (see Q7).
- The organization boundaries of ADR-0022 and ADR-0028 are unchanged.

## Open questions for the owner

Each question has a recommended default. Work proceeds on the default unless the owner chooses otherwise.

| # | Question | Recommended default |
|---|---|---|
| Q1 | When do #771, #772, #768 and #784 close? | Keep them open, linked to #788, and close them on #788's merge. #783 stays independent. |
| Q2 | May a release ship these stubs before #788? | No; #788 lands first. If a release is urgent, list the stubs in the changelog as raising. |
| Q3 | Where does the WebAssembly bridge (`invoke` over `FS.GG.Wasm.Browser.Host`, including the `started` signal) live? | In each game and the template sample. Promote it to an `FS.GG.Game.Render.Wasm` package once two products repeat it. |
| Q4 | What are the default limits? | A 10,000 ms start deadline, 500 ms per call, 16 queued inputs, a breaker of 3, 8 unsettled commands, patience of 600 versions, and an answer cache of 64. |
| Q5 | Does focus survive a backend switch? | No. Focus is input-host state, and the selection survives. |
| Q6 | Should this be an organization ADR? | No, unless the guest byte ABI becomes shared across repositories. |
| Q7 | Who provides the authority's answer cache? | Each authority for now. Offer `NetworkAdmission` an additive answer cache in Game.Core as a follow-up once two authorities carry one. |

## Alternatives considered

- **Add `SelectionRequest` cases.** This breaks exhaustive matches, and the cases would still carry no source or
  modifiers. Rejected.
- **Make `SceneInteraction.select` asynchronous.** This puts authority and latency into the selection rule. Rejected;
  selection stays a local intent.
- **Type the guest stage against `FS.GG.Wasm.Browser`.** That is a separate release train, and it excludes F# guests.
  Rejected for `ofBytes` with a one-case-per-state `GuestReply`.
- **Use `SessionInput.InputId` as the command id, restarting per epoch.** It conflicts with `InputId`'s meaning, and
  after a reset `NetworkAdmission`'s per-client cursor refuses every restarted sequence as stale. Rejected for ids that
  rise per binding and are carried beside `InputId`.
- **Tie guest recovery to the epoch.** A guest timeout would force a session reset. Rejected for `RearmGuest` and a
  breaker.
- **Use wall-clock acknowledgement timeouts.** They are not replayable. Rejected for version-counted patience with
  heartbeats; the guest deadline is the one wall-clock bound.
- **Put focus in every game's model.** Rejected as the default. It remains possible through `Focus` events and
  `withCues`.
- **Client-side authority.** Rejected; prediction is a marked overlay.

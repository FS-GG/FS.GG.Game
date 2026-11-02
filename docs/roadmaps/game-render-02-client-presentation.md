# GAME-RENDER-02.9: Guest-driven client presentation (design)

Status: **proposed** (design PR for FS.GG.Game#810). It is a companion to ADR 0001
(`docs/adr/0001-standard-input-pipeline.md`) and the glyph ADR (`docs/roadmaps/game-render-02-glyphs.md`).
ADR 0001 gets only a one-line pointer to this file, and only after #797, #798 and #799 merge. This PR does not edit it.

- **Route:** routine.
- **Parent Outcome:** FS-GG/.github#5218 (GAME-RENDER-02).
- **Built against these surfaces:**
  - ADR 0001 and its reducer as merged at `f712d08`;
  - #788's open PRs at their current heads: the input host #797 (`ce667980`), the kit #798 (`6e0b3e96`) and ReferenceGame end to end #799 (`d2dc96d2`);
  - the glyph library and theme tokens as merged at `0eadca0` (#802, #806, #807, #808);
  - the theme-machinery design in #805 (open).
- **Owners consulted:**
  - #788 (input pipeline): FS.GG.Game#788 issuecomment-6571720014;
  - #789 (glyph pass and drawing): #789 issuecomment-6571720476;
  - #795 (token derivation): #795 issuecomment-6571720865.

  Each decision below that depends on one of them is marked *proposed to #788*, *proposed to #789* or *proposed to
  #795* until that owner confirms it.
- **The working proof** is experimental and not shipped: `src/ReferenceGame/ExperimentalClientPresentation.fs` and
  `ExperimentalPlanGame.fs`. ReferenceGame is not packed.

## Problem

The owner's direction (2026-11-02) is that input flows **client input → WASM guest → server**, and also
**client input → WASM guest → client**. The client path changes GUI elements: the selection, and planning elements
such as arrows.

ADR 0001 already has the mechanism. A guest returns `GuestIntent.Local`, the runner performs it as `ApplyLocal`, and the
next guest call starts only after every `ApplyLocal` of the previous decision (rule 4, law P7b). ADR 0001 does not say
what a client-local change *is*, or how it is drawn. Each game would invent its own selection model, its own arrows, its
own link between an arrow and the command it became, and its own byte encoding across the WebAssembly boundary. The
template's unmarked `PreviewPath` (ADR 0001, Context) is what that leads to.

This design makes the client path first-class:

- a standard vocabulary for the `Local`s;
- its lifecycle;
- its link to pending commands;
- a default wire format;
- a place in the glyph layer's pass order, with theme tokens;
- accessibility rules and kit items.

## Prior art

| Source | What it shows | What this design takes |
|---|---|---|
| StarCraft II: Shift-queued waypoints and commands ([Blizzard game guide](https://news.blizzard.com/en-us/article/4552955/game-guide-special-control)) | Shift plus a move or attack order at several points queues waypoints, executed in order. | A plan can be a path of waypoints, and a command can carry a whole plan. A plan id names the draft being built. |
| Beyond All Reason: queued commands ([BAR wiki](https://www.beyondallreason.info/commands/queue-multiple-commands)); Spring engine custom-command draw data ([springrts forum](https://springrts.com/phpbb/viewtopic.php?p=545177), [Mantis #5841](https://springrts.com/mantis/view.php?id=5841)) | Shift queues move, attack, reclaim and repair. The Spring engine draws each queued command's line from per-command draw data (a colour and a flag). A custom command without draw data draws no line. | Styles are the game's choice of `move`, `attack` and `support`, drawn by the library. The game never supplies colours, so no style can be undrawn by omission. |
| Into the Breach: telegraphed enemy actions ([GameSpot review](https://gamespot.com/reviews/into-the-breach-review-a-mechanized-masterpiece/1900-6416865/), [Game Informer review](https://gameinformer.com/games/into_the_breach/b/pc/archive/2018/02/26/game-informer-review-into-the-breach)) | Every enemy attack for the next phase is shown during the player's turn, with arrows and tile highlights, and reviewers credit its readability to distinct silhouettes and clear attack ranges. | `targetLine`, `area` and the highlight roles. Previews are hover-driven and cheap to replace (CP4). |
| Unity Entities: system groups ([World.Update docs](https://docs.unity.com/en-us/engine/7000.0/script-reference/unity/entities/world/update)); Netcode for Entities: prediction ([manual](https://docs.unity3d.com/Packages/com.unity.netcode@1.3/manual/prediction-n4e.html)) | Simulation and presentation are separate system groups that run in order: initialization, then simulation, then presentation. Predicted ghosts are re-simulated from the server's state, separately from interpolated ones. | Three layers that never write each other: the authoritative state, the marked prediction (ADR 0001 stage 3) and the client presentation (CP2). Presentation reads simulation; nothing reads presentation back. |
| Overwatch: gameplay architecture and netcode ([GDC 2017 abstract](https://gdcvault.com/play/1024001/-Overwatch-Gameplay-Architecture-and)) | An ECS in which a small set of systems owns netcode. The networked simulation is deterministic and predictive, and server authoritative. | Presentation state stays out of the networked set: `ClientEffect` is never sent, and the guest's `Submit` is the only path to the server. |
| WCAG 2.2 SC 2.5.7 Dragging Movements ([Understanding 2.5.7](https://www.w3.org/WAI/WCAG22/Understanding/dragging.html)); `aria-grabbed` deprecated ([MDN](https://developer.mozilla.org/en-US/docs/Web/Accessibility/ARIA/Attributes/aria-grabbed)) | Anything done by dragging needs a single-pointer alternative, assessed separately from keyboard access. ARIA's drag-and-drop attributes are deprecated, so no drag semantics can be announced natively. | Every drag plan has a press-press alternative and a keyboard path (CP8). Plan changes are announced through a polite live region with a text summary, not through drag ARIA. |
| WAI-ARIA APG: listbox and grid patterns, as ADR 0001 §4 uses them | `aria-selected`, `aria-multiselectable` and `aria-activedescendant` on a single Tab stop. | `primary` maps to the existing single `aria-selected`. A `group` needs `aria-multiselectable` (Q3). |

The display details of StarCraft II's queue lines (whether they show without Shift held) were not found in a primary
source, so this design does not rely on them.

## Decisions

### CP1. The vocabulary: `ClientEffect` is the game's `'Local`

**`GuestIntent` gains no case.** Its union is closed (#796's union policy, Root's decision on #788), so a new case would
be a MAJOR change. The #788 owner asked for exactly this. A game that adopts the standard vocabulary instantiates the
pipeline with `'Local = ClientEffect<'Custom>`. The guest then returns `GuestIntent.Local (ClientEffect.plan …)`, and
every ADR 0001 guarantee holds unchanged:

- **Ordering (rule 4, P7b):** all of a decision's `ApplyLocal`s run before the next guest call starts.
- **Atomicity (rule 9, P11):** a decision that does not fit the command window applies none of its intents, so a
  `commit` without its `Submit` cannot happen.
- **Effect order (rule 10):** `ApplyLocal` and `Send` come in intent order.

A game with no use for the standard vocabulary keeps its own `'Local`.

The proposed library surface (proposed to #788), in `FS.GG.Game.Render`, file `ClientPresentation.fs/.fsi`, is opaque
and built by functions, so a new effect, element or role is additive:

```fsharp
[<Sealed>] type PlanId              // PlanId.create "move"; names a DRAFT (CP2)
[<Sealed>] type SelectionRole       // SelectionRole.primary | group
[<Sealed>] type HighlightRole       // HighlightRole.target | valid | invalid | named "..."
[<Sealed>] type PlanStyle           // PlanStyle.move | attack | support
[<Sealed>] type PlanAnchor          // PlanAnchor.point p | object id   (an object anchor follows that object's frame)
[<Sealed>] type PlanElement         // PlanElement.arrow from to style | path anchors style | ghost spec valid
                                    //   | area shape style | targetLine from to | label anchor text
[<Sealed>] type PlanSettle          // PlanSettle.clearWhenShown (default) | keepWhenShown
[<Sealed>] type ClientEffect<'Custom>
module ClientEffect =
    val select    : SelectionRole -> ids: string list -> ClientEffect<'Custom>   // set the role's members
    val add       : SelectionRole -> ids: string list -> ClientEffect<'Custom>
    val remove    : SelectionRole -> ids: string list -> ClientEffect<'Custom>
    val clearSelection  : ClientEffect<'Custom>
    val highlight       : id: string -> HighlightRole -> ClientEffect<'Custom>
    val unhighlight     : id: string -> HighlightRole -> ClientEffect<'Custom>
    val clearHighlights : ClientEffect<'Custom>
    val plan      : PlanId -> PlanElement list -> ClientEffect<'Custom>          // replace the draft's elements
    val commit    : PlanId -> ClientEffect<'Custom>                              // = commitWith clearWhenShown
    val commitWith: PlanSettle -> PlanId -> ClientEffect<'Custom>
    val clearPlan : PlanId -> ClientEffect<'Custom>
    val clearPlans: ClientEffect<'Custom>
    val custom    : 'Custom -> ClientEffect<'Custom>                             // the game's escape hatch
[<Sealed>] type PlanPhase           // predicates: isDraft, isCommitted, isOpen, isApplied, isDone, isFailed ...
[<Sealed>] type PlanView            // id, elements, phase, command: CommandId option
[<Sealed>] type ClientPresentation  // the reducer's state; the game keeps it beside its selection
module ClientPresentation =
    val empty     : ClientPresentation
    val apply     : ClientEffect<'Custom> -> ClientPresentation -> ClientPresentation * 'Custom list
    val sent      : planOf: ('Command -> PlanId option) -> CommandEnvelope<'Command> -> ClientPresentation -> ClientPresentation
    val failed    : PipelineFailure -> ClientPresentation -> ClientPresentation
    val track     : PipelineModel<'Input, 'Server, ClientEffect<'Custom>, 'Command> -> ClientPresentation -> ClientPresentation
    val primary   : ClientPresentation -> string option
    val group     : ClientPresentation -> string list
    val highlights: ClientPresentation -> (string * HighlightRole) list
    val plans     : ClientPresentation -> PlanView list                          // creation order
    val describe  : label: (string -> string) -> PlanView -> string              // the text summary (CP8)
    val drain     : ClientPresentation -> ClientPresentation * string list       // announcements, oldest first
```

- **Selection.** `primary` holds at most one object; a request for two changes nothing and records a diagnostic.
  `group` is an ordered set. `primary` is what `InteractiveScene.Selected` shows, so the input host's mirror keeps its
  single `aria-selected` (CP8).
- **Highlights** are roles on objects (`target`, `valid`, `invalid`, or a game's `named` role). The glyph layer draws
  them in the plan pass.
- **Plans** are keyed by `PlanId`. A plan is a list of elements, so an attack can be a path plus a target line.
- **`custom`** is the escape hatch. `apply` returns the game's own effects in order, and the game applies them. The
  proof's drag arm and disarm are custom effects.

### CP2. Lifecycle and semantics

**Three layers, never mixed** (prior art: Unity's simulation and presentation groups):

| Layer | Owner | Survives a backend switch | Read by |
|---|---|---|---|
| Authoritative state | the server | yes (game state) | everything |
| Prediction, drawn marked | `CommandStage.withPrediction` (ADR 0001 stage 3; #799 draws it marked) | yes (derived) | the view |
| Client presentation: selection, highlights, plans | `ClientPresentation`, applied from the guest's `Local`s | yes (client state, like the selection) | the view and the guest's `client ()` |

- `ClientEffect` is never sent. The only route to the server is `Submit`.
- `predict` never reads the presentation, and the presentation never changes `Displayed`.
- Focus and hover stay input-host cues: they do not survive a switch (ADR 0001 Q5). Plans and the selection do.

**A plan's life:**

```text
 plan p        commit p        Send (planOf = p)       open answers             final answer
 ───────▶ Draft ───────▶ Committed ───────▶ Pending ─▶ Accepted/Queued/Running ─┬─▶ Applied ─▶ shown ─▶ cleared (clearWhenShown)
   ▲ replace   │                                                                │                    └▶ Done   (keepWhenShown)
   └───────────┘                                                                ├─▶ Refused / Cancelled ──▶ kept until clearPlan
                                                                                └─▶ Unconfirmed ─────────▶ kept until clearPlan
```

- **A `PlanId` names a draft.** `plan p` replaces the elements of the draft `p`, or creates it. On `commit p` the plan
  leaves the draft name: it follows its command, and `p` is free for the next draft. A guest drafting the next move
  therefore never needs a counter, while the previous arrow is still on its way.
- **`plan p` is idempotent.** The same effect twice gives the same presentation as once (law CL2), so coalescing loses
  nothing: a coalesced hover preview draws exactly what the latest one would.
- **A new `plan p` replaces a refused, cancelled, unconfirmed or done plan of the same id.** It is the guest's
  acknowledgement that the player has seen the outcome.
- **Clearing.** `clearPlan p` and `clearPlans` remove drafts, unsent commits and settled plans. They **never** remove a
  plan that follows an open or applied command: that overlay is a view of a command, and ends when the command does.
  Cancelling a sent command is a game command (RuneScape's cancel, for example), not a client effect.
- **Escape.** It is a `Cancel` input that the guest decides (rule 8). The reference convention is that Cancel clears
  drafts and settled plans when there are any, and otherwise clears the selection. A plan that follows a sent command
  stays (the proof's tests pin this).
- **`Rebind`** keeps every plan. A linked plan keeps following its `CommandId`. Its command settles by state and
  patience, as P6 says for an earlier binding's commands (the proof's in-process test pins this).
- **Partial failure.**
  - A decision refused as `CommandWindowFull` applies nothing, so its `commit` does not apply and the plan stays a
    draft. The player can submit it again once the window frees (pinned by a proof test).
  - A guest that faults or times out decides nothing, so no effect is applied.
  - An exception in the game's `applyLocal` is routed to `io.Fail` as `effectFailed`, and the rest of the batch runs,
    as #788 decided ("Hosts never wedge"). The presentation therefore never sees half an effect.

### CP3. A plan linked to its pending command

The guest cannot know the `CommandId`: the pipeline issues it when the decision ends. The link is therefore made on the
client, from what the command carries:

1. **The command carries its plan id.** The game gives `planOf : 'Command -> PlanId option`. This is the same pattern as
   ADR 0001's rule that the game carries the `CommandId` in its own envelope.
2. **`ClientPresentation.sent planOf envelope`**, called from the game's `io.send`, links the oldest committed plan with
   that id to `CommandEnvelope.id`. If there is none, it links the draft of that id, which covers a `PipelineMsg.Submit`
   with no guest decision. The phase becomes `Pending`. The link never depends on two effects being adjacent.
3. **`ClientPresentation.failed failure`**, from `io.fail`, finds the command through `PipelineFailure.command`.
   `commandRejected`, `commandCancelled` and `commandUnconfirmed` end the plan as `Refused`, `Cancelled` or
   `Unconfirmed`. The first final outcome wins (P18b).
4. **`ClientPresentation.track model`** runs after each batch.
   - A linked command in `InputPipeline.unsettled` takes its open phase from the settlement: `isQueued` → Queued,
     `isRunning` → Running, `isAccepted` → Accepted, otherwise Pending.
   - One that left `unsettled` but is still in `Displayed.predicted` is `Applied`, not yet shown.
   - One that left both with no routed failure was applied, and the authoritative state shows it. Its plan is cleared,
     or kept as `Done` under `keepWhenShown`.

**Request (a), proposed to #788: `InputPipeline.settlement : CommandId -> PipelineModel -> CommandSettlement option`**, an
additive accessor for a command still held in the ledger.

- **Why it is needed.** Today `unsettled` lists open commands only. `track` therefore *infers* "applied" from a command
  that left `unsettled` with no `Fail`. That is sound only when `track` never runs between an update and the `Fail`s of
  the same batch.
- **Why it is safe today.** `PipelineHost` and `PipelineRunner` set the model before performing a batch, and a command
  failure never shares a batch with `ApplyLocal` or `Send`. Failures come from `Acknowledged`, `Authoritative` and
  `Heartbeat` alone.
- **Prediction.** Without prediction, an `applied` answer that arrives before its `Authoritative` state would clear the
  arrow one message early. The accessor removes both caveats.
- **Default without it:** the inference above, which the proof uses.

**Plans anchor at authoritative positions.** A plan's object anchors resolve against the bounds the game passes to the
plan pass. A game that predicts a planned move passes the **authoritative** frame's bounds for plans. Otherwise the
predicted unit would drag its own arrow's start to the destination (Q5). The proof does not predict `MoveTo`.

### CP4. Hover previews and continuous input

**Hover previews follow ADR 0001 rule 2.** A guest that previews on `Hover` returns `plan p [...]` for the hovered
target. `Hover` is coalesced: a newer one replaces the waiting one at the tail. Because `plan p` replaces the elements
(CP2), the coalesced result is exactly the latest preview.

**Latency bound.** A preview reflects the latest hover within at most two guest calls: the one in flight, then the
latest waiting one.

- A sync guest draws it in the same task as the event, plus the present.
- An async or WebAssembly guest is bounded by two call deadlines (2 × 500 ms by default, ADR 0001 Q4), and in practice
  by two call durations.
- #799's input-to-command latency is the measured base.

**Continuous input.** ADR 0001 §1 states that "continuous input (drag, wheel, press and release) arrives as new cases in
a major version".

**Request (b), proposed to #788: in the next MAJOR, `InputAction.DragStart`, `DragMove` and `DragEnd`, each with a
scene point.**
- `DragMove` is coalesced under rule 2, like `Hover`.
- A drag starts after **4 CSS px** of travel, so a click stays one `Activate`.
- The press keeps reporting `Activate` first, so a guest arms on it, as the proof does.
- **Until then:** the game's own listener feeds `PipelineInput.Game (DragTo p | Release (p, dragged))`, coalesced to one
  move per animation frame, because rule 2 never coalesces `Game` inputs. The proof does exactly this, marked
  experimental.

### CP5. Wire format, with golden vectors

The codec stays the game's (ADR 0001 stage 2: `ofBytes` takes the game's `encode` and `decode`). The library offers a
**standard default codec** for `GuestIntent<ClientEffect<'Custom>, 'Command> list`, proposed as `ClientEffectCodec` in
`FS.GG.Game.Render` (proposed to #788). The game supplies the codecs for its own `'Command` and `'Custom`.

**Layout, version 1, little-endian:**

```text
decision := u8 version (1) · u16 count · intent*count            ; nothing may follow
intent   := 0x00 effect | 0x01 u32 len · command[len]            ; len <= 65536
effect   := 0x01 role ids | 0x02 role ids | 0x03 role ids | 0x04          ; select add remove clearSelection
          | 0x10 str hrole | 0x11 str hrole | 0x12                        ; highlight unhighlight clearHighlights
          | 0x20 str(plan) u16 n · element*n                              ; plan
          | 0x21 str(plan) u8 settle | 0x22 str(plan) | 0x23              ; commit clearPlan clearPlans
          | 0x7F u32 len · custom[len]                                    ; custom (len <= 65536)
element  := 0x01 anchor anchor style                                      ; arrow
          | 0x02 u16 n (n >= 2) · anchor*n · style                        ; path
          | 0x03 u8 family · u8 identity · text(icon) · u8 hasTeam [u16 team] · point · u16 w · u16 h · u8 valid  ; ghost
          | 0x04 (0x00 point fixed(r>0) | 0x01 fixed fixed fixed(w>=0) fixed(h>=0)) · style                      ; area
          | 0x05 anchor anchor | 0x06 anchor text                         ; targetLine label
anchor   := 0x00 point | 0x01 str
point    := fixed fixed
fixed    := i32, scene units x 256 (Q24.8): Floor(v x 256 + 0.5), |v x 256| <= 2147483392
ids      := u16 n · str*n                                         ; n <= 1024 (every list)
str      := u16 len (1..1024) · UTF-8      text := u16 len (0..1024) · UTF-8
role 0 primary 1 group · hrole 0 target 1 valid 2 invalid · style 0 move 1 attack 2 support
settle 0 clearWhenShown 1 keepWhenShown · family 0 unit 1 structure 2 terrain 3 item 4 marker
identity 0 own 1 ally 2 neutral 3 enemy 4 unknown
```

- **Why fixed point:** integers are identical in Rust, C, .NET and JavaScript, with no NaN, no negative zero and no
  rounding mode. 1/256 of a scene unit is far below a CSS pixel at any supported scale.
- **Strict and canonical.** The decoder refuses:
  - another version, an unknown tag or kind, and a truncated or trailing byte;
  - empty ids, invalid UTF-8, an out-of-range count, length or coordinate;
  - a path of fewer than two waypoints, a ghost of no footprint, and a non-boolean flag.

  Every encoding it accepts re-encodes to the same bytes (the fuzz test pins this). ADR 0001 maps a decode `Error` to
  `Faulted`, which halts the stage. A guest that writes an effect its host cannot read is broken, not degraded, and the
  version byte lets a host refuse a newer guest at once.
- **The ghost on the wire** is a subset of `GlyphSpec`: family, identity, icon, team, anchor and footprint in cells.
  `GlyphSpec` is opaque with no read accessors, so the library codec needs `GlyphSpec.family`, `anchor`, `footprint`,
  `identity`, `icon` and `team` readers. These are additive, and are proposed to #789 (Q6). The default if #789
  declines is that a ghost element holds this subset record, and `toGlyphSpec` builds the spec for drawing.

**Golden vectors** (all 23 are in `tests/Game.Render.Tests/ClientPresentationTests.fs`). The game's command codec in
the vectors is `u8 len · plan · i32 x · i32 y`, and its custom codec is `Arm = 00`, `Disarm = 01`:

| Decision | Bytes |
|---|---|
| `[]` | `010000` |
| `[Local (select primary ["token"])]` | `01010000010001000500746f6b656e` |
| `[Local (plan "move" [arrow (object "token") (point 156 60) move])]` | `010100002004006d6f7665010001010500746f6b656e00009c0000003c000000` |
| `[Local (commit "move"); Submit (MoveTo ("move", 156, 60))]`, the Enter decision | `010200002104006d6f766500010d000000046d6f76659c0000003c000000` |
| `[Local (plan "g" [ghost structure enemy "structure" team 2 at (48, 16), 2 x 1, invalid])]` | `01010000200100670100030103090073747275637475726501020000300000001000000200010000` |
| `[Local (plan "x" [arrow (point 8388607 -8388607) (point 1/256 -1/256) move])]`, the coordinate extremes | `01010000200100780100010000ffff7f000100800001000000ffffffff00` |
| `[Local (custom Arm)]` | `010100007f0100000000` |

The Enter decision and the custom vector were worked out by hand from the layout before the encoder produced them.
The rest were produced by the encoder, checked against the layout, and are pinned.

- **Local correspondence**, as ADR 0001 §7 says: a recording F# stand-in answers from the vectors through the same
  `ofBytes` codec.
- **In CI**, a WebAssembly guest of the same rule must produce the same bytes. Building one is the template migration's
  job (ADR 0001 §8), not this Delivery's.

### CP6. Drawing: the plan pass

**Pass order** (proposed to #789, which owns the pass order):

```text
amplifiers → bodies → PLANS → selection brackets → (cue layer: hover, then focus)
```

- **Above bodies.** An arrow crosses units, and a placement ghost may sit on an occupied cell, so a plan must read over
  bodies.
- **Below brackets and cues.** Selection and focus are state and accessibility markers, and D10 keeps them on top. No
  plan ever hides them.
- **D10's guarantee is unchanged.** Amplifiers still draw first, so no amplifier covers a frame. The added guarantee is
  that **no plan covers a bracket or a cue**.
- **In the glyph scene, not in the cue layer.**
  - #797 snaps top-level `Rect` nodes in the input host's cue layer to whole device pixels. Plan dots are plain
    rectangles, so drawing them as cues would move them.
  - Plans are not input-host state: they survive a backend switch, and cues do not.
  - The #788 owner raised this, and the #789 owner is asked to confirm.

**Surface** (proposed to #789, additive on its opaque types):

- `Glyph.plan : bounds: (string -> Rect option) -> PlanView -> Glyph`. It draws only in the plan pass. An object
  anchor ends at that object's frame plus the theme's gap, so an arrow stops short of the body it points at.
- `GlyphDrawing.plan`: the new pass. `GlyphDrawing.nodes` returns amplifiers, body, plan, brackets.
- `Glyphs.scene` draws every amplifier, then every body, then every plan, then every bracket.
- **Highlights** draw in the plan pass as a ring of dots in the role's ink around the object's frame, outside the cue
  reserve.

**The portable profile** (opaque rectangles only, as `Glyph.path`, so `ScenePixels` decides every pixel):

- **The shaft** is sampled every dot (`max(band, 2 / minCssPerUnit)` units). Each dot sits on a one-unit
  `plan.keyline`, so it reads over any body or team colour (the E1 and E7 lesson: check contrast against what a mark
  touches).
- **The head is the style, never the hue alone** (D12 rule 1):

  | Style | Head |
  |---|---|
  | Move | a chevron of three dots |
  | Attack | a reticle of four dots around the tip |
  | Support | a plus |
  | Refused, cancelled or unconfirmed | a cross in `plan.invalid` |

- **The phase is the dot pattern:**

  | Phase | Pattern |
  |---|---|
  | Draft or committed | every other sample |
  | Submitted (pending, accepted, queued or running) | two samples on, one off |
  | Applied, or done | continuous |
  | Failed | every other sample, in `plan.invalid`, with the cross head |

  So the monochrome render tells every style and phase apart.
- **A ghost** is the `GlyphSpec`'s frame drawn hollow, as dots on a keyline:
  - **valid:** in its identity colour (the proof uses `plan.move`);
  - **invalid:** in `plan.invalid`, with the cross at the anchor.
- **An area** is its outline in dots. A target line is an attack arrow.
- **A label** is a marker dot in the portable profile, and its text goes to the summary (CP8). Text is drawn in the
  stroked profile only (Q7).
- **SVG and Pixi are equivalent** (the owner's binding requirement, Root on FS-GG/.github#5218, 2026-11-02: SVG must
  stand in for Pixi in documentation). Every plan element is built from opaque, whole-unit rectangles in the portable
  profile, so SVG and Pixi decide the same pixels.
  - **Doc-safe set:** arrows, paths, ghosts, areas, target lines, highlights and the label *marker* are all in the
    portable, doc-safe set. A label's *text* is not portable (Q7). It is `docSafe = false`, and its static equivalent
    is the marker plus the summary text.
  - **Tolerance:** 2 per channel on every decided pixel, the kit's existing SVG and Pixi tolerance.
  - **Checking:** the proof measures it (K9). Game#814 adds the kit's standing equivalence section and the doc export.
- **Babylon draws plans unlit** (#786) through the same fill path as glyphs. Until #786 lands, the kit asserts the
  declared colours on SVG and Pixi, and the 0.75 lit factor on Babylon, as the glyph kit does.

### CP7. Theme tokens

The aliases below are proposed to #795 for derivation, and to #789/#802 for the alias layer. They are added **after #805
settles the 35 colour aliases**, so neither count moves now.

| Alias | Drawn as | Proposed derivation (#795 decides) | Validation |
|---|---|---|---|
| `plan.move` | move arrows and paths; valid ghosts | the `glyph.path` ramp step | ≥ `contrastTarget` on the ground; ≥ 3:1 on `plan.keyline` |
| `plan.attack` | attack arrows, target lines | a `status.bad`-hue step, distinct from `plan.invalid` | same |
| `plan.support` | support arrows | an `allegiance.ally`-hue step | same |
| `plan.invalid` | failed plans, invalid ghosts, the `invalid` highlight | a `status.warn` or `status.bad` step | same |
| `plan.keyline` | the keyline under every plan mark | the pole opposite the plan inks, as `status.keyline` | the separator; not held to the ground |

- **Distinctness:** the four style inks differ pairwise at the status-level ΔE2000 threshold, normal and under CVD (#795's
  checks).
- **Shape redundancy** is required whatever the inks are (CP6). Sizes reuse `band` and `gap`.
- **The proof** uses fixed inks with no theme, each at least 7:1 on its keyline (a test pins this), because the
  glyph library is not on the #799 stack it sits on.

### CP8. Accessibility

- **Selection in the mirror.** `primary` maps to `InteractiveScene.Selected`, so the input host's `aria-selected` reflects
  the guest's selection without a server round trip. The proof's smoke checks this on all three backends.
  - A `group` role needs `aria-multiselectable` on the listbox or grid and a selected *set* in `InteractiveScene`. That
    is a host change, proposed to #788 (Q3).
  - Until then, group members are listed in the summary.
- **Announcements.** Every phase change of a plan is announced once in a **polite** live region (`role=status`,
  `aria-live=polite`): drafted, sent, done, refused, cancelled, unconfirmed and cleared.
  - **A drag never announces per move.** `plan p` replacing a draft is silent.
  - **Hosting:** the input host already keeps a polite status in its mirror. The library writes `drain`'s output there,
    proposed to #788. The proof uses its own region.
- **Text summary.** `describe label plan` gives one line per plan, for example "Move Player token to (156, 60), sent".
  The surface references the summary list with `aria-describedby`.
- **Single-pointer alternative (WCAG 2.5.7).** Every plan a drag can make, a press-press can make. The proof's guest
  shows this: press the unit, then press the ground, to draft the same arrow (pinned in process).
  - **Keyboard path:** select with the arrows and Enter, retarget with the arrows over cells (`ObjectAccess.cell`), and
    submit with Enter.
- **Contrast and colour.**
  - Plan inks meet 3:1 (WCAG 1.4.11) on the ground and on their keyline.
  - Style and phase never rest on colour alone (WCAG 1.4.1), so the monochrome theme works.
- **No motion.** Plans are static drawings; a phase change redraws them. The design adds no animation, so no
  reduced-motion handling is needed.

### CP9. Kit items and laws

**Kit items** for a new `presentation` section on all three backends, run with real input, proposed for the
implementation PR:

| Id | Requirement |
|---|---|
| K1 | A guest `Local` changes the selection: the brackets are drawn and `aria-selected` is on the item, with no `Send` and no authority message. |
| K2 | An arrow overlay is drawn: a shaft sample's pixel shows the style's ink, and the frame differs from the no-plan frame. Removing it gives back exactly the no-plan frame. |
| K3 | A plan survives a backend switch: the presentation is equal, and the new backend draws the same decided pixels. |
| K4 | A hover preview is coalesced. While an async guest call is held, a burst of N hovers causes at most two more calls, and the final preview is the last hover's. |
| K5 | An overlay linked to a pending command: drawn in the submitted pattern while open, cleared once the authoritative state shows it (`clearWhenShown`), kept as done (`keepWhenShown`), and kept in the invalid ink when refused. |
| K6 | Pass order: where a plan crosses a body, the plan's pixel shows. Where it meets a selection bracket or the focus ring, the bracket or ring shows. |
| K7 | Exactly one polite announcement per phase change, and none per drag move. The summary names every plan. |
| K8 | Escape through the reference guest clears drafts and settled plans, never a plan that follows a sent command. |
| K9 | **SVG and Pixi equivalence** (Root's binding requirement, #5218). A scene with every plan element kind and every phase is drawn on SVG and on Pixi. On every decided pixel, the two agree, and each agrees with the scene's decided colour, within 2 per channel. Game#814's standing equivalence section runs it with the rest of the portable set. |

**Laws** (in process, on .NET and on emitted Fable with equal counts, like P1–P20):

| Id | Law |
|---|---|
| CL1 | `apply` is total and deterministic for every effect sequence. |
| CL2 | `plan p e` twice equals once. `[plan p e1; plan p e2]` draws as `plan p e2` (coalescing safety). |
| CL3 | At most one draft and one unsent commit per id. A `CommandId` shows at most one plan. An unlinked plan has no command and a linked one has one. Plan orders are unique. |
| CL4 | `clearPlan` and `clearPlans` never remove an open or applied linked plan. |
| CL5 | The first final outcome wins. `track` never reopens a failed plan. |
| CL6 | `track` maps open settlements to Pending, Accepted, Queued or Running, a predicted command to Applied, and a shown one to cleared or Done. |
| CL7 | The codec round-trips every decision in range and refuses every strict prefix, every trailing byte and every malformed tag. Any accepted encoding re-encodes to the same bytes. Golden vectors are pinned. |
| CL8 | A decision refused `CommandWindowFull` leaves its draft a draft. A `Rebind` keeps every plan, and a linked plan still settles. |

## The proof (experimental)

The proof is **not shipped**. ReferenceGame is not packed, and every file and test is marked experimental with
FS.GG.Game#810.

- **`src/ReferenceGame/ExperimentalClientPresentation.fs`** holds:
  - the vocabulary as plain unions;
  - the reducer: `apply`, `sent`, `failed`, `track`, `drain` and `describe`;
  - the default codec;
  - the plan pass in the portable profile.
- **`src/ReferenceGame/ExperimentalPlanGame.fs`** is ReferenceGame with a planned move:
  - an F# `InputGuest.sync`;
  - a command stage with no prediction;
  - an authority that admits with Game.Core's `NetworkAdmission` and refuses a move into column 0, the wall;
  - the renderers: ReferenceGame's Scene, then the plan pass;
  - a `Session` wiring `PipelineHost` to the authority.

**What it does:**

1. A pointer press on the token decides `[select primary token; custom Arm]`.
2. The game-owned listener feeds `DragTo`, one per animation frame. Each one decides `[plan move [arrow token → p]]`.
3. The release decides the final arrow and `custom Disarm`. A release without a drag only disarms.
4. Enter on the token decides `[commit move; Submit (MoveTo ("move", x, y))]`.
5. The arrow is drawn submitted while the command is pending. The network is held, so the pending state is observable.
6. When the authority's snapshot shows the token at the new column, the arrow clears.
7. A move into the wall is refused. The plan stays, drawn in the invalid ink with the cross, and survives a backend
   switch. Escape clears it.

**Evidence** (SDK 10.0.403, this PR's head): see the PR body for the counts the generators printed.

- `tests/Game.Render.Tests`, `ExperimentalClientPresentation`:
  - the 23 golden vectors;
  - fuzz properties over the codec's whole accepted range;
  - the CL laws and the plan-pass checks;
  - the planned move in process.
- The browser smoke (`scripts/test-render-browser-host.sh`) runs the planned move with **real** Playwright mouse and
  keyboard input on SVG, Pixi and Babylon:
  - the draft, aria-selected, draft pattern, pending pattern, settlement, refusal, Escape and announcement checks on
    each backend;
  - the backend-switch check on the SVG run;
  - the SVG-vs-Pixi equivalence check (K9). It covers every element kind (arrow, path, ghost, circle and box areas,
    target line and label marker) in the draft, pending, applied and refused phases, on every decided pixel.

## Implementation plan (after acceptance and #797–#799 merging)

1. **#788 owner:**
   - the one-line pointer in ADR 0001;
   - `ClientPresentation.fs/.fsi` and `ClientEffectCodec`, from this proof, with opaque types and the CL laws on .NET
     and Fable;
   - the additive `InputPipeline.settlement` (request a);
   - the polite status hosting (CP8).
2. **#789 owner:** the plan pass (`Glyph.plan`, `GlyphDrawing.plan`, the `Glyphs.scene` order), highlights, ghosts,
   and the `GlyphSpec` readers.
3. **#795 owner:** the five `plan.*` aliases and their checks, after #805.
4. **The kit's `presentation` section, K1–K9,** on all three backends, with K9 joining Game#814's equivalence section. Then delete this experimental module and
   point ReferenceGame at the library.
5. **The next MAJOR of the input vocabulary:** `DragStart`, `DragMove` and `DragEnd` (request b). The proof's
   listener goes away then.
6. **Consumers:** the template migration first, as ADR 0001 §8 says (its `PreviewPath` becomes a plan, and its
   WebAssembly guest emits the default codec), then SC2, FSBarV2 and RuneScape.

## Open questions, with recommended defaults

Work proceeds on the default unless the owner chooses otherwise.

| # | Question | Recommended default |
|---|---|---|
| Q1 | Where does the plan pass sit? | Amplifiers, bodies, **plans**, brackets, then cues, in the glyph scene and not the cue layer (CP6). Proposed to #789. |
| Q2 | Should the vocabulary be closed unions, as in the proof, or opaque? | Opaque, built by functions, so effects, elements and roles are additive (CP1). A game rarely matches them: the library interprets them. |
| Q3 | How does a `group` selection reach ARIA? | The next input-host change adds a selected set to `InteractiveScene` and `aria-multiselectable`. Until then, the group is in the summary. Proposed to #788. |
| Q4 | Does a refused plan linger on a timer? | No. It stays until the guest clears or replaces it, so the player always perceives it, with no time limit and no wall clock in the reducer. |
| Q5 | What does a plan anchor to when the game predicts the planned effect? | The authoritative frame's bounds (CP3). The predicted unit is drawn marked by #799's rule. |
| Q6 | How does a ghost cross the wire with an opaque `GlyphSpec`? | Additive read accessors on `GlyphSpec` from #789. Otherwise, a ghost subset record with `toGlyphSpec`. |
| Q7 | How are labels drawn? | A marker in the portable profile, with the text in the summary. Text in the stroked profile once #773 lands. |
| Q8 | How is the link to a command made? | `planOf` on the command (CP3), not effect adjacency. It survives `PipelineMsg.Submit`, rebinds and a game that interprets effects itself. |
| Q9 | Is there one draft per id, or many? | One draft per id, and a committed plan leaves the id (CP2). Multiple simultaneous drafts use several ids. |
| Q10 | Does the default codec live in `FS.GG.Game.Render` or a WebAssembly package? | In `FS.GG.Game.Render`, because it is plain bytes with no Wasm dependency. ADR 0001 Q3's `FS.GG.Game.Render.Wasm` would host the `invoke` bridge, not the codec. |
| Q11 | Should continuous input be added before the next major? | No. `InputAction` is closed, so the game-owned listener with `Game` inputs is the bridge (CP4), and it is retired at the major. |

## Alternatives considered

- **A `GuestIntent.Client of ClientEffect` case.** It breaks the closed union (a MAJOR change) for no gain over `'Local`,
  which already carries the ordering guarantee. Rejected at the #788 owner's request.
- **Linking a plan to the command by effect adjacency** (a `commit` binds the next `Send` of its batch). This works under
  `PipelineRunner`, but breaks for `PipelineMsg.Submit`, which has no decision, and for a game that reorders or
  interprets effects itself. Rejected for `planOf`.
- **Drawing plans in the cue layer.** #797 snaps the cue layer's rectangles to device pixels, and cues do not survive a
  backend switch. Rejected for the plan pass.
- **Plans as game state, sent to the server.** RTS planning UIs (StarCraft II, Beyond All Reason) keep drafts on the
  client and send only the order. Sending drafts would add latency and server load for what is presentation. Rejected.
- **A timed "refused" flash.** It needs a wall clock in the reducer, and players who look away miss it. Rejected for
  Q4's default.
- **Floating-point coordinates on the wire.** NaN, negative zero and differences between runtimes would need rules of
  their own. Rejected for Q24.8 fixed point.

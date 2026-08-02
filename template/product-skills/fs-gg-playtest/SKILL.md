---
name: fs-gg-playtest
description: Prove gameplay headlessly at the right evidence level — use fast Playable simulation proofs for components and runner-issued production journeys for user-facing boot-to-outcome claims.
---

# Headless Playtest (Gameplay Evidence) Capability

## Scope

Use this skill to prove a gameplay requirement with evidence at the right boundary.
`Playable`/`Driver` remains the fast simulation/component route for laws, balance, properties,
workloads, witness search, and helper correctness. A user-facing start, control, progression, or
terminal-outcome claim instead requires a `ProductionJourney`: boot the shipped composition root and
drive timestamp-free host events through its real mapping, dispatch, update, fixed-tick, and
deterministic effect-result seams.

This is the test-time companion to the simulation half ([[fs-gg-game:fs-gg-game-core]]): that skill
teaches how the world *steps*; this one teaches how you *prove it plays correctly*. It materializes for
the `game` and `sample-pack` profiles. The harness reaches up to nothing beyond `FS.GG.Game.Core` and
the BCL — it performs no I/O and reads no wall clock, which is what lets its evidence be deterministic.

## Evidence boundary: component versus production journey

Direct helper calls, a `Playable` with an overwritten `Init`, and command-level traces are valuable
`simulation-input` evidence. They do not prove that a player can boot or reach that state. Never
describe them as complete end-to-end acceptance evidence.

For `production-journey` coverage:

1. Build `ProductionJourney<...>` beside the product composition root. `Boot` must be the shipped
   boot/init function; `MapEvent`, `Update`, `FixedTick`, and `ApplyEffectResult` must reference the
   production functions rather than test adapters. A caller-supplied model or boot closure, even one
   wrapped by a product-side factory, fails the composition-authority check — see "Owning the whole
   entry point" below for the pattern that satisfies it.
2. Drive `JourneyEvent.Start`, menu actions, key up/down, pointer/aim, `Interact`, pause/resume,
   `FixedTick`, and declared deterministic `EffectResult` values as the scenario needs.
3. Set a positive `MaxSteps` and a terminal predicate. Exhaustion is a failed receipt containing the
   final-fingerprint and captured-input digests. A terminal predicate already true at boot also
   fails: zero production events are not a journey.
4. Replay a scripted or seeded-policy capture and compare `Trace.frames`. Exportable proofs call
   `Journey.runScriptWithIdentity` or `Journey.runPolicyWithIdentity` with stable non-empty input and
   terminal-predicate identities; the legacy runners remain replay helpers, not schema-v1 issuers.
5. Add a public parameterless `IProductionJourneyProofV1` implementation to the product's proof
   assembly. Its route/scenario/input/terminal declarations are matched to the opaque receipt after
   `Run` executes the imported production composition. Pass that DLL to `fsgg-playtest` with
   `--journey-proof-assembly` and explicitly allowlist the producer composition DLL with
   `--journey-authority-assembly`; critical composition functions, proof metadata, and the opaque
   receipt must identify that exact assembly name/module version. The CLI accepts no JSON receipt or
   caller key. For `emit-evidence`, pass `--journey-report-out <junit.xml>`: this is generated output
   from the same in-memory proof execution, never a caller report input, and it must resolve to a
   different canonical path than `--out`. The manifest row must say
   `requires=production-journey`. A hand-authored `productionJourney` token, simulation trace,
   stale/modified receipt, or caller-supplied TRX/JUnit fails closed.

An unbound displayed action must return `JourneyDispatch.Unbound "Action name"`. The runner then
reports the production wiring gap instead of silently treating the event as a no-op.

**`'menu` is the product's general displayed-action vocabulary, not a menu-only slot.** Instantiate it
with every UI-triggered action your product exposes — every button, HUD control, and dialog choice, not
only literal pause-menu entries. A product that instantiates `'menu` with `unit` (or any other
single-inhabitant type) can never construct more than one distinct `MenuAction` value, so no script,
however written, can ever reach a second `MapEvent` arm for it — including a catch-all `Unbound` arm
guarding an action nobody wired. That shape shipped unplayable for ten milestones behind 100%-green
suites (`2026-08-01-Rogue3-12.md` §4.1): `TraverseDoor` and `UnlockDoor` were implemented and
unit-tested, but no production input route ever dispatched them, and journey coverage — defined
entirely over the messages a script issues — was structurally blind to the gap between the message
set and the player.

### Closing the blind spot: `Journey.checkActionCoverage`

Journey coverage measured only over what committed scripts issue cannot see an `Unbound` arm no script
ever reaches, and it cannot see a single-inhabitant vocabulary slot make that arm unreachable by
construction regardless of what scripts exist. `Journey.checkActionCoverage` names both:

```fsharp
open FS.GG.Game.Core
open FS.GG.Game.Harness

type Model = { Started: bool }
type Menu =
    | NewGame
    | Settings
    | Quit

// A minimal adapter: Quit is deliberately left Unbound to show what the check catches.
let adapter: ProductionJourney<Model, unit, unit, Menu, unit, string, Model> =
    { RouteId = "doc-example"
      ScenarioId = "menu-coverage"
      TestId = "GP-DOC-001"
      MaxSteps = 8
      Boot = fun () -> { Started = false }
      MapEvent =
        fun event _ ->
            match event with
            | JourneyEvent.Start
            | JourneyEvent.MenuAction NewGame -> JourneyDispatch.Mapped [ "start" ]
            | JourneyEvent.MenuAction Settings -> JourneyDispatch.Mapped [ "open-settings" ]
            | JourneyEvent.MenuAction Quit -> JourneyDispatch.Unbound "quit"
            | _ -> JourneyDispatch.Mapped []
      Update = fun _ model -> model
      FixedTick = id
      ApplyEffectResult = fun _ model -> model
      IsTerminal = fun _ -> false
      Fingerprint = id
      EncodeEvent = sprintf "%A"
      EncodeFingerprint = sprintf "%A" }

// Every value your product's own menu-action type can hold — the honest, exhaustive declaration
// this check depends on.
let vocabulary =
    [ JourneyEvent.MenuAction NewGame
      JourneyEvent.MenuAction Settings
      JourneyEvent.MenuAction Quit ]

// Every script your committed suite actually runs.
let committedScripts = [ [ JourneyEvent.Start ]; [ JourneyEvent.MenuAction Settings ] ]

let report = Journey.checkActionCoverage adapter vocabulary committedScripts
let clean: bool = ActionCoverageReport.isClean report // false — Quit is Unbound and no script issues it
let reasons: string list = ActionCoverageReport.describe report // one line naming the unreached action
```

It reports two kinds of gap, both members of `ActionCoverageGap`:

- `UnexercisedUnbound(action, event)` — `MapEvent` returns `Unbound action` for `event`, and `event`
  never appears in `committedScripts`. Nothing in the suite can ever surface this arm firing or failing
  to fire.
- `DegenerateVocabulary(slot, inhabitants)` — the declared `vocabulary` supplies only one distinct
  producible value at `"menu"`, `"key"`, or `"pointer"`. This is the Rogue3 shape exactly: a hidden
  `Unbound` catch-all behind a single-inhabitant type is dead code that no script can ever reach, and
  `UnexercisedUnbound` alone cannot see it, because `MapEvent` never returns `Unbound` for the one value
  that exists — it returns `Mapped`. Run both checks; neither substitutes for the other.

`vocabulary` must be your own honest declaration of every event the product's displayed surface can
emit. A `vocabulary` narrower than the real product understates coverage, not the other way round: an
action you never declare here is exactly as invisible to this check as it is to the committed suite.
This function evaluates `MapEvent` once per vocabulary event against the freshly booted model, so an
`Unbound` arm reachable only from a later game state is outside what one call proves — script your
`vocabulary` from the states you actually need covered if boot alone is not enough.

**What this still cannot see.** `checkActionCoverage` finds an `Unbound` arm your own vocabulary
reaches; it cannot find a `'message` your `MapEvent` produces for **no** event at all — a message no
input path emits, as distinct from one it explicitly refuses. Proving that a message is reachable from
the product's real entry point through the real control surface needs the product to own its whole
journey entry point (below), and is `.github#2087`'s scope, not this function's: this closes the
coverage-model blind spot; it does not by itself prove a bot or player can reach the action from boot.

### Owning the whole entry point: composition authority

`Journey.runScriptWithIdentity` and `Journey.runPolicyWithIdentity` refuse an adapter whose composition
functions (`Boot`, `MapEvent`, `Update`, `FixedTick`, `ApplyEffectResult`, `IsTerminal`) do not all come
from one assembly. This is not incidental strictness: it is the mechanism that stops a test from
laundering its own state into a "production" journey through a side door, so the receipt's
`compositionAuthority` field means what it says.

**The trap.** A product-side factory that just wraps a caller-supplied value does not satisfy the check,
even though it looks like the product owns the composition:

```text
// LOOKS like the product owns Boot. Does not satisfy compositionAuthority.
let journeyBootOf model = fun () -> model   // defined in the product
...
Boot = journeyBootOf someTestBuiltModel     // called from a test with a test-built model
```

The returned closure's composition authority follows the value that built it, not the module that wraps
it — a caller-constructed closure crossing into the composition this way is exactly what the check
exists to catch. Reaching for this shape costs a full round trip through the check every time: build,
run, get refused, try to route around it, get refused again. That is the source of this guidance
(`2026-08-01-Rogue3-12.md` §4.7) — three failed `dotnet test` runs and two discarded implementations
before landing on the pattern below.

**The supported pattern.** The product owns the *whole* entry point: expose a function that takes a
script (and only product-internal parameters that resolve without any caller-supplied closure) and **no
model parameter at all**. Every composition function is then defined and referenced entirely inside the
product module, so `compositionAuthority` sees exactly one assembly — not because the check was routed
around, but because the composition genuinely has one author.

```fsharp
open FS.GG.Game.Core
open FS.GG.Game.Harness

type Model = { Started: bool }

// The product owns Boot outright: no parameters, no caller-supplied closure folded in.
let boot () : Model = { Started = false }

let mapEvent (event: JourneyEvent<unit, unit, unit, unit>) (_model: Model) =
    match event with
    | JourneyEvent.Start -> JourneyDispatch.Mapped [ "start" ]
    | _ -> JourneyDispatch.Mapped []

let update (message: string) (model: Model) =
    match message with
    | "start" -> { model with Started = true }
    | _ -> model

let adapter: ProductionJourney<Model, unit, unit, unit, unit, string, Model> =
    { RouteId = "doc-owned-entry-point"
      ScenarioId = "owns-boot"
      TestId = "GP-DOC-002"
      MaxSteps = 4
      Boot = boot
      MapEvent = mapEvent
      Update = update
      FixedTick = id
      ApplyEffectResult = fun _ model -> model
      IsTerminal = fun model -> model.Started
      Fingerprint = id
      EncodeEvent = sprintf "%A"
      EncodeFingerprint = sprintf "%A" }

// The product's own entry point: a script in, a JourneyRun out. NOTHING else — no model
// parameter, no caller-built closure crossing into Boot/MapEvent/Update/FixedTick/
// ApplyEffectResult/IsTerminal above. A test calls this function and only this function; every
// composition function it closes over was authored in the product module, which is what
// satisfies compositionAuthority for the reason the check exists, not by routing around it.
let runOwnedJourney (script: JourneyEvent<unit, unit, unit, unit> list) =
    Journey.runScriptWithIdentity "doc-owned-entry-point/script-v1" "doc-owned-entry-point/started" adapter script

let run = runOwnedJourney [ JourneyEvent.Start ]
let passed: bool = JourneyReceipt.result run.Receipt = JourneyResult.Passed
```

A test authored against this shape supplies only `script` — never a model, never a boot closure — and
passes the composition-authority check on the first attempt. If you still hit the rejection, the
diagnostic now names the likely cause in addition to each function's assembly and module version id: a
caller-constructed closure crossing into the composition. Move that closure inside the product's own
entry point rather than trying to wrap it more cleverly; wrapping is what fails.

## Independent acceptance critic

After the deterministic journey is green, run a fresh-context critic. Use a separate subagent when
available; otherwise perform a clearly separated review pass without relying on the implementer's
claim summary. Emit one structured row per requirement/AC:

```text
AC-### | supported|unsupported|ambiguous | checkpoints=<indices> |
terminal=<predicate> | route=<production evidence locations> | reason=<short finding>
```

Unsupported or ambiguous required behavior prevents completion. The critic may veto or ask for
stronger evidence, but cannot create or upgrade provenance: deleting or mismatching the runner
receipt must still fail mechanically even if every critic row says `supported`.

Save those rows as the critic artifact passed with `--critic`. For production-journey manifest rows,
`fsgg-playtest coverage-lint` and `emit-evidence` require the executable proof assembly and critic
artifact together. The critic AC set must exactly equal the ACs cited by production rows; missing or
extra/mismatched rows fail, and every `unsupported` or `ambiguous` row vetoes.

## Public Contract

The signatures you consume are bundled with this product under `docs/api-surface/Game.Harness/`:

- `Trace.fsi` — `Origin` (`InputDriven` | `Synthetic`) and the opaque `Trace<'f>`. `Trace.frames`
  is the value you assert on; `Trace.isSynthetic` is the provenance bit a gate reads.
- `Playable.fsi` — `Playable<'world,'key>` (init, keymap, apply, step, dt) and the `Bot<'view>` policy.
- `Driver.fsi` — `runScript` (raw key → keymap → `Command`), `runCommands`, and `runBot` with capture.
- `Journey.fsi` — production host events, composition adapter, scripted/seeded runners, opaque
  receipt, bounded failure, and production-journey provenance.
- `Matrix.fsi` — `Seat`, `MatchSetup`, `Match`, `runMatrix`, and `winRate`.
- `Synthetic.fsi` — the labeled fallback entry point.

The world `'world` and the raw key token `'key` are yours; the harness never learns a concrete world.
Every `Bot` draw threads a seeded value `Rng` — deconstruct with `let struct (cmds, next) = …`.

## Describe your game as a `Playable`, then drive the real input route

A `Playable` is the whole contract the scripted and bot drivers need. `Apply` and `Step` are pure
transitions (Model–Update–Effect); `Keymap` is *your* game's real binding, so a scripted test hits the
exact route a player does. `runScript` resolves each raw key through the keymap into a `Command` before
applying it — an **unbound token produces no command** — then advances exactly one whole fixed step at
`Playable.Dt`; no variable `dt`, and no render interpolant is fed back.

```fsharp
open FS.GG.Game.Core
open FS.GG.Game.Harness

// Your game as data the harness can drive. Apply and Step are pure: no I/O, no wall clock.
type World = { PaddleY: int; Tick: int }

let playable: Playable<World, string> =
    { Init = { PaddleY = 4; Tick = 0 }
      Keymap = Map [ "w", Command.MoveNorth; "s", Command.MoveSouth ]
      Apply =
        fun cmd w ->
            match cmd with
            | Command.MoveNorth -> { w with PaddleY = w.PaddleY - 1 }
            | Command.MoveSouth -> { w with PaddleY = w.PaddleY + 1 }
            | _ -> w
      Step = fun w _dt -> { w with Tick = w.Tick + 1 }
      Dt = 1.0 / 60.0 }

// Drive the real input route: raw key -> keymap -> Command -> one fixed step per frame.
let script = [ [ "w" ]; [ "w" ]; [ "s" ]; [] ] // one frame = the keys held that tick
let a = Driver.runScript playable Driver.identityFingerprint script
let b = Driver.runScript playable Driver.identityFingerprint script
let replays: bool = Trace.equalFrames a b // true — two runs of one script are byte-identical
```

`identityFingerprint` compares the whole `'world` by structural equality — the default. When the world
is large or carries fields irrelevant to the assertion, pass a projection `('world -> 'f)` instead and
the trace compares only that.

## Let a bot generate states, then replay its playthrough

A `Bot` decides from a **caller-supplied view and a seeded `Rng` only** — the full world model cannot
appear in its signature, so a policy genuinely cannot cheat by reading the whole board. `runBot`
captures every command it issued, and that capture replays through `runCommands` to a byte-identical
trace — a bot (or agent) playthrough becomes a regression golden for free.

```fsharp
open FS.GG.Game.Core
open FS.GG.Game.Harness

type World = { BallRow: int; PaddleRow: int; Tick: int }
type View = { Ball: int; Paddle: int } // a projection — never the world model

let playable: Playable<World, string> =
    { Init = { BallRow = 0; PaddleRow = 4; Tick = 0 }
      Keymap = Map.empty
      Apply =
        fun cmd w ->
            match cmd with
            | Command.MoveNorth -> { w with PaddleRow = w.PaddleRow - 1 }
            | Command.MoveSouth -> { w with PaddleRow = w.PaddleRow + 1 }
            | _ -> w
      Step = fun w _ -> { w with Tick = w.Tick + 1 }
      Dt = 1.0 / 60.0 }

let bot: Bot<View> =
    { Decide =
        fun view rng ->
            if view.Ball < view.Paddle then struct ([ Command.MoveNorth ], rng)
            else struct ([ Command.MoveSouth ], rng) }

let observe (w: World) : View = { Ball = w.BallRow; Paddle = w.PaddleRow }
let run = Driver.runBot playable observe bot 1234UL 600 Driver.identityFingerprint
// The captured commands replay through the scripted driver to a byte-identical trace.
let replay = Driver.runCommands playable Driver.identityFingerprint run.Captured
let same: bool = Trace.equalFrames run.Trace replay
```

## Assert a balance property across many seeds

`Matrix.runMatrix` runs a set of `(bot, bot, seed)` matches, one outcome per match, **independent of
the order** they are supplied — each match is a pure function of its seed. `outcome : 'world -> 'o` is
yours, so the world type never leaks into the runner. `winRate` folds the outcomes into the band an
assertion checks.

```fsharp
open FS.GG.Game.Core
open FS.GG.Game.Harness

type World = { Ticks: int; Lead: int }
type Outcome =
    | ChallengerWon
    | BaselineWon
    | Drawn

let challenger: Bot<int> = { Decide = fun _ rng -> struct ([ Command.MoveNorth ], rng) }
let baseline: Bot<int> = { Decide = fun _ rng -> struct ([], rng) }

let setup: MatchSetup<World, int> =
    { Dt = 1.0
      Init = fun _rng -> { Ticks = 0; Lead = 0 }
      Observe = fun _seat w -> w.Ticks
      Apply =
        fun seat _cmd w ->
            match seat with
            | Seat.A -> { w with Lead = w.Lead + 1 }
            | Seat.B -> { w with Lead = w.Lead - 1 }
      Step = fun w _ -> { w with Ticks = w.Ticks + 1 }
      IsOver = fun w -> w.Ticks >= 100
      MaxSteps = 200 }

let winner (w: World) : Outcome =
    if w.Lead > 0 then ChallengerWon
    elif w.Lead < 0 then BaselineWon
    else Drawn

let matches = [ for seed in 0UL..49UL -> { Seed = seed; A = challenger; B = baseline } ]
let outcomes = Matrix.runMatrix setup winner matches |> List.map snd
let winRateChallenger: float = Matrix.winRate (fun o -> o = ChallengerWon) outcomes
```

## Evidence: synthetic is a labeled fallback, and it never satisfies

`Synthetic.trace` lets you start from hand-built worlds when driving from real input is too expensive.
It is the **only** route to an `Origin.Synthetic` trace, and there is no route from it to
`Origin.InputDriven` — the provenance is unforgeable, so any trace or evidence derived from a hand-built
world is self-identifying.

```fsharp
open FS.GG.Game.Harness

type World = { AboutToWin: bool }

// The ONLY route to an Origin.Synthetic trace: its evidence is self-identifying and never satisfies.
let project (w: World) : bool = w.AboutToWin
let hand = Synthetic.trace project [ { AboutToWin = true }; { AboutToWin = false } ]
let disclosed: bool = Trace.isSynthetic hand // true — recorded as disclosed, not satisfying
```

That label has teeth. Under the SDD satisfaction rule an obligation is satisfied **only** by
`result: pass` **and** `synthetic: false`, and Governance's non-relaxable `evidenceNotSynthetic` gate
taints synthetic evidence down the DAG. So:

> A gameplay FR is satisfied only by an `Origin.InputDriven` trace — a script or a bot driven through
> the real `Command` frontier. A `Synthetic.trace` **discloses** a stand-in and can never close the
> obligation, no matter how green it looks.

The hatch is a distinct, typed surface by deliberate design of the harness: the shortcut is available,
but its cost is visible at the type level and in the evidence record, never silent. The per-FR gate
that reads that provenance bit and refuses a synthetic pass is ADR-0048's — this package is the
capability that gate presumes, not the other way round. Reach for real input first; use
`Synthetic.trace` only to reach a state that real input genuinely cannot, and expect its obligation to
read as *disclosed, not satisfied*.

## Prove the expected frame workload, not only the simulation frequency

A fixed 60 Hz `Playable` or bounded production journey proves deterministic simulation cadence. It
does **not** prove that input,
AI/perception/pathfinding, presentation projection, and scene construction fit inside a 60 FPS frame.
Define all five `ExpectedWorkload` kinds: idle, movement+aiming, combat+effects, maximum
visibility/fog, and maximum expected actors. Drive them through `Workload.run` with real raw-key
frames. Assert determinism on `run.Trace`; run timing verdicts in Release and publish
`Workload.renderArtifact` beside the scaffold performance evidence.

A MiniTank-shaped adapter reports fixed/catch-up steps, bounded AI/perception/pathfinding work,
actors, scene nodes, static-blocker builds/queries, and moving versus interpolated-moving actors.
The accepted shape preindexes sight/shell blockers once, bounds threat/flee search to its declared
window, emits row-run fog/minimap geometry, reuses static grid/terrain scene subtrees, and
interpolates player, enemies, shells, convoy, and every future mover by stable identity. Keep a
deliberately naive fixture: repeated terrain scans, unbounded search, per-cell fog nodes, or one
missing interpolated mover must fail.

Never put elapsed milliseconds in the fingerprint. Timing varies; `Trace.equalFrames` must not.

## Package Boundary

The harness depends only on `FS.GG.Game.Core` and the BCL — no render/input stack, no keymap type, and
no I/O. Only `Workload.run` reads a monotonic clock, outside the trace; keep `Playable.Apply`/`Step`
pure and thread the seeded `Rng`. The moment a
step reads a clock or an ambient RNG, replay dies and the FR gate is right to reject it. The keymap the
scripted driver folds through is *your* game's own binding (author it beside your input handling); the
harness never learns a device — mapping a real device to a `Command` is the render layer's job
([[fs-gg-rendering:fs-gg-keyboard-input]]).

## Test Commands

```bash
dotnet test                                          # run the whole gameplay suite
dotnet test --filter "FullyQualifiedName~Playtest"   # just the headless gameplay tests
```

Record the run as an `observedRun` receipt (a TRX/JUnit report) so the per-FR gate reads a run that
actually happened, not a self-attested pass. Production rows additionally require runner receipt
JSONL bound to the matching test identity.

## Common pitfalls

- **A `Step` that reads a wall clock or ambient RNG.** Determinism dies silently; the trace stops
  replaying. Thread the seeded `Rng` and take `dt` as a parameter.
- **Feeding the render interpolant back into the sim.** `alpha` is presentation only; never step with it.
- **A too-wide fingerprint golden.** If the world carries render-only or timestamp-like fields, project
  them out — otherwise the golden is brittle for reasons unrelated to the FR.
- **Reaching for `Synthetic.trace` to make a red FR green.** It cannot; it records as disclosed. Drive
  the real input instead, or accept the obligation as unmet.
- **A bot view that is the whole world.** Instantiating `'view = 'world` technically compiles, but it
  throws away the point of the boundary — project the view down to what the policy legitimately sees.
- **Calling a helper directly or replacing `Playable.Init` for a user-facing AC.** That proves a
  component only. Add a production journey starting from boot.
- **A product-side boot factory that wraps a caller-supplied model.** `journeyBootOf model = fun () ->
  model` looks like the product owns `Boot`, but the returned closure's composition authority follows
  the caller, not the factory, so `runScriptWithIdentity` rejects it. Give the product an entry point
  that takes a script and no model at all — see "Owning the whole entry point" above.
- **Silently dropping a displayed action.** Return `JourneyDispatch.Unbound`; an empty mapped message
  list is reserved for a deliberately handled no-op.
- **Instantiating `'menu` (or `'key`/`'pointer`) with a single-inhabitant type.** No script can then
  construct a second value at that slot, so any `Unbound` arm distinguishing it from something else is
  unreachable regardless of suite size. Run `Journey.checkActionCoverage` and treat a
  `DegenerateVocabulary` gap as a design smell, not noise.
- **Treating the acceptance critic as an oracle.** It can veto weak coverage, but only the runner can
  issue production-journey provenance.

## Generated Product

Author a `Playable` beside your world, write one headless test per gameplay FR driving `runScript` or
`runBot` through it, assert on `Trace.frames`, and record the TRX. Balance-shaped FRs use `runMatrix` +
`winRate`; a state real input cannot reach uses `Synthetic.trace` and is declared synthetic in its
evidence.

## Persistent problems

When a problem outlasts reasonable in-repo attempts, extensive external research is **mandatory** —
consult **official online docs first** (the F#/.NET docs), then community sources. If your product uses
Spec Kit, record findings under the feature's `specs/<feature>/feedback/`; otherwise record them in this
skill's **Sources** line. Offline, the mandate degrades to recording "research blocked — <why>".

## Related

- [[fs-gg-game:fs-gg-game-core]] — the simulation the harness drives: the fixed-step `Loop`, the seeded
  `Rng`, and why `alpha` must never re-enter the sim.
- [[fs-gg-game:fs-gg-ai]] — `Ai.TeamView`, the sealed fog boundary you can instantiate a `Bot`'s `'view`
  as to keep a policy honestly blind to the full model.
- [[fs-gg-rendering:fs-gg-keyboard-input]] — map a real device to the `Command` values your keymap binds.

## Sources / links

- F#/.NET docs: https://learn.microsoft.com/en-us/dotnet/fsharp/
- SplitMix64 (the RNG determinism relies on): https://prng.di.unimi.it/splitmix64.c

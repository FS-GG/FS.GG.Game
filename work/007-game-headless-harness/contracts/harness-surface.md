# Contract: `FS.GG.Game.Harness` public surface

The paired `.fsi` files under `src/Game.Harness/` are the authoritative machine
contract. This document records the surface and the load-bearing invariants; where
prose and the `.fsi` could disagree, the `.fsi` wins.

## Modules & signatures (namespace `FS.GG.Game.Harness`)

### `Trace`
- `[<RequireQualifiedAccess>] type Origin = InputDriven | Synthetic`
- `[<Sealed>] type Trace<'f>` — opaque; no public constructor or fields.
- `Trace.frames : Trace<'f> -> 'f list`
- `Trace.origin : Trace<'f> -> Origin`
- `Trace.isSynthetic : Trace<'f> -> bool`
- `Trace.equalFrames : Trace<'f> -> Trace<'f> -> bool`

### `Playable`
- `type Playable<'world,'key when 'key : comparison> =
    { Init: 'world; Keymap: Map<'key, Command>;
      Apply: Command -> 'world -> 'world; Step: 'world -> float -> 'world; Dt: float }`
- `Playable.resolve : Playable<'world,'key> -> 'key -> Command option`
- `type Bot<'view> = { Decide: 'view -> Rng -> struct (Command list * Rng) }`

### `Driver`
- `Driver.identityFingerprint : 'world -> 'world`
- `Driver.runCommands : Playable<'world,'key> -> fingerprint:('world -> 'f) -> script:(Command list list) -> Trace<'f>`
- `Driver.runScript : Playable<'world,'key> -> fingerprint:('world -> 'f) -> keyScript:('key list list) -> Trace<'f>`
- `type Run<'f,'view> = { Trace: Trace<'f>; Captured: Command list list }`
- `Driver.runBot : Playable<'world,'key> -> observe:('world -> 'view) -> Bot<'view> -> seed:uint64 -> steps:int -> fingerprint:('world -> 'f) -> Run<'f,'view>`

### `Matrix`
- `[<RequireQualifiedAccess>] type Seat = A | B`
- `type MatchSetup<'world,'view> =
    { Dt: float; Init: Rng -> 'world; Observe: Seat -> 'world -> 'view;
      Apply: Seat -> Command -> 'world -> 'world; Step: 'world -> float -> 'world;
      IsOver: 'world -> bool; MaxSteps: int }`
- `type Match<'view> = { Seed: uint64; A: Bot<'view>; B: Bot<'view> }`
- `Matrix.runMatch : MatchSetup<'world,'view> -> outcome:('world -> 'o) -> Match<'view> -> 'o`
- `Matrix.runMatrix : MatchSetup<'world,'view> -> outcome:('world -> 'o) -> matches:(Match<'view> list) -> (Match<'view> * 'o) list`
- `Matrix.winRate : ('o -> bool) -> 'o list -> float`

### `Synthetic`
- `Synthetic.trace : fingerprint:('world -> 'f) -> worlds:('world list) -> Trace<'f>`

## Invariants (the contract's teeth)

- **Determinism (FR-001, FR-008):** for a fixed `Playable` and a fixed script,
  `Trace.frames` is byte-identical across runs and platforms. No wall clock, no
  ambient RNG, no `Map`/`HashSet` order in a result.
- **Real input route (FR-002):** `runScript` resolves every raw key through the
  keymap before applying; an unbound token contributes no command.
- **Whole fixed steps (FR-003):** every frame advances by exactly one `Step world
  Dt` at the constant `Dt`; `alpha` is never fed back.
- **Bot isolation (FR-004):** `Bot.Decide` takes only `'view` and `Rng`; `'world`
  is absent from its signature; identical `(view, seed)` yields identical commands.
- **Order independence (FR-005):** `runMatrix` returns one `'o` per input match;
  permuting `matches` permutes the result identically and changes no match's `'o`.
- **Synthetic provenance (FR-006):** `Origin.Synthetic` is reachable *only* through
  `Synthetic.trace`, and `Origin.InputDriven` *only* through the driver; the two
  are distinct and `Trace.isSynthetic` is unforgeable — an evidence gate can trust
  it, and `synthetic:true` can never satisfy an obligation.
- **Leaf dependency (FR-007):** the assembly references only `FS.GG.Game.Core`,
  FSharp.Core, and `System.*`; it performs no I/O and reads no wall clock.

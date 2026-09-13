# SVG-RUNTIME-01 — Sessions and continuous gameplay

Status: active; SVG-RUNTIME-01.1 implementation complete in its routine change, with .2 next after merge readback.
Route: routine.

Owners: FS.GG.Game owns session semantics, fixed-step execution, collision classification and gameplay
intent meaning. FS.GG.Rendering owns the browser clock/projection adapter. FS.GG.Templates owns the
generated continuous-game composition and installed journeys. This is order 7 of the accepted SVG
programme and covers C08/C10 plus M5.1–M5.5.

## Outcome and boundary

A product implements one `SessionContract` and runs it through a bounded, deterministic fixed-step
runtime. Hosts inject elapsed time, device-independent semantic inputs and lifecycle observations. The
runtime never reads a clock, DOM, worker, transport or renderer. Integer elapsed microseconds determine
whole steps; presentation interpolation remains outside authority state.

Local and authoritative-server modes share initialization, input ordering, projection, snapshot and exact
compatibility rules. Browser adapters own animation frames, suspension recovery, stale request rejection,
projection coalescing and disposal. Collision reuses Game.Core's circles, AABBs, convex polygons, spatial
grid, ray/segment casts, swept overlap and kinematic response; it does not create a second geometry model.
The representative product is a neutral continuous vector arena with movement, collectibles, obstacles,
health, score and win/lose/restart behavior.

SVG-RUNTIME-01 does not add animation clips, audio, persistence, replay, networking or public package
publication. Those remain SVG-PRESENT-01, Preview B, SVG-REPLAY-01 and SVG-NETWORK-01. S.I.R. remains
strictly off limits.

## Executable milestones

- [x] **SVG-RUNTIME-01.1 — Portable fixed-step session reducer — route: routine**

  Owner: Game. Add a pure runtime around the existing generic session envelope. Validate a bounded integer
  tick policy; enforce monotonic semantic input sequence; implement elapsed advancement, pause, resume,
  single-step, reset, compatible restore and idempotent disposal. Refusals preserve accepted product and
  runtime state. Curate the exact same sources into the Fable package view and compare a canonical transition
  corpus byte-for-byte in .NET and Fable.

  Acceptance: no clock or renderer dependency enters Game.Core; catch-up executes no more than the configured
  bound and reports dropped suspension time; pause never banks elapsed time; reset/restore clear transient
  clock state; wrong-session, stale-input, incompatible snapshot and post-disposal observations are explicit.

  Evidence: 741 Game.Core tests pass, including bounded catch-up, pause/resume/step/reset/restore/dispose and
  refusal-state preservation. The packed .NET and Fable consumers produce the same 512-byte canonical session
  transition corpus, with its schema and compatibility profile pinned to the committed oracle digest.

- [ ] **SVG-RUNTIME-01.2 — Generation-safe operations and projection backpressure — route: routine**

  Owner: Game. Add portable request/generation state for local-worker and authoritative-server interpreters.
  Inputs and required session records remain ordered; obsolete projections may coalesce. Cancellation,
  replacement, failure and disposal invalidate old generations and reject stale replies without committing
  twice. Provide a server-session adapter contract without selecting a transport.

  Acceptance: deterministic traces cover stale replies, cancellation races, slow consumers, projection
  coalescing, input preservation, suspension recovery and bounded ownership. Packed .NET/Fable consumers
  agree on every reducer transition.

- [ ] **SVG-RUNTIME-01.3 — Collision and continuous-arena correspondence — route: routine**

  Owner: Game. Compose the existing Geometry, SpatialGrid and Resolution surfaces into a bounded kinematic
  world adapter. Cover circle/AABB/convex overlap, broad-phase candidates, segment/raycast, swept motion,
  triggers, sliding and bounce. Add a neutral deterministic arena through Game.Harness with movement,
  collectible, obstacle, health, score, win/lose and restart outcomes.

  Acceptance: narrow phase agrees with direct fixtures; broad phase has no false negatives; high-speed swept
  motion cannot tunnel through thin-obstacle fixtures; collision detection stays separate from product
  response; two independent runs produce identical projections and snapshots.

- [ ] **SVG-RUNTIME-01.4 — Browser clock and retained projection host — route: routine**

  Owner: Rendering. Add a disposable browser session host over `requestAnimationFrame` and the Game runtime
  contract. It converts host elapsed time at one boundary, keeps presentation interpolation separate, pauses
  or requests recovery after suspension, rejects stale generations and applies only monotonic projections to
  retained SVG state. Blur, visibility change, replacement and disposal release every frame, listener,
  request and worker.

  Acceptance: .NET/Fable correspondence plus Chromium/Firefox/WebKit cover variable presentation cadence,
  bounded catch-up, pause/step/reset, tab suspension, stale worker reply, projection coalescing and zero-owned
  resources after disposal.

- [ ] **SVG-RUNTIME-01.5 — Generated continuous player and Preview-B handoff — route: routine**

  Owner: Templates; Game and Rendering retain producer defects and completion ledgers. Compose the current
  SVG input profile, session runtime, collision adapter and retained scene into the opt-in candidate. Qualify
  direct, SDD, wizard-adopter and retained upgrade/rollback routes without changing public pins.

  Acceptance: an isolated generated player executes real keyboard, pointer, touch and gamepad movement through
  the same semantic commands; pause/step/reset and lose/win/restart alter actual authority state; player output
  excludes Studio modules. Evidence binds exact Game, Rendering and Templates revisions and archives, Fable
  correspondence, headless/browser outcomes, public baselines and every migration/rollback result.

## Completion and release impact

After all five milestones meet their authority boundaries, record SVG-RUNTIME-01 complete with publication
pending Preview B, update the unified projection and select SVG-PRESENT-01. Candidate versions remain private
and unique to their bytes. No provider, registry, lifecycle or default changes occur in this feature.

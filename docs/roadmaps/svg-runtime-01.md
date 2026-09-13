# SVG-RUNTIME-01 — Sessions and continuous gameplay

Status: complete at the source/generated-candidate boundary. Game, Rendering and Templates owner changes
are merged and the exact generated continuous-player qualification is green. Publication remains owned by
SVG-PREVIEW-B.
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

- [x] **SVG-RUNTIME-01.2 — Generation-safe operations and projection backpressure — route: routine**

  Owner: Game. Add portable request/generation state for local-worker and authoritative-server interpreters.
  Inputs and required session records remain ordered; obsolete projections may coalesce. Cancellation,
  replacement, failure and disposal invalidate old generations and reject stale replies without committing
  twice. Provide a server-session adapter contract without selecting a transport.

  Acceptance: deterministic traces cover stale replies, cancellation races, slow consumers, projection
  coalescing, input preservation, suspension recovery and bounded ownership. Packed .NET/Fable consumers
  agree on every reducer transition.

  Evidence: 746 Game.Core tests cover ordered required completions, stale-generation refusal, cancellation,
  replacement, failure, projection coalescing and disposal. Packed .NET and Fable consumers produce the same
  536-byte canonical corpus. The checked-in Quint model typechecks, finds no invariant violation across 1,000
  bounded 30-step simulations, and passes witnesses for reversed replies, slow projection consumption,
  cancellation races and recovery after replacement.

- [x] **SVG-RUNTIME-01.3 — Collision and continuous-arena correspondence — route: routine**

  Owner: Game. Compose the existing Geometry, SpatialGrid and Resolution surfaces into a bounded kinematic
  world adapter. Cover circle/AABB/convex overlap, broad-phase candidates, segment/raycast, swept motion,
  triggers, sliding and bounce. Add a neutral deterministic arena through Game.Harness with movement,
  collectible, obstacle, health, score, win/lose and restart outcomes.

  Acceptance: narrow phase agrees with direct fixtures; broad phase has no false negatives; high-speed swept
  motion cannot tunnel through thin-obstacle fixtures; collision detection stays separate from product
  response; two independent runs produce identical projections and snapshots.

  Evidence: the kinematic adapter delegates AABB, circle and convex detection and segment casts to the shared
  geometry surface, uses the spatial grid for insertion-ordered swept candidates, and applies slide/bounce
  only after detection. Core fixtures cover direct agreement, broad-phase completeness, triggers and thin-wall
  tunnelling. The neutral arena drives movement, collectibles, hazards, score, health, win/loss and restart
  through `Playable`; two independent command runs produce equal projection traces.

- [x] **SVG-RUNTIME-01.4 — Browser clock and retained projection host — route: routine**

  Owner: Rendering. Add a disposable browser session host over `requestAnimationFrame` and the Game runtime
  contract. It converts host elapsed time at one boundary, keeps presentation interpolation separate, pauses
  or requests recovery after suspension, rejects stale generations and applies only monotonic projections to
  retained SVG state. Blur, visibility change, replacement and disposal release every frame, listener,
  request and worker.

  Acceptance: .NET/Fable correspondence plus Chromium/Firefox/WebKit cover variable presentation cadence,
  bounded catch-up, pause/step/reset, tab suspension, stale worker reply, projection coalescing and zero-owned
  resources after disposal.

  Evidence: Rendering PR #1310 merged as `504e5f0ab3d06e363dae65f42687139744d09553`; native gamepad
  qualification repair PR #1311 merged as `50bb064c8acb0251a469ca406d193551f8e209d1`. Portable .NET/Fable
  correspondence and the Chromium, Firefox and WebKit browser matrix cover clock, lifecycle, coalescing,
  stale-generation rejection, retained projection and zero-resource disposal behavior.

- [x] **SVG-RUNTIME-01.5 — Generated continuous player and Preview-B handoff — route: routine**

  Owner: Templates; Game and Rendering retain producer defects and completion ledgers. Compose the current
  SVG input profile, session runtime, collision adapter and retained scene into the opt-in candidate. Qualify
  direct, SDD, wizard-adopter and retained upgrade/rollback routes without changing public pins.

  Acceptance: an isolated generated player executes real keyboard, pointer, touch and gamepad movement through
  the same semantic commands; pause/step/reset and lose/win/restart alter actual authority state; player output
  excludes Studio modules. Evidence binds exact Game, Rendering and Templates revisions and archives, Fable
  correspondence, headless/browser outcomes, public baselines and every migration/rollback result.

  Evidence: the final Game producer repair is merged as `c6de5b83eaa3d3f14909b42c3f8c3c94558157c9` (PR #625).
  Templates PR #471 merged as `4a392a18ada74a87a12dd0b31aebaa7039a84b86` after exact candidate
  qualification passed generated authoring, input and runtime players in Chromium, Firefox and WebKit,
  Orca/AT-SPI, package consumers, typed receivers and repository composition. Public pins remained unchanged.

## Completion and release impact

All five milestones meet their authority boundaries. SVG-RUNTIME-01 is complete with publication pending
Preview B, and SVG-PRESENT-01 is selected next. Candidate versions remain private and unique to their bytes.
No provider, registry, lifecycle or default changes occur in this feature.

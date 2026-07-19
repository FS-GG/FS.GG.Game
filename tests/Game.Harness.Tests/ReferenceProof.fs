/// WI-7 — the reference-game obligation-chain proof (FS-GG/FS.GG.Game#422).
///
/// The other files in this suite prove the *harness's* own FRs (FR-001..FR-008). This file proves the
/// thing WI-8 must be able to trust before it flips the gameplay gate to block-on-ship: that the whole
/// per-FR obligation chain is **green on a reference game**, end to end and *non-synthetically*.
///
/// The reference sim is the integer-grid `PongSim` fixture (the "Pong" TestSpec, `docs/TestSpecs/Games/
/// pong.md`). Each `GameplayFr` below is a gameplay requirement of that game, classified with the
/// `gameplay` facet (the ADR-0048 classifier, made explicit here as data), cross-referenced to the
/// TestSpec acceptance criteria it covers, and **satisfied by an `Origin.InputDriven` trace** — a world
/// driven through the real `Command` frontier (raw key -> keymap -> `Command` -> fixed step), never the
/// synthetic hatch. The two `gate:` tests are the teeth: one asserts every gameplay-FR proof is a
/// non-synthetic `InputDriven` trace (the chain is green); the other asserts that the *same frames*,
/// laundered through `Synthetic.trace`, are refused — provenance the gate reads, not the frame values.
///
/// Every scenario's initial world is a constructed `Pong` state ("Given the ball strikes the paddle at
/// its centre, when ...", exactly as the TestSpec ACs are phrased) driven through `PongSim.step` by the
/// real driver. Constructing a *starting* state is standard test setup; it is the synthetic hatch —
/// hand-built *frame sequences* fed to `Synthetic.trace` — that the satisfaction rule forbids, and this
/// file never uses it to satisfy an FR.
module Game.Harness.Tests.ReferenceProof

open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Harness
open Game.Harness.Tests.PongSim

// ---------------------------------------------------------------------------------------------------
// The classified gameplay-FR manifest: the reference game's requirements, the `gameplay` facet, and
// the TestSpec ACs each covers. Kept as data so the "coverage is total" gate can check it.
// ---------------------------------------------------------------------------------------------------

/// One gameplay requirement of the reference game, classified and cross-referenced. `Facet` is the
/// ADR-0048 classifier value; `CoversAc` are the `pong.md` §14 acceptance-criteria numbers.
type GameplayFr =
    { Id: string
      Facet: string
      Summary: string
      CoversAc: int list }

let private gp (id: string) (summary: string) (coversAc: int list) : GameplayFr =
    { Id = id
      Facet = "gameplay"
      Summary = summary
      CoversAc = coversAc }

/// The reference game's gameplay FRs. GP-001..GP-009 are each backed by an `InputDriven` trace (the
/// `gameplayProofs` list); GP-010 is a bot-vs-bot match outcome, driven through the same real frontier.
let manifest: GameplayFr list =
    [ gp "GP-001" "A script and a captured bot playthrough each replay byte-identically" [ 13 ]
      gp "GP-002" "The real key->keymap->Command route drives the paddle; unbound/no-move keys are no-ops" [ 2; 3 ]
      gp "GP-003" "The paddle clamps at the top and bottom walls and never overlaps them" [ 2 ]
      gp "GP-004" "The ball integrates at unit speed and always stays on the playfield" [ 4 ]
      gp "GP-005" "A wall bounce reflects the ball and preserves its speed" [ 4 ]
      gp "GP-006" "A covered paddle deflects the ball back into play with no point scored" [ 5; 15 ]
      gp "GP-007" "A deflection resolves in one step — the ball leaves the paddle and is not hit again" [ 8 ]
      gp "GP-008" "A ball past an uncovered goal scores for the opponent and re-serves from centre" [ 9 ]
      gp "GP-009" "Every serve resets the ball to centre; the launch direction is seed-determined" [ 1; 16 ]
      gp "GP-010" "A first-to-N match ends decisively at the winning point and the runner is order-free" [ 10; 13 ] ]

// ---------------------------------------------------------------------------------------------------
// Driving helpers — the non-synthetic route.
// ---------------------------------------------------------------------------------------------------

/// The reference sim's starting world (left paddle centred, covers rows 4..7).
let private I = playable.Init

/// Drive `w0` through the real `Command` frontier for the given raw-key frames. The trace is
/// `Origin.InputDriven` by construction — this is the route that satisfies a gameplay obligation.
let private driveFrom (w0: Pong) (script: string list list) : Trace<Pong> =
    Driver.runScript { playable with Init = w0 } Driver.identityFingerprint script

/// One whole fixed step from `w0` with no input.
let private stepFrom (w0: Pong) : Pong =
    driveFrom w0 [ [] ] |> Trace.frames |> List.exactlyOne

/// A left-paddle top row that does NOT cover the ball at row `r` (parks the paddle against a wall).
let private missState (ballRow: int) : Pong =
    { I with BallX = 1; BallY = ballRow; BallDX = -1; BallDY = -1; LeftY = 0 }

// A determinism script mixing bound moves, a no-input frame, and repeated keys.
let private detScript: string list list = [ [ "w" ]; [ "w" ]; [ "s" ]; []; [ "s" ]; [ "w" ] ]

// A ball arriving at the covered left paddle (rows 4..7): the deflection scenario.
let private deflectState: Pong =
    { I with BallX = 1; BallY = 5; BallDX = -1; BallDY = -1; LeftY = 4 }

/// The representative `InputDriven` proof trace for each trace-backed gameplay FR. The `gate:` test
/// walks this list and asserts every trace is non-synthetic; the per-FR tests assert the behaviour.
let private gameplayProofs: (string * Trace<Pong>) list =
    [ "GP-001", driveFrom I detScript
      "GP-002", driveFrom I [ [ "w" ]; [ "z" ]; [ "esc" ] ]
      "GP-003", driveFrom I (List.replicate 8 [ "w" ])
      "GP-004", driveFrom I (List.replicate 16 [])
      "GP-005", driveFrom { I with BallX = 8; BallY = 0; BallDX = -1; BallDY = -1 } [ [] ]
      "GP-006", driveFrom deflectState [ [] ]
      "GP-007", driveFrom deflectState [ []; [] ]
      "GP-008", driveFrom (missState 6) [ [] ]
      "GP-009", driveFrom I (List.replicate 30 []) ]

// ---------------------------------------------------------------------------------------------------
// The two-seat match wiring reused for GP-010 (win condition + order independence).
// ---------------------------------------------------------------------------------------------------

// A deliberately NON-palindromic outcome sequence ([LeftWins; RightWins; RightWins]): the two
// positions that swap under reversal differ, so an order-dependence bug affecting them is visible.
let private matches: Match<struct (int * int)> list =
    [ { Seed = 1UL; A = chaseBot; B = sitterBot }
      { Seed = 2UL; A = sitterBot; B = chaseBot }
      { Seed = 3UL; A = sitterBot; B = chaseBot } ]

[<Tests>]
let tests =
    testList
        "ReferenceProof"
        [ testCase "GP-001 a script and a captured bot playthrough each replay byte-identically"
          <| fun _ ->
              // AC-13 determinism: same seed + same input log => identical trace.
              let a = driveFrom I detScript
              let b = driveFrom I detScript
              Expect.isTrue (Trace.equalFrames a b) "two runs of one script must be byte-identical"

              let run = Driver.runBot playable observeLeft chaseBot 7UL 120 Driver.identityFingerprint
              let replay = Driver.runCommands playable Driver.identityFingerprint run.Captured
              Expect.isTrue (Trace.equalFrames run.Trace replay) "a captured bot playthrough must replay identically"
              Expect.equal (Trace.origin run.Trace) Origin.InputDriven "a bot run is non-synthetic evidence"

          testCase "GP-002 the real key route drives the paddle; unbound and no-move keys are no-ops"
          <| fun _ ->
              // AC-2/AC-3 controls: raw key -> keymap -> Command -> apply.
              let up = driveFrom I [ [ "w" ] ] |> Trace.frames |> List.exactlyOne
              let unbound = driveFrom I [ [ "z" ] ] |> Trace.frames |> List.exactlyOne
              let paused = driveFrom I [ [ "esc" ] ] |> Trace.frames |> List.exactlyOne
              Expect.equal up.LeftY (I.LeftY - 1) "'w' resolves to MoveNorth and moves the paddle up one row"
              Expect.equal unbound.LeftY I.LeftY "an unbound token 'z' produces no command — the paddle is unchanged"
              Expect.equal paused.LeftY I.LeftY "'esc' binds Pause, which does not move the paddle"
              Expect.equal (Playable.resolve playable "z") None "'z' is genuinely unbound in the keymap"
              Expect.equal (Playable.resolve playable "esc") (Some Command.Pause) "'esc' is bound to Pause (a no-move command), not silently unbound"

          testCase "GP-003 the paddle clamps at both walls and never overlaps them"
          <| fun _ ->
              // AC-2 clamp.
              let top = driveFrom I (List.replicate 8 [ "w" ]) |> Trace.frames |> List.last
              let bottom = driveFrom I (List.replicate 8 [ "s" ]) |> Trace.frames |> List.last
              Expect.equal top.LeftY 0 "holding up clamps the paddle at the top wall (row 0)"
              Expect.equal bottom.LeftY (H - P) "holding down clamps the paddle at the bottom wall (H - P)"
              let ys =
                  driveFrom I (List.replicate 8 [ "w" ] @ List.replicate 8 [ "s" ])
                  |> Trace.frames
                  |> List.map (fun w -> w.LeftY)
              Expect.isTrue (ys |> List.forall (fun y -> y >= 0 && y <= H - P)) "the paddle never overlaps a wall"

          testCase "GP-004 the ball integrates at unit speed and never leaves the playfield"
          <| fun _ ->
              // AC-4: a discrete grid stepped at unit speed keeps every frame on the board and the
              // velocity magnitude at exactly one cell. (AC-14's sub-step tunneling guard is not modelled
              // by this fixture — a unit-speed integer grid has no displacement large enough to tunnel.)
              let frames = driveFrom I (List.replicate 16 []) |> Trace.frames
              Expect.isTrue
                  (frames |> List.forall (fun w -> abs w.BallDX = 1 && abs w.BallDY = 1))
                  "the ball's velocity is always exactly one cell on each axis (speed preserved)"
              Expect.isTrue
                  (frames
                   |> List.forall (fun w -> w.BallX >= 0 && w.BallX <= W - 1 && w.BallY >= 0 && w.BallY <= H - 1))
                  "the ball is never outside the playfield — it cannot pass through a wall"

          testCase "GP-005 a wall bounce reflects the ball and preserves its speed"
          <| fun _ ->
              // AC-4: at the top wall vy flips, speed stays, and the ball does not pass the wall.
              let topBounce = stepFrom { I with BallX = 8; BallY = 0; BallDX = -1; BallDY = -1 }
              Expect.equal topBounce.BallDY 1 "at the top wall the vertical velocity flips from -1 to +1"
              Expect.equal topBounce.BallY 0 "the ball stays on the wall row — it does not pass through"
              Expect.equal (abs topBounce.BallDX) 1 "horizontal speed is unchanged by a wall bounce"

              let bottomBounce = stepFrom { I with BallX = 8; BallY = H - 1; BallDX = -1; BallDY = 1 }
              Expect.equal bottomBounce.BallDY -1 "at the bottom wall the vertical velocity flips from +1 to -1"
              Expect.equal bottomBounce.BallY (H - 1) "the ball stays on the bottom wall row"

          testCase "GP-006 a covered paddle deflects the ball back into play with no point scored"
          <| fun _ ->
              // AC-5/AC-15: the ball reaches the left paddle column on a covered row and is reflected
              // back toward the field (vx > 0), and no rally can stall — it leaves moving horizontally.
              let deflected = stepFrom deflectState
              Expect.equal deflected.BallDX 1 "a covered deflection sends the ball back toward the field (vx > 0)"
              Expect.equal deflected.ScoreR 0 "a successful deflection scores no point for the opponent"
              Expect.equal deflected.ScoreL 0 "...nor for the deflecting side"
              Expect.isLessThan deflected.BallX W "the ball stays on the playfield after the deflection"

          testCase "GP-007 a deflection resolves in one step — the ball leaves the paddle and is not hit again"
          <| fun _ ->
              // AC-8 no double-hit. PongSim resolves a deflection atomically: the reflect places the ball
              // one cell off the goal line moving outward (vx > 0), so it can never re-enter the paddle
              // column and be reflected twice. Drive the REAL deflection (from deflectState, DX = -1), so
              // step 1 is the reflection and the following steps must show the ball departing.
              let frames = driveFrom deflectState [ []; [] ] |> Trace.frames
              Expect.equal (List.head frames).BallDX 1 "step 1 is the deflection: the ball turns outbound (vx > 0)"
              Expect.isTrue
                  (frames |> List.forall (fun w -> w.BallDX = 1))
                  "the ball keeps moving away — no second reflection flips it back"
              Expect.isTrue
                  (frames |> List.forall (fun w -> w.ScoreL = 0 && w.ScoreR = 0))
                  "the deflection keeps the ball in play — no point is scored"
              let xs = frames |> List.map (fun w -> w.BallX)
              Expect.equal xs (List.sort xs) "the ball's column advances monotonically away from the paddle"

          testCase "GP-008 a ball past an uncovered goal scores for the opponent and re-serves from centre"
          <| fun _ ->
              // AC-9 scoring: the left paddle does not cover the ball, so the right side scores and the
              // ball re-serves from the centre of the field.
              let scored = stepFrom (missState 6)
              Expect.equal scored.ScoreR 1 "an uncovered goal awards the opponent (right) a point"
              Expect.equal scored.BallX (W / 2) "the ball re-serves from the horizontal centre"
              Expect.equal scored.BallY (H / 2) "...and the vertical centre"

          testCase "GP-009 every serve resets the ball to centre and the launch direction is seed-determined"
          <| fun _ ->
              // AC-16: rally state does not carry across a point — every serve starts at centre. AC-1/AC-13:
              // the launch direction is drawn from the seeded Rng, so it is reproducible and load-bearing.
              let frames = driveFrom I (List.replicate 30 []) |> Trace.frames
              // Pair each frame with its predecessor; a frame whose score rose is the one a point was
              // just conceded on, and the re-serve must place the ball back at the exact centre.
              let scoringSteps =
                  List.pairwise (I :: frames)
                  |> List.filter (fun (prev, cur) -> cur.ScoreL > prev.ScoreL || cur.ScoreR > prev.ScoreR)
              Expect.isNonEmpty scoringSteps "the reference run must score at least once, producing a re-serve"
              Expect.isTrue
                  (scoringSteps |> List.forall (fun (_, cur) -> cur.BallX = W / 2 && cur.BallY = H / 2))
                  "every point re-serves the ball from centre — rally state does not carry across the point"

              let dirOf (seed: uint64) = (init (Rng.ofSeed seed)).BallDY
              Expect.equal (dirOf 3UL) (dirOf 3UL) "the serve direction is reproducible for a fixed seed"
              let directions = [ 0UL..8UL ] |> List.map dirOf |> List.distinct
              Expect.isGreaterThan (List.length directions) 1 "across seeds the launch direction varies — the seed is load-bearing"

          testCase "GP-010 a first-to-N match ends decisively and the runner is order-free"
          <| fun _ ->
              // AC-10 win condition + AC-13 determinism, through the two-seat frontier (no synthetic state).
              let single = Matrix.runMatch matchSetup id { Seed = 1UL; A = chaseBot; B = sitterBot }
              Expect.isTrue
                  (single.ScoreL = WinScore || single.ScoreR = WinScore)
                  "a finished match ends exactly when a side reaches the winning score"
              Expect.notEqual (outcome single) Draw "a chaser-vs-sitter match has a decisive winner"
              Expect.isGreaterThan single.Tick WinScore "the match was a real rally, not an instant win"

              let forward = Matrix.runMatrix matchSetup outcome matches |> List.map snd
              // The outcomes are non-palindromic ([LeftWins; RightWins; RightWins]), so this equality
              // genuinely constrains order: a bug that swapped ends would change the first/last outcome.
              Expect.notEqual (List.head forward) (List.last forward) "the outcome sequence is non-palindromic, so order is actually under test"
              let reversed = Matrix.runMatrix matchSetup outcome (List.rev matches) |> List.map snd
              Expect.equal (List.rev forward) reversed "permuting the matches permutes the outcomes identically"

          // ---- the gate: the teeth WI-8 flips to block-on-ship ----

          testCase "gate: the obligation chain is green — every gameplay-FR proof is a non-synthetic InputDriven trace"
          <| fun _ ->
              // The per-FR gameplay gate (ADR-0048) reads exactly this provenance bit. If any proof were
              // Synthetic, the obligation would be unsatisfied and the gate would refuse the ship.
              for (id, trace) in gameplayProofs do
                  Expect.isFalse (Trace.isSynthetic trace) (sprintf "%s must be proven non-synthetically" id)
                  Expect.equal (Trace.origin trace) Origin.InputDriven (sprintf "%s is satisfied by input-driven evidence" id)

          testCase "gate: a synthetic shortcut is refused — identical frames, but Origin.Synthetic never satisfies"
          <| fun _ ->
              // Launder a real proof's frames through the synthetic hatch. The frames are byte-identical,
              // yet the provenance still separates them: the synthetic copy can never close the obligation.
              let real = driveFrom deflectState [ [] ]
              let laundered = Synthetic.trace Driver.identityFingerprint (Trace.frames real)
              Expect.isTrue (Trace.equalFrames real laundered) "the frame sequences are identical"
              Expect.isFalse (Trace.isSynthetic real) "the driven proof is non-synthetic"
              Expect.isTrue (Trace.isSynthetic laundered) "the laundered copy is disclosed as synthetic"
              Expect.notEqual (Trace.origin real) (Trace.origin laundered) "provenance is not derived from the frames"

          testCase "manifest: gameplay-FR coverage is total and every FR names a TestSpec acceptance criterion"
          <| fun _ ->
              let ids = manifest |> List.map (fun f -> f.Id)
              Expect.equal ids (List.sort ids) "the gameplay FRs are listed in id order"
              Expect.equal (List.distinct ids) ids "gameplay-FR ids are unique"
              Expect.equal (List.length manifest) 10 "GP-001..GP-010 are all present"
              Expect.isTrue (manifest |> List.forall (fun f -> f.Facet = "gameplay")) "every FR carries the gameplay classifier facet"
              Expect.isTrue
                  (manifest |> List.forall (fun f -> not f.CoversAc.IsEmpty))
                  "every gameplay FR cites at least one acceptance criterion"
              Expect.isTrue
                  (manifest |> List.forall (fun f -> f.CoversAc |> List.forall (fun ac -> ac >= 1 && ac <= 19)))
                  "every cited AC is a real Pong TestSpec §14 criterion (1..19)"

              // Every trace-backed FR (GP-001..GP-009) has a proof in the gate list, and vice versa.
              let proven = gameplayProofs |> List.map fst |> List.sort
              let expected = [ 1..9 ] |> List.map (sprintf "GP-%03d")
              Expect.equal proven expected "GP-001..GP-009 each have an input-driven proof trace" ]

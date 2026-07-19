module Game.Harness.Tests.DriverTests

open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Harness
open Game.Harness.Tests.PongSim

// A fixed script of raw key frames driving the left paddle: some moves, an unbound token, and an
// empty (no-input) frame.
let private keyScript: string list list =
    [ [ "w" ]; [ "w" ]; [ "s" ]; []; [ "z" ]; [ "w" ]; [] ]

// A compact, platform-stable projection of the world, for a frozen golden.
let private proj (w: Pong) : string =
    sprintf "%d,%d,%d,%d,%d,%d,%d" w.BallX w.BallY w.BallDX w.BallDY w.LeftY w.ScoreL w.ScoreR

// A probe world that records every dt the driver hands its step function, so a variable or zero dt is
// observable — the fixture in PongSim deliberately ignores dt, which cannot catch FR-003's dt half.
type private DtProbe = { Sum: float; Steps: int; LastDt: float }

let private dtPlayable: Playable<DtProbe, string> =
    { Init = { Sum = 0.0; Steps = 0; LastDt = 0.0 }
      Keymap = Map.empty
      Apply = fun _ w -> w
      // 0.25 is dyadic, so the accumulated sum is exact and the golden is a literal.
      Step = fun w dt -> { Sum = w.Sum + dt; Steps = w.Steps + 1; LastDt = dt }
      Dt = 0.25 }

[<Tests>]
let tests =
    testList
        "Driver"
        [ testCase "FR-001 determinism golden: same script yields byte-identical frames"
          <| fun _ ->
              let t1 = Driver.runScript playable Driver.identityFingerprint keyScript
              let t2 = Driver.runScript playable Driver.identityFingerprint keyScript
              Expect.isTrue (Trace.equalFrames t1 t2) "two runs of one script must produce equal frames"
              Expect.equal (Trace.frames t1) (Trace.frames t2) "frame lists must be structurally equal"

          testCase "FR-001 determinism golden: frames match a committed, pinned value"
          <| fun _ ->
              // A frozen golden — catches a nondeterminism source that is stable within a process but
              // varies across builds/platforms, which a same-process self-comparison cannot.
              let frames = Driver.runScript playable proj keyScript |> Trace.frames
              let golden = "9,5,1,-1,3,0,0|10,4,1,-1,2,0,0|11,3,1,-1,3,0,0|12,2,1,-1,3,0,0|13,1,1,-1,3,0,0|14,0,1,-1,2,0,0|8,6,1,-1,2,1,0"
              Expect.equal (String.concat "|" frames) golden "frames must match the committed golden"

          testCase "FR-002 keymap resolves bound tokens and drops unbound ones"
          <| fun _ ->
              Expect.equal (Playable.resolve playable "w") (Some Command.MoveNorth) "w binds MoveNorth"
              Expect.equal (Playable.resolve playable "s") (Some Command.MoveSouth) "s binds MoveSouth"
              Expect.equal (Playable.resolve playable "z") None "an unbound token resolves to None"

          testCase "FR-002 an unbound token applies no command (no-op), a bound one moves the paddle"
          <| fun _ ->
              let bound = Driver.runScript playable Driver.identityFingerprint [ [ "w" ] ]
              let unbound = Driver.runScript playable Driver.identityFingerprint [ [ "z" ] ]
              let boundLeftY = (List.exactlyOne (Trace.frames bound)).LeftY
              let unboundLeftY = (List.exactlyOne (Trace.frames unbound)).LeftY
              // "w" moves the paddle up (a smaller row); "z" is unbound, so the paddle stays.
              Expect.isLessThan boundLeftY unboundLeftY "the bound key must move the paddle; the unbound one must not"
              Expect.equal unboundLeftY playable.Init.LeftY "an unbound-only frame leaves the paddle where it started"

          testCase "FR-003 advances exactly one whole fixed step per frame, even with no input"
          <| fun _ ->
              let t = Driver.runScript playable Driver.identityFingerprint keyScript
              let frames = Trace.frames t
              Expect.equal (List.length frames) (List.length keyScript) "one recorded frame per input frame"
              Expect.equal (List.last frames).Tick (List.length keyScript) "Tick equals the number of fixed steps"
              let ticks = frames |> List.map (fun w -> w.Tick)
              Expect.equal ticks [ 1..List.length keyScript ] "each frame advances the tick by exactly one"

          testCase "FR-003 every step receives exactly the fixed Dt — never a variable or zero dt"
          <| fun _ ->
              // The teeth of "no variable dt": the probe records each dt it was handed.
              let n = 5
              let t = Driver.runCommands dtPlayable Driver.identityFingerprint (List.replicate n [])
              let last = List.last (Trace.frames t)
              Expect.equal last.Steps n "one step per frame"
              Expect.equal last.LastDt dtPlayable.Dt "the last step got exactly Dt"
              Expect.equal last.Sum (float n * dtPlayable.Dt) "the accumulated dt is exactly steps * Dt (no variable/zero dt)"

          testCase "FR-003 an empty script advances nothing and yields an input-driven, zero-frame trace"
          <| fun _ ->
              let t = Driver.runCommands playable Driver.identityFingerprint []
              Expect.isEmpty (Trace.frames t) "no frames for an empty script"
              Expect.isFalse (Trace.isSynthetic t) "an empty driven trace is still InputDriven"

          testCase "FR-008 a captured bot playthrough replays to a byte-identical trace"
          <| fun _ ->
              let run = Driver.runBot playable observeLeft chaseBot 7UL 200 Driver.identityFingerprint
              let replay = Driver.runCommands playable Driver.identityFingerprint run.Captured
              Expect.isTrue
                  (Trace.equalFrames run.Trace replay)
                  "replaying Run.Captured through runCommands must reproduce the original trace"
              Expect.equal (List.length run.Captured) 200 "the capture holds one command list per step"

          testCase "FR-008 runBot with zero steps captures nothing and traces nothing"
          <| fun _ ->
              let run = Driver.runBot playable observeLeft chaseBot 1UL 0 Driver.identityFingerprint
              Expect.isEmpty (Trace.frames run.Trace) "steps = 0 yields no frames"
              Expect.isEmpty run.Captured "steps = 0 captures nothing" ]

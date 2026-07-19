module Game.Harness.Tests.DriverTests

open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Harness
open Game.Harness.Tests.PongSim

// A fixed script of raw key frames driving the left paddle: some moves, an unbound token, and an
// empty (no-input) frame.
let private keyScript: string list list =
    [ [ "w" ]; [ "w" ]; [ "s" ]; []; [ "z" ]; [ "w" ]; [] ]

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
              // Tick counts fixed steps; N frames => N steps, including the empty ones.
              Expect.equal (List.last frames).Tick (List.length keyScript) "Tick equals the number of fixed steps"
              // The intermediate ticks are strictly 1..N — proof no frame skipped or doubled a step.
              let ticks = frames |> List.map (fun w -> w.Tick)
              Expect.equal ticks [ 1..List.length keyScript ] "each frame advances the tick by exactly one"

          testCase "FR-008 a captured bot playthrough replays to a byte-identical trace"
          <| fun _ ->
              let run = Driver.runBot playable observeLeft chaseBot 7UL 200 Driver.identityFingerprint
              let replay = Driver.runCommands playable Driver.identityFingerprint run.Captured
              Expect.isTrue
                  (Trace.equalFrames run.Trace replay)
                  "replaying Run.Captured through runCommands must reproduce the original trace"
              Expect.equal (List.length run.Captured) 200 "the capture holds one command list per step" ]

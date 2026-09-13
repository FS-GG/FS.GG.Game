module Game.Harness.Tests.ContinuousArenaTests

open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Harness

let private rect x y width height = { X = x; Y = y; Width = width; Height = height }

let private config items obstacles winningScore health =
    { PlayerStart = rect 0.0 0.0 1.0 1.0
      Speed = 2.0
      InitialHealth = health
      WinningScore = winningScore
      CollisionCellSize = 2.0
      Obstacles = obstacles
      Items = items }

[<Tests>]
let tests =
    testList "ContinuousArena" [
        testCase "real command route collects, wins and restarts" <| fun _ ->
            let pickup = { Id = "gem"; Bounds = rect 2.0 0.0 0.5 1.0; Kind = ContinuousArenaItemKind.Collectible 1 }
            let arena = config [ pickup ] [] 1 3
            let playable = ContinuousArena.playable arena (Map.ofList [ 'd', Command.MoveEast ]) 1.0
            let won =
                Driver.runScript playable ContinuousArena.project [ [ 'd' ] ]
                |> Trace.frames
                |> List.head
            Expect.equal won.Outcome ContinuousArenaOutcome.Won "collectible wins"
            let restarted = ContinuousArena.apply arena Command.Fire { ContinuousArena.init arena with Outcome = ContinuousArenaOutcome.Won }
            Expect.equal restarted (ContinuousArena.init arena) "fire restarts terminal authority state"

        testCase "hazard reduces health and produces loss" <| fun _ ->
            let hazard = { Id = "hazard"; Bounds = rect 2.0 0.0 0.5 1.0; Kind = ContinuousArenaItemKind.Hazard 2 }
            let arena = config [ hazard ] [] 99 2
            let world = ContinuousArena.init arena |> ContinuousArena.apply arena Command.MoveEast
            let lost = ContinuousArena.step arena world 1.0
            Expect.equal lost.Health 0 "damage applied"
            Expect.equal lost.Outcome ContinuousArenaOutcome.Lost "loss is authority state"

        testCase "obstacle response prevents penetration" <| fun _ ->
            let wall =
                { Id = "wall"
                  Shape = KinematicShape.AxisAlignedBox(rect 3.0 -2.0 0.1 4.0)
                  Response = KinematicResponse.Slide }
            let arena = config [] [ wall ] 99 3
            let world = ContinuousArena.init arena |> ContinuousArena.apply arena Command.MoveEast
            let stopped = ContinuousArena.step arena world 2.0
            Expect.isLessThanOrEqual (stopped.Player.X + stopped.Player.Width) 3.0000001 "player stops at wall"

        testCase "independent harness runs are structurally identical" <| fun _ ->
            let pickup = { Id = "gem"; Bounds = rect 2.0 0.0 0.5 1.0; Kind = ContinuousArenaItemKind.Collectible 1 }
            let arena = config [ pickup ] [] 2 3
            let playable = ContinuousArena.playable arena (Map.ofList [ 'd', Command.MoveEast; 's', Command.MoveSouth ]) 0.5
            let script = [ [ 'd' ]; []; [ 's' ]; []; [ 'd' ] ]
            let first = Driver.runScript playable ContinuousArena.project script
            let second = Driver.runScript playable ContinuousArena.project script
            Expect.isTrue (Trace.equalFrames first second) "same commands and fixed steps produce the same trace"
    ]

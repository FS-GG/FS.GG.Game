module FS.GG.Game.Core.Fable.Consumer

open FS.GG.Game.Core

type ScenarioId = private ScenarioId of int

let private fail name expected actual =
    failwithf "%s: expected %A, got %A" name expected actual

let private equal name expected actual =
    if actual <> expected then
        fail name expected actual

let private some name value =
    match value with
    | Some value -> value
    | None -> failwithf "%s: expected Some, got None" name

let private cell col row : Cell = { Col = col; Row = row }

let private cellFixture () =
    let scenario = ScenarioId 1
    let actual = [ cell 2 -1; cell -3 4; cell 2 -2 ] |> List.sort
    let expected = [ cell -3 4; cell 2 -2; cell 2 -1 ]
    equal "scenario id" (ScenarioId 1) scenario
    equal "Cell structural ordering" expected actual

let private edgeFixture () =
    let a = cell -4 9
    let b = cell -3 9
    let forward = Edges.edgeBetween a b |> some "forward edge"
    let reverse = Edges.edgeBetween b a |> some "reverse edge"
    equal "edge relation is canonical" forward reverse
    equal "edge low cell" a forward.Lo
    equal "edge high cell" b forward.Hi
    equal "non-adjacent cells have no edge" None (Edges.edgeBetween a (cell -2 9))

let private losFixture () =
    let blocked = Set.ofList [ cell 1 0 ]
    let clear c = not (Set.contains c blocked)
    equal "supercover fixture is blocked" false (Los.lineOfSightBy Supercover clear (cell 0 0) (cell 2 1))
    equal "supercover fixture is symmetric" false (Los.lineOfSightBy Supercover clear (cell 2 1) (cell 0 0))

let private pathfindingFixture () =
    let blocked = Set.ofList [ cell 1 0 ]
    let walkable c =
        c.Col >= 0 && c.Col <= 2 && c.Row >= 0 && c.Row <= 1 && not (Set.contains c blocked)

    let actual =
        Pathfinding.astar FourWay 16 walkable (cell 0 0) (cell 2 0)
        |> some "bounded pathfinding fixture"

    equal
        "bounded pathfinding chooses the only open route"
        [ cell 0 0; cell 0 1; cell 1 1; cell 2 1; cell 2 0 ]
        actual

[<EntryPoint>]
let main _ =
    cellFixture ()
    edgeFixture ()
    losFixture ()
    pathfindingFixture ()
    printfn "FS.GG.Game.Core packed Fable consumer: OK"
    0

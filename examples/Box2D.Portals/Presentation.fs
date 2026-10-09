module Box2D.Portals.Presentation

open FS.GG.Game.Core
open FS.GG.Game.Physics.Box2D
open FS.GG.Game.Render

type AreaView =
    { Area: AreaTopology.AreaId
      Bindings: (string * Pose) list
      Scene: FS.GG.UI.Scene.Scene }

type Frame =
    { Tick: int64
      Alpha: float
      Areas: AreaView list }

let private paint = FS.GG.UI.Scene.Paint.stroke (FS.GG.UI.Scene.Colors.rgb 40uy 90uy 200uy) 0.05

/// Local metres and +Y-up are preserved. Areas have no invented shared screen layout.
let view alpha (previous: AreaSnapshot) (current: AreaSnapshot) =
    let copied = AreaPhysics.interpolate alpha previous current
    { Tick = current.Tick
      Alpha = alpha
      Areas =
        copied
        |> Map.toList
        |> List.choose (fun (area, poses) ->
            if Map.isEmpty poses then None
            else
                let bindings = Map.toList poses
                Some
                    { Area = area
                      Bindings = bindings
                      Scene = Adapter.drawPoints paint (bindings |> List.map (fun (_, pose) -> pose.Position)) }) }

/// Inspect actual point contents and identity coverage, including duplicate and missing entities.
let inspect (current: AreaSnapshot) (frame: Frame) =
    if frame.Tick <> current.Tick then failwith "Presentation tick differs."
    let identities =
        frame.Areas
        |> List.collect (fun area -> area.Bindings |> List.map (fun (entity, _) -> entity, area.Area))
    if identities.Length <> current.Ownership.Count || Map.ofList identities <> current.Ownership then
        failwith "Presentation entity coverage/ownership differs."
    let expectedAreas = current.Ownership |> Map.toList |> List.map snd |> List.distinct |> List.sort
    if List.map (fun area -> area.Area) frame.Areas <> expectedAreas then
        failwith "Presentation area order/coverage differs."
    for area in frame.Areas do
        if List.map fst area.Bindings <> (area.Bindings |> List.map fst |> List.sort) then
            failwith "Presentation entity order differs."
        let expected = area.Bindings |> List.map (fun (_, pose) -> Adapter.point pose.Position)
        match area.Scene.Nodes with
        | [ FS.GG.UI.Scene.Points(points, _) ] when points = expected -> ()
        | _ -> failwith "Presentation point contents differ from entity bindings."
    frame.Areas.Length, identities.Length

/// A finite real-world source example; no alpha reaches Step and no world advances per entity.
let qualify () =
    let traveller = { Scene.moving with Position = { X = -0.65; Y = 0. } }
    let ordinary =
        { Physics.circle "ordinary" 0.1 { X = -3.; Y = 3. } with
            LinearVelocity = { X = 1.; Y = 0.5 } }
    let snapshots =
        use runtime = new AreaRuntime(Scene.graph, Scene.settings, 0.05)
        [ for tick in 1 .. 5 do
            let commands =
                if tick = 1 then
                    [ { Area = Scene.areaA; Command = CreateBody traveller }
                      { Area = Scene.areaA; Command = CreateBody ordinary } ]
                else []
            yield runtime.Step commands |> Scene.advance ]
    let traversals = snapshots |> List.collect (fun snapshot -> snapshot.Traversals)
    match traversals with
    | [ event ] when event.Entity = traveller.Entity && event.Tick > 1L -> ()
    | _ -> failwith "Expected one traveller crossing after a copied pre-transfer tick."
    let final = List.last snapshots
    if final.Ownership <> Map.ofList [ ordinary.Entity, Scene.areaA; traveller.Entity, Scene.areaB ] then
        failwith "Expected ordinary/source and traveller/destination ownership."
    let frames =
        snapshots
        |> List.pairwise
        |> List.collect (fun (previous, current) ->
            [ for alpha in [ 0.; 0.5; 1. ] do
                let frame = view alpha previous current
                let nodes, points = inspect current frame
                if nodes < 1 || nodes > 2 || points <> 2 then failwith "Expected at most two occupied areas and exactly two points."
                for area in frame.Areas do
                    for (entity, pose) in area.Bindings do
                        let now = current.Areas[area.Area].Bodies[entity]
                        if current.Traversals |> List.exists (fun event -> event.Entity = entity) then
                            if pose <> now then failwith "Traveller interpolated across area coordinates."
                        elif entity = ordinary.Entity then
                            let old = previous.Areas[area.Area].Bodies[entity]
                            let close a b = abs (a - b) <= 1e-9
                            if not (close pose.Position.X (old.Position.X + alpha * (now.Position.X - old.Position.X))
                                    && close pose.Position.Y (old.Position.Y + alpha * (now.Position.Y - old.Position.Y))) then
                                failwith "Ordinary body did not interpolate."
                yield frame ])
    snapshots, frames

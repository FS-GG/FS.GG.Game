module Game.Core.Tests.AreaTopologyTests

open System
open Expecto
open FS.GG.Game.Core

let private p x y : Point = { X = x; Y = y }
let private get = function Ok value -> value | Error reason -> failwith reason
let private a = AreaTopology.AreaId "a"
let private b = AreaTopology.AreaId "b"
let private c = AreaTopology.AreaId "c"
let private opening x : AreaTopology.Opening = { Start = p x -2.; Finish = p x 2. }
let private identity = AreaTopology.rigidMap 0. (p 0. 0.) |> get
let private motion x y : AreaTopology.Motion = { Position = p x y; Velocity = p 6. 0.; Orientation = 0.3; AngularVelocity = 2. }
let private portal id source destination x : AreaTopology.Portal =
    { Id = AreaTopology.PortalId id; Source = source; Destination = destination
      SourceOpening = opening x; DestinationOpening = opening x
      Direction = AreaTopology.PositiveToNegative; Mapping = identity; Reverse = None }
let private decide graph radius previous current = AreaTopology.tryCrossing graph a Set.empty radius previous current
let private near actual expected = Expect.isLessThan (abs (actual - expected)) 1e-9 "mapped value"

[<Tests>]
let tests =
    testList "Game.Core AreaTopology" [
        test "arbitrary graph supports loops multiple entrances and one-way edges" {
            let graph = AreaTopology.create [a; b; c] [portal "ab" a b 0.; portal "bc" b c 0.; portal "ca" c a 0.; portal "ac" a c 1.] |> get
            Expect.equal (AreaTopology.outgoing a graph |> List.map (fun x -> x.Id)) [AreaTopology.PortalId "ab"; AreaTopology.PortalId "ac"] "stable entrances"
            Expect.equal (AreaTopology.outgoing b graph |> List.map (fun x -> x.Destination)) [c] "no implicit b-to-a edge"
        }
        test "swept fast mover crosses despite endpoints outside opening region" {
            let graph = AreaTopology.create [a; b] [portal "fast" a b 0.] |> get
            let hit = decide graph 0.5 (p -100. 0.) (motion 100. 0.) |> Option.get
            near hit.Fraction 0.5
            Expect.equal hit.SourcePoint (p 0. 0.) "intersection"
            Expect.equal hit.MappedMotion.Position (p 100. 0.) "tick-end position, no additional integration"
        }
        test "direction parallel line-start and missed aperture are refused" {
            let graph = AreaTopology.create [a; b] [portal "p" a b 0.] |> get
            for previous, current in [p 1. 0., motion -1. 0.; p -1. 0., motion -1. 2.; p 0. 0., motion 1. 0.; p -1. 3., motion 1. 3.] do
                Expect.isNone (decide graph 0. previous current) "no directed crossing"
            let reverseDirection = { portal "reverse" a b 0. with Direction = AreaTopology.NegativeToPositive }
            let reverseGraph = AreaTopology.create [a; b] [reverseDirection] |> get
            Expect.isSome (decide reverseGraph 0. (p 1. 0.) (motion -1. 0.)) "opposite direction can be explicitly selected"
        }
        test "whole-body enclosing-disc aperture clearance including exact fit" {
            let graph = AreaTopology.create [a; b] [portal "p" a b 0.] |> get
            Expect.isSome (decide graph 2. (p -1. 0.) (motion 1. 0.)) "exact aperture fit"
            Expect.isNone (decide graph 2.01 (p -1. 0.) (motion 1. 0.)) "too wide"
            Expect.isNone (decide graph 0.5 (p -1. 1.75) (motion 1. 1.75)) "centre fits but body clips end"
        }
        test "rotated map transforms pose and momentum and AreaTopology.inverse round trips" {
            let mapping = AreaTopology.rigidMap (Math.PI / 2.) (p 10. 20.) |> get
            let edge = portal "rotated" a b 0.
            let edge = { edge with Mapping = mapping; DestinationOpening = { Start = AreaTopology.mapPoint mapping edge.SourceOpening.Start; Finish = AreaTopology.mapPoint mapping edge.SourceOpening.Finish } }
            let graph = AreaTopology.create [a; b] [edge] |> get
            let hit = decide graph 0.5 (p -1. 0.) (motion 1. 0.) |> Option.get
            near hit.MappedMotion.Position.X 10.
            near hit.MappedMotion.Position.Y 21.
            near hit.MappedMotion.Velocity.X 0.
            near hit.MappedMotion.Velocity.Y 6.
            near hit.MappedMotion.Orientation (0.3 + Math.PI / 2.)
            Expect.equal hit.MappedMotion.AngularVelocity 2. "angular momentum parameter unchanged"
            let original = AreaTopology.mapMotion (AreaTopology.inverse mapping) hit.MappedMotion
            near original.Position.X 1.
            near original.Position.Y 0.
            near original.Velocity.X 6.
        }
        test "declared two-way pairs must have AreaTopology.inverse maps and opposite traversal" {
            let mapping = AreaTopology.rigidMap (Math.PI / 2.) (p 4. 5.) |> get
            let forward = portal "forward" a b 0.
            let forward = { forward with Mapping = mapping; Reverse = Some (AreaTopology.PortalId "back"); DestinationOpening = { Start = AreaTopology.mapPoint mapping forward.SourceOpening.Start; Finish = AreaTopology.mapPoint mapping forward.SourceOpening.Finish } }
            let back : AreaTopology.Portal = { Id = AreaTopology.PortalId "back"; Source = b; Destination = a; SourceOpening = forward.DestinationOpening; DestinationOpening = forward.SourceOpening; Direction = AreaTopology.NegativeToPositive; Mapping = AreaTopology.inverse mapping; Reverse = Some forward.Id }
            Expect.isOk (AreaTopology.create [a; b] [forward; back]) "valid pair"
            Expect.isError (AreaTopology.create [a; b] [forward; { back with Mapping = identity }]) "non-AreaTopology.inverse map"
            Expect.isError (AreaTopology.create [a; b] [forward; { back with Direction = AreaTopology.PositiveToNegative }]) "same physical direction"
            Expect.isError (AreaTopology.create [a; b] [forward; { back with Reverse = None }]) "non-reciprocal declaration"
        }
        test "malformed topology and non-finite maps are rejected" {
            let edge = portal "p" a b 0.
            for angle, translation in [nan, p 0. 0.; infinity, p 0. 0.; 0., p infinity 0.; 0., p 0. nan] do
                Expect.isError (AreaTopology.rigidMap angle translation) "finite map only"
            for areas, edges in [ [a], [edge]; [a; a; b], [edge]; [a; b], [edge; edge]; [a; b], [{ edge with SourceOpening = { Start = p 0. 0.; Finish = p 0. 0. } }]; [a; b], [{ edge with DestinationOpening = opening 1. }]; [a; b], [{ edge with Id = AreaTopology.PortalId "" }]; [AreaTopology.AreaId ""; b], [] ] do
                Expect.isError (AreaTopology.create areas edges) "malformed graph"
        }
        test "non-finite motion radius and mapped overflow never emit a crossing" {
            let graph = AreaTopology.create [a; b] [portal "p" a b 0.] |> get
            for radius, previous, current in [nan, p -1. 0., motion 1. 0.; infinity, p -1. 0., motion 1. 0.; -1., p -1. 0., motion 1. 0.; 0., p nan 0., motion 1. 0.; 0., p -1. 0., { motion 1. 0. with Velocity = p infinity 0. }; 0., p -1. 0., { motion 1. 0. with Orientation = nan }; 0., p -1. 0., { motion 1. 0. with AngularVelocity = infinity }; 0., p -Double.MaxValue 0., motion Double.MaxValue 0.] do
                Expect.isNone (decide graph radius previous current) "finite decisions only"
            Expect.isNone (AreaTopology.tryCrossing graph c Set.empty 0. (p -1. 0.) (motion 1. 0.)) "unknown area"
        }
        test "competing crossings choose earliest then stable ID independent of insertion order" {
            let early = portal "z-early" a b 0.
            let later = portal "a-later" a c 1.
            let tie = portal "a-tie" a c 0.
            for edges in [[later; early; tie]; [tie; early; later]] do
                let graph = AreaTopology.create [a; b; c] edges |> get
                let hit = decide graph 0. (p -1. 0.) (motion 2. 0.) |> Option.get
                Expect.equal hit.Portal.Id tie.Id "one stable earliest winner"
                let unarmed = AreaTopology.tryCrossing graph a (Set.singleton tie.Id) 0. (p -1. 0.) (motion 2. 0.) |> Option.get
                Expect.equal unarmed.Portal.Id early.Id "blocked opening excluded"
                Expect.isNone (AreaTopology.tryCrossing graph a (Set.ofList [early.Id; tie.Id; later.Id]) 0. (p -1. 0.) (motion 2. 0.)) "all unarmed"
        }
    ]

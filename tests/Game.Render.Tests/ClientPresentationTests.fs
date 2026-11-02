module Game.Render.Tests.ClientPresentationTests

// FS.GG.Game#810 (GAME-RENDER-02.9), EXPERIMENTAL proof: the client-effect vocabulary a guest returns as `Local`s, the
// presentation reducer, its link to pending commands, the default codec's golden vectors and fuzzing over its whole
// accepted range, and ReferenceGame's planned move end to end in process. See
// docs/roadmaps/game-render-02-client-presentation.md. The module under test is not shipped.

open Expecto
open FsCheck
open FsCheck.FSharp
open FS.GG.UI.Scene
open FS.GG.Game.Render
open FS.GG.Game.Reference
open FS.GG.Game.Reference.ReferenceState
open FS.GG.Game.Reference.ExperimentalClientPresentation
open FS.GG.Game.Reference.ExperimentalPlanGame

type private Intent = GuestIntent<ClientEffect<Custom>, Command>

// ---------------------------------------------------------------------------------------------------------------------
// The test codecs for the game's own payloads
// ---------------------------------------------------------------------------------------------------------------------

let private customBytes (custom: Custom) =
    match custom with
    | Arm -> [| 0uy |]
    | Disarm -> [| 1uy |]

let private customOf (data: byte array) =
    match data with
    | [| 0uy |] -> Ok Arm
    | [| 1uy |] -> Ok Disarm
    | _ -> Error "unknown custom"

let private i32 (value: int) =
    [| byte value; byte (value >>> 8); byte (value >>> 16); byte (value >>> 24) |]

let private commandBytes (MoveTo(plan, x, y)) =
    let name = System.Text.Encoding.UTF8.GetBytes plan
    Array.concat [ [| byte name.Length |]; name; i32 x; i32 y ]

let private commandOf (data: byte array) =
    if data.Length < 1 || data.Length <> 1 + int data.[0] + 8 then
        Error "bad command"
    else
        let n = int data.[0]
        let read at = int data.[at] ||| (int data.[at + 1] <<< 8) ||| (int data.[at + 2] <<< 16) ||| (int data.[at + 3] <<< 24)
        let plan = System.Text.Encoding.UTF8.GetString(data, 1, n)

        if System.Text.Encoding.UTF8.GetBytes plan <> data.[1..n] then
            Error "bad plan text"
        else
            Ok(MoveTo(plan, read (1 + n), read (5 + n)))

let private encode (intents: Intent list) = Codec.encode customBytes commandBytes intents
let private decode (data: byte array) = Codec.decode customOf commandOf data

let private point x y : Point = { X = x; Y = y }

// ---------------------------------------------------------------------------------------------------------------------
// Golden vectors (one per effect, element, anchor and intent kind). The design doc quotes them.
// ---------------------------------------------------------------------------------------------------------------------

let goldenVectors: (string * Intent list * string) list =
    let local effect = GuestIntent.Local effect

    [
        "empty decision", [], "010000"
        "select primary token", [ local (Select(Primary, [ "token" ])) ], "01010000010001000500746f6b656e"
        "add group a b", [ local (Add(Group, [ "a"; "b" ])) ], "0101000002010200010061010062"
        "remove group a", [ local (Remove(Group, [ "a" ])) ], "0101000003010100010061"
        "clear selection", [ local ClearSelection ], "0101000004"
        "highlight token target", [ local (Highlight("token", Target)) ], "01010000100500746f6b656e00"
        "unhighlight token invalid", [ local (Unhighlight("token", Invalid)) ], "01010000110500746f6b656e02"
        "clear highlights", [ local ClearHighlights ], "0101000012"
        "plan move arrow token to (156, 60)",
        [ local (Plan(PlanId "move", [ Arrow(AtObject "token", AtPoint(point 156.0 60.0), Move) ])) ],
        "010100002004006d6f7665010001010500746f6b656e00009c0000003c000000"
        "plan path of three waypoints, attack",
        [
            local (
                Plan(PlanId "p", [ Path([ AtPoint(point 0.0 0.0); AtPoint(point 1.5 -2.25); AtObject "aim" ], Attack) ])
            )
        ],
        "010100002001007001000203000000000000000000000080010000c0fdffff01030061696d01"
        "plan ghost, structure enemy, icon, team 2, invalid",
        [
            local (
                Plan(
                    PlanId "g",
                    [
                        GhostAt
                            {
                                Family = StructureGhost
                                Identity = Enemy
                                Icon = Some "structure"
                                Team = Some 2
                                Anchor = point 48.0 16.0
                                Footprint = { Width = 2; Height = 1 }
                                Valid = false
                            }
                    ]
                )
            )
        ],
        "01010000200100670100030103090073747275637475726501020000300000001000000200010000"
        "plan ghost, unit own, no icon, no team, valid",
        [
            local (
                Plan(
                    PlanId "g",
                    [
                        GhostAt
                            {
                                Family = UnitGhost
                                Identity = Own
                                Icon = None
                                Team = None
                                Anchor = point 16.0 16.0
                                Footprint = { Width = 1; Height = 1 }
                                Valid = true
                            }
                    ]
                )
            )
        ],
        "0101000020010067010003000000000000100000001000000100010001"
        "plan area circle support", [ local (Plan(PlanId "a", [ Area(Circle(point 10.0 10.0, 4.5), Support) ])) ], "010100002001006101000400000a0000000a00008004000002"
        "plan area box move",
        [
            local (
                Plan(
                    PlanId "a",
                    [
                        Area(
                            Box
                                {
                                    X = -8.0
                                    Y = 8.0
                                    Width = 32.0
                                    Height = 0.0
                                },
                            Move
                        )
                    ]
                )
            )
        ],
        "01010000200100610100040100f8ffff00080000002000000000000000"
        "plan target line", [ local (Plan(PlanId "t", [ TargetLine(AtObject "token", AtObject "aim") ])) ], "0101000020010074010005010500746f6b656e01030061696d"
        "plan label", [ local (Plan(PlanId "l", [ Label(AtPoint(point 1.0 2.0), "Rally ✓") ])) ], "010100002001006c010006000001000000020000090052616c6c7920e29c93"
        "commit clear when shown", [ local (Commit(PlanId "move", ClearWhenShown)) ], "010100002104006d6f766500"
        "commit keep when shown", [ local (Commit(PlanId "move", KeepWhenShown)) ], "010100002104006d6f766501"
        "clear plan", [ local (ClearPlan(PlanId "move")) ], "010100002204006d6f7665"
        "clear plans", [ local ClearPlans ], "0101000023"
        "custom arm", [ local (Custom Arm) ], "010100007f0100000000"
        "commit and submit move (the Enter decision)",
        [ local (Commit(PlanId "move", ClearWhenShown)); GuestIntent.Submit(MoveTo("move", 156, 60)) ],
        "010200002104006d6f766500010d000000046d6f76659c0000003c000000"
        "coordinate extremes", [ local (Plan(PlanId "x", [ Arrow(AtPoint(point 8388607.0 -8388607.0), AtPoint(point 0.00390625 -0.00390625), Move) ])) ], "01010000200100780100010000ffff7f000100800001000000ffffffff00"
    ]

// ---------------------------------------------------------------------------------------------------------------------
// Generators over the codec's whole accepted range
// ---------------------------------------------------------------------------------------------------------------------

/// Unicode scalar values, weighted towards ASCII but reaching the astral planes.
let private scalar: Gen<string> =
    Gen.frequency
        [
            6, Gen.choose (0x20, 0x7E)
            2, Gen.choose (0x80, 0xD7FF)
            1, Gen.choose (0xE000, 0xFFFD)
            1, Gen.choose (0x10000, 0x10FFFF)
        ]
    |> Gen.map System.Char.ConvertFromUtf32

let private textOf (minimum: int) : Gen<string> =
    gen {
        let! length = Gen.frequency [ 8, Gen.choose (minimum, 12); 1, Gen.choose (minimum, 400) ]
        let! parts = Gen.listOfLength length scalar
        let text = String.concat "" parts
        // Keep within the byte limit: cut whole scalars from the end.
        let mutable cut = parts

        while System.Text.Encoding.UTF8.GetByteCount(String.concat "" cut) > Codec.maxString do
            cut <- List.take (cut.Length - 1) cut

        return if cut.Length = parts.Length then text else String.concat "" cut
    }

let private idText = textOf 1

/// A coordinate on the format's grid, across its whole range.
let private coordinate: Gen<float> =
    Gen.frequency
        [
            4, Gen.choose (-4096 * 256, 4096 * 256)
            // FsCheck's Gen.choose overflows on a span wider than Int32: draw a sign and a magnitude instead.
            2,
            Gen.map2
                (fun negative magnitude -> if negative then -magnitude else magnitude)
                (ArbMap.generate<bool> ArbMap.defaults)
                (Gen.choose (0, 2147483392))
            1, Gen.elements [ -2147483392; 2147483392; 0; 1; -1 ]
        ]
    |> Gen.map (fun fixedPoint -> float fixedPoint / 256.0)

let private genPoint = Gen.map2 point coordinate coordinate

let private genAnchor =
    Gen.oneof [ Gen.map AtPoint genPoint; Gen.map AtObject idText ]

let private genStyle = Gen.elements [ Move; Attack; Support ]

let private genGhost =
    gen {
        let! family = Gen.elements [ UnitGhost; StructureGhost; TerrainGhost; ItemGhost; MarkerGhost ]
        let! identity = Gen.elements [ Own; Ally; Neutral; Enemy; Unknown ]
        let! icon = Gen.oneof [ Gen.constant None; Gen.map Some idText ]
        let! team = Gen.oneof [ Gen.constant None; Gen.map Some (Gen.choose (0, 0xFFFF)) ]
        let! anchor = genPoint
        let! w = Gen.choose (1, 0xFFFF)
        let! h = Gen.choose (1, 0xFFFF)
        let! valid = ArbMap.generate<bool> ArbMap.defaults

        return
            {
                Family = family
                Identity = identity
                Icon = icon
                Team = team
                Anchor = anchor
                Footprint = { Width = w; Height = h }
                Valid = valid
            }
    }

let private genElement =
    Gen.oneof
        [
            Gen.map3 (fun a b s -> Arrow(a, b, s)) genAnchor genAnchor genStyle
            gen {
                let! n = Gen.choose (2, 6)
                let! points = Gen.listOfLength n genAnchor
                let! style = genStyle
                return Path(points, style)
            }
            Gen.map GhostAt genGhost
            gen {
                let! circle = ArbMap.generate<bool> ArbMap.defaults
                let! style = genStyle

                if circle then
                    let! centre = genPoint
                    let! r = Gen.choose (1, 2147483392)
                    return Area(Circle(centre, float r / 256.0), style)
                else
                    let! x = coordinate
                    let! y = coordinate
                    let! w = Gen.choose (0, 2147483392)
                    let! h = Gen.choose (0, 2147483392)

                    return
                        Area(
                            Box
                                {
                                    X = x
                                    Y = y
                                    Width = float w / 256.0
                                    Height = float h / 256.0
                                },
                            style
                        )
            }
            Gen.map2 (fun a b -> TargetLine(a, b)) genAnchor genAnchor
            Gen.map2 (fun a t -> Label(a, t)) genAnchor (textOf 0)
        ]

let private genRole = Gen.elements [ Primary; Group ]
let private genHighlight = Gen.elements [ Target; Valid; Invalid ]
let private genIds = Gen.sized (fun size -> Gen.choose (0, min 6 (size + 1)) |> Gen.bind (fun n -> Gen.listOfLength n idText))
let private genPlanId = Gen.map PlanId idText

let private genEffect: Gen<ClientEffect<Custom>> =
    Gen.oneof
        [
            Gen.map2 (fun r ids -> Select(r, ids)) genRole genIds
            Gen.map2 (fun r ids -> Add(r, ids)) genRole genIds
            Gen.map2 (fun r ids -> Remove(r, ids)) genRole genIds
            Gen.constant ClearSelection
            Gen.map2 (fun id r -> Highlight(id, r)) idText genHighlight
            Gen.map2 (fun id r -> Unhighlight(id, r)) idText genHighlight
            Gen.constant ClearHighlights
            gen {
                let! plan = genPlanId
                let! n = Gen.choose (0, 4)
                let! elements = Gen.listOfLength n genElement
                return Plan(plan, elements)
            }
            Gen.map2 (fun p s -> Commit(p, s)) genPlanId (Gen.elements [ ClearWhenShown; KeepWhenShown ])
            Gen.map ClearPlan genPlanId
            Gen.constant ClearPlans
            Gen.map Custom (Gen.elements [ Arm; Disarm ])
        ]

/// Effects over a small pool of ids, so plans, commits and links collide as they do in a real game.
let private genPooledEffect: Gen<ClientEffect<Custom>> =
    let plan = Gen.elements [ PlanId "a"; PlanId "b"; PlanId "move" ]
    let id = Gen.elements [ "token"; "aim"; "x" ]

    Gen.oneof
        [
            Gen.map2 (fun p n -> Plan(p, List.replicate n (Label(AtPoint(point 1.0 1.0), "")))) plan (Gen.choose (0, 2))
            Gen.map2 (fun p s -> Commit(p, s)) plan (Gen.elements [ ClearWhenShown; KeepWhenShown ])
            Gen.map ClearPlan plan
            Gen.constant ClearPlans
            Gen.map2 (fun r i -> Select(r, [ i ])) genRole id
            Gen.map2 (fun r i -> Add(r, [ i ])) genRole id
            Gen.map2 (fun r i -> Remove(r, [ i ])) genRole id
        ]

let private genCommand =
    gen {
        let! plan = Gen.elements [ "move"; "p"; "é" ]
        let! x = ArbMap.generate<int> ArbMap.defaults
        let! y = ArbMap.generate<int> ArbMap.defaults
        return MoveTo(plan, x, y)
    }

let private genIntents: Gen<Intent list> =
    gen {
        let! n = Gen.choose (0, 6)

        return!
            Gen.listOfLength
                n
                (Gen.frequency [ 5, Gen.map GuestIntent.Local genEffect; 1, Gen.map GuestIntent.Submit genCommand ])
    }

/// The number of generated cases per property: FSGG_FUZZ_CASES, 1000 by default.
let private cases =
    match System.Environment.GetEnvironmentVariable "FSGG_FUZZ_CASES" with
    | null -> 1000
    | text -> int text

let private propertyN (count: int) name (gen: Gen<'T>) (check: 'T -> unit) =
    testCase name
    <| fun () ->
        let prop =
            Prop.forAll (Arb.fromGen gen) (fun value ->
                check value
                true)

        Check.One(TestSupport.propertyConfig count, prop)

let private property name gen check = propertyN cases name gen check

let private encoded (intents: Intent list) =
    match encode intents with
    | Ok data -> data
    | Error reason -> failtestf "a generated decision was refused: %s" reason

let private isRefused (result: Result<'T, string>) =
    match result with
    | Error _ -> true
    | Ok _ -> false

// ---------------------------------------------------------------------------------------------------------------------
// The session, with a network the test holds and drains
// ---------------------------------------------------------------------------------------------------------------------

let private start: Model =
    {
        Screen = Playing
        Room = Atrium
        X = 1
        DestinationActive = false
        CameraTransition = false
        VaultClear = false
        Aim = 200, 40
        Tick = 0
        Selected = None
    }

let private event action source target (at: Point option) =
    InputEvent.create source action target at InputModifiers.none false

let private press target x y =
    event InputAction.Activate InputSource.Pointer target (Some(point x y))

let private enter = event InputAction.Activate InputSource.Keyboard (Some "token") None
let private escape = event InputAction.Cancel InputSource.Keyboard None None

type private Rig =
    {
        Session: Session
        Route: InputRoute
        Sent: ResizeArray<CommandEnvelope<Command>>
        Said: ResizeArray<string>
        Drain: unit -> unit
    }

let private rig (window: int) =
    let network = System.Collections.Generic.Queue<unit -> unit>()
    let route = InputRoute.create ()
    let sent = ResizeArray<CommandEnvelope<Command>>()
    let said = ResizeArray<string>()

    let running =
        new Session("player", start, window, route, network.Enqueue, (fun _ _ -> ()), ignore, sent.Add, said.Add)

    let drain () =
        while network.Count > 0 do
            (network.Dequeue()) ()

    {
        Session = running
        Route = route
        Sent = sent
        Said = said
        Drain = drain
    }

/// Press the token, drag to `x, y`, release there.
let private drag (r: Rig) (fromX: float) (x: float) (y: float) =
    InputRoute.report (press (Some "token") fromX 60.0) r.Route
    r.Session.Input(DragTo(point ((fromX + x) / 2.0) y))
    r.Session.Input(DragTo(point x y))
    r.Session.Input(Release(point x y, true))

let private phases (r: Rig) =
    r.Session.Presentation.Plans |> List.map (fun entry -> entry.Phase)

/// Settlements of each open kind, read from a real pipeline (the type is opaque).
let private settlements () =
    let stage = CommandStage.create (fun (v: uint64) -> v)
    let pipeline = InputPipeline.create (InputGuest.sync (fun _ -> GuestOutcome.Decided [])) stage
    let model = InputPipeline.init pipeline "s" None 0UL

    let submit model =
        fst (InputPipeline.update pipeline (PipelineMsg.Submit "c") model)

    let model = submit (submit (submit (submit model)))
    let ids = InputPipeline.unsettled model |> List.map fst

    let answer (id, ack) model =
        fst (InputPipeline.update pipeline (PipelineMsg.Acknowledged(id, ack)) model)

    let model =
        model
        |> answer (ids.[1], CommandAck.accepted)
        |> answer (ids.[2], CommandAck.queued)
        |> answer (ids.[3], CommandAck.started)

    InputPipeline.unsettled model |> List.map snd

let private luminance (c: Color) =
    let channel (v: byte) =
        let s = float v / 255.0
        if s <= 0.04045 then s / 12.92 else ((s + 0.055) / 1.055) ** 2.4

    0.2126 * channel c.Red + 0.7152 * channel c.Green + 0.0722 * channel c.Blue

let private contrast a b =
    let la, lb = luminance a, luminance b
    (max la lb + 0.05) / (min la lb + 0.05)

let private rects (nodes: SceneNode list) =
    nodes
    |> List.choose (fun node ->
        match node with
        | SceneNode.Rectangle(bounds, fill) -> Some(bounds, fill)
        | _ -> None)

[<Tests>]
let tests =
    testList
        "ExperimentalClientPresentation"
        [
            testList
                "codec golden vectors"
                [
                    for name, intents, hex in goldenVectors ->
                        test name {
                            let data = encoded intents
                            Expect.equal (Codec.hex data) hex "the vector's bytes"
                            Expect.equal (decode data) (Ok intents) "the vector decodes to its decision"
                        }
                ]

            testList
                "codec fuzzing over the accepted range"
                [
                    property "every decision in range round-trips exactly" genIntents (fun intents ->
                        let data = encoded intents
                        Expect.equal (decode data) (Ok intents) "decode after encode")

                    property "every strict prefix of an encoding is refused, and never raises" genIntents (fun intents ->
                        let data = encoded intents

                        // Every prefix of a short encoding, and 64 spread over a long one (decoding is linear).
                        let lengths =
                            if data.Length <= 256 then
                                [ 0 .. data.Length - 1 ]
                            else
                                [ for i in 0..63 -> i * (data.Length - 1) / 63 ] @ [ data.Length - 1 ] |> List.distinct

                        for length in lengths do
                            Expect.isTrue (isRefused (decode data.[0 .. length - 1])) (sprintf "prefix of %d bytes" length))

                    property "trailing bytes are refused" (Gen.zip genIntents (ArbMap.generate<byte> ArbMap.defaults)) (fun (intents, extra) ->
                        Expect.isTrue (isRefused (decode (Array.append (encoded intents) [| extra |]))) "one trailing byte")

                    property
                        "a one-byte mutation never raises, and whatever it decodes re-encodes to the same bytes"
                        (Gen.zip3 genIntents (ArbMap.generate<int> ArbMap.defaults) (ArbMap.generate<byte> ArbMap.defaults))
                        (fun (intents, at, value) ->
                            let data = encoded intents
                            let mutated = Array.copy data
                            mutated.[abs (at % mutated.Length)] <- value

                            match decode mutated with
                            | Ok decoded -> Expect.equal (encode decoded) (Ok mutated) "canonical"
                            | Error _ -> ())

                    property "random bytes never raise, and any decision they decode is canonical" (ArbMap.generate<byte array> ArbMap.defaults) (fun data ->
                        let data = Array.append [| Codec.version |] data

                        match decode data with
                        | Ok decoded -> Expect.equal (encode decoded) (Ok data) "canonical"
                        | Error _ -> ())

                    test "a coordinate off the grid is quantised half up" {
                        let intents =
                            [ GuestIntent.Local(Plan(PlanId "q", [ Arrow(AtPoint(point 0.001953125 -0.001953125), AtPoint(point 1.0 1.0), Move) ])) ]

                        let expected =
                            [ GuestIntent.Local(Plan(PlanId "q", [ Arrow(AtPoint(point 0.00390625 0.0), AtPoint(point 1.0 1.0), Move) ])) ]

                        Expect.equal (decode (encoded intents)) (Ok expected) "Floor(v x 256 + 0.5) / 256"
                    }

                    test "a positive circle radius that rounds to zero is refused before writing" {
                        for radius in [ System.Double.Epsilon; 0.001; 1.0 / 512.0 - 1e-12 ] do
                            let intents = [ GuestIntent.Local(Plan(PlanId "r", [ Area(Circle(point 0.0 0.0, radius), Move) ])) ]
                            Expect.isError (encode intents) (sprintf "radius %.17g has no positive Q24.8 representation" radius)
                    }

                    test "non-grid circle radii round to a positive canonical wire radius" {
                        for radius, expectedRadius in [ 1.0 / 512.0, 1.0 / 256.0; 1.0 / 256.0, 1.0 / 256.0; 0.005, 1.0 / 256.0 ] do
                            let circle r = [ GuestIntent.Local(Plan(PlanId "r", [ Area(Circle(point 0.0 0.0, r), Move) ])) ]
                            let bytes = encoded (circle radius)
                            Expect.equal (decode bytes) (Ok(circle expectedRadius)) "half-up radius remains positive"
                            Expect.equal (encode (circle expectedRadius)) (Ok bytes) "accepted bytes are canonical"
                    }

                    test "nonfinite and nonpositive circle radii fail closed" {
                        for radius in [ nan; infinity; -infinity; 0.0; -0.001 ] do
                            Expect.isError
                                (encode [ GuestIntent.Local(Plan(PlanId "r", [ Area(Circle(point 0.0 0.0, radius), Move) ])) ])
                                (sprintf "radius %g is invalid" radius)
                    }

                    test "the limits are accepted exactly at their bound and refused one past it" {
                        let id = String.replicate Codec.maxString "a"
                        Expect.isOk (encode [ GuestIntent.Local(Select(Group, [ id ])) ]) "a 1024-byte id"
                        Expect.isError (encode [ GuestIntent.Local(Select(Group, [ id + "a" ])) ]) "a 1025-byte id"
                        let many = List.replicate Codec.maxItems "a"
                        Expect.isOk (encode [ GuestIntent.Local(Select(Group, many)) ]) "1024 ids"
                        Expect.isError (encode [ GuestIntent.Local(Select(Group, "a" :: many)) ]) "1025 ids"
                        Expect.isOk (encode (List.replicate Codec.maxItems (GuestIntent.Local ClearPlans))) "1024 intents"
                        Expect.isError (encode (List.replicate (Codec.maxItems + 1) (GuestIntent.Local ClearPlans))) "1025 intents"
                        let ok = Codec.encode (fun (data: byte array) -> data) commandBytes [ GuestIntent.Local(Custom(Array.zeroCreate Codec.maxPayload)) ]
                        Expect.isOk ok "a 65536-byte payload"
                        let over = Codec.encode (fun (data: byte array) -> data) commandBytes [ GuestIntent.Local(Custom(Array.zeroCreate (Codec.maxPayload + 1))) ]
                        Expect.isError over "a 65537-byte payload"
                        let edge v = encode [ GuestIntent.Local(Plan(PlanId "e", [ Label(AtPoint(point v 0.0), "") ])) ]
                        Expect.isOk (edge Codec.maxCoordinate) "the largest coordinate"
                        Expect.isOk (edge -Codec.maxCoordinate) "the smallest coordinate"
                        Expect.isError (edge (Codec.maxCoordinate + 0.002)) "one quantum beyond"
                        Expect.isError (edge nan) "NaN"
                        Expect.isError (edge infinity) "infinity"
                    }

                    test "values outside the vocabulary's range are refused by the encoder, not written" {
                        let refused element =
                            Expect.isError (encode [ GuestIntent.Local(Plan(PlanId "r", [ element ])) ]) (sprintf "%A" element)

                        refused (Path([ AtPoint(point 0.0 0.0) ], Move))
                        refused (Arrow(AtObject "", AtPoint(point 0.0 0.0), Move))
                        refused (Area(Circle(point 0.0 0.0, 0.0), Move))
                        refused (Area(Box { X = 0.0; Y = 0.0; Width = -1.0; Height = 0.0 }, Move))

                        let ghost =
                            {
                                Family = UnitGhost
                                Identity = Own
                                Icon = None
                                Team = None
                                Anchor = point 0.0 0.0
                                Footprint = { Width = 1; Height = 1 }
                                Valid = true
                            }

                        refused (GhostAt { ghost with Icon = Some "" })
                        refused (GhostAt { ghost with Team = Some -1 })
                        refused (GhostAt { ghost with Team = Some 65536 })
                        refused (GhostAt { ghost with Footprint = { Width = 0; Height = 1 } })
                        Expect.isError (encode [ GuestIntent.Local(Plan(PlanId "", [])) ]) "an empty plan id"
                    }

                    test "malformed encodings are refused with a reason" {
                        let reason (hex: string) =
                            let data = System.Convert.FromHexString hex

                            match decode data with
                            | Error reason -> reason
                            | Ok decoded -> failtestf "%s decoded to %A" hex decoded

                        Expect.stringContains (reason "020000") "format version" "another version"
                        Expect.stringContains (reason "0101000099") "unknown effect tag 0x99" "an unknown effect"
                        Expect.stringContains (reason "01010002") "unknown intent kind" "an unknown intent"
                        Expect.stringContains (reason "01010000010001000000") "empty" "an empty id"
                        Expect.stringContains (reason "0101000001000100010080") "UTF-8" "a lone continuation byte"
                        Expect.stringContains (reason "0101000001000100020061") "truncated" "a short id"
                        Expect.stringContains (reason "0101000001000100010061ff") "trailing" "a trailing byte"
                        Expect.stringContains (reason "010100000102") "selection role" "an unknown role"
                        Expect.stringContains (reason "010100002101006d02") "settle" "an unknown settle policy"
                        Expect.stringContains (reason "01010000200100700100020000") "path" "a path of no waypoints"
                        Expect.stringContains (reason ("01010000010001000104" + String.replicate 1025 "61")) "over 1024" "a 1025-byte id"
                        Expect.stringContains (reason "0101000001010104") "items, over 1024" "1025 ids"
                        Expect.stringContains (reason "010104") "items, over 1024" "1025 intents"
                        Expect.stringContains (reason "010100002001006301000600ffffff7f000000000000") "out of range" "a coordinate past the range"
                        Expect.stringContains (reason "010100002001007001000201000000000000000000000000") "under 2" "a path of one waypoint"
                        Expect.stringContains (reason "0101000020010067010003000000000000000000000000000000010001") "not positive" "a ghost of no footprint"
                        Expect.stringContains (reason "0101000020010067010003000000000000000000000000000001000102") "not 0 or 1" "a validity flag of 2"
                    }
                ]

            testList
                "presentation laws"
                [
                    property "applying any sequence of effects is total and deterministic" (Gen.listOf genEffect) (fun effects ->
                        Expect.equal (applyAll effects empty) (applyAll effects empty) "same effects, same presentation")

                    property "a plan is idempotent, so coalescing a preview loses nothing" (Gen.zip3 genPlanId (Gen.listOf genElement) (Gen.listOf genElement)) (fun (plan, first, second) ->
                        let once, _ = apply (Plan(plan, second)) empty
                        let twice, _ = applyAll [ Plan(plan, second); Plan(plan, second) ] empty
                        let coalesced, _ = applyAll [ Plan(plan, first); Plan(plan, second) ] empty
                        Expect.equal twice once "the same plan twice is once"
                        Expect.equal coalesced.Plans once.Plans "the latest preview is what a coalesced one draws")

                    property
                        "invariants hold over any interleaving of effects, sends, failures and tracking"
                        (Gen.listOf (
                            Gen.frequency
                                [
                                    3, Gen.map Choice1Of4 genEffect
                                    6, Gen.map Choice1Of4 genPooledEffect
                                    2, Gen.map Choice2Of4 (Gen.elements [ "a"; "b"; "move" ])
                                    1, Gen.map Choice3Of4 (Gen.choose (0, 5))
                                    2, Gen.map Choice4Of4 (Gen.choose (0, 63))
                                ]
                        ))
                        (fun steps ->
                            let open' = settlements ()
                            let mutable p = empty
                            let mutable issued = 0UL
                            let ids = ResizeArray<CommandId>()

                            for step in steps do
                                match step with
                                | Choice1Of4 effect -> p <- fst (apply effect p)
                                | Choice2Of4 plan ->
                                    issued <- issued + 1UL
                                    let id = (CommandId.tryParse (sprintf "player:%d" issued)).Value
                                    ids.Add id

                                    let envelope =
                                        let stage = CommandStage.create (fun (v: uint64) -> v)
                                        let pipeline = InputPipeline.create (InputGuest.sync (fun _ -> GuestOutcome.Decided [])) stage
                                        let model = InputPipeline.init pipeline "player" (Some(issued - 1UL)) 0UL

                                        InputPipeline.update pipeline (PipelineMsg.Submit(MoveTo(plan, 0, 0))) model
                                        |> snd
                                        |> List.pick (fun effect ->
                                            match effect with
                                            | PipelineEffect.Send envelope -> Some envelope
                                            | _ -> None)

                                    p <- sent planOf envelope p
                                | Choice3Of4 which when ids.Count > 0 ->
                                    let id = ids.[which % ids.Count]

                                    let failure =
                                        match which % 3 with
                                        | 0 -> PipelineFailure.commandRejected id "no"
                                        | 1 -> PipelineFailure.commandCancelled id "stop"
                                        | _ -> PipelineFailure.commandUnconfirmed id

                                    p <- failed failure p
                                | Choice3Of4 _ -> ()
                                | Choice4Of4 mask ->
                                    let unsettled =
                                        ids
                                        |> Seq.indexed
                                        |> Seq.filter (fun (i, _) -> (mask >>> (i % 6)) &&& 1 = 1)
                                        |> Seq.map (fun (i, id) -> id, open'.[i % open'.Length])
                                        |> List.ofSeq

                                    let predicted = ids |> Seq.filter (fun id -> CommandId.sequence id % 2UL = 0UL) |> List.ofSeq
                                    p <- track unsettled predicted p

                                for entry in p.Plans do
                                    match entry.Phase with
                                    | Draft
                                    | Committed -> Expect.isNone entry.Command "an unlinked plan has no command"
                                    | _ -> Expect.isSome entry.Command "a linked plan has its command"

                                let count predicate = p.Plans |> List.filter predicate |> List.length

                                for entry in p.Plans do
                                    Expect.isLessThanOrEqual (count (fun other -> other.Id = entry.Id && other.Phase = Draft)) 1 "one draft per id"
                                    Expect.isLessThanOrEqual (count (fun other -> other.Id = entry.Id && other.Phase = Committed)) 1 "one unsent commit per id"

                                    match entry.Command with
                                    | Some id -> Expect.equal (count (fun other -> other.Command = Some id)) 1 "a command shows one plan"
                                    | None -> ()

                                Expect.equal (p.Plans |> List.map (fun e -> e.Order) |> List.distinct).Length p.Plans.Length "orders are unique"
                                Expect.equal (List.distinct p.Group) p.Group "the group has no duplicates")

                    property "Clear and ClearPlans never remove a plan that follows an open or applied command" (Gen.listOf genEffect) (fun effects ->
                        let linked =
                            { empty with
                                Plans =
                                    [
                                        for i, phase in List.indexed [ Pending; Accepted; Queued; Running; Applied ] ->
                                            {
                                                Id = PlanId "move"
                                                Elements = []
                                                Phase = phase
                                                Settle = ClearWhenShown
                                                Command = Some((CommandId.tryParse (sprintf "player:%d" (i + 1))).Value)
                                                Order = uint64 (i + 1)
                                            }
                                    ]
                                Next = 6UL
                            }

                        let after, _ = applyAll (ClearPlans :: ClearPlan(PlanId "move") :: effects) linked
                        let kept = after.Plans |> List.filter (fun entry -> entry.Command.IsSome) |> List.map (fun e -> e.Phase)
                        Expect.equal kept [ Pending; Accepted; Queued; Running; Applied ] "every linked plan is still there")

                    test "the first final outcome wins, and tracking never reopens a failed plan" {
                        let id = (CommandId.tryParse "player:7").Value

                        let p =
                            { empty with
                                Plans =
                                    [
                                        {
                                            Id = PlanId "move"
                                            Elements = []
                                            Phase = Pending
                                            Settle = ClearWhenShown
                                            Command = Some id
                                            Order = 1UL
                                        }
                                    ]
                            }

                        let refused = failed (PipelineFailure.commandRejected id "blocked") p
                        Expect.equal (refused.Plans |> List.map (fun e -> e.Phase)) [ Refused "blocked" ] "refused"
                        let again = failed (PipelineFailure.commandCancelled id "late") refused
                        Expect.equal again.Plans refused.Plans "a later failure changes nothing"
                        let tracked = track [ id, (settlements ()).[0] ] [ id ] refused
                        Expect.equal tracked.Plans refused.Plans "tracking does not reopen it"
                    }

                    test "the primary selection holds at most one object, and the group is an ordered set" {
                        let p, _ = applyAll [ Select(Primary, [ "a"; "b" ]) ] empty
                        Expect.isNone p.Primary "two primaries are refused"
                        Expect.isNonEmpty p.Diagnostics "with a diagnostic"
                        let p, _ = applyAll [ Select(Primary, [ "a" ]); Add(Group, [ "x"; "y"; "x" ]); Remove(Group, [ "y" ]) ] empty
                        Expect.equal p.Primary (Some "a") "primary"
                        Expect.equal p.Group [ "x" ] "group"
                    }

                    test "track maps open settlements, predicted commands, and shown ones" {
                        let id = (CommandId.tryParse "player:1").Value

                        let base' =
                            { empty with
                                Plans =
                                    [
                                        {
                                            Id = PlanId "move"
                                            Elements = []
                                            Phase = Pending
                                            Settle = ClearWhenShown
                                            Command = Some id
                                            Order = 1UL
                                        }
                                    ]
                            }

                        let phaseAfter unsettled predicted (p: Presentation) =
                            (track unsettled predicted p).Plans |> List.map (fun e -> e.Phase)

                        let s = settlements ()
                        Expect.equal (phaseAfter [ id, s.[0] ] [] base') [ Pending ] "pending"
                        Expect.equal (phaseAfter [ id, s.[1] ] [] base') [ Accepted ] "accepted"
                        Expect.equal (phaseAfter [ id, s.[2] ] [] base') [ Queued ] "queued"
                        Expect.equal (phaseAfter [ id, s.[3] ] [] base') [ Running ] "running"
                        Expect.equal (phaseAfter [] [ id ] base') [ Applied ] "applied, not yet shown"
                        Expect.equal (phaseAfter [] [] base') [] "shown: cleared"

                        let keep =
                            { base' with
                                Plans = base'.Plans |> List.map (fun e -> { e with Settle = KeepWhenShown })
                            }

                        Expect.equal (phaseAfter [] [] keep) [ Done ] "shown: kept as done"
                    }
                ]

            testList
                "plan pass (portable profile)"
                [
                    test "the phase is the dot pattern, never the hue alone" {
                        let pattern phase = [ for i in 0..11 -> drawn phase i ]
                        let shapes = [ Draft; Pending; Applied ] |> List.map pattern
                        Expect.equal (List.distinct shapes).Length 3 "draft, submitted and applied differ by pattern"
                        Expect.equal (pattern Committed) (pattern Draft) "committed is still a draft until it is sent"
                        Expect.equal (pattern Running) (pattern Pending) "every open phase is submitted"
                    }

                    test "styles differ by head shape, and a failed plan draws a cross in the invalid ink" {
                        let arrow style phase =
                            drawPlan
                                proofInks
                                (fun _ -> None)
                                {
                                    Id = PlanId "a"
                                    Elements = [ Arrow(AtPoint(point 0.0 0.0), AtPoint(point 64.0 0.0), style) ]
                                    Phase = phase
                                    Settle = ClearWhenShown
                                    Command = None
                                    Order = 1UL
                                }

                        let headShape style phase =
                            arrow style phase |> rects |> List.filter (fun ((x, _, _, _), _) -> x > 52.0) |> List.map fst |> List.sort

                        let heads = [ Move; Attack; Support ] |> List.map (fun style -> headShape style Draft)
                        Expect.equal (List.distinct heads).Length 3 "three head shapes"
                        let fills = arrow Move (Refused "blocked") |> rects |> List.map snd |> List.distinct
                        Expect.containsAll fills [ proofInks.Invalid; proofInks.Keyline ] "the invalid ink on its keyline"
                        Expect.isFalse (List.contains proofInks.Move fills) "no move ink on a refused plan"
                        Expect.notEqual (headShape Move (Refused "blocked")) (headShape Move Draft) "the cross is not the chevron"
                    }

                    test "an arrow from an object starts outside its bounds, and every ink keeps 7:1 on the keyline" {
                        let token = tokenRect start

                        let nodes =
                            drawPlan
                                proofInks
                                (fun id -> if id = "token" then Some token else None)
                                {
                                    Id = PlanId "move"
                                    Elements = [ Arrow(AtObject "token", AtPoint(point 156.0 60.0), Move) ]
                                    Phase = Draft
                                    Settle = ClearWhenShown
                                    Command = None
                                    Order = 1UL
                                }

                        let inked = nodes |> rects |> List.filter (fun (_, fill) -> fill = proofInks.Move)

                        for (x, y, w, h), _ in inked do
                            let cx, cy = x + w / 2.0, y + h / 2.0
                            let inside = cx >= token.X && cx <= token.X + token.Width && cy >= token.Y && cy <= token.Y + token.Height
                            Expect.isFalse inside (sprintf "a dot at (%g, %g) is inside the token" cx cy)

                        for ink in [ proofInks.Move; proofInks.Attack; proofInks.Support; proofInks.Invalid ] do
                            Expect.isGreaterThanOrEqual (contrast ink proofInks.Keyline) 7.0 (sprintf "%A on the keyline" ink)
                    }

                    test "the plan pass draws after the bodies" {
                        let r = rig 8
                        drag r 60.0 156.0 60.0
                        let shown = r.Session.Shown
                        let game = (ReferenceRenderers.view { shown.Model with Selected = shown.Presentation.Primary }).Nodes
                        let all = (view shown).Nodes
                        Expect.equal (List.take game.Length all) game "the bodies first"
                        Expect.isNonEmpty (List.skip game.Length all) "then the plan pass"
                    }
                ]

            testList
                "ReferenceGame planned move, in process"
                [
                    test "press-drag-release draws the arrow through the guest's Locals, with no command sent" {
                        let r = rig 8
                        drag r 60.0 156.0 60.0
                        let p = r.Session.Presentation
                        Expect.equal p.Primary (Some "token") "the guest's Local selected the token"
                        Expect.isEmpty r.Sent "selection and planning never reach the server"
                        Expect.equal (phases r) [ Draft ] "one draft"

                        Expect.equal
                            p.Plans.Head.Elements
                            [ Arrow(AtObject "token", AtPoint(point 156.0 60.0), Move) ]
                            "the arrow ends where the drag was released"

                        Expect.isFalse r.Session.Shown.Armed "the release disarmed the drag"
                        Expect.equal r.Session.Customs [ Arm; Disarm ] "the escape hatch carried the game's own effects"
                    }

                    test "Enter submits it; the arrow follows the pending command and clears when the snapshot shows the move" {
                        let r = rig 8
                        drag r 60.0 156.0 60.0
                        InputRoute.report enter r.Route
                        Expect.equal (r.Sent |> Seq.map (CommandEnvelope.command >> commandText) |> List.ofSeq) [ "move move 156 60" ] "one MoveTo"
                        Expect.equal (phases r) [ Pending ] "the arrow follows the pending command"
                        Expect.equal r.Session.Presentation.Plans.Head.Command (Some(CommandEnvelope.id r.Sent.[0])) "linked to its id"
                        Expect.equal r.Session.Shown.Model.X 1 "not predicted: the token has not moved"
                        r.Drain()
                        Expect.equal r.Session.Authoritative.Model.X 4 "the authority moved the token to column 4"
                        Expect.equal r.Session.Shown.Model.X 4 "the game shows it"
                        Expect.isEmpty (phases r) "the arrow cleared once the snapshot showed the move"
                        Expect.isEmpty r.Session.Failures "no failure"

                        Expect.equal
                            (List.ofSeq r.Said)
                            [ "Plan move drafted"; "Plan move sent"; "Plan move done" ]
                            "one polite announcement per phase change, none per drag move"
                    }

                    test "a refused move stays drawn as refused until Escape clears it; a second Escape clears the selection" {
                        let r = rig 8
                        drag r 60.0 28.0 60.0
                        InputRoute.report enter r.Route
                        r.Drain()
                        Expect.equal (phases r) [ Refused "blocked" ] "refused into the wall"
                        Expect.equal r.Session.Shown.Model.X 1 "nothing moved"
                        Expect.equal (r.Said |> Seq.last) "Plan refused: blocked" "announced"
                        InputRoute.report escape r.Route
                        Expect.isEmpty (phases r) "Escape cleared the refused plan"
                        Expect.equal r.Session.Presentation.Primary (Some "token") "and kept the selection"
                        InputRoute.report escape r.Route
                        Expect.isNone r.Session.Presentation.Primary "a second Escape cleared the selection"
                    }

                    test "Escape never clears a plan that follows a sent command" {
                        let r = rig 8
                        drag r 60.0 156.0 60.0
                        InputRoute.report enter r.Route
                        InputRoute.report escape r.Route
                        Expect.equal (phases r) [ Pending ] "still following"
                        r.Drain()
                        Expect.isEmpty (phases r) "cleared by settlement"
                    }

                    test "the single-pointer alternative: a click on the token then a press on the ground drafts the same arrow" {
                        let r = rig 8
                        InputRoute.report (press (Some "token") 60.0 60.0) r.Route
                        r.Session.Input(Release(point 60.0 60.0, false))
                        Expect.isEmpty (phases r) "a click is not a drag"
                        InputRoute.report (press None 156.0 60.0) r.Route

                        Expect.equal
                            (r.Session.Presentation.Plans |> List.map (fun e -> e.Elements))
                            [ [ Arrow(AtObject "token", AtPoint(point 156.0 60.0), Move) ] ]
                            "the same draft as the drag"
                    }

                    test "partial failure: a decision that does not fit the window applies none of its intents" {
                        let r = rig 1
                        drag r 60.0 156.0 60.0
                        InputRoute.report enter r.Route
                        drag r 60.0 124.0 60.0
                        InputRoute.report enter r.Route
                        Expect.equal r.Sent.Count 1 "the second MoveTo was not sent"

                        Expect.isTrue
                            (r.Session.Failures |> List.exists PipelineFailure.isCommandWindowFull)
                            "CommandWindowFull was routed"

                        Expect.equal (phases r) [ Pending; Draft ] "the commit did not apply: the second plan is still a draft"
                        r.Drain()
                        Expect.equal (phases r) [ Draft ] "the first settled and cleared; the draft waits"
                        InputRoute.report enter r.Route
                        r.Drain()
                        Expect.equal r.Session.Shown.Model.X 3 "the retried draft moved the token to column 3"
                        Expect.isEmpty (phases r) "and cleared"
                    }

                    test "a Rebind keeps every plan, and a linked plan still settles by its command" {
                        let r = rig 8
                        drag r 60.0 156.0 60.0
                        InputRoute.report enter r.Route
                        drag r 60.0 124.0 60.0
                        r.Session.Dispatch(PipelineMsg.Rebind("player", Some 1UL))
                        Expect.equal (phases r) [ Pending; Draft ] "both plans survive the rebind"
                        r.Drain()
                        Expect.equal (phases r) [ Draft ] "the linked plan settled after the rebind"
                        Expect.equal r.Session.Shown.Model.X 4 "its move was applied"
                    }
                ]
        ]

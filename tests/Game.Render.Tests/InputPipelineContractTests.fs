module Game.Render.Tests.InputPipelineContractTests

// FS.GG.Game#787: the CONTRACT of the standard input pipeline (docs/adr/0001-standard-input-pipeline.md). Builders and
// accessors are data and are pinned here. Every function the ADR gives behaviour to is a stub that raises naming
// FS.GG.Game#788; the last test pins that, so a stub cannot silently start returning a value. FS.GG.Game#788 replaces
// that test with the behaviour and the kit's `input` section.

open Expecto
open FS.GG.UI.Scene
open FS.GG.Game.Render

let private tile id =
    {
        Id = id
        Label = "Tile " + id
        Bounds =
            {
                X = 0.0
                Y = 0.0
                Width = 10.0
                Height = 10.0
            }
        Shape = SelectableShape.Box
    }

let private press target =
    InputEvent.create
        InputSource.Pointer
        InputAction.Activate
        target
        (Some { X = 5.0; Y = 5.0 })
        { InputModifiers.none with
            Shift = true
        }
        false

let private raisesFor788 (name: string) (call: unit -> unit) =
    let message =
        try
            call ()
            None
        with error ->
            Some error.Message

    match message with
    | Some text -> Expect.stringContains text "FS.GG.Game#788" (name + " names the Delivery that implements it")
    | None -> failtestf "%s returned instead of raising: it is a contract stub until FS.GG.Game#788" name

[<Tests>]
let tests =
    testList
        "input pipeline contract (FS.GG.Game#787)"
        [
            test "an input event carries source, action, target, point, modifiers and repeat" {
                let event = press (Some "a")
                Expect.equal (InputEvent.source event) InputSource.Pointer "source"
                Expect.equal (InputEvent.action event) InputAction.Activate "action"
                Expect.equal (InputEvent.target event) (Some "a") "target"
                Expect.equal (InputEvent.point event) (Some { X = 5.0; Y = 5.0 }) "point"
                Expect.isTrue (InputEvent.modifiers event).Shift "shift held"
                Expect.isFalse (InputEvent.modifiers event).Control "control not held"
                Expect.isFalse (InputEvent.isRepeat event) "not a repeat"
            }

            test "an input route delivers to the bound handler only" {
                let route = InputRoute.create ()
                let heard = ResizeArray<InputEvent>()
                InputRoute.report (press None) route
                InputRoute.bind heard.Add route
                let event = press (Some "b")
                InputRoute.report event route
                InputRoute.unbind route
                InputRoute.report (press None) route
                Expect.equal heard.Count 1 "only the event reported while bound"
                Expect.equal (InputEvent.target heard[0]) (Some "b") "the bound event"
            }

            test "an input event is equal to another with the same fields" {
                Expect.equal (press (Some "a")) (press (Some "a")) "structural equality"
                Expect.notEqual (press (Some "a")) (press (Some "b")) "a different target"
            }

            test "object access defaults to full and declares pointer-only, grouped and grid objects" {
                let interaction =
                    SceneInteraction.create (fun () -> [ tile "a"; tile "b"; tile "c" ]) (fun () -> None) (fun _ s -> s)

                Expect.isTrue
                    (ObjectAccess.isKeyboardReachable (SceneInteraction.access interaction (tile "a")))
                    "undeclared objects keep keyboard and mirror access"

                let declared =
                    interaction
                    |> SceneInteraction.withAccess (fun target ->
                        match target.Id with
                        | "a" -> ObjectAccess.pointerOnly
                        | "b" -> ObjectAccess.grouped "Map"
                        | _ -> ObjectAccess.cell "Grid" 2 3)

                let a = SceneInteraction.access declared (tile "a")
                let b = SceneInteraction.access declared (tile "b")
                let c = SceneInteraction.access declared (tile "c")
                Expect.isFalse (ObjectAccess.isKeyboardReachable a) "pointer-only has no focus stop"
                Expect.equal (ObjectAccess.group a) None "pointer-only is in no group"
                Expect.equal (ObjectAccess.group b) (Some "Map") "its group"
                Expect.equal (ObjectAccess.cellOf b) None "a grouped object has no cell"
                Expect.equal (ObjectAccess.group c) (Some "Grid") "a cell's group"
                Expect.equal (ObjectAccess.cellOf c) (Some(2, 3)) "its row and column"
                Expect.throws (fun () -> ObjectAccess.grouped " " |> ignore) "a blank group label is refused"
                Expect.throws (fun () -> ObjectAccess.cell "Grid" -1 0 |> ignore) "a negative row is refused"
            }

            test "interaction cues and the cue context are data" {
                let cues = InteractionCues.create (Some "a") (Some "b") true
                Expect.equal (InteractionCues.focused cues) (Some "a") "focused"
                Expect.equal (InteractionCues.hovered cues) (Some "b") "hovered"
                Expect.isTrue (InteractionCues.focusVisible cues) "focus visible"
                Expect.equal (InteractionCues.focused InteractionCues.none) None "none has no focus"
                Expect.isFalse (InteractionCues.focusVisible InteractionCues.none) "none draws no focus"
                let context = CueContext.create cues [ tile "a" ] 2.0
                Expect.equal (CueContext.cssPerUnit context) 2.0 "scale"
                Expect.equal (CueContext.objects context |> List.map _.Id) [ "a" ] "objects"
                Expect.throws (fun () -> CueContext.create cues [] 0.0 |> ignore) "a zero scale is refused"
                Expect.throws (fun () -> CueContext.create cues [] nan |> ignore) "a NaN scale is refused"
            }

            test "guest policy defaults and bounds" {
                Expect.equal (GuestPolicy.timeout GuestPolicy.standard) 500 "standard deadline"
                Expect.equal (GuestPolicy.queueLimit GuestPolicy.standard) 16 "standard queue"
                Expect.equal (GuestPolicy.breaker GuestPolicy.standard) 3 "standard breaker"

                let policy =
                    GuestPolicy.standard
                    |> GuestPolicy.withTimeout 50
                    |> GuestPolicy.withQueueLimit 1
                    |> GuestPolicy.withBreaker 1

                Expect.equal (GuestPolicy.timeout policy) 50 "deadline set"
                Expect.equal (GuestPolicy.queueLimit policy) 1 "queue set"
                Expect.equal (GuestPolicy.breaker policy) 1 "breaker set"
                Expect.throws (fun () -> GuestPolicy.withTimeout 0 GuestPolicy.standard |> ignore) "no zero deadline"
                Expect.throws (fun () -> GuestPolicy.withQueueLimit 0 GuestPolicy.standard |> ignore) "no empty queue"
                Expect.throws (fun () -> GuestPolicy.withBreaker 0 GuestPolicy.standard |> ignore) "no zero breaker"
            }

            test "guests are synchronous, asynchronous or byte-level" {
                let sync: InputGuest<unit, unit, string, string> =
                    InputGuest.sync (fun _ -> GuestOutcome.Decided [ GuestIntent.Local "select" ])

                let later: InputGuest<unit, unit, string, string> =
                    InputGuest.ofAsync (fun _ _ -> async { return GuestOutcome.Decided [] }) GuestPolicy.standard

                let bytes: InputGuest<unit, unit, string, string> =
                    InputGuest.ofBytes
                        (fun _ -> [||])
                        (fun _ _ _ -> async { return GuestReply.Output [||] })
                        (fun _ -> Ok [])
                        GuestPolicy.standard

                Expect.isFalse (InputGuest.isAsync sync) "sync"
                Expect.isTrue (InputGuest.isAsync later) "async"
                Expect.isTrue (InputGuest.isAsync bytes) "byte-level guests answer asynchronously"
                let admitted = sync |> InputGuest.withAdmission (fun () -> Error "not armed")
                Expect.isFalse (InputGuest.isAsync admitted) "admission keeps the kind"
            }

            test "a command id reads back from its wire text and nothing else does" {
                let id = CommandId.tryParse "client-7:42"
                Expect.isSome id "a valid id parses"
                let id = id.Value
                Expect.equal (CommandId.client id) "client-7" "client"
                Expect.equal (CommandId.sequence id) 42UL "sequence"
                Expect.equal (CommandId.text id) "client-7:42" "round trip"

                Expect.equal
                    (CommandId.tryParse "a:b:3" |> Option.map CommandId.client)
                    (Some "a:b")
                    "the last colon splits"

                for bad in
                    [
                        ""
                        ":1"
                        "x:"
                        "x:0"
                        "x:-1"
                        "x:+1"
                        "x: 1"
                        " :1"
                        "x:18446744073709551616"
                    ] do
                    Expect.isNone (CommandId.tryParse bad) (sprintf "%A is refused" bad)

                Expect.isTrue (CommandId.tryParse "e:2" < CommandId.tryParse "e:10") "ordered by sequence"
            }

            test "command stage bounds" {
                let stage: CommandStage<uint64, string> = CommandStage.create id
                Expect.throws (fun () -> CommandStage.withWindow 0 stage |> ignore) "no empty window"
                Expect.throws (fun () -> CommandStage.withPatience 0UL stage |> ignore) "no zero patience"
            }

            test "the first pipeline model continues above the authority's last sequence" {
                let pipeline: InputPipeline<unit, unit, uint64, string, string> =
                    InputPipeline.create
                        (InputGuest.sync (fun _ -> GuestOutcome.Decided []))
                        (CommandStage.create (fun (version: uint64) -> version))

                let model = InputPipeline.init pipeline "client-1" (Some 41UL) 3UL
                Expect.equal (InputPipeline.client model) "client-1" "client"
                Expect.equal (InputPipeline.lastSequence model) 41UL "continues above the authority's cursor"
                Expect.equal (InputPipeline.lastSequence (InputPipeline.init pipeline "c" None 0UL)) 0UL "new binding"
                let shown = InputPipeline.displayed model
                Expect.equal (Displayed.authoritative shown) 3UL "authoritative"
                Expect.equal (Displayed.shown shown) 3UL "shown is authoritative"
                Expect.isEmpty (Displayed.predicted shown) "nothing predicted"
                Expect.isEmpty (InputPipeline.unsettled model) "no commands"
                Expect.isFalse (InputPipeline.guestHalted model) "guest usable"
                Expect.throws (fun () -> InputPipeline.init pipeline " " None 0UL |> ignore) "a blank client is refused"
            }

            test "every behaviour is a stub until FS.GG.Game#788" {
                let objects = [ tile "a" ]

                let frame =
                    {
                        Scene = Scene.empty
                        Objects = objects
                        Selected = None
                    }

                let guest: InputGuest<unit, unit, string, string> =
                    InputGuest.sync (fun _ -> GuestOutcome.Decided [])

                let pipeline =
                    InputPipeline.create guest (CommandStage.create (fun (version: uint64) -> version))

                let model = InputPipeline.init pipeline "e" None 0UL
                let io = PipelineIo.create id ignore ignore ignore (fun _ _ -> ())

                let interaction =
                    SceneInteraction.create (fun () -> objects) (fun () -> None) (fun _ s -> s)

                raisesFor788 "SceneInteraction.standardCues" (fun () ->
                    SceneInteraction.standardCues (CueContext.create InteractionCues.none objects 1.0)
                    |> ignore)

                raisesFor788 "InputEvent.toSelectionRequest" (fun () ->
                    InputEvent.toSelectionRequest frame (press (Some "a")) |> ignore)

                raisesFor788 "InputGuest.run" (fun () ->
                    InputGuest.run guest (Unchecked.defaultof<_>) ignore System.Threading.CancellationToken.None
                    |> ignore)

                raisesFor788 "InputPipeline.update" (fun () ->
                    InputPipeline.update pipeline (PipelineMsg.Authoritative 1UL) model |> ignore)

                raisesFor788 "PipelineRunner.create" (fun () -> PipelineRunner.create pipeline io ignore |> ignore)

                raisesFor788 "PipelineHost.start" (fun () ->
                    PipelineHost.start pipeline (InputRoute.create ()) io "e" None 0UL |> ignore)

                raisesFor788 "SceneRenderers.withInput" (fun () ->
                    SceneRenderers.create (fun () -> Scene.empty) (fun _ -> Error "unused")
                    |> SceneRenderers.withInput interaction (InputRoute.create ())
                    |> ignore)

                raisesFor788 "SceneSurface.withInputHost" (fun () ->
                    SceneSurface.create RendererBackend.Svg (fun _ -> Error "unused")
                    |> SceneSurface.withInputHost (fun _ _ -> Error "unused")
                    |> ignore)

                let subject =
                    RendererConformance.subject
                        (RendererRegistry.svg (fun () -> Scene.empty) (fun _ -> Error "unused"))
                        [ RendererBackend.Svg ]
                        [ () ]
                        (fun _ -> 0)
                        (fun _ _ _ -> async { return Ok() })

                let probe =
                    InteractionProbe.create
                        (fun _ _ -> async { return () })
                        (fun _ _ -> async { return () })
                        (fun _ -> async { return () })
                        (fun _ -> async { return () })
                        (fun _ -> "")
                        (fun _ -> None)
                        (fun _ -> 0)
                        (fun _ -> None)
                        (fun _ -> 0)

                raisesFor788 "RendererConformance.withInput" (fun () ->
                    RendererConformance.withInput interaction (InputRoute.create ()) probe (fun () -> []) subject
                    |> ignore)

                raisesFor788 "RendererConformance.withOwnInputHost" (fun () ->
                    RendererConformance.withOwnInputHost RendererBackend.Svg subject |> ignore)
            }
        ]

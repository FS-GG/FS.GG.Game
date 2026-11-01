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

            test "object access defaults to full and declares pointer-only and grouped objects" {
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
                        | _ -> ObjectAccess.full)

                let a = SceneInteraction.access declared (tile "a")
                let b = SceneInteraction.access declared (tile "b")
                Expect.isFalse (ObjectAccess.isKeyboardReachable a) "pointer-only has no focus stop"
                Expect.equal (ObjectAccess.group a) None "pointer-only is in no group"
                Expect.isTrue (ObjectAccess.isKeyboardReachable b) "a grouped object is reachable through its group"
                Expect.equal (ObjectAccess.group b) (Some "Map") "its group"
                Expect.throws (fun () -> ObjectAccess.grouped " " |> ignore) "a blank group label is refused"
            }

            test "interaction cues are data" {
                let cues = InteractionCues.create (Some "a") (Some "b") true
                Expect.equal (InteractionCues.focused cues) (Some "a") "focused"
                Expect.equal (InteractionCues.hovered cues) (Some "b") "hovered"
                Expect.isTrue (InteractionCues.focusVisible cues) "focus visible"
                Expect.equal (InteractionCues.focused InteractionCues.none) None "none has no focus"
                Expect.isFalse (InteractionCues.focusVisible InteractionCues.none) "none draws no focus"
            }

            test "guest policy defaults and bounds" {
                Expect.equal (GuestPolicy.timeout GuestPolicy.standard) 500 "standard deadline"
                Expect.equal (GuestPolicy.queueLimit GuestPolicy.standard) 16 "standard queue"

                let policy =
                    GuestPolicy.standard
                    |> GuestPolicy.withTimeout 50
                    |> GuestPolicy.withQueueLimit 1

                Expect.equal (GuestPolicy.timeout policy) 50 "deadline set"
                Expect.equal (GuestPolicy.queueLimit policy) 1 "queue set"
                Expect.throws (fun () -> GuestPolicy.withTimeout 0 GuestPolicy.standard |> ignore) "no zero deadline"
                Expect.throws (fun () -> GuestPolicy.withQueueLimit 0 GuestPolicy.standard |> ignore) "no empty queue"
            }

            test "guests are synchronous or asynchronous" {
                let sync: InputGuest<unit, string, string> =
                    InputGuest.sync (fun _ -> GuestOutcome.Decided [ GuestIntent.Local "select" ])

                let later: InputGuest<unit, string, string> =
                    InputGuest.async (fun _ _ -> async { return GuestOutcome.Decided [] }) GuestPolicy.standard

                Expect.isFalse (InputGuest.isAsync sync) "sync"
                Expect.isTrue (InputGuest.isAsync later) "async"
                let admitted = sync |> InputGuest.withAdmission (fun () -> Error "not armed")
                Expect.isFalse (InputGuest.isAsync admitted) "admission keeps the kind"
            }

            test "a command id reads back from its wire text and nothing else does" {
                let id = CommandId.tryParse "client-7:42"
                Expect.isSome id "a valid id parses"
                let id = id.Value
                Expect.equal (CommandId.epoch id) "client-7" "epoch"
                Expect.equal (CommandId.sequence id) 42UL "sequence"
                Expect.equal (CommandId.text id) "client-7:42" "round trip"

                Expect.equal
                    (CommandId.tryParse "a:b:3" |> Option.map CommandId.epoch)
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

            test "the first pipeline model shows the authoritative state with nothing predicted" {
                let pipeline =
                    InputPipeline.create
                        (InputGuest.sync (fun (_: GuestRequest<unit>) -> GuestOutcome<string, string>.Decided []))
                        (CommandStage.create (fun (version: uint64) -> version))

                let model, effects = InputPipeline.init pipeline "epoch-1" 3UL
                Expect.isEmpty effects "init has no effects"
                Expect.equal (InputPipeline.epoch model) "epoch-1" "epoch"
                let shown = InputPipeline.displayed model
                Expect.equal (Displayed.authoritative shown) 3UL "authoritative"
                Expect.equal (Displayed.shown shown) 3UL "shown is authoritative"
                Expect.isEmpty (Displayed.predicted shown) "nothing predicted"
                Expect.isEmpty (InputPipeline.unsettled model) "no commands"
                Expect.isFalse (InputPipeline.guestHalted model) "guest usable"
                Expect.throws (fun () -> InputPipeline.init pipeline " " 0UL |> ignore) "a blank epoch is refused"
            }

            test "every behaviour is a stub until FS.GG.Game#788" {
                let objects = [ tile "a" ]

                let frame =
                    {
                        Scene = Scene.empty
                        Objects = objects
                        Selected = None
                    }

                let pipeline =
                    InputPipeline.create
                        (InputGuest.sync (fun (_: GuestRequest<unit>) -> GuestOutcome<string, string>.Decided []))
                        (CommandStage.create (fun (version: uint64) -> version))

                let model, _ = InputPipeline.init pipeline "e" 0UL

                raisesFor788 "SceneInteraction.standardCues" (fun () ->
                    SceneInteraction.standardCues InteractionCues.none objects |> ignore)

                raisesFor788 "InputEvent.toSelectionRequest" (fun () ->
                    InputEvent.toSelectionRequest frame (press (Some "a")) |> ignore)

                raisesFor788 "InputGuest.ofBytes" (fun () ->
                    InputGuest.ofBytes
                        (fun (_: GuestRequest<unit>) -> [||])
                        (fun _ _ -> async { return GuestReply.Output [||] })
                        (fun _ -> Ok([]: GuestIntent<string, string> list))
                        GuestPolicy.standard
                    |> ignore)

                raisesFor788 "InputPipeline.update" (fun () ->
                    InputPipeline.update pipeline (PipelineMsg.Authoritative 1UL) model |> ignore)

                raisesFor788 "PipelineHost.start" (fun () ->
                    PipelineHost.start pipeline (InputRoute.create ()) "e" 0UL id ignore ignore ignore (fun _ _ -> ())
                    |> ignore)

                raisesFor788 "SceneRenderers.withInput" (fun () ->
                    SceneRenderers.create (fun () -> Scene.empty) (fun _ -> Error "unused")
                    |> SceneRenderers.withInput
                        (SceneInteraction.create (fun () -> objects) (fun () -> None) (fun _ s -> s))
                        (InputRoute.create ())
                    |> ignore)

                raisesFor788 "SceneSurface.withInputHost" (fun () ->
                    SceneSurface.create RendererBackend.Svg (fun _ -> Error "unused")
                    |> SceneSurface.withInputHost (fun _ _ -> Error "unused")
                    |> ignore)
            }
        ]

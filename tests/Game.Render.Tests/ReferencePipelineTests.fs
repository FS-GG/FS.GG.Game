module Game.Render.Tests.ReferencePipelineTests

// FS.GG.Game#788 PR D: ReferenceGame through the standard input pipeline, end to end and in process: an input on the
// route goes to the F# guest, its command to the authority (admitted by Game.Core's NetworkAdmission, answered from its
// cache), and the authoritative snapshot back to what the game shows. The "network" is a queue drained by the test, so
// every answer arrives later than the input, as it does in the browser smoke (tests/Render.Browser.Smoke).

open Expecto
open FS.GG.Game.Render
open FS.GG.Game.Reference
open FS.GG.Game.Reference.ReferenceState
open FS.GG.Game.Reference.ReferencePipeline

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

let private press (target: string option) (x: float) (y: float) =
    InputEvent.create
        InputSource.Pointer
        InputAction.Activate
        target
        (Some { X = x; Y = y })
        InputModifiers.none
        false

/// A session whose network is a queue the test drains.
let private session () =
    let network = System.Collections.Generic.Queue<unit -> unit>()
    let route = InputRoute.create ()
    let shown = ResizeArray<Model>()
    let sent = ResizeArray<CommandEnvelope<Command>>()
    let running = Session("player", start, route, network.Enqueue, (fun _ _ -> ()), shown.Add, sent.Add)

    let drain () =
        while network.Count > 0 do
            (network.Dequeue()) ()

    running, route, shown, sent, drain

[<Tests>]
let tests =
    testList
        "ReferencePipeline"
        [
            test "a press on the token goes through the F# guest to a Step command, and the authoritative snapshot comes back" {
                let running, route, shown, sent, drain = session ()
                InputRoute.report (press (Some "token") 60.0 60.0) route

                Expect.equal (sent |> Seq.map (CommandEnvelope.command >> commandText) |> List.ofSeq) [ "step" ] "one Step left the client"
                Expect.equal (CommandId.text (CommandEnvelope.id sent.[0])) "player:1" "the first command id of the binding"
                Expect.equal running.Shown.Selected (Some "token") "the guest's Local selected the token at once"
                Expect.equal running.Authoritative.Version 0UL "the authority has not heard it yet"
                Expect.equal running.Shown.X 2 "the step is shown predicted until the snapshot arrives"

                Expect.equal
                    (Displayed.predicted (InputPipeline.displayed running.Model) |> List.map CommandId.text)
                    [ "player:1" ]
                    "and it is marked as predicted"

                drain ()
                Expect.equal running.Authoritative.Version 1UL "the authority applied it once"
                Expect.equal running.Authoritative.Model.X 2 "the authoritative state stepped"
                Expect.equal running.Shown.X 2 "the game shows the authoritative state"
                Expect.isEmpty (Displayed.predicted (InputPipeline.displayed running.Model)) "nothing predicted any more"
                Expect.isEmpty (InputPipeline.unsettled running.Model) "the command settled applied"
                Expect.isEmpty running.Failures "no failure was routed"
                Expect.equal (List.last (List.ofSeq shown)).X 2 "the last shown state is the snapshot's"
            }

            test "every activation is decided: a repeated press steps again, the aim marker only selects, ground aims, Cancel clears" {
                let running, route, _, sent, drain = session ()
                InputRoute.report (press (Some "token") 60.0 60.0) route
                InputRoute.report (press (Some "token") 60.0 60.0) route
                InputRoute.report (press (Some "aim") 200.0 40.0) route
                InputRoute.report (press None 120.0 100.0) route
                drain ()

                Expect.equal
                    (sent |> Seq.map (CommandEnvelope.command >> commandText) |> List.ofSeq)
                    [ "step"; "step"; "aim 120 100" ]
                    "two steps and an aim, in input order"

                Expect.equal running.Authoritative.Version 3UL "three commands applied"
                Expect.equal running.Shown.X 3 "stepped twice"
                Expect.equal running.Shown.Aim (120, 100) "aimed at the ground press"
                Expect.equal running.Shown.Selected (Some "aim") "the aim marker was selected last"

                InputRoute.report (InputEvent.create InputSource.Keyboard InputAction.Cancel None None InputModifiers.none false) route
                drain ()
                Expect.equal running.Shown.Selected None "Cancel cleared the selection"
            }

            test "the authority admits each id once: a duplicate or stale id is answered from the cache and never applied again" {
                let running, route, _, sent, drain = session ()
                InputRoute.report (press (Some "token") 60.0 60.0) route
                InputRoute.report (press (Some "token") 60.0 60.0) route
                drain ()
                let authority = Authority.create "player" start
                let authority, first, snapshot = Authority.receive sent.[0] authority
                Expect.equal (snapshot |> Option.map (fun s -> s.Version)) (Some 1UL) "the first delivery applies"
                let authority, second, _ = Authority.receive sent.[1] authority
                let again, repeated, none = Authority.receive sent.[0] authority
                Expect.isNone none "a duplicate of an older id applies nothing"
                Expect.equal again.Snapshot.Version 2UL "the version did not move"
                Expect.equal (Authority.lastSequence "player" again) (Some 2UL) "the authority's cursor is the highest id"

                // The cached answer is the id's first answer, `applied 1`: a client that hears it keeps the command
                // predicted until the authoritative state reaches version 1, which a `rejected` would have withdrawn.
                let model = InputPipeline.init pipeline "player" None { Version = 0UL; Model = start }
                let model, _ = InputPipeline.update pipeline (PipelineMsg.Submit Command.Step) model
                let model, _ = InputPipeline.update pipeline (PipelineMsg.Acknowledged(CommandEnvelope.id sent.[0], repeated)) model

                Expect.equal
                    (Displayed.predicted (InputPipeline.displayed model) |> List.map CommandId.text)
                    [ "player:1" ]
                    "the repeated answer is the cached applied, not a new one"

                Expect.isEmpty (InputPipeline.unsettled model) "and it is final"
                ignore first
                ignore second
                Expect.equal running.Authoritative.Version 2UL "the session's own authority applied each once"
            }
        ]

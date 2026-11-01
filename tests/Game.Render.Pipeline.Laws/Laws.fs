/// The laws of the standard input pipeline (FS.GG.Game ADR 0001, P1-P20 with P7b, P10b and P18b), as checks that run
/// unchanged on .NET and on Fable (node). Output is rendered by hand (never `%A`) so both runtimes print identical
/// lines; scripts/test-pipeline-laws.sh compares them byte for byte.
module Game.Render.Pipeline.Laws.Laws

open FS.GG.Game.Render

// ---------------------------------------------------------------------------------------------------------------
// Output

let private lines = ResizeArray<string>()
let mutable private failed = 0

let check (law: string) (name: string) (ok: bool) (detail: string) =
    if ok then
        lines.Add("PASS " + law + " " + name)
    else
        failed <- failed + 1
        lines.Add("FAIL " + law + " " + name + ": " + detail)

let results () = List.ofSeq lines, failed

// ---------------------------------------------------------------------------------------------------------------
// A tiny game: the authority's state is a version and the commands it applied.

type Server = { V: uint64; Items: string list }

let server v items = { V = v; Items = items }

type Msg = PipelineMsg<string, Server, string, string>
type Effect = PipelineEffect<string, string, string>
type Model = PipelineModel<string, Server, string, string>
type Pipeline = InputPipeline<string, int, Server, string, string>

let idText (id: CommandId) = CommandId.text id

let failureText (failure: PipelineFailure) =
    match failure with
    | PipelineFailure.GuestFaulted(s, r) -> sprintf "GuestFaulted(%d,%s)" s r
    | PipelineFailure.GuestTimedOut s -> sprintf "GuestTimedOut(%d)" s
    | PipelineFailure.GuestHalted n -> sprintf "GuestHalted(%d)" n
    | PipelineFailure.InputRefused(s, r) -> sprintf "InputRefused(%d,%s)" s r
    | PipelineFailure.InputCancelled(s, r) -> sprintf "InputCancelled(%d,%s)" s r
    | PipelineFailure.QueueOverflow dropped ->
        "QueueOverflow(" + (dropped |> List.map string |> String.concat ",") + ")"
    | PipelineFailure.CommandWindowFull s ->
        "CommandWindowFull(" + (s |> Option.map string |> Option.defaultValue "-") + ")"
    | PipelineFailure.CommandRejected(id, r) -> sprintf "CommandRejected(%s,%s)" (idText id) r
    | PipelineFailure.CommandCancelled(id, r) -> sprintf "CommandCancelled(%s,%s)" (idText id) r
    | PipelineFailure.CommandUnconfirmed id -> sprintf "CommandUnconfirmed(%s)" (idText id)
    | PipelineFailure.RebindRefused r -> sprintf "RebindRefused(%s)" r

let inputText (input: PipelineInput<string>) =
    match input with
    | PipelineInput.Scene event ->
        let action =
            match InputEvent.action event with
            | InputAction.Activate -> "activate"
            | InputAction.ActivateSecondary -> "secondary"
            | InputAction.Hover -> "hover"
            | InputAction.Focus -> "focus"
            | InputAction.Cancel -> "cancel"

        action + ":" + (InputEvent.target event |> Option.defaultValue "-")
    | PipelineInput.Game input -> "game:" + input

let effectText (effect: Effect) =
    match effect with
    | PipelineEffect.StartGuest call ->
        sprintf
            "Start(%d,%d,%s,%d)"
            (GuestCall.id call)
            (GuestCall.sequence call)
            (inputText (GuestCall.input call))
            (GuestCall.basis call)
    | PipelineEffect.CancelGuest id -> sprintf "Cancel(%d)" id
    | PipelineEffect.ArmStartDeadline(id, ms) -> sprintf "ArmStart(%d,%d)" id ms
    | PipelineEffect.ArmDeadline(id, ms) -> sprintf "Arm(%d,%d)" id ms
    | PipelineEffect.Continue -> "Continue"
    | PipelineEffect.ApplyLocal local -> "Local(" + local + ")"
    | PipelineEffect.Send envelope ->
        sprintf
            "Send(%s,%s,%d,%s)"
            (idText (CommandEnvelope.id envelope))
            (CommandEnvelope.input envelope |> Option.map string |> Option.defaultValue "-")
            (CommandEnvelope.basis envelope)
            (CommandEnvelope.command envelope)
    | PipelineEffect.Fail failure -> "Fail(" + failureText failure + ")"

let render (effects: Effect list) =
    effects |> List.map effectText |> String.concat " "

// ---------------------------------------------------------------------------------------------------------------
// Pipelines and a pure stepper

let event action target =
    InputEvent.create InputSource.Pointer action target None InputModifiers.none false

let scene action target : Msg =
    PipelineMsg.Input(PipelineInput.Scene(event action (Some target)))

let game input : Msg =
    PipelineMsg.Input(PipelineInput.Game input)

/// The decision is supplied by the test through `GuestEnded`; the guest value is never run by the reducer.
let guestOf (policy: GuestPolicy) : InputGuest<string, int, string, string> =
    InputGuest.ofAsync (fun _ _ -> async { return GuestOutcome.Decided [] }) policy

let pipelineOf (policy: GuestPolicy) (stage: CommandStage<Server, string>) : Pipeline =
    InputPipeline.create (guestOf policy) stage

let stage () : CommandStage<Server, string> = CommandStage.create (fun s -> s.V)

let standard () =
    pipelineOf GuestPolicy.standard (stage ())

/// Apply messages in order; return the final model and each message's effects.
let steps (pipeline: Pipeline) (model: Model) (msgs: Msg list) =
    let mutable model = model

    let effects =
        msgs
        |> List.map (fun msg ->
            let next, effects = InputPipeline.update pipeline msg model
            model <- next
            effects)

    model, effects

let last (xs: 'a list) = List.item (List.length xs - 1) xs

let decided intents =
    GuestOutcome<string, string>.Decided intents

let submit c = GuestIntent<string, string>.Submit c
let local l = GuestIntent<string, string>.Local l

/// One complete guest round for call `call`: running, then the outcome.
let round (call: uint64) outcome : Msg list =
    [
        PipelineMsg.GuestRunning call
        PipelineMsg.GuestEnded(call, outcome)
        PipelineMsg.StartNext
    ]

let starts (effects: Effect list) =
    effects
    |> List.choose (fun effect ->
        match effect with
        | PipelineEffect.StartGuest call -> Some call
        | _ -> None)

let sends (effects: Effect list) =
    effects
    |> List.choose (fun effect ->
        match effect with
        | PipelineEffect.Send envelope -> Some envelope
        | _ -> None)

let fails (effects: Effect list) =
    effects
    |> List.choose (fun effect ->
        match effect with
        | PipelineEffect.Fail failure -> Some(failureText failure)
        | _ -> None)

// ---------------------------------------------------------------------------------------------------------------
// Deterministic generator (Park-Miller; exact in IEEE doubles, so .NET and JS agree)

type Rng(seed: int) =
    let mutable state = if seed <= 0 then 1 else seed

    member _.Next(bound: int) =
        state <- int ((int64 state * 48271L) % 2147483647L)
        state % bound

// ---------------------------------------------------------------------------------------------------------------
// The laws

let p1 () =
    let pipeline = standard ()
    let model = InputPipeline.init pipeline "a" (Some 5UL) (server 0UL [])

    let model, effects =
        steps
            pipeline
            model
            ([ scene InputAction.Activate "x" ]
             @ round 1UL (decided [ submit "c1" ])
             @ [ scene InputAction.Activate "y" ]
             @ round 2UL (decided [ submit "c2" ])
             @ [ PipelineMsg.Rebind("a", Some 3UL); scene InputAction.Activate "z" ]
             @ round 3UL (decided [ submit "c3" ])
             @ [ PipelineMsg.Rebind("b", Some 40UL); game "g" ]
             @ round 4UL (decided [ submit "c4" ]))

    let all = List.concat effects
    let ids = sends all |> List.map (CommandEnvelope.id >> idText)

    check
        "P1"
        "command sequences continue above lastSequence and across rebinds"
        (ids = [ "a:6"; "a:7"; "a:8"; "b:41" ])
        (String.concat "," ids)

    let calls = starts all |> List.map GuestCall.id

    check
        "P1"
        "call ids rise and never repeat"
        (calls = [ 1UL; 2UL; 3UL; 4UL ])
        (calls |> List.map string |> String.concat ",")

    let sequences = starts all |> List.map GuestCall.sequence

    check
        "P1"
        "input sequences rise from 1 for the model's lifetime"
        (sequences = [ 1UL; 2UL; 3UL; 4UL ])
        (sequences |> List.map string |> String.concat ",")

    check
        "P1"
        "the model reports the highest sequence issued"
        (InputPipeline.lastSequence model = 41UL)
        (string (InputPipeline.lastSequence model))

let p2 () =
    let pipeline = standard ()
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let _, effects =
        steps pipeline model [ scene InputAction.Activate "x"; scene InputAction.Activate "y"; game "g" ]

    check
        "P2"
        "one call in flight; later inputs wait"
        ((starts (List.concat effects)).Length = 1)
        (render (List.concat effects))

let p3 () =
    let policy =
        GuestPolicy.standard
        |> GuestPolicy.withStartTimeout 900
        |> GuestPolicy.withTimeout 40

    let pipeline = pipelineOf policy (stage ())
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let _, effects =
        steps pipeline model [ scene InputAction.Activate "x"; PipelineMsg.GuestRunning 1UL ]

    check
        "P3"
        "StartGuest comes with ArmStartDeadline"
        (render effects.[0] = "Start(1,1,activate:x,0) ArmStart(1,900)")
        (render effects.[0])

    check "P3" "ArmDeadline follows GuestRunning" (render effects.[1] = "Arm(1,40)") (render effects.[1])

    let m, startLate =
        steps pipeline model [ scene InputAction.Activate "x"; PipelineMsg.GuestStartDeadline 1UL ]

    check
        "P3"
        "a start deadline before GuestRunning faults and halts"
        (render startLate.[1] = "Cancel(1) Fail(GuestFaulted(1,start timeout)) Continue"
         && InputPipeline.guestHalted m)
        (render startLate.[1])

    let _, ignored =
        steps
            pipeline
            model
            [
                scene InputAction.Activate "x"
                PipelineMsg.GuestRunning 1UL
                PipelineMsg.GuestStartDeadline 1UL
            ]

    check "P3" "a start deadline after GuestRunning is ignored" (ignored.[2].IsEmpty) (render ignored.[2])

    let m, timed =
        steps
            pipeline
            model
            [
                scene InputAction.Activate "x"
                PipelineMsg.GuestRunning 1UL
                PipelineMsg.GuestDeadline 1UL
            ]

    check
        "P3"
        "a call deadline after GuestRunning times the call out"
        (render timed.[2] = "Cancel(1) Fail(GuestTimedOut(1)) Continue"
         && not (InputPipeline.guestHalted m))
        (render timed.[2])

let p4 () =
    let policy = GuestPolicy.standard |> GuestPolicy.withBreaker 2
    let pipeline = pipelineOf policy (stage ())
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let m, effects =
        steps
            pipeline
            model
            ([ scene InputAction.Activate "x" ]
             @ round 1UL GuestOutcome.TimedOut
             @ [ scene InputAction.Activate "y" ]
             @ round 2UL (decided [])
             @ [ scene InputAction.Activate "z" ]
             @ round 3UL GuestOutcome.TimedOut)

    check "P4" "a decided call resets the breaker" (not (InputPipeline.guestHalted m)) (render (List.concat effects))

    let m, effects =
        steps
            pipeline
            m
            ([ scene InputAction.Activate "w" ]
             @ round 4UL GuestOutcome.TimedOut
             @ [ scene InputAction.Activate "v" ])

    check
        "P4"
        "consecutive timeouts trip the breaker"
        (InputPipeline.guestHalted m
         && List.contains "GuestHalted(2)" (fails (List.concat effects)))
        (render (List.concat effects))

    check
        "P4"
        "a halted stage refuses inputs"
        (fails (last effects) = [ "InputRefused(5,guest halted)" ])
        (render (last effects))

    let m2, rearm =
        steps pipeline m [ PipelineMsg.RearmGuest; scene InputAction.Activate "u" ]

    check
        "P4"
        "RearmGuest unhalts and changes nothing else"
        (not (InputPipeline.guestHalted m2)
         && rearm.[0].IsEmpty
         && (starts rearm.[1]).Length = 1)
        (render (List.concat rearm))

    let m3, faulted =
        steps pipeline model ([ scene InputAction.Activate "x" ] @ round 1UL (GuestOutcome.Faulted "trap"))

    check
        "P4"
        "Faulted halts at once"
        (InputPipeline.guestHalted m3
         && List.contains "GuestFaulted(1,trap)" (fails (List.concat faulted)))
        (render (List.concat faulted))

let p5 () =
    let pipeline = standard ()
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let m, effects =
        steps
            pipeline
            model
            [
                scene InputAction.Activate "x"
                PipelineMsg.GuestRunning 1UL
                PipelineMsg.GuestDeadline 1UL
                PipelineMsg.GuestEnded(1UL, decided [ submit "late" ])
                PipelineMsg.GuestEnded(99UL, decided [ submit "stranger" ])
            ]

    check
        "P5"
        "a reply for a timed-out or unknown call changes nothing"
        (effects.[3].IsEmpty
         && effects.[4].IsEmpty
         && (InputPipeline.unsettled m).IsEmpty)
        (render (List.concat effects))

let p6 () =
    let pipeline =
        pipelineOf GuestPolicy.standard (stage () |> CommandStage.withSettlement (fun c s -> List.contains c s.Items))

    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let m, effects =
        steps
            pipeline
            model
            ([ scene InputAction.Activate "x" ]
             @ round 1UL (decided [ submit "c1" ])
             @ [
                 scene InputAction.Activate "y"
                 scene InputAction.Activate "z"
                 PipelineMsg.Rebind("b", None)
             ])

    check
        "P6"
        "a rebind cancels the call in flight and the queue"
        (render (last effects) = "Cancel(2) Fail(InputCancelled(2,rebind)) Fail(InputCancelled(3,rebind))")
        (render (last effects))

    check
        "P6"
        "the rebind keeps commands of the earlier binding"
        ((InputPipeline.unsettled m |> List.map (fst >> idText)) = [ "a:1" ])
        (InputPipeline.unsettled m |> List.map (fst >> idText) |> String.concat ",")

    let m2, _ = steps pipeline m [ PipelineMsg.Authoritative(server 3UL [ "c1" ]) ]

    check
        "P6"
        "an earlier binding's command still settles by state"
        ((InputPipeline.unsettled m2).IsEmpty)
        (InputPipeline.unsettled m2 |> List.map (fst >> idText) |> String.concat ",")

    let m3, refused = steps pipeline m [ PipelineMsg.Rebind(" ", None) ]

    check
        "P6"
        "a blank rebind is refused and changes nothing"
        (InputPipeline.client m3 = "b"
         && fails refused.[0] = [ "RebindRefused(a client binding must not be blank)" ])
        (render refused.[0])

let p7 () =
    let pipeline = standard ()
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let _, effects =
        steps
            pipeline
            model
            ([ scene InputAction.Activate "x"; scene InputAction.Activate "y" ]
             @ round 1UL (decided []))

    let calls = starts (List.concat effects)

    check
        "P7"
        "StartGuest carries the call, never a client snapshot (the interpreter reads client () at start)"
        (calls.Length = 2 && GuestCall.sequence calls.[1] = 2UL)
        (render (List.concat effects))

/// P7b: a FIFO dispatcher (Elmish's), the game's state as a counter, a guest that echoes the client it saw.
let p7b () =
    let pipeline = standard ()
    let mutable model = InputPipeline.init pipeline "a" None (server 0UL [])
    let mutable gameState = 0
    let seen = ResizeArray<int * int>()
    let queue = System.Collections.Generic.Queue<Choice<Msg, string>>()

    let rec drain () =
        if queue.Count > 0 then
            match queue.Dequeue() with
            | Choice2Of2 _local -> gameState <- gameState + 1
            | Choice1Of2 msg ->
                let next, effects = InputPipeline.update pipeline msg model
                model <- next

                for effect in effects do
                    match effect with
                    | PipelineEffect.StartGuest call ->
                        // The interpreter reads client () NOW, then the guest decides at once (a sync guest).
                        let client = gameState
                        seen.Add(int (GuestCall.sequence call), client)
                        queue.Enqueue(Choice1Of2(PipelineMsg.GuestRunning(GuestCall.id call)))

                        queue.Enqueue(
                            Choice1Of2(
                                PipelineMsg.GuestEnded(GuestCall.id call, decided [ local "bump"; local "bump" ])
                            )
                        )
                    | PipelineEffect.ApplyLocal l -> queue.Enqueue(Choice2Of2 l)
                    | PipelineEffect.Continue -> queue.Enqueue(Choice1Of2 PipelineMsg.StartNext)
                    | _ -> ()

            drain ()

    for target in [ "x"; "y"; "z" ] do
        queue.Enqueue(Choice1Of2(scene InputAction.Activate target))

    drain ()

    let observed =
        seen |> Seq.map (fun (s, c) -> sprintf "%d@%d" s c) |> String.concat ","

    check "P7b" "each call reads the state every earlier Local produced" (observed = "1@0,2@2,3@4") observed

let p8 () =
    let pipeline = standard ()
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let _, effects =
        steps
            pipeline
            model
            ([
                scene InputAction.Activate "busy"
                scene InputAction.Hover "h1"
                scene InputAction.Activate "a1"
                scene InputAction.Hover "h2"
                scene InputAction.Focus "f1"
                scene InputAction.Focus "f2"
                game "g1"
             ]
             @ round 1UL (decided [])
             @ round 2UL (decided [])
             @ round 3UL (decided [])
             @ round 4UL (decided [])
             @ round 5UL (decided []))

    let order =
        starts (List.concat effects)
        |> List.map (GuestCall.input >> inputText)
        |> String.concat ","

    check
        "P8"
        "a newer Hover or Focus replaces the waiting one at the tail; others keep arrival order"
        (order = "activate:busy,activate:a1,hover:h2,focus:f2,game:g1")
        order

let p9 () =
    let pipeline =
        pipelineOf (GuestPolicy.standard |> GuestPolicy.withQueueLimit 2) (stage ())

    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let _, effects =
        steps
            pipeline
            model
            ([
                scene InputAction.Activate "busy"
                scene InputAction.Activate "w1"
                scene InputAction.Activate "w2"
                scene InputAction.Activate "over"
             ]
             @ round 1UL (decided [])
             @ round 2UL (decided [])
             @ round 3UL (decided []))

    check "P9" "a full queue drops the arriving input" (fails effects.[3] = [ "QueueOverflow(4)" ]) (render effects.[3])

    let order =
        starts (List.concat effects)
        |> List.map (GuestCall.input >> inputText)
        |> String.concat ","

    check "P9" "the waiting inputs stay" (order = "activate:busy,activate:w1,activate:w2") order

let p10 () =
    let pipeline =
        pipelineOf GuestPolicy.standard (stage () |> CommandStage.withWindow 1)

    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let _, effects =
        steps
            pipeline
            model
            ([
                scene InputAction.Activate "x"
                PipelineMsg.GuestRunning 1UL
                PipelineMsg.GuestEnded(1UL, decided [ local "l1"; submit "c1"; local "l2" ])
            ])

    check
        "P10"
        "decision effects: Local and Send in intent order, then Continue"
        (render effects.[2] = "Local(l1) Send(a:1,1,0,c1) Local(l2) Continue")
        (render effects.[2])

    let _, timed =
        steps
            pipeline
            model
            [
                scene InputAction.Activate "x"
                PipelineMsg.GuestRunning 1UL
                PipelineMsg.GuestDeadline 1UL
            ]

    check
        "P10"
        "a pipeline-ended call: Cancel, then Fail, then Continue"
        (render timed.[2] = "Cancel(1) Fail(GuestTimedOut(1)) Continue")
        (render timed.[2])

let p10b () =
    let pipeline = standard ()
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let _, effects =
        steps
            pipeline
            model
            ([ scene InputAction.Activate "x"; PipelineMsg.Submit "direct" ]
             @ round 1UL (decided [ submit "decided" ]))

    let order =
        sends (List.concat effects)
        |> List.map CommandEnvelope.command
        |> String.concat ","

    check "P10b" "Submit never overtakes the call in flight" (order = "decided,direct") order
    let input = sends (List.concat effects) |> List.map CommandEnvelope.input
    check "P10b" "a Submit carries no input sequence" (input = [ Some 1UL; None ]) (render (List.concat effects))

    let halted, _ =
        steps pipeline model ([ scene InputAction.Activate "x" ] @ round 1UL (GuestOutcome.Faulted "trap"))

    let _, effects =
        steps pipeline halted [ PipelineMsg.Submit "while-halted"; scene InputAction.Activate "y" ]

    check
        "P10b"
        "Submit is accepted while halted; guest inputs are refused"
        ((sends effects.[0] |> List.map CommandEnvelope.command) = [ "while-halted" ]
         && fails effects.[1] = [ "InputRefused(2,guest halted)" ])
        (render (List.concat effects))

let p11 () =
    let pipeline =
        pipelineOf GuestPolicy.standard (stage () |> CommandStage.withWindow 1)

    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let _, effects =
        steps
            pipeline
            model
            ([ scene InputAction.Activate "x" ]
             @ round 1UL (decided [ submit "c1" ])
             @ [ scene InputAction.Activate "y" ]
             @ round 2UL (decided [ local "l"; submit "c2" ]))

    check
        "P11"
        "a decision that does not fit applies no intent, Locals included"
        (render effects.[6] = "Fail(CommandWindowFull(2)) Continue")
        (render effects.[6])

let p12 () =
    let pipeline = standard ()
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let _, effects =
        steps
            pipeline
            model
            [
                scene InputAction.Activate "x"
                PipelineMsg.GuestRunning 1UL
                scene InputAction.Cancel "x"
            ]

    check "P12" "a Cancel input during a call does not cancel it" (effects.[2].IsEmpty) (render effects.[2])

let p14 () =
    let pipeline =
        pipelineOf
            GuestPolicy.standard
            (stage ()
             |> CommandStage.withPrediction (fun c s -> { s with Items = s.Items @ [ c ] }))

    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let m, _ =
        steps
            pipeline
            model
            ([ scene InputAction.Activate "x" ]
             @ round 1UL (decided [ submit "c1"; submit "c2" ]))

    let shown m =
        (Displayed.shown (InputPipeline.displayed m)).Items |> String.concat ","

    let predicted m =
        Displayed.predicted (InputPipeline.displayed m)
        |> List.map idText
        |> String.concat ","

    check
        "P14"
        "pending commands are predicted in sequence order"
        (shown m = "c1,c2" && predicted m = "a:1,a:2")
        (shown m)

    let id1 = (Displayed.predicted (InputPipeline.displayed m)).[0]
    let id2 = (Displayed.predicted (InputPipeline.displayed m)).[1]

    let m, _ =
        steps pipeline m [ PipelineMsg.Acknowledged(id1, CommandAck.applied 5UL) ]

    check "P14" "applied at a version not yet reached stays predicted" (predicted m = "a:1,a:2") (predicted m)
    let m, _ = steps pipeline m [ PipelineMsg.Authoritative(server 5UL [ "c1" ]) ]

    check
        "P14"
        "the prediction leaves once the authoritative state reaches the version"
        (predicted m = "a:2" && shown m = "c1,c2")
        (predicted m + "|" + shown m)

    let m, _ =
        steps pipeline m [ PipelineMsg.Acknowledged(id2, CommandAck.rejected "no") ]

    check
        "P14"
        "rejected withdraws the prediction; shown is authoritative"
        (predicted m = "" && shown m = "c1")
        (shown m)

    let plain = standard ()

    let pm, _ =
        steps
            plain
            (InputPipeline.init plain "a" None (server 0UL []))
            ([ scene InputAction.Activate "x" ] @ round 1UL (decided [ submit "c1" ]))

    check
        "P14"
        "without prediction, nothing is predicted"
        ((Displayed.predicted (InputPipeline.displayed pm)).IsEmpty)
        ""

let p15 () =
    let pipeline = standard ()
    let model = InputPipeline.init pipeline "a" None (server 5UL [ "new" ])
    let m, _ = steps pipeline model [ PipelineMsg.Authoritative(server 4UL [ "old" ]) ]

    check
        "P15"
        "an older authoritative state is ignored"
        ((Displayed.authoritative (InputPipeline.displayed m)).Items = [ "new" ])
        ""

let p16 () =
    let pipeline =
        pipelineOf GuestPolicy.standard (stage () |> CommandStage.withPatience 3UL)

    let model = InputPipeline.init pipeline "a" None (server 10UL [])

    let m, _ =
        steps pipeline model ([ scene InputAction.Activate "x" ] @ round 1UL (decided [ submit "c1" ]))

    let m, a = steps pipeline m [ PipelineMsg.Heartbeat 12UL ]

    check
        "P16"
        "below patience nothing settles"
        (a.[0].IsEmpty && (InputPipeline.unsettled m).Length = 1)
        (render a.[0])

    let id = fst (InputPipeline.unsettled m).[0]

    let m, b =
        steps
            pipeline
            m
            [
                PipelineMsg.Acknowledged(id, CommandAck.progress "50%")
                PipelineMsg.Heartbeat 14UL
            ]

    check "P16" "a running answer restarts patience" (b.[1].IsEmpty) (render b.[1])
    let m, c = steps pipeline m [ PipelineMsg.Heartbeat 15UL ]

    check
        "P16"
        "a heartbeat at patience settles Unconfirmed"
        (fails c.[0] = [ "CommandUnconfirmed(a:1)" ]
         && (InputPipeline.unsettled m).IsEmpty)
        (render c.[0])

let p17 () =
    let pipeline =
        pipelineOf
            GuestPolicy.standard
            (stage ()
             |> CommandStage.withSettlement (fun c s -> List.contains c s.Items)
             |> CommandStage.withPrediction (fun c s -> { s with Items = s.Items @ [ c ] }))

    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let m, _ =
        steps pipeline model ([ scene InputAction.Activate "x" ] @ round 1UL (decided [ submit "c1" ]))

    check "P17" "settled never reads the predicted state" ((InputPipeline.unsettled m).Length = 1) ""
    let m, _ = steps pipeline m [ PipelineMsg.Authoritative(server 1UL [ "c1" ]) ]

    check
        "P17"
        "settled applies at the first authoritative version where it holds"
        ((InputPipeline.unsettled m).IsEmpty)
        ""

let p18 () =
    let pipeline = standard ()
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let m, _ =
        steps
            pipeline
            model
            ([ scene InputAction.Activate "x" ]
             @ round 1UL (decided [ submit "c1"; submit "c2"; submit "c3" ]))

    let ids = InputPipeline.unsettled m |> List.map fst

    let m, a =
        steps
            pipeline
            m
            [
                PipelineMsg.Acknowledged(ids.[0], CommandAck.accepted)
                PipelineMsg.Acknowledged(ids.[0], CommandAck.accepted)
            ]

    check
        "P18"
        "a repeated answer is idempotent"
        (a.[1].IsEmpty && (InputPipeline.unsettled m).Length = 3)
        (render a.[1])

    let m, b =
        steps
            pipeline
            m
            [
                PipelineMsg.Acknowledged(ids.[1], CommandAck.cancelled "pre-empted")
                PipelineMsg.Acknowledged(ids.[2], CommandAck.expired)
            ]

    check
        "P18"
        "cancelled is distinct from rejected; expired settles Unconfirmed"
        (fails b.[0] = [ "CommandCancelled(a:2,pre-empted)" ]
         && fails b.[1] = [ "CommandUnconfirmed(a:3)" ])
        (render (List.concat b))

let p18b () =
    let pipeline = standard ()
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let m, _ =
        steps
            pipeline
            model
            ([ scene InputAction.Activate "x" ]
             @ round 1UL (decided [ submit "c1"; submit "c2" ]))

    let ids = InputPipeline.unsettled m |> List.map fst

    let m, a =
        steps
            pipeline
            m
            [
                PipelineMsg.Acknowledged(ids.[0], CommandAck.rejected "no")
                PipelineMsg.Acknowledged(ids.[0], CommandAck.started)
                PipelineMsg.Acknowledged(ids.[0], CommandAck.applied 1UL)
            ]

    check
        "P18b"
        "the first final answer wins"
        (a.[1].IsEmpty
         && a.[2].IsEmpty
         && (InputPipeline.unsettled m |> List.map (fst >> idText)) = [ "a:2" ])
        (render (List.concat a))

    let m, b =
        steps
            pipeline
            m
            [
                PipelineMsg.Acknowledged(ids.[1], CommandAck.queued)
                PipelineMsg.Acknowledged(ids.[1], CommandAck.started)
            ]

    let state = InputPipeline.unsettled m |> List.map snd

    check
        "P18b"
        "queued then started moves an open command to running"
        (state.Length = 1 && CommandSettlement.isRunning state.[0])
        ""

    let stranger = (CommandId.tryParse "zz:9").Value

    let _, c =
        steps pipeline m [ PipelineMsg.Acknowledged(stranger, CommandAck.applied 1UL) ]

    check "P18b" "an answer for an unknown id changes nothing" (c.[0].IsEmpty) ""

let p19 () =
    let refused (f: unit -> unit) =
        try
            f ()
            false
        with _ ->
            true

    check "P19" "zero patience is refused" (refused (fun () -> (CommandStage.withPatience 0UL (stage ())) |> ignore)) ""
    check "P19" "an empty window is refused" (refused (fun () -> (CommandStage.withWindow 0 (stage ())) |> ignore)) ""

    check
        "P19"
        "a zero breaker is refused"
        (refused (fun () -> (GuestPolicy.withBreaker 0 GuestPolicy.standard) |> ignore))
        ""

    check
        "P19"
        "a zero start deadline is refused"
        (refused (fun () -> (GuestPolicy.withStartTimeout 0 GuestPolicy.standard) |> ignore))
        ""

    check
        "P19"
        "a blank client is refused"
        (refused (fun () -> (InputPipeline.init (standard ()) " " None (server 0UL [])) |> ignore))
        ""

/// Randomized invariants: P1 (ids unique, sequences rising), P2 (one call), P10 (no batch carries both a Local and a
/// StartGuest; Continue is last), P14 (shown is the fold of predicted).
let invariants () =
    let rng = Rng 20261102
    let mutable violations = []
    let scripts = 150

    for script in 1..scripts do
        let pipeline =
            pipelineOf
                (GuestPolicy.standard
                 |> GuestPolicy.withQueueLimit 3
                 |> GuestPolicy.withBreaker 2)
                (stage ()
                 |> CommandStage.withWindow 3
                 |> CommandStage.withPatience 4UL
                 |> CommandStage.withPrediction (fun c s -> { s with Items = s.Items @ [ c ] }))

        let mutable model = InputPipeline.init pipeline "c" None (server 0UL [])
        let mutable version = 0UL
        let mutable lastCall = 0UL
        let mutable flight: GuestCall<string> option = None
        let seenIds = System.Collections.Generic.HashSet<string>()
        let mutable lastSeq = 0UL
        let mutable binding = 0

        for _ in 1..40 do
            let msg: Msg =
                match rng.Next 12, flight with
                | 0, _
                | 1, _ -> scene InputAction.Activate (string (rng.Next 5))
                | 2, _ -> scene InputAction.Hover (string (rng.Next 5))
                | 3, _ -> PipelineMsg.Submit(sprintf "s%d" (rng.Next 9))
                | 4, Some call -> PipelineMsg.GuestRunning(GuestCall.id call)
                | 5, Some call ->
                    let n = rng.Next 3

                    PipelineMsg.GuestEnded(
                        GuestCall.id call,
                        decided
                            [
                                for i in 1..n ->
                                    if rng.Next 2 = 0 then
                                        local "l"
                                    else
                                        submit (sprintf "d%d" i)
                            ]
                    )
                | 6, Some call -> PipelineMsg.GuestDeadline(GuestCall.id call)
                | 7, _ ->
                    version <- version + uint64 (rng.Next 3)
                    PipelineMsg.Authoritative(server version [])
                | 8, _ -> PipelineMsg.Heartbeat(version + uint64 (rng.Next 6))
                | 9, _ ->
                    match InputPipeline.unsettled model with
                    | [] -> PipelineMsg.StartNext
                    | open' ->
                        let id = fst open'.[rng.Next open'.Length]

                        let ack =
                            match rng.Next 5 with
                            | 0 -> CommandAck.accepted
                            | 1 -> CommandAck.started
                            | 2 -> CommandAck.applied (version + 1UL)
                            | 3 -> CommandAck.rejected "r"
                            | _ -> CommandAck.cancelled "c"

                        PipelineMsg.Acknowledged(id, ack)
                | 10, _ ->
                    binding <- binding + 1
                    PipelineMsg.Rebind(sprintf "c%d" binding, Some(uint64 (rng.Next 4)))
                | _ -> PipelineMsg.StartNext

            let next, effects = InputPipeline.update pipeline msg model
            model <- next

            let hasLocal =
                effects
                |> List.exists (fun e ->
                    match e with
                    | PipelineEffect.ApplyLocal _ -> true
                    | _ -> false)

            let startCount = (starts effects).Length

            if hasLocal && startCount > 0 then
                violations <-
                    sprintf "script %d: a batch carries ApplyLocal and StartGuest" script
                    :: violations

            let isContinue (e: Effect) =
                match e with
                | PipelineEffect.Continue -> true
                | _ -> false

            if effects |> List.exists isContinue && not (isContinue (last effects)) then
                violations <- sprintf "script %d: Continue is not last" script :: violations

            if startCount > 1 then
                violations <- sprintf "script %d: two calls started" script :: violations

            for call in starts effects do
                if GuestCall.id call <= lastCall then
                    violations <- sprintf "script %d: call id repeated" script :: violations

                if flight.IsSome then
                    violations <- sprintf "script %d: a call started while one is in flight" script :: violations

                lastCall <- GuestCall.id call
                flight <- Some call

            for e in effects do
                match e with
                | PipelineEffect.Send envelope ->
                    let text = idText (CommandEnvelope.id envelope)

                    if not (seenIds.Add text) then
                        violations <- sprintf "script %d: id %s issued twice" script text :: violations

                    if CommandId.sequence (CommandEnvelope.id envelope) <= lastSeq then
                        violations <- sprintf "script %d: command sequence did not rise" script :: violations

                    lastSeq <- CommandId.sequence (CommandEnvelope.id envelope)
                | PipelineEffect.Continue
                | PipelineEffect.CancelGuest _ -> flight <- None
                | _ -> ()

            let displayed = InputPipeline.displayed model

            if
                (Displayed.shown displayed).Items.Length
                <> (Displayed.predicted displayed).Length
            then
                violations <- sprintf "script %d: shown is not the fold of predicted" script :: violations

    check
        "P1,P2,P10,P14"
        (sprintf "randomized invariants over %d scripts of 40 messages" scripts)
        violations.IsEmpty
        (violations |> List.truncate 3 |> String.concat "; ")

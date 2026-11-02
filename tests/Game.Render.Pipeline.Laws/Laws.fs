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

let failureText (failure: PipelineFailure) = PipelineFailure.text failure

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

    // The predicted state shows c1 (prediction appends it); the authoritative state at version 1 does not.
    let m, early = steps pipeline m [ PipelineMsg.Authoritative(server 1UL []) ]

    check
        "P17"
        "settled never reads the predicted state"
        ((InputPipeline.unsettled m).Length = 1
         && (Displayed.shown (InputPipeline.displayed m)).Items = [ "c1" ])
        (render early.[0])

    let m, _ = steps pipeline m [ PipelineMsg.Authoritative(server 2UL [ "c1" ]) ]

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
                // Applied at a version the state has not reached, so the command stays in the ledger (prediction needs
                // it); a reordered `started` and a later `rejected` must not reopen or change it.
                PipelineMsg.Acknowledged(ids.[0], CommandAck.applied 3UL)
                PipelineMsg.Acknowledged(ids.[0], CommandAck.started)
                PipelineMsg.Acknowledged(ids.[0], CommandAck.rejected "no")
            ]

    check
        "P18b"
        "the first final answer wins: started and rejected after applied change nothing"
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

// ---------------------------------------------------------------------------------------------------------------
// Laws added by the #796 review (FS.GG.Game#788): each pins a behaviour a mutant could change unnoticed.

let withPrediction (s: CommandStage<Server, string>) =
    s |> CommandStage.withPrediction (fun c state -> { state with Items = state.Items @ [ c ] })

let unsettledText (m: Model) =
    InputPipeline.unsettled m |> List.map (fst >> idText) |> String.concat ","

let predictedText (m: Model) =
    Displayed.predicted (InputPipeline.displayed m) |> List.map idText |> String.concat ","

/// P11: the window is checked for ALL of a decision's submits, and counts every unsettled command, the earlier
/// binding's included.
let p11Window () =
    let pipeline = pipelineOf GuestPolicy.standard (stage () |> CommandStage.withWindow 3)
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let _, effects =
        steps
            pipeline
            model
            ([ scene InputAction.Activate "x" ]
             @ round 1UL (decided [ submit "c1"; submit "c2" ])
             @ [ scene InputAction.Activate "y" ]
             @ round 2UL (decided [ submit "c3"; submit "c4" ]))

    let all = List.concat effects

    check
        "P11"
        "two submits do not fit a window with room for one"
        ((sends all |> List.map CommandEnvelope.command) = [ "c1"; "c2" ]
         && fails all = [ "CommandWindowFull(2)" ])
        (render all)

    let single = pipelineOf GuestPolicy.standard (stage () |> CommandStage.withWindow 1)
    let model = InputPipeline.init single "a" None (server 0UL [])

    let _, effects =
        steps
            single
            model
            ([ scene InputAction.Activate "x" ]
             @ round 1UL (decided [ submit "c1" ])
             @ [ PipelineMsg.Rebind("b", None); scene InputAction.Activate "y" ]
             @ round 2UL (decided [ submit "c2" ]))

    let all = List.concat effects

    check
        "P11"
        "the window counts the earlier binding's unsettled commands"
        ((sends all |> List.map (CommandEnvelope.id >> idText)) = [ "a:1" ]
         && fails all = [ "CommandWindowFull(2)" ])
        (render all)

/// P18 and P18b: a final command is never reopened or changed, and `Unconfirmed` is never upgraded (implementation
/// decision "Unconfirmed is final").
let p18Final () =
    let pipeline = pipelineOf GuestPolicy.standard (stage () |> withPrediction)
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let m, _ =
        steps pipeline model ([ scene InputAction.Activate "x" ] @ round 1UL (decided [ submit "c1"; submit "c2" ]))

    let ids = InputPipeline.unsettled m |> List.map fst

    let m, a =
        steps
            pipeline
            m
            [
                PipelineMsg.Acknowledged(ids.[0], CommandAck.applied 5UL)
                PipelineMsg.Acknowledged(ids.[0], CommandAck.cancelled "late")
                PipelineMsg.Acknowledged(ids.[0], CommandAck.queued)
            ]

    check
        "P18b"
        "answers after applied neither fail nor reopen the command"
        (a.[1].IsEmpty && a.[2].IsEmpty && unsettledText m = "a:2" && predictedText m = "a:1,a:2")
        (render (List.concat a) + " | " + unsettledText m + " | " + predictedText m)

    let m, b =
        steps
            pipeline
            m
            [
                PipelineMsg.Acknowledged(ids.[1], CommandAck.expired)
                PipelineMsg.Acknowledged(ids.[1], CommandAck.applied 7UL)
                PipelineMsg.Acknowledged(ids.[1], CommandAck.ended 7UL "done")
            ]

    check
        "P18"
        "Unconfirmed is final: a late applied or ended does not upgrade or predict it"
        (fails b.[0] = [ "CommandUnconfirmed(a:2)" ]
         && b.[1].IsEmpty
         && b.[2].IsEmpty
         && unsettledText m = ""
         && predictedText m = "a:1")
        (render (List.concat b) + " | " + predictedText m)

/// P16: where patience starts (B1 of the #796 review), what restarts it, and that it runs for earlier bindings.
let p16Patience () =
    let pipeline =
        pipelineOf GuestPolicy.standard (stage () |> CommandStage.withPatience 4UL |> withPrediction)

    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    // Heartbeats advance what is heard to 10 before the command is sent; none of that counts against it.
    let m, _ = steps pipeline model [ PipelineMsg.Heartbeat 10UL ]

    let m, a =
        steps
            pipeline
            m
            ([ scene InputAction.Activate "x" ]
             @ round 1UL (decided [ submit "c1" ])
             @ [ PipelineMsg.Heartbeat 10UL; PipelineMsg.Heartbeat 13UL; PipelineMsg.Heartbeat 14UL ])

    check
        "P16"
        "patience starts at the later of the basis and the version heard when the command was sent"
        ((a |> List.take 6 |> List.concat |> fails) = []
         && fails a.[6] = [ "CommandUnconfirmed(a:1)" ])
        (render (List.concat a))

    let fresh = InputPipeline.init pipeline "a" None (server 0UL [])

    let m, _ =
        steps pipeline fresh ([ scene InputAction.Activate "x" ] @ round 1UL (decided [ submit "c1"; submit "c2" ]))

    let ids = InputPipeline.unsettled m |> List.map fst

    let m, b =
        steps
            pipeline
            m
            [
                PipelineMsg.Heartbeat 3UL
                PipelineMsg.Acknowledged(ids.[0], CommandAck.queued)
                PipelineMsg.Acknowledged(ids.[1], CommandAck.accepted)
                PipelineMsg.Heartbeat 6UL
                PipelineMsg.Heartbeat 7UL
            ]

    check
        "P16"
        "queued and accepted restart patience"
        (fails b.[3] = [] && fails b.[4] = [ "CommandUnconfirmed(a:1)"; "CommandUnconfirmed(a:2)" ])
        (render (List.concat b))

    let m, _ =
        steps pipeline fresh ([ scene InputAction.Activate "x" ] @ round 1UL (decided [ submit "c1" ]))

    let id = fst (InputPipeline.unsettled m).[0]

    let m, c =
        steps
            pipeline
            m
            [
                PipelineMsg.Acknowledged(id, CommandAck.applied 3UL)
                PipelineMsg.Heartbeat 5UL
            ]

    check
        "P16"
        "a heartbeat never advances prediction"
        (c.[1].IsEmpty && predictedText m = "a:1" && (Displayed.authoritative (InputPipeline.displayed m)).V = 0UL)
        (render (List.concat c) + " | " + predictedText m)

    let m, _ =
        steps pipeline fresh ([ scene InputAction.Activate "x" ] @ round 1UL (decided [ submit "c1" ]))

    let _, d = steps pipeline m [ PipelineMsg.Rebind("b", None); PipelineMsg.Heartbeat 4UL ]

    check
        "P16"
        "an earlier binding's command still settles Unconfirmed by patience"
        (fails d.[1] = [ "CommandUnconfirmed(a:1)" ])
        (render (List.concat d))

/// P10b: queue order of `Submit`s, and implementation decisions 1 and 2 (submits do not count toward the guest queue
/// limit; a halt keeps them).
let p10bQueue () =
    let pipeline = standard ()
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let _, effects =
        steps
            pipeline
            model
            ([ scene InputAction.Activate "x"; scene InputAction.Activate "y"; PipelineMsg.Submit "s" ]
             @ round 1UL (decided [])
             @ round 2UL (decided []))

    check
        "P10b"
        "a Submit waits behind an earlier queued input"
        (render effects.[5] = "Start(2,2,activate:y,0) ArmStart(2,10000)"
         && render effects.[8] = "Send(a:1,-,0,s)")
        (render effects.[5] + " | " + render effects.[8])

    let small = pipelineOf (GuestPolicy.standard |> GuestPolicy.withQueueLimit 1) (stage ())
    let model = InputPipeline.init small "a" None (server 0UL [])

    let _, effects =
        steps
            small
            model
            [
                scene InputAction.Activate "x"
                PipelineMsg.Submit "s1"
                PipelineMsg.Submit "s2"
                scene InputAction.Activate "y"
            ]

    check
        "P10b"
        "queued Submits do not count toward the guest queue limit"
        ((last effects).IsEmpty)
        (render (last effects))

    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let m, effects =
        steps
            pipeline
            model
            ([ scene InputAction.Activate "x"; PipelineMsg.Submit "s" ]
             @ round 1UL (GuestOutcome.Faulted "trap"))

    check
        "P10b"
        "a halt keeps queued Submits, which are sent while the stage is halted"
        (InputPipeline.guestHalted m && render (last effects) = "Send(a:1,-,0,s)")
        (render (List.concat effects))

/// P4: what resets the breaker, what a halt does to the queue, and a duplicate GuestRunning.
let p4Breaker () =
    let pipeline = pipelineOf (GuestPolicy.standard |> GuestPolicy.withBreaker 2) (stage ())
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let between outcome =
        [ scene InputAction.Activate "x" ]
        @ round 1UL GuestOutcome.TimedOut
        @ [ scene InputAction.Activate "y" ]
        @ round 2UL outcome
        @ [ scene InputAction.Activate "z" ]
        @ round 3UL GuestOutcome.TimedOut

    let refused, _ = steps pipeline model (between (GuestOutcome.Refused "no"))
    check "P4" "a refused call resets the breaker" (not (InputPipeline.guestHalted refused)) ""

    let cancelled, _ = steps pipeline model (between (GuestOutcome.Cancelled "frozen"))

    check
        "P4"
        "a cancelled call does not reset the breaker"
        (InputPipeline.guestHalted cancelled)
        ""

    let m, effects =
        steps
            pipeline
            model
            [
                scene InputAction.Activate "x"
                scene InputAction.Activate "y"
                game "g"
                PipelineMsg.GuestRunning 1UL
                PipelineMsg.GuestEnded(1UL, GuestOutcome.Faulted "trap")
                PipelineMsg.StartNext
                PipelineMsg.RearmGuest
            ]

    check
        "P4"
        "a halt refuses the queued guest inputs at once, and RearmGuest finds nothing to start"
        (fails effects.[4] = [ "GuestFaulted(1,trap)"; "InputRefused(2,guest halted)"; "InputRefused(3,guest halted)" ]
         && effects.[5].IsEmpty
         && effects.[6].IsEmpty
         && not (InputPipeline.guestHalted m))
        (render (List.concat effects))

    let _, effects =
        steps pipeline model [ scene InputAction.Activate "x"; PipelineMsg.GuestRunning 1UL; PipelineMsg.GuestRunning 1UL ]

    check "P4" "a duplicate GuestRunning arms no second deadline" (effects.[2].IsEmpty) (render effects.[2])

/// P15: an authoritative state at the SAME version is taken (versions are non-decreasing), and settles commands.
let p15Equal () =
    let pipeline =
        pipelineOf GuestPolicy.standard (stage () |> CommandStage.withSettlement (fun c s -> List.contains c s.Items))

    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let m, _ =
        steps
            pipeline
            model
            ([ scene InputAction.Activate "x" ]
             @ round 1UL (decided [ submit "c1" ])
             @ [
                 PipelineMsg.Authoritative(server 1UL [])
                 PipelineMsg.Authoritative(server 1UL [ "c1" ])
             ])

    check
        "P15"
        "a state at the same version is taken and settles"
        ((InputPipeline.unsettled m).IsEmpty
         && (Displayed.authoritative (InputPipeline.displayed m)).Items = [ "c1" ])
        (unsettledText m)

/// Implementation decision "envelope basis": a decided command's basis is the authoritative version when the decision
/// ended the call, not the call's basis.
let envelopeBasis () =
    let pipeline = standard ()
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let _, effects =
        steps
            pipeline
            model
            [
                scene InputAction.Activate "x"
                PipelineMsg.GuestRunning 1UL
                PipelineMsg.Authoritative(server 2UL [])
                PipelineMsg.GuestEnded(1UL, decided [ submit "c1" ])
                PipelineMsg.StartNext
                PipelineMsg.Submit "s"
            ]

    check
        "P17"
        "a command's basis is the authoritative version at its decision (the call's basis was 0)"
        (render effects.[0] = "Start(1,1,activate:x,0) ArmStart(1,10000)"
         && render effects.[3] = "Send(a:1,1,2,c1) Continue"
         && render effects.[5] = "Send(a:2,-,2,s)")
        (render (List.concat effects))

/// P7b across a Rebind (B2 of the #796 review): a call that ended before the rebind still has its StartNext on the way,
/// behind the game's Local; an input that arrives after the rebind must not start before it.
let p7bRebind () =
    let pipeline = standard ()
    let mutable model = InputPipeline.init pipeline "a" None (server 0UL [])
    let mutable gameState = 0
    let mutable calls = 0
    let seen = ResizeArray<string>()
    let queue = System.Collections.Generic.Queue<Choice<Msg, string>>()

    let rec drain () =
        if queue.Count > 0 then
            match queue.Dequeue() with
            | Choice2Of2 _ -> gameState <- gameState + 1
            | Choice1Of2 msg ->
                let next, effects = InputPipeline.update pipeline msg model
                model <- next

                for effect in effects do
                    match effect with
                    | PipelineEffect.StartGuest call ->
                        calls <- calls + 1
                        seen.Add(sprintf "%d@%d" (GuestCall.sequence call) gameState)

                        if calls = 1 then
                            queue.Enqueue(Choice1Of2(PipelineMsg.GuestRunning(GuestCall.id call)))
                            queue.Enqueue(Choice1Of2(PipelineMsg.GuestEnded(GuestCall.id call, decided [ local "bump" ])))
                            queue.Enqueue(Choice1Of2(PipelineMsg.Rebind("a", None)))
                            queue.Enqueue(Choice1Of2(scene InputAction.Activate "y"))
                    | PipelineEffect.ApplyLocal l -> queue.Enqueue(Choice2Of2 l)
                    | PipelineEffect.Continue -> queue.Enqueue(Choice1Of2 PipelineMsg.StartNext)
                    | _ -> ()

            drain ()

    queue.Enqueue(Choice1Of2(scene InputAction.Activate "x"))
    drain ()
    let observed = String.concat "," seen

    check "P7b" "a Rebind does not let the next call start before the ended call's Locals" (observed = "1@0,2@1") observed

/// P10 for a Rebind, and implementation decision "queued Submits survive a Rebind": the call is cancelled, the queued
/// Submit is sent under the new binding, then the failures.
let p10Rebind () =
    let pipeline = standard ()
    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let _, effects =
        steps
            pipeline
            model
            [
                scene InputAction.Activate "x"
                scene InputAction.Activate "y"
                PipelineMsg.Submit "s"
                PipelineMsg.Rebind("b", Some 7UL)
            ]

    check
        "P10"
        "a Rebind's batch: CancelGuest, the queued Submit under the new binding, then Fail"
        (render (last effects) = "Cancel(1) Send(b:8,-,0,s) Fail(InputCancelled(1,rebind)) Fail(InputCancelled(2,rebind))")
        (render (last effects))

/// The rendered effects of every randomized step, in order: part of the digest the .NET and Fable runs must agree on.
let transcript = ResizeArray<string>()

/// An independent reference model of the command stage (ADR 0001 stage 3), for the randomized pass: which commands
/// are open and in what running state, where their patience starts, which are applied at what version, and the
/// failures each message must route.
type private OracleState =
    | OOpen of kind: string
    | OApplied of version: uint64
    | OFinal

type private OracleEntry =
    {
        Id: string
        Command: string
        mutable State: OracleState
        mutable Origin: uint64
    }

/// Randomized invariants over 200 scripts of 50 messages, every message kind generated (stale replies, RearmGuest,
/// Faulted, start deadlines, every answer, older and non-+1 versions, rebinds above the highest sequence issued):
/// P1 (ids unique, sequences rising), P2 (one call), P5 (a stale reply changes nothing), P10 (no batch carries both a
/// Local and a StartGuest; Continue is last), P11 (the window is never exceeded; a decision fits whole or not at all),
/// and, against the reference model, P14 (predicted and shown), P15, P16 (patience), P17 (settlement), P18 and P18b
/// (answers, first final wins).
let invariants () =
    let rng = Rng 20261102
    let mutable violations = []
    let scripts = 200
    let window = 3
    let patienceOf = 4UL
    let counts = System.Collections.Generic.Dictionary<string, int>()

    let count (kind: string) =
        counts.[kind] <-
            (match counts.TryGetValue kind with
             | true, n -> n + 1
             | _ -> 1)

    for script in 1..scripts do
        let violation (text: string) =
            violations <- sprintf "script %d: %s" script text :: violations

        let pipeline =
            pipelineOf
                (GuestPolicy.standard
                 |> GuestPolicy.withQueueLimit 3
                 |> GuestPolicy.withBreaker 2)
                (stage ()
                 |> CommandStage.withWindow window
                 |> CommandStage.withPatience patienceOf
                 |> CommandStage.withSettlement (fun c s -> List.contains c s.Items)
                 |> withPrediction)

        let mutable model = InputPipeline.init pipeline "c" None (server 0UL [])
        let mutable version = 0UL
        let mutable lastCall = 0UL
        let mutable flight: GuestCall<string> option = None
        let seenIds = System.Collections.Generic.HashSet<string>()
        let mutable lastSeq = 0UL
        let mutable binding = 0
        // The reference model.
        let oracle = ResizeArray<OracleEntry>()
        let mutable authV = 0UL
        let mutable heard = 0UL
        let mutable answer: (string * uint64) option = None
        let isOpen (entry: OracleEntry) =
            match entry.State with
            | OOpen _ -> true
            | _ -> false

        let openCount () = oracle |> Seq.filter isOpen |> Seq.length

        let patience () =
            [
                for entry in oracle do
                    if isOpen entry && heard >= entry.Origin && heard - entry.Origin >= patienceOf then
                        entry.State <- OFinal
                        yield "CommandUnconfirmed(" + entry.Id + ")"
            ]

        for _ in 1..50 do
            answer <- None
            let issued = oracle |> Seq.map (fun entry -> entry.Command) |> List.ofSeq

            let msg: Msg =
                match rng.Next 24, flight with
                | (0 | 1 | 2), _ -> scene InputAction.Activate (string (rng.Next 5))
                | 3, _ -> scene InputAction.Hover (string (rng.Next 5))
                | 4, _ -> game (string (rng.Next 3))
                | 5, _ -> PipelineMsg.Submit(sprintf "s%d" (rng.Next 9))
                | 6, Some call -> PipelineMsg.GuestRunning(GuestCall.id call)
                | (7 | 8), Some call ->
                    let n = rng.Next 4

                    PipelineMsg.GuestEnded(
                        GuestCall.id call,
                        decided
                            [
                                for i in 1..n ->
                                    if rng.Next 2 = 0 then
                                        local "l"
                                    else
                                        submit (sprintf "d%d" (rng.Next 9))
                            ]
                    )
                | 9, Some call ->
                    let outcome =
                        match rng.Next 4 with
                        | 0 -> GuestOutcome.Faulted "trap"
                        | 1 -> GuestOutcome.Refused "no"
                        | 2 -> GuestOutcome.Cancelled "frozen"
                        | _ -> GuestOutcome.TimedOut

                    PipelineMsg.GuestEnded(GuestCall.id call, outcome)
                | 10, Some call -> PipelineMsg.GuestDeadline(GuestCall.id call)
                | 11, Some call -> PipelineMsg.GuestStartDeadline(GuestCall.id call)
                | 12, _ when lastCall > 0UL ->
                    // A stale reply: for a call that is not the one in flight.
                    let stale = 1UL + uint64 (rng.Next(int lastCall))

                    if flight |> Option.exists (fun call -> GuestCall.id call = stale) then
                        PipelineMsg.StartNext
                    else
                        PipelineMsg.GuestEnded(stale, decided [ submit "stale" ])
                | (13 | 14), _ ->
                    version <- version + uint64 (rng.Next 4)
                    let items = issued |> List.filter (fun _ -> rng.Next 3 = 0)
                    PipelineMsg.Authoritative(server version items)
                | 15, _ when version > 0UL -> PipelineMsg.Authoritative(server (version - 1UL) issued)
                | 16, _ -> PipelineMsg.Heartbeat(version + uint64 (rng.Next 6))
                | (17 | 18 | 19), _ when oracle.Count > 0 ->
                    let entry = oracle.[rng.Next oracle.Count]
                    let at = version + uint64 (rng.Next 3)

                    // The answer is opaque, so the generator tells the reference model what it is.
                    let described, ack =
                        match rng.Next 9 with
                        | 0 -> "accepted", CommandAck.accepted
                        | 1 -> "queued", CommandAck.queued
                        | 2 -> "running", CommandAck.started
                        | 3 -> "running", CommandAck.progress "p"
                        | 4 -> "applied", CommandAck.applied at
                        | 5 -> "applied", CommandAck.ended at "o"
                        | 6 -> "rejected", CommandAck.rejected "r"
                        | 7 -> "cancelled", CommandAck.cancelled "c"
                        | _ -> "expired", CommandAck.expired

                    answer <- Some(described, at)
                    PipelineMsg.Acknowledged((CommandId.tryParse entry.Id).Value, ack)
                | 20, _ -> PipelineMsg.RearmGuest
                | 21, _ when rng.Next 2 = 0 ->
                    binding <- binding + 1

                    let last =
                        if rng.Next 2 = 0 then
                            lastSeq + uint64 (rng.Next 4)
                        else
                            uint64 (rng.Next 3)

                    PipelineMsg.Rebind(sprintf "c%d" binding, Some last)
                | _ -> PipelineMsg.StartNext

            let kind =
                match msg with
                | PipelineMsg.Input _ -> "input"
                | PipelineMsg.Submit _ -> "submit"
                | PipelineMsg.StartNext -> "startNext"
                | PipelineMsg.GuestRunning _ -> "running"
                | PipelineMsg.GuestStartDeadline _ -> "startDeadline"
                | PipelineMsg.GuestEnded(_, GuestOutcome.Decided _) -> "decided"
                | PipelineMsg.GuestEnded _ -> "ended"
                | PipelineMsg.GuestDeadline _ -> "deadline"
                | PipelineMsg.RearmGuest -> "rearm"
                | PipelineMsg.Acknowledged _ -> "ack"
                | PipelineMsg.Authoritative _ -> "authoritative"
                | PipelineMsg.Heartbeat _ -> "heartbeat"
                | PipelineMsg.Rebind _ -> "rebind"

            count kind
            let openBefore = openCount ()
            let flightBefore = flight
            let next, effects = InputPipeline.update pipeline msg model
            model <- next
            transcript.Add(render effects)

            let hasLocal =
                effects
                |> List.exists (fun e ->
                    match e with
                    | PipelineEffect.ApplyLocal _ -> true
                    | _ -> false)

            let startCount = (starts effects).Length

            if hasLocal && startCount > 0 then
                violation "a batch carries ApplyLocal and StartGuest"

            let isContinue (e: Effect) =
                match e with
                | PipelineEffect.Continue -> true
                | _ -> false

            if effects |> List.exists isContinue && not (isContinue (last effects)) then
                violation "Continue is not last"

            if startCount > 1 then
                violation "two calls started"

            // P5: a reply for a call that is not in flight changes nothing.
            match msg with
            | PipelineMsg.GuestEnded(call, _) when not (flightBefore |> Option.exists (fun f -> GuestCall.id f = call)) ->
                if not effects.IsEmpty then
                    violation ("a stale reply had effects: " + render effects)
            | _ -> ()

            for call in starts effects do
                if GuestCall.id call <= lastCall then
                    violation "call id repeated"

                if flight.IsSome then
                    violation "a call started while one is in flight"

                lastCall <- GuestCall.id call
                flight <- Some call

            let sent = ResizeArray<string>()

            for e in effects do
                match e with
                | PipelineEffect.Send envelope ->
                    let text = idText (CommandEnvelope.id envelope)
                    sent.Add(CommandEnvelope.command envelope)

                    if not (seenIds.Add text) then
                        violation ("id issued twice: " + text)

                    if CommandId.sequence (CommandEnvelope.id envelope) <= lastSeq then
                        violation "command sequence did not rise"

                    lastSeq <- CommandId.sequence (CommandEnvelope.id envelope)

                    if CommandEnvelope.basis envelope <> authV then
                        violation ("a command's basis is not the authoritative version: " + text)

                    oracle.Add
                        {
                            Id = text
                            Command = CommandEnvelope.command envelope
                            State = OOpen "pending"
                            Origin = max authV heard
                        }
                | PipelineEffect.Continue
                | PipelineEffect.CancelGuest _ -> flight <- None
                | _ -> ()

            // P11: a decision fits whole, or not at all.
            match msg with
            | PipelineMsg.GuestEnded(call, GuestOutcome.Decided intents) when
                flightBefore |> Option.exists (fun f -> GuestCall.id f = call)
                ->
                let wanted =
                    intents
                    |> List.choose (fun intent ->
                        match intent with
                        | GuestIntent.Submit c -> Some c
                        | _ -> None)

                let full =
                    fails effects
                    |> List.exists (fun f -> f.StartsWith "CommandWindowFull(")

                if not wanted.IsEmpty && openBefore + wanted.Length > window then
                    if not full || sent.Count > 0 || hasLocal then
                        violation ("a decision that does not fit applied something: " + render effects)
                elif List.ofSeq sent <> wanted || full then
                    violation ("a decision that fits was not sent whole: " + render effects)
            | _ -> ()

            // The reference model's own transition, and the command failures it says this message routes.
            let expected =
                match msg with
                | PipelineMsg.Acknowledged(id, _) ->
                    let text = idText id

                    match oracle |> Seq.tryFind (fun entry -> entry.Id = text), answer with
                    | Some entry, Some(answered, at) when isOpen entry ->
                        let restart kind =
                            entry.State <- OOpen kind
                            entry.Origin <- heard
                            []

                        match answered with
                        | "accepted" ->
                            restart (
                                match entry.State with
                                | OOpen "pending" -> "accepted"
                                | OOpen kind -> kind
                                | _ -> "accepted"
                            )
                        | "queued" -> restart "queued"
                        | "running" -> restart "running"
                        | "applied" ->
                            entry.State <- OApplied at
                            []
                        | "rejected" ->
                            entry.State <- OFinal
                            [ "CommandRejected(" + text + ",r)" ]
                        | "cancelled" ->
                            entry.State <- OFinal
                            [ "CommandCancelled(" + text + ",c)" ]
                        | _ ->
                            entry.State <- OFinal
                            [ "CommandUnconfirmed(" + text + ")" ]
                    | _ -> []
                | PipelineMsg.Authoritative state ->
                    let v = state.V

                    if v < authV then
                        []
                    else
                        authV <- v
                        heard <- max heard v

                        for entry in oracle do
                            if isOpen entry && List.contains entry.Command state.Items then
                                entry.State <- OApplied v

                        patience ()
                | PipelineMsg.Heartbeat v ->
                    heard <- max heard v
                    patience ()
                | _ -> []

            let routed =
                fails effects
                |> List.filter (fun f ->
                    f.StartsWith "CommandUnconfirmed("
                    || f.StartsWith "CommandRejected("
                    || f.StartsWith "CommandCancelled(")

            if routed <> expected then
                violation (
                    "routed "
                    + String.concat "," routed
                    + " but the reference model routes "
                    + String.concat "," expected
                )

            if openCount () > window then
                violation "the window was exceeded"

            let openIds =
                oracle
                |> Seq.choose (fun entry ->
                    match entry.State with
                    | OOpen kind -> Some(entry.Id + "=" + kind)
                    | _ -> None)
                |> String.concat ","

            let unsettled =
                InputPipeline.unsettled model
                |> List.map (fun (id, settlement) ->
                    idText id
                    + "="
                    + (if CommandSettlement.isPending settlement then "pending"
                       elif CommandSettlement.isRunning settlement then "running"
                       elif CommandSettlement.isQueued settlement then "queued"
                       elif CommandSettlement.isAccepted settlement then "accepted"
                       else "?"))
                |> String.concat ","

            if unsettled <> openIds then
                violation ("unsettled " + unsettled + " but the reference model has " + openIds)

            let displayed = InputPipeline.displayed model

            if (Displayed.authoritative displayed).V <> authV then
                violation "the authoritative version is not the reference model's"

            let predicted =
                oracle
                |> Seq.filter (fun entry ->
                    match entry.State with
                    | OOpen _ -> true
                    | OApplied v -> v > authV
                    | OFinal -> false)
                |> List.ofSeq

            if predictedText model <> (predicted |> List.map (fun entry -> entry.Id) |> String.concat ",") then
                violation ("predicted " + predictedText model + " is not the reference model's")

            if
                (Displayed.shown displayed).Items
                <> (Displayed.authoritative displayed).Items @ (predicted |> List.map (fun entry -> entry.Command))
            then
                violation "shown is not predict folded over predicted"

    check
        "P1,P2,P5,P10,P11,P14-P18b"
        (sprintf "randomized invariants against a reference model over %d scripts of 50 messages" scripts)
        violations.IsEmpty
        (violations |> List.rev |> List.truncate 3 |> String.concat "; ")

    let generated =
        [ "input"; "submit"; "startNext"; "running"; "startDeadline"; "decided"; "ended"; "deadline"; "rearm"; "ack"; "authoritative"; "heartbeat"; "rebind" ]
        |> List.map (fun kind ->
            kind,
            (match counts.TryGetValue kind with
             | true, n -> n
             | _ -> 0))

    check
        "P1,P2,P5,P10,P11,P14-P18b"
        "the randomized pass generates every message kind"
        (generated |> List.forall (fun (_, n) -> n > 0))
        (generated |> List.map (fun (k, n) -> sprintf "%s=%d" k n) |> String.concat " ")

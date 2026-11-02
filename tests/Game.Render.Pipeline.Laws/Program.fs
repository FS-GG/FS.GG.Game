module Game.Render.Pipeline.Laws.Program

open FS.GG.Game.Render
open Game.Render.Pipeline.Laws.Laws

let private request (client: int) : GuestRequest<string, int> =
    let pipeline = standard ()

    let model = InputPipeline.init pipeline "a" None (server 0UL [])

    let _, effects =
        InputPipeline.update pipeline (scene InputAction.Activate "x") model

    GuestRequest.create (starts effects).Head client

let private outcomeText (outcome: GuestOutcome<string, string>) =
    match outcome with
    | GuestOutcome.Decided intents ->
        "Decided("
        + (intents
           |> List.map (fun i ->
               match i with
               | GuestIntent.Local l -> "L:" + l
               | GuestIntent.Submit c -> "S:" + c)
           |> String.concat ",")
        + ")"
    | GuestOutcome.Refused r -> "Refused(" + r + ")"
    | GuestOutcome.Faulted r -> "Faulted"
    | GuestOutcome.TimedOut -> "TimedOut"
    | GuestOutcome.Cancelled r -> "Cancelled(" + r + ")"

let private run guest =
    async {
        let mutable started = 0

        let! outcome =
            InputGuest.run guest (request 7) (fun () -> started <- started + 1) System.Threading.CancellationToken.None

        return outcomeText outcome, started
    }

let private bytesGuest (reply: GuestReply) : InputGuest<string, int, string, string> =
    InputGuest.ofBytes
        (fun r -> [| byte (GuestRequest.client r) |])
        (fun bytes started _ ->
            async {
                started ()
                return reply
            })
        (fun bytes ->
            if bytes = [| 1uy |] then Ok [ GuestIntent.Submit "one" ]
            elif bytes = [| 2uy |] then Ok [ GuestIntent.Local "two" ]
            else Error "unknown bytes")
        GuestPolicy.standard

let private asyncLaws =
    async {
        // P13: no exception escapes `InputGuest.run`.
        let throwing name (guest: InputGuest<string, int, string, string>) =
            async {
                let! text, _ = run guest
                check "P13" (name + " becomes Faulted") (text = "Faulted") text
            }

        do! throwing "an exception in decide" (InputGuest.sync (fun _ -> failwith "decide"))

        do!
            throwing
                "an exception in an async decide"
                (InputGuest.ofAsync (fun _ _ -> async { return failwith "async" }) GuestPolicy.standard)

        do!
            throwing
                "an exception in encode"
                (InputGuest.ofBytes
                    (fun _ -> failwith "encode")
                    (fun _ _ _ -> async { return GuestReply.Output [||] })
                    (fun _ -> Ok [])
                    GuestPolicy.standard)

        do!
            throwing
                "an exception in invoke"
                (InputGuest.ofBytes
                    (fun _ -> [||])
                    (fun _ _ _ -> async { return failwith "invoke" })
                    (fun _ -> Ok [])
                    GuestPolicy.standard)

        do!
            throwing
                "an exception in decode"
                (InputGuest.ofBytes
                    (fun _ -> [||])
                    (fun _ _ _ -> async { return GuestReply.Output [| 1uy |] })
                    (fun _ -> failwith "decode")
                    GuestPolicy.standard)

        do!
            throwing
                "an exception in admit"
                (InputGuest.sync (fun _ -> GuestOutcome.Decided [])
                 |> InputGuest.withAdmission (fun _ -> failwith "admit"))

        let! refused, startedRefused =
            run (
                InputGuest.sync (fun _ -> GuestOutcome.Decided [])
                |> InputGuest.withAdmission (fun c -> if c = 7 then Error "bound" else Ok())
            )

        check
            "P13"
            "an admission Error is Refused before any call"
            (refused = "Refused(bound)" && startedRefused = 0)
            refused

        let! syncText, syncStarted =
            run (InputGuest.sync (fun r -> GuestOutcome.Decided [ GuestIntent.Local(string (GuestRequest.client r)) ]))

        check
            "P7"
            "a sync guest is warm at once and sees the client it was given"
            (syncText = "Decided(L:7)" && syncStarted = 1)
            syncText

        // P20: one golden vector per FS.GG.Wasm.Contracts.TerminalState, through ofBytes and run, answered by an F# stand-in
        // for a guest module. No Rust guest module exists yet: the CI module that must answer the same vectors arrives
        // with the ReferenceGame guest (FS.GG.Game#788 PR D).
        let vectors =
            [
                "Succeeded", GuestReply.Output [| 1uy |], "Decided(S:one)"
                "Succeeded (undecodable)", GuestReply.Output [| 9uy |], "Faulted"
                "GuestRejected", GuestReply.Refused 3, "Refused(status 3)"
                "Faulted", GuestReply.Faulted "trap", "Faulted"
                "TimedOut", GuestReply.TimedOut, "TimedOut"
                "Invalidated", GuestReply.Invalidated, "Cancelled(invalidated)"
                "Frozen", GuestReply.Frozen, "Cancelled(frozen)"
            ]

        for name, reply, expected in vectors do
            let! text, started = run (bytesGuest reply)
            check "P20" ("TerminalState " + name + " maps as the table says") (text = expected && started = 1) text

        // The runner performs one batch in order; Continue is dispatched after the Local messages (P7b, runner side).
        let order = ResizeArray<string>()
        let pipeline = standard ()

        let io =
            PipelineIo.create
                (fun () -> 0)
                (fun l -> order.Add("local:" + l))
                (fun e -> order.Add("send:" + CommandEnvelope.command e))
                (fun f -> order.Add("fail"))
                (fun _ _ -> ())

        let runner =
            PipelineRunner.create pipeline io (fun msg ->
                order.Add(
                    match msg with
                    | PipelineMsg.StartNext -> "StartNext"
                    | _ -> "msg"
                ))

        let model = InputPipeline.init pipeline "a" None (server 0UL [])

        let _, effects =
            steps
                pipeline
                model
                ([
                    scene InputAction.Activate "x"
                    PipelineMsg.GuestRunning 1UL
                    PipelineMsg.GuestEnded(1UL, decided [ local "a"; submit "c"; local "b" ])
                ])

        PipelineRunner.performAll (last effects) runner
        let text = String.concat "," order

        check
            "P7b"
            "the runner dispatches StartNext after performing the batch's Locals"
            (text = "local:a,send:c,local:b,StartNext")
            text

        // An exception from an io callback never escapes the batch: the rest runs, and it is routed as EffectFailed.
        let order = ResizeArray<string>()

        let io =
            PipelineIo.create
                (fun () -> 0)
                (fun l ->
                    if l = "boom" then
                        failwith "applyLocal broke"

                    order.Add("local:" + l))
                (fun e ->
                    if CommandEnvelope.command e = "bad" then
                        failwith "send broke"

                    order.Add("send:" + CommandEnvelope.command e))
                (fun f -> order.Add("fail:" + PipelineFailure.text f))
                (fun _ _ -> failwith "no timers")

        let runner =
            PipelineRunner.create pipeline io (fun msg ->
                order.Add(
                    match msg with
                    | PipelineMsg.StartNext -> "StartNext"
                    | _ -> "msg"
                ))

        let _, effects =
            steps
                pipeline
                model
                ([
                    scene InputAction.Activate "x"
                    PipelineMsg.GuestRunning 1UL
                    PipelineMsg.GuestEnded(1UL, decided [ local "boom"; submit "bad"; local "b" ])
                ])

        PipelineRunner.performAll (last effects @ [ PipelineEffect.ArmDeadline(9UL, 5) ]) runner
        let text = String.concat "," order

        check
            "P13"
            "an exception from an io callback is routed as EffectFailed and the batch goes on"
            (text = "fail:EffectFailed(applyLocal,applyLocal broke),fail:EffectFailed(send,send broke),local:b,StartNext,fail:EffectFailed(schedule,no timers)")
            text

        // client () raising ends the call Faulted; a disposed runner dispatches nothing, even for a timer armed before.
        let dispatched = ResizeArray<string>()
        let timers = ResizeArray<unit -> unit>()

        let io =
            PipelineIo.create
                (fun () -> failwith "no client")
                ignore
                ignore
                ignore
                (fun _ action -> timers.Add action)

        let runner =
            PipelineRunner.create pipeline io (fun msg ->
                dispatched.Add(
                    match msg with
                    | PipelineMsg.GuestEnded(_, GuestOutcome.Faulted reason) -> "faulted:" + reason
                    | PipelineMsg.GuestDeadline _ -> "deadline"
                    | _ -> "other"
                ))

        let _, effects = steps pipeline model [ scene InputAction.Activate "x" ]
        PipelineRunner.performAll (effects.[0] @ [ PipelineEffect.ArmDeadline(1UL, 5) ]) runner
        let before = String.concat "," dispatched
        (runner :> System.IDisposable).Dispose()

        for timer in timers do
            timer ()

        PipelineRunner.perform PipelineEffect.Continue runner

        check
            "P13"
            "client () raising ends the call Faulted"
            (before = "faulted:no client")
            before

        check
            "P5"
            "a disposed runner dispatches nothing, even for timers armed before"
            (String.concat "," dispatched = "faulted:no client")
            (String.concat "," dispatched)

        // InputGuest.run never raises when its token is cancelled: a decision after cancellation, and an exception
        // after cancellation, are both Cancelled.
        let source = new System.Threading.CancellationTokenSource()

        let lateGuest: InputGuest<string, int, string, string> =
            InputGuest.ofAsync
                (fun _ _ ->
                    async {
                        source.Cancel()
                        return GuestOutcome.Decided [ GuestIntent.Local "late" ]
                    })
                GuestPolicy.standard

        let! late = InputGuest.run lateGuest (request 1) ignore source.Token
        let source = new System.Threading.CancellationTokenSource()

        let throwingGuest: InputGuest<string, int, string, string> =
            InputGuest.ofAsync
                (fun _ _ ->
                    async {
                        source.Cancel()
                        return failwith "after cancel"
                    })
                GuestPolicy.standard

        let! thrown = InputGuest.run throwingGuest (request 1) ignore source.Token

        check
            "P13"
            "a cancelled token ends the call Cancelled, never raising"
            (outcomeText late = "Cancelled(cancelled)" && outcomeText thrown = "Cancelled(cancelled)")
            (outcomeText late + " " + outcomeText thrown)
    }

let private report () =
    let lines, failed = results ()

    for line in lines do
        printfn "%s" line

    let mutable digest = 7L

    // The digest covers every printed line AND the rendered effects of every randomized step, so the .NET and Fable
    // runs agree on what the reducer did, not only on the verdicts.
    for line in Seq.append lines transcript do
        for c in line do
            digest <- (digest * 31L + int64 c) % 1000000007L

    printfn "pipeline-laws checks=%d failed=%d digest=%d" lines.Length failed digest
    failed

let private all =
    async {
        p1 ()
        p2 ()
        p3 ()
        p4 ()
        p5 ()
        p6 ()
        p7 ()
        p7b ()
        p8 ()
        p9 ()
        p10 ()
        p10b ()
        p11 ()
        p12 ()
        p14 ()
        p15 ()
        p16 ()
        p17 ()
        p18 ()
        p18b ()
        p19 ()
        p11Window ()
        p18Final ()
        p16Patience ()
        p10bQueue ()
        p4Breaker ()
        p15Equal ()
        envelopeBasis ()
        p7bRebind ()
        p10Rebind ()
        invariants ()
        do! asyncLaws
        return report ()
    }

#if FABLE_COMPILER
Async.StartImmediate(
    async {
        let! failed = all

        if failed > 0 then
            failwithf "%d pipeline law(s) failed" failed
    }
)
#else
[<EntryPoint>]
let main _ =
    if Async.RunSynchronously all > 0 then 1 else 0
#endif

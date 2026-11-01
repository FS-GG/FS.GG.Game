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

        // P20: one golden vector per FS.GG.Wasm.Contracts.TerminalState, through ofBytes and run (an F# stand-in for the
        // module; the Rust module answers the same vectors in CI).
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
    }

let private report () =
    let lines, failed = results ()

    for line in lines do
        printfn "%s" line

    let mutable digest = 7L

    for line in lines do
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

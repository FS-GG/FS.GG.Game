module FS.GG.Game.Render.Browser.Smoke.ReferencePipelineSmoke

// FS.GG.Game#788 PR D: ReferenceGame played end to end through the standard input pipeline on the real SVG, Pixi and
// Babylon backends, with REAL input: a press on the token (the input host) goes to the reference game's F# guest
// (`ReferencePipeline.guest`), its Step command leaves the client, the authority admits it with `NetworkAdmission` and
// applies it, and the authoritative snapshot comes back and is presented by the backend. Each leg of the "network" is
// a separate task (setTimeout 0), so nothing is answered synchronously.
//
// Latency, per backend, over 20 presses: input-to-command is the time the command leaves the client (`io.send`) minus
// the pointerdown event's timeStamp; input-to-snapshot is the time the backend has presented the authoritative snapshot
// that applied it (after `Present` returned) minus the same timeStamp. Both are reported as INFO lines (median, p95 and
// max, nearest rank), so a run on SwiftShader and a run on a GPU can be compared.

open Browser.Dom
open Fable.Core
open FS.GG.Game.Render
open FS.GG.Game.Render.Browser
open FS.GG.Game.Reference
open FS.GG.Game.Reference.ReferencePipeline

[<Emit("performance.now()")>]
let private now () : float = jsNative

[<Emit("(() => { if (!window.__fsggDowns) { const d = []; window.__fsggDowns = d; window.addEventListener('pointerdown', e => d.push(e.timeStamp), true) } })()")>]
let private installDownClock () : unit = jsNative

[<Emit("window.__fsggDowns.splice(0)")>]
let private takeDowns () : float array = jsNative

[<Emit("setTimeout($1, $0)")>]
let private after (_milliseconds: int) (_action: unit -> unit) : unit = jsNative

let private start: ReferenceState.Model =
    {
        Screen = ReferenceState.Playing
        Room = ReferenceState.Atrium
        X = 1
        DestinationActive = false
        CameraTransition = false
        VaultClear = false
        Aim = 200, 40
        Tick = 0
        Selected = None
    }

/// Nearest-rank percentile of `values` (sorted here).
let private percentile (p: float) (values: float list) =
    match List.sort values with
    | [] -> nan
    | sorted ->
        let rank = int (System.Math.Ceiling(p / 100.0 * float sorted.Length)) |> max 1 |> min sorted.Length
        sorted.[rank - 1]

let private summary (values: float list) =
    sprintf
        "p50=%.2f p95=%.2f max=%.2f ms (n=%d)"
        (percentile 50.0 values)
        (percentile 95.0 values)
        (List.max values)
        values.Length

let presses = 20

let run (check: string -> bool -> string -> unit) (info: string -> string -> unit) : Async<unit> =
    async {
        installDownClock ()

        for backend in RendererBackend.all do
            let name = RendererBackend.id backend
            let container = document.createElement "div"
            container.id <- "stage-reference-pipeline-" + name
            document.body.appendChild container |> ignore
            let late = BrowserRenderers.route ()
            let options = BrowserRenderers.options "Reference game" ReferenceRenderers.width ReferenceRenderers.height
            let route = InputRoute.create ()

            let registry =
                match BrowserRenderers.surfaces container options late with
                | (svg :: _) as all ->
                    ReferenceRenderers.declare (SceneSurface.mountScene svg) all
                    |> SceneRenderers.withInput ReferenceRenderers.interaction route
                    |> SceneRenderers.registry
                | [] -> failwith "no surfaces"

            let driver = BrowserRenderers.start late registry start ignore

            if backend <> RendererBackend.Svg then
                driver.Dispatch(RendererMsg.SelectBackend backend)

            let! _ = BrowserConformance.waitUntil 600 (fun () -> driver.Mounted = Some backend)
            let sent = ResizeArray<float * CommandEnvelope<Command>>()
            let shownAt = ResizeArray<float * uint64>()
            let session: Session option ref = ref None
            let mutable shownVersion = 0UL

            let onShown (model: ReferenceState.Model) =
                driver.Dispatch(RendererMsg.StateChanged model)

                match session.Value with
                | Some running when running.Authoritative.Version > shownVersion ->
                    // Presented: the driver's Present for this state has returned.
                    shownVersion <- running.Authoritative.Version
                    shownAt.Add(now (), shownVersion)
                | _ -> ()

            session.Value <-
                Some(new Session("player", start, route, after 0, after, onShown, (fun envelope -> sent.Add(now (), envelope))))

            let running = session.Value.Value
            let downs = ResizeArray<float>()
            takeDowns () |> ignore

            for press in 1..presses do
                let x, y, w, h = ReferenceRenderers.tokenBounds running.Shown
                do! BrowserConformance.press container backend { X = x + w / 2.0; Y = y + h / 2.0 }
                downs.AddRange(takeDowns ())
                let! _ = BrowserConformance.waitUntil 600 (fun () -> shownVersion >= uint64 press)
                ()

            let shown = running.Shown
            let authoritative = running.Authoritative
            let commands = sent |> Seq.map (fun (_, envelope) -> commandText (CommandEnvelope.command envelope)) |> List.ofSeq

            check
                (sprintf "reference pipeline %s: real presses on the token go through the F# guest to %d Step commands, the authority applies each once, and the backend presents the authoritative snapshot" name presses)
                (commands = List.replicate presses "step"
                 && authoritative.Version = uint64 presses
                 && authoritative.Model.X = start.X + presses
                 && driver.Model.State.X = start.X + presses
                 && driver.Model.State.Selected = Some "token"
                 && (InputPipeline.unsettled running.Model).IsEmpty
                 && (Displayed.predicted (InputPipeline.displayed running.Model)).IsEmpty
                 && running.Failures.IsEmpty)
                (sprintf
                    "commands=%A version=%d X=%d shown X=%d selected=%A unsettled=%d failures=%A"
                    commands
                    authoritative.Version
                    authoritative.Model.X
                    driver.Model.State.X
                    driver.Model.State.Selected
                    (InputPipeline.unsettled running.Model).Length
                    (running.Failures |> List.map PipelineFailure.text))

            check
                (sprintf "reference pipeline %s: command ids rise from 1 and each snapshot version is shown once, in order" name)
                ((sent |> Seq.map (fun (_, e) -> CommandId.sequence (CommandEnvelope.id e)) |> List.ofSeq) = [ 1UL .. uint64 presses ]
                 && (shownAt |> Seq.map snd |> List.ofSeq) = [ 1UL .. uint64 presses ]
                 && downs.Count = presses)
                (sprintf "sent=%d shown=%A downs=%d" sent.Count (shownAt |> Seq.map snd |> List.ofSeq) downs.Count)

            if downs.Count = presses && sent.Count = presses && shownAt.Count = presses then
                let toCommand = [ for i in 0 .. presses - 1 -> fst sent.[i] - downs.[i] ]
                let toSnapshot = [ for i in 0 .. presses - 1 -> fst shownAt.[i] - downs.[i] ]
                info (sprintf "latency %s input-to-command" name) (summary toCommand)
                info (sprintf "latency %s input-to-snapshot" name) (summary toSnapshot)

            running.Dispose()
            (driver :> System.IDisposable).Dispose()
            container.remove ()
    }

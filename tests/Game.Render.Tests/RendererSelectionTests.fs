module Game.Render.Tests.RendererSelectionTests

// GAME-RENDER-MULTI-01.4 (FS.GG.Game#730): Elmish renderer selection. A registry keyed by game-state
// type offers a backend only when an adapter exists for it; switching is an Elmish message that keeps
// the game state; the host owns mounted resources and disposes each mount exactly once. SVG over the
// existing Scene `Adapter` path is the default; Pixi and Babylon are declared but have no adapter yet.

open Expecto
open FS.GG.UI.Scene
open FS.GG.Game.Core
open FS.GG.Game.Render

type private Arena = { Tick: int; Player: Cell }

let private arena tick col : Arena =
    {
        Tick = tick
        Player = { Col = col; Row = 1 }
    }

let private fill = Colors.rgb 10uy 20uy 30uy

/// The game's own Scene projection, built from the existing Adapter drawing functions.
let private view (state: Arena) : Scene = Adapter.drawCell 16.0 fill state.Player

/// Records every resource event so lifecycle assertions read real calls, not model fields.
type private Journal() =
    member val Events = ResizeArray<string>()

    member this.Count(event: string) =
        this.Events |> Seq.filter ((=) event) |> Seq.length

let private svgSurface (journal: Journal) (presented: ResizeArray<Scene>) =
    fun (scene: Scene) ->
        journal.Events.Add "svg:mount"
        presented.Add scene

        Ok
            {
                Present =
                    fun next ->
                        journal.Events.Add "svg:present"
                        presented.Add next
                Dispose = fun () -> journal.Events.Add "svg:dispose"
            }

let private fakeAdapter (journal: Journal) (backend: RendererBackend) : RendererAdapter<Arena> =
    let name = RendererBackend.id backend

    {
        Backend = backend
        Mount =
            fun _ ->
                journal.Events.Add(name + ":mount")

                Ok
                    {
                        Present = fun _ -> journal.Events.Add(name + ":present")
                        Dispose = fun () -> journal.Events.Add(name + ":dispose")
                    }
    }

let private svgRegistry (journal: Journal) =
    RendererRegistry.svg view (svgSurface journal (ResizeArray()))

/// A minimal Elmish loop: update, run effects on the host, feed host messages back in.
let rec private drive (host: RendererHost<Arena>) (model: RendererModel<Arena>) (msgs: RendererMsg<Arena> list) =
    match msgs with
    | [] -> model
    | msg :: rest ->
        let next, effects = RendererSelection.update msg model
        let feedback = host.Run effects
        drive host next (feedback @ rest)

let private start (host: RendererHost<Arena>) registry state =
    let model, effects = RendererSelection.init registry state
    drive host model (host.Run effects)

[<Tests>]
let tests =
    testList
        "FS.GG.Game.Render RendererSelection (#730)"
        [
            test "backend tokens are stable and round-trip" {
                Expect.equal
                    (RendererBackend.all |> List.map RendererBackend.id)
                    [ "svg"; "pixi"; "babylon" ]
                    "stable order and tokens"

                for backend in RendererBackend.all do
                    Expect.equal (RendererBackend.tryParse (RendererBackend.id backend)) (Some backend) "round trip"

                Expect.isNone (RendererBackend.tryParse "SVG") "exact match only"
                Expect.equal RendererBackend.defaultBackend RendererBackend.Svg "SVG is the default"
            }

            test "an SVG-only registry offers SVG and declares Pixi/Babylon unavailable" {
                let registry = svgRegistry (Journal())
                Expect.equal (RendererRegistry.available registry) [ RendererBackend.Svg ] "only SVG"
                Expect.isFalse (RendererRegistry.isAvailable RendererBackend.Pixi registry) "no Pixi adapter yet"
                Expect.isFalse (RendererRegistry.isAvailable RendererBackend.Babylon registry) "no Babylon adapter yet"
                Expect.isNone (RendererRegistry.tryFind RendererBackend.Pixi registry) "no Pixi adapter yet"
            }

            test "registering an adapter makes exactly that backend available, in stable order" {
                let journal = Journal()

                let registry =
                    svgRegistry journal
                    |> RendererRegistry.register (fakeAdapter journal RendererBackend.Babylon)
                    |> RendererRegistry.register (fakeAdapter journal RendererBackend.Pixi)

                Expect.equal
                    (RendererRegistry.available registry)
                    RendererBackend.all
                    "declared order, not insertion order"

                let replaced =
                    registry |> RendererRegistry.register (fakeAdapter journal RendererBackend.Pixi)

                Expect.equal (RendererRegistry.available replaced) RendererBackend.all "re-registering replaces"
            }

            test "init selects SVG and mounts it with the initial state" {
                let model, effects = RendererSelection.init (svgRegistry (Journal())) (arena 0 2)
                Expect.equal model.Selected RendererBackend.Svg "default backend"
                Expect.equal model.State (arena 0 2) "state kept"
                Expect.isNone model.Refusal "nothing refused"
                Expect.equal effects [ RendererEffect.Mount(RendererBackend.Svg, arena 0 2) ] "mount SVG"
                Expect.equal (RendererSelection.available model) [ RendererBackend.Svg ] "selector offers SVG"
            }

            test "switching backends preserves game state and swaps resources" {
                let journal = Journal()

                let registry =
                    svgRegistry journal
                    |> RendererRegistry.register (fakeAdapter journal RendererBackend.Pixi)

                let model, _ = RendererSelection.init registry (arena 0 2)
                let model, _ = RendererSelection.update (RendererMsg.StateChanged(arena 7 4)) model

                let switched, effects =
                    RendererSelection.update (RendererMsg.SelectBackend RendererBackend.Pixi) model

                Expect.equal switched.Selected RendererBackend.Pixi "switched"
                Expect.equal switched.State (arena 7 4) "game state survives the switch"

                Expect.equal
                    effects
                    [
                        RendererEffect.Unmount RendererBackend.Svg
                        RendererEffect.Mount(RendererBackend.Pixi, arena 7 4)
                    ]
                    "unmount previous, mount next with the current state"

                let back, _ =
                    RendererSelection.update (RendererMsg.SelectBackend RendererBackend.Svg) switched

                Expect.equal back.State (arena 7 4) "state survives switching back"
            }

            test "selecting an unavailable backend is refused without changing selection or state" {
                let model, _ = RendererSelection.init (svgRegistry (Journal())) (arena 3 1)

                for backend in [ RendererBackend.Pixi; RendererBackend.Babylon ] do
                    let refused, effects =
                        RendererSelection.update (RendererMsg.SelectBackend backend) model

                    Expect.equal refused.Selected RendererBackend.Svg "selection kept"
                    Expect.equal refused.State (arena 3 1) "state kept"
                    Expect.equal refused.Refusal (Some(RendererRefusal.Unavailable backend)) "refusal recorded"
                    Expect.isEmpty effects "no resource effects"
            }

            test "re-selecting the current backend is a no-op" {
                let model, _ = RendererSelection.init (svgRegistry (Journal())) (arena 0 0)

                let same, effects =
                    RendererSelection.update (RendererMsg.SelectBackend RendererBackend.Svg) model

                Expect.equal same.Selected model.Selected "unchanged"
                Expect.isEmpty effects "no remount"
            }

            test "host disposes each mount exactly once across switches and host disposal" {
                let journal = Journal()

                let registry =
                    svgRegistry journal
                    |> RendererRegistry.register (fakeAdapter journal RendererBackend.Pixi)

                let host = RendererHost.create registry
                let model = start host registry (arena 0 0)
                Expect.equal host.Mounted (Some RendererBackend.Svg) "SVG mounted"

                let model =
                    drive
                        host
                        model
                        [
                            RendererMsg.SelectBackend RendererBackend.Pixi
                            RendererMsg.StateChanged(arena 1 1)
                            RendererMsg.SelectBackend RendererBackend.Svg
                        ]

                Expect.equal model.State (arena 1 1) "state preserved"
                Expect.equal host.Mounted (Some RendererBackend.Svg) "back on SVG"
                Expect.equal (journal.Count "pixi:mount") 1 "Pixi mounted once"
                Expect.equal (journal.Count "pixi:dispose") 1 "Pixi disposed once"
                Expect.equal (journal.Count "pixi:present") 1 "Pixi presented the change"
                Expect.equal (journal.Count "svg:mount") 2 "SVG mounted twice"
                Expect.equal (journal.Count "svg:dispose") 1 "first SVG mount disposed once"

                (host :> System.IDisposable).Dispose()
                (host :> System.IDisposable).Dispose()
                Expect.equal (journal.Count "svg:dispose") 2 "second SVG mount disposed once, idempotently"
                Expect.isNone host.Mounted "nothing owned after disposal"

                let after = host.Run [ RendererEffect.Mount(RendererBackend.Svg, arena 2 2) ]
                Expect.isEmpty after "disposed host is inert"
                Expect.equal (journal.Count "svg:mount") 2 "no mount after disposal"
            }

            test "a Mount while another backend is mounted disposes the previous one first" {
                let journal = Journal()

                let registry =
                    svgRegistry journal
                    |> RendererRegistry.register (fakeAdapter journal RendererBackend.Pixi)

                use host = RendererHost.create registry
                host.Run [ RendererEffect.Mount(RendererBackend.Svg, arena 0 0) ] |> ignore
                host.Run [ RendererEffect.Mount(RendererBackend.Pixi, arena 0 0) ] |> ignore

                Expect.equal
                    (List.ofSeq journal.Events)
                    [ "svg:mount"; "svg:dispose"; "pixi:mount" ]
                    "no two backends own resources at once"
            }

            test "a failed mount falls back to SVG with the game state intact" {
                let journal = Journal()

                let failing: RendererAdapter<Arena> =
                    {
                        Backend = RendererBackend.Babylon
                        Mount = fun _ -> Error "no WebGL context"
                    }

                let registry = svgRegistry journal |> RendererRegistry.register failing
                use host = RendererHost.create registry
                let model = start host registry (arena 0 0)

                let model =
                    drive
                        host
                        model
                        [
                            RendererMsg.StateChanged(arena 5 3)
                            RendererMsg.SelectBackend RendererBackend.Babylon
                        ]

                Expect.equal model.Selected RendererBackend.Svg "fell back to the default"
                Expect.equal model.State (arena 5 3) "state kept"

                Expect.equal
                    model.Refusal
                    (Some(RendererRefusal.MountFailed(RendererBackend.Babylon, "no WebGL context")))
                    "failure recorded"

                Expect.equal host.Mounted (Some RendererBackend.Svg) "SVG remounted"
                Expect.equal (journal.Count "svg:mount") 2 "SVG remounted once"
                Expect.equal (journal.Count "svg:dispose") 1 "original SVG mount disposed once"
            }

            test "a throwing adapter is reported as a mount failure" {
                let throwing: RendererAdapter<Arena> =
                    {
                        Backend = RendererBackend.Pixi
                        Mount = fun _ -> failwith "boom"
                    }

                let registry = svgRegistry (Journal()) |> RendererRegistry.register throwing
                use host = RendererHost.create registry
                let feedback = host.Run [ RendererEffect.Mount(RendererBackend.Pixi, arena 0 0) ]
                Expect.equal feedback [ RendererMsg.MountFailed(RendererBackend.Pixi, "boom") ] "exception captured"
                Expect.isNone host.Mounted "nothing mounted"
            }

            test "a mount of an unregistered backend is reported, never silently skipped" {
                use host = RendererHost.create (svgRegistry (Journal()))

                match host.Run [ RendererEffect.Mount(RendererBackend.Pixi, arena 0 0) ] with
                | [ RendererMsg.MountFailed(RendererBackend.Pixi, _) ] -> ()
                | other -> failtestf "expected a Pixi mount failure, got %A" other
            }

            test "a default mount failure is recorded and re-selecting retries it" {
                let model, _ = RendererSelection.init (svgRegistry (Journal())) (arena 0 0)

                let failed, effects =
                    RendererSelection.update (RendererMsg.MountFailed(RendererBackend.Svg, "detached container")) model

                Expect.equal failed.Selected RendererBackend.Svg "nothing to fall back to"
                Expect.isEmpty effects "no fallback loop"

                let retried, effects =
                    RendererSelection.update (RendererMsg.SelectBackend RendererBackend.Svg) failed

                Expect.isNone retried.Refusal "retry clears the refusal"
                Expect.equal effects [ RendererEffect.Mount(RendererBackend.Svg, arena 0 0) ] "retry mounts"
            }

            test "a mount failure for a superseded backend is ignored" {
                let model, _ = RendererSelection.init (svgRegistry (Journal())) (arena 0 0)

                let same, effects =
                    RendererSelection.update (RendererMsg.MountFailed(RendererBackend.Pixi, "late")) model

                Expect.equal same.Selected RendererBackend.Svg "selection kept"
                Expect.isNone same.Refusal "nothing recorded"
                Expect.isEmpty effects "no effects"
            }

            test "the SVG backend presents exactly the existing Adapter Scene for every state" {
                let presented = ResizeArray<Scene>()
                let registry = RendererRegistry.svg view (svgSurface (Journal()) presented)
                use host = RendererHost.create registry
                let states = [ arena 0 0; arena 1 2; arena 2 5 ]
                let model = start host registry states.Head

                drive host model (states.Tail |> List.map RendererMsg.StateChanged) |> ignore

                Expect.equal
                    (List.ofSeq presented)
                    (states |> List.map (fun s -> Adapter.drawCell 16.0 fill s.Player))
                    "SVG output is the unchanged Scene projection"
            }
        ]

module FS.GG.Game.Render.Browser.Smoke.RetainedSurface

// GAME-RENDER-ADOPT-01.2 (FS.GG.Game#750): a game's own retained SVG surface registered as the default adapter,
// with no Scene projection and no refusing placeholder. The state type here is a small board with selectable cells,
// accessible labels, a camera and an input host; the checks run in the real browser through the same
// `RendererDriver` and conformance kit as the Scene path.

open Browser.Dom
open Browser.Types
open Fable.Core
open FS.GG.Game.Render
open FS.GG.Game.Render.Browser
open FS.GG.UI.Scene

type Board =
    {
        /// Shades the cells, so every stamp looks different.
        Stamp: int
        /// The cell the game says is selected.
        Chosen: string option
    }

let cellIds = [ "a"; "b"; "c" ]

/// The fill of the cell `id` at `stamp`: distinct per stamp, so the output probe can tell states apart.
let fillOf (stamp: int) : Color =
    {
        Red = byte (40 + 30 * (stamp % 6))
        Green = 90uy
        Blue = 160uy
        Alpha = 255uy
    }

let size (_: Board) = 320.0, 180.0

let scene (board: Board) : RetainedScene =
    let cell index (id: string) : SemanticSceneObject =
        {
            Id = id
            Selectable = true
            AccessibleLabel = "Cell " + id
            Content =
                {
                    Nodes = [ SceneNode.Rectangle((20.0 + 60.0 * float index, 40.0, 50.0, 50.0), fillOf board.Stamp) ]
                }
        }

    {
        RootId = "board"
        Revision = 1000 - board.Stamp
        Camera = { PanX = 0.0; PanY = 0.0; Zoom = 1.0 }
        Layers =
            [
                {
                    Id = "cells"
                    Visible = true
                    Objects =
                        {
                            Id = "wall"
                            Selectable = false
                            AccessibleLabel = "Wall"
                            Content =
                                {
                                    Nodes =
                                        [
                                            SceneNode.Rectangle(
                                                (0.0, 0.0, 320.0, 20.0),
                                                { Red = 5uy; Green = 5uy; Blue = 5uy; Alpha = 255uy }
                                            )
                                        ]
                                }
                        }
                        :: (cellIds |> List.mapi cell)
                }
            ]
    }

let private has (needle: string) (html: string) = html.Contains needle

let private count (container: HTMLElement) (selector: string) = int (container.querySelectorAll selector).length

let svgFill (color: Color) =
    sprintf "fill=\"rgb(%d %d %d)\"" color.Red color.Green color.Blue

[<Emit("$0.dispatchEvent(new WheelEvent('wheel', { deltaY: -100, clientX: 10, clientY: 10, bubbles: true, cancelable: true }))")>]
let private wheel (root: Element) : bool = jsNative

[<Emit("$0.click()")>]
let private click (element: Element) : unit = jsNative

let private query (container: HTMLElement) (selector: string) = container.querySelector selector

let private attribute (container: HTMLElement) (selector: string) (name: string) =
    match query container selector with
    | null -> ""
    | element -> element.getAttribute name

let private selectedIds (container: HTMLElement) =
    let found = container.querySelectorAll "[data-scene-object-id][data-selected='true']"
    [ for index in 0 .. int found.length - 1 -> found.[index].getAttribute "data-scene-object-id" ]

let run (check: string -> bool -> string -> unit) : Async<unit> =
    async {
        let container = document.createElement "div"
        container.id <- "stage-retained"
        document.body.appendChild container |> ignore

        let attached = ref 0
        let released = ref 0
        let chosenByUser = ResizeArray<string option>()
        let failInput = ref false

        let attach (target: HTMLElement) =
            if failInput.Value then
                Error "the input profile was refused"
            else
                attached.Value <- attached.Value + 1
                target.setAttribute ("data-input", "attached")

                Ok
                    { new System.IDisposable with
                        member _.Dispose() =
                            released.Value <- released.Value + 1
                            target.removeAttribute "data-input"
                    }

        let surface =
            RetainedSvgSurface.create container "Retained board" size scene
            |> RetainedSvgSurface.withSelection (fun board -> board.Chosen) chosenByUser.Add
            |> RetainedSvgSurface.withInput attach

        let registry = RetainedSvgSurface.registry surface
        let route = BrowserRenderers.route ()
        let first = { Stamp = 1; Chosen = Some "b" }
        let driver = BrowserRenderers.start route registry first ignore

        // Registered without a Scene projection or a placeholder.
        check
            "retained svg: the ready adapter is the default and the only backend offered"
            (RendererRegistry.available registry = [ RendererBackend.Svg ]
             && driver.Mounted = Some RendererBackend.Svg
             && driver.Model.Status = RendererStatus.Active)
            (sprintf "%A %A" (RendererRegistry.available registry) driver.Mounted)

        check
            "retained svg: selectable objects and a non-selectable one keep their identities"
            (count container "[data-scene-object-id]" = 4
             && count container "[data-scene-object-id='wall']" = 1
             && count container "[data-scene-control-id]" = 3)
            container.innerHTML

        check
            "retained svg: accessible labels are kept"
            (attribute container "[data-scene-object-id='a']" "aria-label" = "Cell a"
             && System.String.IsNullOrEmpty(attribute container "[data-scene-object-id='wall']" "aria-label"))
            container.innerHTML

        check "retained svg: the game's input host is attached once" (attached.Value = 1 && released.Value = 0) (sprintf "%d/%d" attached.Value released.Value)

        check
            "retained svg: the state's selection is shown without reporting it as the user's"
            (selectedIds container = [ "b" ] && chosenByUser.Count = 0)
            (sprintf "%A %d" (selectedIds container) chosenByUser.Count)

        // Camera: the user's zoom survives a newer state.
        let root = query container "svg"
        wheel root |> ignore
        let zoomed = attribute container "[data-scene-viewport]" "transform"

        check "retained svg: a wheel event zooms the camera" (zoomed <> "" && not (zoomed.EndsWith "scale(1)")) zoomed

        driver.Dispatch(RendererMsg.StateChanged { Stamp = 2; Chosen = Some "c" })

        check
            "retained svg: the user's camera survives a presented state"
            (attribute container "[data-scene-viewport]" "transform" = zoomed)
            (attribute container "[data-scene-viewport]" "transform")

        check
            "retained svg: each presented scene is the next revision, whatever the game's own"
            (attribute container "svg" "data-scene-revision" = "2")
            (attribute container "svg" "data-scene-revision")

        check
            "retained svg: the new state's selection replaces the old, and the content follows the state"
            (selectedIds container = [ "c" ]
             && has (svgFill (fillOf 2)) container.innerHTML
             && not (has (svgFill (fillOf 1)) container.innerHTML))
            (sprintf "%A" (selectedIds container))

        check "retained svg: a presented state does not remount or reattach input" (attached.Value = 1 && driver.Mounted = Some RendererBackend.Svg) (string attached.Value)

        // The user selects through the accessible control for cell a.
        click (query container "[data-scene-control-id='a']")

        check
            "retained svg: a selection the user makes is reported once, as the user's"
            (chosenByUser.Count = 1 && chosenByUser.[0] = Some "a" && selectedIds container = [ "a" ])
            (sprintf "%A %A" (List.ofSeq chosenByUser) (selectedIds container))

        // The game's next state wins again, silently.
        driver.Dispatch(RendererMsg.StateChanged { Stamp = 3; Chosen = None })

        check
            "retained svg: a state without a selection clears the surface silently"
            (selectedIds container = [] && chosenByUser.Count = 1)
            (sprintf "%A %d" (selectedIds container) chosenByUser.Count)

        driver.Dispatch(RendererMsg.StateChanged { Stamp = 4; Chosen = Some "no-such-cell" })

        check
            "retained svg: a state naming an unknown object is presented and leaves the surface unselected"
            (selectedIds container = [] && driver.Mounted = Some RendererBackend.Svg && driver.Model.Status = RendererStatus.Active)
            (sprintf "%A" (selectedIds container))

        // A switch the registry refuses keeps the surface.
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)

        check
            "retained svg: an undeclared backend is refused and the surface stays"
            (driver.Model.Refusal = Some(RendererRefusal.Unavailable RendererBackend.Pixi)
             && driver.Mounted = Some RendererBackend.Svg
             && count container "[data-scene-object-id]" = 4)
            (sprintf "%A" driver.Model.Refusal)

        (driver :> System.IDisposable).Dispose()

        check
            "retained svg: disposing releases the input host and the SVG surface"
            (released.Value = attached.Value
             && container.children.length = 0
             && not (container.hasAttribute "data-input"))
            (sprintf "%d/%d %s" released.Value attached.Value container.innerHTML)

        // An input host that refuses fails the mount without leaving the SVG behind.
        failInput.Value <- true
        let refused = BrowserRenderers.start route registry first ignore

        check
            "retained svg: a refused input host fails the mount and leaves nothing mounted"
            (refused.Mounted = None
             && (match refused.Model.Status with
                 | RendererStatus.Failed [ RendererBackend.Svg, reason ] -> reason = "the input profile was refused"
                 | _ -> false)
             && container.children.length = 0)
            (sprintf "%A %s" refused.Model.Status container.innerHTML)

        (refused :> System.IDisposable).Dispose()
        failInput.Value <- false

        // The conformance kit holds the registry like any other.
        let states =
            [
                first
                { Stamp = 2; Chosen = Some "a" }
                { Stamp = 3; Chosen = None }
                { Stamp = 4; Chosen = Some "c" }
            ]

        let shows (_: ConformanceHost<Board>) (_: RendererBackend) (board: Board) =
            async {
                let html = container.innerHTML

                return
                    if has (svgFill (fillOf board.Stamp)) html && selectedIds container = Option.toList board.Chosen then
                        Ok()
                    else
                        Error(sprintf "the surface does not show stamp %d with %A selected: %A" board.Stamp board.Chosen (selectedIds container))
            }

        let subject =
            BrowserConformance.subject container route registry [ RendererBackend.Svg ] states shows

        let! results = RendererConformance.run subject

        for result in results do
            check ("retained kit: " + result.Check) result.Passed result.Detail

        check
            "retained kit: the SVG-only subject ran all of its checks"
            (results.Length = 13)
            (string results.Length)

        check "retained kit: the stage is empty afterwards" (container.children.length = 0) container.innerHTML
        check "retained kit: every input host the kit mounted was released" (released.Value = attached.Value) (sprintf "%d/%d" released.Value attached.Value)
    }

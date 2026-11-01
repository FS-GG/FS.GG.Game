namespace FS.GG.Game.Render.Browser

open Browser.Dom
open Browser.Types
open Fable.Core
open FS.GG.Game.Render
open FS.GG.UI.KeyboardInput
open FS.GG.UI.Scene

module internal CanvasInput =

    [<Emit("$0.isComposing === true")>]
    let private isComposing (_event: KeyboardEvent) : bool = jsNative

    [<Emit("!!($0 && ($0.closest?.('input,textarea,select,[contenteditable=true],[contenteditable=plaintext-only]')))")>]
    let private isNativeEditableTarget (_target: EventTarget) : bool = jsNative

    [<Emit("$0.closest('[data-scene-control-id]')?.getAttribute('data-scene-control-id') ?? null")>]
    let private controlIdOf (_target: EventTarget) : string option = jsNative

    let tagMirror (mirror: Element) (backend: RendererBackend) =
        mirror.setAttribute ("data-renderer", RendererBackend.id backend)

    [<Sealed>]
    type Host
        internal
        (
            container: HTMLElement,
            canvas: HTMLCanvasElement,
            backend: RendererBackend,
            label: string,
            width: float,
            height: float,
            hitTest: Point -> string option,
            report: SelectionRequest -> unit,
            first: InteractiveScene
        ) =
        let listeners = ResizeArray<EventTarget * string * (Event -> unit)>()
        let controls = document.createElement "div"
        let status = document.createElement "div"
        let buttons = System.Collections.Generic.Dictionary<string, HTMLElement>()
        let mutable current = first
        let mutable focused: string option = None
        let mutable disposed = false

        let listen (target: EventTarget) (name: string) (handler: Event -> unit) =
            target.addEventListener (name, handler)
            listeners.Add(target, name, handler)

        // Writes only what changed (FS.GG.Game#751 review B1): rewriting a button's text or the live status on every
        // Present makes a screen reader announce each frame, and re-inserting a button drops its keyboard focus.
        let setAttribute (element: Element) (name: string) (value: string) =
            if element.getAttribute name <> value then
                element.setAttribute (name, value)

        let setText (element: Element) (value: string) =
            if element.textContent <> value then
                element.textContent <- value

        let sync () =
            let wanted = current.Objects |> List.map (fun target -> target.Id) |> Set.ofList

            for id in buttons.Keys |> Seq.toArray do
                if not (wanted.Contains id) then
                    buttons.[id].remove()
                    buttons.Remove id |> ignore

            // The status is the group's first child; button i belongs at child i + 1. A button is moved only when it is
            // not already there, so an in-order button (and the focus it holds) is never touched.
            current.Objects
            |> List.iteri (fun index target ->
                let button =
                    match buttons.TryGetValue target.Id with
                    | true, existing -> existing
                    | _ ->
                        let created = document.createElement "button"
                        created.setAttribute ("type", "button")
                        created.setAttribute ("data-scene-control-id", target.Id)
                        buttons.[target.Id] <- created
                        created

                setText button target.Label

                setAttribute
                    button
                    "aria-pressed"
                    (if current.Selected = Some target.Id then
                         "true"
                     else
                         "false")

                setAttribute button "data-focused" (if focused = Some target.Id then "true" else "false")

                let slot = controls.children.[index + 1]

                if not (obj.ReferenceEquals(slot, button)) then
                    controls.insertBefore (button, slot) |> ignore)

            current.Selected
            |> Option.bind (fun id -> current.Objects |> List.tryFind (fun target -> target.Id = id))
            |> Option.map (fun target -> target.Label)
            |> Option.defaultValue "No object selected"
            |> setText status

        let ask (request: SelectionRequest) =
            let change =
                match request with
                | SelectionRequest.Select id -> current.Selected <> Some id
                | SelectionRequest.Clear -> current.Selected.IsSome

            if change then
                report request

        let localPoint (pointer: MouseEvent) : Point =
            let bounds = canvas.getBoundingClientRect ()

            {
                X = (pointer.clientX - bounds.left) * width / max 1.0 bounds.width
                Y = (pointer.clientY - bounds.top) * height / max 1.0 bounds.height
            }

        do
            canvas.setAttribute ("role", "application")
            canvas.setAttribute ("tabindex", "0")
            canvas.setAttribute ("aria-label", label)
            controls.setAttribute ("data-scene-controls", RendererBackend.id backend)
            controls.setAttribute ("role", "group")
            controls.setAttribute ("aria-label", label + " object selection")
            tagMirror controls backend
            status.setAttribute ("role", "status")
            status.setAttribute ("aria-live", "polite")
            status.setAttribute ("data-scene-selection-status", "true")
            controls.appendChild status |> ignore
            container.appendChild controls |> ignore

            listen canvas "pointerdown" (fun event ->
                let pointer = event :?> MouseEvent

                if not disposed && pointer.button = 0.0 then
                    match hitTest (localPoint pointer) with
                    | Some id ->
                        focused <- Some id
                        sync ()
                        ask (SelectionRequest.Select id)
                    | None -> ())

            listen canvas "keydown" (fun event ->
                let keyboard = event :?> KeyboardEvent

                let key =
                    match
                        ViewerKeyboard.tryMapSvgIntent
                            keyboard.key
                            true
                            (isComposing keyboard)
                            (isNativeEditableTarget event.target)
                    with
                    | Some SvgKeyboardIntent.FocusNext -> Some InteractionKey.FocusNext
                    | Some SvgKeyboardIntent.FocusPrevious -> Some InteractionKey.FocusPrevious
                    | Some SvgKeyboardIntent.ActivateFocused -> Some InteractionKey.Activate
                    | Some SvgKeyboardIntent.ClearSelection -> Some InteractionKey.Clear
                    | None -> None

                match key with
                | Some key when not disposed ->
                    let next, request = SceneInteraction.key key focused current
                    focused <- next
                    sync ()
                    request |> Option.iter ask
                    event.preventDefault ()
                | _ -> ())

            listen controls "click" (fun event ->
                match controlIdOf event.target with
                | Some id when not disposed && buttons.ContainsKey id -> ask (SelectionRequest.Select id)
                | _ -> ())

            sync ()

        member _.Update(frame: InteractiveScene) =
            if not disposed then
                current <- frame

                focused <-
                    focused
                    |> Option.filter (fun id -> frame.Objects |> List.exists (fun target -> target.Id = id))

                sync ()

        member _.Dispose() =
            if not disposed then
                disposed <- true

                for target, name, handler in listeners do
                    target.removeEventListener (name, handler)

                listeners.Clear()

                if not (isNull controls.parentNode) then
                    controls.parentNode.removeChild controls |> ignore

    let attach container canvas backend label width height hitTest report frame =
        Host(container, canvas, backend, label, width, height, hitTest, report, frame)

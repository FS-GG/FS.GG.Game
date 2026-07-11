// The HOST's input vocabulary — the part of it the TestSpec blocks compile against.
//
// The TestSpecs write `Keys: Set<Key>` and `KeyDown of Key` without ever declaring `Key`, because in
// a real product it comes from the UI/host layer, not from the simulation core. `FS.GG.Game.Core` is
// deliberately BCL-only (see Primitives.fsi: "the sim core reaches up to nothing"), so it does not
// ship `Key`, and this repo has no copy to compile against. This file reconstructs it.
//
// This is the SECOND reconstruction in the harness, and it is the same kind of thing as
// skill-block-context/_scaffold.fs: an unenforced cross-repo contract. If the host renames a case or
// reshapes `Key`, this file keeps compiling and the gate keeps passing over TestSpecs that now teach
// a vocabulary the host no longer ships. The fix is the same as the scaffold's — for the owning repo
// to publish the type so this can reference it instead of re-declaring it — and it is tracked with
// the scaffold's own drift, not separately.
//
// DELIBERATELY MINIMAL. The TestSpec blocks use `Key` and `Button` as OPAQUE types: they appear in
// `Set<Key>`, in DU payloads (`KeyDown of Key`), and nowhere else — no block matches on a specific
// case, and the prose that names actual keys ("W/S", "Esc") does so outside the fenced code. So the
// cases below exist to make the type inhabited and to look like what a host would ship, not to be a
// complete keyboard. A block that reaches for a case this does not have fails loudly, which is the
// correct outcome: it is the moment to reconcile with the real host type rather than to widen a
// fabrication until the doc compiles against it.

namespace FsGg.DocCheck

module Host =

    /// The host's keyboard key. Opaque to the TestSpecs — see the header.
    type Key =
        | KeyLeft | KeyRight | KeyUp | KeyDown
        | KeyW | KeyA | KeyS | KeyD
        | KeySpace | KeyEnter | KeyEscape | KeyTab | KeyShift | KeyCtrl
        | KeyP | KeyQ | KeyE | KeyR | KeyF | KeyZ | KeyX | KeyC
        | KeyDigit of int

    /// The host's mouse button. Opaque to the TestSpecs — see the header.
    type Button =
        | LeftButton
        | RightButton
        | MiddleButton

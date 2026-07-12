// Typecheck fixtures for the pong TestSpec (see scripts/typecheck-md-blocks.fsx).

//#block 2 "let paddleRect (p: Paddle) : Rect ="
// The `Vec2 -> Scene.Rect` crossing — the one block in this corpus that exercises it. It was SKIPPED
// until #165, because `_scaffold.fs` did not reconstruct `Geometry.toRect`; it does now, against the
// REAL `FS.GG.UI.Scene.Rect`. This is the gate's former blind spot, and it is now compiled.
//
// The `open` is the whole fixture, and it is load-bearing. The block annotates `paddleRect` as
// `: Rect` UNQUALIFIED, and `FS.GG.Game.Core.Rect` (ambient, from the sim) and `FS.GG.UI.Scene.Rect`
// are structurally identical — so without this, `Rect` binds to the SIM rect and the block fails
// against a `toRect` that correctly returns the SCENE one. A fixture's text is emitted AFTER the
// corpus's ambient opens, so this one shadows them, which reproduces the reader's render layer:
// there, the file that builds a scene has opened `FS.GG.UI.Scene`, and `Rect` means Scene's.
//
// Note what is NOT done here: the sim `Rect` is not swapped out corpus-wide, and `toRect` is not
// re-pointed at the sim type. Either would make the #129/#132/#140 crossing typecheck by fiat, which
// is the defect this gate exists to catch.
open FS.GG.UI.Scene

//#block 5 "| MenuUp | MenuDown              // move cursor (wraps)"
// A DU-CASE CONTINUATION. The prose above this block says "add these cases to your Msg"; the block
// is written as bare `| Case` lines with no `type ... =` header, so it cannot stand alone. The
// fixture supplies the header the prose left implicit, and the block's cases are then compiled
// verbatim below it — which is the point: the case SHAPES (`MenuAdjust of dir:int`) are what a
// reader copies, and they are what this checks.
type MenuMsg =

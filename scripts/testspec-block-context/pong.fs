// Typecheck fixtures for the pong TestSpec (see scripts/typecheck-md-blocks.fsx).

//#block 2
//#skip reaches for `Geometry.toRect` — the Vec2 -> Scene.Rect crossing that skill-block-context/_scaffold.fs DELIBERATELY does not reconstruct (see its header: a `toRect` returning the SIM `Rect` would be a lie of exactly the shape this gate exists to catch). The block is not wrong; it is unreachable until the scaffold's scene edge is something this repo can compile against. It is the ONLY block in the corpus that exercises that crossing, so this skip is the gate's one blind spot — printed on every run, deliberately.

//#block 5
// A DU-CASE CONTINUATION. The prose above this block says "add these cases to your Msg"; the block
// is written as bare `| Case` lines with no `type ... =` header, so it cannot stand alone. The
// fixture supplies the header the prose left implicit, and the block's cases are then compiled
// verbatim below it — which is the point: the case SHAPES (`MenuAdjust of dir:int`) are what a
// reader copies, and they are what this checks.
type MenuMsg =

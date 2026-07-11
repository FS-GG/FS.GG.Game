// Typecheck fixtures for the breakout TestSpec (see scripts/typecheck-md-blocks.fsx).

//#block 4
// A DU-CASE CONTINUATION. The prose above this block says "add these cases to your Msg"; the block
// is written as bare `| Case` lines with no `type ... =` header, so it cannot stand alone. The
// fixture supplies the header the prose left implicit, and the block's cases are then compiled
// verbatim below it — which is the point: the case SHAPES (`MenuAdjust of dir:int`) are what a
// reader copies, and they are what this checks.
type MenuMsg =

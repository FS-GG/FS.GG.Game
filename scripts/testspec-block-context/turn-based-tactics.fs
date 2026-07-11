// Typecheck fixtures for the turn-based-tactics TestSpec (see scripts/typecheck-md-blocks.fsx).

//#block 1
//#skip pseudo-F#, not F#: `let tile, cost = pq.Dequeue()` destructures a PriorityQueue element as a tuple (Dequeue returns the ELEMENT, not (element, priority)), `Dictionary [ start, 0 ]` builds a Dictionary from a tuple list, and the final `Map.ofSeq` consumes KeyValuePairs. It communicates the lazy-deletion Dijkstra shape, which is what §4 is for, but it is not code a reader can copy. Making it real F# is a change to the ALGORITHM the spec states and belongs in its own item, not smuggled into a gate — filed as FS-GG/FS.GG.Game#158.

//#block 3
// The spec marks this "cosmetic; not authoritative" and never declares it — the animation state is
// the reader's, and the rules must not read it.
type AnimState = { Elapsed: float; Playing: string option }

//#block 5
// A DU-CASE CONTINUATION. The prose above this block says "add these cases to your Msg"; the block
// is written as bare `| Case` lines with no `type ... =` header, so it cannot stand alone. The
// fixture supplies the header the prose left implicit, and the block's cases are then compiled
// verbatim below it — which is the point: the case SHAPES (`MenuAdjust of dir:int`) are what a
// reader copies, and they are what this checks.
type MenuMsg =

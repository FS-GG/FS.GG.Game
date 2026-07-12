// Typecheck fixtures for fs-gg-grids (see scripts/typecheck-md-blocks.fsx).
//
// The skill's four blocks read as one running example — block 2 uses block 1's `c`, block 4 uses
// block 3's `corners`. Each block compiles as its own module (a reader can land on any section),
// so the carried-over names are re-bound here, to the SAME values the earlier block gives them.

//#block 2 "let wall : Grids.Edge = { Col = 4; Row = 2; Orientation = Grids.Vertical }"
let c : Cell = { Col = 3; Row = 2 }

//#block 3 "let touching = Grids.edgeCells wall          // [ {Col=3;Row=2}; {Col=4;Row=2} ] — left then right, c is one"
let c : Cell = { Col = 3; Row = 2 }
let wall : Grids.Edge = { Col = 4; Row = 2; Orientation = Grids.Vertical }
let corners : Grids.Vertex list = Grids.cellCorners c

//#block 4 "let spec : Grids.GridSpec = { CellSize = 32.0; Origin = { X = 0.0; Y = 0.0 } }"
let c : Cell = { Col = 3; Row = 2 }
let wall : Grids.Edge = { Col = 4; Row = 2; Orientation = Grids.Vertical }
let corners : Grids.Vertex list = Grids.cellCorners c

// Typecheck fixtures for fs-gg-line-drawing (see scripts/typecheck-md-blocks.fsx).
//
// Blocks 2-4 continue block 1's running example (`a`, `b`), and each compiles as its own module —
// so `a`/`b` are re-bound here to the values block 1 gives them.

//#block 2 "let mode = Supercover           // LineMode is a top-level type; no qualifier needed"
let a : Cell = { Col = 0; Row = 0 }
let b : Cell = { Col = 5; Row = 2 }

//#block 3 "let wall = { Col = 3; Row = 1 }"
let a : Cell = { Col = 0; Row = 0 }
let b : Cell = { Col = 5; Row = 2 }

//#block 4 "Los.lineOfSightBy Thin isTransparent a b = Los.lineOfSightBy Thin isTransparent b a   // always true"
let a : Cell = { Col = 0; Row = 0 }
let b : Cell = { Col = 5; Row = 2 }
let isTransparent (c: Cell) = c <> { Col = 3; Row = 1 }

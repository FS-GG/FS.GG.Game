// Typecheck fixtures for the mini-tanks TestSpec (see scripts/typecheck-md-blocks.fsx).
//
// Only two of the eight ```fsharp blocks need context. Blocks 1-6 are self-contained type sketches
// (they open FS.GG.Game.Core, the scaffold's Geometry.Vec2, and their own predecessor blocks) and the
// §8.1 ChannelMap compiles against the real FS.GG.UI.Symbology the testspecs corpus references. The
// two below are a DU-case CONTINUATION and a lifted BODY, exactly the two shapes the harness fixtures
// exist for — see tower-defense.fs for the same pattern.

//#block 7 "| MenuUp | MenuDown              // move cursor (wraps)"
// A DU-CASE CONTINUATION. The prose above the block says "extend your Msg with these cases"; the block
// is written as bare `| Case` lines with no `type ... =` header, so it cannot stand alone. The fixture
// supplies the header the prose left implicit, and the block's case SHAPES (`MenuAdjust of dir:int`) —
// what a reader copies — are compiled verbatim below it.
type MenuMsg =

//#block 8 "let struct (ticks, acc') ="
// The fixed-step accumulator fragment (§13). The block is a BODY lifted out of the Tick handler, so its
// free names are the handler's locals — bound here. `Model` comes from the document itself (block 5
// declares it); `simStep` is the whole §7.2 step, stubbed to the identity so the fragment typechecks.
let mutable acc = 0.0
let realDt = 1.0 / 60.0
let dtFixed = 1.0 / 60.0
let maxStepsPerFrame = 5
let model : Model = Unchecked.defaultof<Model>
let simStep (m: Model) : Model = m

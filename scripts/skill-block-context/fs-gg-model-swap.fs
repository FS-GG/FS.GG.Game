// Typecheck fixtures for fs-gg-model-swap (see scripts/typecheck-md-blocks.fsx).

//#block 1 "type Model = Product.Model.Model"
//#skip the Program.fs seam of the GENERATED product — it aliases `Product.Model.Model`, `Product.View.view`, `Product.EvidenceCommands.tick`, namespaces that exist only inside a scaffolded product (FS.GG.Templates owns them) and nowhere in this repo. Nothing here to compile it against.

//#block 2 "type Enemy ="
// The `Vec2 -> Scene.Rect` crossing, in the skills corpus. This block is the one that TEACHES the
// crossing — "build your positions on Geometry.Vec2 and cross into the scene with its toPoint/toRect"
// is the whole point of the pitfall it closes — and until #176 it was gated by NOTHING: its fence is
// indented (it sits inside a bullet), the extractor only saw column-0 fences, and the drop-detector
// that was supposed to catch that shared the same blind predicate. #165 reconstructed `toRect` so this
// could be compiled instead of stepped around; this fixture is what finally compiles it.
//
// The `open` is the whole fixture, and it is load-bearing — the same reason, verbatim, as
// scripts/testspec-block-context/pong.fs //#block 2. The block annotates `bounds` as `: Rect`
// UNQUALIFIED, and this corpus's ambient opens end `FS.GG.Game.Core; FsGg.SkillCheck.Scaffold` with no
// Scene. `FS.GG.Game.Core.Rect` and `FS.GG.UI.Scene.Rect` are structurally identical and nominally
// distinct, so without this line `Rect` binds to the SIM rect while `toRect` correctly returns the
// SCENE one, and the block fails FS0001. A fixture's text is emitted AFTER the ambient opens, so this
// shadows them — which is exactly the reader's render layer, where the file that builds a scene has
// opened `FS.GG.UI.Scene` and `Rect` means Scene's.
//
// What is NOT done here, and must not be: `toRect` is not re-pointed at the sim `Rect`, and the sim
// `Rect` is not dropped from the ambient opens. Either would make the #129/#132/#140/#144 crossing
// typecheck by fiat — which is the defect this gate exists to catch.
open FS.GG.UI.Scene

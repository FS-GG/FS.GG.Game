// Typecheck fixtures for fs-gg-persistence (see scripts/typecheck-md-blocks.fsx).
//
// Both blocks teach `FS.GG.UI.Canvas` (`Persistence.save`, `SaveSlot`, `PersistenceEffect`) — a
// FS.GG.Rendering package this repo does not reference (it pins FS.GG.UI.Scene and
// FS.GG.UI.KeyboardInput, not Canvas; see Directory.Packages.local.props). Same hole as
// fs-gg-audio: published skills that no gate can reach. Filed with the PR for FS-GG/FS.GG.Game#141.

//#block 1
//#skip teaches FS.GG.UI.Canvas (`PersistenceEffect`, `Persistence.save`, `SaveSlot`) — a package this repo does not reference. It ALSO elides its body (`let serialize ... = ...`), which is not F# and could not compile even with the package.

//#block 2
//#skip teaches FS.GG.UI.Canvas (`Persistence.interpret` / PersistenceEvidence) — package not referenced by this repo.

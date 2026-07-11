// Typecheck fixtures for fs-gg-persistence (see scripts/typecheck-md-blocks.fsx).
//
// Both blocks teach `FS.GG.UI.Canvas` (`Persistence.save`, `SaveSlot`, `PersistenceEffect`), a
// FS.GG.Rendering package this repo does not consume — so, like fs-gg-audio's, they were published,
// copied by readers, and reachable by no gate (FS.GG.Game#150). The skills corpus declares Canvas in
// `PackageRefs` now, so block 2 compiles against the REAL assembly; no product project references
// it (see the gate-only group in Directory.Packages.local.props).
//
// Block 2 is self-contained (it opens FS.GG.UI.Canvas and binds its own values), so it needs no
// section here.

//#block 1
//#skip an ELIDED body: `let serialize (model: Model) : string = ...`, where `...` stands in for the reader's own encoder. `...` is not F#, so there is nothing here to typecheck — the same honest property as fs-gg-ai block 2, and NOT the package hole that used to hide behind it (FS.GG.UI.Canvas is referenced now, and block 2 compiles against it).

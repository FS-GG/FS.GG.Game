// Typecheck fixtures for fs-gg-persistence (see scripts/typecheck-md-blocks.fsx).
//
// All three blocks teach `FS.GG.UI.Canvas` (`Persistence.save`, `SaveSlot`, `PersistenceEffect`), a
// FS.GG.Rendering package this repo does not consume — so, like fs-gg-audio's, they were published,
// copied by readers, and reachable by no gate (FS.GG.Game#150). The skills corpus declares Canvas in
// `PackageRefs` now, so blocks 2 and 3 compile against the REAL assembly; no product project
// references it (see the gate-only group in Directory.Packages.local.props).
//
// Blocks 2 and 3 are self-contained (each opens FS.GG.UI.Canvas and binds its own values), so neither
// needs a section here. Block 2 is the `SaveCues.forTransition` cue seam (FS.GG.Game#214); block 3 is
// the record-only `Persistence.interpret` fold. Block 2 is deliberately COMPILED rather than skipped:
// it is the one that teaches the `Started` case, so a reader copying it is copying the fix for the
// `Init` blind spot — it has to bind.

//#block 1 "type Msg = CheckpointReached | ContinueGame | EraseSave"
//#skip an ELIDED body: `let serialize (model: Model) : string = ...`, where `...` stands in for the reader's own encoder. `...` is not F#, so there is nothing here to typecheck — the same honest property as fs-gg-ai block 2, and NOT the package hole that used to hide behind it (FS.GG.UI.Canvas is referenced now, and block 2 compiles against it).

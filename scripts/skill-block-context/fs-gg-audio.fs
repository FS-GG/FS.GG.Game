// Typecheck fixtures for fs-gg-audio (see scripts/typecheck-skill-blocks.fsx).
//
// EVERY block in this skill is skipped, and not because the blocks are unimportant — because this
// gate cannot reach them. They teach `FS.GG.Audio.Core` / `FS.GG.Audio.Host`, packages FS.GG.Game
// does not reference at all (it builds FS.GG.Game.Core and pins FS.GG.UI.Scene /
// FS.GG.UI.KeyboardInput; see Directory.Packages.local.props). There is nothing here to compile
// them against, and inventing a stand-in Audio surface would mean typechecking the skills against
// a fiction — the exact failure mode this gate exists to end.
//
// So this skill's five blocks are in the same state fs-gg-game-core's were before #141: published,
// copied by readers, and gated by nothing. That is a real, open hole and it is filed as such (see
// the PR for FS-GG/FS.GG.Game#141) rather than papered over. The fix is to reference the audio
// package here — or, more likely, to run this same harness in the repo that OWNS it.

//#block 1
//#skip teaches FS.GG.Audio.Core (`Audio.playSfx`, `SoundId`, `AudioEffect`) — a package this repo does not reference, so there is nothing to typecheck it against. See this file's header: an unreachable subject, not an excused one.

//#block 2
//#skip teaches FS.GG.Audio.Core (`Audio.setBusVolume`, `Audio.duck`, `Audio.playSfx3D`) — package not referenced by this repo.

//#block 3
//#skip teaches FS.GG.Audio.Core (`Audio.interpret` / AudioEvidence) — package not referenced by this repo.

//#block 4
//#skip teaches FS.GG.Audio.Host (`OpenAlBackend.create`, `Viewer.runAppWithAudio`) — package not referenced by this repo.

//#block 5
//#skip teaches FS.GG.Audio.Core + the generated host (`GeneratedAppHost.dispatchKey`) — neither is referenced by this repo.

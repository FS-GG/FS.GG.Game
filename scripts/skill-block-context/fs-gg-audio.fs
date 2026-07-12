// Typecheck fixtures for fs-gg-audio (see scripts/typecheck-md-blocks.fsx).
//
// All five blocks USED to be skipped, and not because they were unimportant — because the gate
// could not reach them. They teach `FS.GG.Audio.Core` / `FS.GG.Audio.Host`, packages FS.GG.Game
// does not consume, so there was nothing here to compile them against: published skills, copied by
// readers, gated by nothing (FS.GG.Game#150).
//
// They are reachable now, and against the REAL packages — the same assemblies the reader restores.
// The skills corpus declares them in `PackageRefs` (scripts/typecheck-md-blocks.fsx) and their
// versions come from the gate-only group in Directory.Packages.local.props; no product project
// references any of them. Standing up a stand-in Audio surface was never an option: a skill
// typechecked against a fiction is worse than one that is not typechecked at all, because the green
// tick stops anyone looking.
//
// Blocks 1 and 3 are self-contained — they open FS.GG.Audio.Core themselves and bind their own
// values, so they need no section here.

//#block 2 "Audio.setBusVolume Music 0.4                     // quieten the music bus"
// The bus/ducking section. The block is a bare sequence of calls with NO `open` of its own (it
// continues the previous section's context in the prose), so the open is supplied here. `Music` is
// `Bus.Music`, unqualified — which is what the reader types, and it resolves only because `Bus` is
// not RequireQualifiedAccess. If Audio ever qualifies it, this block stops compiling and the prose
// is wrong: that is the gate working.
open FS.GG.Audio.Core

//#block 4 "use backend = OpenAlBackend.create AudioCues.resolver"
// The host seam: a real device, and the SINK value the viewer's launcher takes.
//
// `AudioCues` is the PRODUCT's generated cue table, not a package — the same kind of reconstruction
// _scaffold.fs does for `Geometry.Vec2`, and the only thing in this block that is not real published
// surface. Its `resolver` is a genuine `FS.GG.Audio.Host.AssetResolver`, so the block is still
// typechecked against the real `OpenAlBackend.create` signature; only the cue bytes are ours, and
// returning `None` is honest because the resolver's RESULT is not what the block teaches.
//
// The block STOPS at `let audioSink = Audio.play backend` and does not launch (FS.GG.Game#204). It
// used to end `Viewer.runAppWithAudio viewerOptions (Audio.play backend) generatedHost`, which is
// the GAME family's launcher — and the skill now also materializes on `app`, whose Controls-family
// launcher is a different function. Naming one of the two in the one block a reader copies is how an
// `app` author ends up calling a function that will not type-check for them. The per-family entry
// points are a TABLE in the body instead, so `viewerOptions`/`generatedHost` have no block left to
// serve and are gone from this fixture with them.
open FS.GG.Audio.Core

module AudioCues =
    let resolver : AssetResolver =
        { ResolveSound = fun (_: SoundId) -> None
          ResolveTrack = fun (_: TrackId) -> None }

//#block 5 "[ Audio.setMasterVolume next.Settings.Volume"
// `Started`, and the trap it closes (FS.GG.Rendering#458): `forTransition` is a function of a
// TRANSITION, and the initial model makes none, so state that was LOADED rather than transitioned
// into never reaches the mixer unless `Started` carries it.
//
// The block is the reader's own `AudioCues.forTransition`, so everything it names is theirs: the
// `Msg` cases, and a `Model` whose initial state implies a sound (a restored volume). Both are
// declared here — they are exactly what a product would write, and the block is compiled VERBATIM
// against them, so `Audio.setMasterVolume`/`playMusic`/`playSfx` are checked against the real
// published `FS.GG.Audio.Core` surface a reader would copy this into.
//
// `Started` is a plain `Msg` case, NOT framework surface: the scaffold's generated host dispatches
// it (`forTransition Started m m`), but the case itself is declared by the product — so it is
// declared here, in the product's half, rather than arriving from an `open`.
open FS.GG.Audio.Core

type Settings = { Volume: float }
type Model = { Settings: Settings }
type Msg =
    | Started
    | Fired

//#block 6 "GeneratedAppHost.dispatchKey host keyEvent model"
// The record-only path: the same `AudioEvidence` a headless run yields, so a test can assert on
// sound WITHOUT a device. `dispatchKey` returns `(model * ViewerEffect list)` and `audioRequests`
// narrows that to the `AudioEffect list` the block interprets — the `|> snd` and the two module
// names are the point of the block, and all of it is real SkiaViewer surface.
//
// The `open` order is load-bearing: `Audio` exists in BOTH FS.GG.Audio.Core (`interpret`) and
// FS.GG.Audio.Host (`play`), and F# MERGES same-named modules across opens. The block says
// `Audio.interpret`, so Core must be in scope — it is, and Host is not opened here at all.
open FS.GG.UI.KeyboardInput
open FS.GG.UI.SkiaViewer
open FS.GG.Audio.Core

type KeyModel = { Score: int }
type KeyMsg = Fire

let host : GeneratedAppHost<KeyModel, KeyMsg> = Unchecked.defaultof<_>
let keyEvent : ViewerKeyEvent = Unchecked.defaultof<_>
let model : KeyModel = { Score = 0 }

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
// The block STOPS at `let audioSink = Audio.play backend` and does not launch (FS.GG.Game#204),
// because the launcher is per-FAMILY and naming one of the two here is how an `app` author ends up
// copying the `game` family's function. The launch is the LAUNCHER block's job (below), and it
// teaches BOTH families for that reason. Named, not numbered: a block ordinal in prose is exactly the
// drift the anchor check catches in the directives and cannot catch here.
open FS.GG.Audio.Core

module AudioCues =
    let resolver : AssetResolver =
        { ResolveSound = fun (_: SoundId) -> None
          ResolveTrack = fun (_: TrackId) -> None }

//#block 5 "| Started -> [ Audio.setMasterVolume next.Settings.Volume ]"
// Deriving cues from a model DIFF: most gameplay events (a score, an enemy death) carry no `Msg`,
// so `forTransition` recovers them by comparing `previous` against `next` inside the `Tick` arm.
// The block is the reader's own `forTransition`, so everything it names is theirs — the `Msg`
// cases (`Started`, `Fired`, and a payload-carrying `Tick`) and a `Model` whose fields the diff
// reads (`Score`, `Enemies`, and the `Settings.Volume` the `Started` arm restores). All are
// declared here, exactly as a product would write them, and the block is compiled VERBATIM against
// them so `Audio.playSfx`/`setMasterVolume` are checked against the real published surface.
//
// `Enemies` is `int list` (ids) so `List.length` is honest; `Score` is a plain `int` so `>` is the
// real comparison the reader copies. No label overlaps Scene's Point/Rect (§2b), by construction.
// Each block compiles against its OWN section, so these `Settings`/`Model`/`Msg` do not collide
// with block 6's — the two never share a compilation unit.
open FS.GG.Audio.Core

type Settings = { Volume: float }
type Model =
    { Settings: Settings
      Score: int
      Enemies: int list }
type Msg =
    | Started
    | Fired
    | Tick of float

//#block 6 "| Started ->"
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

//#block 7 "let appOutcome       = ControlsElmish.runInteractiveAppWithAudio viewerOptions audioSink interactiveHost"
// The LAUNCH, per family — and the block this skill spent two releases unable to compile.
//
// `ControlsElmish.runInteractiveAppWithAudio` is the launcher an `app`-profile product must call to
// get sound, and it first shipped in FS.GG.UI 0.9.0. While this repo pinned the 0.5.0 train the gate
// could not reach it, so #215 shipped BOTH entry points as a prose table — uncompiled, and therefore
// invisible to the consuming repo's API-coverage scanner, which counts symbols it finds in ```fsharp
// blocks (it dropped fs-gg-audio from 4 documented symbols to 2, and the two it lost were exactly
// these). #217 moved the train to 0.9.0; this block is what that move was FOR (#225). The table is
// still in the body as the at-a-glance summary — the defect was never that the table existed, it was
// that the one example an `app` author copies could not be checked.
//
// BOTH families are bound here on purpose. They are the pair a reader must choose BETWEEN, and the
// mistake the section exists to prevent — reaching for the game family's function on a Controls
// product — is only visible when both are in scope and their host records are distinct types.
//
// All FOUR launchers, because the SILENT twins were the same defect one level down (#234). #225 left
// `ControlsElmish.runInteractiveApp` and `Viewer.runApp` in the prose table only — and the coverage
// scanner counts symbols it finds in ```fsharp blocks, so the skill taught four launchers and gated
// two. They need no new bindings: a silent launcher is its `*WithAudio` twin with the sink argument
// left out, so it takes `viewerOptions` and the same host record, and returns the same
// `Result<ViewerLaunchOutcome, ViewerRunFailure>` — which is what the block now proves, rather than
// the body merely asserting it.
//
// The hosts are `Unchecked.defaultof<_>` because what the block teaches is the CALL: which launcher,
// and the sink's position between `viewerOptions` and the host record. A hand-built host record would
// be fiction with more moving parts; the launcher SIGNATURES are real published surface, and they are
// what is checked. `audioSink` is `AudioEffect list -> unit` — the type `Audio.play backend` returns
// in block 4, so the value this block accepts is the one the reader actually has.
open FS.GG.Audio.Core
open FS.GG.UI.SkiaViewer
open FS.GG.UI.Controls.Elmish

type LaunchModel = { Score: int }
type LaunchMsg = Fired

let viewerOptions : ViewerOptions = Unchecked.defaultof<_>
let audioSink : AudioEffect list -> unit = ignore
let interactiveHost : InteractiveAppHost<LaunchModel, LaunchMsg> = Unchecked.defaultof<_>
let generatedHost : GeneratedAppHost<LaunchModel, LaunchMsg> = Unchecked.defaultof<_>

//#block 8 "GeneratedAppHost.dispatchKey host keyEvent model"
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

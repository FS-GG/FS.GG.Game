# Game audio library — architecture & design

> **Provenance (relocated 2026-07-06, ADR-0022).** Authored **2026-07-05 in FS-GG/.github** and relocated here with the rest of the game-skill design corpus. The **`fs-gg-audio`** skill this designs is owned by **FS.GG.Game** (P4 skill-ownership migration). **Note — unlike the sim primitives, the audio *surface* did NOT move to `FS.GG.Game.Core`:** the `Audio` request vocabulary (`AudioEffect` / `Audio.interpret`) stayed in **`FS.GG.UI.Canvas`** (render-adjacent), so the `FS.GG.UI.Canvas.Audio` references below remain accurate.

- **Date:** 2026-07-05
- **Owner:** `FS.GG.Game` (the `fs-gg-audio` skill; **Audio surface home:** `FS.GG.UI.Canvas`)
- **Status:** Design proposal (pre-ADR). Seeds a Rendering epic + a cross-repo coordination
  item; does **not** itself change a contract.
- **Scope:** The **playback backend + effects + spatial audio + asset story** for the
  existing `fs-gg-audio` capability — i.e. the piece the game TestSpecs call *"a real
  playback backend is deferred"*.
- **Language/target:** F# on `net10.0` (FSharp.Core `10.1.301`), cross-platform
  (Windows/Linux/macOS), functional-first, Elmish/MVU-friendly.
- **Related:** [`architecture.md`](../architecture.md) · game TestSpecs
  [`flappy-bird`](../TestSpecs/Games/flappy-bird.md) §10, [`metroidvania`](../TestSpecs/Games/metroidvania.md) §10 ·
  skill registry row `fs-gg-audio` ([`registry/skills.yml`](../../registry/skills.yml)) ·
  Rendering Feature 243 / [Rendering#113](https://github.com/FS-GG/FS.GG.Rendering/issues/113).

---

## 1. TL;DR — the recommendation

> Build **`FS.GG.UI.Audio`** as the host-side interpreter that turns the *already-shipped*
> pure `AudioEffect` request values into real sound, behind a small **pluggable
> `IAudioBackend`** seam. Ship **two backends**: a **deterministic null/record backend**
> (default under test and headless; this is essentially today's `Audio.interpret`) and a
> **real device backend built on OpenAL via `Silk.NET.OpenAL`** (MIT bindings over
> OpenAL Soft). Decode assets with the **fully-managed** `NVorbis` (OGG) + a built-in WAV
> reader; MP3 via `NLayer` is optional. Get reverb / filters / echo / distortion / 3D
> positioning **for free** from OpenAL's **EFX** extension, with a thin managed F# DSP
> layer for the handful of effects a backend lacks. Curate a **CC0 / MIT-0 starter
> asset pack** (Kenney + Sonniss GDC + OpenGameArt-CC0) with a per-asset SPDX license
> manifest.

**Why this shape:** it preserves the FS-GG invariant that game logic stays a pure
function of values (the `AudioEffect`/`AudioEvidence` edge is the audio analogue of the
`Json-is-contract`, `Rich/Plain-are-projections` rule and *degrade-to-zero*), it keeps
determinism and headless testing intact, it aligns the audio backend with the **same
Silk.NET ecosystem** the OpenGL/windowing stack already lives in, and every mandatory
dependency is **permissive (MIT / MIT-0 / public-domain)** with the one LGPL component
(OpenAL Soft native) **dynamically linked and swappable** — LGPL-clean for closed-source
games.

---

## 2. What exists today, and the gap this fills

The `fs-gg-audio` capability is **already defined** — but only at its *pure edge*. Per the
game TestSpecs (flappy-bird §10, metroidvania §10), audio today is:

- **Requested as pure values.** `update` returns `AudioEffect` values (`PlaySfx`,
  `PlayMusic … loop`, `StopMusic`, `SetMasterVolume`, …) alongside the model change and
  **never touches an audio device**.
- **Interpreted deterministically.** `Audio.interpret` folds a frame's requests into
  `AudioEvidence` — the requested effects in dispatch order, volumes clamped `[0.0, 1.0]`.
- **Named opaquely.** `SoundId` / `TrackId` are names the game owns; *"the host resolves
  them to real assets."*
- **Explicitly backend-less.** *"a real playback backend is deferred, so tests assert on
  `AudioEvidence.Requested`, not on audio output."*

So the contract surface, the determinism story, and the test discipline **already exist and
are correct**. What is missing — and what this document designs — is the **host-side
realization**: the thing that takes `AudioEvidence.Requested` (or the live `AudioEffect`
stream) and actually *makes noise*, with mixing, music streaming, volume buses, fades,
3D positioning, and effects, using free and license-compatible libraries.

This is deliberately the same pattern as the rendering edge: **pure request → recorded
evidence (contract, testable) → optional real projection (host, degradable).** The audio
backend is to `AudioEffect` what the SkiaViewer is to a `Scene`.

---

## 3. Design principles (inherited from FS-GG)

| # | Principle | What it means for audio |
|---|---|---|
| P1 | **Pure edge, effectful host** | Game code keeps emitting `AudioEffect` values; only `FS.GG.UI.Audio` (host) opens a device. No change to `update`'s signature. |
| P2 | **Degrade to zero** | No device / no assets / CI runner ⇒ the null backend runs, records evidence, and the game is *identical minus sound*. Never a hard failure. |
| P3 | **Determinism under test** | The default backend is byte-deterministic and hardware-free; the real backend is opt-in. Snapshot tests assert on `AudioEvidence`, never on samples. |
| P4 | **One-way dependencies** | `FS.GG.UI.Audio` depends only on `FS.GG.UI.Canvas`/core value types and third-party audio libs; nothing in FS-GG depends *up* into it. Lives under Rendering, never Governance. |
| P5 | **Permissive licensing only** | Every mandatory dependency and shipped asset is MIT / MIT-0 / BSD / CC0 / public-domain. LGPL is allowed *only* dynamically-linked and replaceable. No GPL, no proprietary-paid (rules out BASS). |
| P6 | **Pluggable backend** | `IAudioBackend` is a narrow seam so a team can swap OpenAL ↔ miniaudio ↔ null without touching game code, and so we are never captive to one native dependency. |

---

## 4. Backend library survey (the research)

Candidates evaluated for a cross-platform `net10.0` game-audio backend, focused on
**license compatibility**, **cross-platform reach**, **3D + effects capability**, and
**ecosystem fit** with the existing Silk.NET/OpenGL stack.

| Library | What it is | License | Cross-platform | 3D + effects | Verdict |
|---|---|---|---|---|---|
| **Silk.NET.OpenAL** ✅ | MIT-licensed managed bindings to **OpenAL** (Soft), same project family as the Silk.NET GL/windowing bindings. | Bindings **MIT**; native **OpenAL Soft = LGPL-2.1** (dynamically linked ⇒ clean). | Win/Linux/macOS + mobile-proven. | **Yes** — native 3D positional audio + the full **EFX** effects/filters suite. | **PRIMARY.** Best ecosystem fit + richest built-in feature set; LGPL-clean via dynamic link. |
| **miniaudio** (P/Invoke) ✅ | Single-file C audio engine; high-level engine with mixing, effects, node-graph, optional 3D spatialization. | **Public-domain OR MIT-0** — zero obligations. | Win/Linux/macOS/mobile/web. | **Yes** — node-graph effects + 3D spatialization built in. | **SECONDARY / license-purist path.** Most permissive of all; needs a thin native build + P/Invoke (or an existing binding). Recommended alternate backend. |
| **OpenTK (OpenAL)** | MIT bindings to OpenAL; the older sibling to Silk.NET. | MIT (+ LGPL native as above). | Win/Linux/macOS; weaker mobile, irregular releases. | Yes (same OpenAL/EFX). | **Fallback binding** only if Silk.NET is undesirable; strictly less ecosystem-aligned. |
| **NAudio** | Mature .NET audio/MIDI library. | MIT. | **Windows-centric** (WASAPI/DirectSound); not a cross-platform playback path. | Partial; no first-class game 3D. | **Rejected** for the cross-platform backend (fails P1 platform reach). Fine as an optional Windows-only capture/utility. |
| **ManagedBass / BASS** | .NET wrapper over the BASS engine. Excellent features. | Wrapper MIT, but **BASS itself is proprietary**, free only for non-commercial; commercial needs a **paid** license. | Yes. | Yes. | **Rejected** on P5 — not free/permissive for commercial games. |
| **SoLoud** (+ C# bindings) | Easy portable C/C++ game audio engine (zlib/libpng). | **zlib/libpng** (permissive). | Yes. | Yes (3D + effects). | **Viable alternate** to miniaudio; bindings less maintained. Kept as a documented option, not the default. |

**Decoders (fully-managed, no native dep):**

| Decoder | Format | License | Note |
|---|---|---|---|
| **NVorbis** | OGG Vorbis | permissive (MIT-style, Xiph-derived) | Pure managed, no P/Invoke/unsafe; ideal default music/SFX format. |
| built-in WAV reader | PCM WAV | (ours) | Trivial; the zero-dependency baseline for short SFX. |
| **NLayer** | MP3 (MPEG 1/2 L1-3) | **MIT** | Optional. MP3 decode patents expired (2017); safe to ship. |

**Net licensing position.** Mandatory deps are **MIT** (`Silk.NET.OpenAL`, `NVorbis`) plus
the **LGPL** OpenAL Soft native, which we ship as a **separate, dynamically-linked,
user-replaceable** shared library (`libopenal.so`/`.dylib`/`OpenAL32.dll`) — the
well-trodden LGPL-compliant path for commercial games (link dynamically + include the
LGPL notice/copy). Choosing the **miniaudio** backend removes even that, leaving a
100% public-domain/MIT-0/MIT dependency set.

---

## 5. Architecture

```text
   GAME (pure)                       FS.GG.UI.Audio  (host, this design)                  NATIVE
 ┌──────────────┐   AudioEffect    ┌───────────────────────────────────────────┐
 │ update:      │  values (pure)   │  Audio.interpret ─► AudioEvidence          │   (test/CI: stop here —
 │ Model→Model  │ ───────────────► │      (deterministic, hardware-free) ◄──────┼──   deterministic evidence)
 │  * AudioEffect│                  │                     │                      │
 └──────────────┘                  │                     ▼                      │
                                   │        ┌──────────────────────────┐        │
   assets (SoundId/TrackId)        │        │  AudioEngine             │        │
   ─────────────────────────────►  │        │  • voice/mixer mgmt       │        │
   AssetResolver + AudioCache      │        │  • buses: Master/Music/   │        │
   (decode via NVorbis/WAV)        │        │    Sfx/Ui/Ambient         │        │
                                   │        │  • fades / ducking        │        │
                                   │        │  • 3D listener + emitters │        │
                                   │        │  • effect graph (sends)   │        │
                                   │        └───────────┬──────────────┘        │
                                   │                    ▼  IAudioBackend seam    │
                                   │   ┌────────────┐ ┌───────────┐ ┌──────────┐ │
                                   │   │ NullBackend│ │ OpenAL     │ │ miniaudio │─┼─► OpenAL Soft (LGPL,
                                   │   │ (record)   │ │ (Silk.NET) │ │ (P/Invoke)│ │   dyn-linked) / libminiaudio
                                   │   └────────────┘ └───────────┘ └──────────┘ │
                                   └───────────────────────────────────────────┘
```

### 5.1 Layers

- **L0 — `IAudioBackend` (seam).** The narrowest possible device abstraction:
  `openDevice`, `createBuffer`/`streamBuffer`, `play`/`stop`/`pause`, `setSourceGain`,
  `setListener`/`setSourcePosition` (3D), `attachEffect`/`setFilter`, `dispose`. Three
  implementations: **Null** (record, default), **OpenAL** (Silk.NET), **miniaudio**
  (P/Invoke). Backend choice is a host config value, never visible to game code.
- **L1 — Asset & decode.** `AssetResolver` maps opaque `SoundId`/`TrackId` → a file/stream;
  `NVorbis`/WAV decode to PCM. **Short SFX** are decoded once into a cached static buffer;
  **music/ambient** stream in chunks (loopable) to bound memory.
- **L2 — `AudioEngine`.** Voice pool + mixer; **named buses** (`Master`, `Music`, `Sfx`,
  `Ui`, `Ambient`) with independent gain (the TestSpecs' *Master volume* + *Sound* toggle
  map straight onto `Master`/`Sfx`); fades, cross-fades, and side-chain **ducking**
  (drop music under a stinger); a 3D **listener** + per-voice **emitter** position; an
  **effect graph** of auxiliary sends (reverb/echo) + per-voice **direct filters**.
- **L3 — F# API surface.** Two idioms, both pure at the call site:
  1. **Interpreter drive** — feed `AudioEvidence.Requested` (or the live `AudioEffect`
     list) into `Audio.play : AudioEngine -> AudioEffect list -> unit` once per frame.
     Game code is unchanged.
  2. **Elmish `Cmd`/`Sub`** — mirror the existing `FS.GG.UI.Controls.Elmish.Authoring`
     convention: `Audio.Cmd.playSfx`, `Audio.Cmd.playMusic`, `Audio.Cmd.fade`, and a
     `Audio.Sub.events` subscription that surfaces `PlaybackFinished`/`LoopWrapped` back
     as messages. This matches the shipped `Cmd.none`/`Sub.none` aliases (Feature 241).

### 5.2 Mapping `AudioEffect` → backend

| `AudioEffect` (pure) | Engine action | OpenAL backend | miniaudio backend |
|---|---|---|---|
| `PlaySfx (id, gain)` | acquire voice on `Sfx` bus, one-shot | `alSourcePlay` w/ cached buffer | `ma_engine_play_sound` |
| `PlayMusic (id, loop)` | stream on `Music` bus | queued streaming buffers, `AL_LOOPING` | `ma_sound` streaming, looping |
| `StopMusic` | stop `Music` voice (opt. fade-out) | `alSourceStop` | `ma_sound_stop` |
| `SetMasterVolume v` | set `Master` bus gain (clamp `[0,1]`) | listener gain | engine volume |
| *(new)* `PlaySfx3D (id, pos, gain)` | 3D emitter on `Sfx` bus | `AL_POSITION` + distance model | spatialized sound |
| *(new)* `SetBusVolume (bus, v)` | per-bus gain | group gain | node-graph gain |
| *(new)* `Duck (bus, amount, ms)` | timed side-chain attenuation | scheduled gain ramp | scheduled gain ramp |

The three *(new)* variants are **additive** to the `AudioEffect` DU — they extend the
pure surface without breaking existing games (a v1 flappy-bird never emits them). Any
addition to `AudioEffect` is a **contract-relevant change** to the `fs-gg-audio` capability
and must go through the normal Rendering surface-bump + registry reconcile.

---

## 6. Effects & DSP

Two complementary sources of effects, chosen so we implement almost nothing ourselves:

1. **Native EFX (OpenAL).** OpenAL Soft implements the full **EFX** extension: **reverb**
   (incl. EAX-quality environmental reverb), **echo**, **chorus**, **flanger**,
   **distortion**, **compressor**, **equalizer**, **ring modulator**, **autowah**, plus
   **low-pass / high-pass / band-pass filters** on both the *direct* (dry) path and the
   *send* (wet) path of a source. These are created as **effect objects** attached to
   **auxiliary effect slots**; voices route sends to slots. This gives us
   environment reverb ("cave", "hall"), occlusion/muffling (low-pass on the direct path),
   and per-voice coloring **without writing a DSP kernel**.
2. **Managed F# DSP micro-layer** (`FS.GG.UI.Audio.Dsp`, pure, backend-independent) for the
   few things a backend lacks or that we want deterministic and portable: **biquad
   filters** (low/high/band/peak), **gain/pan**, a simple **delay line**, **linear/eq-power
   fades**, and a **tanh soft-clip**. Small, unit-testable, value-in/value-out — usable by
   the miniaudio node graph, the null backend (for offline render tests), or as a fallback
   when EFX is unavailable. This is where an F#-idiomatic, allocation-free `Span<float32>`
   processing style pays off.

**Effect declaration is data.** An `EffectSpec` DU (`Reverb of ReverbParams`,
`LowPass of cutoff`, `Echo of …`) is a pure value the engine realizes on whichever backend
is active — so a game can request *"muffled + hall reverb in this room"* declaratively and
still test that the request was made (evidence), independent of whether a device is open.

---

## 7. Spatial / 3D audio

- **Listener** = one per scene (position, velocity, orientation), driven from the game's
  camera/player each frame.
- **Emitters** = per-voice position (+ optional velocity for Doppler). OpenAL applies the
  **distance-attenuation model** (inverse/linear/exponential, with `refDistance`/`maxDistance`
  /`rolloff`) and panning natively; miniaudio spatializes in its node graph.
- **2D games** (the current TestSpec set — flappy-bird, metroidvania, tower-defense) use a
  **stereo-pan-by-x** convenience (`PlaySfx3D` with `z=0`) so a threat on the right is heard
  on the right, without committing the game to a 3D coordinate space.
- Falls back cleanly: no 3D support ⇒ emitters collapse to non-positional voices at the
  requested gain (degrade-to-zero, P2).

---

## 8. Determinism & testing

This is the property that must **not** regress. The design keeps it by construction:

- **Default backend under test = Null/record.** No device is opened; `Audio.interpret`
  yields the same `AudioEvidence` the TestSpecs already assert on. Existing audio tests are
  untouched.
- **Offline render tests (optional).** The managed DSP layer can render a fixed input
  buffer to a deterministic output buffer for exact numeric snapshots (fixed-point-stable),
  mirroring the framework's **fixed-width deterministic render** testing culture — no audio
  hardware, byte-identical in CI.
- **The real device backend is never in the CI assertion path.** It is exercised behind a
  `--audio-device` opt-in and a manual/soak lane, exactly as the SkiaViewer host is exercised
  separately from Scene snapshot tests.

---

## 9. Free & license-compatible sound assets

A library is only useful with sounds to play. Recommend shipping a **small curated starter
pack** (a dozen UI/impact SFX + 1-2 ambient loops) under the `game`/`sample-pack` profiles,
sourced **exclusively from permissive, commercial-safe origins**, each recorded in a license
manifest.

| Source | License | Attribution? | Best for |
|---|---|---|---|
| **Kenney** (kenney.nl) | **CC0** (public domain) | No | UI, impacts, power-ups, retro SFX, simple music — the backbone of the starter pack. |
| **Sonniss — GDC Game Audio Bundle** | Royalty-free, commercial-OK | No | Large professional SFX library; hand-pick a few. |
| **OpenGameArt — CC0 collections** | **CC0** | No | Extra SFX/music loops for 2D/indie. |
| **Freesound** (filter to **CC0** only) | mixed CC — use **CC0** subset | No (for CC0) | One-off specific sounds; must filter out CC-BY/CC-BY-NC. |
| **Pixabay — Sounds** | Pixabay content license, commercial-OK | No | Stock SFX/music, no-attribution. |
| **Mixkit** | Mixkit free license | No (per license terms) | Stock music/SFX (verify per-item terms). |

**License hygiene requirement (fits the org's registry/governance culture).** Every shipped
asset carries an entry in an **`assets/audio/LICENSES.yml`** manifest — `{ file, sha256,
source-url, license (SPDX or CC0-1.0), author }` — and a scaffold/CI gate refuses an
undeclared or non-permissive asset. This mirrors the skill-manifest `sha256` +
`materializes-when` discipline already in `registry/skills.yml`: **content-addressed,
declared, checkable**. Prefer **CC0-1.0 / MIT-0** to avoid runtime attribution obligations;
CC-BY is allowed only with a generated `CREDITS` file; **CC-BY-NC / CC-BY-SA / GPL assets
are rejected** (fail P5).

---

## 10. Effort analysis — what's achievable at reasonable cost

Phased so each phase is independently shippable and *publish-before-flip* clean. Sizes are
rough (S ≈ days, M ≈ 1-2 wks, L ≈ 3-4 wks for one engineer).

| Phase | Deliverable | Effort | Notes / risk |
|---|---|---|---|
| **P0 — Seam + Null** | `IAudioBackend`, `NullBackend` (= today's `interpret`), `AudioEngine` skeleton, `AudioEffect`→action mapping, buses. **No device.** | **S** | Pure/managed only; zero native risk; keeps all tests green. Immediately useful (validates the seam). |
| **P1 — OpenAL MVP** | Silk.NET.OpenAL backend: WAV + NVorbis decode, one-shot SFX, streaming looped music, `Master`/`Sfx`/`Music` gain, `SetMasterVolume`. Ships `libopenal` per-RID with LGPL notice. | **M** | Native packaging (per-RID `runtimes/`), device init/teardown, streaming buffer queue. This alone satisfies **every current TestSpec** (flappy-bird, metroidvania). |
| **P2 — Effects + 3D** | EFX reverb/echo/filters via `EffectSpec`; `PlaySfx3D`, listener/emitter, distance model + stereo-pan-by-x; managed biquad/fade DSP. | **M** | EFX slot/send wiring; distance-model tuning. Additive `AudioEffect` variants → Rendering surface-bump + registry reconcile. |
| **P3 — Ergonomics** | Elmish `Cmd`/`Sub` surface, fades/cross-fades, ducking, playback-finished events. | **S-M** | Mirrors `Controls.Elmish.Authoring` conventions; mostly managed. |
| **P4 — Assets + hygiene** | Curated CC0 starter pack under `game`/`sample-pack`, `LICENSES.yml` manifest + gate, `fs-gg-audio` skill body fleshed out to cover playback (not just requests). | **S-M** | Content + a small gate; no engine risk. |
| **P5 — Alt backend** *(optional)* | miniaudio P/Invoke backend for the license-purist / zero-LGPL path. | **M** | Only if a consumer needs to avoid the LGPL notice entirely. Proves the seam. |

**Bottom line:** a genuinely useful, cross-platform, effects-capable F# game-audio library
that satisfies the *entire current TestSpec corpus* is **P0+P1 ≈ 2-3 weeks**; the full
"effects + 3D + Elmish + assets" experience is **P0-P4 ≈ 6-9 weeks** for one engineer,
because OpenAL/EFX + managed decoders do the heavy lifting and the pure edge already exists.

---

## 11. Risks & open questions

| # | Risk / question | Mitigation / proposal |
|---|---|---|
| R1 | **LGPL notice burden** on OpenAL Soft. | Dynamic-link + ship license copy + notice; document it; offer the miniaudio (MIT-0) backend as the escape hatch (P5). |
| R2 | **Native packaging** of `libopenal` across RIDs (win-x64, linux-x64, osx-arm64, …). | Consume a prebuilt native NuGet (`Silk.NET.OpenAL.Soft.Native`) which already ships per-RID binaries; no in-repo C build. |
| R3 | **Streaming/looping seams** (gapless loop points, buffer underruns). | Double/triple-buffered queue with a headroom target; managed decode on a dedicated pump; soak-test lane. |
| R4 | **`AudioEffect` DU growth** touches the shipped capability contract. | Treat 3D/bus/duck variants as an explicit, reviewed additive surface-bump (Rendering) + skill-registry reconcile (`.github`). Not a silent change. |
| R5 | **Backend feature parity** (EFX vs. miniaudio node-graph). | The `EffectSpec` value abstracts intent; unsupported effects degrade to the nearest managed-DSP approximation or no-op (P2). |
| R6 | **Asset license drift.** | The `LICENSES.yml` + `sha256` gate makes an undeclared/incompatible asset a hard CI failure, same model as the skill manifest. |

---

## 12. Cross-repo placement & next steps

- **Implementation home:** **`FS.GG.Rendering`**, as `FS.GG.UI.Audio` (+
  `FS.GG.UI.Audio.Dsp`) — it depends *down* onto `FS.GG.UI.Canvas`/core value types and
  third-party audio libs, and nothing depends up into it (P4). It is **never** a Governance
  concern.
- **Contract touchpoints:** the pure `AudioEffect`/`AudioEvidence` surface is part of the
  `fs-gg-audio` capability; the P2 additive variants are a Rendering **library-surface bump**
  that the `.github` **skill-registry** row must reconcile (like the 13→16 manifest
  catch-up that first added `fs-gg-audio`). No new *cross-repo* contract is introduced by
  the backend itself.
- **Decision record:** promote the backend-selection + LGPL-dynamic-link + pluggable-seam
  choice to an **ADR** (Rendering-local if it stays inside one repo; cross-repo `.github`
  ADR only if the `AudioEffect` surface or asset-manifest contract is formalized org-wide).
- **Coordination:** file a Rendering epic ("Real audio backend for `fs-gg-audio`") with the
  P0-P4 children above, and a Coordination-board item so the surface-bump/reconcile is
  sequenced; keep `registry/dependencies.yml` coherent when the capability surface advances.
- **New central package pins** (when P1 lands, added to `dist/dotnet` CPM): `Silk.NET.OpenAL`,
  `Silk.NET.OpenAL.Soft.Native`, `NVorbis` (and optionally `NLayer`).

---

## 13. Sources

Backend & bindings — [Silk.NET (dotnet/Silk.NET, MIT)](https://github.com/dotnet/Silk.NET) ·
[Silk.NET.OpenAL (NuGet)](https://www.nuget.org/packages/Silk.NET.OpenAL) ·
[Silk.NET.OpenAL.Soft.Native (NuGet)](https://www.nuget.org/packages/Silk.NET.OpenAL.Soft.Native/) ·
[miniaudio (mackron/miniaudio — public-domain / MIT-0)](https://github.com/mackron/miniaudio) ·
[SoLoud (jarikomppa/soloud — zlib/libpng)](https://github.com/jarikomppa/soloud) ·
[NAudio (naudio/NAudio — MIT, Windows-centric)](https://github.com/naudio/NAudio) ·
[ManagedBass (wrapper; BASS proprietary/paid)](https://github.com/ManagedBass/ManagedBass).
Decoders — [NVorbis](https://github.com/NVorbis/NVorbis) ·
[NLayer (MP3, MIT)](https://www.nuget.org/packages/NVorbis/).
Effects — [OpenAL EFX effects extension overview (Game Developer)](https://www.gamedeveloper.com/programming/openal-s-efx) ·
[openal-soft EFX header (kcat/openal-soft)](https://github.com/kcat/openal-soft/blob/master/include/AL/efx.h).
LGPL & dynamic linking — [openal-soft license discussion (#187)](https://github.com/kcat/openal-soft/issues/187) ·
[using OpenAL in a commercial game (thread)](https://openal.opensource.creative.narkive.com/LAqt0XsE/using-in-a-commercial-game).
Assets — [Kenney (CC0)](https://kenney.nl) ·
[Sonniss GDC Game Audio Bundle](https://sonniss.com/gameaudiogdc/) ·
[OpenGameArt CC0 sound effects](https://opengameart.org/content/cc0-sound-effects) ·
[OpenGameArt CC0 music](https://opengameart.org/content/cc0-music-0) ·
[Freesound](https://freesound.org) · [Pixabay Sounds](https://pixabay.com/sound-effects/).

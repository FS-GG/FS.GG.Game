# FS.GG.Game.Skills

FS.GG.Game's owner-authored **`mirrored: false` product** skill bytes (`owner: fs-gg-game`) as one
versioned package.

FS.GG.Game owns a `scope: product` skill class whose bytes live at
`template/product-skills/<id>/SKILL.md`. The `mirrored: false` subset has **no FS.GG.Rendering mirror**
(ADR-0022 §6), so nothing carries it to a scaffold. This package is that channel.

## Why a package (and why a sibling, not `.github`'s FS.GG.Drivers)

`fsgg-sdd scaffold` runs on an **offline** inner loop, and generic SDD is contractually barred from
embedding any cross-repo path or source. So the SDD CLI cannot reach into FS.GG.Game at scaffold time.
Instead:

1. FS.GG.Game **publishes** these skill bytes + `skill-manifest.json` as this versioned package
   ([ADR-0062](https://github.com/FS-GG/.github/blob/main/docs/adr/0062-versioned-kit-package-replaces-byte-copy-sync.md)
   substrate, [ADR-0063](https://github.com/FS-GG/.github/blob/main/docs/adr/0063-scaffold-materializer-sources-skills-from-the-owner-repo.md)
   owner-repo byte source).
2. `FS.GG.SDD.Cli` **pins** it and **restores** it at CLI build/publish time — online.
3. At **scaffold time** — offline — the CLI **materializes** each skill into the product tree's skill
   roots from the bytes it already carries, verifying each against the manifest `sha256`
   ([ADR-0014](https://github.com/FS-GG/.github/blob/main/docs/adr/0014-skill-vendoring-one-manifest-one-materialize-verify.md)).

This mirrors `.github`'s [`FS.GG.Drivers`](https://github.com/FS-GG/.github/tree/main/src/FS.GG.Drivers)
substrate one repo over. It is deliberately **not** an expanded `.github` FS.GG.Drivers: `FS.GG.Drivers`
is the `.github`-authored driver bytes, and a `fs-gg-game`-owned product skill cannot ride it without a
**frozen copy** of these bytes in `.github` — the restatement ADR-0058 forbids, ADR-0062 replaces, and
ADR-0063 rules out. So FS.GG.Game publishes its own owner bytes (FS.GG.Game#449, closing the byte-SOURCE
gap under FS.GG.SDD#622 / FS-GG/.github#1308).

## What it ships

```
game-skills/skill-manifest.json        the delivered set + per-skill sha256 (the ADR-0014 record)
game-skills/skills/<id>/SKILL.md        the bytes for each mirrored:false product row
build/FS.GG.Game.Skills.props          a consumer handle: $(FsggGameSkillsContentDir) → the content root
```

Only `mirrored: false` `scope: product` rows carry bytes. A `mirrored: true` row (e.g. `fs-gg-audio`,
`fs-gg-game-core`) is listed in the manifest (the generator's single output) but its bytes are
deliberately **not** delivered here — it reaches a scaffold through FS.GG.Rendering's frozen
`--profile game` mirror (ADR-0022 §6).

## Consuming it

There is **no consumer materialize target** in this package, by design: the materialize is the SDD CLI's,
at scaffold time. `build/FS.GG.Game.Skills.props` exposes `$(FsggGameSkillsContentDir)` so the CLI's
build can locate the packed bytes; the CLI reads `skill-manifest.json`, and for each `mirrored: false`
product row whose `materializes-when` holds, lays `skills/<id>/SKILL.md` into the scaffold's skill roots
and verifies it against the recorded `sha256`. See ADR-0063 for the materializer design (FS.GG.SDD#623).

## Deriving, not restating

The delivered set lives in exactly one authored place —
`template/skill-manifest/skill-manifest.json`, emitted by `scripts/generate-skill-manifest.fsx` from the
authored `SKILL.md` bodies (ADR-0058). `stage-skills.py` reads that manifest at pack time and stages
exactly its `mirrored: false` `scope: product` rows; a skill added or retired needs no edit to this
package. `verify-package.sh` proves the packed set derives from the manifest, packs every delivered
member, and fails loud on a byte that does not match its recorded `sha256`.

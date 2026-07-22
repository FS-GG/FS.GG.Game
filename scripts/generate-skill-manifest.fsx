// (Re)generate the FS.GG.Game product skill-manifest (ADR-0014 §Decision 1 / ADR-0022 P4).
//
// Writes template/skill-manifest/skill-manifest.json: the product-scope catalog of the game
// skills FS.GG.Game owns after the ADR-0022 extraction — migrated byte-identically from
// FS.GG.Rendering (owner fs-gg-rendering → fs-gg-game; the .github registry/skills.yml reconciles
// its rows from THIS manifest, so registry = manifest = bytes). Each entry carries the SHA256 of
// its canonical SKILL.md body.
//
// Digest semantics match Fsgg.SkillMirror.sha256 and FS.GG.Rendering's generator: lowercase hex
// over the UTF-8 bytes of the body TEXT — hash(Encoding.UTF8.GetBytes(File.ReadAllText path)) — so
// a BOM never enters the digest on either the producing or the verifying side. The bytes (hence the
// sha256) are identical to the copies FS.GG.Rendering still ships from its FROZEN `--profile game`
// (the accepted two-copies cost during the extraction, ADR-0022 §6; retired by the P6 provider epic).
//
// Unlike Rendering's generator this catalog holds the ADR-0017 CANONICAL `materializes-when`
// grammar DIRECTLY (bare tokens, `in [..]`, no parens/quotes) — the grammar the .github skill-union
// gate (scripts/skill-union-assert.sh) and the typed Fsgg.Registry validator evaluate. There is no
// C-style `.template.config/template.json` to normalize from yet: the `dotnet new fs-gg-game`
// template package is deferred to the provider epic (ADR-0022 §2.1 "later"), so this catalog is the
// single source of truth until then. (When the template lands, extend this to read template.json.)
//
// Usage:
//   dotnet fsi scripts/generate-skill-manifest.fsx            # regenerate
//   dotnet fsi scripts/generate-skill-manifest.fsx --check    # exit 1 if on-disk manifest differs

open System
open System.IO
open System.Security.Cryptography
open System.Text

let repoRoot =
    let rec find dir =
        if File.Exists(Path.Combine(dir, "FS.GG.Game.slnx")) then dir
        else
            match Directory.GetParent dir |> Option.ofObj with
            | Some p -> find p.FullName
            | None -> failwith "Could not locate repository root (FS.GG.Game.slnx)."
    find __SOURCE_DIRECTORY__

let repoPath (rel: string) =
    Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar))

/// The single generated artifact this script emits, repo-relative and named ONCE — so `--list` below
/// and the writer at the foot cannot disagree about it (ADR-0044: derive the path from the generator,
/// never keep a second copy of it).
let manifestRel = "template/skill-manifest/skill-manifest.json"

// `scripts/generated-paths` roster contract (ADR-0044 / .github#498): one `kind<TAB>path<TAB>marker`
// row per emitted path — an EMPTY marker names a whole file nobody authors, which is the set
// `verify-paths` may subtract from its drift report (a worker who touches this generator regenerates
// the manifest, and §1 told them NOT to reserve it, so drift on it is a rebase, not a decision).
// Answered HERE, before a single SKILL.md body is read, so the roster call stays cheap.
if Environment.GetCommandLineArgs() |> Array.contains "--list" then
    printfn "skill-manifest\t%s\t" manifestRel
    exit 0

/// ADR-0022 §6: is FS.GG.Rendering REQUIRED to ship a byte-identical copy of this body?
/// (FS.GG.Game#280)
///
/// This is the mirror set, and it is the reason the manifest carries a `mirrored` flag at all:
/// scripts/check-skill-refs.sh READS it, to decide whether a bare `[[ref]]` in a body is an error.
/// A mirrored body is read by two gates with different publish sets, so a bare (repo-relative) ref
/// gets OPPOSITE verdicts from them — it resolves here and dangles there. Only a qualified ref
/// survives the mirror (FS.GG.Game#273/#279).
///
/// AN OBLIGATION, NOT AN OBSERVATION — and the distinction is not academic, because the obligation
/// is VIOLATED as this is written. Rendering's copies still carry the pre-#279 BARE refs while ours
/// are qualified, so not one of the four is byte-identical today; FS.GG.Rendering#714 / PR #721 are
/// the in-flight re-sync. `Mirrored` therefore says "ADR-0022 §6 requires Rendering to ship these
/// bytes", which is exactly the property the ref rule needs — a body under that obligation must be
/// written so it survives BOTH readers, whether or not the copy is currently in step. It emphatically
/// does not say "the copy is in step". Nothing in this repo could say that: Rendering's tree is not
/// visible from here, and this gate is deliberately hermetic (no network), a design FS.GG.Rendering#722
/// independently reaches for its own copy of this same script.
///
/// THE TRAP, and it is the whole subtlety of this field: `Mirrored` does NOT mean "FS.GG.Rendering
/// has a body by this name". The P6 four below — collision / grids / line-drawing / visibility — DO
/// have Rendering counterparts and are under NO such obligation: they were rewritten against the
/// .fsi and deliberately diverge (see the catalog note). Nobody promises those bytes agree, so
/// nothing forces the two gates to agree, and a bare ref in one of them is checked by exactly one
/// reader that can see the tree it names. Classify on the OBLIGATION, not on name collision. Four,
/// not eight — which is also what Rendering derives from the org registry (#722), and what its own
/// #541 got wrong.
///
/// This repo cannot VERIFY the verdict, and must not pretend to. Checking a mirror against its
/// canonical needs a reader that sees both trees — FS-GG/.github, where registry/skills.yml already
/// reconciles its rows FROM this manifest (registry = manifest = bytes), which is the direction that
/// makes this flag useful to everyone else rather than a third hand-maintained reading of one fact.
type Mirror =
    /// ADR-0022 §6 requires FS.GG.Rendering to ship these bytes — bare [[refs]] here are an error.
    | Mirrored
    /// Under no mirror obligation: no copy, or one that deliberately diverges. Bare is fine.
    | NotMirrored

// The product-scope catalog: id -> (canonical SKILL.md source, ADR-0017 canonical materializes-when,
// ADR-0022 §6 mirror verdict).
//
// THE MIRROR VERDICT IS MANDATORY, and that is the point of it being a field rather than a list
// somewhere (FS.GG.Game#280). It used to live in scripts/check-skill-refs.sh as a hardcoded
// MIRRORED_SKILLS list, and a list is a thing you can FORGET TO UPDATE — silently, and in the
// direction that hurts: a body mirrored in FUTURE was simply absent from it, so bare refs in it
// stayed legal, so it dangled in Rendering while BOTH gates reported green. That is FS.GG.Game#273
// verbatim, reintroduced by the guard written to prevent it. Here, a new row that omits the verdict
// does not COMPILE, so the question cannot be skipped — only answered.
// Four game product skills migrated from FS.GG.Rendering (ADR-0022 P4), plus fs-gg-ballistics, which
// is NOT a migration: it originates here and has no FS.GG.Rendering counterpart to stay byte-identical
// with. All gate on the simulation profiles. supplied-by is derived from the source path (dirname + "/").
//
// The ADR-0022 P6 second wave (FS.GG.Game#35) adds four more — fs-gg-collision / fs-gg-grids /
// fs-gg-line-drawing / fs-gg-visibility. These are the skills whose backing code #32/#33/#34/#38 pulled
// down into FS.GG.Game.Core (Los/Fov/Visibility/Grids/Resolution), so the guidance follows the
// implementation. Unlike the P4 four they did NOT move byte-identically: Rendering's bodies teach a
// product-owned `Collision.fs`/`Visibility.fs`/`Grids.fs`/`LineDrawing.fs` fragment, whereas the
// authoritative implementation here is a package module with a different surface. They were rewritten
// against the .fsi, so their digests deliberately differ from the frozen copies FS.GG.Rendering still
// ships from `--profile game` (the two-copies cost, ADR-0022 §6 / the `game-starter-two-copies`
// registry row, which is `coherent: false` precisely to hold divergence like this).
//
// `fs-gg-ai` (FS.GG.Game#42) is, like fs-gg-ballistics, NOT a migration: the org registry carried 38
// skills and none of them was AI, so it has no FS.GG.Rendering counterpart to stay byte-identical with.
// It originates here because it sits above the spatial substrate this repo owns.
//
// `fs-gg-effects` (FS.GG.Game#70) is the same: no FS.GG.Rendering counterpart. It is the mitigation
// half of the damage story whose region half `fs-gg-ballistics` owns. NOTE that the same change
// deprecated `Resolution.knockback` in favour of `Resolution.push`, so `fs-gg-collision`'s body — one
// of the P6 rewrites, already divergent from Rendering's frozen `--profile game` copy — changed too.
// Both the new row and that changed digest must be reconciled into .github's registry/skills.yml.
//
// `fs-gg-physics` (FS.GG.Game#79) likewise originates here — it teaches the `Loop` double step buffer
// this repo owns and the opt-in `Physics` impulse layer built on it, so there is no frozen
// FS.GG.Rendering copy to diverge from. Adding it takes the owner:fs-gg-game row count to 12, which the
// .github registry/skills.yml must gain as a NEW row; that reconcile is a cross-repo follow-up
// (registry = manifest = bytes), deliberately outside this repo's touch-set. It is the second such row
// pending, after fs-gg-effects above — see FS-GG/.github#330 and #328.
let catalog =
    [ "fs-gg-ai", "template/product-skills/fs-gg-ai/SKILL.md", "profile in [game, sample-pack]", NotMirrored
      // NOT [game, sample-pack] (FS.GG.Game#204). Audio is the ONE product skill here that is not
      // simulation-only: FS.GG.Rendering#436 widened it to every profile that opens a viewer window,
      // and so to every profile that can make a sound. Rendering's template.json — the only thing
      // that actually materializes these bodies today — gates it on
      // `(profile == "app" || profile == "sample-pack" || profile == "game")`, and its generator
      // normalizes that condition into the row below. This catalog is hand-declared (there is no
      // `dotnet new fs-gg-game` template to read yet), so it can — and did — drift away from the
      // materialization it describes. Keep it equal to Rendering's condition, not to its neighbours.
      "fs-gg-audio", "template/product-skills/fs-gg-audio/SKILL.md", "profile in [app, sample-pack, game]", NotMirrored
      "fs-gg-ballistics", "template/product-skills/fs-gg-ballistics/SKILL.md", "profile in [game, sample-pack]", NotMirrored
      "fs-gg-collision", "template/product-skills/fs-gg-collision/SKILL.md", "profile in [game, sample-pack]", NotMirrored
      "fs-gg-effects", "template/product-skills/fs-gg-effects/SKILL.md", "profile in [game, sample-pack]", NotMirrored
      "fs-gg-game-core", "template/product-skills/fs-gg-game-core/SKILL.md", "profile in [game, sample-pack]", NotMirrored
      "fs-gg-grids", "template/product-skills/fs-gg-grids/SKILL.md", "profile in [game, sample-pack]", NotMirrored
      "fs-gg-line-drawing", "template/product-skills/fs-gg-line-drawing/SKILL.md", "profile in [game, sample-pack]", NotMirrored
      // fs-gg-mapgen (FS.GG.Game#027 / M1 of the procedural map generation design) is, like fs-gg-ai and
      // fs-gg-ballistics, NOT a migration: the org registry had no map-generation skill, so it has no
      // FS.GG.Rendering counterpart to stay byte-identical with. It originates here because the seeded,
      // integer, byte-deterministic MapGen substrate sits in FS.GG.Game.Core. The .github registry/
      // skills.yml must gain this as a NEW owner:fs-gg-game row (registry = manifest = bytes), an M6
      // cross-repo follow-up like fs-gg-effects/fs-gg-physics before it.
      "fs-gg-mapgen", "template/product-skills/fs-gg-mapgen/SKILL.md", "profile in [game, sample-pack]", NotMirrored
      "fs-gg-model-swap", "template/product-skills/fs-gg-model-swap/SKILL.md", "profile in [game, sample-pack]", NotMirrored
      "fs-gg-persistence", "template/product-skills/fs-gg-persistence/SKILL.md", "profile in [game, sample-pack]", NotMirrored
      "fs-gg-playtest", "template/product-skills/fs-gg-playtest/SKILL.md", "profile in [game, sample-pack]", NotMirrored
      "fs-gg-physics", "template/product-skills/fs-gg-physics/SKILL.md", "profile in [game, sample-pack]", NotMirrored
      "fs-gg-visibility", "template/product-skills/fs-gg-visibility/SKILL.md", "profile in [game, sample-pack]", NotMirrored ]

/// Provider source directory (trailing slash) that holds the canonical SKILL.md — supplied-by.
let suppliedByOf (source: string) : string =
    source.Substring(0, source.LastIndexOf '/') + "/"

/// Minimal JSON string escape.
let jsonEscape (s: string) : string =
    s.Replace("\\", "\\\\").Replace("\"", "\\\"")

let sha256Text (body: string) : string =
    Encoding.UTF8.GetBytes body
    |> SHA256.HashData
    |> Array.map (fun b -> b.ToString "x2")
    |> String.concat ""

// A duplicated id would emit two rows for one skill, and if their verdicts disagreed the gate would
// silently take the MIRRORED one (`select(.mirrored == true)` matches the row that says yes) — a
// contradiction in the catalog resolving itself, quietly, in a file whose entire job since #280 is to
// state the verdict out loud. Cheap to make impossible, so it is.
let duplicateIds =
    catalog
    |> List.countBy (fun (id, _, _, _) -> id)
    |> List.filter (fun (_, n) -> n > 1)
    |> List.map fst

if not duplicateIds.IsEmpty then
    eprintfn "skill-manifest: duplicate catalog id(s): %s" (String.concat ", " duplicateIds)
    exit 1

let manifestJson =
    let entries =
        catalog
        |> List.sortBy (fun (id, _, _, _) -> id)
        |> List.map (fun (id, source, condition, mirror) ->
            let body = File.ReadAllText(repoPath source)
            let mirrored = match mirror with | Mirrored -> "true" | NotMirrored -> "false"

            sprintf
                "    {\n      \"id\": \"%s\",\n      \"scope\": \"product\",\n      \"sha256\": \"%s\",\n      \"mirrored\": %s,\n      \"resolvablePath\": \".agents/skills/%s/SKILL.md\",\n      \"materializes-when\": \"%s\",\n      \"supplied-by\": \"%s\"\n    }"
                id (sha256Text body) mirrored id (jsonEscape condition) (jsonEscape (suppliedByOf source)))
        |> String.concat ",\n"

    sprintf "{\n  \"schemaVersion\": 1,\n  \"skills\": [\n%s\n  ]\n}\n" entries

let manifestPath = repoPath manifestRel
let check = Environment.GetCommandLineArgs() |> Array.contains "--check"

if check then
    let current = if File.Exists manifestPath then File.ReadAllText manifestPath else ""

    if current = manifestJson then
        printfn "skill-manifest: up to date (%d skills)" catalog.Length
        exit 0
    else
        eprintfn "skill-manifest: STALE — run `dotnet fsi scripts/generate-skill-manifest.fsx`"
        exit 1
else
    Directory.CreateDirectory(Path.GetDirectoryName manifestPath) |> ignore
    File.WriteAllText(manifestPath, manifestJson)
    printfn "skill-manifest: wrote %s (%d skills)" manifestPath catalog.Length

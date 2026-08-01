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
// a BOM never enters the digest on either the producing or the verifying side.
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

/// Mirror classification retired (FS.GG.Game#540 / FS-GG/.github#1862). Rendering no longer
/// ships second copies; every catalog row is owner-authored here and delivered by FS.GG.Game.Skills.

// The product-scope catalog: id -> (canonical SKILL.md source, ADR-0017 canonical
// materializes-when). All rows are owner-authored in this repository and delivered by
// FS.GG.Game.Skills. `supplied-by` is derived from the source path, so adding a product row here is
// sufficient to include it in the generated manifest and package without joining another set.
let catalog =
    [ "fs-gg-ai", "template/product-skills/fs-gg-ai/SKILL.md", "profile in [game, sample-pack]"
      // NOT [game, sample-pack] (FS.GG.Game#204). Audio is the ONE product skill here that is not
      // simulation-only: FS.GG.Rendering#436 established that it applies to every profile that opens
      // a viewer window, and so to every profile that can make a sound. The retired Rendering
      // template path gated it on
      // `(profile == "app" || profile == "sample-pack" || profile == "game")`, and its generator
      // normalizes that condition into the row below. This catalog is hand-declared (there is no
      // `dotnet new fs-gg-game` template to read yet), so it can — and did — drift away from the
      // materialization it describes. Keep the historical applicability condition, not its neighbours'.
      "fs-gg-audio", "template/product-skills/fs-gg-audio/SKILL.md", "profile in [app, sample-pack, game]"
      "fs-gg-ballistics", "template/product-skills/fs-gg-ballistics/SKILL.md", "profile in [game, sample-pack]"
      "fs-gg-collision", "template/product-skills/fs-gg-collision/SKILL.md", "profile in [game, sample-pack]"
      "fs-gg-effects", "template/product-skills/fs-gg-effects/SKILL.md", "profile in [game, sample-pack]"
      "fs-gg-game-fable", "template/product-skills/fs-gg-game-fable/SKILL.md", "profile in [game, sample-pack]"
      "fs-gg-game-core", "template/product-skills/fs-gg-game-core/SKILL.md", "profile in [game, sample-pack]"
      "fs-gg-grids", "template/product-skills/fs-gg-grids/SKILL.md", "profile in [game, sample-pack]"
      "fs-gg-line-drawing", "template/product-skills/fs-gg-line-drawing/SKILL.md", "profile in [game, sample-pack]"
      // fs-gg-mapcraft (FS.GG.Game map construction & analysis; renamed from fs-gg-mapgen in M7,
      // work/033) is, like fs-gg-ai and fs-gg-ballistics, NOT a migration: the org registry had no
      // map-construction skill, so it has no FS.GG.Rendering counterpart. It originates here because the
      // seeded MapGen generators + the producer-agnostic MapAnalysis machinery sit in FS.GG.Game.Core.
      // The .github registry/skills.yml gains this as a NEW owner:fs-gg-game row (registry = manifest =
      // bytes) — the cross-repo follow-up FS-GG/.github#1355 (originally filed as fs-gg-mapgen, renamed).
      "fs-gg-mapcraft", "template/product-skills/fs-gg-mapcraft/SKILL.md", "profile in [game, sample-pack]"
      "fs-gg-model-swap", "template/product-skills/fs-gg-model-swap/SKILL.md", "profile in [game, sample-pack]"
      "fs-gg-persistence", "template/product-skills/fs-gg-persistence/SKILL.md", "profile in [game, sample-pack]"
      "fs-gg-playtest", "template/product-skills/fs-gg-playtest/SKILL.md", "profile in [game, sample-pack]"
      "fs-gg-physics", "template/product-skills/fs-gg-physics/SKILL.md", "profile in [game, sample-pack]"
      "fs-gg-visibility", "template/product-skills/fs-gg-visibility/SKILL.md", "profile in [game, sample-pack]" ]

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

// A duplicated id would emit two rows for one skill. Cheap to make impossible, so it is.
let duplicateIds =
    catalog
    |> List.countBy (fun (id, _, _) -> id)
    |> List.filter (fun (_, n) -> n > 1)
    |> List.map fst

if not duplicateIds.IsEmpty then
    eprintfn "skill-manifest: duplicate catalog id(s): %s" (String.concat ", " duplicateIds)
    exit 1

let manifestJson =
    let entries =
        catalog
        |> List.sortBy (fun (id, _, _) -> id)
        |> List.map (fun (id, source, condition) ->
            let body = File.ReadAllText(repoPath source)

            sprintf
                "    {\n      \"id\": \"%s\",\n      \"scope\": \"product\",\n      \"sha256\": \"%s\",\n      \"resolvablePath\": \".agents/skills/%s/SKILL.md\",\n      \"materializes-when\": \"%s\",\n      \"supplied-by\": \"%s\"\n    }"
                id (sha256Text body) id (jsonEscape condition) (jsonEscape (suppliedByOf source)))
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

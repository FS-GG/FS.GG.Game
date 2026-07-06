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

// The product-scope catalog: id -> (canonical SKILL.md source, ADR-0017 canonical materializes-when).
// The four game product skills migrated from FS.GG.Rendering (ADR-0022 P4); all four gate on the
// simulation profiles. supplied-by is derived from the source path (dirname + "/").
let catalog =
    [ "fs-gg-audio", "template/product-skills/fs-gg-audio/SKILL.md", "profile in [game, sample-pack]"
      "fs-gg-game-core", "template/product-skills/fs-gg-game-core/SKILL.md", "profile in [game, sample-pack]"
      "fs-gg-model-swap", "template/product-skills/fs-gg-model-swap/SKILL.md", "profile in [game, sample-pack]"
      "fs-gg-persistence", "template/product-skills/fs-gg-persistence/SKILL.md", "profile in [game, sample-pack]" ]

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

let manifestPath = repoPath "template/skill-manifest/skill-manifest.json"
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

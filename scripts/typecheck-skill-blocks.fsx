// Typecheck every ```fsharp block in the published product skills (FS.GG.Game#141).
//
// The gap this closes. The skill bodies were gated on everything EXCEPT the thing that kept
// breaking: check-skill-refs.sh validates that [[refs]] resolve, the skill-manifest-drift job
// validates that the sha256 digests match, and the `fsharp` code — the part a reader actually
// copies into their product — was only ever READ. A skill could ship an example that cannot
// compile and every gate stayed green. That is the silent-no-op shape (FS-GG/.github#416) one
// level up: a gate reporting success over a subject it never examined. The same defect was found
// four times by hand (#129, #132, #140) and zero times by CI.
//
// What it does. Each block is extracted, prepended with a fixture, and compiled against the REAL
// built FS.GG.Game.Core — not a reconstruction of it. A compiler error is a gate failure, reported
// against the SKILL.md line the reader would copy from.
//
// Two things make that tractable, because the blocks are SKETCHES, not programs:
//
//   1. `module rec`. A block declares its own types (`type Creep = { Pos: Geometry.Vec2 ... }`)
//      and then uses free values of those types (`creeps`). A fixture that merely PREPENDS cannot
//      bind `creeps : Creep list` — `Creep` does not exist yet. In a recursive module it can:
//      declarations are mutually visible regardless of order, so the fixture forward-references
//      the type the block declares below it, and the block is still compiled VERBATIM against its
//      OWN declarations. No mutation of the subject, no duplicate type standing in for it.
//
//   2. `# <line> "<file>"` line directives. The compiler reports errors against the SKILL.md line
//      the block came from, so a failure reads `SKILL.md(226,59): error FS0001: ... expected
//      'Point' but here has type 'Geometry.Vec2'` — the reader is pointed at the prose they must
//      fix, not at a generated file they have never seen.
//
// The scaffold context. `Geometry.Vec2` (Vx/Vy) lives in the generated PRODUCT
// (src/<ProductDir>/Vec2.fs, owned by FS.GG.Templates), not in FS.GG.Game.Core, so this repo has
// no copy to compile against. skill-block-context/_scaffold.fs RECONSTRUCTS it, and that
// reconstruction is a contract with a repo we do not own — see the header of that file.
//
// Fail-closed. A gate that reports green over a subject it never compiled is the bug this exists
// to kill, so the harness refuses to pass unless it can prove it looked: no SKILL.md files, no
// blocks, a block count that disagrees with an independent fence count, a missing FS.GG.Game.Core
// build, or a Compile-item count that disagrees with the block count are all hard failures. Skips
// require a written reason and are printed on every run.
//
// Usage:
//   dotnet fsi scripts/typecheck-skill-blocks.fsx            # typecheck (this is the gate)
//   dotnet fsi scripts/typecheck-skill-blocks.fsx --list     # list the blocks, compile nothing
//   dotnet fsi scripts/typecheck-skill-blocks.fsx --keep     # leave the generated project on disk
//
// The build is Debug by default; override with --configuration Release.

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.RegularExpressions

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

let relative (full: string) =
    Path.GetRelativePath(repoRoot, full).Replace(Path.DirectorySeparatorChar, '/')

let argv = fsi.CommandLineArgs |> Array.skip 1
let hasFlag f = argv |> Array.contains f
let listOnly = hasFlag "--list"
let keepGenerated = hasFlag "--keep"

let configuration =
    match argv |> Array.tryFindIndex ((=) "--configuration") with
    | Some i when i + 1 < argv.Length -> argv[i + 1]
    | _ -> "Debug"

/// A GitHub Actions annotation, so a failure lands on the diff instead of only in the log.
let annotate level (file: string) (line: int) (col: int) (msg: string) =
    // Annotation messages must not carry raw newlines.
    let flat = Regex.Replace(msg, @"\s*\r?\n\s*", " ")
    printfn "::%s file=%s,line=%d,col=%d::%s" level file line col flat

let fail (msg: string) =
    printfn "::error::%s" msg
    eprintfn "typecheck-skill-blocks: %s" msg
    exit 1

// ---------------------------------------------------------------------------------------------
// 1. Extract
// ---------------------------------------------------------------------------------------------

type Block =
    { Skill: string          // registry skill id, e.g. fs-gg-game-core
      SourceFile: string     // absolute path to the SKILL.md
      Ordinal: int           // 1-based index of the block WITHIN its SKILL.md — the fixture key
      StartLine: int         // 1-based SKILL.md line of the block's FIRST CODE LINE (after the fence)
      Code: string }

/// Blocks are fenced with an exactly-``` ```fsharp `` opener at column 0 and a ``` closer. The
/// skills use no nested/indented fences and no other fence language (asserted below), so a line
/// scanner is honest here and a markdown parser would be ceremony.
let extractBlocks (skillFile: string) : Block list =
    let skill = Path.GetFileName(Path.GetDirectoryName skillFile)
    let lines = File.ReadAllLines skillFile
    let blocks = ResizeArray<Block>()
    let mutable i = 0
    let mutable ordinal = 0
    while i < lines.Length do
        if lines[i].TrimEnd() = "```fsharp" then
            let openFence = i                       // 0-based
            let body = ResizeArray<string>()
            let mutable j = i + 1
            while j < lines.Length && lines[j].TrimEnd() <> "```" do
                body.Add lines[j]
                j <- j + 1
            if j >= lines.Length then
                fail $"{relative skillFile}: unterminated ```fsharp fence opened at line {openFence + 1}."
            ordinal <- ordinal + 1
            blocks.Add
                { Skill = skill
                  SourceFile = skillFile
                  Ordinal = ordinal
                  StartLine = openFence + 2         // 1-based line of the first code line
                  Code = String.Join("\n", body) }
            i <- j + 1
        else
            i <- i + 1
    List.ofSeq blocks

let skillFiles =
    let dir = repoPath "template/product-skills"
    if not (Directory.Exists dir) then
        fail $"template/product-skills does not exist — nothing to typecheck. Refusing to pass."
    Directory.GetFiles(dir, "SKILL.md", SearchOption.AllDirectories) |> Array.sort

if skillFiles.Length = 0 then
    fail "found 0 SKILL.md files under template/product-skills — the extractor is broken or the \
          skills moved. Refusing to report success over a subject I never examined."

let blocks = skillFiles |> Array.collect (extractBlocks >> Array.ofList) |> List.ofArray

// Independent cross-check of the extractor. A regression that silently stops SEEING blocks would
// otherwise sail through green — the exact failure this gate exists to prevent, reproduced inside
// the gate itself. Count the fence-open lines the dumb way and demand agreement.
let independentFenceCount =
    skillFiles
    |> Array.sumBy (fun f -> File.ReadAllLines f |> Array.filter (fun l -> l.TrimEnd() = "```fsharp") |> Array.length)

if blocks.Length <> independentFenceCount then
    fail $"extractor disagreement: parsed {blocks.Length} blocks but counted {independentFenceCount} \
           ```fsharp fences. The extractor is dropping blocks — fix it before trusting this gate."

if blocks.Length = 0 then
    fail "found 0 ```fsharp blocks across the product skills. Refusing to pass: a gate with no \
          subject is a green light over nothing."

// ---------------------------------------------------------------------------------------------
// 2. Fixtures
// ---------------------------------------------------------------------------------------------
//
// A fixture supplies the free values a sketch leaves unbound (`creeps`, `walls`, `cellPx`, …).
// One file per skill, in scripts/skill-block-context/<skill-id>.fs, split into sections:
//
//     //#block 6
//     let cellPx = 32.0
//     let creeps : Creep list = []     // forward-references the block's own type — `module rec`
//
//     //#block 7
//     //#skip <why this block cannot be compiled>
//
// A block with no section gets an EMPTY fixture and is compiled as-is: a self-contained block
// needs no ceremony, and a sketch that needs bindings fails loudly with an unbound-identifier
// error naming exactly what to add. Absence is never a skip.

type Fixture =
    | Context of recursive: bool * text: string   // F# text prepended to the block, in its module
    | Skipped of reason: string                   // printed on every run, never silent

let fixtureDir = repoPath "scripts/skill-block-context"

let loadFixtures (skill: string) : Map<int, Fixture> =
    let path = Path.Combine(fixtureDir, skill + ".fs")
    if not (File.Exists path) then Map.empty
    else
        let mutable current = None
        let acc = System.Collections.Generic.Dictionary<int, ResizeArray<string>>()
        let skips = System.Collections.Generic.Dictionary<int, string>()
        let recs = System.Collections.Generic.HashSet<int>()
        for line in File.ReadAllLines path do
            let m = Regex.Match(line, @"^//#block\s+(\d+)\s*$")
            if m.Success then
                let n = int m.Groups[1].Value
                // A second section for the same block would silently REPLACE the first, dropping
                // bindings the author wrote and then failing the block with a confusing
                // unbound-identifier error while the binding sits right there in this file.
                if acc.ContainsKey n then
                    fail $"scripts/skill-block-context/{skill}.fs declares //#block {n} twice. \
                           The second section would silently discard the first — merge them."
                current <- Some n
                acc[n] <- ResizeArray()
            else
                match current with
                | None -> ()    // file header, before the first //#block — ignored
                | Some n ->
                    let s = Regex.Match(line, @"^//#skip\s+(.+)$")
                    if s.Success then skips[n] <- s.Groups[1].Value.Trim()
                    elif Regex.IsMatch(line, @"^//#rec\s*$") then recs.Add n |> ignore
                    else acc[n].Add line
        let fixtures =
            acc
            |> Seq.map (fun kv ->
                match skips.TryGetValue kv.Key with
                | true, reason -> kv.Key, Skipped reason
                | _ -> kv.Key, Context(recs.Contains kv.Key, String.Join("\n", kv.Value).Trim()))
            |> Map.ofSeq
        // A //#block section keyed to a block that does not exist is a stale fixture — usually a
        // block was deleted or reordered. Say so; a silently-ignored fixture is a lie in a file
        // whose whole job is to be true.
        let ordinals = blocks |> List.filter (fun b -> b.Skill = skill) |> List.map _.Ordinal |> Set.ofList
        for KeyValue(n, _) in acc do
            if not (ordinals.Contains n) then
                fail $"scripts/skill-block-context/{skill}.fs declares //#block {n}, but \
                       {skill} has only {ordinals.Count} ```fsharp blocks. Stale fixture — a block \
                       was deleted or reordered; re-key the fixture."
        fixtures

let fixturesBySkill =
    blocks |> List.map _.Skill |> List.distinct |> List.map (fun s -> s, loadFixtures s) |> Map.ofList

let fixtureFor (b: Block) =
    fixturesBySkill[b.Skill] |> Map.tryFind b.Ordinal

let scaffoldFile = Path.Combine(fixtureDir, "_scaffold.fs")
if not (File.Exists scaffoldFile) then
    fail "scripts/skill-block-context/_scaffold.fs is missing — it reconstructs the generated \
          product's Geometry.Vec2, which the skills teach against and FS.GG.Game.Core does not ship."

// ---------------------------------------------------------------------------------------------
// 3. Report / --list
// ---------------------------------------------------------------------------------------------

let compiled, skipped =
    blocks |> List.partition (fun b -> match fixtureFor b with Some(Skipped _) -> false | _ -> true)

printfn "typecheck-skill-blocks: %d ```fsharp blocks across %d skills" blocks.Length skillFiles.Length
for skill in blocks |> List.map _.Skill |> List.distinct do
    let n = blocks |> List.filter (fun b -> b.Skill = skill) |> List.length
    printfn "  %-24s %2d block(s)" skill n

// Skips are printed on EVERY run, as warnings, whether or not anything failed. An unexamined block
// that nobody is reminded about is how the gate rots back into theatre.
for b in skipped do
    match fixtureFor b with
    | Some(Skipped reason) ->
        annotate "warning" (relative b.SourceFile) b.StartLine 1
            $"skill-block typecheck SKIPPED (block {b.Ordinal} of {b.Skill}): {reason}"
    | _ -> ()

if not skipped.IsEmpty then
    printfn "typecheck-skill-blocks: %d block(s) SKIPPED by an explicit //#skip, %d compiled."
        skipped.Length compiled.Length

if listOnly then
    for b in blocks do
        let state =
            match fixtureFor b with
            | Some(Skipped r) -> $"SKIP ({r})"
            | Some(Context _) -> "compile (with fixture)"
            | None -> "compile (self-contained)"
        printfn "  %s block %d @ %s:%d — %s" b.Skill b.Ordinal (relative b.SourceFile) b.StartLine state
    exit 0

if compiled.IsEmpty then
    fail "every block is skipped — this gate would compile nothing and report green. Refusing."

// ---------------------------------------------------------------------------------------------
// 4. Generate the project
// ---------------------------------------------------------------------------------------------

// Typecheck against the REAL built assembly, exactly as a product consumer would bind to the
// package. If it is absent we do NOT quietly fall back to a source reference or a stub: a gate
// that "passes" against a subject that was never built is the defect, not a convenience.
let coreDll =
    repoPath $"src/Game.Core/bin/{configuration}/net10.0/FS.GG.Game.Core.dll"

if not (File.Exists coreDll) then
    fail $"FS.GG.Game.Core.dll not found at {relative coreDll} — build it first \
           (dotnet build src/Game.Core/FS.GG.Game.Core.fsproj -c {configuration}). Refusing to \
           typecheck the skills against an assembly that does not exist."

let outDir = Path.Combine(Path.GetTempPath(), $"fsgg-skill-typecheck-{Guid.NewGuid():N}")
Directory.CreateDirectory outDir |> ignore

let moduleName (b: Block) =
    let sanitized = Regex.Replace(b.Skill, @"[^A-Za-z0-9]", "_")
    $"FsGg.SkillCheck.Generated.{sanitized}_{b.Ordinal}"

/// One file per block: the fixture, then the block VERBATIM behind a line directive that re-anchors
/// the compiler on the SKILL.md.
///
/// Two structural adjustments, and they are the only two:
///
///   * The block's own `open` lines are HOISTED above the fixture and BLANKED where they stood, so
///     the block keeps its exact line count (and so its errors keep pointing at the right SKILL.md
///     line). Hoisting is safe — an `open` cannot carry a type error — and it is required: in a
///     recursive module F# demands every `open` come first (FS3200). Our own opens then follow, with
///     the SCAFFOLD LAST, because that is the order a real product sees: the product's `Geometry`
///     (Vec2) is opened after `FS.GG.Game.Core`, and F# MERGES the two same-named modules. Getting
///     this order wrong would resolve `Geometry.Vec2` against the wrong half of the merge.
///
///   * `module rec` only when the fixture asks for it (//#rec). A recursive module is what lets a
///     fixture forward-reference a type the block itself declares (`creeps : Creep list`), but it
///     also forbids things plain F# allows — notably tuple-pattern bindings at module level
///     (`let a, b = Grids.edgeSegment spec wall`, FS0873). So plain module is the default and
///     recursion is opted into by the three fixtures that genuinely need it.
let generateBlockFile (b: Block) =
    let fixture = fixtureFor b
    let isRec = match fixture with Some(Context(true, _)) -> true | _ -> false

    // Hoist the block's TOP-LEVEL opens, preserving the block's line count by blanking them in
    // place. Column-0 only, deliberately: an INDENTED `open` belongs to a nested scope, and lifting
    // it to the top of the file would widen its scope and change name resolution — the gate would
    // then typecheck a program that resolves differently from the one the reader pastes. An
    // indented open is left exactly where it is (F# accepts it there in a non-recursive module).
    let isTopLevelOpen (l: string) = Regex.IsMatch(l, @"^open\s+\S+")
    let codeLines = b.Code.Split('\n')
    let blockOpens = codeLines |> Array.filter isTopLevelOpen |> Array.map _.Trim()
    let body =
        codeLines
        |> Array.map (fun l -> if isTopLevelOpen l then "" else l)
        |> String.concat "\n"

    // ...but a nested `open` inside a block we are about to compile as a RECURSIVE module is a hard
    // error (FS3200: in a recursive group every `open` must come first), and hoisting it is exactly
    // what we just refused to do. Say so plainly rather than emitting a file that cannot compile.
    if isRec && codeLines |> Array.exists (fun l -> Regex.IsMatch(l, @"^\s+open\s+\S+")) then
        fail $"{relative b.SourceFile} block {b.Ordinal} has an INDENTED `open` and its fixture asks \
               for //#rec. F# forbids that (FS3200), and hoisting the open out of its scope would \
               change name resolution. Drop the //#rec, or bind the free value without needing it."

    let sb = StringBuilder()
    let recKeyword = if isRec then "rec " else ""
    sb.AppendLine($"module {recKeyword}{moduleName b}").AppendLine() |> ignore
    for o in Array.distinct blockOpens do
        sb.AppendLine(o) |> ignore
    sb.AppendLine("open FS.GG.Game.Core") |> ignore
    sb.AppendLine("open FsGg.SkillCheck.Scaffold") |> ignore   // LAST — see the note above
    sb.AppendLine() |> ignore

    match fixture with
    | Some(Context(_, ctx)) when ctx <> "" ->
        sb.AppendLine($"// fixture: scripts/skill-block-context/{b.Skill}.fs //#block {b.Ordinal}") |> ignore
        sb.AppendLine(ctx).AppendLine() |> ignore
    | _ -> ()

    // `# N "file"` sets the NEXT physical line to line N of `file`, so errors in the block are
    // reported against the SKILL.md the reader is holding.
    let anchor = b.SourceFile.Replace(@"\", "/")
    sb.AppendLine($"# {b.StartLine} \"{anchor}\"") |> ignore
    sb.AppendLine(body) |> ignore

    let stem = Regex.Replace(b.Skill, @"[^A-Za-z0-9]", "_")
    let file = Path.Combine(outDir, $"{stem}_{b.Ordinal}.fs")
    File.WriteAllText(file, sb.ToString())
    file

let scaffoldCopy = Path.Combine(outDir, "_scaffold.fs")
File.Copy(scaffoldFile, scaffoldCopy)

let blockFiles = compiled |> List.map generateBlockFile
let compileItems = scaffoldCopy :: blockFiles

// Every generated file must actually EXIST on disk and be non-empty. (Counting `compileItems`
// against `compiled` would be a tautology — the list is built by mapping over `compiled`, so its
// length always agrees and the check could never fire. The disk is the independent witness.)
for f in compileItems do
    if not (File.Exists f) then
        fail $"generator did not write {Path.GetFileName f} — it would have been compiled as nothing."
    if (FileInfo f).Length = 0L then
        fail $"generator wrote an EMPTY {Path.GetFileName f} — an empty file compiles clean and \
               would pass this gate over a block it never examined."

let projectXml =
    let items =
        compileItems
        |> List.map (fun f -> $"""    <Compile Include="{Path.GetFileName f}" />""")
        |> String.concat "\n"
    $"""<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Library</OutputType>
    <!-- Generated, transient: no lockfile, and so no NU1403 against the committed FSharp.Core pin
         (ADR-0032). The repo's Directory.Build.props / CPM are kept out by the empty
         Directory.Build.props+targets written beside this file — MSBuild stops its upward search at
         the first one it finds, and that is the only thing that actually stops the import (setting
         ImportDirectoryBuildProps here would be too late: Sdk.props imports them before this
         PropertyGroup is ever evaluated). -->
    <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <!-- FS0040: `module rec` warns that a forward reference is initialisation-checked at runtime.
         That is exactly the mechanism the fixture relies on and it is orthogonal to TYPE errors,
         which are what this gate reads. FS1182 (unused) is noise on a sketch. Nothing here
         suppresses a type error. -->
    <NoWarn>$(NoWarn);40;1182</NoWarn>
    <WarningLevel>0</WarningLevel>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
{items}
  </ItemGroup>

  <ItemGroup>
    <Reference Include="FS.GG.Game.Core">
      <HintPath>{coreDll}</HintPath>
    </Reference>
  </ItemGroup>

</Project>
"""

// Terminate MSBuild's upward search for Directory.Build.props/.targets right here, so the repo's
// shared build config and CPM can never be imported into this throwaway project (they would fail
// its implicit FSharp.Core reference under ManagePackageVersionsCentrally). The generated project
// normally lives under the system temp dir with nothing above it — but TMPDIR is not ours to
// assume, and this makes the isolation a property of the harness rather than of the environment.
let emptyProps = """<Project></Project>"""
File.WriteAllText(Path.Combine(outDir, "Directory.Build.props"), emptyProps)
File.WriteAllText(Path.Combine(outDir, "Directory.Build.targets"), emptyProps)

let projectFile = Path.Combine(outDir, "SkillBlocks.fsproj")
File.WriteAllText(projectFile, projectXml)

// Read the project back and count the Compile items the COMPILER will actually see. This is the
// coverage assertion that matters: everything upstream of it is our own bookkeeping agreeing with
// itself, whereas this is the emitted artefact agreeing with the block list. One Compile item per
// compiled block, plus the scaffold — anything else means the gate is about to examine less than
// it claims to.
let emittedCompileItems = Regex.Matches(File.ReadAllText projectFile, @"<Compile Include=").Count
if emittedCompileItems <> compiled.Length + 1 then
    fail $"the generated project has {emittedCompileItems} Compile item(s) for {compiled.Length} \
           block(s) + 1 scaffold. The gate would compile less than it reports — refusing."

// ---------------------------------------------------------------------------------------------
// 5. Compile
// ---------------------------------------------------------------------------------------------

let run (fileName: string) (args: string) =
    let psi = ProcessStartInfo(fileName, args, RedirectStandardOutput = true, RedirectStandardError = true)
    use p = Process.Start psi
    // Drain both pipes CONCURRENTLY. Reading one to completion before touching the other deadlocks
    // as soon as the child fills the other pipe's buffer (~64 KB) — which a cascading compile
    // failure across 40+ generated files will do. The gate would then hang to its job timeout and
    // report an infrastructure failure instead of the type error it actually found.
    let stdout = p.StandardOutput.ReadToEndAsync()
    let stderr = p.StandardError.ReadToEndAsync()
    p.WaitForExit()
    p.ExitCode, stdout.Result + stderr.Result

printfn "typecheck-skill-blocks: compiling %d block(s) against %s" compiled.Length (relative coreDll)

let exitCode, output = run "dotnet" $"build \"{projectFile}\" -c {configuration} --nologo -v minimal"

if not keepGenerated then
    try Directory.Delete(outDir, true) with _ -> ()
else
    printfn "typecheck-skill-blocks: generated project kept at %s" outDir

// Compiler diagnostics come back anchored on the SKILL.md (the line directive did that), so we can
// re-emit them as annotations on the real file. Anything anchored on a generated .fs instead means
// the FIXTURE is broken, not the doc — say which, or the author debugs the wrong file.
let diagnostics =
    Regex.Matches(output, @"^(?<file>[^\s(].*?)\((?<line>\d+),(?<col>\d+)\):\s*(?<lvl>error|warning)\s+(?<code>FS\d+):\s*(?<msg>.*)$",
                  RegexOptions.Multiline)
    |> Seq.map (fun m ->
        m.Groups["file"].Value, int m.Groups["line"].Value, int m.Groups["col"].Value,
        m.Groups["lvl"].Value, m.Groups["code"].Value, m.Groups["msg"].Value.Trim())
    |> Seq.distinct
    |> List.ofSeq

let errors = diagnostics |> List.filter (fun (_, _, _, lvl, _, _) -> lvl = "error")

if exitCode = 0 && errors.IsEmpty then
    printfn ""
    printfn "typecheck-skill-blocks: OK — %d block(s) typecheck against FS.GG.Game.Core." compiled.Length
    exit 0

printfn ""
for (file, line, col, _, code, msg) in errors do
    let isSkillDoc = file.EndsWith "SKILL.md"
    let shown = if Path.IsPathRooted file then relative file else file
    if isSkillDoc then
        annotate "error" shown line col $"{code}: {msg}"
        printfn "  %s:%d:%d  %s: %s" shown line col code msg
    else
        // Anchored on a generated file => the fixture (or the scaffold) does not compile.
        annotate "error" $"scripts/skill-block-context" 1 1
            $"fixture/scaffold does not compile ({Path.GetFileName file}:{line}): {code}: {msg}"
        printfn "  [fixture] %s:%d:%d  %s: %s" (Path.GetFileName file) line col code msg

if errors.IsEmpty then
    // Non-zero exit with no parsed diagnostic: never swallow it.
    printfn "%s" output
    fail $"the block compilation failed (exit {exitCode}) but produced no parseable diagnostic — \
           see the raw build output above."

printfn ""
fail $"{errors.Length} error(s): a ```fsharp block in a published skill does not typecheck against \
       FS.GG.Game.Core. Readers copy these blocks into their product — fix the skill body (or, if \
       the block is a sketch missing a binding, add it to scripts/skill-block-context/<skill>.fs)."

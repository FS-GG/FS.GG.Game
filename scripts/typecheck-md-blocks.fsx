// Typecheck every ```fsharp block in the published markdown corpora (FS.GG.Game#141, #149).
//
// The gap this closes. The docs were gated on everything EXCEPT the thing that kept breaking:
// check-skill-refs.sh validates that [[refs]] resolve, the skill-manifest-drift job validates that
// the sha256 digests match, and the `fsharp` code — the part a reader actually copies into their
// product — was only ever READ. A doc could ship an example that cannot compile and every gate
// stayed green. That is the silent-no-op shape (FS-GG/.github#416) one level up: a gate reporting
// success over a subject it never examined. The same defect was found four times by hand (#129,
// #132, #140, #144) and zero times by CI.
//
// TWO CORPORA (see `corpora` below):
//
//   skills     template/product-skills/**/SKILL.md               — independent examples
//   testspecs  docs/TestSpecs/Games/*.md + docs/TestSpecTutorial.md — a CUMULATIVE model per document
//
// #141 gated the first. #149 gated the second, and the difference between them is the one real
// design point here: a TestSpec's blocks are NOT independent examples, they are one model built up
// across a document. Pong's `Model` block says `Left: Paddle`, and `Paddle` was declared three
// blocks earlier. Compiling each block in isolation — which is right for a skill, where two blocks
// may each declare their own `Creep` — would fail on nearly every TestSpec `Model`. So a CUMULATIVE
// corpus emits one file per block in document order and has each block `open` the blocks before it.
// That is the reader's mental model made literal, and it is what makes the gate worth having:
// `Model` binds to the ACTUAL `Paddle` the document declares, so the document is checked against
// ITSELF rather than against a fixture's reconstruction of it.
//
// What it does. Each block is extracted, prepended with a fixture, and compiled against the REAL
// built FS.GG.Game.Core — not a reconstruction of it. A compiler error is a gate failure, reported
// against the markdown line the reader would copy from.
//
// Three things make that tractable, because the blocks are SKETCHES, not programs:
//
//   1. `module rec`. A block declares its own types (`type Creep = { Pos: Geometry.Vec2 ... }`) and
//      then uses free values of those types (`creeps`). A fixture that merely PREPENDS cannot bind
//      `creeps : Creep list` — `Creep` does not exist yet. In a recursive module it can:
//      declarations are mutually visible regardless of order, so the fixture forward-references the
//      type the block declares below it, and the block is still compiled VERBATIM against its OWN
//      declarations. No mutation of the subject, no duplicate type standing in for it.
//
//   2. `# <line> "<file>"` line directives. The compiler reports errors against the markdown line
//      the block came from, so a failure reads `pong.md(226,59): error FS0001: ... expected 'Point'
//      but here has type 'Geometry.Vec2'` — the reader is pointed at the prose they must fix, not at
//      a generated file they have never seen.
//
//   3. Cumulative opens (testspecs only). Block N of a document opens blocks 1..N-1 of the SAME
//      document. Shadowing across those module boundaries is legal, which is what lets a document
//      re-state `type Vec2 = Geometry.Vec2` in a later block for the reader's benefit (doodle-jump
//      does exactly this) without the compiler seeing a duplicate definition.
//
// The reconstructed context. Two files stand up context that this repo does not own and cannot
// reference, and NOTHING else in the harness fabricates anything:
//
//   skill-block-context/_scaffold.fs      `Geometry.Vec2` (Vx/Vy), from the generated product's
//                                         src/<ProductDir>/Vec2.fs — owned by FS.GG.Templates.
//   testspec-block-context/_prelude.fs    the host input vocabulary (`Key`, `Button`) the TestSpecs
//                                         assume — owned by the UI layer.
//
// Both are unenforced cross-repo contracts; each says so in its own header.
//
// Fail-closed. A gate that reports green over a subject it never compiled is the bug this exists to
// kill, so the harness refuses to pass unless it can prove it looked: no source files, no blocks, a
// block count that disagrees with an independent fence count, a missing FS.GG.Game.Core build, or a
// Compile-item count that disagrees with the block count are all hard failures. Skips require a
// written reason and are printed on every run.
//
// Usage:
//   dotnet fsi scripts/typecheck-md-blocks.fsx                      # both corpora (this is the gate)
//   dotnet fsi scripts/typecheck-md-blocks.fsx --corpus testspecs   # just one
//   dotnet fsi scripts/typecheck-md-blocks.fsx --list               # list the blocks, compile nothing
//   dotnet fsi scripts/typecheck-md-blocks.fsx --keep               # leave the generated project on disk
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

let optionValue name fallback =
    match argv |> Array.tryFindIndex ((=) name) with
    | Some i when i + 1 < argv.Length -> argv[i + 1]
    | _ -> fallback

let configuration = optionValue "--configuration" "Debug"
let corpusFilter = optionValue "--corpus" "all"

/// A GitHub Actions annotation, so a failure lands on the diff instead of only in the log.
let annotate level (file: string) (line: int) (col: int) (msg: string) =
    // Annotation messages must not carry raw newlines.
    let flat = Regex.Replace(msg, @"\s*\r?\n\s*", " ")
    printfn "::%s file=%s,line=%d,col=%d::%s" level file line col flat

let fail (msg: string) =
    printfn "::error::%s" msg
    eprintfn "typecheck-md-blocks: %s" msg
    exit 1

// ---------------------------------------------------------------------------------------------
// 0. The corpora
// ---------------------------------------------------------------------------------------------

type Corpus =
    { Id: string
      /// Human name, for the log header.
      Label: string
      /// The markdown this corpus is made of. Absolute paths, sorted.
      Sources: unit -> string[]
      /// The fixture key for a source file — the stem of its fixture .fs, and a block's `Doc`.
      DocOf: string -> string
      /// scripts/<dir>, holding one <doc>.fs fixture file per document.
      FixtureDir: string
      /// Compiled BEFORE the blocks, in order. Reconstructions of context this repo does not own.
      Preludes: string list
      /// `open`ed by every block, after the block's own opens, in this order.
      AmbientOpens: string list
      /// NuGet packages the blocks legitimately need. The VERSION is not repeated here — it is read
      /// from the repo's central pin (see `pinnedVersion`), because a second copy of a version is a
      /// drift bug with a delay fuse.
      PackageRefs: string list
      /// Does block N see the declarations of blocks 1..N-1 of the same document?
      Cumulative: bool
      /// Namespace for the generated per-block modules.
      ModuleNs: string }

let scaffold = "scripts/skill-block-context/_scaffold.fs"

/// The version this repo has centrally pinned for `package`. Read, never restated: the gate must
/// compile a block against the SAME package the product does, and a hardcoded version here would
/// drift from Directory.Packages*.props silently.
let pinnedVersion (package: string) =
    let props =
        [ "Directory.Packages.local.props"; "Directory.Packages.props" ]
        |> List.map repoPath
        |> List.filter File.Exists
    let hit =
        props
        |> List.tryPick (fun p ->
            let m = Regex.Match(File.ReadAllText p,
                                $"""<PackageVersion\s+Include="{Regex.Escape package}"\s+Version="(?<v>[^"]+)"\s*/>""")
            if m.Success then Some m.Groups["v"].Value else None)
    match hit with
    | Some v -> v
    | None ->
        fail $"no central PackageVersion pin found for '{package}' in Directory.Packages*.props. \
               A block needs it to compile, and this harness will not invent a version — add the \
               pin, or drop the package from the corpus."

let corpora =
    [ { Id = "skills"
        Label = "product skills — template/product-skills/**/SKILL.md"
        Sources = fun () ->
            let dir = repoPath "template/product-skills"
            if not (Directory.Exists dir) then
                fail "template/product-skills does not exist — nothing to typecheck. Refusing to pass."
            Directory.GetFiles(dir, "SKILL.md", SearchOption.AllDirectories) |> Array.sort
        // A skill's key is its DIRECTORY name (…/fs-gg-grids/SKILL.md -> fs-gg-grids): every file is
        // called SKILL.md, so the stem would key all of them to one fixture.
        DocOf = fun f -> Path.GetFileName(Path.GetDirectoryName f)
        FixtureDir = "scripts/skill-block-context"
        Preludes = [ scaffold ]
        AmbientOpens = [ "FS.GG.Game.Core"; "FsGg.SkillCheck.Scaffold" ]
        // The packages the SKILLS teach and this repo does not consume (FS.GG.Game#150). fs-gg-audio
        // binds FS.GG.Audio.Core/Host; fs-gg-persistence binds FS.GG.UI.Canvas; and audio's host
        // blocks reach into the viewer that DRIVES the audio — `Viewer.runAppWithAudio` and
        // `GeneratedAppHost.dispatchKey`/`audioRequests` are FS.GG.UI.SkiaViewer, `ViewerKeyEvent` is
        // FS.GG.UI.KeyboardInput. Without these, all seven of those blocks were UNREACHABLE: the gate
        // had nothing to compile them against and skipped them, so published code that readers copy
        // verbatim was gated by nothing — the silent-no-op shape (.github#416) reproduced inside the
        // harness built to end it.
        //
        // They are compiled against the REAL published assemblies, exactly as a reader restores them.
        // A stand-in Audio/Canvas surface was the alternative and is strictly worse: a skill
        // typechecked against a fiction still shows a green tick, and the tick is what stops anyone
        // looking. Same reasoning as the testspecs corpus's Expecto below.
        //
        // No PRODUCT project references any of these — see the gate-only group in
        // Directory.Packages.local.props, which is where `pinnedVersion` reads their versions from.
        PackageRefs =
            [ "FS.GG.Audio.Core"; "FS.GG.Audio.Host"
              "FS.GG.UI.Canvas"; "FS.GG.UI.SkiaViewer"; "FS.GG.UI.KeyboardInput" ]
        Cumulative = false
        ModuleNs = "FsGg.SkillCheck.Generated" }

      { Id = "testspecs"
        Label = "TestSpec corpus — docs/TestSpecs/Games/*.md, docs/TestSpecTutorial.md"
        Sources = fun () ->
            let games = repoPath "docs/TestSpecs/Games"
            if not (Directory.Exists games) then
                fail "docs/TestSpecs/Games does not exist — nothing to typecheck. Refusing to pass."
            let tutorial = repoPath "docs/TestSpecTutorial.md"
            if not (File.Exists tutorial) then
                fail "docs/TestSpecTutorial.md does not exist — nothing to typecheck. Refusing to pass."
            Array.append (Directory.GetFiles(games, "*.md")) [| tutorial |] |> Array.sort
        DocOf = Path.GetFileNameWithoutExtension
        FixtureDir = "scripts/testspec-block-context"
        Preludes = [ scaffold; "scripts/testspec-block-context/_prelude.fs" ]
        // Scaffold LAST, as in the skills corpus: in a real product the generated `Geometry` (Vec2)
        // is opened after `FS.GG.Game.Core`'s, and F# MERGES the two same-named modules. Reproducing
        // that merge is the point — the #129/#132/#140/#144 bug class is precisely a value crossing
        // between its two halves.
        AmbientOpens = [ "FS.GG.Game.Core"; "FsGg.DocCheck.Host"; "FsGg.SkillCheck.Scaffold" ]
        // TestSpecTutorial Part C teaches the scaffold's Expecto test style (`testList`, `Expect.*`).
        // That is a REAL package the scaffolded product's test project references, so the block is
        // compiled against the real thing rather than skipped or faked — the alternative would leave
        // the one block that teaches readers how to write their assertions ungated.
        PackageRefs = [ "Expecto" ]
        Cumulative = true
        ModuleNs = "FsGg.DocCheck.Generated" } ]

let selected =
    match corpusFilter with
    | "all" -> corpora
    | id ->
        match corpora |> List.tryFind (fun c -> c.Id = id) with
        | Some c -> [ c ]
        | None ->
            let known = corpora |> List.map _.Id |> String.concat ", "
            fail $"unknown --corpus '{id}'. Known: {known}, all."

// ---------------------------------------------------------------------------------------------
// 1. Extract
// ---------------------------------------------------------------------------------------------

type Block =
    { Doc: string           // fixture key: skill id (fs-gg-grids) or document stem (pong)
      SourceFile: string    // absolute path to the markdown
      Ordinal: int          // 1-based index of the block WITHIN its file — the fixture key
      StartLine: int        // 1-based markdown line of the block's FIRST CODE LINE (after the fence)
      Code: string }

/// Blocks are fenced with an exactly-``` ```fsharp `` opener at column 0 and a ``` closer. The
/// corpora use no nested or indented fences — the independent fence count below would catch it if
/// they started to — so a line scanner is honest here and a markdown parser would be ceremony.
let extractBlocks (docOf: string -> string) (sourceFile: string) : Block list =
    let doc = docOf sourceFile
    let lines = File.ReadAllLines sourceFile
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
                fail $"{relative sourceFile}: unterminated ```fsharp fence opened at line {openFence + 1}."
            ordinal <- ordinal + 1
            blocks.Add
                { Doc = doc
                  SourceFile = sourceFile
                  Ordinal = ordinal
                  StartLine = openFence + 2         // 1-based line of the first code line
                  Code = String.Join("\n", body) }
            i <- j + 1
        else
            i <- i + 1
    List.ofSeq blocks

// ---------------------------------------------------------------------------------------------
// 2. Fixtures
// ---------------------------------------------------------------------------------------------
//
// A fixture supplies what a sketch leaves unbound: free values (`creeps`, `cellPx`), types the prose
// never declares (`TowerId`, `DamageType`), or the `type Msg =` header that a DU-continuation
// fragment is written below. One file per document, in <FixtureDir>/<doc>.fs, split into sections:
//
//     //#block 6
//     let cellPx = 32.0
//     let creeps : Creep list = []     // forward-references the block's own type — needs //#rec
//
//     //#block 7
//     //#skip <why this block cannot be compiled>
//
// A block with no section gets an EMPTY fixture and is compiled as-is: a self-contained block needs
// no ceremony, and a sketch that needs bindings fails loudly with an unbound-identifier error naming
// exactly what to add. Absence is never a skip.

type Fixture =
    | Context of recursive: bool * text: string   // F# text prepended to the block, in its module
    | Skipped of reason: string                   // printed on every run, never silent

let blockDirective = Regex(@"^//#block\s+(\d+)\s*$")
let skipDirective = Regex(@"^//#skip\s+(.+)$")
let recDirective = Regex(@"^//#rec\s*$")

/// Every `//#…` line must BE a directive the harness understands. A mistyped or half-written one —
/// a bare `//#skip` with the reason left off, a `//#bloc 3` — otherwise parses as an ordinary F#
/// comment and is swallowed into the block's fixture text, and the damage lands nowhere near the
/// mistake: the bare `//#skip` silently loses its skip and the block gets COMPILED, which is the
/// opposite of what its author asked for, with no diagnostic anywhere. A directive that is ignored
/// in silence is the same silent-no-op this whole harness exists to kill, so it fails here, at the
/// line the mistake is on.
let validateDirectives (corpus: Corpus) (doc: string) (lines: string[]) =
    lines
    |> Array.iteri (fun i line ->
        if line.StartsWith "//#"
           && not (blockDirective.IsMatch line || skipDirective.IsMatch line || recDirective.IsMatch line) then
            let text = line.Trim()
            fail $"{corpus.FixtureDir}/{doc}.fs line {i + 1} is not a directive this harness \
                   understands: {text}. Expected `//#block <n>`, `//#skip <reason>`, or `//#rec`. A \
                   directive that is silently ignored fails as somebody ELSE's bug — refusing.")

let loadFixtures (corpus: Corpus) (blocks: Block list) (doc: string) : Map<int, Fixture> =
    let path = repoPath $"{corpus.FixtureDir}/{doc}.fs"
    if not (File.Exists path) then Map.empty
    else
        let mutable current = None
        let acc = System.Collections.Generic.Dictionary<int, ResizeArray<string>>()
        let skips = System.Collections.Generic.Dictionary<int, string>()
        let recs = System.Collections.Generic.HashSet<int>()
        let lines = File.ReadAllLines path
        validateDirectives corpus doc lines
        for line in lines do
            let m = blockDirective.Match line
            if m.Success then
                let n = int m.Groups[1].Value
                // A second section for the same block would silently REPLACE the first, dropping
                // bindings the author wrote and then failing the block with a confusing
                // unbound-identifier error while the binding sits right there in this file.
                if acc.ContainsKey n then
                    fail $"{corpus.FixtureDir}/{doc}.fs declares //#block {n} twice. The second \
                           section would silently discard the first — merge them."
                current <- Some n
                acc[n] <- ResizeArray()
            else
                match current with
                | None -> ()    // file header, before the first //#block — ignored
                | Some n ->
                    let s = skipDirective.Match line
                    if s.Success then skips[n] <- s.Groups[1].Value.Trim()
                    elif recDirective.IsMatch line then recs.Add n |> ignore
                    else acc[n].Add line
        let fixtures =
            acc
            |> Seq.map (fun kv ->
                match skips.TryGetValue kv.Key with
                | true, reason -> kv.Key, Skipped reason
                | _ -> kv.Key, Context(recs.Contains kv.Key, String.Join("\n", kv.Value).Trim()))
            |> Map.ofSeq
        // A //#block section keyed to a block that does not exist is a stale fixture — usually a
        // block was deleted or reordered. Say so; a silently-ignored fixture is a lie in a file whose
        // whole job is to be true.
        let ordinals = blocks |> List.filter (fun b -> b.Doc = doc) |> List.map _.Ordinal |> Set.ofList
        for KeyValue(n, _) in acc do
            if not (ordinals.Contains n) then
                fail $"{corpus.FixtureDir}/{doc}.fs declares //#block {n}, but {doc} has only \
                       {ordinals.Count} ```fsharp block(s). Stale fixture — a block was deleted or \
                       reordered; re-key the fixture."
        fixtures

// ---------------------------------------------------------------------------------------------
// 2b. The label rule
// ---------------------------------------------------------------------------------------------
//
// A TYPECHECK ALONE DOES NOT CLOSE THE LOOP #149 ASKS IT TO, and this is the part that does.
//
// #144 found eight TestSpec sketches declaring the X/Y-labelled record the core skill forbids, and
// #147 fixed them by hand. But a document that declares its OWN `type Vec2 = { X: float; Y: float }`
// and then uses it consistently COMPILES PERFECTLY — nothing crosses a type boundary, so there is no
// type error to find. Point the typechecker at the corpus, revert #147, and the gate stays green. It
// was verified: mutating pong's `type Vec2 = Geometry.Vec2` back into an X/Y record — #144, verbatim
// — produced zero compile errors.
//
// So the rule the corpus actually states in its own prose ("NEVER a record you label X/Y/Width/
// Height, which collide with Scene's Point/Rect") is enforced HERE, textually, as itself.
//
// Why those four labels. `FS.GG.UI.Scene.Point`/`Rect` and the sim `FS.GG.Game.Core.Point`/`Rect`
// both carry X/Y/Width/Height; the generated product's `Geometry.Vec2` carries Vx/Vy precisely so it
// has ZERO label overlap with either. F# resolves a BARE record literal by its label set, so a game
// type that reuses X/Y makes `{ X = 1.0; Y = 2.0 }` resolve against whichever of the colliding types
// was declared last — silently, and differently depending on what is open. That is the whole
// #129/#132/#140/#144 bug class, and it is a LABEL bug, not a type error.
//
// This runs over EVERY block, including the //#skip'd ones: a skipped block is still published prose
// that a reader copies, and the check is textual, so nothing excuses it from the rule.

let forbiddenFieldLabels = [ "X"; "Y"; "Width"; "Height" ]

/// Matches a record-field DECLARATION (`X: float`) at the start of a line or after a field
/// boundary. The boundary class includes `|` so that the FIRST field of an anonymous record
/// (`{| X: float; ... |}`) is caught: without it, `{` is followed by `|` rather than the label, so
/// the opening field slipped through while every later one (after a `;`) was flagged — the rule
/// would have been enforced inconsistently within a single declaration, which is worse than not
/// enforcing it at all. frogger's `Model` really does hold anonymous records, so this is reachable.
///
/// Deliberately not a record-field ASSIGNMENT (`X = 1.0`): a literal with the wrong labels is
/// already a type error, and the compiler reports it better than we could.
let forbiddenLabelPattern =
    Regex($@"(^|[{{;(|])\s*(?<label>{String.Join('|', forbiddenFieldLabels)})\s*:",
          RegexOptions.Compiled)

let lintLabels (blocks: Block list) : int =
    let mutable violations = 0
    for b in blocks do
        b.Code.Split('\n')
        |> Array.iteri (fun i line ->
            // Strip a trailing comment before matching — the corpus is full of prose like
            // "// NOT X — that label collides with Scene's Point/Rect", which must not trip its own rule.
            let code =
                match line.IndexOf "//" with
                | -1 -> line
                | at -> line.Substring(0, at)
            // Every match on the line, not just the first: `{ X: float; Y: float }` is TWO
            // violations, and reporting one of them would understate the very thing being counted.
            for m in forbiddenLabelPattern.Matches code do
                violations <- violations + 1
                let label = m.Groups["label"].Value
                let docLine = b.StartLine + i
                annotate "error" (relative b.SourceFile) docLine (m.Groups["label"].Index + 1)
                    $"forbidden record-field label '{label}'. X/Y/Width/Height are the labels of \
                      Scene's Point/Rect AND of the sim Point/Rect, so a bare record literal resolves \
                      against the wrong one (FS.GG.Game#129/#132/#140/#144). Positions and velocities \
                      go in the scaffold's collision-safe Geometry.Vec2 (Vx/Vy); scalars take an \
                      honest name (LeftX, TopY, WidthPx, HeightTiles)."
                printfn "  %s:%d  forbidden field label '%s' — use Vx/Vy (Geometry.Vec2) or a scalar \
                         named LeftX/TopY/WidthPx." (relative b.SourceFile) docLine label)
    violations

// ---------------------------------------------------------------------------------------------
// 3. Check one corpus
// ---------------------------------------------------------------------------------------------

// Typecheck against the REAL built assembly, exactly as a product consumer would bind to the
// package. If it is absent we do NOT quietly fall back to a source reference or a stub: a gate that
// "passes" against a subject that was never built is the defect, not a convenience.
let coreDll = repoPath $"src/Game.Core/bin/{configuration}/net10.0/FS.GG.Game.Core.dll"

if not (File.Exists coreDll) && not listOnly then
    fail $"FS.GG.Game.Core.dll not found at {relative coreDll} — build it first \
           (dotnet build src/Game.Core/FS.GG.Game.Core.fsproj -c {configuration}). Refusing to \
           typecheck the docs against an assembly that does not exist."

let ident (s: string) = Regex.Replace(s, @"[^A-Za-z0-9]", "_")

let run (fileName: string) (args: string) =
    let psi = ProcessStartInfo(fileName, args, RedirectStandardOutput = true, RedirectStandardError = true)
    use p = Process.Start psi
    // Drain both pipes CONCURRENTLY. Reading one to completion before touching the other deadlocks
    // as soon as the child fills the other pipe's buffer (~64 KB) — which a cascading compile failure
    // across 40+ generated files will do. The gate would then hang to its job timeout and report an
    // infrastructure failure instead of the type error it actually found.
    let stdout = p.StandardOutput.ReadToEndAsync()
    let stderr = p.StandardError.ReadToEndAsync()
    p.WaitForExit()
    p.ExitCode, stdout.Result + stderr.Result

/// Returns the number of compile errors (0 = this corpus is green).
let checkCorpus (corpus: Corpus) : int =
    printfn ""
    printfn "── %s ──" corpus.Label

    let sources = corpus.Sources()
    if sources.Length = 0 then
        fail $"[{corpus.Id}] found 0 source files. Refusing to report success over a subject I never \
               examined."

    let blocks = sources |> Array.collect (extractBlocks corpus.DocOf >> Array.ofList) |> List.ofArray

    // Independent cross-check of the extractor. A regression that silently stops SEEING blocks would
    // otherwise sail through green — the exact failure this gate exists to prevent, reproduced inside
    // the gate itself. Count the fence-open lines the dumb way and demand agreement.
    let independentFenceCount =
        sources
        |> Array.sumBy (fun f ->
            File.ReadAllLines f |> Array.filter (fun l -> l.TrimEnd() = "```fsharp") |> Array.length)

    if blocks.Length <> independentFenceCount then
        fail $"[{corpus.Id}] extractor disagreement: parsed {blocks.Length} blocks but counted \
               {independentFenceCount} ```fsharp fences. The extractor is dropping blocks — fix it \
               before trusting this gate."

    if blocks.Length = 0 then
        fail $"[{corpus.Id}] found 0 ```fsharp blocks. Refusing to pass: a gate with no subject is a \
               green light over nothing."

    let fixturesByDoc =
        blocks
        |> List.map _.Doc
        |> List.distinct
        |> List.map (fun d -> d, loadFixtures corpus blocks d)
        |> Map.ofList

    let fixtureFor (b: Block) = fixturesByDoc[b.Doc] |> Map.tryFind b.Ordinal

    for p in corpus.Preludes do
        if not (File.Exists(repoPath p)) then
            fail $"[{corpus.Id}] {p} is missing — it reconstructs context the blocks are compiled \
                   against and FS.GG.Game.Core does not ship."

    // -- report ------------------------------------------------------------------------------

    let compiled, skipped =
        blocks |> List.partition (fun b -> match fixtureFor b with Some(Skipped _) -> false | _ -> true)

    printfn "%d ```fsharp block(s) across %d file(s)" blocks.Length sources.Length
    for doc in blocks |> List.map _.Doc |> List.distinct do
        let n = blocks |> List.filter (fun b -> b.Doc = doc) |> List.length
        printfn "  %-28s %2d block(s)" doc n

    // Skips are printed on EVERY run, as warnings, whether or not anything failed. An unexamined
    // block that nobody is reminded about is how the gate rots back into theatre.
    for b in skipped do
        match fixtureFor b with
        | Some(Skipped reason) ->
            annotate "warning" (relative b.SourceFile) b.StartLine 1
                $"md-block typecheck SKIPPED (block {b.Ordinal} of {b.Doc}): {reason}"
        | _ -> ()

    if not skipped.IsEmpty then
        printfn "%d block(s) SKIPPED by an explicit //#skip, %d compiled." skipped.Length compiled.Length

    if listOnly then
        // --list compiles nothing and checks nothing; it must not emit errors it then reports success
        // over. The label rule runs in the CHECK path below.
        for b in blocks do
            let state =
                match fixtureFor b with
                | Some(Skipped r) -> $"SKIP ({r})"
                | Some(Context _) -> "compile (with fixture)"
                | None -> "compile (self-contained)"
            printfn "  %s block %d @ %s:%d — %s" b.Doc b.Ordinal (relative b.SourceFile) b.StartLine state
        0
    else

    // The label rule (see §2b). Runs over EVERY block — including the skipped ones, which the
    // compiler never sees but a reader still copies.
    let labelViolations = lintLabels blocks
    if labelViolations > 0 then
        printfn "%d forbidden record-field label(s)." labelViolations

    if compiled.IsEmpty then
        fail $"[{corpus.Id}] every block is skipped — this gate would compile nothing and report \
               green. Refusing."

    // -- generate ----------------------------------------------------------------------------

    let outDir = Path.Combine(Path.GetTempPath(), $"fsgg-md-typecheck-{corpus.Id}-{Guid.NewGuid():N}")
    Directory.CreateDirectory outDir |> ignore

    let moduleName (b: Block) = $"{corpus.ModuleNs}.{ident b.Doc}_{b.Ordinal}"

    // The COMPILED blocks of one document that precede this one. A skipped block is never emitted, so
    // a later block cannot open it — if a skip strands a declaration a later block needs, that block
    // fails loudly with an unbound-name error rather than the skip quietly widening to swallow blocks
    // nobody chose to skip.
    let predecessorsOf (b: Block) =
        if not corpus.Cumulative then []
        else
            compiled
            |> List.filter (fun p -> p.Doc = b.Doc && p.Ordinal < b.Ordinal)
            |> List.sortBy _.Ordinal
            |> List.map moduleName

    /// One file per block: the fixture, then the block VERBATIM behind a line directive that
    /// re-anchors the compiler on the markdown.
    ///
    /// Two structural adjustments, and they are the only two:
    ///
    ///   * The block's own `open` lines are HOISTED above the fixture and BLANKED where they stood,
    ///     so the block keeps its exact line count (and so its errors keep pointing at the right
    ///     markdown line). Hoisting is safe — an `open` cannot carry a type error — and it is
    ///     required: in a recursive module F# demands every `open` come first (FS3200). Our own opens
    ///     follow: ambient first, then the document's own PREDECESSOR blocks, so a name the document
    ///     declares always shadows an ambient one of the same name rather than the reverse.
    ///
    ///   * `module rec` only when the fixture asks for it (//#rec). A recursive module is what lets a
    ///     fixture forward-reference a type the block itself declares (`creeps : Creep list`), but it
    ///     also forbids things plain F# allows — notably tuple-pattern bindings at module level
    ///     (`let a, b = Grids.edgeSegment spec wall`, FS0873). So plain module is the default and
    ///     recursion is opted into by the fixtures that genuinely need it.
    let generateBlockFile (b: Block) =
        let fixture = fixtureFor b
        let isRec = match fixture with Some(Context(true, _)) -> true | _ -> false

        // Hoist the block's TOP-LEVEL opens, preserving the block's line count by blanking them in
        // place. Column-0 only, deliberately: an INDENTED `open` belongs to a nested scope, and
        // lifting it to the top of the file would widen its scope and change name resolution — the
        // gate would then typecheck a program that resolves differently from the one the reader
        // pastes. An indented open is left exactly where it is (F# accepts it there in a
        // non-recursive module).
        let isTopLevelOpen (l: string) = Regex.IsMatch(l, @"^open\s+\S+")
        let codeLines = b.Code.Split('\n')
        let blockOpens = codeLines |> Array.filter isTopLevelOpen |> Array.map _.Trim()
        let body =
            codeLines
            |> Array.map (fun l -> if isTopLevelOpen l then "" else l)
            |> String.concat "\n"

        // ...but a nested `open` inside a block we are about to compile as a RECURSIVE module is a
        // hard error (FS3200: in a recursive group every `open` must come first), and hoisting it is
        // exactly what we just refused to do. Say so plainly rather than emitting a file that cannot
        // compile.
        if isRec && codeLines |> Array.exists (fun l -> Regex.IsMatch(l, @"^\s+open\s+\S+")) then
            fail $"{relative b.SourceFile} block {b.Ordinal} has an INDENTED `open` and its fixture \
                   asks for //#rec. F# forbids that (FS3200), and hoisting the open out of its scope \
                   would change name resolution. Drop the //#rec, or bind the free value without \
                   needing it."

        let sb = StringBuilder()
        let recKeyword = if isRec then "rec " else ""
        sb.AppendLine($"module {recKeyword}{moduleName b}").AppendLine() |> ignore
        for o in Array.distinct blockOpens do
            sb.AppendLine(o) |> ignore
        for o in corpus.AmbientOpens do
            sb.AppendLine($"open {o}") |> ignore
        for p in predecessorsOf b do
            sb.AppendLine($"open {p}") |> ignore
        sb.AppendLine() |> ignore

        match fixture with
        | Some(Context(_, ctx)) when ctx <> "" ->
            sb.AppendLine($"// fixture: {corpus.FixtureDir}/{b.Doc}.fs //#block {b.Ordinal}") |> ignore
            sb.AppendLine(ctx).AppendLine() |> ignore
        | _ -> ()

        // `# N "file"` sets the NEXT physical line to line N of `file`, so errors in the block are
        // reported against the markdown the reader is holding.
        let anchor = b.SourceFile.Replace(@"\", "/")
        sb.AppendLine($"# {b.StartLine} \"{anchor}\"") |> ignore
        sb.AppendLine(body) |> ignore

        let file = Path.Combine(outDir, $"{ident b.Doc}_{b.Ordinal}.fs")
        File.WriteAllText(file, sb.ToString())
        file

    let preludeCopies =
        corpus.Preludes
        |> List.map (fun p ->
            let dest = Path.Combine(outDir, Path.GetFileName p)
            File.Copy(repoPath p, dest)
            dest)

    // Compile order IS document order: a cumulative block opens its predecessors, and F# requires a
    // module to be compiled before it can be opened.
    let ordered = compiled |> List.sortBy (fun b -> b.Doc, b.Ordinal)
    let blockFiles = ordered |> List.map generateBlockFile
    let compileItems = preludeCopies @ blockFiles

    // Every generated file must actually EXIST on disk and be non-empty. (Counting `compileItems`
    // against `compiled` would be a tautology — the list is built by mapping over `compiled`, so its
    // length always agrees and the check could never fire. The disk is the independent witness.)
    for f in compileItems do
        if not (File.Exists f) then
            fail $"[{corpus.Id}] generator did not write {Path.GetFileName f} — it would have been \
                   compiled as nothing."
        if (FileInfo f).Length = 0L then
            fail $"[{corpus.Id}] generator wrote an EMPTY {Path.GetFileName f} — an empty file \
                   compiles clean and would pass this gate over a block it never examined."

    let projectXml =
        let items =
            compileItems
            |> List.map (fun f -> $"""    <Compile Include="{Path.GetFileName f}" />""")
            |> String.concat "\n"
        let packages =
            corpus.PackageRefs
            |> List.map (fun p ->
                $"""    <PackageReference Include="{p}" Version="{pinnedVersion p}" />""")
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
    <!-- fsc stops REPORTING at 100 errors by default and never type-checks the files after that.
         It cannot make this gate pass over a broken block — the run still exits non-zero, and the
         gate demands a zero exit AND an empty error list — but it silently TRUNCATES the report, so
         an author fixing a corpus-wide mistake sees a partial list, fixes it, and is met by a fresh
         batch from the documents fsc never got to. Ask for the whole picture in one run. -->
    <OtherFlags>$(OtherFlags) --maxerrors:2000</OtherFlags>
    <!-- Keeping the repo's Directory.Build.props out (above) also drops the org's restore-safety
         promotions with it, and a corpus with PackageRefs is where they matter MOST: a package the
         feed cannot serve at the pinned version would otherwise be silently SUBSTITUTED (NU1603) or
         downgraded (NU1605), and the gate would typecheck the block against a surface no reader will
         ever restore — green, and lying. Promote them back. -->
    <WarningsAsErrors>$(WarningsAsErrors);NU1603;NU1605;NU1608</WarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
{items}
  </ItemGroup>

  <ItemGroup>
    <Reference Include="FS.GG.Game.Core">
      <HintPath>{coreDll}</HintPath>
    </Reference>
{packages}
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

    let projectFile = Path.Combine(outDir, $"{ident corpus.Id}Blocks.fsproj")
    File.WriteAllText(projectFile, projectXml)

    // Read the project back and count the Compile items the COMPILER will actually see. This is the
    // coverage assertion that matters: everything upstream of it is our own bookkeeping agreeing with
    // itself, whereas this is the emitted artefact agreeing with the block list. One Compile item per
    // compiled block, plus the preludes — anything else means the gate is about to examine less than
    // it claims to.
    let emittedCompileItems = Regex.Matches(File.ReadAllText projectFile, @"<Compile Include=").Count
    let expected = compiled.Length + preludeCopies.Length
    if emittedCompileItems <> expected then
        fail $"[{corpus.Id}] the generated project has {emittedCompileItems} Compile item(s) for \
               {compiled.Length} block(s) + {preludeCopies.Length} prelude(s). The gate would compile \
               less than it reports — refusing."

    // -- compile -----------------------------------------------------------------------------

    printfn "compiling %d block(s) against %s" compiled.Length (relative coreDll)

    let exitCode, output = run "dotnet" $"build \"{projectFile}\" -c {configuration} --nologo -v minimal"

    if not keepGenerated then
        try Directory.Delete(outDir, true) with _ -> ()
    else
        printfn "generated project kept at %s" outDir

    // Compiler diagnostics come back anchored on the markdown (the line directive did that), so we
    // re-emit them as annotations on the real file. A diagnostic anchored anywhere ELSE means the
    // FIXTURE or PRELUDE does not compile, not the doc — saying which is the difference between the
    // author fixing the right file and hunting through prose that is already correct.
    let sourceSet = sources |> Array.map (fun f -> f.Replace(@"\", "/")) |> Set.ofArray

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
        if labelViolations = 0 then
            printfn "OK — %d block(s) typecheck against FS.GG.Game.Core." compiled.Length
        else
            printfn "%d block(s) typecheck, but the label rule is violated %d time(s) — see above."
                compiled.Length labelViolations
        labelViolations
    else

    printfn ""
    for (file, line, col, _, code, msg) in errors do
        let normalized = file.Replace(@"\", "/")
        let shown = if Path.IsPathRooted file then relative file else file
        if sourceSet.Contains normalized then
            annotate "error" shown line col $"{code}: {msg}"
            printfn "  %s:%d:%d  %s: %s" shown line col code msg
        else
            annotate "error" corpus.FixtureDir 1 1
                $"fixture/prelude does not compile ({Path.GetFileName file}:{line}): {code}: {msg}"
            printfn "  [fixture] %s:%d:%d  %s: %s" (Path.GetFileName file) line col code msg

    if errors.IsEmpty then
        // Non-zero exit with no parsed diagnostic: never swallow it.
        printfn "%s" output
        fail $"[{corpus.Id}] the block compilation failed (exit {exitCode}) but produced no parseable \
               diagnostic — see the raw build output above."

    errors.Length + labelViolations

// ---------------------------------------------------------------------------------------------
// 4. Drive
// ---------------------------------------------------------------------------------------------

let results = selected |> List.map (fun c -> c.Id, checkCorpus c)
let totalErrors = results |> List.sumBy snd

printfn ""

if listOnly then exit 0

if totalErrors = 0 then
    printfn "typecheck-md-blocks: OK — every ```fsharp block typechecks against FS.GG.Game.Core."
    exit 0

for (id, n) in results do
    if n > 0 then printfn "typecheck-md-blocks: %s — %d error(s)." id n

fail $"{totalErrors} error(s): a ```fsharp block in a published document either does not typecheck \
       against FS.GG.Game.Core, or breaks the X/Y/Width/Height label rule. Readers copy these blocks \
       into their product — fix the prose (or, if the block is a sketch missing a binding, add it to \
       the corpus's fixture file)."

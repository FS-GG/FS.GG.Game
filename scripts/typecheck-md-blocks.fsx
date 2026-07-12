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
// ...and a guard is only fail-closed if it can actually FAIL (#176). The block-count cross-check
// above was computed with the EXTRACTOR'S OWN predicate, so the two were blind to an indented
// ```fsharp fence in precisely the same way, always agreed, and passed over two published blocks
// nothing ever compiled. The extractor now takes a fence at any indent (§1), and its cross-check is
// written to a deliberately different, permissive rule so that it can disagree — see
// `looseFencePattern`. A check that shares the bug it checks for is theatre, and it is the very shape
// (.github#416) this harness exists to end.
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
        //
        // FS.GG.UI.Scene is declared EXPLICITLY even though Canvas/SkiaViewer already drag it in
        // transitively: _scaffold.fs binds `Scene.Point`/`Rect` directly now (#165), and a direct
        // dependency carried only as somebody else's transitive one breaks the day that somebody
        // else drops it.
        PackageRefs =
            [ "FS.GG.Audio.Core"; "FS.GG.Audio.Host"
              "FS.GG.UI.Canvas"; "FS.GG.UI.SkiaViewer"; "FS.GG.UI.KeyboardInput"
              "FS.GG.UI.Scene" ]
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
        //
        // FS.GG.UI.Scene, because the shared `_scaffold.fs` prelude binds `Scene.Point`/`Rect` for
        // its `toPoint`/`toRect` edge (#165). This corpus never had Scene on its graph — #150 put it
        // on the SKILLS corpus only — so without this the prelude would not compile here at all.
        PackageRefs = [ "Expecto"; "FS.GG.UI.Scene" ]
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
      /// The opening fence's indent, in characters — stripped from `Code` (see `extractBlocks`).
      /// Columns reported by the compiler and by the label lint are columns in that DEDENTED text,
      /// so this is added back before any annotation, and a diagnostic lands ON the token it names.
      /// Zero for a column-0 fence, which is all but two blocks in the corpora.
      Indent: int
      /// Lines in `Code`. Fixed at extraction, so `indentAt` can map a diagnostic's markdown line back
      /// to its block without re-splitting every block's text on every lookup.
      LineCount: int
      Code: string }

/// A fence opener: three-or-more backticks tagged exactly `fsharp`, at ANY indent. An indented one is
/// not exotic — a fence inside a bullet list sits at the bullet's content column, and two published
/// blocks do exactly that (#176). Requiring column 0 silently dropped both, and they were compiled by
/// nothing.
let fenceOpen = Regex(@"^(?<indent>[ \t]*)(?<ticks>`{3,})fsharp[ \t]*$")

/// The closer: a bare fence of at least as many backticks as the opener. An indented block's closer is
/// indented too, so some leeway is required — but NOT unlimited leeway, or a backtick-only line deep
/// inside a block's code (say, within a triple-quoted string) would close it early and silently
/// truncate the body. CommonMark's rule is the right amount: a closer may sit up to three spaces past
/// the opener's own indent, and no further.
let isFenceClose (indent: string) (ticks: string) (line: string) =
    let closerIndent = line.Length - line.TrimStart([| ' '; '\t' |]).Length
    let t = line.Trim()
    closerIndent <= indent.Length + 3
    && t.Length >= ticks.Length
    && t |> Seq.forall ((=) '`')

/// Strip the opening fence's indent from a body line — up to that width of leading whitespace and
/// NEVER more, so a line indented further than the fence keeps the extra indent that carries the
/// code's own structure. This is what CommonMark does, so it is the code the reader copies off the
/// rendered page; it is also what puts a block's own `open` lines back at column 0, where the hoist
/// in §3 can see them.
let dedent (indent: string) (line: string) =
    let mutable k = 0
    while k < indent.Length && k < line.Length && (line[k] = ' ' || line[k] = '\t') do
        k <- k + 1
    line.Substring k

let extractBlocks (docOf: string -> string) (sourceFile: string) : Block list =
    let doc = docOf sourceFile
    let lines = File.ReadAllLines sourceFile
    let blocks = ResizeArray<Block>()
    let mutable i = 0
    let mutable ordinal = 0
    while i < lines.Length do
        let m = fenceOpen.Match lines[i]
        if m.Success then
            let indent = m.Groups["indent"].Value
            let ticks = m.Groups["ticks"].Value
            let openFence = i                       // 0-based
            let body = ResizeArray<string>()
            let mutable j = i + 1
            while j < lines.Length && not (isFenceClose indent ticks lines[j]) do
                body.Add(dedent indent lines[j])
                j <- j + 1
            if j >= lines.Length then
                fail $"{relative sourceFile}: unterminated {ticks}fsharp fence opened at line \
                       {openFence + 1}."
            ordinal <- ordinal + 1
            blocks.Add
                { Doc = doc
                  SourceFile = sourceFile
                  Ordinal = ordinal
                  StartLine = openFence + 2         // 1-based line of the first code line
                  Indent = indent.Length
                  LineCount = body.Count
                  Code = String.Join("\n", body) }
            i <- j + 1
        else
            i <- i + 1
    List.ofSeq blocks

/// The extractor's cross-check, and it must NOT share the extractor's matcher — that sharing WAS the
/// bug (#176). Both sides used to test `l.TrimEnd() = "```fsharp"`, so both were blind to an indented
/// fence in exactly the same way and could never disagree. A cross-check that carries the bug it is
/// checking for is not a cross-check; it is the fail-open shape (.github#416) reproduced inside the
/// harness built to end it.
///
/// So this is written to a DIFFERENT rule, deliberately, and a permissive one: it is loose about
/// everything the extractor is strict about — any indent, any fence length, a tilde fence, an
/// info-string suffix. It therefore OVER-counts rather than under-counts, which is the only safe
/// direction for a guard whose entire job is to notice DROPPED blocks. Should it ever count a fence
/// the extractor legitimately ignores, the disagreement is a hard failure demanding that someone
/// reconcile the two here — which is the point. Nothing is dropped in silence.
let looseFencePattern = Regex(@"^[ \t]*(?:`{3,}|~{3,})[ \t]*fsharp\b", RegexOptions.Multiline)

// ---------------------------------------------------------------------------------------------
// 1b. Anchors — what makes the positional fixture key CHECKED rather than assumed (#181)
// ---------------------------------------------------------------------------------------------
//
// A fixture is keyed by ORDINAL: `//#block 3` means "the third ```fsharp block in this document". The
// stale-fixture guard in §2 catches an ordinal that has fallen OUT OF RANGE — but that is the rare
// edit. The common one is a block INSERTED AHEAD of an existing block, which shifts every later block
// down by one while every ordinal stays perfectly in range. The guard cannot see it, and the fixture
// silently re-binds to the WRONG block.
//
// This is not hypothetical: it happened in #176. TestSpecTutorial's Part C fixture was keyed
// `//#block 1`; a block was un-hidden ahead of it, Part C became block 2, and `//#block 1` was still a
// valid ordinal in a now-2-block document — so nothing fired and the fixture bound to prose it was
// never written for. It was caught by hand (#180). Nothing in the harness would have told the next
// author, and the failure is not even reliably loud: a fixture that merely supplies an `open` would
// attach to the wrong block and STILL COMPILE, leaving one block silently over-provisioned and another
// under-provisioned. That is the fail-open shape (.github#416) this harness exists to end, reproduced
// inside the harness itself.
//
// So the fixture must ASSERT what it is keyed to, and the harness must PROVE the assertion:
//
//     //#block 3 "type Creep = { Pos: Geometry.Vec2; Hp: int }"
//
// The anchor is a line copied verbatim from the block. §2 requires it to identify EXACTLY ONE block in
// the document, and requires that block to be the one the ordinal names. An insertion then fails with a
// precise, actionable error naming both ordinals — instead of nothing at all.
//
// WHY NOT "the block's first code line", which is the obvious rule and the one this issue proposed?
// Because measuring the corpus refuted it. The blocks open by convention: SIX blocks of fs-gg-game-core
// begin `open FS.GG.Game.Core`, and ELEVEN documents have at least two blocks sharing a first line. An
// anchor that six blocks agree on distinguishes none of them, so the fixture would re-bind across an
// insertion and the anchor check would nod it through — a guard that cannot separate the things it is
// guarding, which is worse than no guard at all, because the green tick is what stops anyone looking.
//
// WHY NOT a content digest? It is total, but it re-keys on every in-place edit of the block — so the
// author who fixes a typo in the prose is met by a hash mismatch, and a guard that cries on correct
// behaviour is one people learn to route around. WHY NOT the markdown line number? Any prose edit above
// the block moves it; strictly more brittle than the ordinal it would replace.
//
// So: the author names ANY line of the block, and UNIQUENESS carries the guarantee. That keeps the
// anchor readable, keeps it stable under edits elsewhere in the block, and — unlike a fixed rule —
// never forces a PROSE edit to satisfy the harness. doodle-jump is exactly why that last part matters:
// two of its blocks legitimately open with the same `type Vec2 = Geometry.Vec2` (the header calls that
// out — a document may re-state Vec2 for the reader's benefit), and a rule demanding a distinct FIRST
// line would have the harness dictating documentation. It names a deeper line instead, and nothing in
// the prose has to move.

/// Anchors are compared with runs of whitespace collapsed. The anchor is an identity claim about a LINE
/// of the block, not an assertion about its column alignment — so re-aligning a trailing comment must
/// not invalidate it. Without this, a purely cosmetic edit inside a block would fail the gate and demand
/// a re-key, and a guard that cries on correct behaviour is one authors learn to route around, which
/// costs far more than the whitespace it was pedantic about.
let normalizeAnchor (l: string) = Regex.Replace(l.Trim(), @"\s+", " ")

/// The lines of a block that a fixture may name as its anchor: non-blank, trimmed, de-duplicated, and
/// VERBATIM (matching normalizes; suggesting does not, because a suggestion is meant to be pasted and
/// should look like the line it came from). Comments and `open`s are eligible — a block of nothing else
/// has no other line to offer, and it is UNIQUENESS, not substance, that makes an anchor prove a binding.
let anchorCandidates (b: Block) =
    b.Code.Split('\n') |> Array.map _.Trim() |> Array.filter (fun l -> l <> "") |> Array.distinct

/// Does this block contain the line the fixture named?
let blockHasAnchor (anchor: string) (b: Block) =
    let target = normalizeAnchor anchor
    anchorCandidates b |> Array.exists (fun l -> normalizeAnchor l = target)

/// A line that says something about THIS block, as opposed to boilerplate every block shares. Used only
/// to RANK a suggestion — never to restrict what an author may name.
let isSubstantive (l: string) =
    not (l.StartsWith "//") && not (Regex.IsMatch(l, @"^open\s+\S+"))

/// The anchor `--list` offers for a block: the first line that occurs in NO OTHER block of the document,
/// preferring a substantive one over an `open` or a comment. Uniqueness is judged under the SAME
/// normalization the check uses, so a suggested anchor is always one the check will accept. `None` when
/// the block shares every one of its lines with a sibling — an unanchorable block, which §2 reports
/// rather than papers over.
let suggestAnchor (docBlocks: Block list) (b: Block) : string option =
    let elsewhere =
        docBlocks
        |> List.filter (fun o -> o.Ordinal <> b.Ordinal)
        |> Seq.collect anchorCandidates
        |> Seq.map normalizeAnchor
        |> Set.ofSeq
    let unique = anchorCandidates b |> Array.filter (normalizeAnchor >> elsewhere.Contains >> not)
    match unique |> Array.tryFind isSubstantive with
    | Some l -> Some l
    | None -> unique |> Array.tryHead

// ---------------------------------------------------------------------------------------------
// 2. Fixtures
// ---------------------------------------------------------------------------------------------
//
// A fixture supplies what a sketch leaves unbound: free values (`creeps`, `cellPx`), types the prose
// never declares (`TowerId`, `DamageType`), or the `type Msg =` header that a DU-continuation
// fragment is written below. One file per document, in <FixtureDir>/<doc>.fs, split into sections:
//
//     //#block 6 "let towers : Tower list = []"
//     let cellPx = 32.0
//     let creeps : Creep list = []     // forward-references the block's own type — needs //#rec
//
//     //#block 7 "let render model = ..."
//     //#skip <why this block cannot be compiled>
//
// The quoted ANCHOR is a line copied verbatim from the block the ordinal names, and it is REQUIRED. The
// ordinal alone is a positional key that silently re-binds when a block is inserted ahead of it (#181);
// the anchor is what turns it into a key the harness can CHECK. Below, `loadFixtures` proves that the
// anchor identifies exactly one block of the document AND that it is the block the ordinal names — so
// an insertion fails with an error naming both ordinals, instead of passing in silence.
//
// You do not have to work the anchor out. `--list` prints a ready-to-paste `//#block N "..."` for every
// block, already chosen to be unique within its document.
//
// A block with no section gets an EMPTY fixture and is compiled as-is: a self-contained block needs
// no ceremony, and a sketch that needs bindings fails loudly with an unbound-identifier error naming
// exactly what to add. Absence is never a skip.

type Fixture =
    | Context of recursive: bool * text: string   // F# text prepended to the block, in its module
    | Skipped of reason: string                   // printed on every run, never silent

// The anchor is greedy to the LAST quote on the line, so a block line that itself contains a string
// literal — `let title = "Pong"` — anchors without escaping.
let blockDirective = Regex(@"^//#block\s+(\d+)\s+""(.*)""\s*$")
let skipDirective = Regex(@"^//#skip\s+(.+)$")
let recDirective = Regex(@"^//#rec\s*$")

/// The anchorless key that WAS the grammar until #181. Matched only to diagnose it: it is by far the
/// most likely "not a directive" an author will write, and answering it with the generic
/// expected-a-directive error would send them looking for a typo instead of telling them what changed.
let anchorlessBlockDirective = Regex(@"^//#block\s+(\d+)\s*$")

/// The `//#block N "<anchor>"` line an author should write for block N, ready to paste. Falls back to a
/// placeholder for the one case that has no answer: a block sharing every line with a sibling, which
/// nothing can anchor and which `loadFixtures` reports on its own terms.
let suggestedDirective (docBlocks: Block list) (n: int) =
    docBlocks
    |> List.tryFind (fun b -> b.Ordinal = n)
    |> Option.bind (suggestAnchor docBlocks)
    |> Option.map (fun a -> $"//#block {n} \"{a}\"")
    |> Option.defaultValue $"//#block {n} \"<a line copied from the block>\""

/// Every `//#…` line must BE a directive the harness understands. A mistyped or half-written one —
/// a bare `//#skip` with the reason left off, a `//#bloc 3` — otherwise parses as an ordinary F#
/// comment and is swallowed into the block's fixture text, and the damage lands nowhere near the
/// mistake: the bare `//#skip` silently loses its skip and the block gets COMPILED, which is the
/// opposite of what its author asked for, with no diagnostic anywhere. A directive that is ignored
/// in silence is the same silent-no-op this whole harness exists to kill, so it fails here, at the
/// line the mistake is on.
let validateDirectives (corpus: Corpus) (doc: string) (docBlocks: Block list) (lines: string[]) =
    lines
    |> Array.iteri (fun i line ->
        if line.StartsWith "//#"
           && not (blockDirective.IsMatch line || skipDirective.IsMatch line || recDirective.IsMatch line) then
            let text = line.Trim()
            let anchorless = anchorlessBlockDirective.Match line
            if anchorless.Success then
                let n = int anchorless.Groups[1].Value
                fail $"{corpus.FixtureDir}/{doc}.fs line {i + 1}: `{text}` is an ANCHORLESS block key. An \
                       ordinal on its own silently re-binds to the wrong block the moment someone inserts \
                       a block ahead of it (#181), so a fixture must also NAME a line of the block it \
                       means. Write: {suggestedDirective docBlocks n} — or run `dotnet fsi \
                       scripts/typecheck-md-blocks.fsx --list`, which prints the line for every block."
            fail $"{corpus.FixtureDir}/{doc}.fs line {i + 1} is not a directive this harness \
                   understands: {text}. Expected `//#block <n> \"<anchor line>\"`, `//#skip <reason>`, or \
                   `//#rec`. A directive that is silently ignored fails as somebody ELSE's bug — refusing.")

/// `strict` is the gate; `not strict` is --list, which must be able to READ a fixture file that the gate
/// would reject — because --list is the tool an author reaches for to FIX one. A repair tool that
/// refuses to run on the broken file it exists to repair is a circle, and it is how a guard ends up
/// resented and routed around. So the parse below accepts the anchorless legacy key too, and every
/// check that could reject it is gated on `strict`.
let loadFixtures (corpus: Corpus) (blocks: Block list) (doc: string) (strict: bool) : Map<int, Fixture> =
    let path = repoPath $"{corpus.FixtureDir}/{doc}.fs"
    if not (File.Exists path) then Map.empty
    else
        let docBlocks = blocks |> List.filter (fun b -> b.Doc = doc)
        let mutable current = None
        let acc = System.Collections.Generic.Dictionary<int, ResizeArray<string>>()
        let anchors = System.Collections.Generic.Dictionary<int, string>()
        let skips = System.Collections.Generic.Dictionary<int, string>()
        let recs = System.Collections.Generic.HashSet<int>()
        let lines = File.ReadAllLines path
        if strict then validateDirectives corpus doc docBlocks lines

        /// (ordinal, anchor) — the anchor is None only for the legacy anchorless key, which `strict`
        /// has already refused by the time we get here.
        let parseBlockKey (line: string) =
            let m = blockDirective.Match line
            if m.Success then Some(int m.Groups[1].Value, Some(m.Groups[2].Value.Trim()))
            else
                let legacy = anchorlessBlockDirective.Match line
                if legacy.Success then Some(int legacy.Groups[1].Value, None) else None

        for line in lines do
            match parseBlockKey line with
            | Some(n, anchor) ->
                // A second section for the same block would silently REPLACE the first, dropping
                // bindings the author wrote and then failing the block with a confusing
                // unbound-identifier error while the binding sits right there in this file.
                if acc.ContainsKey n then
                    fail $"{corpus.FixtureDir}/{doc}.fs declares //#block {n} twice. The second \
                           section would silently discard the first — merge them."
                current <- Some n
                anchor |> Option.iter (fun a -> anchors[n] <- a)
                acc[n] <- ResizeArray()
            | None ->
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
        //
        // This catches only an ordinal that has fallen OUT OF RANGE. The insertion case — every ordinal
        // still in range, every fixture now bound one block too early — is invisible here and is what
        // the anchor check below exists for (#181). Both run: the range check gives the better message
        // when a block is deleted off the end, and it is the cheaper of the two.
        let ordinals = docBlocks |> List.map _.Ordinal |> Set.ofList
        if strict then
            for KeyValue(n, _) in acc do
                if not (ordinals.Contains n) then
                    fail $"{corpus.FixtureDir}/{doc}.fs declares //#block {n}, but {doc} has only \
                           {ordinals.Count} ```fsharp block(s). Stale fixture — a block was deleted or \
                           reordered; re-key the fixture."

        // THE ANCHOR CHECK (§1b). The ordinal says which block the fixture MEANS; the anchor is the
        // fixture's own claim about what that block CONTAINS, and here that claim is proved or the gate
        // fails. Insert a block ahead of an anchored fixture and this is what fires — where the range
        // check above stays perfectly, uselessly silent.
        //
        // Note what is proved: not merely that the anchor is SOMEWHERE in block n, but that it is in
        // block n and in NO OTHER block of the document. An anchor matching two blocks would still nod
        // through the re-key it exists to catch, so ambiguity is a failure in its own right, not a
        // near-miss to be resolved by picking the first match.
        for KeyValue(n, anchor) in (if strict then Seq.toList anchors else []) do
            let matching = docBlocks |> List.filter (blockHasAnchor anchor)
            match matching with
            | [ b ] when b.Ordinal = n -> ()        // the binding is proved — this is the happy path
            | [ b ] ->
                fail $"{corpus.FixtureDir}/{doc}.fs declares //#block {n} anchored to `{anchor}`, but \
                       that line is in block {b.Ordinal} of {doc} — not block {n}. A block was INSERTED \
                       or removed ahead of it, so the ordinal no longer points where this fixture thinks \
                       it does, and the fixture is now bound to the WRONG block (#181). Re-key it to \
                       `//#block {b.Ordinal} \"{anchor}\"` — and check the other sections in this file, \
                       which have almost certainly shifted by the same amount."
            | [] ->
                fail $"{corpus.FixtureDir}/{doc}.fs declares //#block {n} anchored to `{anchor}`, but no \
                       block of {doc} contains that line at all. The block was edited or deleted, so this \
                       fixture is anchored to something that no longer exists. Block {n} is now \
                       `{suggestedDirective docBlocks n}`; `dotnet fsi scripts/typecheck-md-blocks.fsx \
                       --list` prints the current line for every block."
            | many ->
                let where = many |> List.map (fun b -> string b.Ordinal) |> String.concat ", "
                fail $"{corpus.FixtureDir}/{doc}.fs declares //#block {n} anchored to `{anchor}`, but that \
                       line appears in {many.Length} blocks of {doc} (blocks {where}). An anchor that \
                       matches more than one block proves nothing — it would nod through the very re-key \
                       it is here to catch (#181). Name a line UNIQUE to block {n}, e.g. \
                       `{suggestedDirective docBlocks n}`."
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
                // + b.Indent: the code was dedented at extraction, the markdown was not.
                annotate "error" (relative b.SourceFile) docLine (b.Indent + m.Groups["label"].Index + 1)
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
    // the gate itself. `looseFencePattern` is deliberately NOT the extractor's matcher: see its
    // definition in §1, and #176 for the two published blocks that were dropped while the two agreed.
    let independentFenceCount =
        sources |> Array.sumBy (fun f -> looseFencePattern.Matches(File.ReadAllText f).Count)

    if blocks.Length <> independentFenceCount then
        fail $"[{corpus.Id}] extractor disagreement: the extractor parsed {blocks.Length} block(s), but \
               an independent, deliberately-permissive fence scan counted {independentFenceCount} \
               fsharp fence opener(s). One of the two is wrong, and the gate is worth nothing until \
               they agree — so reconcile them here, in the open. If the extractor parsed FEWER, it is \
               either DROPPING published blocks that readers copy (#176 — widen it), or the scan \
               counted a ```fsharp fence NESTED inside another fenced block, which the extractor \
               rightly skips over and the scan does not (teach the scan to skip fenced regions). If it \
               parsed MORE, the scan is the strict one and missed an opener the extractor found."

    if blocks.Length = 0 then
        fail $"[{corpus.Id}] found 0 ```fsharp blocks. Refusing to pass: a gate with no subject is a \
               green light over nothing."

    let fixturesByDoc =
        blocks
        |> List.map _.Doc
        |> List.distinct
        |> List.map (fun d -> d, loadFixtures corpus blocks d (not listOnly))
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
        //
        // The `//#block N "<anchor>"` line is printed VERBATIM and paste-ready, because re-keying a
        // fixture after inserting a block is exactly when an author needs it and exactly when they
        // are least inclined to hand-count fences (#181). The mismatch error in `loadFixtures` points
        // here for the same reason: a guard that tells you it is unhappy but not what to write is a
        // guard people learn to route around.
        let blocksOf = blocks |> List.groupBy _.Doc |> Map.ofList
        for b in blocks do
            let state =
                match fixtureFor b with
                | Some(Skipped r) -> $"SKIP ({r})"
                | Some(Context _) -> "compile (with fixture)"
                | None -> "compile (self-contained)"
            printfn "  %s block %d @ %s:%d — %s" b.Doc b.Ordinal (relative b.SourceFile) b.StartLine state
            match suggestAnchor blocksOf[b.Doc] b with
            | Some a -> printfn "      //#block %d \"%s\"" b.Ordinal a
            | None ->
                // No line of this block is unique within its document, so nothing can anchor it. Say so
                // here rather than printing a directive that `loadFixtures` would reject as ambiguous.
                printfn "      (UNANCHORABLE — every line of this block also appears in another block of \
                         %s. A fixture cannot be keyed to it until one of them says something the other \
                         does not.)" b.Doc
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
    ///     A block may therefore only `open` a namespace that resolves WITHOUT the ambient opens —
    ///     they run after it. That rules out opening a module nested inside one of them, e.g. bare
    ///     `open Geometry` (the scaffold's, inside `FsGg.SkillCheck.Scaffold`). No block does, and
    ///     none should: `FS.GG.Game.Core` ships its own `[<RequireQualifiedAccess>]` `Geometry`, so in
    ///     a reader's file — where both are in scope — `open Geometry` does not compile either
    ///     (FS0892). The corpus reaches the scaffold's geometry the way a reader must, QUALIFIED, as
    ///     `Geometry.Vec2` / `Geometry.toRect`. fs-gg-model-swap was the one block that broke that
    ///     rule; nothing compiled it until #176, and #176 corrected the prose.
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

    // The line directive re-anchors the LINE on the markdown, but the COLUMN it reports is a column in
    // the dedented block text (see Block.Indent). Give the fence's indent back, so a diagnostic on an
    // indented block points at the token it names instead of N columns to its left. A column-0 block —
    // every block here but two — is unaffected.
    let indentAt (file: string) (line: int) =
        blocks
        |> List.tryFind (fun b ->
            b.SourceFile.Replace(@"\", "/") = file
            && line >= b.StartLine
            && line < b.StartLine + b.LineCount)
        |> Option.map _.Indent
        |> Option.defaultValue 0

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
            let mdCol = col + indentAt normalized line
            annotate "error" shown line mdCol $"{code}: {msg}"
            printfn "  %s:%d:%d  %s: %s" shown line mdCol code msg
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

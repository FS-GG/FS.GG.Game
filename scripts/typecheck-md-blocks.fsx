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
// The product-side context. Two files stand up context that this repo does not own, and NOTHING else
// in the harness fabricates anything:
//
//   skill-block-context/_scaffold.fs      `Geometry` (Vec2/Vx/Vy, and the `toPoint`/`toRect` scene
//                                         edge). GENERATED — copied verbatim from the published
//                                         FS.GG.UI.Template package by
//                                         scripts/generate-scaffold-context.fsx, and drift-gated in
//                                         CI. This is the REAL type a scaffolded product ships, not a
//                                         re-declaration of it (FS.GG.Game#189 / FS.GG.Rendering#570).
//   testspec-block-context/_prelude.fs    the host input vocabulary (`Key`, `Button`) the TestSpecs
//                                         assume — owned by the UI layer. Still a RECONSTRUCTION, and
//                                         so still an unenforced cross-repo contract; it says so in
//                                         its own header.
//
// The scaffold used to be a reconstruction too, and that was the more dangerous of the two: a
// hand-written twin keeps compiling after the real type moves under it, holding this gate green over
// skills that now teach a shape the scaffold no longer ships. Generating it from the published source
// makes that failure impossible rather than merely documented — which is the same argument this whole
// harness makes about the blocks themselves.
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
      /// Compiled BEFORE the blocks, in order. Product-side context this repo does not own — the
      /// scaffold is GENERATED from the published template package; `_prelude.fs` is still a
      /// reconstruction. See the header.
      Preludes: string list
      /// `open`ed by every block, after the block's own opens, in this order.
      AmbientOpens: string list
      /// NuGet packages the blocks legitimately need. The VERSION is not repeated here — it is read
      /// from the repo's central pin (see `pinnedVersion`), because a second copy of a version is a
      /// drift bug with a delay fuse.
      PackageRefs: string list
      /// Does block N see the declarations of blocks 1..N-1 of the same document?
      Cumulative: bool
      /// The directory of documents that SPECIFY a product for an implementer to build — the subject
      /// of the framework-citation rule (§3b), and the only documents that carry a `stack:`. `None`
      /// exempts the corpus from §3b entirely, which is a decision a new corpus is forced to MAKE:
      /// an exemption that has to be typed is one somebody chose, and this file's whole history is
      /// gates that checked less than they appeared to.
      GameSpecDir: string option
      /// Namespace for the generated per-block modules.
      ModuleNs: string }

/// The generated product's real geometry, copied verbatim from FS.GG.UI.Template by
/// scripts/generate-scaffold-context.fsx (FS.GG.Game#189). Its namespace is the one the published
/// fragment fixes — `AppRoot` — and the corpora `open` exactly that, which is also what a scaffolded
/// product does. Do not hand-edit it; CI regenerates and fails on drift.
let scaffold = "scripts/skill-block-context/_scaffold.fs"

/// The namespace `_scaffold.fs` declares — READ from the generated file, never restated here.
///
/// Restating it would put the same string in two places and make this harness able to drift from the
/// file it compiles, which is the identical defect one level up from the twin this item deleted. So
/// the generator requires the fragment to declare SOME namespace and does not care which; the corpora
/// then `open` whatever it declares. A template that re-homed the fragment out of `AppRoot` would
/// therefore be absorbed, not break the gate — the one thing that must never happen silently is the
/// namespace VANISHING, which would otherwise surface as an inscrutable wall of `FS0039 Geometry is
/// not defined` in every block of both corpora. That case fails here, loudly, naming the file.
let scaffoldNs =
    let path = repoPath scaffold
    if not (File.Exists path) then
        fail $"{scaffold} is missing. It is GENERATED — restore it with: \
               dotnet fsi scripts/generate-scaffold-context.fsx"
    let m = Regex.Match(File.ReadAllText path, @"(?m)^namespace\s+(?<ns>[\w.]+)\s*$")
    if not m.Success then
        fail $"{scaffold} declares no namespace. It is generated from the published template fragment, \
               which fixes one — regenerate it with: dotnet fsi scripts/generate-scaffold-context.fsx"
    m.Groups["ns"].Value

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
        AmbientOpens = [ "FS.GG.Game.Core"; scaffoldNs ]
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
        // transitively: _scaffold.fs binds `Scene.Point`/`Rect` directly (#165, and now from the
        // REAL published fragment — #189), and a direct dependency carried only as somebody else's
        // transitive one breaks the day that somebody else drops it.
        //
        // It must also stay on the SAME release train as the FS.GG.UI.Template pin the scaffold is
        // generated from: the fragment returns Scene's `Point`/`Rect`, so template and Scene are one
        // coherent set. Both are pinned in Directory.Packages.local.props, which says so.
        //
        // FS.GG.UI.Controls.Elmish is the `app`-profile launcher family
        // (`ControlsElmish.runInteractiveAppWithAudio`) — the function an `app` product must call to
        // get sound. It is here because a block now BINDS it: fs-gg-audio's launcher block (#225).
        // Until then it was deliberately absent, and the reason is worth keeping. The 0.5.0 train this
        // repo used to pin could not reach the function at all, so #215 was forced to ship both
        // launchers as a prose TABLE; #217 moved the train to 0.9.0, which made the function reachable
        // but did NOT add this ref, because a PackageRef whose symbols no block binds compiles nothing
        // and overstates what this gate checks. The pin, the ref, and the block land together or not
        // at all.
        PackageRefs =
            [ "FS.GG.Audio.Core"; "FS.GG.Audio.Host"
              "FS.GG.UI.Canvas"; "FS.GG.UI.Controls.Elmish"; "FS.GG.UI.SkiaViewer"
              "FS.GG.UI.KeyboardInput"; "FS.GG.UI.Scene" ]
        Cumulative = false
        // EXEMPT from the framework-citation rule (§3b), deliberately. A SKILL.md is the framework
        // teaching its OWN module — fs-gg-grids names `Grids` in its title — so the rule would be
        // asking a document to cite the thing it IS. A TestSpec is the opposite: a product handed to
        // an implementer who has never heard of `Pathfinding`, and who will hand-roll it if the spec
        // does not say the word. That asymmetry is the entire reason §3b exists, and it is why this
        // is `None` rather than an oversight.
        GameSpecDir = None
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
        AmbientOpens = [ "FS.GG.Game.Core"; "FsGg.DocCheck.Host"; scaffoldNs ]
        // TestSpecTutorial Part C teaches the scaffold's Expecto test style (`testList`, `Expect.*`).
        // That is a REAL package the scaffolded product's test project references, so the block is
        // compiled against the real thing rather than skipped or faked — the alternative would leave
        // the one block that teaches readers how to write their assertions ungated.
        //
        // FS.GG.UI.Scene, because the shared `_scaffold.fs` prelude binds `Scene.Point`/`Rect` for
        // its `toPoint`/`toRect` edge (#165). This corpus never had Scene on its graph — #150 put it
        // on the SKILLS corpus only — so without this the prelude would not compile here at all. That
        // is now doubly true: since #189 the prelude is the REAL published fragment, whose `toPoint`/
        // `toRect` return Scene types, so Scene is the fragment's own dependency and not an artefact
        // of how we chose to reconstruct it.
        //
        // FS.GG.UI.Symbology, because FOUR specs with a real unit roster (turn-based-tactics,
        // tower-defense, roguelike-dungeon-crawler, sandbox-survival) write their stat -> `Token`
        // ChannelMap as a §8 block. (metroidvania is the fifth with a roster and deliberately does
        // NOT: it commits to per-kind sprites and declines the primitive in prose, per §3b. Do not
        // "fix" that by adding a block.)
        //
        // That map is the ONE part of the symbology story a reader can get subtly wrong for free,
        // and the errors are quiet ones a prose description cannot catch: `Token.Speed` is an int in
        // 0..6, so a spec that hands it px/s is an out-of-domain Error rather than a rounding slip,
        // and `Health`/`Threat` are 0..1 fractions with the same trap. This is the first gate-only
        // pin serving the TESTSPEC corpus rather than the skills; the pin lives in
        // Directory.Packages.local.props for the usual reason (`pinnedVersion` reads it and refuses
        // to invent a version).
        //
        // WHAT THIS PIN DOES NOT BUY, so nobody mistakes green for readable: the gate compiles the
        // ChannelMaps, it does not RUN `Legibility` over them. Capacity/domain findings and whether
        // two kinds are told apart are properties of the mapping, not of its types — the specs carry
        // those as §14 assertions, which is where they belong.
        PackageRefs = [ "Expecto"; "FS.GG.UI.Scene"; "FS.GG.UI.Symbology" ]
        Cumulative = true
        // The subject of §3b. `docs/TestSpecTutorial.md` is in this corpus but NOT under this
        // directory, which is exactly right: it must cite the primitives it teaches (it is a reader's
        // first game — the last place a hand-rolled accumulator belongs) but it specifies no product
        // and carries no `stack:`, so the declaration half skips it BY PATH. A game spec cannot buy
        // the same exemption by deleting its front-matter; that is a hard failure.
        GameSpecDir = Some "docs/TestSpecs/Games"
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
                // Gated on `strict` like every other rejection here: --list is the tool you reach for to
                // REPAIR this file, so it has to survive reading it (see the note on `loadFixtures`).
                if strict && acc.ContainsKey n then
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
        // Both checks below walk their sections in ORDINAL order. `fail` exits on the first problem, and
        // a Dictionary's enumeration order is not a contract — so without the sort, a file with several
        // mis-keyed sections could report a different one on CI than it does locally, or on a re-run. A
        // gate whose diagnosis moves under you is a gate you stop believing.
        let ordinals = docBlocks |> List.map _.Ordinal |> Set.ofList
        if strict then
            for KeyValue(n, _) in acc |> Seq.sortBy _.Key do
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
        for KeyValue(n, anchor) in (if strict then anchors |> Seq.sortBy _.Key |> List.ofSeq else []) do
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
//
// ...AND OVER EVERY FIXTURE (#171). It did not, until #171, and that hole was the same shape as the
// bug this whole harness exists to end. The lint read `Block.Code` — the MARKDOWN — while fixture text
// is spliced into the block's compilation unit downstream, in `generateBlockFile`. So
//
//     type LintProbe = { X: float; Y: float; Width: float; Height: float }
//
// in any fixture file was four violations of the rule this section exists to enforce, and zero
// diagnostics: the gate stayed green. That is the silent-no-op shape (.github#416) reproduced INSIDE
// the harness built to end it — the same sentence this file's header uses about the bug it was
// written for.
//
// And a fixture is where the rule bites HARDEST, not least. A block that declares its own X/Y record
// at least states the collision in prose a reader can see and a reviewer can catch. A FIXTURE that
// hands a block an X/Y-labelled `Tile` teaches the block to bind against the colliding shape from
// OUTSIDE the document: the block then compiles perfectly, the markdown is spotless, and the gate
// ticks green over the precise defect it was built to kill.
//
// The subject is each corpus's <FixtureDir>/*.fs AND its preludes, read WHOLE. The preludes have to be
// named explicitly because one of them lives outside the FixtureDir it serves — `_scaffold.fs` is a
// skills fixture but a testspecs PRELUDE — and it is the sharpest file of the lot: it is compiled into
// every block of BOTH corpora, so a single colliding label there would retrain the whole corpus at
// once. It is also GENERATED, which changes the ADVICE the diagnostic gives (you do not hand-edit it;
// you raise it upstream) but not the rule — a violation there is real, and it is the loudest available.
//
// "Read WHOLE" is deliberate, and it is a SUPERSET of what is spliced: `loadFixtures` ignores a
// fixture file's header (everything above the first //#block) and DISCARDS the context of a //#skip'd
// section, so neither reaches a compilation unit. The lint reads them anyway. A forbidden label parked
// in a skipped section is not exempt — it is one deleted //#skip away from being live, and it is
// already the shape a later author will copy from. Same reasoning as the block lint running over the
// skipped blocks, one level down. The cost is nil: the only text this adds is comments, which are
// stripped before matching.
//
// ONE pass over the deduplicated UNION, not one pass per corpus. `_scaffold.fs` lives in the skills
// FixtureDir and is ALSO a testspecs prelude, so a per-corpus pass would count one defect twice and
// annotate it twice. A gate that reports two errors for one bug is one whose count nobody believes,
// and the count is the thing this section is for.

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

/// Every forbidden record-field label DECLARATION in a piece of F# text, as (0-based line index within
/// the text, 0-based column within that line, the label). The one scanner both lints below share — the
/// markdown and the fixtures are two halves of one compilation unit, and a rule enforced by two
/// separate implementations is a rule with two sets of bugs.
let forbiddenLabelsIn (text: string) : (int * int * string) list =
    text.Split('\n')
    |> Seq.indexed
    |> Seq.collect (fun (i, line) ->
        // Strip a trailing comment before matching — both corpora are full of prose like
        // "// NOT X — that label collides with Scene's Point/Rect", which must not trip its own rule.
        let code =
            match line.IndexOf "//" with
            | -1 -> line
            | at -> line.Substring(0, at)
        // Every match on the line, not just the first: `{ X: float; Y: float }` is TWO violations, and
        // reporting one of them would understate the very thing being counted.
        forbiddenLabelPattern.Matches code
        |> Seq.map (fun (m: Match) -> i, m.Groups["label"].Index, m.Groups["label"].Value))
    |> List.ofSeq

/// WHY the label is forbidden — one sentence, shared, because a block and a fixture break the same rule
/// for the same reason. What differs is the ADVICE, which each lint appends.
let labelRuleWhy (label: string) =
    $"forbidden record-field label '{label}'. X/Y/Width/Height are the labels of Scene's Point/Rect AND \
      of the sim Point/Rect, so a bare record literal resolves against the wrong one \
      (FS.GG.Game#129/#132/#140/#144)."

let lintBlockLabels (blocks: Block list) : int =
    let mutable violations = 0
    for b in blocks do
        for (i, col, label) in forbiddenLabelsIn b.Code do
            violations <- violations + 1
            let docLine = b.StartLine + i
            // + b.Indent: the code was dedented at extraction, the markdown was not.
            annotate "error" (relative b.SourceFile) docLine (b.Indent + col + 1)
                $"{labelRuleWhy label} Positions and velocities go in the scaffold's collision-safe \
                  Geometry.Vec2 (Vx/Vy); scalars take an honest name (LeftX, TopY, WidthPx, \
                  HeightTiles)."
            printfn "  %s:%d  forbidden field label '%s' — use Vx/Vy (Geometry.Vec2) or a scalar \
                     named LeftX/TopY/WidthPx." (relative b.SourceFile) docLine label
    violations

/// The same rule over the FIXTURES — the other half of every block's compilation unit (#171). See the
/// section header for why this is the sharper half, why the subject is read whole, and why it is one
/// pass over a deduplicated union rather than one pass per corpus.
let lintFixtureLabels (corpora: Corpus list) : int =
    let scaffoldPath = Path.GetFullPath(repoPath scaffold)

    let files =
        corpora
        |> List.collect (fun c ->
            let dir = repoPath c.FixtureDir
            if not (Directory.Exists dir) then
                fail $"[{c.Id}] {c.FixtureDir} does not exist — it holds the fixtures every block of \
                       this corpus is compiled with. Refusing to lint a subject that is not there."
            [ yield! Directory.GetFiles(dir, "*.fs")
              yield! c.Preludes |> List.map repoPath ])
        |> List.map Path.GetFullPath
        |> List.distinct
        |> List.sort

    printfn ""
    printfn "── the X/Y/Width/Height label rule, over the fixtures ──"
    printfn "%d fixture/prelude file(s)" files.Length

    // ...and the same fail-closed rule the rest of this harness holds itself to: a lint with no subject
    // is a green light over nothing. Every corpus today declares at least the scaffold as a prelude, so
    // this cannot fire NOW — but it is not a tautology, and that is the point of writing it: a corpus
    // added with no preludes, or a FixtureDir emptied by a rename that misses this file, would
    // otherwise print "OK — no forbidden record-field label" having opened not one file, and the tick
    // is what stops anyone looking.
    if files.IsEmpty then
        fail "[fixtures] the label rule found 0 fixture/prelude file(s) to lint across the selected \
              corpora. Every block is compiled WITH these files, so a corpus that has none means either \
              the fixtures moved and this lint was not told, or a corpus declares no preludes. Refusing \
              to report the fixtures clean over a subject I never opened."

    let mutable violations = 0
    for file in files do
        let rel = relative file
        // The advice is a property of the FILE, not of the violation. The scaffold is GENERATED, so
        // "rename the label" would send its reader to hand-edit a file CI regenerates and fails on any
        // diff in. A violation there is not a local mistake at all — it is the published fragment
        // shipping a colliding label, which is upstream's to fix and everyone's to suffer.
        let advice =
            if file = scaffoldPath then
                $"{scaffold} is GENERATED from the published FS.GG.UI.Template fragment — do NOT \
                   hand-edit it (CI regenerates it and fails on any diff). A colliding label HERE means \
                   the PUBLISHED fragment now ships one, which poisons every block of both corpora and \
                   every scaffolded product alike. Raise it on FS.GG.Rendering."
            else
                "A fixture is spliced into the block's compilation unit, so a colliding label here \
                 teaches the BLOCK to bind against it — and the block then compiles CLEAN, leaving this \
                 gate green over the exact defect it exists to catch (FS.GG.Game#171). Use the \
                 scaffold's Geometry.Vec2 (Vx/Vy), or an honest scalar name (LeftX, TopY, WidthPx)."
        for (i, col, label) in forbiddenLabelsIn (File.ReadAllText file) do
            violations <- violations + 1
            annotate "error" rel (i + 1) (col + 1) $"{labelRuleWhy label} {advice}"
            printfn "  %s:%d  forbidden field label '%s' in a FIXTURE — the block it feeds would \
                     compile clean against it." rel (i + 1) label

    if violations = 0 then
        printfn "OK — no forbidden record-field label in any fixture or prelude."
    else
        printfn "%d forbidden record-field label(s) in the fixtures." violations
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

// ---------------------------------------------------------------------------------------------
// 3a. The two `Geometry` modules must not collide (#189)
// ---------------------------------------------------------------------------------------------
//
// Both corpora open `FS.GG.Game.Core` and THEN the scaffold, and F# MERGES same-named modules from
// two opened namespaces — which is the whole point (see `AmbientOpens`): `Geometry.Vec2` (product)
// and `Geometry.intersects` (Game.Core) both resolve, exactly as in a reader's product. The merge is
// the mechanism, and it has a sharp edge: the scaffold is opened LAST, so on a name they SHARE, the
// scaffold silently WINS.
//
// While the scaffold was a hand-written twin it exposed three names (`Vec2`, `toPoint`, `toRect`) and
// the edge was narrow. Generating it from the published fragment (#189) widened it to nine — `zero`,
// `vec2`, `add`, `sub`, `scale`, `clamp` came along too, which is most of the value, and every one of
// them is a name a geometry library plausibly wants. The day `FS.GG.Game.Core.Geometry` grows a
// `scale` or a `clamp`, every block calling it silently re-resolves to the scaffold's Vec2 overload
// instead of the sim one. If the signatures happen to typecheck, this gate stays GREEN while readers
// copy a block that means something different in their product — a value crossing between the two
// halves of the merged namespace, which is precisely the #129/#132/#140 bug class.
//
// Nothing else would catch it, so assert it: the two modules' member names must be DISJOINT. Today
// they are (Game.Core: intersects/contains/center/ofCenter/aabbContact/…). This is a guard that can
// actually fail — the property it asserts is one an innocent upstream addition breaks.
/// The scaffold's module-level members, taken at the SHALLOWEST `let`/`type` indent in the file —
/// derived rather than hardcoded to 4 spaces, so a reindent upstream cannot silently shrink the set
/// the guards below compare (a guard that quietly checks less is worse than no guard).
///
/// TWO guards read this, and they must read the SAME set. §3a asserts these names are disjoint from
/// `FS.GG.Game.Core.Geometry`'s; §3b resolves the corpus's `Geometry.…` citations against the UNION of
/// the two, because after the merge described below a reader's `Geometry` really does hold both halves.
/// One derivation, therefore, not two — the §2b argument one level up: a rule enforced by two separate
/// implementations is a rule with two sets of bugs.
let scaffoldMemberNames () =
    let scaffoldText = File.ReadAllText(repoPath scaffold)
    let decls =
        Regex.Matches(scaffoldText, @"(?m)^(?<indent>[ ]+)(?:let|type)\s+(?<name>\w+)")
        |> Seq.map (fun m -> m.Groups["indent"].Value.Length, m.Groups["name"].Value)
        |> List.ofSeq
    if decls.IsEmpty then
        fail $"{scaffold} declares no members — it is generated from the published fragment, which \
               declares several. Regenerate: dotnet fsi scripts/generate-scaffold-context.fsx"
    let moduleIndent = decls |> List.map fst |> List.min
    decls |> List.filter (fst >> (=) moduleIndent) |> List.map snd |> Set.ofList

let assertGeometryModulesDisjoint () =
    let scaffoldNames = scaffoldMemberNames ()

    // Game.Core's `Geometry`, by reflection over the SAME assembly the blocks compile against.
    let asm = Reflection.Assembly.LoadFrom coreDll
    match asm.GetType "FS.GG.Game.Core.Geometry" with
    | null ->
        fail "FS.GG.Game.Core.Geometry not found in the built assembly. The corpora's ambient opens \
              merge it with the scaffold's `Geometry`, so its absence means the merge this gate \
              reproduces is not the one a reader gets."
    | geom ->
        let coreNames =
            geom.GetMembers(Reflection.BindingFlags.Public ||| Reflection.BindingFlags.Static)
            |> Seq.map _.Name
            |> Seq.append (geom.GetNestedTypes() |> Seq.map _.Name)
            |> Set.ofSeq

        let collisions = Set.intersect scaffoldNames coreNames
        if not collisions.IsEmpty then
            let names = collisions |> Set.toList |> String.concat ", "
            fail $"the scaffold's `Geometry` and `FS.GG.Game.Core.Geometry` both declare: {names}. The \
                   corpora open Game.Core and THEN the scaffold, and F# merges same-named modules — so \
                   on a shared name the SCAFFOLD WINS, silently, in every block. If the signatures \
                   happen to typecheck, this gate goes green while readers copy a block that means \
                   something else in their product (the #129/#132/#140 crossing-bug class). Resolve it \
                   deliberately: rename in FS.GG.Game.Core, or raise it on FS.GG.Rendering — do not \
                   delete this check."

if not listOnly then
    assertGeometryModulesDisjoint ()

// ---------------------------------------------------------------------------------------------
// 3b. The framework-citation rule (#222, #230)
// ---------------------------------------------------------------------------------------------
//
// A TYPECHECK CANNOT SEE THIS ONE EITHER, and for the same reason §2b cannot be a typecheck: the
// defect is in the PROSE, and prose compiles to nothing.
//
// tower-defense §4.4 says "the path is **computed** by A* over the grid (4-neighbour, unit cost)".
// sandbox-survival §4.9 says lighting propagates by "**BFS flood fill**". `FS.GG.Game.Core` ships
// `Pathfinding.astar` and `Pathfinding.bfs` — deterministic, property-tested, and built for exactly
// this. Neither spec names either. Every ```fsharp block in both documents typechecks perfectly,
// because a spec that never writes the algorithm down has nothing to typecheck: the reader writes it,
// from the prose, into their product. **The reader copies the spec, not the framework**, so the
// framework's tested primitive loses to the spec's prose every time.
//
// That is the #222 defect, and #230 is the finding that it was never one spec: the same shape sits in
// the whole corpus. `FixedStep.drain` — which drains an accumulator AND caps the spiral of death —
// was hand-described in all sixteen documents. `Rng` — splitmix64, splittable, and a VALUE, so it can
// live in an Elmish model honestly — was hand-described as `System.Random` in ten of them and
// re-invented under the name `RngState` in three more, while roguelike-dungeon-crawler's own §13
// forbids the `System.Random` the other ten mandate. A corpus can contradict itself for a long time
// when nothing reads it.
//
// THE RULE IS "CITE THE MODULE", NOT "USE THE MODULE". This is the load-bearing choice, so it is
// worth being exact about why.
//
// A reimplementation can be legitimate. sandbox-survival's enemy pathing is "greedy local (no A*)",
// and for a v1 horde that is a real design decision, not an oversight. A gate that demanded
// `Pathfinding.astar` there would be demanding a WORSE game, and would be routed around within a
// week. But the honest way to state that decision NAMES THE PRIMITIVE IT DECLINES — "deliberately not
// `Pathfinding.astar`, because …" — and a reader who meets that sentence learns three things at once:
// the primitive exists, it was considered, and here is why this game does not want it. A reader who
// meets "greedy local (no A*)" learns only that they are on their own.
//
// So one check serves both outcomes, and there is NO opt-out marker — no `<!--#no-framework-->` to
// grep for, nothing invisible in the rendered page, nothing that rots into a silent exemption. Use
// the primitive and cite it, or decline it and cite it. The gate cannot tell those apart and does not
// need to: the citation is the thing that stops the next reader hand-rolling in ignorance, and it is
// the thing #222's §4.4 was missing.
//
// A CITATION IS `Module.member`, AND IT MUST RESOLVE. Not a bare mention of the module name — that
// test is free to pass and therefore worthless here. EVERY spec in this corpus already carried a
// model field called `Rng`, so "the document says Rng somewhere" was satisfied, corpus-wide, by the
// very documents teaching `System.Random`. And the resolve half is not hypothetical either:
// roguelike-dungeon-crawler §5 drew room counts from `Rng.range(0,2)`, and `Rng.range` DOES NOT
// EXIST. A citation to a function the framework does not ship is not a citation — it is a second way
// to strand the reader, and it is one this gate now refuses. Both halves fall out of one question
// asked against the REAL built assembly: does `Module.member` name something Game.Core actually has?
//
// WHAT IS DELIBERATELY NOT IN THE MAP. `Physics` and `Geometry` ship real primitives and are absent
// from `algorithmRules` on purpose. `Physics` is a rigid-body world (bodies, manifolds, restitution);
// a Pong paddle reflecting a ball is not a `Physics.step`, and a keyword rule on "collision" would
// order eleven arcade specs to adopt a solver they are right not to want — an overclaiming `stack:`
// is the same lie as a silent one, in the other direction. `Geometry` is the module §3a exists for:
// the corpus's `Geometry.toRect` is the SCAFFOLD's, not Game.Core's, so a rule demanding a
// `Geometry.…` citation would be satisfied by a citation of a different module that merely shares its
// name. Both are judgement calls a human makes per spec; the six rules below are the ones a machine
// can make. That boundary is the honest one, and naming it here is how it stays deliberate.
//
// SCOPE. TestSpecs only — a `Corpus` opts in by declaring `GameSpecDir`, so a corpus added later must
// DECIDE rather than default into silence. A SKILL.md is the framework teaching its own module and
// names it in its title; a TestSpec is a product handed to an implementer who has never heard of
// `Pathfinding`, and that asymmetry is the whole point. Within the corpus the tutorial is checked too
// — it teaches the reader their first game, so it is the last place a hand-rolled accumulator belongs
// — but it is not a game spec and has no `stack:`, so the §3b-c `stack:` rules skip it BY PATH,
// not by the absence of the key they check. A game spec cannot escape them by deleting its own
// front-matter: that is a hard failure below.
//
// WHAT THE `stack:` RULE DOES NOT ASSERT, AND WHY. It checks that a spec built on Game.Core DECLARES
// a `framework:`, and that every module the declaration CLAIMS is cited somewhere in the body. It does
// NOT check the other direction — that every module the body cites is claimed — and that omission is
// deliberate, load-bearing, and was found by this gate failing on correct prose.
//
// turn-based-tactics §1 says: "If you find yourself hand-rolling a Dijkstra, a Bresenham, or an FoV,
// check the framework first." That sentence is the corpus at its BEST — it is the fix #222 shipped.
// And a `cited => claimed` rule reads it as proof that the game uses `Fov`, and orders the spec to
// declare it. But turn-based-tactics has no fog and does not use `Fov`; #222 deliberately left it out,
// because a stack that sends a reader to a module the game never touches is the same lie as a silent
// one, in the other direction.
//
// The gate cannot tell "this document DESCRIBES a visibility sweep" from "this document MENTIONS one,
// to tell you not to write it". Only the author knows. So the machine asserts the half it can prove —
// a claim with no citation behind it is certainly false — and leaves the half it cannot to review. A
// lint that cannot distinguish two cases must not pretend it can: it would be wrong exactly where the
// prose is best, and a rule that punishes good prose is a rule that gets deleted.
//
// THE LIMIT, STATED. This is a TEXTUAL rule at DOCUMENT granularity, like §2b. A spec that cites
// `Pathfinding.astar` in §4.4 and then hand-rolls a Dijkstra in §9 passes, and a spec could satisfy it
// with a citation parked in a footnote. That is real, and it is the correct trade: the failure this
// harness keeps meeting is the SILENT one — a corpus where nobody ever wrote the module's name down —
// and section-level proximity heuristics would buy a little precision for a lot of arbitrariness. The
// gate makes the OMISSION loud. A reviewer still has to read the prose.

/// F# compiles a module whose name collides with a type of the same name — `Rng` the struct and `Rng`
/// the module — to a CLR class with a `Module` suffix. Recover the name the SOURCE writes, which is
/// the name a spec cites: `RngModule` -> `Rng`, `CommandModule` -> `Command`.
let sourceModuleName (clrName: string) =
    if clrName.EndsWith "Module" && clrName.Length > "Module".Length then
        clrName.Substring(0, clrName.Length - "Module".Length)
    else
        clrName

let genericArity = Regex(@"`\d+$", RegexOptions.Compiled)

/// Every top-level MODULE of the built FS.GG.Game.Core, with its public members and nested types — by
/// reflection over the SAME assembly the blocks compile against, exactly as §3a does. Not a list
/// maintained here: a hand-kept copy of the framework's surface is a copy that goes stale, and a
/// citation rule checked against a stale copy is one that starts rejecting real functions and
/// accepting deleted ones.
///
/// MODULES only (`abstract` + `sealed` = an F# module), not records or unions. `Point.X` is an
/// INSTANCE field, so a doc writing `Point.X` means the field and would fail a static-member lookup —
/// a false positive on prose that is perfectly correct. The citations that carry the risk this section
/// is about are module functions (`Pathfinding.astar`, `Rng.ofSeed`), and those resolve exactly.
let coreModules: Map<string, Set<string>> =
    if listOnly then
        Map.empty
    else
        let asm = Reflection.Assembly.LoadFrom coreDll

        let byModule =
            asm.GetExportedTypes()
            |> Seq.filter (fun t ->
                t.Namespace = "FS.GG.Game.Core" && not t.IsNested && t.IsAbstract && t.IsSealed)
            |> Seq.map (fun t ->
                let members =
                    t.GetMembers(Reflection.BindingFlags.Public ||| Reflection.BindingFlags.Static)
                    |> Seq.map _.Name
                    |> Seq.append (t.GetNestedTypes() |> Seq.map _.Name)
                    // `get_baseStep` is the property accessor for `baseStep`, which is already in the
                    // set; `Tags` is the compiler's union-case tag class. Neither is a name a spec cites.
                    |> Seq.filter (fun n ->
                        not (n.StartsWith "get_" || n.StartsWith "set_" || n = "Tags"))
                    |> Seq.map (fun n -> genericArity.Replace(n, ""))
                    |> Set.ofSeq
                sourceModuleName t.Name, members)
            |> Map.ofSeq

        if byModule.IsEmpty then
            fail "no public modules found in the built FS.GG.Game.Core. The framework-citation rule \
                  (§3b) resolves every `Module.member` the corpus cites against this set, so an empty \
                  one would pass every citation in every document — including citations of functions \
                  that do not exist. Refusing to report the corpus clean against a surface I could not \
                  read."

        // The corpora open Game.Core and THEN the scaffold, and F# MERGES the two `Geometry` modules
        // (§3a) — so in a reader's product, and in every block here, `Geometry` really does hold both
        // halves. `Geometry.toRect` is the scaffold's; `Geometry.intersects` is Game.Core's; both
        // resolve, and a citation rule that knew only one half would reject four specs for writing
        // correct code. §3a has just proved the two are disjoint, so this union loses nothing.
        byModule
        |> Map.change "Geometry" (function
            | Some core -> Some(Set.union core (scaffoldMemberNames ()))
            | None ->
                fail "FS.GG.Game.Core.Geometry vanished between §3a and §3b."
                None)

/// An algorithm `FS.GG.Game.Core` ships, and the words a spec reaches for when it is about to
/// hand-roll it instead.
type AlgorithmRule =
    { /// The module that ships it. Asserted to EXIST in the assembly below — see `assertRulesResolve`.
      Owner: string
      /// What the spec is about to reimplement, for the diagnostic.
      What: string
      /// Where to point the reader instead.
      Use: string
      Pattern: Regex }

let algorithmRule owner what use_ pattern =
    { Owner = owner
      What = what
      Use = use_
      Pattern = Regex(pattern, RegexOptions.IgnoreCase ||| RegexOptions.Compiled) }

let algorithmRules =
    [ algorithmRule
        "Pathfinding"
        "a graph search"
        "Pathfinding.astar / .bfs / .distanceField — and .flowField, which is what MANY agents \
         converging on ONE goal actually want (one field, not N searches)"
        // `(?<![*\w])A\*(?!\*)` and not `\bA\*`: markdown bold is `**A**`, which contains the two
        // characters `A*` and matched the naive pattern. tower-defense's upgrade trees fork into
        // "branch **A**/**B**", so the naive rule fired on a document for reasons having nothing to
        // do with pathfinding — and a rule that cries wolf on bold text is one whose next real
        // finding gets waved through.
        //
        // No `DFS`/`depth-first`, deliberately. Game.Core ships A*, BFS and a Dijkstra
        // (`distanceField`) — every one of them a shortest-path or flood primitive. A depth-first walk
        // is a DIFFERENT algorithm used for a different job (carving a maze, walking a tree), and
        // Game.Core has no answer for it. Firing here would order a spec to cite `Pathfinding` and
        // then hand its reader four functions that cannot do what they asked — advice worse than
        // silence, and the rule would be routed around rather than obeyed.
        @"(?<![*\w])A\*(?!\*)|\bDijkstra\b|\bBFS\b|breadth[- ]first\
          |flood[- ]?fill|\bpathfind\w*|\bshortest path\b"
      algorithmRule
        "Los"
        "a line between two cells"
        "Los.line (Bresenham) / Los.lineOfSight"
        @"\bBresenham\b|line[- ]of[- ]sight|\bLoS\b"
      algorithmRule
        "Fov"
        "a visibility sweep"
        "Fov.fov (symmetric shadowcasting)"
        @"\bshadow[- ]?cast\w*|field[- ]of[- ]view|\bFoV\b"
      algorithmRule
        "SpatialGrid"
        "a broadphase"
        "SpatialGrid.build / .query / .queryRadius"
        @"\bspatial (grid|hash)\b|\buniform grid\b|\bbroad[- ]?phase\w*|\bquadtree\b"
      algorithmRule
        "FixedStep"
        "a fixed-timestep accumulator"
        "FixedStep.drain, which drains the accumulator AND caps the spiral of death in one call"
        @"\bfixed[- ](time)?step\b|\baccumulator\b|spiral[- ]of[- ]death"
      algorithmRule
        "Rng"
        "a seeded PRNG"
        "Rng — splitmix64, and a VALUE, so it lives in an Elmish model honestly (Rng.split gives \
         independent sub-streams)"
        @"\bRNG\b|\bPRNG\b|\bSystem\.Random\b|\bxorshift\b|\bxoshiro\b|\bsplitmix\b|\bPCG\b\
          |\brandom\w*|\bre-?seed\w*|\bseeded\b" ]

/// ...and a rule that names a module the framework does not have can never fire (#176's lesson, which
/// this file learned once already: the block-count cross-check shared the extractor's own predicate,
/// so the two agreed by construction and passed over two blocks nothing compiled).
///
/// Rename `Pathfinding` in Game.Core and every pattern above goes on matching, every citation demand
/// goes on being satisfiable by a module that no longer exists, and this gate reports a clean corpus
/// forever. So the map is checked against the assembly, and a stale entry is a HARD failure, not a
/// quietly dead rule.
let assertRulesResolve () =
    for r in algorithmRules do
        if not (coreModules.ContainsKey r.Owner) then
            let known = coreModules |> Map.keys |> Seq.sort |> String.concat ", "
            fail $"§3b's algorithm map owns '{r.Owner}' to FS.GG.Game.Core, which has no such module. \
                   Either it was renamed and this map was not told — in which case every rule for it \
                   is now DEAD, and this gate would report the corpus clean while nothing enforces the \
                   citation — or the map has a typo. Known modules: {known}."

if not listOnly then
    assertRulesResolve ()

/// The YAML front-matter block, and the body BELOW it. The split matters: the `stack:` line names the
/// modules, so a body that included it would let a document satisfy the "cite the primitive" rule with
/// its own declaration — the two halves of §3b would prove each other and neither would read the prose.
let splitFrontMatter (text: string) =
    let m = Regex.Match(text, @"\A---\r?\n(?<fm>.*?)\r?\n---\r?\n", RegexOptions.Singleline)
    if m.Success then Some m.Groups["fm"].Value, text.Substring m.Length else None, text

/// Every `Module.member` in a piece of markdown that names a REAL Game.Core module, as
/// (module, member, 0-based line index). Prose and code alike — `Pathfinding.reachable` in a sentence
/// is exactly as much a citation as one in a block, and §4.4 (the defect that started this) was prose.
let citation = Regex(@"\b(?<m>[A-Z][A-Za-z0-9]*)\.(?<x>[A-Za-z_][A-Za-z0-9_']*)", RegexOptions.Compiled)

/// `Pathfinding.fsi` is a FILE, not a citation of a member called `fsi`. Pointing a reader at the
/// signature file is normal here — the skills corpus does it a dozen times (`Los.fsi`, `Fov.fsi`,
/// `Effects.fsi`) — and the day a TestSpec does, the resolve rule would report
/// "`Pathfinding.fsi` does not exist in FS.GG.Game.Core" over prose that is entirely correct.
///
/// That is not a small blemish: this section's own header argues that a lint which fires on good prose
/// is a lint that gets routed around, and it would be true of this one. So a `Module.<ext>` is not a
/// citation, and it is not a violation either — it is a filename, and the rule has nothing to say
/// about it.
let sourceFileExtensions = set [ "fs"; "fsi"; "fsx"; "fsproj"; "md"; "dll"; "json"; "yml"; "yaml" ]

let citationsIn (text: string) =
    text.Split('\n')
    |> Seq.indexed
    |> Seq.collect (fun (i, line) ->
        citation.Matches line
        |> Seq.map (fun m -> m.Groups["m"].Value, m.Groups["x"].Value, i)
        |> Seq.filter (fun (m, x, _) ->
            coreModules.ContainsKey m && not (sourceFileExtensions.Contains x)))
    |> List.ofSeq

/// The modules a `stack:` line CLAIMS. The parenthetical is prose — `(Pathfinding; Los for ranged
/// LoS)` — so this reads the real module names out of it rather than trying to parse a grammar the
/// authors were never given. A misspelled claim simply is not found, and the underclaim check below
/// then reports the module as undeclared, which is the true statement anyway.
let claimedModules (framework: string) =
    Regex.Matches(framework, @"\b[A-Z][A-Za-z0-9]*\b")
    |> Seq.map _.Value
    |> Seq.filter coreModules.ContainsKey
    |> Set.ofSeq

let lintFrameworkCitations (corpora: Corpus list) : int =
    let subjects =
        corpora |> List.choose (fun c -> c.GameSpecDir |> Option.map (fun dir -> c, dir))

    printfn ""
    printfn "── the framework-citation rule (§3b), over the TestSpecs ──"

    if subjects.IsEmpty then
        // Not reachable while the testspecs corpus is selected, and deliberately not written as if it
        // were: `--corpus skills` legitimately selects nothing here, and must SAY so rather than print
        // a tick over a subject it never opened. The tick is what stops anyone looking.
        printfn "no corpus in this selection declares a GameSpecDir — the framework rule checked nothing."
        0
    else

    let mutable violations = 0

    for (corpus, gameSpecDir) in subjects do
        let specRoot = Path.GetFullPath(repoPath gameSpecDir) + string Path.DirectorySeparatorChar
        let sources = corpus.Sources()
        printfn "%d document(s) in %s; %d module(s) in FS.GG.Game.Core"
            sources.Length corpus.Label coreModules.Count

        for source in sources do
            let rel = relative source
            let text = File.ReadAllText source
            let frontMatter, body = splitFrontMatter text
            // The line the body starts on, so a diagnostic lands on the line the AUTHOR sees.
            let bodyOffset = text.Substring(0, text.Length - body.Length).Split('\n').Length - 1

            let cited = citationsIn body
            let citedModules = cited |> List.map (fun (m, _, _) -> m) |> Set.ofList

            // ---- (1) every citation must RESOLVE against the real assembly ----
            for (m, x, i) in cited do
                if not (coreModules[m].Contains x) then
                    violations <- violations + 1
                    let near =
                        coreModules[m]
                        |> Set.filter (fun k -> k.StartsWith(x.Substring(0, min 3 x.Length), StringComparison.OrdinalIgnoreCase))
                        |> Set.toList
                    let hint =
                        if near.IsEmpty then
                            let all = coreModules[m] |> Set.toList |> List.sort |> String.concat ", "
                            $"{m} ships: {all}."
                        else
                            let suggestions = String.concat " / " near
                            $"Did you mean {suggestions}?"
                    annotate "error" rel (bodyOffset + i + 1) 1
                        $"`{m}.{x}` does not exist in FS.GG.Game.Core. A citation to a function the \
                          framework does not ship strands the reader exactly as a missing citation \
                          does — they go and write it themselves. {hint}"
                    printfn "  %s:%d  cites `%s.%s`, which FS.GG.Game.Core does not ship."
                        rel (bodyOffset + i + 1) m x

            // ---- (2) an algorithm the framework ships must be cited ----
            let fired =
                algorithmRules
                |> List.choose (fun r ->
                    let m = r.Pattern.Match body
                    if m.Success then
                        let line = body.Substring(0, m.Index).Split('\n').Length - 1
                        Some(r, m.Value, line)
                    else
                        None)

            for (r, matched, line) in fired do
                if not (citedModules.Contains r.Owner) then
                    violations <- violations + 1
                    annotate "error" rel (bodyOffset + line + 1) 1
                        $"this document describes {r.What} (\"{matched}\") and never cites \
                          `{r.Owner}` — the module FS.GG.Game.Core ships it in. The reader copies the \
                          SPEC, not the framework, so an uncited primitive is a primitive that gets \
                          hand-rolled (FS.GG.Game#222). Use {r.Use} — or, if reimplementing it is \
                          DELIBERATE, say so and name what you are declining (\"deliberately not \
                          `{r.Owner}.…`, because …\"): a reader who meets that sentence learns the \
                          primitive exists; a reader who meets \"{matched}\" learns nothing."
                    printfn "  %s:%d  describes %s (\"%s\") but never cites `%s`."
                        rel (bodyOffset + line + 1) r.What matched r.Owner

            // ---- (3) the `stack:` declaration — game specs only ----
            // BY PATH, not by whether the document happens to have the key being checked. The tutorial
            // is in this corpus and is not a game spec; a game spec that DELETED its stack: line would
            // otherwise exempt itself from the rule by breaking it.
            if source.StartsWith(specRoot, StringComparison.Ordinal) then
                let stack =
                    frontMatter
                    |> Option.bind (fun fm ->
                        let m = Regex.Match(fm, @"(?m)^stack:\s*(?<v>.+)$")
                        if m.Success then Some m.Groups["v"].Value else None)

                match stack with
                | None ->
                    violations <- violations + 1
                    annotate "error" rel 1 1
                        "no `stack:` in the front-matter. Every TestSpec declares the stack it is \
                         built on, and §3b's framework rules are enforced through it — a spec without \
                         one is not an exempt spec, it is an unreadable one."
                    printfn "  %s:1  no `stack:` front-matter." rel
                | Some stack ->
                    let framework =
                        let m = Regex.Match(stack, @"framework:\s*""(?<v>[^""]*)""")
                        if m.Success then Some m.Groups["v"].Value else None

                    // Only a SUGGESTION for the diagnostic below — never a requirement. See the
                    // "what the stack rule does NOT assert" note in the section header.
                    let required = fired |> List.map (fun (r, _, _) -> r.Owner) |> Set.ofList

                    match framework with
                    | None when required.IsEmpty && citedModules.IsEmpty -> ()
                    | None ->
                        violations <- violations + 1
                        // Suggest from whichever set is non-empty: a spec can reach this branch by
                        // CITING a module without tripping an algorithm rule, and "FS.GG.Game.Core ()"
                        // is not a suggestion, it is a puzzle.
                        let names =
                            Set.union required citedModules |> Set.toList |> String.concat "; "
                        annotate "error" rel 1 1
                            $"this spec is built on FS.GG.Game.Core and its `stack:` does not say so. \
                              Add: framework: \"FS.GG.Game.Core ({names})\". The stack is what a reader \
                              checks to see what they are allowed to lean on; 8 of these 15 specs \
                              `open FS.GG.Game.Core` in a block and NONE of them declared it \
                              (FS.GG.Game#230)."
                        printfn "  %s:1  uses FS.GG.Game.Core; `stack:` declares no `framework:`." rel
                    | Some framework ->
                        if not (framework.Contains "FS.GG.Game.Core") then
                            violations <- violations + 1
                            annotate "error" rel 1 1
                                $"`framework:` is \"{framework}\" and does not name FS.GG.Game.Core, \
                                  which this spec is built on."
                            printfn "  %s:1  `framework:` does not name FS.GG.Game.Core." rel

                        let claimed = claimedModules framework

                        // Overclaim: the stack names a module the body never cites ANYWHERE. That
                        // claim cannot be true, so it is the one direction a machine can call — and
                        // it is the direction #222 got right by hand, deliberately NOT claiming `Fov`
                        // for turn-based-tactics because that game has no fog.
                        for m in Set.difference claimed citedModules do
                            violations <- violations + 1
                            annotate "error" rel 1 1
                                $"`framework:` claims `{m}`, but no `{m}.…` is cited anywhere in this \
                                  spec. An overclaiming stack is as false as a silent one — it sends \
                                  the reader to a module this game does not use."
                            printfn "  %s:1  `framework:` claims `%s`, which this spec never cites." rel m

    if violations = 0 then
        printfn "OK — every framework algorithm is cited, every citation resolves, every `stack:` is honest."
    else
        printfn "%d framework-citation violation(s)." violations

    violations

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
            fail $"[{corpus.Id}] {p} is missing — it stands up product-side context the blocks are \
                   compiled against and FS.GG.Game.Core does not ship. If it is the scaffold, it is \
                   GENERATED: restore it with  dotnet fsi scripts/generate-scaffold-context.fsx"

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
    // compiler never sees but a reader still copies. The FIXTURE half of the rule runs once, in §4,
    // over the union of the selected corpora's fixture files.
    let labelViolations = lintBlockLabels blocks
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
    ///     `open Geometry` (the scaffold's, inside the fragment's own `AppRoot`). No block does, and
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

// The fixture half of the label rule (§2b), FIRST: it is textual, it is instant, and it is the half
// that — when it fires — explains why an otherwise-clean corpus is teaching the wrong shape. `--list`
// compiles nothing and checks nothing, so it stays out of this exactly as it stays out of the block
// lint.
let fixtureLabelViolations = if listOnly then 0 else lintFixtureLabels selected

// The framework-citation rule (§3b), also before the compile, and for the same reason: it is textual,
// it is instant, and when it fires it explains why a corpus that typechecks perfectly is still
// teaching the reader to hand-roll a primitive the framework ships.
let citationViolations = if listOnly then 0 else lintFrameworkCitations selected

let results = selected |> List.map (fun c -> c.Id, checkCorpus c)
let totalErrors = (results |> List.sumBy snd) + fixtureLabelViolations + citationViolations

printfn ""

if listOnly then exit 0

if totalErrors = 0 then
    printfn "typecheck-md-blocks: OK — every ```fsharp block typechecks against FS.GG.Game.Core, \
             neither the blocks nor the fixtures they are compiled with break the label rule, and every \
             TestSpec cites the framework primitives it is built on."
    exit 0

for (id, n) in results do
    if n > 0 then printfn "typecheck-md-blocks: %s — %d error(s)." id n
if fixtureLabelViolations > 0 then
    printfn "typecheck-md-blocks: fixtures — %d forbidden record-field label(s)." fixtureLabelViolations
if citationViolations > 0 then
    printfn "typecheck-md-blocks: testspecs — %d framework-citation violation(s)." citationViolations

fail $"{totalErrors} error(s): a ```fsharp block in a published document either does not typecheck \
       against FS.GG.Game.Core, or breaks the X/Y/Width/Height label rule — or a FIXTURE the blocks are \
       compiled with breaks it, which is the worse case: the block it feeds binds against the colliding \
       shape and compiles perfectly clean (#171) — or a TestSpec describes an algorithm the framework \
       already ships and never names the module that ships it, which is how a reader ends up \
       hand-rolling a tested primitive (#222/#230). Readers copy these documents into their product — \
       fix the prose (or, if a block is a sketch missing a binding, add it to the corpus's fixture file)."

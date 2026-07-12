// Generates scripts/skill-block-context/_scaffold.fs from the PUBLISHED scaffold geometry.
//
// WHY THIS EXISTS (FS.GG.Game#189, answering FS.GG.Rendering#570).
//
// `_scaffold.fs` is the prelude every md-block corpus compiles before the blocks: it supplies
// `Geometry.Vec2` (Vx/Vy), the scaffold's collision-safe vector, which a generated product owns and
// FS.GG.Game.Core does not ship. It used to be a HAND-WRITTEN TWIN of that type — a re-declaration,
// on the stated grounds that the real `Vec2` "cannot be referenced" because it lives in the generated
// product rather than in a package.
//
// That is true of a REFERENCE and false of the SOURCE. FS.GG.Rendering's template package packs its
// repo under `content/`, so the canonical fragment ships INSIDE the FS.GG.UI.Template NuGet package
// at a stable path, and can simply be restored and copied:
//
//     content/template/fragments/vec2/src/Product/Vec2.fs
//
// So the twin is not necessary, and a twin is exactly the thing the md-block gate exists to prevent
// one level up: a reconstruction that keeps compiling after the real type moves under it, holding the
// gate green over skills that now teach a shape the scaffold no longer ships. The re-declaration was
// an UNENFORCED CROSS-REPO CONTRACT — it said so in its own header, and asked to be deleted the day
// the geometry became referenceable. This script is that deletion.
//
// WHAT IT BUYS BEYOND DELETING A DUPLICATE. The twin carried only what the gate happened to need:
// `Vec2`, and hand-reconstructed `toPoint`/`toRect` (#165). The published fragment also ships `zero`,
// `vec2`, `add`, `sub`, `scale` and `clamp` — so a skill that teaches any of them could not be
// compiled at all before this, and the gate had no way to say so. Generating from source makes the
// gate's context a superset by construction instead of by somebody remembering to widen the twin.
//
// It also replaces reconstructed helper BODIES with the real ones. The twin's `toRect` did not guard
// a negative size; the published one takes `abs` of both extents, so a stray sign cannot invert the
// rect. A gate that typechecks a block against a fiction still shows a green tick, and the tick is
// what stops anyone looking.
//
// VERBATIM, deliberately. The fragment is copied byte-for-byte under a generated banner: no namespace
// rewrite, no reformatting, no trimming of the parts this repo does not use. Rendering pins exactly
// that property with a merge-blocking test (`Feature570PublishedScaffoldGeometryTests`) — the file
// carries no `dotnet new` conditional, has a fixed namespace (`AppRoot`), and compiles outside a
// generated product against published packages alone. Any transformation here would be a new
// divergence to defend, and would put this script back in the business the twin was in. The corpora
// therefore `open AppRoot` (see `AmbientOpens` in typecheck-md-blocks.fsx), which is also what a real
// product does.
//
// The ONE normalisation is line endings (CRLF -> LF). The fragment ships LF today; normalising means
// a future re-pack on a CRLF machine cannot make the drift gate flap on a file nobody edited. It
// cannot change F# semantics, so `verbatim` survives it.
//
// FAIL-CLOSED. This script writes the context that a gate then reports green over, so every way it
// could produce a PLAUSIBLE-BUT-WRONG scaffold is a hard failure, not a fallback:
//
//   - no central version pin                -> refuse (never invent a version)
//   - restore fails, or NuGet SUBSTITUTES a
//     version (NU1603/NU1605/NU1608)        -> refuse (a substituted template is a different scaffold)
//   - the fragment is not in the package     -> refuse LOUDLY (this is the contract Rendering guards;
//                                              if an `Exclude` ever stops it shipping we must go RED,
//                                              not silently keep the last-known-good copy on disk)
//   - the fragment does not LOOK like the
//     scaffold geometry                     -> refuse (never overwrite the prelude with something that
//                                              restored successfully but is not `Geometry.Vec2`)
//
// The last one matters most: the failure this whole item is about is a gate that is green over the
// wrong subject. A generator that cheerfully writes whatever it found reintroduces it.
//
// Usage:
//   dotnet fsi scripts/generate-scaffold-context.fsx            # regenerate the file in place
//   dotnet fsi scripts/generate-scaffold-context.fsx --check    # exit 1 on drift, write nothing (CI)

open System
open System.Diagnostics
open System.IO
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

let argv = fsi.CommandLineArgs |> Array.skip 1
let checkOnly = argv |> Array.contains "--check"

let fail (msg: string) =
    printfn "::error::%s" msg
    eprintfn "generate-scaffold-context: %s" msg
    exit 1

/// The package that PACKS the fragment, and the path it packs it to. Both are the contract with
/// FS.GG.Rendering (#570) — named once, here.
let packageId = "FS.GG.UI.Template"
let fragmentPath = "content/template/fragments/vec2/src/Product/Vec2.fs"
let target = "scripts/skill-block-context/_scaffold.fs"

// ---------------------------------------------------------------------------------------------
// 1. The pinned version — read from the repo's central pin, never invented
// ---------------------------------------------------------------------------------------------

/// Same rule (and same reason) as `pinnedVersion` in typecheck-md-blocks.fsx: the version is read
/// from Directory.Packages*.props and never restated, because a second copy of a version is a drift
/// bug with a delay fuse. Here it is worse than drift — generating from a different template than the
/// gate compiles against would produce a scaffold no reader's product can reproduce.
let pinnedVersion (package: string) =
    let props =
        [ "Directory.Packages.local.props"; "Directory.Packages.props" ]
        |> List.map repoPath
        |> List.filter File.Exists
    let hit =
        props
        |> List.tryPick (fun p ->
            let m =
                Regex.Match(
                    File.ReadAllText p,
                    $"""<PackageVersion\s+Include="{Regex.Escape package}"\s+Version="(?<v>[^"]+)"\s*/>"""
                )
            if m.Success then Some m.Groups["v"].Value else None)
    match hit with
    | Some v -> v
    | None ->
        fail
            $"no central PackageVersion pin found for '{package}' in Directory.Packages*.props. This \
              script generates {target} from that package and will NOT invent a version — add the pin."

let version = pinnedVersion packageId

// ---------------------------------------------------------------------------------------------
// 2. Restore the package into a scratch folder
// ---------------------------------------------------------------------------------------------

let run (fileName: string) (args: string) (workingDir: string) =
    let psi =
        ProcessStartInfo(
            fileName,
            args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDir
        )
    use p = Process.Start psi
    // Drain both pipes concurrently — reading one to completion before the other deadlocks the moment
    // the child fills the other's buffer. Same reasoning as typecheck-md-blocks.fsx.
    let stdout = p.StandardOutput.ReadToEndAsync()
    let stderr = p.StandardError.ReadToEndAsync()
    p.WaitForExit()
    p.ExitCode, stdout.Result + stderr.Result

let workDir = Path.Combine(Path.GetTempPath(), $"fsgg-scaffold-gen-{Guid.NewGuid():N}")
Directory.CreateDirectory workDir |> ignore

try
    // `PackageDownload` fetches the package WITHOUT putting it in a compile graph — we want the file
    // out of it, not a reference to it. It requires an exact-version range: `[x]`, not `x`.
    //
    // The empty Directory.Build.props/targets beside the project are what keep the REPO's props (and
    // its central package management) out of a project that must not inherit them: MSBuild stops its
    // upward search at the first one it finds. Setting a property inside the project would be far too
    // late — Sdk.props imports them before the first PropertyGroup is evaluated.
    //
    // NU1603/NU1605/NU1608 are promoted to ERRORS because the repo's Directory.Build.props (which
    // normally promotes them) is exactly what we just excluded. Without them NuGet would silently
    // SUBSTITUTE a nearby version when the feed cannot serve the pinned one, and this script would
    // generate the prelude from a template no reader restores — green, and lying.
    let projXml =
        $"""<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
    <RestorePackagesPath>pkgs</RestorePackagesPath>
    <WarningsAsErrors>$(WarningsAsErrors);NU1603;NU1605;NU1608</WarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <PackageDownload Include="{packageId}" Version="[{version}]" />
  </ItemGroup>

</Project>
"""
    File.WriteAllText(Path.Combine(workDir, "Directory.Build.props"), "<Project></Project>")
    File.WriteAllText(Path.Combine(workDir, "Directory.Build.targets"), "<Project></Project>")
    File.WriteAllText(Path.Combine(workDir, "fetch.fsproj"), projXml)

    printfn "restoring %s %s …" packageId version
    let code, output = run "dotnet" "restore fetch.fsproj" workDir
    if code <> 0 then
        fail
            $"restore of {packageId} {version} failed — cannot generate {target} without it. A \
              SUBSTITUTED version (NU1603/NU1605) is a failure here too, deliberately: it would \
              generate the scaffold from a template no reader restores.\n{output}"

    // NuGet lowercases the package folder.
    let fragment =
        Path.Combine(
            workDir,
            "pkgs",
            packageId.ToLowerInvariant(),
            version,
            fragmentPath.Replace('/', Path.DirectorySeparatorChar)
        )

    if not (File.Exists fragment) then
        fail
            $"{packageId} {version} restored, but it does NOT contain '{fragmentPath}'. That path is a \
              contract with FS.GG.Rendering (#570), guarded there by a merge-blocking test — if it has \
              stopped shipping, the contract is broken and this gate must go RED rather than silently \
              keep the copy already on disk. Raise it on FS.GG.Rendering before touching this script."

    // -----------------------------------------------------------------------------------------
    // 3. Sanity-check the fragment before we let it become the gate's context
    // -----------------------------------------------------------------------------------------

    // A restore can succeed and still hand us the wrong file — a moved fragment, a renamed type, a
    // reshaped record. Writing THAT into the prelude would hold the md-block gate green over a
    // context nobody vetted, which is the precise failure this whole item removes. So the file must
    // still look like the scaffold geometry, or we refuse to write it.
    let body = (File.ReadAllText fragment).Replace("\r\n", "\n")

    let required =
        [ "namespace AppRoot", "the fixed namespace the corpora `open`"
          "module Geometry", "the module the skills write as `Geometry.Vec2`"
          "type Vec2 =", "the scaffold's vector type"
          "Vx:", "the collision-safe label `Vx` (the whole point of Vec2)"
          "Vy:", "the collision-safe label `Vy` (the whole point of Vec2)"
          "toPoint", "the scene edge the corpora compile"
          "toRect", "the scene edge the corpora compile" ]

    for token, why in required do
        if not (body.Contains token) then
            fail
                $"the fragment restored from {packageId} {version} does not contain '{token}' — {why}. \
                  Refusing to overwrite {target} with a file that is not the scaffold geometry. If the \
                  scaffold has legitimately changed shape, this script and the corpora must be updated \
                  DELIBERATELY (and FS.GG.Rendering told), not silently."

    // -----------------------------------------------------------------------------------------
    // 4. Emit
    // -----------------------------------------------------------------------------------------

    let banner =
        [ "// ─────────────────────────────────────────────────────────────────────────────────────────"
          "// GENERATED FILE — DO NOT EDIT. Your changes will be overwritten, and CI will fail first."
          "//"
          $"// Source:    {packageId} {version} :: {fragmentPath}"
          "// Generator: dotnet fsi scripts/generate-scaffold-context.fsx"
          "// Pin:       Directory.Packages.local.props (generator-only group)"
          "//"
          "// This is the generated product's REAL collision-safe geometry, copied verbatim from the"
          "// published template package — not a re-declaration of it. It is the context the md-block"
          "// gate (scripts/typecheck-md-blocks.fsx) compiles every skill and TestSpec block against, so"
          "// it has to be the type the reader's product actually ships, byte for byte."
          "//"
          "// It used to be a hand-written twin, on the grounds that the real `Vec2` \"cannot be"
          "// referenced\" — true of a reference, false of the SOURCE, which FS.GG.Rendering packs under"
          "// `content/`. The twin was an unenforced cross-repo contract: it kept compiling after the real"
          "// type moved under it, holding the gate green over skills teaching a shape the scaffold no"
          "// longer shipped. FS.GG.Game#189 / FS.GG.Rendering#570 replaced it with this."
          "//"
          "// To change what the gate sees, bump the pin and regenerate. To change the GEOMETRY, change it"
          "// in FS.GG.Rendering — it is theirs, and every scaffolded product gets it from there."
          "// ─────────────────────────────────────────────────────────────────────────────────────────"
          "" ]
        |> String.concat "\n"

    let generated = banner + "\n" + (body.TrimEnd '\n') + "\n"

    let targetFile = repoPath target
    let current = if File.Exists targetFile then File.ReadAllText targetFile else ""

    if checkOnly then
        if current.Replace("\r\n", "\n") <> generated then
            fail
                $"{target} is STALE — it does not match {packageId} {version}. Regenerate it and commit \
                  the result:  dotnet fsi scripts/generate-scaffold-context.fsx"
        printfn "OK — %s is up to date with %s %s." target packageId version
    else
        File.WriteAllText(targetFile, generated)
        printfn "wrote %s (from %s %s)" target packageId version

finally
    try Directory.Delete(workDir, true) with _ -> ()

// Regenerates the public-surface baselines in readiness/surface-baselines/ from the built assemblies.
// Run after an intended public-API change, then commit the updated *.txt. The CI gate runs this and
// fails on any uncommitted drift (it is the Principle II "visibility lives in .fsi" guard).
//
// Adapted from FS.GG.Rendering scripts/refresh-surface-baselines.fsx (ADR-0022 / P2). Same emit
// format (one exported-type FullName per line, sorted, "Module" suffix stripped, compiler-generated
// excluded) so a package moved between the repos keeps a byte-comparable surface record.

open System
open System.IO
open System.Reflection
open System.Runtime.CompilerServices

let scriptDir = __SOURCE_DIRECTORY__
let repoRoot = Path.GetFullPath(Path.Combine(scriptDir, ".."))

// Every package → its src project folder (assembly name == package name). One row per committed baseline.
let packages = [ "FS.GG.Game.Core", "Game.Core" ]

let binDir proj = Path.Combine(repoRoot, "src", proj, "bin", "Debug", "net10.0")
let binDirs = packages |> List.map (snd >> binDir)

// Resolve cross-assembly dependencies from any package bin dir, so GetExportedTypes can fully load
// type signatures without #r wiring.
AppDomain.CurrentDomain.add_AssemblyResolve(
    ResolveEventHandler(fun _ args ->
        let name = AssemblyName(args.Name).Name
        binDirs
        |> List.tryPick (fun d ->
            let f = Path.Combine(d, name + ".dll")
            if File.Exists f then Some(Assembly.LoadFrom f) else None)
        |> Option.toObj))

let isCompilerGenerated (ty: Type) =
    ty.GetCustomAttributes(typeof<CompilerGeneratedAttribute>, false).Length > 0
    || ty.Name.StartsWith("<", StringComparison.Ordinal)

let names (assembly: Assembly) =
    assembly.GetExportedTypes()
    |> Array.filter (fun ty -> not (isCompilerGenerated ty))
    |> Array.map (fun ty ->
        match ty.FullName with
        | null -> ty.Name
        | fullName when ty.Name.EndsWith("Module", StringComparison.Ordinal) -> fullName.Replace("Module", "")
        | fullName -> fullName)
    |> Array.distinct
    |> Array.sort

let write packageName values =
    let path = Path.Combine(repoRoot, "readiness", "surface-baselines", packageName + ".txt")
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllLines(path, values)
    printfn "wrote %s (%d public types)" path (Array.length values)

for (packageName, proj) in packages do
    let dll = Path.Combine(binDir proj, packageName + ".dll")
    if not (File.Exists dll) then
        failwithf "missing %s — build the solution (Debug) before refreshing baselines" dll
    write packageName (names (Assembly.LoadFrom dll))

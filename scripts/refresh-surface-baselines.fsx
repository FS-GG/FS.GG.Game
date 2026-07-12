// Regenerates the public-surface baselines in readiness/surface-baselines/ from the built assemblies.
// Run after an intended public-API change, then commit the updated *.txt. The CI gate runs this and
// fails on any uncommitted drift (it is the Principle II "visibility lives in .fsi" guard).
//
// TWO baselines are written per package, because type names alone cannot see a member appearing or
// disappearing on a type that already exists (issue #236):
//   readiness/surface-baselines/<pkg>.txt          — one line per exported TYPE
//   readiness/surface-baselines/members/<pkg>.txt  — one line per exported MEMBER, with its signature
// Without the member file the gate is types-only: deleting a public function — a SemVer major break —
// leaves `Pathfinding`'s three type lines untouched and the gate reports GREEN. The type-level file
// keeps its exact format, so anything already reading it by path is unaffected; the member file is
// purely additive.
//
// Adapted from FS.GG.Rendering scripts/refresh-surface-baselines.fsx (ADR-0022 / P2), now including
// the member half that was dropped when this script was first ported. Same emit shape for BOTH files
// (sorted, "Module" suffix stripped, compiler-generated excluded) so a package moved between the repos
// keeps a comparable surface record.
//
// It deviates from Rendering's generator in exactly three places, each a deliberate bug fix, NOT drift
// to be "corrected" back into parity (all three are filed against Rendering — FS.GG.Rendering#697):
//   1. `typeRef` strips the arity suffix from every generic segment instead of truncating at the first
//      backtick, which silently erased a nested generic's own name (`Dictionary`2+Enumerator`).
//   2. `displayName` trims the TRAILING "Module" rather than replacing the substring everywhere.
//   3. `writeLines` emits "\n" explicitly rather than Environment.NewLine.
// Each one made the baseline record something other than the surface — which is the whole failure this
// file exists to prevent, so parity with a buggy emitter is worth less than a baseline that is true.

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Runtime.CompilerServices
open System.Text.Json

let scriptDir = __SOURCE_DIRECTORY__
let repoRoot = Path.GetFullPath(Path.Combine(scriptDir, ".."))

// Every package → its src project folder (assembly name == package name). One row per committed baseline.
let packages =
    [ "FS.GG.Game.Core", "Game.Core"
      "FS.GG.Game.Render", "Game.Render" ]

let binDir proj = Path.Combine(repoRoot, "src", proj, "bin", "Debug", "net10.0")
let binDirs = packages |> List.map (snd >> binDir)

// Third-party dependencies are NOT copied into a library project's bin/ (only executables and test
// projects get the full closure), yet a public signature may name one — `Adapter` returns
// FS.GG.UI.Scene types. Reflecting over a member forces the runtime to resolve them, so read the
// restore graph: `obj/project.assets.json` pins the exact version of every package the project
// resolved, so this probe needs nothing built but the packages themselves, and cannot drift from
// what the build used.
let private restoredAssemblies () =
    let probe = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

    let tryProperty (element: JsonElement) (name: string) =
        match element.TryGetProperty name with
        | true, value -> Some value
        | _ -> None

    // Present, and actually a string. Anything else means this assets file cannot say where the
    // package lives, so skip it rather than compose a path out of null.
    let stringProperty element name =
        tryProperty element name
        |> Option.filter (fun value -> value.ValueKind = JsonValueKind.String)
        |> Option.bind (fun value -> Option.ofObj (value.GetString()))

    let harvestFile (packagesPath: string) (relativeRoot: string) (fileName: string) =
        // `_._` is NuGet's "framework supported, but empty" placeholder.
        if not (fileName.EndsWith("_._", StringComparison.Ordinal)) then
            let full =
                Path.Combine(packagesPath, relativeRoot, fileName.Replace('/', Path.DirectorySeparatorChar))

            let simpleName = Path.GetFileNameWithoutExtension fileName

            // `runtime` is probed before `compile`, so a real implementation assembly always beats a
            // reference one. Project-to-project references name a .fsproj rather than a package dir,
            // so `full` does not exist for them and they fall through to the bin-dir probe above.
            if not (probe.ContainsKey simpleName) && File.Exists full then
                probe[simpleName] <- full

    let harvest (assetsPath: string) =
        use doc = JsonDocument.Parse(File.ReadAllText assetsPath)
        let root = doc.RootElement

        let packagesPath =
            tryProperty root "project"
            |> Option.bind (fun project -> tryProperty project "restore")
            |> Option.bind (fun restore -> stringProperty restore "packagesPath")

        match packagesPath, tryProperty root "libraries", tryProperty root "targets" with
        | Some packagesPath, Some libraries, Some targets ->
            for target in targets.EnumerateObject() do
                for entry in target.Value.EnumerateObject() do
                    let relativeRoot =
                        match libraries.TryGetProperty entry.Name with
                        | true, library -> stringProperty library "path"
                        | _ -> None

                    match relativeRoot with
                    | Some relativeRoot ->
                        for section in [ "runtime"; "compile" ] do
                            match entry.Value.TryGetProperty section with
                            | true, files ->
                                for file in files.EnumerateObject() do
                                    harvestFile packagesPath relativeRoot file.Name
                            | _ -> ()
                    | None -> ()
        | _ -> ()

    for (_, proj) in packages do
        let assets = Path.Combine(repoRoot, "src", proj, "obj", "project.assets.json")
        if File.Exists assets then harvest assets

    probe

let private restored = restoredAssemblies ()

// Resolve cross-assembly dependencies from a package bin dir first (so FS.GG.Game.* bind to the copies
// just built), then from the restore graph, so reflection can walk a full public signature.
AppDomain.CurrentDomain.add_AssemblyResolve(
    ResolveEventHandler(fun _ args ->
        let name = AssemblyName(args.Name).Name

        binDirs
        |> List.tryPick (fun d ->
            let f = Path.Combine(d, name + ".dll")
            if File.Exists f then Some f else None)
        |> Option.orElseWith (fun () ->
            match restored.TryGetValue name with
            | true, path -> Some path
            | _ -> None)
        |> Option.map Assembly.LoadFrom
        |> Option.toObj))

// Compiler-generated / anonymous members are EXCLUDED: their names embed a non-deterministic hash
// (e.g. `<>f__AnonymousType…`) and would make the baseline unstable across builds. The same exclusion
// carries the member baseline: F# emits the structural `Equals`/`GetHashCode`/`CompareTo`/`ToString`
// overrides as [<CompilerGenerated>], so filtering them leaves the surface a reader actually authored.
let isCompilerGenerated (m: MemberInfo) =
    m.GetCustomAttributes(typeof<CompilerGeneratedAttribute>, false).Length > 0
    || m.Name.StartsWith("<", StringComparison.Ordinal)

let fullNameOf (ty: Type) =
    match ty.FullName with
    | null -> ty.Name
    | value -> value

// Strip only the TRAILING "Module" that F# appends when a module's name collides with a type's.
// `fullName.Replace("Module", "")` — which is what Rendering still does — eats the word EVERYWHERE in
// the name, so a module named `ModuleRegistry` (FullName `…ModuleRegistryModule`) would be recorded
// as `…Registry`: a baseline entry naming a type that does not exist. `fullName` ends with `ty.Name`,
// so trimming the suffix by length is exact.
let displayName (ty: Type) =
    let fullName = fullNameOf ty

    if ty.Name.EndsWith("Module", StringComparison.Ordinal) then
        fullName.Substring(0, fullName.Length - "Module".Length)
    else
        fullName

let exportedTypes (assembly: Assembly) =
    assembly.GetExportedTypes() |> Array.filter (fun ty -> not (isCompilerGenerated ty))

let names (assembly: Assembly) =
    exportedTypes assembly |> Array.map displayName |> Array.distinct |> Array.sort

// Render a type reference the same way on every machine and every build: no assembly-qualified
// generic arguments (which `Type.FullName` embeds), no `\`1` arity suffix, generic parameters by
// their own name. A signature line is only useful as a baseline if it is byte-identical across runs.
let rec typeRef (ty: Type) : string =
    if ty.IsGenericParameter then
        ty.Name
    elif ty.IsArray || ty.IsByRef || ty.IsPointer then
        let suffix =
            if ty.IsArray then "[]"
            elif ty.IsByRef then "&"
            else "*"

        match ty.GetElementType() with
        | null -> ty.Name
        | element -> typeRef element + suffix
    elif ty.IsGenericType then
        // A constructed generic's FullName is `Ns.Outer\`2+Inner\`1[[System.Int32, System.Private…]]`:
        // arity suffix on EVERY generic segment, and assembly-qualified arguments in brackets. Cut the
        // bracketed arguments (we render our own from GetGenericArguments), then drop each `N.
        //
        // Truncating at the FIRST backtick instead — which is what Rendering still does — throws away
        // everything after it, so `Dictionary\`2+Enumerator` emits as plain `Dictionary<K, V>`. The
        // nested type's name vanishes, a member returning `Dictionary<K,V>.Enumerator` records the same
        // line as one returning `Dictionary<K,V>`, and `Array.distinct` then merges them — a real
        // signature change that this baseline exists to catch would leave the file untouched.
        let stem =
            let raw = fullNameOf ty

            let withoutArguments =
                match raw.IndexOf('[') with
                | -1 -> raw
                | bracket -> raw.Substring(0, bracket)

            let builder = Text.StringBuilder(withoutArguments.Length)
            let mutable inArity = false

            for c in withoutArguments do
                if c = '`' then inArity <- true
                elif inArity && Char.IsDigit c then ()
                else
                    inArity <- false
                    builder.Append(c) |> ignore

            builder.ToString()

        let args = ty.GetGenericArguments() |> Array.map typeRef |> String.concat ", "
        $"{stem}<{args}>"
    else
        fullNameOf ty

let private parameters (ps: ParameterInfo array) =
    ps |> Array.map (fun p -> typeRef p.ParameterType) |> String.concat ", "

// Property/event accessors are also emitted as `get_X`/`add_X` methods. The Property and Event lines
// already carry them, so dropping the accessor methods keeps one member = one line.
let private isAccessor (m: MethodInfo) =
    m.IsSpecialName
    && [ "get_"; "set_"; "add_"; "remove_" ]
       |> List.exists (fun prefix -> m.Name.StartsWith(prefix, StringComparison.Ordinal))

let private memberFlags =
    BindingFlags.Public ||| BindingFlags.Instance ||| BindingFlags.Static ||| BindingFlags.DeclaredOnly

// Deliberately NOT `GetMembers`: that also populates nested types, which forces the runtime to
// resolve every assembly a nested type mentions. Asking for each member kind resolves only what a
// public signature actually names — and nested types are exported types in their own right anyway,
// so they already get their own lines.
let private membersOf (ty: Type) : MemberInfo array =
    [| yield! ty.GetConstructors(memberFlags) |> Array.map (fun m -> m :> MemberInfo)
       yield! ty.GetMethods(memberFlags) |> Array.map (fun m -> m :> MemberInfo)
       yield! ty.GetProperties(memberFlags) |> Array.map (fun m -> m :> MemberInfo)
       yield! ty.GetFields(memberFlags) |> Array.map (fun m -> m :> MemberInfo)
       yield! ty.GetEvents(memberFlags) |> Array.map (fun m -> m :> MemberInfo) |]

let private signature (owner: string) (m: MemberInfo) =
    match m with
    | :? ConstructorInfo as ctor -> Some $"{owner}.new({parameters (ctor.GetParameters())})"
    | :? MethodInfo as method' when isAccessor method' -> None
    | :? MethodInfo as method' ->
        let generics =
            if method'.IsGenericMethodDefinition then
                let args = method'.GetGenericArguments() |> Array.map _.Name |> String.concat ", "
                $"<{args}>"
            else
                ""

        Some $"{owner}.{method'.Name}{generics}({parameters (method'.GetParameters())}) : {typeRef method'.ReturnType}"
    | :? PropertyInfo as property ->
        let accessors =
            [ if property.CanRead then "get"
              if property.CanWrite then "set" ]
            |> String.concat ", "

        Some $"{owner}.{property.Name} : {typeRef property.PropertyType} [{accessors}]"
    | :? FieldInfo as field -> Some $"{owner}.{field.Name} : {typeRef field.FieldType}"
    | :? EventInfo as event' ->
        match event'.EventHandlerType with
        | null -> Some $"{owner}.{event'.Name} : event"
        | handler -> Some $"{owner}.{event'.Name} : event {typeRef handler}"
    | _ -> None

let memberSignatures (assembly: Assembly) =
    exportedTypes assembly
    |> Array.collect (fun ty ->
        let owner = displayName ty

        membersOf ty
        |> Array.filter (fun m -> not (isCompilerGenerated m))
        |> Array.choose (signature owner))
    |> Array.distinct
    |> Array.sort

// Explicit "\n", NOT File.WriteAllLines: that separates with Environment.NewLine, so regenerating on
// Windows rewrites every line with CRLF. The repo has no .gitattributes normalising it, so the whole
// baseline would show as drift and the gate would go red on a machine, not on an API change. A
// baseline is only a baseline if the bytes depend on the surface alone.
let private writeLines path (values: string array) noun =
    Directory.CreateDirectory(Path.GetDirectoryName(path: string)) |> ignore
    let text = if Array.isEmpty values then "" else String.concat "\n" values + "\n"
    File.WriteAllText(path, text)
    printfn "wrote %s (%d public %s)" path (Array.length values) noun

let write packageName (assembly: Assembly) =
    let root = Path.Combine(repoRoot, "readiness", "surface-baselines")
    writeLines (Path.Combine(root, packageName + ".txt")) (names assembly) "types"
    writeLines (Path.Combine(root, "members", packageName + ".txt")) (memberSignatures assembly) "members"

for (packageName, proj) in packages do
    let dll = Path.Combine(binDir proj, packageName + ".dll")
    if not (File.Exists dll) then
        failwithf "missing %s — build the solution (Debug) before refreshing baselines" dll
    write packageName (Assembly.LoadFrom dll)

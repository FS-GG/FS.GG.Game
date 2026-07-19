module Game.Harness.Tests.DependencyTests

open System.IO
open System.Reflection.PortableExecutable
open System.Reflection.Metadata
open Expecto
open FS.GG.Game.Harness

// The names a BCL-only leaf on Game.Core is allowed to reference.
let private isAllowed (name: string) : bool =
    name = "FSharp.Core"
    || name = "FS.GG.Game.Core"
    || name = "netstandard"
    || name = "mscorlib"
    || name.StartsWith("System")

// Every type the harness assembly references, as (namespace, name), read from its IL metadata.
// GetReferencedAssemblies() cannot see this — System.IO, System.Net and System.DateTime all resolve
// to System.Runtime / System.Private.CoreLib — so the I/O + wall-clock ban (FR-007) is only checkable
// at the type-reference level.
let private referencedTypes () : (string * string) list =
    let loc = typeof<Origin>.Assembly.Location
    use fs = File.OpenRead(loc)
    use pe = new PEReader(fs)
    let md = pe.GetMetadataReader()

    [ for handle in md.TypeReferences do
          let tr = md.GetTypeReference(handle)
          md.GetString(tr.Namespace), md.GetString(tr.Name) ]

// A referenced type that would let determinism leak: I/O, networking, a wall clock, ambient RNG, or a
// process/environment probe.
let private isForbidden (ns: string, name: string) : bool =
    ns.StartsWith("System.IO")
    || ns.StartsWith("System.Net")
    || (ns = "System.Diagnostics" && name = "Stopwatch")
    || (ns = "System"
        && (name = "DateTime"
            || name = "DateTimeOffset"
            || name = "Random"
            || name = "Environment"
            || name = "TimeProvider"))

[<Tests>]
let tests =
    testList
        "Dependency"
        [ testCase "FR-007 the harness references only FS.GG.Game.Core and the BCL"
          <| fun _ ->
              let assembly = typeof<Origin>.Assembly
              let referenced = assembly.GetReferencedAssemblies() |> Array.choose (fun a -> Option.ofObj a.Name)

              let disallowed = referenced |> Array.filter (isAllowed >> not)

              Expect.isEmpty
                  disallowed
                  (sprintf "the harness must reference only Game.Core + BCL; found: %A" disallowed)

          testCase "FR-007 the harness does reference FS.GG.Game.Core"
          <| fun _ ->
              // Assert the subject is real: the leaf-dependency claim is only meaningful if it is in
              // fact built on Game.Core.
              let assembly = typeof<Origin>.Assembly
              let referenced = assembly.GetReferencedAssemblies() |> Array.choose (fun a -> Option.ofObj a.Name)
              Expect.contains referenced "FS.GG.Game.Core" "the harness is built on Game.Core"

          testCase "FR-007 no render/input/graphics assembly is referenced"
          <| fun _ ->
              let assembly = typeof<Origin>.Assembly
              let referenced = assembly.GetReferencedAssemblies() |> Array.choose (fun a -> Option.ofObj a.Name)

              let forbidden =
                  referenced
                  |> Array.filter (fun n ->
                      n.StartsWith("FS.GG.UI")
                      || n.StartsWith("FS.GG.Game.Render")
                      || n.StartsWith("SkiaSharp"))

              Expect.isEmpty forbidden (sprintf "no render/input stack allowed; found: %A" forbidden)

          testCase "FR-007 the harness references no I/O, networking, or wall-clock type"
          <| fun _ ->
              // The teeth of "performs no I/O and no wall-clock read": scan the IL type references,
              // since all of these fold into System.Runtime and are invisible to GetReferencedAssemblies.
              let forbidden = referencedTypes () |> List.filter isForbidden

              Expect.isEmpty
                  forbidden
                  (sprintf "the harness must reference no I/O / networking / clock / ambient-RNG type; found: %A" forbidden) ]

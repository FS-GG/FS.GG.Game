module Game.Harness.Tests.DependencyTests

open Expecto
open FS.GG.Game.Harness

// The names a BCL-only leaf on Game.Core is allowed to reference.
let private isAllowed (name: string) : bool =
    name = "FSharp.Core"
    || name = "FS.GG.Game.Core"
    || name = "netstandard"
    || name = "mscorlib"
    || name.StartsWith("System")

[<Tests>]
let tests =
    testList
        "Dependency"
        [ testCase "FR-007 the harness references only FS.GG.Game.Core and the BCL"
          <| fun _ ->
              // Origin is a type defined in the harness assembly.
              let assembly = typeof<Origin>.Assembly
              let referenced = assembly.GetReferencedAssemblies() |> Array.map (fun a -> a.Name)

              let disallowed =
                  referenced |> Array.filter (fun n -> not (isAllowed (n |> Option.ofObj |> Option.defaultValue "")))

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

              Expect.isEmpty forbidden (sprintf "no render/input stack allowed; found: %A" forbidden) ]

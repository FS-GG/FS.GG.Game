module Game.Render.Tests.Program

open Expecto
open FsCheck
open FsCheck.FSharp

// Explicit opt-in for the parallel console-lock regression control. Normal runs add no tests.
// Run the full suite with FSGG_RENDER_NEGATIVE_CONTROL=1 under a finite outer timeout;
// it must finish nonzero and report eight errored properties with FsCheck replay details.
[<Tests>]
let negativeControl =
    testList "parallel property failure control" [
        if System.Environment.GetEnvironmentVariable "FSGG_RENDER_NEGATIVE_CONTROL" = "1" then
            for n in 1 .. 8 do
                testCase (sprintf "deliberate counterexample %d" n) (fun () ->
                    Check.One(TestSupport.propertyConfig 1000, Prop.forAll (Arb.fromGen (Gen.choose (1, 100))) (fun (_: int) -> false)))
    ]

[<EntryPoint>]
let main argv =
    Tests.runTestsInAssemblyWithCLIArgs [] argv

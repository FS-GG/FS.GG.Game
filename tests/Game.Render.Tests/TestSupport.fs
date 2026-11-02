module Game.Render.Tests.TestSupport

open FsCheck

// FsCheck's default runner writes to Console.Out even for passing cases. Expecto's parallel
// logger takes the console and its own writer locks in the opposite order, so a failure can
// deadlock the suite. Keep FsCheck's counterexample and replay report without touching stdout.
// This is the compatible runner used by FS-GG/FS.GG.Algorithms#36.
let private quietThrowingRunner =
    { new IRunner with
        member _.OnStartFixture _ = ()
        member _.OnArguments(_, _, _) = ()
        member _.OnShrink(_, _) = ()

        member _.OnFinished(name, result) =
            match result with
            | TestResult.Passed _ -> ()
            | _ -> failwith (Runner.onFinishedToString name result) }

let propertyConfig count =
    Config.QuickThrowOnFailure.WithMaxTest(count).WithRunner quietThrowingRunner

module Playtest.Cli.Tests.Program

open Expecto

[<EntryPoint>]
let main argv = Tests.runTestsInAssemblyWithCLIArgs [] argv

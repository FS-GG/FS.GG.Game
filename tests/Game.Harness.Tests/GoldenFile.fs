/// Golden record/update helper for trace dumps (Phase 1, FR-009). It reads and writes golden files,
/// so it lives in the **test project**, never the package: the harness performs no I/O (WI-007
/// FR-007, enforced by `DependencyTests`). It is built on the pure `Trace.render`, so a golden is
/// captured from a green run and rewritten on an intended change, never hand-transcribed.
module Game.Harness.Tests.GoldenFile

open System
open System.IO

/// Whether update mode was requested via the environment. The suite reads this once and passes the
/// flag explicitly to `check`, keeping the comparison itself env-free and testable.
let updateRequested () : bool =
    match Environment.GetEnvironmentVariable "PLAYTEST_UPDATE_GOLDENS" with
    | null
    | "" -> false
    | _ -> true

/// The first line index at which two rendered dumps differ — the line-level analogue of
/// `Trace.firstDivergence`, so a golden diff points at the exact step. `None` when equal.
let firstLineDivergence (a: string) (b: string) : int option =
    let al = a.Split('\n')
    let bl = b.Split('\n')
    let n = min al.Length bl.Length

    let rec go i =
        if i >= n then
            if al.Length = bl.Length then None else Some i
        elif al.[i] <> bl.[i] then
            Some i
        else
            go (i + 1)

    go 0

/// Compare a rendered trace to the golden at `path`. When `update` is set (or the golden is absent),
/// write it from the current run and return `Ok`. Otherwise compare and return `Error (line, expected,
/// actual)` on the first drift, or `Ok ()` on a match. All file I/O is confined to this test project.
let check (update: bool) (path: string) (rendered: string) : Result<unit, int * string * string> =
    if update || not (File.Exists path) then
        match Path.GetDirectoryName path with
        | null
        | "" -> ()
        | dir -> Directory.CreateDirectory dir |> ignore

        File.WriteAllText(path, rendered)
        Ok()
    else
        let golden = File.ReadAllText path

        match firstLineDivergence golden rendered with
        | None -> Ok()
        | Some i ->
            let lineAt (s: string) =
                let ls = s.Split('\n')
                if i < ls.Length then ls.[i] else "<eof>"

            Error(i, lineAt golden, lineAt rendered)

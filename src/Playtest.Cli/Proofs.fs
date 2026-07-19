/// The proof report: which `GP-###` has an `InputDriven`, `Synthetic`, or `Missing` proof, read from a
/// deterministic file (`GP-### <provenance>` per line) rather than by reflecting over a test assembly.
module FS.GG.Playtest.Proofs

open System

/// The provenance of a gameplay-FR's proof. Only `InputDriven` satisfies (the SDD/Governance synthetic
/// rule); `Synthetic` and `Missing` never cover an AC.
type Provenance =
    | InputDriven
    | Synthetic
    | Missing

/// Parse a provenance token (case-insensitive), or `None` when unrecognized.
let parseProvenance (s: string) : Provenance option =
    match s.Trim().ToLowerInvariant() with
    | "inputdriven" -> Some InputDriven
    | "synthetic" -> Some Synthetic
    | "missing" -> Some Missing
    | _ -> None

/// Parse a proof report (`GP-### <provenance>` per line; `#`/blank lines ignored). Returns `Error` with
/// a message on a malformed line or an unknown provenance token — fail closed, never a silent skip.
let parse (text: string) : Result<Map<string, Provenance>, string> =
    let mutable error: string option = None

    let entries =
        text.Replace("\r\n", "\n").Split('\n')
        |> Array.toList
        |> List.choose (fun raw ->
            let line = raw.Trim()

            if line = "" || line.StartsWith("#") then
                None
            else
                match line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries) with
                | [| gp; prov |] ->
                    match parseProvenance prov with
                    | Some p -> Some(gp, p)
                    | None ->
                        error <- Some(sprintf "unknown provenance '%s' for %s" prov gp)
                        None
                | _ ->
                    error <- Some(sprintf "malformed proof line: %s" line)
                    None)

    match error with
    | Some e -> Error e
    | None -> Ok(Map.ofList entries)

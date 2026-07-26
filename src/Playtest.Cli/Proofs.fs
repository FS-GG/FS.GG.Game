/// Simulation/synthetic proof declarations plus validated production-journey receipts.
module FS.GG.Playtest.Proofs

open System
open System.Reflection
open FS.GG.Game.Harness

/// The provenance of a gameplay-FR's proof. The manifest decides whether simulation input is enough
/// or a validated production journey is required.
type Provenance =
    | InputDriven
    | ProductionJourney
    | Synthetic
    | Missing

/// Parse a provenance token (case-insensitive), or `None` when unrecognized.
let parseProvenance (s: string) : Provenance option =
    match s.Trim().ToLowerInvariant() with
    | "inputdriven" -> Some InputDriven
    // ProductionJourney is intentionally absent: only a validated runner receipt can provide it.
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

/// Load and execute every public `IProductionJourneyProof` in an assembly. Production provenance is
/// obtained only from the opaque in-memory receipt returned by `Journey.runScript`/`runPolicy`; no
/// JSON, key, checksum, or caller-authored provenance text is accepted.
let loadJourneyProofs (assemblyPath: string) : Result<Map<string, Provenance>, string> =
    try
        let proofType = typeof<IProductionJourneyProof>
        let assembly = Assembly.LoadFrom assemblyPath
        let implementations =
            assembly.GetExportedTypes()
            |> Array.filter (fun candidate ->
                not candidate.IsAbstract
                && not candidate.IsInterface
                && proofType.IsAssignableFrom candidate
                && candidate.GetConstructor(Type.EmptyTypes) <> null)

        if implementations.Length = 0 then
            Error(sprintf "no public IProductionJourneyProof implementation found in %s" assemblyPath)
        else
            implementations
            |> Array.fold
                (fun state implementation ->
                    match state with
                    | Error error -> Error error
                    | Ok proofs ->
                        match Activator.CreateInstance implementation with
                        | null -> Error(sprintf "cannot instantiate production journey proof %s" implementation.FullName)
                        | instance ->
                            let proof = instance :?> IProductionJourneyProof
                            let receipt = proof.Run()
                            let testId = JourneyReceipt.testId receipt

                            if String.IsNullOrWhiteSpace proof.TestId || proof.TestId <> testId then
                                Error(sprintf "%s returned a receipt for mismatched test identity '%s'" implementation.FullName testId)
                            elif JourneyReceipt.result receipt <> JourneyResult.Passed then
                                Error(sprintf "production journey proof %s did not pass" testId)
                            elif JourneyReceipt.steps receipt > JourneyReceipt.maxSteps receipt then
                                Error(sprintf "production journey proof %s violates its declared step bound" testId)
                            elif Map.containsKey testId proofs then
                                Error(sprintf "duplicate production journey proof for %s" testId)
                            else
                                Ok(Map.add testId ProductionJourney proofs))
                (Ok Map.empty)
    with ex ->
        Error(sprintf "cannot execute production journey proof assembly %s: %s" assemblyPath ex.Message)

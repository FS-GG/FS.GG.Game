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

/// An opaque receipt which has crossed every in-process production-journey validation gate. The
/// receipt remains non-constructible; this wrapper only carries it to the evidence serializer.
type ValidatedJourneyProof =
    { Provenance: Provenance
      Receipt: JourneyReceipt
      /// Minted by the CLI in the same in-memory execution which returned `Receipt`.
      ExecutionId: string }

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
let private assemblyAuthority (assembly: Assembly) =
    let name = assembly.GetName().Name |> Option.ofObj |> Option.defaultValue "<unnamed>"
    name + "/" + assembly.ManifestModule.ModuleVersionId.ToString("N")

/// Load proofs while requiring every receipt to identify the exact producer assembly selected by
/// the consumer. This allowlist prevents an external assembly from composing its own adapter and
/// presenting internally consistent identities as a producer-owned journey.
let loadJourneyReceiptsWithAuthority
    (assemblyPath: string)
    (authorityAssemblyPath: string)
    : Result<Map<string, ValidatedJourneyProof>, string> =
    try
        let proofType = typeof<IProductionJourneyProof>
        let assembly = Assembly.LoadFrom assemblyPath
        let expectedAuthority = Assembly.LoadFrom authorityAssemblyPath |> assemblyAuthority
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

                            match proof with
                            | :? IProductionJourneyProofV1 as proofV1
                                when String.IsNullOrWhiteSpace proofV1.CompositionAuthority
                                     || String.IsNullOrWhiteSpace proofV1.RouteId
                                     || String.IsNullOrWhiteSpace proofV1.ScenarioId
                                     || String.IsNullOrWhiteSpace proofV1.InputIdentity
                                     || String.IsNullOrWhiteSpace proofV1.TerminalPredicateIdentity ->
                                Error(sprintf "%s declares an empty schema-v1 journey identity" implementation.FullName)
                            | :? IProductionJourneyProofV1 as proofV1
                                when proofV1.CompositionAuthority <> JourneyReceipt.compositionAuthority receipt
                                     || proofV1.RouteId <> JourneyReceipt.routeId receipt
                                     || proofV1.ScenarioId <> JourneyReceipt.scenarioId receipt
                                     || proofV1.InputIdentity <> JourneyReceipt.inputIdentity receipt
                                     || proofV1.TerminalPredicateIdentity
                                        <> JourneyReceipt.terminalPredicateIdentity receipt ->
                                Error(sprintf "%s returned a receipt with mismatched schema-v1 journey identities" implementation.FullName)
                            | :? IProductionJourneyProofV1 as proofV1
                                when proofV1.CompositionAuthority <> expectedAuthority ->
                                Error(
                                    sprintf
                                        "%s uses composition authority '%s', which is not the allowlisted producer '%s'"
                                        implementation.FullName
                                        proofV1.CompositionAuthority
                                        expectedAuthority
                                )
                            | :? IProductionJourneyProofV1 ->
                              if String.IsNullOrWhiteSpace proof.TestId || proof.TestId <> testId then
                                Error(sprintf "%s returned a receipt for mismatched test identity '%s'" implementation.FullName testId)
                              elif JourneyReceipt.schemaVersion receipt <> 1 then
                                Error(
                                    sprintf
                                        "production journey proof %s returned unsupported receipt schema version %d"
                                        testId
                                        (JourneyReceipt.schemaVersion receipt)
                                )
                              elif JourneyReceipt.origin receipt <> Origin.ProductionJourney then
                                Error(sprintf "production journey proof %s has a non-production origin" testId)
                              elif String.IsNullOrWhiteSpace(JourneyReceipt.runnerIdentity receipt)
                                   || String.IsNullOrWhiteSpace(JourneyReceipt.runnerVersion receipt) then
                                Error(sprintf "production journey proof %s has no runner identity/version" testId)
                              elif JourneyReceipt.result receipt <> JourneyResult.Passed then
                                Error(sprintf "production journey proof %s did not pass" testId)
                              elif not (JourneyReceipt.terminalPredicateReached receipt) then
                                Error(sprintf "production journey proof %s did not reach its terminal predicate" testId)
                              elif JourneyReceipt.steps receipt <= 0 then
                                Error(sprintf "production journey proof %s executed no production event" testId)
                              elif JourneyReceipt.steps receipt > JourneyReceipt.maxSteps receipt then
                                Error(sprintf "production journey proof %s violates its declared step bound" testId)
                              elif
                                [ JourneyReceipt.inputDigest receipt
                                  JourneyReceipt.scriptDigest receipt
                                  JourneyReceipt.traceDigest receipt
                                  JourneyReceipt.initialFingerprintDigest receipt
                                  JourneyReceipt.terminalFingerprintDigest receipt ]
                                |> List.exists String.IsNullOrWhiteSpace
                              then
                                Error(sprintf "production journey proof %s has an incomplete digest binding" testId)
                              elif Map.containsKey testId proofs then
                                Error(sprintf "duplicate production journey proof for %s" testId)
                              else
                                Ok(
                                    Map.add
                                        testId
                                        { Provenance = ProductionJourney
                                          Receipt = receipt
                                          ExecutionId = Guid.NewGuid().ToString("N") }
                                        proofs
                                )
                            | _ ->
                                Error(
                                    sprintf
                                        "%s implements legacy IProductionJourneyProof but not the identity-bound IProductionJourneyProofV1"
                                        implementation.FullName
                                ))
                (Ok Map.empty)
    with ex ->
        Error(sprintf "cannot execute production journey proof assembly %s: %s" assemblyPath ex.Message)

let loadJourneyReceipts (assemblyPath: string) : Result<Map<string, ValidatedJourneyProof>, string> =
    Error(
        sprintf
            "cannot load production journey proofs from %s without an explicit producer authority; "
            assemblyPath
        + "use loadJourneyReceiptsWithAuthority"
    )

let loadJourneyProofsWithAuthority
    (assemblyPath: string)
    (authorityAssemblyPath: string)
    : Result<Map<string, Provenance>, string> =
    loadJourneyReceiptsWithAuthority assemblyPath authorityAssemblyPath
    |> Result.map (Map.map (fun _ proof -> proof.Provenance))

/// Compatibility projection used by coverage-only callers which do not need the serialized receipt.
let loadJourneyProofs (assemblyPath: string) : Result<Map<string, Provenance>, string> =
    Error(
        sprintf
            "cannot load production journey proofs from %s without an explicit producer authority; "
            assemblyPath
        + "use loadJourneyProofsWithAuthority"
    )

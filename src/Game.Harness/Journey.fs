namespace FS.GG.Game.Harness

open System
open System.Globalization
open System.Security.Cryptography
open System.Text
open FS.GG.Game.Core

[<RequireQualifiedAccess>]
type JourneyEvent<'key, 'pointer, 'menu, 'effectResult> =
    | Start
    | MenuAction of 'menu
    | KeyInput of key: 'key * pressed: bool
    | PointerInput of 'pointer
    | Interact
    | Pause
    | Resume
    | FixedTick
    | EffectResult of 'effectResult

[<RequireQualifiedAccess>]
type JourneyDispatch<'message> =
    | Mapped of 'message list
    | Unbound of action: string

[<RequireQualifiedAccess>]
type ActionCoverageGap =
    | UnexercisedUnbound of action: string * event: string
    | DegenerateVocabulary of slot: string * inhabitants: int

type ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint> =
    { RouteId: string
      ScenarioId: string
      TestId: string
      MaxSteps: int
      Boot: unit -> 'model
      MapEvent:
        JourneyEvent<'key, 'pointer, 'menu, 'effectResult> ->
        'model ->
            JourneyDispatch<'message>
      Update: 'message -> 'model -> 'model
      FixedTick: 'model -> 'model
      ApplyEffectResult: 'effectResult -> 'model -> 'model
      IsTerminal: 'model -> bool
      Fingerprint: 'model -> 'fingerprint
      EncodeEvent: JourneyEvent<'key, 'pointer, 'menu, 'effectResult> -> string
      EncodeFingerprint: 'fingerprint -> string }

[<RequireQualifiedAccess>]
type JourneyResult =
    | Passed
    | Failed of reason: string

[<RequireQualifiedAccess>]
type JourneyInputKind =
    | FixedScript
    | SeededPolicy

type internal ReceiptData =
    { SchemaVersion: int
      RunnerIdentity: string
      RunnerVersion: string
      CompositionAuthority: string
      Origin: Origin
      RouteId: string
      ScenarioId: string
      TestId: string
      InputKind: JourneyInputKind
      InputIdentity: string
      InputDigest: string
      ScriptDigest: string
      TraceDigest: string
      InitialFingerprintDigest: string
      TerminalFingerprintDigest: string
      TerminalPredicateIdentity: string
      TerminalPredicateReached: bool
      Result: JourneyResult
      Steps: int
      MaxSteps: int }

[<Sealed>]
type JourneyReceipt internal (data: ReceiptData) =
    member internal _.Data = data

module private Stable =
    let digestBytes (value: byte[]) =
        value
        |> SHA256.HashData
        |> Convert.ToHexString
        |> fun s -> s.ToLowerInvariant()

    let frame (values: string list) =
        let builder = StringBuilder()

        for value in values do
            builder.Append(Encoding.UTF8.GetByteCount value).Append(':').Append(value) |> ignore

        Encoding.UTF8.GetBytes(builder.ToString())

    let digestParts values = values |> frame |> digestBytes

[<RequireQualifiedAccess>]
module JourneyReceipt =
    let schemaVersion (receipt: JourneyReceipt) = receipt.Data.SchemaVersion
    let runnerIdentity (receipt: JourneyReceipt) = receipt.Data.RunnerIdentity
    let runnerVersion (receipt: JourneyReceipt) = receipt.Data.RunnerVersion
    let compositionAuthority (receipt: JourneyReceipt) = receipt.Data.CompositionAuthority
    let origin (receipt: JourneyReceipt) = receipt.Data.Origin
    let routeId (receipt: JourneyReceipt) = receipt.Data.RouteId
    let scenarioId (receipt: JourneyReceipt) = receipt.Data.ScenarioId
    let testId (receipt: JourneyReceipt) = receipt.Data.TestId
    let inputKind (receipt: JourneyReceipt) = receipt.Data.InputKind
    let inputIdentity (receipt: JourneyReceipt) = receipt.Data.InputIdentity
    let inputDigest (receipt: JourneyReceipt) = receipt.Data.InputDigest
    let scriptDigest (receipt: JourneyReceipt) = receipt.Data.ScriptDigest
    let traceDigest (receipt: JourneyReceipt) = receipt.Data.TraceDigest
    let initialFingerprintDigest (receipt: JourneyReceipt) = receipt.Data.InitialFingerprintDigest
    let terminalFingerprintDigest (receipt: JourneyReceipt) = receipt.Data.TerminalFingerprintDigest
    let terminalPredicateIdentity (receipt: JourneyReceipt) = receipt.Data.TerminalPredicateIdentity
    let terminalPredicateReached (receipt: JourneyReceipt) = receipt.Data.TerminalPredicateReached
    let result (receipt: JourneyReceipt) = receipt.Data.Result
    let steps (receipt: JourneyReceipt) = receipt.Data.Steps
    let maxSteps (receipt: JourneyReceipt) = receipt.Data.MaxSteps

    let definitionDigest (receipt: JourneyReceipt) =
        let data = receipt.Data
        let origin =
            match data.Origin with
            | Origin.ProductionJourney -> "production-journey"
            | Origin.InputDriven -> "input-driven"
            | Origin.Synthetic -> "synthetic"
        let inputKind =
            match data.InputKind with
            | JourneyInputKind.FixedScript -> "fixed-script"
            | JourneyInputKind.SeededPolicy -> "seeded-policy"
        let outcome =
            match data.Result with
            | JourneyResult.Passed -> "passed"
            | JourneyResult.Failed reason -> "failed:" + reason

        Stable.digestParts
            [ string data.SchemaVersion
              origin
              data.RouteId
              data.ScenarioId
              data.TestId
              inputKind
              data.InputIdentity
              data.InputDigest
              data.ScriptDigest
              data.TraceDigest
              data.InitialFingerprintDigest
              data.TerminalFingerprintDigest
              data.TerminalPredicateIdentity
              (data.TerminalPredicateReached.ToString().ToLowerInvariant())
              outcome
              string data.Steps
              string data.MaxSteps ]

type IProductionJourneyProof =
    abstract TestId: string
    abstract Run: unit -> JourneyReceipt

type IProductionJourneyProofV1 =
    inherit IProductionJourneyProof
    abstract CompositionAuthority: string
    abstract RouteId: string
    abstract ScenarioId: string
    abstract InputIdentity: string
    abstract TerminalPredicateIdentity: string

type JourneyRun<'model, 'event, 'fingerprint> =
    { Trace: Trace<'fingerprint>
      Captured: 'event list
      Final: 'model
      Receipt: JourneyReceipt }

type JourneyPolicy<'model, 'event> =
    { DecideEvents: 'model -> Rng -> struct ('event list * Rng) }

type ActionCoverageReport = { Gaps: ActionCoverageGap list }

[<RequireQualifiedAccess>]
module ActionCoverageReport =
    let isClean report = List.isEmpty report.Gaps

    let describe report =
        report.Gaps
        |> List.map (function
            | ActionCoverageGap.UnexercisedUnbound(action, event) ->
                sprintf
                    "unbound action '%s' (event %s) is never reached by any committed script"
                    action
                    event
            | ActionCoverageGap.DegenerateVocabulary(slot, inhabitants) ->
                sprintf
                    "vocabulary slot '%s' supplies only %d distinct producible value(s); no committed \
                     script can construct a second, so an Unbound arm distinguishing them is \
                     unreachable by construction"
                    slot
                    inhabitants)

[<RequireQualifiedAccess>]
module Journey =
    let private finish
        (adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint>)
        (compositionAuthority: string)
        (inputKind: JourneyInputKind)
        (inputIdentity: string)
        (terminalPredicateIdentity: string)
        (inputDigest: string -> string)
        (captured: JourneyEvent<'key, 'pointer, 'menu, 'effectResult> list)
        (fingerprints: 'fingerprint list)
        (initialFingerprint: 'fingerprint)
        (model: 'model)
        (failure: string option)
        : JourneyRun<'model, JourneyEvent<'key, 'pointer, 'menu, 'effectResult>, 'fingerprint> =
        let encodedEvents = captured |> List.map adapter.EncodeEvent
        let encodedFrames = fingerprints |> List.map adapter.EncodeFingerprint
        let scriptDigest = Stable.digestParts encodedEvents
        let terminalReached = adapter.IsTerminal model
        let result =
            match failure with
            | Some reason -> JourneyResult.Failed reason
            | None when terminalReached -> JourneyResult.Passed
            | None ->
                JourneyResult.Failed(
                    sprintf
                        "terminal predicate not reached within %d event(s); final fingerprint sha256:%s; captured-input sha256:%s"
                        captured.Length
                        (Stable.digestParts [ adapter.EncodeFingerprint (adapter.Fingerprint model) ])
                        (Stable.digestParts encodedEvents)
                )

        let data =
            { SchemaVersion = 1
              RunnerIdentity = "FS.GG.Game.Harness.Journey"
              RunnerVersion =
                typeof<JourneyReceipt>.Assembly.GetName().Version
                |> Option.ofObj
                |> Option.map string
                |> Option.defaultValue "0.0.0.0"
              CompositionAuthority = compositionAuthority
              Origin = Origin.ProductionJourney
              RouteId = adapter.RouteId
              ScenarioId = adapter.ScenarioId
              TestId = adapter.TestId
              InputKind = inputKind
              InputIdentity = inputIdentity
              InputDigest = inputDigest scriptDigest
              ScriptDigest = scriptDigest
              TraceDigest = Stable.digestParts encodedFrames
              InitialFingerprintDigest =
                Stable.digestParts [ adapter.EncodeFingerprint initialFingerprint ]
              TerminalFingerprintDigest =
                Stable.digestParts [ adapter.EncodeFingerprint (adapter.Fingerprint model) ]
              TerminalPredicateIdentity = terminalPredicateIdentity
              TerminalPredicateReached = terminalReached
              Result = result
              Steps = captured.Length
              MaxSteps = adapter.MaxSteps }

        { Trace = Trace.create Origin.ProductionJourney fingerprints
          Captured = captured
          Final = model
          Receipt = JourneyReceipt data }

    let private applyEvent
        (adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint>)
        (event: JourneyEvent<'key, 'pointer, 'menu, 'effectResult>)
        (model: 'model)
        : Result<'model, string> =
        match event with
        | JourneyEvent.FixedTick -> Ok(adapter.FixedTick model)
        | JourneyEvent.EffectResult effect -> Ok(adapter.ApplyEffectResult effect model)
        | _ ->
            match adapter.MapEvent event model with
            | JourneyDispatch.Mapped messages ->
                Ok(messages |> List.fold (fun current msg -> adapter.Update msg current) model)
            | JourneyDispatch.Unbound action ->
                Error(sprintf "displayed action '%s' is unbound in the production route" action)

    let private validateExportIdentities
        inputIdentity
        terminalPredicateIdentity
        (adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint>)
        =
        [ "adapter.RouteId", adapter.RouteId
          "adapter.ScenarioId", adapter.ScenarioId
          "adapter.TestId", adapter.TestId
          "inputIdentity", inputIdentity
          "terminalPredicateIdentity", terminalPredicateIdentity ]
        |> List.iter (fun (name, value) ->
            if String.IsNullOrWhiteSpace value then
                invalidArg name "a serialized production journey identity must be non-empty")

    let private compositionAuthority
        (adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint>)
        =
        let functions: (string * objnull) list =
            [ "Boot", box (adapter.Boot : unit -> 'model)
              "MapEvent",
              box
                  (adapter.MapEvent:
                      JourneyEvent<'key, 'pointer, 'menu, 'effectResult>
                          -> 'model
                          -> JourneyDispatch<'message>)
              "Update", box (adapter.Update : 'message -> 'model -> 'model)
              "FixedTick", box (adapter.FixedTick : 'model -> 'model)
              "ApplyEffectResult", box (adapter.ApplyEffectResult : 'effectResult -> 'model -> 'model)
              "IsTerminal", box (adapter.IsTerminal : 'model -> bool) ]
        let identities =
            functions
            |> List.map (fun (name, value) ->
                let assembly =
                    match value with
                    | null -> invalidArg "adapter" ("production composition function is null: " + name)
                    | functionValue -> functionValue.GetType().Assembly
                let assemblyName =
                    assembly.GetName().Name
                    |> Option.ofObj
                    |> Option.defaultValue "<unnamed>"
                let mvid = assembly.ManifestModule.ModuleVersionId.ToString("N")
                name, assemblyName + "/" + mvid)
        let distinct = identities |> List.map snd |> List.distinct

        match distinct with
        | [ authority ] -> authority
        | _ ->
            let detail =
                identities
                |> List.map (fun (name, identity) -> name + "=" + identity)
                |> String.concat ", "
            invalidArg
                "adapter"
                ("production composition functions do not share one assembly authority: " + detail
                 + ". Likely cause: a caller-constructed closure crossing into the composition -- for \
                    example, a product-side boot factory that wraps a caller-supplied model \
                    (journeyBootOf model = fun () -> model) still returns a closure whose composition \
                    authority follows the caller that built it, not the factory that returned it. Give \
                    the product the whole entry point instead: a function that takes a script (and only \
                    product-internal parameters, never a caller-supplied closure) and no model \
                    parameter at all, so every composition function is authored inside the product \
                    module.")

    let private runScriptCore
        (compositionAuthority: string)
        inputIdentity
        terminalPredicateIdentity
        (adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint>)
        (script: JourneyEvent<'key, 'pointer, 'menu, 'effectResult> list)
        =
        if adapter.MaxSteps <= 0 then
            invalidArg "adapter.MaxSteps" "a production journey must declare a positive maximum"

        let captured = script |> List.truncate adapter.MaxSteps
        let mutable model = adapter.Boot()
        let initialFingerprint = adapter.Fingerprint model
        let frames = ResizeArray<_>(captured.Length)
        let mutable failure =
            if adapter.IsTerminal model then
                Some "terminal predicate is already satisfied at boot; no production journey was executed"
            elif script.Length > adapter.MaxSteps then
                Some(sprintf "script exceeds declared maximum of %d event(s)" adapter.MaxSteps)
            else
                None

        for event in captured do
            if failure.IsNone then
                match applyEvent adapter event model with
                | Ok next ->
                    model <- next
                    frames.Add(adapter.Fingerprint model)
                | Error reason -> failure <- Some reason

        finish
            adapter
            compositionAuthority
            JourneyInputKind.FixedScript
            inputIdentity
            terminalPredicateIdentity
            id
            captured
            (List.ofSeq frames)
            initialFingerprint
            model
            failure

    let runScriptWithIdentity inputIdentity terminalPredicateIdentity adapter script =
        validateExportIdentities inputIdentity terminalPredicateIdentity adapter
        runScriptCore (compositionAuthority adapter) inputIdentity terminalPredicateIdentity adapter script

    let runScript adapter script =
        runScriptCore "" "" "" adapter script

    let private runPolicyCore
        (compositionAuthority: string)
        policyIdentity
        terminalPredicateIdentity
        (adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint>)
        (policy: JourneyPolicy<'model, JourneyEvent<'key, 'pointer, 'menu, 'effectResult>>)
        seed
        =
        if adapter.MaxSteps <= 0 then
            invalidArg "adapter.MaxSteps" "a production journey must declare a positive maximum"

        let mutable model = adapter.Boot()
        let initialFingerprint = adapter.Fingerprint model
        let mutable rng = Rng.ofSeed seed
        let captured = ResizeArray<_>()
        let frames = ResizeArray<_>()
        let mutable failure =
            if adapter.IsTerminal model then
                Some "terminal predicate is already satisfied at boot; no production journey was executed"
            else
                None

        while captured.Count < adapter.MaxSteps && failure.IsNone && not (adapter.IsTerminal model) do
            let struct (events, nextRng) = policy.DecideEvents model rng
            rng <- nextRng

            if List.isEmpty events then
                failure <- Some "seeded policy emitted no event before reaching its terminal predicate"
            else
                for event in events do
                    if captured.Count < adapter.MaxSteps && failure.IsNone then
                        captured.Add event

                        match applyEvent adapter event model with
                        | Ok next ->
                            model <- next
                            frames.Add(adapter.Fingerprint model)
                        | Error reason -> failure <- Some reason

        let policyDigest scriptDigest =
            Stable.digestParts
                [ "seeded-policy"
                  seed.ToString(CultureInfo.InvariantCulture)
                  scriptDigest ]

        finish
            adapter
            compositionAuthority
            JourneyInputKind.SeededPolicy
            policyIdentity
            terminalPredicateIdentity
            policyDigest
            (List.ofSeq captured)
            (List.ofSeq frames)
            initialFingerprint
            model
            failure

    let runPolicyWithIdentity policyIdentity terminalPredicateIdentity adapter policy seed =
        validateExportIdentities policyIdentity terminalPredicateIdentity adapter
        runPolicyCore
            (compositionAuthority adapter)
            policyIdentity
            terminalPredicateIdentity
            adapter
            policy
            seed

    let runPolicy adapter policy seed =
        runPolicyCore "" "" "" adapter policy seed

    let private slotCardinalityGap
        (adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint>)
        (vocabulary: JourneyEvent<'key, 'pointer, 'menu, 'effectResult> list)
        (slot: string)
        (normalize: JourneyEvent<'key, 'pointer, 'menu, 'effectResult> -> JourneyEvent<'key, 'pointer, 'menu, 'effectResult> option)
        =
        let inhabitants =
            vocabulary
            |> List.choose normalize
            |> List.map adapter.EncodeEvent
            |> List.distinct
            |> List.length

        if inhabitants = 1 then
            Some(ActionCoverageGap.DegenerateVocabulary(slot, inhabitants))
        else
            None

    let checkActionCoverage
        (adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint>)
        (vocabulary: JourneyEvent<'key, 'pointer, 'menu, 'effectResult> list)
        (committedScripts: JourneyEvent<'key, 'pointer, 'menu, 'effectResult> list list)
        : ActionCoverageReport =
        let model = adapter.Boot()

        let exercised =
            committedScripts
            |> List.collect id
            |> List.map adapter.EncodeEvent
            |> Set.ofList

        let unexercisedUnbound =
            vocabulary
            |> List.choose (fun event ->
                match adapter.MapEvent event model with
                | JourneyDispatch.Mapped _ -> None
                | JourneyDispatch.Unbound action ->
                    let encoded = adapter.EncodeEvent event
                    if Set.contains encoded exercised then
                        None
                    else
                        Some(ActionCoverageGap.UnexercisedUnbound(action, encoded)))

        // Cardinality is read off the DECLARED vocabulary, not off reflection over 'menu/'key/
        // 'pointer: the harness never constrains those type parameters to `equality`, and a
        // caller-declared vocabulary is exactly the set this check can otherwise ever exercise.
        // `KeyInput` is normalized to `pressed = true` so a key's up/down pair counts once, per
        // key, rather than doubling every key's inhabitant count.
        let degenerate =
            [ slotCardinalityGap adapter vocabulary "menu" (function
                  | JourneyEvent.MenuAction _ as event -> Some event
                  | _ -> None)
              slotCardinalityGap adapter vocabulary "key" (function
                  | JourneyEvent.KeyInput(key, _) -> Some(JourneyEvent.KeyInput(key, true))
                  | _ -> None)
              slotCardinalityGap adapter vocabulary "pointer" (function
                  | JourneyEvent.PointerInput _ as event -> Some event
                  | _ -> None) ]
            |> List.choose id

        { Gaps = unexercisedUnbound @ degenerate }

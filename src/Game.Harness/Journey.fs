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

type IProductionJourneyProof =
    abstract TestId: string
    abstract Run: unit -> JourneyReceipt

type IProductionJourneyProofV1 =
    inherit IProductionJourneyProof
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

[<RequireQualifiedAccess>]
module Journey =
    let private finish
        (adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint>)
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

    let private runScriptCore
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
        runScriptCore inputIdentity terminalPredicateIdentity adapter script

    let runScript adapter script =
        runScriptCore "" "" adapter script

    let private runPolicyCore
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
        runPolicyCore policyIdentity terminalPredicateIdentity adapter policy seed

    let runPolicy adapter policy seed =
        runPolicyCore "" "" adapter policy seed

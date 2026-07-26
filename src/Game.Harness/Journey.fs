namespace FS.GG.Game.Harness

open System
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

type internal ReceiptData =
    { RouteId: string
      ScenarioId: string
      TestId: string
      ScriptDigest: string
      TraceDigest: string
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
    let routeId (receipt: JourneyReceipt) = receipt.Data.RouteId
    let scenarioId (receipt: JourneyReceipt) = receipt.Data.ScenarioId
    let testId (receipt: JourneyReceipt) = receipt.Data.TestId
    let scriptDigest (receipt: JourneyReceipt) = receipt.Data.ScriptDigest
    let traceDigest (receipt: JourneyReceipt) = receipt.Data.TraceDigest
    let result (receipt: JourneyReceipt) = receipt.Data.Result
    let steps (receipt: JourneyReceipt) = receipt.Data.Steps
    let maxSteps (receipt: JourneyReceipt) = receipt.Data.MaxSteps

type IProductionJourneyProof =
    abstract TestId: string
    abstract Run: unit -> JourneyReceipt

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
        (captured: JourneyEvent<'key, 'pointer, 'menu, 'effectResult> list)
        (fingerprints: 'fingerprint list)
        (model: 'model)
        (failure: string option)
        : JourneyRun<'model, JourneyEvent<'key, 'pointer, 'menu, 'effectResult>, 'fingerprint> =
        let encodedEvents = captured |> List.map adapter.EncodeEvent
        let encodedFrames = fingerprints |> List.map adapter.EncodeFingerprint
        let result =
            match failure with
            | Some reason -> JourneyResult.Failed reason
            | None when adapter.IsTerminal model -> JourneyResult.Passed
            | None ->
                JourneyResult.Failed(
                    sprintf
                        "terminal predicate not reached within %d event(s); final fingerprint sha256:%s; captured-input sha256:%s"
                        captured.Length
                        (Stable.digestParts [ adapter.EncodeFingerprint (adapter.Fingerprint model) ])
                        (Stable.digestParts encodedEvents)
                )

        let data =
            { RouteId = adapter.RouteId
              ScenarioId = adapter.ScenarioId
              TestId = adapter.TestId
              ScriptDigest = Stable.digestParts encodedEvents
              TraceDigest = Stable.digestParts encodedFrames
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

    let runScript
        (adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint>)
        (script: JourneyEvent<'key, 'pointer, 'menu, 'effectResult> list)
        =
        if adapter.MaxSteps <= 0 then
            invalidArg "adapter.MaxSteps" "a production journey must declare a positive maximum"

        let captured = script |> List.truncate adapter.MaxSteps
        let mutable model = adapter.Boot()
        let frames = ResizeArray<_>(captured.Length)
        let mutable failure =
            if script.Length > adapter.MaxSteps then
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

        finish adapter captured (List.ofSeq frames) model failure

    let runPolicy
        (adapter: ProductionJourney<'model, 'key, 'pointer, 'menu, 'effectResult, 'message, 'fingerprint>)
        (policy: JourneyPolicy<'model, JourneyEvent<'key, 'pointer, 'menu, 'effectResult>>)
        seed
        =
        if adapter.MaxSteps <= 0 then
            invalidArg "adapter.MaxSteps" "a production journey must declare a positive maximum"

        let mutable model = adapter.Boot()
        let mutable rng = Rng.ofSeed seed
        let captured = ResizeArray<_>()
        let frames = ResizeArray<_>()
        let mutable failure = None

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

        finish adapter (List.ofSeq captured) (List.ofSeq frames) model failure

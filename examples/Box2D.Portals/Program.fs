module Box2D.Portals.Program

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json

let private qualifyDefault () =
    let observed = Scene.qualify ()
    let event = observed.Head.Traversals.Head
    printfn "PASS portal: tick=%d entity=%s source=%A destination=%A bodies=1 traversals=1 replay=exact"
        event.Tick event.Entity event.Source event.Destination
    printfn "pose=(%g,%g) velocity=(%g,%g) angularVelocity=%g"
        event.DestinationPose.Position.X event.DestinationPose.Position.Y
        event.DestinationPose.LinearVelocity.X event.DestinationPose.LinearVelocity.Y event.DestinationPose.AngularVelocity
    0

let private sha256 (bytes: byte array) = SHA256.HashData bytes |> Convert.ToHexStringLower

let private qualifyPerformance sourceRevision =
    // Worlds are disposed inside qualify, before any warmup or timed view invocation.
    let snapshots, referenceFrames = Presentation.qualify ()
    if snapshots.Length <> 5 || referenceFrames.Length <> 12 then
        failwith "Performance workload must have five snapshots and twelve reference frames."
    let inputs =
        snapshots |> List.pairwise |> List.collect (fun (previous, current) ->
            [ for alpha in [ 0.; 0.5; 1. ] do yield previous, current, alpha ]) |> List.toArray
    let expected = List.toArray referenceFrames
    let check index (frame: Presentation.Frame) =
        let _, current, _ = inputs[index % inputs.Length]
        let nodes, points = Presentation.inspect current frame
        let reference = expected[index % expected.Length]
        let bindings (value: Presentation.Frame) = value.Areas |> List.map (fun area -> area.Area, area.Bindings)
        if frame.Tick <> reference.Tick || frame.Alpha <> reference.Alpha || bindings frame <> bindings reference then
            failwith "Measured view differs from the qualified reference."
        if nodes < 1 || nodes > 2 || points <> 2 then failwith "Performance structural bounds differ."
        nodes, points
    for index in 0 .. expected.Length - 1 do check index expected[index] |> ignore
    for _ in 1 .. 5 do
        for index in 0 .. inputs.Length - 1 do
            let previous, current, alpha = inputs[index]
            Presentation.view alpha previous current |> check index |> ignore

    // Storage is allocated once outside all timed intervals. Every returned frame is
    // retained and inspected afterwards; inspection/serialization are not timed.
    let frames = Array.zeroCreate<Presentation.Frame> (100 * inputs.Length)
    use measuredProcess = Process.GetCurrentProcess ()
    let samples =
        [| for sample in 1 .. 20 do
            let collectionsBefore = [| for generation in 0 .. 2 -> GC.CollectionCount generation |]
            let cpuBefore = measuredProcess.TotalProcessorTime.Ticks
            let allocationBefore = GC.GetAllocatedBytesForCurrentThread ()
            let started = Stopwatch.GetTimestamp ()
            for index in 0 .. frames.Length - 1 do
                let previous, current, alpha = inputs[index % inputs.Length]
                frames[index] <- Presentation.view alpha previous current
            let elapsedTicks = Stopwatch.GetTimestamp () - started
            let allocatedBytes = GC.GetAllocatedBytesForCurrentThread () - allocationBefore
            let processCpuTicks = measuredProcess.TotalProcessorTime.Ticks - cpuBefore
            let collections = [| for generation in 0 .. 2 -> GC.CollectionCount generation - collectionsBefore[generation] |]
            let mutable areaNodes = 0
            let mutable points = 0
            for index in 0 .. frames.Length - 1 do
                let nodes, count = check index frames[index]
                areaNodes <- areaNodes + nodes
                points <- points + count
            if areaNodes <> 2400 || points <> 2400 then failwith "Measured workload totals differ."
            yield
                {| sample = sample; views = frames.Length; elapsedTicks = elapsedTicks
                   elapsedNanosecondsPerView = float elapsedTicks * 1e9 / float Stopwatch.Frequency / float frames.Length
                   processCpuTicks = processCpuTicks
                   processCpuNanosecondsPerView = float processCpuTicks * 100. / float frames.Length
                   currentThreadAllocatedBytes = allocatedBytes
                   currentThreadAllocatedBytesPerView = float allocatedBytes / float frames.Length
                   processGcCollections = collections; areaNodes = areaNodes; points = points |} |]
    let distribution values =
        let sorted = Array.sort values
        {| minimum = sorted[0]; median = (sorted[9] + sorted[10]) / 2.; maximum = sorted[19] |}
    let definition = "GAME-PORTAL-01.P3;Presentation.view;areas=2;entities=2;setupTicks=5;pairs=4;alpha=0,0.5,1;viewsPerCycle=12;warmupCycles=5;samples=20;cyclesPerSample=100;maxAreaNodesPerView=2;pointsPerView=2;noSimulationDuringMeasurement;v1"
    let assembly = typeof<Presentation.Frame>.Assembly.Location
    let result =
        {| schema = "fsgg.portal.presentation-smoke/1"; originalItem = "GAME-PORTAL-01.P3"
           declaredSourceRevision = sourceRevision; assemblySha256 = File.ReadAllBytes assembly |> sha256
           workloadDefinition = definition; workloadSha256 = Encoding.UTF8.GetBytes definition |> sha256
           historicalPreEditBaseline = "unknown"; verdict = "current-smoke-observed-no-timing-threshold"
           scope = "Actual Presentation.view interpolation+scene projection; loop, frame storage and counter overhead included. Setup, validation and JSON outside timing. No physics, raster, GPU or product-adoption timing."
           counterScope = "Elapsed stopwatch; process CPU and GC collections; current synchronous measurement-thread allocations. Process CPU has coarse resolution and may include runtime threads. No overhead subtraction."
           runtime = Environment.Version.ToString (); os = System.Runtime.InteropServices.RuntimeInformation.OSDescription
           architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString ()
           processorCount = Environment.ProcessorCount; stopwatchFrequency = Stopwatch.Frequency
           stopwatchHighResolution = Stopwatch.IsHighResolution; warmupCycles = 5; samples = samples
           measuredViews = samples |> Array.sumBy _.views
           elapsedNanosecondsPerView = samples |> Array.map _.elapsedNanosecondsPerView |> distribution
           processCpuNanosecondsPerView = samples |> Array.map _.processCpuNanosecondsPerView |> distribution
           currentThreadAllocatedBytesPerView = samples |> Array.map _.currentThreadAllocatedBytesPerView |> distribution |}
    JsonSerializer.Serialize(result, JsonSerializerOptions(WriteIndented = true)) |> printfn "%s"
    0

[<EntryPoint>]
let main argv =
    if argv = [| "--presentation" |] then
        let snapshots, frames = Presentation.qualify ()
        let counts = frames |> List.map (fun frame -> Presentation.inspect snapshots[int frame.Tick - 1] frame)
        printfn "PASS portal presentation: ticks=%d frames=%d areaNodes=%d points=%d alpha=0,0.5,1 local-metres=+Y-up"
            snapshots.Length frames.Length (counts |> List.sumBy fst) (counts |> List.sumBy snd)
        0
    elif argv = [| "--presentation-performance" |] then
        match Environment.GetEnvironmentVariable "FSGG_PORTAL_SOURCE_REVISION" |> Option.ofObj with
        | Some revision when revision.Length = 40 && revision |> Seq.forall (fun c -> c >= '0' && c <= '9' || c >= 'a' && c <= 'f') ->
            qualifyPerformance revision
        | _ ->
            eprintfn "--presentation-performance requires FSGG_PORTAL_SOURCE_REVISION (exact lowercase 40-hex; declared externally, not inferred)."
            2
    elif argv.Length <> 0 then
        eprintfn "Usage: Box2D.Portals [--presentation]"
        eprintfn "Performance smoke: --presentation-performance (requires FSGG_PORTAL_SOURCE_REVISION)."
        2
    else
        qualifyDefault ()

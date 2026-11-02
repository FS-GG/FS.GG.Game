module Program

// FS.GG.Game#795 (GAME-RENDER-02.8), B4 of the #805 review: theme derivation is byte-identical on .NET and Fable,
// run-time custom seeds included (glyph ADR Q12). scripts/test-theme-fuzz.sh runs this on .NET and on node and compares
// the two outputs byte for byte. It prints:
// - the #805 review's 120 custom seeds (`reviewSeed`), then every derivation of the custom-seed fuzz: SEEDS seeds from a fixed MINSTD generator, each through the five preset
//   algorithm lists, as `seed list OK <35 hex>` or `seed list ERR <violation count> <rules>`. A third of the seeds are random,
//   a third near-grey (grounds within one step of grey, chroma 0 to 0.01) and a third near a boundary (grounds around
//   the 0.6 polarity threshold, hues at 0 and just under 360, chroma at the hue floor);
// - the full 8-bit OKLCH round trip's failure count, the digest of the gridded derivation inputs of all 16,777,216
//   colours, and the digest of `ofOklch` over the grid derivation visits;
// - a summary line, `theme-fuzz seeds=N derivations=M ok=K checks=C failed=F digest=D`.

open FS.GG.Game.Reference.Experimental
open FS.GG.Game.Reference.Experimental.Colour
open FS.GG.Game.Reference.Experimental.Themes

/// MINSTD (Park and Miller): exact in IEEE doubles (the product stays under 2^53), so .NET and Fable draw the same
/// numbers.
let mutable private state = 795.0

let private next () =
    state <- (state * 48271.0) % 2147483647.0
    state / 2147483647.0

let private below (n: int) = int (System.Math.Floor(next () * float n))
let private hue () = System.Math.Floor(next () * 36000.0) / 100.0

/// The seed of fuzz case `i`. Every draw is its own `let`, in order: F# and Fable may order the evaluation of one
/// expression's arguments differently, so no expression draws more than once.
let seedOf (i: int) : Seed =
    let kind = i % 3
    let draws n f = [ for _ in 1..n -> f () ]
    let ground, chroma =
        match kind with
        | 0 ->
            let r = below 256
            let g = below 256
            let b = below 256
            let c = System.Math.Floor(next () * 200.0) / 1000.0
            rgb r g b, c
        | 1 ->
            let v = below 256
            let near d = max 0 (min 255 (v + d))
            let dg = below 3 - 1
            let db = below 3 - 1
            let c = [ 0.0; 0.001; 0.002; 0.005; 0.01 ].[below 5]
            rgb v (near dg) (near db), c
        | _ ->
            let v = 140 + below 16
            let db = below 3 - 1
            let c = [ hueChromaFloor; 0.0009; 0.0011; 0.14 ].[below 4]
            rgb v v (max 0 (min 255 (v + db))), c
    let edgeHue () =
        let random = hue ()
        let k = below 5
        [ 0.0; 359.9999; 359.99995; 0.00005; random ].[k]
    let pick n = if kind = 2 then draws n edgeHue else draws n hue
    let allegiances = pick 5
    let teams = pick 8
    let selection = (pick 1).Head
    let marker = (pick 1).Head
    let good = 150.0 + float (below 40) - 20.0
    let warn = 75.0 + float (below 30) - 15.0
    let bad = 25.0 + float (below 30) - 15.0
    { classicSeed with
        Name = sprintf "fuzz%d" i
        Ground = ground
        Chroma = chroma
        AllegianceHues = allegiances
        TeamHues = teams
        SelectionHue = selection
        MarkerHue = marker
        StatusHues = [ good; warn; bad ] }

/// The #805 review's 120 custom seeds (its net-probe3.fsx), which found B4: seed 22, ground #281a22, colourBlindSafe,
/// gave terrain.blocked #3b3c3c on .NET and #3c3c3c under Fable before derivation inputs were gridded.
let reviewSeed (i: int) : Seed =
    let ground = rgb (18 + (i % 30)) (22 + ((i * 7) % 30)) (28 + ((i * 13) % 35))
    let rot = float i * 1.7
    let hues xs = xs |> List.map (fun x -> (x + rot) % 360.0)
    { classicSeed with
        Name = sprintf "s%d" i
        Ground = ground
        Chroma = 0.08 + 0.005 * float (i % 20)
        AllegianceHues = hues [ 240.0; 195.0; 145.0; 25.0; 100.0 ]
        TeamHues = hues [ 255.0; 55.0; 175.0; 330.0; 130.0; 295.0; 95.0; 20.0 ]
        SelectionHue = (95.0 + rot) % 360.0
        MarkerHue = (200.0 + rot) % 360.0
        StatusHues = hues [ 150.0; 75.0; 25.0 ] }

/// A violation's rule and the aliases it names, without measured values (their formatting is the runtime's).
let violationKey (v: ThemeViolation) =
    match v with
    | NotOpaque a -> "opaque " + a
    | UnknownAlias a -> "unknown " + a
    | BelowFloor(a, b, _, f) -> sprintf "floor %s/%s/%s" a b (fixedText 1 f)
    | NotDistinct(a, b, vision, _, _) -> sprintf "distinct %s/%s/%A" a b vision
    | LightnessTooClose(a, b, vision, _, _) -> sprintf "lightness %s/%s/%A" a b vision
    | StatusMeaningLost(a, _, band) -> sprintf "status %s %s" a band
    | TeamOutsideGame(t, n) -> sprintf "team %d/%d" t n
    | BadToken(a, _) -> "token " + a

let private lists =
    [
        "dark", [ baseAlgorithm; dark; repair ]
        "light", [ baseAlgorithm; light; repair ]
        "highContrast", [ baseAlgorithm; dark; highContrast; repair ]
        "colourBlindSafe", [ baseAlgorithm; dark; colourBlindSafe; repair ]
        "monochrome", [ baseAlgorithm; dark; monochrome; repair ]
    ]

[<EntryPoint>]
let main argv =
    let seeds = if argv.Length > 0 then int argv.[0] else 2000
    let mutable digest = 0.0
    let add (text: string) =
        for ch in text do
            digest <- (digest * 257.0 + float (int ch)) % 2147483647.0
    let emit (line: string) =
        add line
        printfn "%s" line
    let mutable derivations = 0
    let mutable ok = 0
    let fx (xs: float list) = xs |> List.map (fixedText 5) |> String.concat " "
    let fuzz (label: string) (seed: Seed) =
        emit (
            sprintf
                "%s seed %s c=%s a=[%s] t=[%s] s=%s m=%s st=[%s]"
                label
                (hex seed.Ground)
                (fixedText 5 seed.Chroma)
                (fx seed.AllegianceHues)
                (fx seed.TeamHues)
                (fixedText 5 seed.SelectionHue)
                (fixedText 5 seed.MarkerHue)
                (fx seed.StatusHues)
        )
        for name, algorithms in lists do
            derivations <- derivations + 1
            match create seed algorithms Map.empty with
            | Ok theme ->
                ok <- ok + 1
                emit (sprintf "%s %s OK %s" label name (hexes theme |> List.map snd |> String.concat ","))
            | Error violations ->
                emit (sprintf "%s %s ERR %d %s" label name violations.Length (violations |> List.map violationKey |> String.concat ";"))
    for i in 0..119 do
        fuzz (sprintf "review%d" i) (reviewSeed i)
    for i in 0 .. seeds - 1 do
        fuzz (string i) (seedOf i)
    let mutable failed = 0
    let check (name: string) (pass: bool) (detail: string) =
        emit (sprintf "%s %s: %s" (if pass then "PASS" else "FAIL") name detail)
        if not pass then failed <- failed + 1
    let roundTrip = roundTripFailures ()
    check "the 8-bit OKLCH round trip" (roundTrip = 0) (sprintf "%d of 16777216 fail" roundTrip)
    check "the derivation inputs of every 8-bit colour" true (sprintf "digest %s" (fixedText 0 (inputDigest ())))
    check "ofOklch over derivation's grid" true (sprintf "digest %s" (fixedText 0 (gridDigest ())))
    check "every preset derives" (presets |> List.forall (fun p -> (derive p) |> Result.isOk)) "5 presets"
    check "the fuzz derived" (derivations = (seeds + 120) * lists.Length) (sprintf "%d derivations, %d accepted" derivations ok)
    printfn "theme-fuzz seeds=%d derivations=%d ok=%d checks=5 failed=%d digest=%s" seeds derivations ok failed (fixedText 0 digest)
    if failed = 0 then 0 else 1

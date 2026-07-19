namespace FS.GG.Game.Core

open System

[<Struct>]
type AgentId = AgentId of int

[<Struct>]
type Sighting<'T> =
    { Agent: AgentId
      Position: Point
      Seen: 'T
      LastSeenTick: int }

/// The fog boundary. Abstract in the `.fsi`: no public constructor, no public field, and every member is
/// internal to this assembly, so no caller can recover the world model from a value of this type.
[<Sealed>]
type TeamView<'T>(tick: int, spotted: Sighting<'T> list, ghosts: Sighting<'T> list) =
    member internal _.Tick = tick
    member internal _.Spotted = spotted
    member internal _.Ghosts = ghosts

[<Struct>]
type Difficulty =
    { ReactionTicks: int
      AimErrorSigma: float
      SpotCycleTicks: int
      UsesWeakPointTargeting: bool
      ThreatWeight: float }

[<RequireQualifiedAccess>]
module Ai =

    /// Ascending `AgentId` is the iteration order of the whole decision layer. Sorting on the raw int keeps
    /// it a total order with no comparer indirection.
    let private byAgent (s: Sighting<'T>) =
        let (AgentId id) = s.Agent
        id

    let view (tick: int) (ghostLifetimeTicks: int) (sightings: Sighting<'T> list) : TeamView<'T> =
        let lifetime = max 0 ghostLifetimeTicks

        // Collapse duplicate ids to the freshest sighting. `List.fold` visits in input order and only
        // replaces on a strictly newer tick, so ties keep the earliest — total, and independent of the
        // Map's internal ordering because we re-sort on the way out.
        let freshest =
            sightings
            |> List.fold
                (fun acc s ->
                    match Map.tryFind (byAgent s) acc with
                    | Some (prev: Sighting<'T>) when prev.LastSeenTick >= s.LastSeenTick -> acc
                    | _ -> Map.add (byAgent s) s acc)
                Map.empty
            |> Map.toList
            |> List.map snd

        // A sighting from the future is clamped to "spotted" rather than rejected: a caller that stamps a
        // sighting one tick ahead should get an agent that can see, not one that silently goes blind.
        let isSpotted (s: Sighting<'T>) = s.LastSeenTick >= tick
        let isForgotten (s: Sighting<'T>) = tick - s.LastSeenTick > lifetime

        let live = freshest |> List.filter (isForgotten >> not)
        let spotted = live |> List.filter isSpotted |> List.sortBy byAgent
        let ghosts = live |> List.filter (isSpotted >> not) |> List.sortBy byAgent
        TeamView<'T>(tick, spotted, ghosts)

    let viewTick (view: TeamView<'T>) = view.Tick
    let spotted (view: TeamView<'T>) = view.Spotted
    let ghosts (view: TeamView<'T>) = view.Ghosts

    /// Both halves are already ascending, so merge rather than re-sorting the concatenation. An agent appears
    /// in exactly one of them (`view` deduplicates), so no tie-break is needed here.
    let known (view: TeamView<'T>) =
        let rec merge xs ys =
            match xs, ys with
            | [], rest
            | rest, [] -> rest
            | x :: xt, y :: yt -> if byAgent x <= byAgent y then x :: merge xt ys else y :: merge xs yt

        merge view.Spotted view.Ghosts

    let tryFind (agent: AgentId) (view: TeamView<'T>) =
        let find = List.tryFind (fun (s: Sighting<'T>) -> s.Agent = agent)

        match find view.Spotted with
        | Some s -> ValueSome s
        | None -> find view.Ghosts |> ValueOption.ofOption

    /// SplitMix64's golden-ratio increment. Mixing the id into the root state with it — rather than walking
    /// `Rng.split` down a list — is what keeps an agent's stream a function of its identity alone.
    [<Literal>]
    let private Golden = 0x9E3779B97F4A7C15UL

    let substream (root: Rng) (agent: AgentId) : Rng =
        let (AgentId id) = agent
        // `Golden` is odd, so `id -> Golden * id` is a bijection on uint64 and `keyed` is injective in `id`:
        // two agents can never share a seed. Folding `Golden` in additively as well keeps `AgentId 0` off the
        // root state (a bare product would map it there). `AgentId 1` does land `keyed` back on `root.State`,
        // which is harmless — `ofSeed`+`split` below transform it, so the agent's stream is still not the
        // root's; injectivity, not seed-distinctness from the root, is what matters here.
        // Unchecked throughout: a negative or `Int32.MinValue` id wraps rather than throwing — ids are
        // identities, not magnitudes.
        let keyed = root.State ^^^ Golden ^^^ (Golden * uint64 (int64 id))
        // One `split` decorrelates the mixed seed, so neighbouring ids do not yield neighbouring streams.
        let struct (stream, _) = Rng.split (Rng.ofSeed keyed)
        stream

    let due (cadenceTicks: int) (tick: int) =
        if cadenceTicks <= 0 then
            false
        else
            // Floored modulus: `-1 % 15` is `-1` in .NET, so a pre-match countdown would never be due.
            ((tick % cadenceTicks) + cadenceTicks) % cadenceTicks = 0

    let aimError (sigma: float) (rng: Rng) : struct (float * Rng) =
        if not (Double.IsFinite sigma) || sigma <= 0.0 then
            // Draw nothing. A perfectly-accurate agent must not consume randomness, or enabling aim error on
            // one agent would shift every other agent's stream in a replay.
            struct (0.0, rng)
        else
            let struct (u1, r1) = Rng.nextFloat rng
            let struct (u2, r2) = Rng.nextFloat r1
            // `nextFloat` is `[0, 1)`, so `u1` can be exactly 0 and `log 0 = -infinity`. Nudge it onto the
            // smallest positive subnormal instead of rejection-sampling, which would consume a variable number
            // of draws and so make one agent's stream depend on its own luck. The nudge caps the deviation at
            // ~38.6σ rather than producing an infinity; at p = 2^-1074 it is not a distribution concern.
            let u1 = if u1 <= 0.0 then Double.Epsilon else u1
            let z = sqrt (-2.0 * log u1) * cos (2.0 * Math.PI * u2)
            struct (sigma * z, r2)

    let threatField
        (hasLos: Cell -> Cell -> bool)
        (sources: (Cell * float * int) list)
        (cells: Cell list)
        : Map<Cell, float> =

        // Float addition is not associative, so the sum must not depend on the order the caller passed the
        // sources in — otherwise the map depends on SpatialGrid insertion order and the replay diverges the
        // moment an entity spawns. Sort into a canonical total order once, up front.
        let ordered =
            sources
            |> List.filter (fun (_, dps, _) -> Double.IsFinite dps)
            |> List.sortBy (fun (c, dps, range) -> struct (c.Col, c.Row, dps, range))

        let inRange (a: Cell) (b: Cell) (range: int) =
            if range < 0 then
                false
            else
                let dc = abs (int64 a.Col - int64 b.Col)
                let dr = abs (int64 a.Row - int64 b.Row)
                let r = int64 range
                // Reject per-axis first. Squaring an unbounded `dc` overflows `int64` (two `Int32` extremes
                // differ by ~4.3e9, whose square is ~1.8e19 > Int64.MaxValue) and the product wraps NEGATIVE,
                // so a cell on the far side of the map would read as in range. After this guard `dc, dr <= r`,
                // and `2 × (2^31 - 1)^2 < Int64.MaxValue`, so the sum below cannot overflow.
                if dc > r || dr > r then false else dc * dc + dr * dr <= r * r

        cells
        |> List.fold
            (fun acc cell ->
                let danger =
                    ordered
                    |> List.fold
                        (fun total (src, dps, range) ->
                            if inRange cell src range && hasLos cell src then total + dps else total)
                        0.0

                Map.add cell danger acc)
            Map.empty

    let fleeField
        (neighbourhood: Neighbourhood)
        (coefficient: float)
        (maxPasses: int)
        (desire: Map<Cell, float>)
        : Map<Cell, float> =

        // Check the SCALED value, not just the input: a non-finite `coefficient` turns a clean field into
        // NaN, and a large-but-finite desire (≈1.7e308) overflows to -infinity under a routine -1.2. Either
        // escapes into the relaxation, where `value > lowest + 1.0` is false for NaN and the poison simply
        // survives to the caller.
        let scaled =
            desire
            |> Map.toList
            |> List.choose (fun (c, v) ->
                let s = v * coefficient
                if Double.IsFinite v && Double.IsFinite s then Some(c, s) else None)
            |> Map.ofList

        if maxPasses <= 0 || scaled.IsEmpty then
            scaled
        else
            let orthogonals (c: Cell) =
                [ { c with Col = c.Col - 1 }
                  { c with Col = c.Col + 1 }
                  { c with Row = c.Row - 1 }
                  { c with Row = c.Row + 1 } ]

            // The same no-corner-cutting rule `Pathfinding.flowField` applies: a diagonal is a neighbour only
            // when both shared orthogonals are also in the field.
            let diagonals (c: Cell) (field: Map<Cell, float>) =
                [ for dc in [ -1; 1 ] do
                      for dr in [ -1; 1 ] do
                          let side1 = { c with Col = c.Col + dc }
                          let side2 = { c with Row = c.Row + dr }

                          if field.ContainsKey side1 && field.ContainsKey side2 then
                              { Col = c.Col + dc; Row = c.Row + dr } ]

            let neighbours (c: Cell) (field: Map<Cell, float>) =
                match neighbourhood with
                | FourWay -> orthogonals c
                | EightWay -> orthogonals c @ diagonals c field

            // Sweep in ascending (Col, Row) so the fixpoint is reached by the same route every time. `Map` is
            // ordered by its key's comparison, and `Cell` is a struct record of two ints, so `Map.toList` is
            // already ascending (Col, Row) — but sort explicitly rather than lean on that.
            let sweep (field: Map<Cell, float>) =
                field
                |> Map.toList
                |> List.sortBy (fun (c, _) -> struct (c.Col, c.Row))
                |> List.fold
                    (fun (acc: Map<Cell, float>, changed) (cell, _) ->
                        let value = acc.[cell]

                        let lowest =
                            neighbours cell acc
                            |> List.fold
                                (fun lo n ->
                                    match Map.tryFind n acc with
                                    | Some v -> min lo v
                                    | None -> lo)
                                Double.PositiveInfinity

                        if Double.IsFinite lowest && value > lowest + 1.0 then
                            (Map.add cell (lowest + 1.0) acc, true)
                        else
                            (acc, changed))
                    (field, false)

            let rec run field pass =
                if pass >= maxPasses then
                    field
                else
                    let next, changed = sweep field
                    if changed then run next (pass + 1) else next

            run scaled 0

    let best (score: 'p -> float) (tie: 'p -> struct (int * int)) (plans: 'p list) : 'p voption =
        // A NaN score orders below every real score. Comparing with `>` alone would let a NaN win by
        // accident on the first plan and then never be displaced, since every comparison against NaN is
        // false — so score NaN as -infinity rather than special-casing the fold.
        let scoreOf p =
            let s = score p
            if Double.IsNaN s then Double.NegativeInfinity else s

        // Carry the incumbent's score (and, once needed, its tie key) rather than recomputing them on every
        // iteration: `score` is the caller's plan evaluator, and the worked example enumerates
        // positions × abilities × target tiles. Scoring each plan once is the difference between n and 2n
        // evaluations of it.
        plans
        |> List.fold
            (fun acc plan ->
                let sNew = scoreOf plan

                match acc with
                | ValueNone -> ValueSome(plan, sNew, tie plan)
                | ValueSome(_, sOld, _) when sNew > sOld -> ValueSome(plan, sNew, tie plan)
                | ValueSome(_, sOld, _) when sNew < sOld -> acc
                | ValueSome(_, _, tOld) ->
                    // Equal scores: the index tail is what makes the winner reproducible.
                    let struct (aNew, bNew) = tie plan
                    let struct (aOld, bOld) = tOld

                    if aNew < aOld || (aNew = aOld && bNew < bOld) then
                        ValueSome(plan, sNew, struct (aNew, bNew))
                    else
                        // Fully tied on (score, tie): keep the incumbent, i.e. earliest in `plans`.
                        acc)
            ValueNone
        |> ValueOption.map (fun (plan, _, _) -> plan)

    // ---------------------------------------------------------------------------------------------
    // Influence map (roadmap 3.2, work item 025). A thin wrapper over `Pathfinding.distanceField`: an
    // integer linear-falloff influence map from strengthed sources, combined by max. No new engine.
    let influenceMap
        (neighbourhood: Neighbourhood)
        (maxVisited: int)
        (cost: Cell -> int)
        (sources: (Cell * int) list)
        : Map<Cell, int> =
        sources
        |> List.fold
            (fun acc (s, strength) ->
                // One distance field per source; the contribution falls off 1 per baseStep of distance.
                let field = Pathfinding.distanceField neighbourhood maxVisited cost [ s ]

                field
                |> Map.fold
                    (fun m cell dist ->
                        let infl = strength - dist

                        if infl > 0 then
                            match Map.tryFind cell m with
                            | Some existing when existing >= infl -> m
                            | _ -> Map.add cell infl m
                        else
                            m)
                    acc)
            Map.empty

[<RequireQualifiedAccess>]
module Difficulty =

    let easy =
        { ReactionTicks = 30
          AimErrorSigma = 0.12
          SpotCycleTicks = 30
          UsesWeakPointTargeting = false
          ThreatWeight = 0.25 }

    let normal =
        { ReactionTicks = 12
          AimErrorSigma = 0.05
          SpotCycleTicks = 15
          UsesWeakPointTargeting = false
          ThreatWeight = 1.0 }

    let hard =
        { ReactionTicks = 3
          AimErrorSigma = 0.01
          SpotCycleTicks = 5
          UsesWeakPointTargeting = true
          ThreatWeight = 2.0 }

    let clamp (difficulty: Difficulty) =
        let nonNegative v =
            if Double.IsFinite v && v > 0.0 then v else 0.0

        { difficulty with
            ReactionTicks = max 0 difficulty.ReactionTicks
            AimErrorSigma = nonNegative difficulty.AimErrorSigma
            SpotCycleTicks = max 0 difficulty.SpotCycleTicks
            ThreatWeight = nonNegative difficulty.ThreatWeight }

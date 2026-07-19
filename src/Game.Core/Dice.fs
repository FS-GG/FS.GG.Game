namespace FS.GG.Game.Core

type Distribution = private { Weights: Map<int, int64> }

[<RequireQualifiedAccess>]
module Dice =

    // Drop any non-positive weights so the invariant "every weight > 0" holds. Weights are int64 so a
    // convolution of many dice (e.g. 6^n) does not overflow the way `int` would — the practical bound
    // is ~24 six-sided dice, far beyond any combat roll.
    let private normalize (m: Map<int, int64>) : Distribution =
        { Weights = m |> Map.filter (fun _ w -> w > 0L) }

    /// The outcome -> weight pairs, in ascending outcome order.
    let outcomes (d: Distribution) : (int * int64) list = d.Weights |> Map.toList

    /// The total weight (sum of all weights).
    let totalWeight (d: Distribution) : int64 =
        d.Weights |> Map.fold (fun acc _ w -> acc + w) 0L

    let constant (v: int) : Distribution = { Weights = Map.ofList [ v, 1L ] }

    let uniform (lo: int) (hi: int) : Distribution =
        if hi < lo then
            { Weights = Map.empty }
        else
            { Weights = [ for v in lo..hi -> (v, 1L) ] |> Map.ofList }

    let die (sides: int) : Distribution = uniform 1 sides

    // Combine two distributions by a binary outcome op, accumulating the product of weights at each
    // resulting outcome — the joint distribution of two INDEPENDENT rolls under `op`.
    let private combineBy (op: int -> int -> int) (a: Distribution) (b: Distribution) : Distribution =
        let mutable acc = Map.empty

        for KeyValue (va, wa) in a.Weights do
            for KeyValue (vb, wb) in b.Weights do
                let v = op va vb
                let w = wa * wb
                acc <- Map.change v (function
                    | Some x -> Some(x + w)
                    | None -> Some w) acc

        normalize acc

    let convolve (a: Distribution) (b: Distribution) : Distribution = combineBy (+) a b
    let advantage (a: Distribution) (b: Distribution) : Distribution = combineBy max a b
    let disadvantage (a: Distribution) (b: Distribution) : Distribution = combineBy min a b

    let repeat (n: int) (d: Distribution) : Distribution =
        if n <= 0 then
            constant 0
        else
            let mutable acc = d

            for _ in 2..n do
                acc <- convolve acc d

            acc

    let mean (d: Distribution) : float =
        let total = totalWeight d

        if total = 0L then
            0.0
        else
            let s = d.Weights |> Map.fold (fun acc v w -> acc + int64 v * w) 0L
            float s / float total

    let variance (d: Distribution) : float =
        let total = totalWeight d

        if total = 0L then
            0.0
        else
            let m = mean d

            let s =
                d.Weights
                |> Map.fold (fun acc v w -> acc + (float v - m) * (float v - m) * float w) 0.0

            s / float total

    /// Draw an outcome with probability proportional to its weight, threading `Rng`. Deterministic
    /// given the seed. An empty distribution returns `(0, rng)` — a documented total fallback.
    let sample (d: Distribution) (rng: Rng) : struct (int * Rng) =
        let total = totalWeight d

        if total = 0L then
            struct (0, rng)
        else
            // `nextFloat` (not `nextInt`) so a total beyond Int32 is still selected uniformly; IEEE and
            // deterministic. Clamp the astronomically-rare f≈1 case into range.
            let struct (f, rng') = Rng.nextFloat rng
            let k = min (total - 1L) (int64 (f * float total))
            // Walk ascending-key outcomes accumulating weight until `k` falls in a bucket.
            let mutable acc = 0L
            let mutable chosen = 0
            let mutable found = false

            for KeyValue (v, w) in d.Weights do
                if not found then
                    acc <- acc + w

                    if k < acc then
                        chosen <- v
                        found <- true

            struct (chosen, rng')

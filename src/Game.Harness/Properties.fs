namespace FS.GG.Game.Harness

open FS.GG.Game.Core

type Counterexample =
    { Seed: uint64
      Script: Command list list
      Step: int }

[<RequireQualifiedAccess>]
type PropertyResult =
    | Held of runs: int
    | Falsified of Counterexample

type PropertyConfig =
    { Runs: int
      MaxLength: int
      Moves: Command list list option }

[<RequireQualifiedAccess>]
module Properties =

    let defaultConfig: PropertyConfig = { Runs = 1000; MaxLength = 32; Moves = None }

    // The first post-step frame index at which the invariant is false, or None if it holds throughout.
    let private firstViolation
        (playable: Playable<'world, 'key>)
        (invariant: 'world -> bool)
        (script: Command list list)
        : int option =
        let frames = Driver.runCommands playable Driver.identityFingerprint script |> Trace.frames

        frames
        |> List.mapi (fun i f -> i, f)
        |> List.tryPick (fun (i, f) -> if invariant f then None else Some i)

    // Shrink a failing script to a minimal counter-script: truncate to the first-violation prefix, then
    // greedily drop frames left-to-right, keeping a removal only if the reduced script still violates.
    let private shrink
        (playable: Playable<'world, 'key>)
        (invariant: 'world -> bool)
        (script: Command list list)
        : Command list list =
        let stillViolates s = (firstViolation playable invariant s).IsSome

        let truncated =
            match firstViolation playable invariant script with
            | Some k -> List.truncate (k + 1) script
            | None -> script

        let rec removeLoop (s: Command list list) =
            let rec tryFrom i =
                if i >= List.length s then
                    s // no single removal still violates — minimal
                else
                    let candidate = (List.take i s) @ (List.skip (i + 1) s)

                    if stillViolates candidate then
                        removeLoop candidate
                    else
                        tryFrom (i + 1)

            tryFrom 0

        removeLoop truncated

    // Generate one random script: a random length in [0, maxLength], then that many random moves.
    let private genScript (moves: Command list list) (maxLength: int) (rng: Rng) : Command list list * Rng =
        let struct (len, rng1) = Rng.nextInt 0 (max 0 maxLength) rng
        let mutable r = rng1
        let frames = ResizeArray<Command list>(len)

        for _ in 1..len do
            let struct (idx, r') = Rng.nextInt 0 (List.length moves - 1) r
            r <- r'
            frames.Add(List.item idx moves)

        List.ofSeq frames, r

    let checkWith
        (config: PropertyConfig)
        (playable: Playable<'world, 'key>)
        (invariant: 'world -> bool)
        (seed: uint64)
        : PropertyResult =
        // Init is checked before any input: a violation there needs no script.
        if not (invariant playable.Init) then
            PropertyResult.Falsified { Seed = seed; Script = []; Step = -1 }
        else
            let moves = config.Moves |> Option.defaultValue (Explore.defaultMoves playable)
            let mutable rng = Rng.ofSeed seed
            let mutable result: PropertyResult option = None
            let mutable run = 0

            while result.IsNone && run < config.Runs do
                let script, rng' = genScript moves config.MaxLength rng
                rng <- rng'

                match firstViolation playable invariant script with
                | Some _ ->
                    let minimal = shrink playable invariant script
                    let step = (firstViolation playable invariant minimal) |> Option.defaultValue 0
                    result <- Some(PropertyResult.Falsified { Seed = seed; Script = minimal; Step = step })
                | None -> ()

                run <- run + 1

            match result with
            | Some r -> r
            | None -> PropertyResult.Held config.Runs

    let check (playable: Playable<'world, 'key>) (invariant: 'world -> bool) (seed: uint64) : PropertyResult =
        checkWith defaultConfig playable invariant seed

namespace FS.GG.Game.Core

type Source =
    | Declared
    | Collision
    | Environmental
    | Periodic

type Damage<'K> =
    { Kind: 'K
      Source: Source
      Base: float }

type StageResult =
    | Continue of float
    | Halt of float

type Stage<'T, 'K> =
    { Name: string
      Run: 'T -> Damage<'K> -> float -> StageResult }

type DamageTrace =
    { Final: float
      Halted: string voption
      Steps: (string * float) list }

[<RequireQualifiedAccess>]
module Effects =

    let inline private isFinite (v: float) = System.Double.IsFinite v

    // Totality, in one place. Every amount that enters a `DamageTrace` passes through here, so a
    // non-finite `Base`, a hostile `falloff` multiplier, and a buggy `Stage` all degrade to 0.0 rather
    // than poisoning a damage total the way `Ballistics.splash` refuses to poison a position.
    let inline private sane (v: float) = if isFinite v then v else 0.0

    // The fold behind both `pipeline` and `applyAll`, differing only in the seed. `Steps` is built
    // reversed and flipped once at the end: the alternative is an O(n²) append per stage.
    let private run (stages: Stage<'T, 'K> list) (target: 'T) (damage: Damage<'K>) (seed: float) : DamageTrace =
        let rec walk remaining amount steps =
            match remaining with
            | [] ->
                { Final = amount
                  Halted = ValueNone
                  Steps = List.rev steps }
            | (stage: Stage<'T, 'K>) :: rest ->
                match stage.Run target damage amount with
                | Continue next ->
                    let next = sane next
                    walk rest next ((stage.Name, next) :: steps)
                | Halt final ->
                    let final = sane final

                    { Final = final
                      Halted = ValueSome stage.Name
                      Steps = List.rev ((stage.Name, final) :: steps) }

        walk stages (sane seed) []

    let pipeline (stages: Stage<'T, 'K> list) (target: 'T) (damage: Damage<'K>) : DamageTrace =
        run stages target damage damage.Base

    // The transport multiplier seeds the pipeline; it is not a stage. See the .fsi — a stage could be
    // reordered after `subtract`, and then falloff would shield the target's armor from the blast.
    let applyAll
        (stages: Stage<'T, 'K> list)
        (damage: Damage<'K>)
        (targets: ('T * float) list)
        : ('T * DamageTrace) list =
        targets
        |> List.map (fun (target, multiplier) -> target, run stages target damage (damage.Base * sane multiplier))

    let amplify (bonusOf: 'T -> float) : Stage<'T, 'K> =
        { Name = "amplify"
          Run = fun target _ amount -> Continue(amount * (1.0 + bonusOf target)) }

    // `>= 1.0` rather than `= 1.0`: at exactly 1.0 a multiply would produce the zero a later `floorAt`
    // silently lifts (the bug this module exists to prevent), and above 1.0 it would multiply by a
    // negative and heal the target. Both are a halt at zero. A NaN resistance fails the test and flows
    // into the multiply, where `sane` catches it.
    let resist (resistOf: 'T -> 'K -> float) : Stage<'T, 'K> =
        { Name = "resist"
          Run =
            fun target damage amount ->
                let r = resistOf target damage.Kind
                if r >= 1.0 then Halt 0.0 else Continue(amount * (1.0 - r)) }

    let subtract (amountOf: 'T -> 'K -> float) : Stage<'T, 'K> =
        { Name = "subtract"
          Run = fun target damage amount -> Continue(amount - amountOf target damage.Kind) }

    let floorAt (minimum: float) : Stage<'T, 'K> =
        // A non-finite floor degrades to 0.0 rather than to a `max` against NaN, which returns its
        // other operand on some paths and NaN on others depending on argument order.
        let minimum = sane minimum

        { Name = "floorAt"
          Run = fun _ _ amount -> Continue(max minimum amount) }

    let immuneWhen (predicate: 'T -> Damage<'K> -> bool) : Stage<'T, 'K> =
        { Name = "immuneWhen"
          Run = fun target damage amount -> if predicate target damage then Halt 0.0 else Continue amount }

    // Keeps `inner`'s name whether or not it fired, so a trace of a skipped stage still reads as the
    // rule it encodes ("cover", not "gatedBy") and the `Steps` list has one entry per stage regardless
    // of the hit's source — a trace is comparable across sources.
    let gatedBy (sources: Source list) (inner: Stage<'T, 'K>) : Stage<'T, 'K> =
        { Name = inner.Name
          Run =
            fun target damage amount ->
                if List.contains damage.Source sources then
                    inner.Run target damage amount
                else
                    Continue amount }

    type Active<'E> =
        { Effect: 'E
          TicksRemaining: int }

    type Policy<'E> =
        | Refresh
        | Strongest of magnitude: ('E -> float)
        | Stacks of cap: int
        | Once

    let applyEffect (policy: Policy<'E>) (incoming: Active<'E>) (actives: Active<'E> list) : Active<'E> list =
        match policy, actives with
        | Refresh, [] -> [ incoming ]
        | Refresh, existing :: _ ->
            // The incoming effect's data wins; only the duration is reconciled, and it may extend but
            // never cut short — reapplying a stun must not shorten the one already running.
            [ { incoming with TicksRemaining = max existing.TicksRemaining incoming.TicksRemaining } ]

        | Strongest _, [] -> [ incoming ]
        | Strongest magnitude, existing :: _ ->
            // `>=`, not `>`: an equal-strength reapplication refreshes. Replacement takes the incoming
            // duration wholesale, which is what "refreshes" means for a fixed-duration effect.
            if magnitude incoming.Effect >= magnitude existing.Effect then
                [ incoming ]
            else
                [ existing ]

        // Appended, so instances stay in application order. At the cap this is a no-op — it does not
        // refresh the newest stack nor evict the oldest, and the choice is documented in the design
        // because the source spec never states one. `cap <= 0` therefore admits nothing.
        | Stacks cap, _ -> if List.length actives < cap then actives @ [ incoming ] else actives

        | Once, [] -> [ incoming ]
        | Once, _ -> actives

    let tickEffects (actives: Active<'E> list) : Active<'E> list =
        // An `int` decrement, so an effect expires on the same step in a 30 fps and a 144 fps replay.
        // `<= 0` rather than `= 0` also collects an instance applied with a non-positive duration.
        actives
        |> List.choose (fun active ->
            let remaining = active.TicksRemaining - 1

            if remaining <= 0 then
                None
            else
                Some { active with TicksRemaining = remaining })

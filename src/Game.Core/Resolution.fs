namespace FS.GG.Game.Core

[<RequireQualifiedAccess>]
module Resolution =

    // Push-out along the MTV: translate the body by -Normal*Depth so it no longer overlaps (DEC-003).
    // Exact (no slop — a caller concern). Pure arithmetic: a NaN operand flows through, never throws.
    let pushOut (position: Point) (contact: Contact) : Point =
        { X = position.X - contact.Normal.X * contact.Depth
          Y = position.Y - contact.Normal.Y * contact.Depth }

    // Kinematic slide: v - (v·n)*n, killing the normal component and keeping the tangential (DEC-002).
    // `normal` is assumed unit (the Contact.Normal contract, DEC-001), so no internal normalisation.
    // Pure arithmetic (NaN-safe).
    let slide (velocity: Point) (normal: Point) : Point =
        let dot = velocity.X * normal.X + velocity.Y * normal.Y
        { X = velocity.X - dot * normal.X
          Y = velocity.Y - dot * normal.Y }

    type CellStep =
        | Enter
        | Stop
        | Block

    type PushStop =
        | Completed
        | Stopped of Cell
        | Blocked of Cell

    type Push =
        { Entered: Cell list
          Final: Cell
          Outcome: PushStop }

    // Discrete grid displacement: walk from `start` by `step` up to `distance` cells, asking `classify`
    // what each next cell does (DEC-004, re-expressed). `start` is assumed occupied and is never
    // classified. `Entered` is accumulated reversed and flipped once at the end. Tail-recursive
    // fixed-order walk ⇒ deterministic and total for any total `classify`.
    let push (start: Cell) (step: Cell) (distance: int) (classify: Cell -> CellStep) : Push =
        let rec walk (current: Cell) (remaining: int) (entered: Cell list) =
            if remaining <= 0 then
                { Entered = List.rev entered
                  Final = current
                  Outcome = Completed }
            else
                let next =
                    { Col = current.Col + step.Col
                      Row = current.Row + step.Row }

                match classify next with
                | Enter -> walk next (remaining - 1) (next :: entered)
                | Stop ->
                    // Entered, and stopped there. `next` is both occupied and the reason.
                    { Entered = List.rev (next :: entered)
                      Final = next
                      Outcome = Stopped next }
                | Block ->
                    // Never entered. The unit keeps `current`; `next` is the obstacle to attribute
                    // collision damage to.
                    { Entered = List.rev entered
                      Final = current
                      Outcome = Blocked next }

        walk start distance []

    // Exactly the old walk: `blocked` maps onto the two relations it could express, and `Final` is the
    // cell it returned. `Stop` is unreachable through this predicate, so no input distinguishes them.
    [<System.Obsolete("Use Resolution.push, which reports why the walk stopped and which cells were entered. See docs/reports/2026-07-10-effects-damage-pipeline-design.md §5.")>]
    let knockback (start: Cell) (step: Cell) (distance: int) (blocked: Cell -> bool) : Cell =
        (push start step distance (fun c -> if blocked c then Block else Enter)).Final

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

    // Discrete grid knockback: walk from `start` by `step` up to `distance` cells, stopping in the last
    // free cell before the first blocked cell (DEC-004). `start` is assumed free; only each next cell is
    // tested. `distance <= 0` returns `start`. Tail-recursive fixed-order walk ⇒ deterministic and total.
    let knockback (start: Cell) (step: Cell) (distance: int) (blocked: Cell -> bool) : Cell =
        let rec walk (current: Cell) (remaining: int) =
            if remaining <= 0 then
                current
            else
                let next = { Col = current.Col + step.Col; Row = current.Row + step.Row }
                if blocked next then current else walk next (remaining - 1)
        walk start distance

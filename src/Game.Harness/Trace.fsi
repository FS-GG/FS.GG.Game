namespace FS.GG.Game.Harness

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// The provenance of a harness trace: whether its frames came from driving the real input frontier
/// (`InputDriven`) or from a hand-built world handed to the synthetic escape hatch (`Synthetic`).
/// This bit is the whole reason the synthetic hatch is a *distinct, typed* surface: an obligation
/// gate reads it, and under the SDD satisfaction rule and Governance's non-relaxable synthetic gate
/// a `Synthetic` trace can never satisfy an FR — only `InputDriven` evidence is non-synthetic.
///
/// **Qualified.** `Synthetic` is an ordinary word a consumer may well have its own of, so
/// `open FS.GG.Game.Harness` must not inject it. Write `Origin.Synthetic`.
[<RequireQualifiedAccess>]
type Origin =
    /// The frames were produced by folding real `Command` inputs (script or bot) through the game's
    /// own step function — the non-synthetic route.
    | InputDriven
    /// The frames were produced from caller-supplied, hand-built worlds via the synthetic hatch.
    | Synthetic

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// A recorded sequence of per-step world fingerprints, tagged with its `Origin`. **Opaque**: it has
/// no public constructor, so an `Origin` can never be forged — an `InputDriven` trace comes only from
/// the driver and a `Synthetic` one only from `Synthetic.trace`. Two runs of the same game over the
/// same script produce equal frames, which is what makes a replay a value comparison (compare
/// `Trace.frames`) rather than a tolerance check.
[<Sealed>]
type Trace<'f>

/// Public contract module exposed by the FS.GG.Game.Harness package.
[<RequireQualifiedAccess>]
module Trace =

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// The fingerprint recorded after each fixed step, in step order. This is the value a caller
    /// asserts on: two byte-identical runs yield equal frame lists.
    val frames: trace: Trace<'f> -> 'f list

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// The provenance of the trace — `Origin.InputDriven` or `Origin.Synthetic`.
    val origin: trace: Trace<'f> -> Origin

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// `true` exactly when the trace came from the synthetic hatch (`Origin.Synthetic`). The bit an
    /// evidence gate reads to refuse a synthetic stand-in.
    val isSynthetic: trace: Trace<'f> -> bool

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// Whether two traces have identical frame sequences (provenance is ignored). The determinism
    /// assertion in value form.
    val equalFrames: a: Trace<'f> -> b: Trace<'f> -> bool when 'f: equality

    /// Construct a trace with an explicit provenance. **Internal**: only the driver (which stamps
    /// `Origin.InputDriven`) and the synthetic hatch (`Origin.Synthetic`) may call it, so the
    /// provenance bit stays unforgeable from outside the package.
    val internal create: origin: Origin -> frames: 'f list -> Trace<'f>

namespace FS.GG.Game.Harness

/// Public contract module exposed by the FS.GG.Game.Harness package.
/// The **labeled fallback**. When driving from real input is too expensive, a test may start from
/// hand-built worlds — but the cost is made visible: this is the only surface that yields a
/// `Origin.Synthetic` trace, and there is no way to reach `Origin.InputDriven` from here. So any
/// trace or evidence derived from a hand-built world is self-identifying (`Trace.isSynthetic`), and
/// under the SDD/Governance synthetic rule it can never satisfy a gameplay obligation.
[<RequireQualifiedAccess>]
module Synthetic =

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// Build a trace from a sequence of hand-built worlds by fingerprinting each. The result is
    /// tagged `Origin.Synthetic` and is distinct in the API from any input-driven run.
    val trace: fingerprint: ('world -> 'f) -> worlds: 'world list -> Trace<'f>

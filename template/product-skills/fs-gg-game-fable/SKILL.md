---
name: fs-gg-game-fable
description: Consume the published FS.GG.Game.Core Fable lockstep profile honestly, with package-only dependencies and canonical cross-runtime evidence.
---

# Game.Core Fable Lockstep Capability

## Scope

Use this skill when a generated game product must consume the published
`FS.GG.Game.Core` Fable compatibility profile across .NET and JavaScript. The
profile identity is `fs-gg-game-core-fable-lockstep-v1`; it is a bounded,
versioned package contract, not an assembly-wide Fable or determinism claim.

Consume the package through the product's package-management mechanism. Pin the
exact package version together with the profile, fixture-schema, .NET SDK,
FSharp.Core, Fable compiler, Node, and browser identities carried by the
package's `fable-compatibility/toolchain-manifest.v1.json`. Restore the package
into a clean consumer directory and compile its supported `fable/` source view.
Never add a sibling project reference, copy an algorithm from Game.Core, or
infer compatibility from a source checkout.

## Classification is the authority boundary

Read `fable-compatibility/compatibility-profile.v1.json` from the exact restored
package before placing a surface in shared logic. Classifications are per
surface, not per module or assembly:

| Grade | Meaning | Permitted use |
| --- | --- | --- |
| `LockstepExact` | The canonical inputs produce the same canonical bytes under the pinned .NET and Fable runtimes. | May enter cross-runtime authoritative logic, behind your own semantic adapter. |
| `Portable` | The surface compiles on both targets, but promises semantics or tolerance only. | Presentation, local analysis, and other non-authoritative work. |
| `DotNetOnly` | The surface is absent from the supported Fable package view. | .NET-only code; never shared authority. |
| `Unclassified` | No explicit profile row establishes the required promise. | Treat as unavailable for authoritative shared logic until profiled. |

For profile v1, `Cell` ordering, `Edges.edgeBetween`,
`Los.lineOfSightBy`, and `Pathfinding.astar` are the only `LockstepExact`
surfaces. Floating value types are `Portable`; the profile names its
`DotNetOnly` modules explicitly. A source file being present does not promote
an operation: an operation not listed by the profile is `Unclassified`.

Do not make sequential RNG, floating-point geometry, a whole module, or the
whole assembly authoritative merely because it compiles. Keep product meaning
at the boundary: adapt the package values into the product's own movement,
semantic-edge, footprint, and addressed-random representations, and test those
adapters separately. The profile proves the named package operations, not a
product's serialization, hash, protocol, or replay design.

## Canonical evidence and replays

The profile's fixture schema defines canonical little-endian bytes and the
`expected.bin` oracle. For every claimed exact boundary, retain evidence that
the package-derived .NET consumer and package-derived Fable consumer emit the
complete same byte stream as that oracle. Record the exact package version,
package SHA-256, profile id, fixture-schema version, toolchain identities, and
the first-byte divergence diagnostics if a comparison fails.

Replay records must bind the profile identity and package identity. A changed
`LockstepExact` output is a new compatibility identity even if a public .NET
signature did not change; an existing replay must continue using the profile it
was created with rather than silently reinterpreting its bytes.

Run clean-consumer evidence outside the Game checkout: use an isolated package
source, empty package cache, and only the packed artifact. A local project
reference or a compiler resolving sibling sources proves neither package
contents nor consumer reproducibility.

## Browser qualification is a separate runtime proof

Node evidence is not browser evidence. The package consumer's browser
qualification compiles the restored Fable source, loads that JavaScript as an
ES module in a headless browser, and compares its emitted canonical bytes with
the same oracle. Run it for the pinned browser identity before asserting
browser lockstep support; record that browser version alongside the Node result.

This qualification covers the executable browser consumer of the profiled
fixture boundary. It does not promise player WASM execution, certify every
browser engine, or generalize a Node pass to browser behavior.

## What to change when the profile changes

Changing a classified exact surface requires a deliberate profile and fixture
revision, regenerated canonical evidence, package publication, and downstream
re-pinning. Do not broaden a grade in product code. First publish the producer
artifact, verify it from each required feed with byte-identical payload evidence
(apart from documented feed signatures), then update consumers to the exact
published identity.

See the package's `fable-compatibility/` metadata for the executable contract;
the producer's compatibility report records the bounded source view and why
unsupported float-heavy code remains outside it.

# FS.GG.Game.Physics.Box2D

An opt-in .NET 10 adapter for [Box2D.NET 3.1.654](https://www.nuget.org/packages/Box2D.NET/3.1.654).
Own a `Runtime`, submit an ordered command list once per fixed tick, and render the
copied immutable `Snapshot` values. Core retains its FSharp.Core/BCL dependency boundary;
this package has no rendering dependency.

The first contract supports one centred circle or box fixture per static, kinematic or
dynamic body, forces, impulses and rigid distance joints between body origins. Units are
metres, seconds, kilograms, and counterclockwise radians with +Y up. Public doubles are
validated and converted to the engine's single precision. Command batches with invalid,
unknown or duplicate IDs refuse before any mutation. Removing a body removes its joints;
removed shape events retain their entity IDs for that tick. An engine exception stops
further stepping; dispose that runtime. All access belongs to one simulation owner.

`Runtime.Step` always uses its configured fixed interval and substeps. Drive it with
Game.Core's `FixedStep.drain` if presentation frames vary, and use the remaining fraction
for `Physics.interpolate`. Retained observations survive later steps and disposal.
Interpolation does not capture contacts or hidden solver state and cannot implement rollback.

The repository's real headless entry point uses a falling body, floor, sensor and distance
joint, and compares 240 ticks in two fresh worlds with the same ordered recorded input.
Replay comparisons cover only the declared dependency, runtime and solver profile.
Public installed consumer qualification, product adoption, browser support, cross-platform
determinism, and exact rollback require their own qualification.

# Box2D headless scene

Run the real entry point from the repository root:

```console
dotnet run --project examples/Box2D.Headless/Box2D.Headless.fsproj -c Release
```

It creates a falling circle, floor, sensor, anchor and bob with a rigid distance joint,
then drives 240 fixed ticks using recorded create/impulse/force commands. The entry point
fails if falling motion, correct sensor enter/exit IDs, floor contact, joint length,
retained snapshots, midpoint interpolation or exact fresh-world replay fails. Successful
output is one JSON object with the observed events, final height and maximum joint error.

The pinned profile is Box2D.NET 3.1.654, .NET 10 (qualification selects 10.0.12 explicitly), +Y up, metres, 1/60 second per tick,
four substeps, default single worker, sleep and continuous collision settings. Runtime/OS
observations belong in the owning roadmap after the actual run. The replay checks do not
claim cross-platform determinism or restoration of hidden solver state.

# FS.GG.Game.Render

Pure **Scene adapter** for FS.GG.Game — projects [`FS.GG.Game.Core`](https://www.nuget.org/packages/FS.GG.Game.Core)
simulation primitives (`Point`/`Rect`/`Cell` and pathfinding routes) onto `FS.GG.UI.Scene` drawables
at the render edge (ADR-0022 §2).

This is the **one** place in FS.GG.Game that reaches up to Rendering: `Game.Core` reaches up to
nothing; this adapter reaches up to `FS.GG.UI.Scene` (FSharp.Core-only, Skia-free). Deterministic,
no I/O.

```
FS.GG.Game.Render ──▶ FS.GG.UI.Scene   (adapter reaches UP — allowed)
FS.GG.Game.Core   ──▶ (nothing)        (bottom layer)
```

House style: `.fsi` is the sole public surface; `net10.0`; `-preview` channel.
See [FS-GG/FS.GG.Game](https://github.com/FS-GG/FS.GG.Game) and [ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md).

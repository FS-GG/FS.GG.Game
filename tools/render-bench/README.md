# Rendering benchmark harness

GAME-RENDER-ADOPT-01.2 ([FS.GG.Game#746](https://github.com/FS-GG/FS.GG.Game/issues/746), under
[FS-GG/.github#5179](https://github.com/FS-GG/.github/issues/5179)). It measures what one game state costs to present
through each `Game.Render` backend (SVG, Pixi and Babylon) in Chromium. It runs on a real GPU and on a software
control.

| Part | Path |
|---|---|
| In-page runner (Fable) | `tests/Render.Browser.Bench/` (`BenchGame.fs` is the plug-in contract, `Bench.fs` the runner, `ReferenceGameBench.fs` the first game) |
| Page instruments | `tests/Render.Browser.Bench/instrument.js`, loaded before any module |
| Driver (CDP, no npm dependency) | `tools/render-bench/driver.mjs` |
| Results schema | `tools/render-bench/results.schema.v1.json` (`fsgg.render-bench/v1`) |
| Build and run | `scripts/render-bench.sh` |
| Baselines | `tools/render-bench/baselines/` |

## What is measured

The route is the production one. The runner registers the game's `view` with `SvgSurface`, `PixiSurface` and
`BabylonSurface` through `RendererRegistry`. `RendererDriver.start` mounts SVG, and `RendererMsg.SelectBackend`
switches to the backend under test. Every frame the scripted state goes through `RendererMsg.StateChanged`. Each run
measures one game, scene, size, backend and repeat, in a fresh browser process.

The page refuses the run if any of these fails:

- the selected backend is mounted and `Active`;
- the scene has exactly `size` drawable nodes;
- a canvas backend issues draw calls.

The driver refuses a GPU-mode run whose backend context reports a software renderer.

Frame pacing is uncapped (`--disable-gpu-vsync --disable-frame-rate-limit`), so the frame interval measures what a
frame costs, not the display rate. The recording window starts after the backend first draws and after the warm-up
frames.

| Field | Definition | Source |
|---|---|---|
| `frameTimeMs` p50/p95/p99 | Deltas between `requestAnimationFrame` timestamps over the recorded frames. Percentiles are nearest-rank. | page, 5 µs clock (the server sends COOP/COEP, so the page is cross-origin isolated) |
| `presentMs` | Time for the state step plus the `StateChanged` dispatch, which includes the backend's `Present`. | page |
| `rafScriptMs` | Time inside every animation-frame callback in a frame, including Babylon's render loop and Pixi's ticker. | page (`requestAnimationFrame` wrapper) |
| `mainThread.cpuMsPerFrame` | Renderer main-thread CPU time (`ThreadTime`) per frame. Task, script, layout and style time are recorded beside it. | CDP `Performance.getMetrics` |
| `processCpuMsPerFrame` | CPU time per frame of the renderer, GPU and browser processes. For SVG, the GPU process's share covers raster and compositing. | CDP `SystemInfo.getProcessInfo` |
| `gpuTimeMs` | `TIME_ELAPSED` queries around every animation-frame callback during the window, summed per frame. A frame with a disjoint query is dropped and counted. | `EXT_disjoint_timer_query_webgl2` |
| `drawCalls.perFrame` | Every WebGL `draw*` call, instanced draw and multi-draw count, per frame. | page (WebGL prototype wrapper) |
| `memory` | JS heap used at the end of the window, and again after a forced GC. DOM nodes and listeners are recorded too. | CDP `Performance.getMetrics`, `HeapProfiler.collectGarbage` |
| `coverage` | Declared nodes, the backend's own count, and the nodes it actually drew. `complete: false` means the timings cover a smaller scene. | SVG elements, Pixi stage children, Babylon `getActiveMeshes()` |

**Unknown is explicit.** A value that cannot be observed is `null`, or it has `status: "unknown"` and a reason. It is
never zero. For SVG, GPU time and draw calls are Unknown: the browser compositor rasterises SVG, and neither is
visible to the page. The GPU-process CPU time is the nearest observable proxy. WebGPU timestamps are not used,
because no backend renders through WebGPU (Pixi is forced to WebGL). The probe records whether a WebGPU adapter with
`timestamp-query` is available.

Variance: each configuration is repeated (default 3). Repeat is the outer loop, and every other repeat runs the cells
in reverse order. Aggregates give the median, mean, sample standard deviation and coefficient of variation of each
statistic across repeats. Raw per-frame samples are kept in each run.

## Modes

The mode names are the profile names of the fdev functional-GPU profile in FS.GG.Rendering
(`docs/reports/svg-fdev-functional-gpu-baseline-20261030.md`).

| Mode | Flags added to the common set | Expected renderer |
|---|---|---|
| `gpu-angle-vulkan` | `--use-angle=vulkan --enable-features=Vulkan --ignore-gpu-blocklist --enable-unsafe-webgpu` | hardware |
| `gpu-angle-gl-egl` | `--use-gl=angle --use-angle=gl-egl --ignore-gpu-blocklist --enable-unsafe-webgpu` | hardware |
| `software` | `--disable-gpu --enable-unsafe-swiftshader --use-angle=swiftshader` (the CI browser smoke's flags) | SwiftShader |
| `custom` | only `--flags "..."` | any |

Common flags: `--headless=new --no-sandbox --window-size=1280,800 --disable-gpu-vsync --disable-frame-rate-limit`, no
background throttling, no extensions. Every results file records the full flag list, the `chrome://gpu` feature
status (CDP `SystemInfo.getInfo`), the GPU device and driver strings, and the WebGL `UNMASKED_RENDERER` strings of a
probe context and of each backend's own context. `driver.mjs probe --browser <chrome> --mode <mode>` prints the same
fingerprint without running a benchmark. It exits non-zero when the mode does not get the renderer it expects.

## Running it on fdev

**Supported route.** Run every fdev Chromium lane, this benchmark and other games' lanes included, through
`~/.local/bin/fdev-chromium-env` ([FS-GG/.github#5193](https://github.com/FS-GG/.github/issues/5193)). The wrapper
puts the fdev Chromium runtime bundle on `LD_LIBRARY_PATH` and `FONTCONFIG_FILE`. The bundle holds the `nss`, `nspr`,
`at-spi2-core`, `libcups` and `libX*` libraries plus a font set, which fdev's system lacks. The libraries come from
signature-verified Arch packages that were downloaded only, never installed; `manifest.json` in the bundle records
them. The wrapper also exports the mode's Chromium flags as `FDEV_CHROMIUM_ARGS` and `FDEV_CHROMIUM_ARGS_JSON`. A
Playwright launcher passes them as `args: JSON.parse(process.env.FDEV_CHROMIUM_ARGS_JSON)`.

| Wrapper mode | Wrapper flags | Matching driver `--mode` |
|---|---|---|
| `--gpu` | `--use-angle=vulkan --enable-features=Vulkan --ignore-gpu-blocklist` | `gpu-angle-vulkan`, which adds `--enable-unsafe-webgpu` |
| `--no-gpu` | `--disable-gpu --enable-unsafe-swiftshader --use-angle=swiftshader` | `software` |

The driver launches Chromium itself and adds its mode flags. It therefore needs only the wrapper's environment. Choose
the wrapper mode that matches `--mode`:

```sh
fdev-chromium-env --gpu --check     # every Playwright Chromium resolves its libraries; prints the font
export RENDER_BENCH_BROWSER=$HOME/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome
fdev-chromium-env --gpu -- node tools/render-bench/driver.mjs probe --browser "$RENDER_BENCH_BROWSER" --mode gpu-angle-vulkan
fdev-chromium-env --gpu -- scripts/render-bench.sh -- --mode gpu-angle-vulkan --out gpu.json \
  --capacity-probe ../.github/scripts/fdev-lane-capacity.py "--capacity-args=--max-load-per-core 0.25"
fdev-chromium-env --no-gpu -- scripts/render-bench.sh --skip-build -- --mode software --out software.json \
  --capacity-probe ../.github/scripts/fdev-lane-capacity.py "--capacity-args=--max-load-per-core 0.25"
node tools/render-bench/driver.mjs table gpu.json software.json
```

The committed 2026-11-01 baselines predate the wrapper. They ran the same Chrome for Testing 153.0.8010.12 with an
equivalent private library directory on `LD_LIBRARY_PATH`. Each results file records its `ldLibraryPath`. The .NET
SDK is the one `global.json` names. Off fdev (CI or a workstation with a system Chromium), no wrapper is needed: set
`RENDER_BENCH_BROWSER`, or let `scripts/render-bench.sh` find `chromium` or `google-chrome`.

Driver options:

| Option | Default |
|---|---|
| `--games` | `reference-game` |
| `--scenes` | `room-tiles,room-swarm` |
| `--sizes` | `100,1000,5000` |
| `--backends` | `svg,pixi,babylon` |
| `--repeats` | 3 |
| `--frames` | 300 |
| `--warmup` | 60 |
| `--run-timeout` | 180 s |
| `--no-samples` | off (raw samples are kept) |

**Capacity.** Before every run, the driver runs the fdev lane-capacity probe (`--capacity-probe`). While the probe
says `hold`, the driver waits up to `--capacity-wait` seconds (default 600), then stops and writes what has run. Each
run records the probe's verdict JSON and `/proc/loadavg` before and after. The baseline used
`--max-load-per-core 0.25`, which holds while the 5-minute load is 3.0 or more on 12 cores. The baseline's probe
bytes match `.github` `scripts/fdev-lane-capacity.py` as merged in .github#5173 (sha256 `307f7e3a…5e835`). The
probe cannot tell its own lane's load from other lanes' load. A software run loads several cores through
SwiftShader, so a software session often waits between its own runs. The load average does not show GPU contention.
Each run therefore also records how many other Chromium GPU processes run beside it (`foreignGpuProcessesBefore`
and `foreignGpuProcessesAfter`).

## Caveats

- **This is not B1–B8 evidence and not bare-metal Home.** fdev is a KVM guest. Its `virtio-gpu-gl` device has DRM
  native context onto the Home host's **integrated** AMD GPU (RADV / radeonsi, Mesa 26.2.4), which other host work
  shares. There is no display: runs are headless with no scanout. The numbers describe this host profile only.
- GPU time covers the backend's own WebGL commands. It does not include the compositor's copy of the canvas into the
  page, or presentation.
- Under `software`, "GPU time" is SwiftShader's CPU execution time. It is comparable only within that mode.
- fdev is shared. The capacity gate and the recorded load average make load visible; they do not remove it. Compare
  runs with their recorded load.
- Uncapped pacing measures throughput. It does not show what a player sees at 60 Hz. A frame time under 16.7 ms means
  headroom, not a higher refresh rate.
- The Babylon adapter currently draws only part of large scenes ([#753](https://github.com/FS-GG/FS.GG.Game/issues/753)).
  Its rows above about 400 nodes are marked `(partial)` and are not comparable with SVG and Pixi. Babylon also renders
  twice per frame (a synchronous render in `Present`, then its render loop), so its draw calls are twice its nodes.
- Pixi batches the whole scene into about two draw calls, so a low draw-call count is expected for Pixi.

## Adding a game (FourD, SC2, BAR, ...)

A game plugs in its own state type. It supplies a `BenchGame<'State>` (`BenchGame.fs`) with these members:

- `Init`;
- a deterministic `Step: frame -> state -> state`;
- the canvas `Width` and `Height`;
- one or more `BenchScene`s, each with a `View: size -> state -> Scene` that returns exactly `size` drawable nodes.

`BenchScene.drawableNodes` counts the drawable nodes.

1. Compile the game's state core into the page. The core is the model type plus the functions `Step` uses, and
   Fable must be able to compile it. ReferenceGame does this by linking `src/ReferenceGame/Program.fs` and excluding
   its .NET-only Harness parts under `#if !FABLE_COMPILER`. A game whose state core is not Fable-portable needs a
   portable projection of its state first. That is adoption work under #5179, not a benchmark-only copy.
2. Add one line to `games` in `tests/Render.Browser.Bench/Program.fs`, for example `FourDBench.game.Id, Runner.run FourDBench.game`.
3. Run with `--games <id> --scenes <scene ids>`.

Today the plug-in lives in this repository, because `FS.GG.Game.Render.Browser` is not published. The driver itself
is page-agnostic. A game repository can drive its own page if that page meets these conditions:

- it loads `instrument.js` before its modules;
- it reads `?game&scene&size&backend&warmup`;
- in its frame loop it calls `__fsggBench.frameStart(timestamp)` and `__fsggBench.frameWork(ms)`;
- it sets `__fsggBench.phase` through `mounting`, `warmup` and `ready`, or to `error` with `__fsggBench.error`;
- it fills `__fsggBench.info.backendNodes`, plus `babylonActiveMeshes` for Babylon;
- it stops when `__fsggBench.stop` is true.

Cross-repository use without copying waits for publication of the browser host. The authoring kit Delivery
(Game#745) owns that.

## Baseline

The committed baselines are for ReferenceGame on fdev, measured on 2026-11-01: one session per mode. The GPU control is
the software mode. Medians are taken across the 3 repeats of each run's own statistic. CV is the coefficient of
variation of the per-run p50 across repeats. "(partial)" marks a backend that drew fewer nodes than the scene holds
(#753). The full per-run records and raw per-frame samples are in the JSON files.

**Reading it.**

- **The GPU path is real.** WebGL backends on `gpu-angle-vulkan` get the RADV renderer and measured GPU time. The same
  scenes on SwiftShader take many times longer (see `software`). Example: Pixi at 100 nodes takes about 0.8 ms per
  frame on the GPU and about 19 ms on SwiftShader.
- **SVG cost** is main-thread work: the frame time tracks main-thread CPU at every size, about 117 ms per frame at
  5,000 nodes. It is the same in both modes. Under `software`, only SVG's raster and compositing move to the CPU.
- **Pixi** batches the whole scene into 2 draw calls and stays under 6 ms at 5,000 nodes.
- **Babylon** issues one draw per drawn mesh, twice per frame. It draws only part of scenes above about 400 nodes
  (#753). At 5,000 swarm circles its JS heap after GC is about 753 MB, because each circle is its own sphere mesh.

**Run-to-run variance (`gpu-angle-vulkan`, 18 configurations, 3 repeats).** CV of p50 frame time: 2.3–13.4%, median
5.6%. CV of p95: 2.0–34.5%, median 6.6%. CV of p99: 1.2–33.9%, median 8.2%. The widest tail spread is SVG
`room-tiles` at 1,000 nodes (p95 CV 34.5%). Compare tails only across repeats. Treat a change in p50 below about 10%
as noise on this host.

### `gpu-angle-vulkan`

`baselines/fdev-2026-11-01-gpu-angle-vulkan.json`. Harness commit `a111e5abcf82` (`dirty: false`). Session 2026-11-01T10:26Z to 2026-11-01T10:59Z (UTC, including capacity holds). 54 runs, 54 ok. 3 repeats × 300 recorded frames after 60 warm-up frames. 1-minute load at run start: 1.62 to 3.34 (the capacity probe admitted every run at `--max-load-per-core 0.25`). No other browser GPU process ran beside any run (`foreignGpuProcessesBefore` maximum 0).

- WebGL renderer: `ANGLE (AMD, Vulkan 1.4.354 (AMD Ryzen 9 7900 12-Core Processor (RADV RAPHAEL_MENDOCINO) (0x0000164E)), radv)`
- chrome://gpu: webgl `enabled`, gpu_compositing `enabled`, rasterization `enabled`, vulkan `enabled_on`, webgpu `enabled`
- WebGPU adapter: amd/rdna-2, fallback False, timestamp-query True
- Browser: Chrome/153.0.8010.12

| scene | size | backend | drawn | frame p50 / p95 / p99 ms | p50 CV | main-thread CPU ms/frame | GPU-process CPU ms/frame | GPU time p50 ms | draw calls/frame | heap after GC MB |
|---|---:|---|---:|---|---:|---:|---:|---:|---:|---:|
| room-tiles | 100 | svg | 100 | 2.545 / 3.435 / 4.7 | 2.9% | 2.616 | 0.789 | Unknown | Unknown | 6.1 |
| room-tiles | 100 | pixi | 100 | 0.84 / 1.655 / 2.595 | 8.9% | 0.336 | 1.28 | 0.577 | 2 | 8.28 |
| room-tiles | 100 | babylon | 100 | 1.605 / 2.805 / 3.675 | 9.6% | 1.114 | 1.25 | 1.259 | 200 | 11.46 |
| room-tiles | 1000 | svg | 1000 | 22.455 / 28.015 / 30.82 | 10.6% | 22.669 | 2.303 | Unknown | Unknown | 6.29 |
| room-tiles | 1000 | pixi | 1000 | 1.135 / 1.995 / 4.26 | 7.5% | 0.736 | 1.178 | 0.568 | 2 | 10.67 |
| room-tiles | 1000 | babylon | 576 (partial) | 7.125 / 9.875 / 11.5 | 11.7% | 6.017 | 3.531 | 5.901 | 1152 | 29.34 |
| room-tiles | 5000 | svg | 5000 | 116.505 / 136.53 / 143.445 | 6.1% | 117.565 | 8.642 | Unknown | Unknown | 7.02 |
| room-tiles | 5000 | pixi | 5000 | 2.675 / 5.26 / 20.21 | 4% | 2.622 | 1.013 | 0.799 | 2 | 24.12 |
| room-tiles | 5000 | babylon | 80 (partial) | 14.51 / 16.205 / 30.68 | 3.9% | 13.659 | 1.887 | 12.54 | 160 | 84.55 |
| room-swarm | 100 | svg | 100 | 2.555 / 3.91 / 7.77 | 3.6% | 2.69 | 0.742 | Unknown | Unknown | 6.08 |
| room-swarm | 100 | pixi | 100 | 0.88 / 1.465 / 2.355 | 4% | 0.364 | 1.181 | 0.554 | 2 | 7.75 |
| room-swarm | 100 | babylon | 100 | 1.54 / 2.38 / 3.075 | 5.2% | 1.036 | 1.167 | 1.063 | 200 | 24.41 |
| room-swarm | 1000 | svg | 1000 | 20.92 / 27.21 / 38.57 | 2.3% | 21.737 | 2.013 | Unknown | Unknown | 6.23 |
| room-swarm | 1000 | pixi | 1000 | 1.515 / 2.34 / 3.28 | 5% | 1.006 | 1.195 | 0.596 | 2 | 12.01 |
| room-swarm | 1000 | babylon | 556 (partial) | 7.67 / 10.215 / 11.275 | 13.4% | 6.673 | 3.41 | 7.036 | 1118 | 162.78 |
| room-swarm | 5000 | svg | 5000 | 109.265 / 138.805 / 166.445 | 5.2% | 108.935 | 7.45 | Unknown | Unknown | 6.8 |
| room-swarm | 5000 | pixi | 5000 | 5.07 / 6.77 / 8.03 | 9.3% | 4.366 | 1.234 | 0.978 | 2 | 29.58 |
| room-swarm | 5000 | babylon | 64 (partial) | 14.96 / 31.155 / 33.45 | 6% | 14.289 | 1.65 | 12.964 | 128 | 752.93 |


### `software`

Pending: the software-control session (same harness commit) is still running under the capacity gate. It is added to this PR when it completes.

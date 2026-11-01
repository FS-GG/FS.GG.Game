// GAME-RENDER-ADOPT-01.2 (FS.GG.Game#746): page-side instruments for the rendering benchmark.
//
// Loaded as a classic script BEFORE any module, so it sees every WebGL context, draw call and animation-frame
// callback the backends make. It measures; it never decides. The F# page (Program.fs) drives the game state and
// the backend through Game.Render's RendererSelection, and tools/render-bench/driver.mjs reads the results over the
// Chrome DevTools Protocol and adds the browser-process metrics (main-thread task time, heap) that a page cannot see.
//
// What is counted here, and how:
//  - draw calls: every drawArrays/drawElements/drawRangeElements/*Instanced call on a WebGL or WebGL2 context, and
//    the ANGLE_instanced_arrays and WEBGL_multi_draw extension calls (a multi-draw counts its draw count). SVG has
//    no draw calls the page can see; the driver reports them as Unknown for SVG.
//  - in-page script time: the time inside every requestAnimationFrame callback, which includes the engines' own
//    render loops (Babylon's runRenderLoop, Pixi's ticker) and the benchmark's state step and Present.
//  - GPU time: EXT_disjoint_timer_query_webgl2 TIME_ELAPSED queries on the live backend context, one around EVERY
//    animation-frame callback during the window (the engines' render loops and the benchmark's own step + Present,
//    which is where the backends issue their WebGL commands), summed per frame. Each query opens and closes inside
//    one callback, so it brackets that callback's commands and not the GPU's idle time between frames: a query left
//    open from one frame to the next would measure the frame period on the GPU timeline instead. A frame with a
//    disjoint or unavailable query is dropped and counted. It is the backend's own WebGL work; the compositor's
//    copy of the canvas to the page is not included. Without the extension, or for SVG, GPU time is Unknown.
(() => {
  "use strict";
  const state = {
    drawCalls: 0,
    rafScriptMs: 0,
    rafCallbacks: 0,
    contexts: [],
  };

  const countDraws = (proto, names) => {
    if (!proto) return;
    for (const name of names) {
      const original = proto[name];
      if (typeof original !== "function") continue;
      proto[name] = function (...args) {
        state.drawCalls += 1;
        return original.apply(this, args);
      };
    }
  };
  const draws = ["drawArrays", "drawElements", "drawRangeElements", "drawArraysInstanced", "drawElementsInstanced"];
  countDraws(globalThis.WebGLRenderingContext && WebGLRenderingContext.prototype, draws);
  countDraws(globalThis.WebGL2RenderingContext && WebGL2RenderingContext.prototype, draws);

  const patchedExtensions = new WeakSet();
  const patchExtension = (ext) => {
    if (!ext || patchedExtensions.has(ext)) return ext;
    patchedExtensions.add(ext);
    for (const name of ["drawArraysInstancedANGLE", "drawElementsInstancedANGLE"]) {
      if (typeof ext[name] === "function") {
        const original = ext[name].bind(ext);
        ext[name] = (...args) => {
          state.drawCalls += 1;
          return original(...args);
        };
      }
    }
    for (const name of ["multiDrawArraysWEBGL", "multiDrawElementsWEBGL", "multiDrawArraysInstancedWEBGL", "multiDrawElementsInstancedWEBGL"]) {
      if (typeof ext[name] === "function") {
        const original = ext[name].bind(ext);
        ext[name] = (...args) => {
          state.drawCalls += Number(args[args.length - 1]) || 0;
          return original(...args);
        };
      }
    }
    return ext;
  };
  for (const ctor of [globalThis.WebGLRenderingContext, globalThis.WebGL2RenderingContext]) {
    if (!ctor) continue;
    const getExtension = ctor.prototype.getExtension;
    ctor.prototype.getExtension = function (name) {
      return patchExtension(getExtension.call(this, name));
    };
  }

  const getContext = HTMLCanvasElement.prototype.getContext;
  HTMLCanvasElement.prototype.getContext = function (kind, ...rest) {
    const ctx = getContext.call(this, kind, ...rest);
    if (ctx && (kind === "webgl" || kind === "webgl2" || kind === "experimental-webgl")) {
      if (!state.contexts.some((entry) => entry.ctx === ctx)) state.contexts.push({ ctx, canvas: this, kind });
    }
    return ctx;
  };

  const raf = window.requestAnimationFrame.bind(window);
  window.requestAnimationFrame = (callback) =>
    raf((timestamp) => {
      const start = performance.now();
      const query = openQuery();
      try {
        return callback(timestamp);
      } finally {
        closeQuery(query);
        state.rafScriptMs += performance.now() - start;
        state.rafCallbacks += 1;
      }
    });

  /// The live backend context: the most recently created WebGL context whose canvas is in the document and whose
  /// context is not lost (the adapters' throwaway capability probes are detached and lost at once).
  const liveContext = () => {
    for (let i = state.contexts.length - 1; i >= 0; i--) {
      const entry = state.contexts[i];
      if (entry.canvas.isConnected && !entry.ctx.isContextLost()) return entry;
    }
    return null;
  };

  const rendererOf = (ctx) => {
    const info = ctx.getExtension("WEBGL_debug_renderer_info");
    return {
      renderer: info ? ctx.getParameter(info.UNMASKED_RENDERER_WEBGL) : ctx.getParameter(ctx.RENDERER),
      vendor: info ? ctx.getParameter(info.UNMASKED_VENDOR_WEBGL) : ctx.getParameter(ctx.VENDOR),
      version: ctx.getParameter(ctx.VERSION),
    };
  };

  // ---- the recording window -------------------------------------------------------------------------------
  const bench = {
    phase: "loading",
    error: null,
    frames: 0, // benchmark frame callbacks so far (the loop runs from mount to finish)
    target: 0, // frames to record
    samples: { interval: [], work: [], draws: [], raf: [] },
    gpu: { status: "unknown", reason: "not started", source: null, frames: new Map(), disjoint: 0, unavailable: 0, pending: [] },
    gpuWindow: false,
    firstGpuFrame: null,
    lastGpuFrame: null,
    context: null,
    recording: false,
    lastTimestamp: null,
    lastDraws: 0,
    lastRaf: 0,
    timer: null,
    info: {},
    windowStart: null,
    windowEnd: null,
  };

  /// Group results by the benchmark frame they were issued in; a frame is complete when every query it opened has
  /// a result.
  const pollQueries = () => {
    const gl = bench.context && bench.context.ctx;
    const ext = bench.timer;
    if (!gl || !ext) return;
    while (bench.gpu.pending.length > 0) {
      const entry = bench.gpu.pending[0];
      if (gl.isContextLost()) {
        bench.gpu.pending.length = 0;
        return;
      }
      if (!gl.getQueryParameter(entry.query, gl.QUERY_RESULT_AVAILABLE)) return;
      const disjoint = gl.getParameter(ext.GPU_DISJOINT_EXT);
      const ns = gl.getQueryParameter(entry.query, gl.QUERY_RESULT);
      bench.gpu.pending.shift();
      gl.deleteQuery(entry.query);
      const frame = bench.gpu.frames.get(entry.frame) || { ns: 0, bad: false };
      if (disjoint) {
        bench.gpu.disjoint += 1;
        frame.bad = true;
      } else frame.ns += ns;
      bench.gpu.frames.set(entry.frame, frame);
    }
  };

  function openQuery() {
    const gl = bench.context && bench.context.ctx;
    const ext = bench.timer;
    if (!bench.gpuWindow || !gl || !ext || gl.isContextLost()) return null;
    const query = gl.createQuery();
    gl.beginQuery(ext.TIME_ELAPSED_EXT, query);
    // Another TIME_ELAPSED query already active on this context (an engine's own instrumentation) makes beginQuery
    // fail; that frame's GPU time is then not observed and the frame is dropped, never guessed.
    if (gl.getError() !== gl.NO_ERROR) {
      gl.deleteQuery(query);
      bench.gpu.unavailable += 1;
      bench.gpu.frames.set(bench.frames, Object.assign(bench.gpu.frames.get(bench.frames) || { ns: 0 }, { bad: true }));
      return null;
    }
    return { query, frame: bench.frames };
  }

  function closeQuery(entry) {
    if (!entry) return;
    const gl = bench.context.ctx;
    gl.endQuery(bench.timer.TIME_ELAPSED_EXT);
    bench.gpu.pending.push(entry);
  }

  /// Called by the page at the START of every benchmark frame callback, before it steps and presents the state.
  bench.frameStart = (timestamp) => {
    bench.frames += 1;
    if (bench.recording) {
      bench.samples.interval.push(timestamp - bench.lastTimestamp);
      bench.samples.draws.push(state.drawCalls - bench.lastDraws);
      bench.samples.raf.push(state.rafScriptMs - bench.lastRaf);
      if (bench.samples.interval.length >= bench.target) {
        bench.recording = false;
        bench.gpuWindow = false;
        bench.lastGpuFrame = bench.frames - 1;
        bench.windowEnd = { frames: bench.frames, now: performance.now(), drawCalls: state.drawCalls, rafScriptMs: state.rafScriptMs };
        bench.phase = "recorded";
      }
    } else if (bench.armed) {
      bench.armed = false;
      bench.recording = true;
      bench.gpuWindow = true;
      bench.firstGpuFrame = bench.frames;
      bench.phase = "recording";
    }
    pollQueries();
    bench.lastTimestamp = timestamp;
    bench.lastDraws = state.drawCalls;
    bench.lastRaf = state.rafScriptMs;
  };

  /// Called by the page at the END of every benchmark frame callback with the time it took to step and present.
  bench.frameWork = (ms) => {
    if (bench.recording) bench.samples.work.push(ms);
  };

  /// The driver arms the window: recording starts at the next frame and covers `frames` frame intervals.
  bench.arm = (frames) => {
    bench.target = frames;
    bench.context = liveContext();
    bench.timer = null;
    if (bench.context && bench.context.kind === "webgl2") {
      bench.timer = bench.context.ctx.getExtension("EXT_disjoint_timer_query_webgl2");
      bench.gpu.source = "EXT_disjoint_timer_query_webgl2";
      bench.gpu.reason = bench.timer ? null : "EXT_disjoint_timer_query_webgl2 is not exposed by this context";
    } else if (bench.context) {
      bench.gpu.reason = "the backend context is WebGL 1; the benchmark reads timer queries only on WebGL 2";
    } else {
      bench.gpu.reason = "no live WebGL context: this backend draws without one (SVG is rasterised by the browser compositor)";
    }
    bench.windowStart = { frames: bench.frames, now: performance.now(), drawCalls: state.drawCalls, rafScriptMs: state.rafScriptMs };
    bench.armed = true;
    return bench.windowStart;
  };

  bench.liveRenderer = () => {
    const entry = liveContext();
    return entry ? Object.assign({ kind: entry.kind }, rendererOf(entry.ctx)) : null;
  };

  bench.counters = () => ({ drawCalls: state.drawCalls, rafScriptMs: state.rafScriptMs, rafCallbacks: state.rafCallbacks, contexts: state.contexts.length });

  /// After the window: wait for the outstanding GPU query results, then summarise them.
  /// The per-frame GPU times of the window. A query is attributed to `bench.frames` as it stands when the callback
  /// starts, which the benchmark callback increments, so group g holds the callbacks after the benchmark callback of
  /// tick g, those before it in tick g+1, and the benchmark callback of tick g+1 (its query opens before it
  /// increments the counter): one execution of every callback. The window opens inside the benchmark callback of
  /// tick k (= firstGpuFrame) and closes inside that of tick m, so groups k..m-1 (= lastGpuFrame) are complete and
  /// number exactly the recorded frames.
  const gpuFrameMs = () => {
    const out = [];
    for (let f = bench.firstGpuFrame; f <= bench.lastGpuFrame; f++) {
      const frame = bench.gpu.frames.get(f);
      if (frame && !frame.bad) out.push(frame.ns / 1e6);
    }
    return out;
  };

  bench.settleGpu = () => {
    pollQueries();
    const results = gpuFrameMs().length;
    if (bench.timer) {
      bench.gpu.status = bench.gpu.pending.length === 0 && results > 0 ? "measured" : "unknown";
      if (bench.gpu.status === "unknown")
        bench.gpu.reason = bench.gpu.pending.length > 0 ? `${bench.gpu.pending.length} query results never became available` : "no complete frame had a usable query result";
      else bench.gpu.reason = null;
    }
    return { pending: bench.gpu.pending.length, results };
  };

  bench.report = () => ({
    phase: bench.phase,
    error: bench.error,
    info: bench.info,
    windowStart: bench.windowStart,
    windowEnd: bench.windowEnd,
    samples: bench.samples,
    gpu: { status: bench.gpu.status, reason: bench.gpu.reason, source: bench.gpu.source, ms: gpuFrameMs(), dropped: bench.lastGpuFrame - bench.firstGpuFrame + 1 - gpuFrameMs().length, disjoint: bench.gpu.disjoint, unavailable: bench.gpu.unavailable, pending: bench.gpu.pending.length },
    renderer: bench.context ? Object.assign({ kind: bench.context.kind }, rendererOf(bench.context.ctx)) : null,
    devicePixelRatio: window.devicePixelRatio,
    crossOriginIsolated: globalThis.crossOriginIsolated === true,
    userAgent: navigator.userAgent,
  });

  window.__fsggBench = bench;
})();

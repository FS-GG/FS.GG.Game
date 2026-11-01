#!/usr/bin/env node
// GAME-RENDER-ADOPT-01.2 (FS.GG.Game#746): the rendering benchmark driver.
//
// Drives Chromium over the Chrome DevTools Protocol (Node's built-in WebSocket; no npm dependency) through the
// page built by scripts/render-bench.sh (tests/Render.Browser.Bench). Every run is one game, scene, size, backend
// and repeat in a FRESH browser process, so no run inherits another's heap, GPU process state or shader cache.
//
//   driver.mjs run      --page <dir> --browser <chrome> --mode <gpu-angle-vulkan|gpu-angle-gl-egl|software|custom> [options]
//   driver.mjs probe    --browser <chrome> --mode <...>            feasibility: chrome://gpu status, WebGL, WebGPU
//   driver.mjs validate <results.json> [--schema <schema.json>]   check a results file against the schema
//   driver.mjs table    <results.json>...                          Markdown summary of the aggregates
//
// See tools/render-bench/README.md for the measurement definitions and caveats.

import { spawn, spawnSync } from "node:child_process";
import { createServer } from "node:http";
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync, statSync } from "node:fs";
import { tmpdir, cpus, totalmem, release, version as osVersion, platform, arch } from "node:os";
import { join, resolve, extname, normalize, sep, dirname } from "node:path";
import { fileURLToPath } from "node:url";

export const SCHEMA = "fsgg.render-bench/v1";
const HERE = dirname(fileURLToPath(import.meta.url));

// ---- browser modes ---------------------------------------------------------------------------------------------
// COMMON keeps every mode comparable: frame pacing is uncapped (no vsync, no 60 Hz limit) so the frame interval is
// the cost of a frame rather than the display rate, and background throttling is off.
const COMMON = [
  "--headless=new",
  "--no-sandbox",
  "--window-size=1280,800",
  "--disable-gpu-vsync",
  "--disable-frame-rate-limit",
  "--disable-background-timer-throttling",
  "--disable-renderer-backgrounding",
  "--disable-backgrounding-occluded-windows",
  "--no-first-run",
  "--no-default-browser-check",
  "--disable-extensions",
  "--mute-audio",
  "--hide-scrollbars",
];
// The mode names are the profile names of the fdev functional-GPU profile in FS.GG.Rendering
// (docs/reports/svg-fdev-functional-gpu-baseline-20261030.md), so results from both harnesses line up.
export const MODES = {
  // Hardware: ANGLE on Vulkan (RADV on fdev). The primary GPU mode.
  "gpu-angle-vulkan": { expect: "hardware", flags: ["--use-angle=vulkan", "--enable-features=Vulkan", "--ignore-gpu-blocklist", "--enable-unsafe-webgpu"] },
  // Hardware: ANGLE on desktop GL through EGL (radeonsi on fdev).
  "gpu-angle-gl-egl": { expect: "hardware", flags: ["--use-gl=angle", "--use-angle=gl-egl", "--ignore-gpu-blocklist", "--enable-unsafe-webgpu"] },
  // The software control: SwiftShader for WebGL and no GPU compositing. Same flags the CI browser smoke uses.
  software: { expect: "software", flags: ["--disable-gpu", "--enable-unsafe-swiftshader", "--use-angle=swiftshader"] },
  custom: { expect: "any", flags: [] },
};

// ---- small utilities -------------------------------------------------------------------------------------------
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const round = (x, digits = 3) => (x == null || !Number.isFinite(x) ? null : Number(x.toFixed(digits)));

/// Nearest-rank percentile of an unsorted sample.
export function percentile(values, p) {
  if (!values.length) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const rank = Math.max(1, Math.ceil((p / 100) * sorted.length));
  return sorted[rank - 1];
}
const mean = (v) => (v.length ? v.reduce((a, b) => a + b, 0) / v.length : null);
function stddev(v) {
  if (v.length < 2) return null;
  const m = mean(v);
  return Math.sqrt(v.reduce((a, b) => a + (b - m) ** 2, 0) / (v.length - 1));
}
export function distribution(values) {
  return {
    count: values.length,
    p50: round(percentile(values, 50)),
    p95: round(percentile(values, 95)),
    p99: round(percentile(values, 99)),
    mean: round(mean(values)),
    min: round(values.length ? Math.min(...values) : null),
    max: round(values.length ? Math.max(...values) : null),
  };
}
/// Run-to-run spread of one statistic over the repeats of a configuration.
export function spread(values) {
  const v = values.filter((x) => Number.isFinite(x));
  const m = mean(v);
  const s = stddev(v);
  return {
    repeats: v.length,
    median: round(percentile(v, 50)),
    mean: round(m),
    stddev: round(s),
    cvPercent: m && s != null ? round((100 * s) / m, 1) : null,
    min: round(v.length ? Math.min(...v) : null),
    max: round(v.length ? Math.max(...v) : null),
  };
}

function parseArgs(argv) {
  const args = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith("--") && a.includes("=")) {
      // --key=value: the only way to pass a value that itself starts with "--" (e.g. --capacity-args=--max-load-per-core 0.25).
      const at = a.indexOf("=");
      args[a.slice(2, at)] = a.slice(at + 1);
    } else if (a.startsWith("--")) {
      const key = a.slice(2);
      const next = argv[i + 1];
      if (next === undefined || next.startsWith("--")) args[key] = true;
      else {
        args[key] = next;
        i++;
      }
    } else args._.push(a);
  }
  return args;
}
const list = (text, fallback) => (text === undefined || text === true ? fallback : String(text).split(",").map((s) => s.trim()).filter(Boolean));
const int = (text, fallback) => {
  if (text === undefined) return fallback;
  const n = Number(text);
  if (!Number.isInteger(n) || n <= 0) throw new Error(`expected a positive integer, got ${text}`);
  return n;
};

// ---- static page server (a secure localhost origin: WebGPU is exposed only to secure contexts) ----------------
const MIME = { ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript", ".json": "application/json", ".map": "application/json", ".wasm": "application/wasm", ".css": "text/css" };
function serve(root) {
  const base = resolve(root);
  const server = createServer((req, res) => {
    const path = decodeURIComponent(new URL(req.url, "http://x").pathname);
    const file = normalize(join(base, path === "/" ? "index.html" : path));
    if (!(file === base || file.startsWith(base + sep)) || !existsSync(file) || !statSync(file).isFile()) {
      res.statusCode = 404;
      res.end("not found");
      return;
    }
    res.setHeader("content-type", MIME[extname(file)] || "application/octet-stream");
    res.setHeader("cache-control", "no-store");
    // Cross-origin isolation raises the page clock (performance.now and rAF timestamps) from 100 us to 5 us
    // resolution in Chromium; every resource is same-origin, so require-corp costs nothing.
    res.setHeader("cross-origin-opener-policy", "same-origin");
    res.setHeader("cross-origin-embedder-policy", "require-corp");
    res.end(readFileSync(file));
  });
  return new Promise((r) => server.listen(0, "127.0.0.1", () => r(server)));
}

// ---- Chrome DevTools Protocol over one browser WebSocket with a flattened page session ------------------------
class Browser {
  static async launch(executable, flags) {
    const profile = mkdtempSync(join(tmpdir(), "fsgg-render-bench-"));
    const proc = spawn(executable, [...flags, `--user-data-dir=${profile}`, "--remote-debugging-port=0", "about:blank"], { stdio: ["ignore", "ignore", "pipe"], detached: true });
    let stderr = "";
    proc.stderr.on("data", (d) => {
      stderr = (stderr + d).slice(-20000);
    });
    let port;
    for (let i = 0; i < 200 && !port; i++) {
      const file = join(profile, "DevToolsActivePort");
      if (existsSync(file)) port = readFileSync(file, "utf8").split("\n")[0];
      else if (proc.exitCode !== null) break;
      else await sleep(50);
    }
    if (!port) {
      try { process.kill(-proc.pid, "SIGKILL"); } catch {}
      rmSync(profile, { recursive: true, force: true });
      throw new Error(`browser did not start: ${stderr.slice(-1500)}`);
    }
    const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
    const b = new Browser(proc, profile, version, () => stderr);
    await b.connect(version.webSocketDebuggerUrl);
    return b;
  }
  constructor(proc, profile, version, stderr) {
    Object.assign(this, { proc, profile, version, stderr, nextId: 0, pending: new Map(), session: null });
  }
  connect(url) {
    this.ws = new WebSocket(url);
    this.ws.onmessage = (event) => {
      const message = JSON.parse(event.data);
      if (message.id && this.pending.has(message.id)) {
        const { resolve: ok, reject } = this.pending.get(message.id);
        this.pending.delete(message.id);
        if (message.error) reject(new Error(`${message.error.message} (${message.error.code})`));
        else ok(message.result);
      }
    };
    return new Promise((r, j) => {
      this.ws.onopen = r;
      this.ws.onerror = () => j(new Error("CDP websocket error"));
    });
  }
  send(method, params = {}, sessionId = undefined) {
    const id = ++this.nextId;
    this.ws.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
    return new Promise((ok, reject) => this.pending.set(id, { resolve: ok, reject }));
  }
  async openPage() {
    const { targetInfos } = await this.send("Target.getTargets");
    const page = targetInfos.find((t) => t.type === "page");
    const { sessionId } = await this.send("Target.attachToTarget", { targetId: page.targetId, flatten: true });
    this.session = sessionId;
    return sessionId;
  }
  page(method, params = {}) {
    return this.send(method, params, this.session);
  }
  async evaluate(expression) {
    const result = await this.page("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
    if (result.exceptionDetails) throw new Error(`page evaluation failed: ${result.exceptionDetails.exception?.description || result.exceptionDetails.text}`);
    return result.result.value;
  }
  async close() {
    try { this.ws.close(); } catch {}
    try { process.kill(-this.proc.pid, "SIGKILL"); } catch {}
    await sleep(200);
    rmSync(this.profile, { recursive: true, force: true });
  }
}

const metricsOf = (result) => Object.fromEntries(result.metrics.map((m) => [m.name, m.value]));
const processCpu = async (browser) => {
  const { processInfo } = await browser.send("SystemInfo.getProcessInfo");
  const totals = {};
  for (const p of processInfo) totals[p.type] = (totals[p.type] || 0) + p.cpuTime;
  return totals;
};

// ---- environment fingerprint -----------------------------------------------------------------------------------
const WEBGL_PROBE = `(() => {
  const out = {};
  for (const kind of ["webgl", "webgl2"]) {
    const c = document.createElement("canvas");
    const gl = c.getContext(kind);
    if (!gl) { out[kind] = null; continue; }
    const dbg = gl.getExtension("WEBGL_debug_renderer_info");
    out[kind] = {
      vendor: dbg ? gl.getParameter(dbg.UNMASKED_VENDOR_WEBGL) : gl.getParameter(gl.VENDOR),
      renderer: dbg ? gl.getParameter(dbg.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER),
      version: gl.getParameter(gl.VERSION),
      timerQuery: kind === "webgl2" ? !!gl.getExtension("EXT_disjoint_timer_query_webgl2") : !!gl.getExtension("EXT_disjoint_timer_query"),
    };
    const lose = gl.getExtension("WEBGL_lose_context"); if (lose) lose.loseContext();
  }
  return out;
})()`;
const WEBGPU_PROBE = `(async () => {
  if (!navigator.gpu) return { available: false, reason: "navigator.gpu is absent" };
  const adapter = await Promise.race([navigator.gpu.requestAdapter(), new Promise((r) => setTimeout(() => r("timeout"), 8000))]);
  if (adapter === "timeout") return { available: false, reason: "requestAdapter did not settle within 8 s" };
  if (!adapter) return { available: false, reason: "requestAdapter returned null" };
  const info = adapter.info || {};
  return { available: true, vendor: info.vendor, architecture: info.architecture, device: info.device, description: info.description, isFallbackAdapter: adapter.isFallbackAdapter ?? info.isFallbackAdapter ?? null, timestampQuery: adapter.features.has("timestamp-query") };
})()`;

async function fingerprint(browser, executable, flags) {
  const info = await browser.send("SystemInfo.getInfo");
  const gpu = info.gpu || {};
  const aux = gpu.auxAttributes || {};
  return {
    browser: { product: browser.version.Browser, protocol: browser.version["Protocol-Version"], userAgent: browser.version["User-Agent"], v8: browser.version["V8-Version"], executable },
    flags,
    gpu: {
      featureStatus: gpu.featureStatus || null,
      devices: (gpu.devices || []).map((d) => ({ vendorId: d.vendorId, deviceId: d.deviceId, vendorString: d.vendorString, deviceString: d.deviceString, driverVendor: d.driverVendor, driverVersion: d.driverVersion })),
      glRenderer: aux.glRenderer ?? null,
      glVersion: aux.glVersion ?? null,
      glImplementationParts: aux.glImplementationParts ?? null,
      displayType: aux.displayType ?? null,
    },
    webgl: await browser.evaluate(WEBGL_PROBE),
    webgpu: await browser.evaluate(WEBGPU_PROBE),
  };
}

function hostFingerprint() {
  const cpu = cpus();
  const read = (path) => {
    try { return readFileSync(path, "utf8").trim(); } catch { return null; }
  };
  return {
    platform: platform(),
    arch: arch(),
    kernel: release(),
    os: osVersion(),
    cpuModel: cpu[0]?.model ?? null,
    logicalCpus: cpu.length,
    memoryGiB: round(totalmem() / 1024 ** 3, 1),
    gpuDevice: read("/sys/class/drm/card0/device/uevent")?.split("\n").find((l) => l.startsWith("PCI_ID=")) ?? null,
    ldLibraryPath: process.env.LD_LIBRARY_PATH || null,
  };
}

/// Classify the WebGL renderer a mode actually got, so a GPU run that silently fell back to software is refused.
export function rendererClass(renderer) {
  if (!renderer) return "none";
  return /swiftshader|llvmpipe|software/i.test(renderer) ? "software" : "hardware";
}

// ---- capacity --------------------------------------------------------------------------------------------------
const loadavg = () => {
  try { return readFileSync("/proc/loadavg", "utf8").trim().split(/\s+/).slice(0, 3).map(Number); } catch { return null; }
};
async function admit(probe, probeArgs, waitSeconds, log) {
  if (!probe) return { status: "not-configured" };
  if (!existsSync(probe)) throw new Error(`capacity probe ${probe} does not exist`);
  const deadline = Date.now() + waitSeconds * 1000;
  for (;;) {
    const r = spawnSync("python3", ["-I", probe, ...probeArgs], { encoding: "utf8" });
    let verdict = null;
    try { verdict = JSON.parse(r.stdout.trim().split("\n").pop()); } catch {}
    if (r.status === 0 && verdict) return { status: "admit", probe, args: probeArgs, verdict };
    if (r.status !== 3 || !verdict) throw new Error(`capacity probe failed (exit ${r.status}): ${r.stderr || r.stdout}`);
    if (Date.now() > deadline) return { status: "hold", probe, args: probeArgs, verdict };
    log(`capacity hold (${(verdict.reasons || []).join("; ")}); waiting`);
    await sleep(30000);
  }
}

// ---- one run ---------------------------------------------------------------------------------------------------
async function waitPhase(browser, wanted, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const s = await browser.evaluate("({ phase: window.__fsggBench && window.__fsggBench.phase, error: window.__fsggBench && window.__fsggBench.error, frames: window.__fsggBench && window.__fsggBench.frames, recorded: window.__fsggBench && window.__fsggBench.samples.interval.length })");
    if (s.phase === "error") throw new Error(`page refused: ${s.error}`);
    if (wanted.includes(s.phase)) return s;
    if (Date.now() > deadline) {
      const e = new Error(`timed out waiting for ${wanted.join("|")} (phase ${s.phase}, frames ${s.frames}, recorded ${s.recorded})`);
      e.timeout = true;
      throw e;
    }
    await sleep(50);
  }
}

async function runOne(ctx, cell) {
  const { executable, flags, mode, origin, frames, warmup, timeoutMs, keepSamples } = ctx;
  const started = Date.now();
  const record = { ...cell, mode, status: "ok", error: null, startedAt: new Date(started).toISOString(), frames, warmupFrames: warmup };
  record.capacity = await admit(ctx.probe, ctx.probeArgs, ctx.capacityWait, ctx.log);
  if (record.capacity.status === "hold") {
    record.status = "held";
    record.error = "capacity probe held the run";
    return record;
  }
  record.loadavgBefore = loadavg();
  const browser = await Browser.launch(executable, flags);
  try {
    await browser.openPage();
    await browser.page("Performance.enable", { timeDomain: "timeTicks" });
    await browser.page("Runtime.enable");
    const url = `${origin}/index.html?game=${encodeURIComponent(cell.game)}&scene=${encodeURIComponent(cell.scene)}&size=${cell.size}&backend=${cell.backend}&warmup=${warmup}`;
    await browser.page("Page.navigate", { url });
    if (ctx.fingerprintNeeded) {
      // The environment fingerprint is taken in the first run's own browser, on the bench origin.
      await waitPhase(browser, ["mounting", "warmup", "ready"], timeoutMs);
      ctx.fingerprint = await fingerprint(browser, executable, flags);
      ctx.fingerprintNeeded = false;
    }
    await waitPhase(browser, ["ready"], timeoutMs);
    const live = await browser.evaluate("window.__fsggBench.liveRenderer()");
    record.backendRenderer = live;
    const expected = MODES[mode]?.expect ?? "any";
    if (cell.backend !== "svg" && expected !== "any" && rendererClass(live && live.renderer) !== expected) {
      throw new Error(`mode ${mode} expects a ${expected} WebGL renderer but the backend drew on '${live && live.renderer}'`);
    }

    const cpu0 = await processCpu(browser);
    const m0 = metricsOf(await browser.page("Performance.getMetrics"));
    const w0 = await browser.evaluate(`window.__fsggBench.arm(${frames})`);
    await waitPhase(browser, ["recorded"], timeoutMs);
    const w1 = await browser.evaluate("({ frames: window.__fsggBench.frames, now: performance.now() })");
    const m1 = metricsOf(await browser.page("Performance.getMetrics"));
    const cpu1 = await processCpu(browser);
    record.loadavgAfter = loadavg();

    for (let i = 0; i < 40; i++) {
      const g = await browser.evaluate("window.__fsggBench.settleGpu()");
      if (g.pending === 0) break;
      await sleep(25);
    }
    await browser.page("HeapProfiler.enable");
    await browser.page("HeapProfiler.collectGarbage");
    const m2 = metricsOf(await browser.page("Performance.getMetrics"));
    const report = await browser.evaluate("window.__fsggBench.report()");
    await browser.evaluate("window.__fsggBench.stop = true");

    const windowFrames = w1.frames - w0.frames;
    const windowSeconds = m1.Timestamp - m0.Timestamp;
    const perFrame = (name) => round(((m1[name] - m0[name]) * 1000) / windowFrames);
    const cpuPerFrame = (type) => (cpu0[type] === undefined || cpu1[type] === undefined ? null : round(((cpu1[type] - cpu0[type]) * 1000) / windowFrames));

    record.page = report.info;
    // What the backend holds and what it actually drew. A backend that culls part of the scene draws less than it
    // was asked to, and its frame time is then the cost of a smaller scene; `complete` makes that explicit.
    const drawn = cell.backend === "babylon" ? report.info.babylonActiveMeshes ?? null : cell.backend === "pixi" || cell.backend === "svg" ? report.info.backendNodes ?? null : null;
    record.coverage = {
      declaredNodes: cell.size,
      backendNodes: report.info.backendNodes ?? null,
      drawnNodes: drawn,
      drawnSource: cell.backend === "babylon" ? "Babylon scene.getActiveMeshes() after frustum culling" : cell.backend === "pixi" ? "Pixi stage display objects (Pixi does not cull by default)" : "SVG elements in the root",
      complete: drawn == null ? null : drawn === cell.size,
    };
    record.frameTimeMs = distribution(report.samples.interval);
    record.fps = record.frameTimeMs.p50 ? round(1000 / record.frameTimeMs.p50, 1) : null;
    record.presentMs = distribution(report.samples.work);
    record.rafScriptMs = distribution(report.samples.raf);
    record.mainThread = {
      source: "CDP Performance.getMetrics over the recording window, divided by the benchmark frames in it",
      windowFrames,
      windowSeconds: round(windowSeconds),
      cpuMsPerFrame: perFrame("ThreadTime"),
      taskMsPerFrame: perFrame("TaskDuration"),
      scriptMsPerFrame: perFrame("ScriptDuration"),
      layoutMsPerFrame: perFrame("LayoutDuration"),
      recalcStyleMsPerFrame: perFrame("RecalcStyleDuration"),
      utilisation: windowSeconds > 0 ? round((m1.TaskDuration - m0.TaskDuration) / windowSeconds) : null,
    };
    record.processCpuMsPerFrame = { source: "CDP SystemInfo.getProcessInfo cpuTime", renderer: cpuPerFrame("renderer"), gpu: cpuPerFrame("GPU"), browser: cpuPerFrame("browser") };
    record.gpuTimeMs =
      report.gpu.status === "measured"
        ? { status: "measured", source: report.gpu.source, ...distribution(report.gpu.ms), disjoint: report.gpu.disjoint, unobserved: report.gpu.unavailable, droppedFrames: report.gpu.dropped }
        : { status: "unknown", reason: report.gpu.reason, source: report.gpu.source };
    record.drawCalls =
      cell.backend === "svg"
        ? { status: "unknown", reason: "SVG is rasterised by the browser compositor; its draw calls are not visible to the page" }
        : { status: "counted", source: "WebGL draw* calls on every context in the page", perFrame: distribution(report.samples.draws) };
    record.memory = {
      jsHeapUsedMB: round(m1.JSHeapUsedSize / 1048576, 2),
      jsHeapTotalMB: round(m1.JSHeapTotalSize / 1048576, 2),
      jsHeapUsedAfterGcMB: round(m2.JSHeapUsedSize / 1048576, 2),
      domNodes: m2.Nodes,
      jsEventListeners: m2.JSEventListeners,
    };
    record.devicePixelRatio = report.devicePixelRatio;
    record.crossOriginIsolated = report.crossOriginIsolated;
    if (keepSamples) record.samples = { frameIntervalMs: report.samples.interval.map((x) => round(x, 2)), gpuMs: report.gpu.ms.map((x) => round(x, 3)) };
  } catch (error) {
    record.status = error.timeout ? "timeout" : "error";
    record.error = String(error.message || error);
  } finally {
    await browser.close();
  }
  record.durationSeconds = round((Date.now() - started) / 1000, 1);
  return record;
}

// ---- aggregation -----------------------------------------------------------------------------------------------
export function aggregate(runs) {
  const groups = new Map();
  for (const r of runs) {
    const key = [r.mode, r.game, r.scene, r.size, r.backend].join("|");
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(r);
  }
  const out = [];
  for (const rs of groups.values()) {
    const ok = rs.filter((r) => r.status === "ok");
    const pick = (f) => ok.map(f).filter((x) => x != null && Number.isFinite(x));
    const { mode, game, scene, size, backend } = rs[0];
    const gpuMeasured = ok.filter((r) => r.gpuTimeMs?.status === "measured");
    out.push({
      mode,
      game,
      scene,
      size,
      backend,
      repeats: rs.length,
      ok: ok.length,
      statuses: [...new Set(rs.map((r) => r.status))],
      frameTimeP50Ms: spread(pick((r) => r.frameTimeMs.p50)),
      frameTimeP95Ms: spread(pick((r) => r.frameTimeMs.p95)),
      frameTimeP99Ms: spread(pick((r) => r.frameTimeMs.p99)),
      mainThreadCpuMsPerFrame: spread(pick((r) => r.mainThread.cpuMsPerFrame)),
      presentMsP50: spread(pick((r) => r.presentMs.p50)),
      gpuProcessCpuMsPerFrame: spread(pick((r) => r.processCpuMsPerFrame.gpu)),
      gpuTimeP50Ms: gpuMeasured.length === ok.length && ok.length > 0 ? spread(gpuMeasured.map((r) => r.gpuTimeMs.p50)) : { status: "unknown", reason: ok[0]?.gpuTimeMs?.reason ?? "no successful run" },
      drawCallsP50: backend === "svg" ? { status: "unknown", reason: "not countable for SVG" } : spread(pick((r) => r.drawCalls.perFrame.p50)),
      jsHeapUsedAfterGcMB: spread(pick((r) => r.memory.jsHeapUsedAfterGcMB)),
      drawnNodes: spread(pick((r) => r.coverage.drawnNodes)),
      complete: ok.length > 0 && ok.every((r) => r.coverage.complete === true),
    });
  }
  return out;
}

// ---- schema validation (the subset of JSON Schema the results schema uses) ---------------------------------------
export function validate(value, schema, root = schema, path = "$") {
  const errors = [];
  if (schema.$ref) {
    const target = schema.$ref.replace(/^#\//, "").split("/").reduce((o, k) => o[k], root);
    return validate(value, target, root, path);
  }
  if (schema.anyOf) {
    const results = schema.anyOf.map((s) => validate(value, s, root, path));
    if (!results.some((r) => r.length === 0)) errors.push(`${path}: matches none of anyOf (${results.map((r) => r[0]).join(" | ")})`);
    return errors;
  }
  if (schema.const !== undefined && value !== schema.const) errors.push(`${path}: expected ${JSON.stringify(schema.const)}`);
  if (schema.enum && !schema.enum.includes(value)) errors.push(`${path}: ${JSON.stringify(value)} is not one of ${schema.enum.join(", ")}`);
  if (schema.type) {
    const types = Array.isArray(schema.type) ? schema.type : [schema.type];
    const actual = value === null ? "null" : Array.isArray(value) ? "array" : Number.isInteger(value) ? "integer" : typeof value;
    const ok = types.some((t) => t === actual || (t === "number" && actual === "integer"));
    if (!ok) return [`${path}: expected ${types.join("|")}, got ${actual}`];
  }
  if (value && typeof value === "object" && !Array.isArray(value)) {
    for (const key of schema.required || []) if (!(key in value)) errors.push(`${path}: missing ${key}`);
    for (const [key, sub] of Object.entries(schema.properties || {})) if (key in value) errors.push(...validate(value[key], sub, root, `${path}.${key}`));
  }
  if (Array.isArray(value)) {
    if (schema.minItems !== undefined && value.length < schema.minItems) errors.push(`${path}: fewer than ${schema.minItems} items`);
    if (schema.items) value.forEach((item, i) => errors.push(...validate(item, schema.items, root, `${path}[${i}]`)));
  }
  if (typeof value === "number" && schema.minimum !== undefined && value < schema.minimum) errors.push(`${path}: below ${schema.minimum}`);
  return errors;
}

// ---- Markdown summary ------------------------------------------------------------------------------------------
const fmt = (s) => (s && s.median != null ? `${s.median}` : "Unknown");
const cv = (s) => (s && s.cvPercent != null ? `${s.cvPercent}%` : "n/a");
export function table(results) {
  const lines = [
    "| mode | scene | size | backend | drawn | ok/runs | frame p50 ms | p95 | p99 | p50 CV | main-thread CPU ms/frame | GPU-process CPU ms/frame | GPU time p50 ms | draw calls/frame | heap after GC MB |",
    "|---|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
  ];
  for (const result of results)
    for (const a of result.aggregates)
      lines.push(
        `| ${a.mode} | ${a.scene} | ${a.size} | ${a.backend} | ${fmt(a.drawnNodes)}${a.complete ? "" : " (partial)"} | ${a.ok}/${a.repeats} | ${fmt(a.frameTimeP50Ms)} | ${fmt(a.frameTimeP95Ms)} | ${fmt(a.frameTimeP99Ms)} | ${cv(a.frameTimeP50Ms)} | ${fmt(a.mainThreadCpuMsPerFrame)} | ${fmt(a.gpuProcessCpuMsPerFrame)} | ${fmt(a.gpuTimeP50Ms)} | ${fmt(a.drawCallsP50)} | ${fmt(a.jsHeapUsedAfterGcMB)} |`,
      );
  return lines.join("\n");
}

/// Indented JSON with every array of numbers on one line, so raw samples stay compact and the rest diffable.
export function serialise(value) {
  return JSON.stringify(value, null, 1).replace(/\[\n[\s\d.,\-en+]*\]/g, (m) => "[" + m.slice(1, -1).split(",").map((x) => x.trim()).join(",") + "]") + "\n";
}

// ---- commands --------------------------------------------------------------------------------------------------
function modeFlags(args) {
  const mode = args.mode || "gpu-angle-vulkan";
  if (!MODES[mode]) throw new Error(`unknown mode ${mode} (known: ${Object.keys(MODES).join(", ")})`);
  const extra = args.flags && args.flags !== true ? String(args.flags).split(/\s+/).filter(Boolean) : [];
  return { mode, flags: [...COMMON, ...MODES[mode].flags, ...extra] };
}

async function commandRun(args) {
  const page = args.page;
  const executable = args.browser;
  if (!page || !existsSync(join(page, "index.html"))) throw new Error("--page <dir with index.html> is required (build it with scripts/render-bench.sh)");
  if (!executable) throw new Error("--browser <chrome executable> is required");
  const { mode, flags } = modeFlags(args);
  if (args["capacity-args"] === true) throw new Error("--capacity-args needs a value; write --capacity-args=<probe arguments>");
  const log = (m) => process.stderr.write(`render-bench: ${m}\n`);
  const server = await serve(page);
  const ctx = {
    executable,
    flags,
    mode,
    origin: `http://127.0.0.1:${server.address().port}`,
    frames: int(args.frames, 300),
    warmup: int(args.warmup, 60),
    timeoutMs: int(args["run-timeout"], 180) * 1000,
    keepSamples: args["no-samples"] !== true,
    probe: args["capacity-probe"] && args["capacity-probe"] !== true ? args["capacity-probe"] : null,
    probeArgs: args["capacity-args"] && args["capacity-args"] !== true ? String(args["capacity-args"]).split(/\s+/).filter(Boolean) : [],
    capacityWait: int(args["capacity-wait"], 600),
    fingerprintNeeded: true,
    fingerprint: null,
    log,
  };
  const games = list(args.games, ["reference-game"]);
  const scenes = list(args.scenes, ["room-tiles", "room-swarm"]);
  const sizes = list(args.sizes, ["100", "1000", "5000"]).map((s) => int(s));
  const backends = list(args.backends, ["svg", "pixi", "babylon"]);
  const repeats = int(args.repeats, 3);
  const runs = [];
  const started = new Date().toISOString();
  const cells = [];
  for (const game of games) for (const scene of scenes) for (const size of sizes) for (const backend of backends) cells.push({ game, scene, size, backend });
  try {
    // Repeat is the OUTER loop and every other repeat runs the cells in reverse order (ABBA), so drift in host state
    // over the session spreads across every cell instead of landing on whichever configuration ran last.
    session: for (let repeat = 1; repeat <= repeats; repeat++)
      for (const base of repeat % 2 === 1 ? cells : [...cells].reverse()) {
        const { game, scene, size, backend } = base;
        const cell = { game, scene, size, backend, repeat };
        const r = await runOne(ctx, cell);
        runs.push(r);
        log(`${mode} ${game}/${scene} size=${size} ${backend} #${repeat}: ${r.status}${r.error ? ` (${r.error})` : ""} p50=${r.frameTimeMs?.p50 ?? "-"}ms p95=${r.frameTimeMs?.p95 ?? "-"}ms cpu=${r.mainThread?.cpuMsPerFrame ?? "-"}ms gpu=${r.gpuTimeMs?.p50 ?? r.gpuTimeMs?.status ?? "-"}ms draws=${r.drawCalls?.perFrame?.p50 ?? "-"} load1=${r.loadavgBefore?.[0] ?? "-"}`);
        if (r.status === "held") {
          log("capacity probe held the session past --capacity-wait; stopping and writing what ran");
          break session;
        }
      }
  } finally {
    server.close();
  }
  const result = {
    schema: SCHEMA,
    createdAt: started,
    finishedAt: new Date().toISOString(),
    harness: { commit: args["harness-commit"] && args["harness-commit"] !== true ? args["harness-commit"] : null, dirty: args["harness-dirty"] === "true" || args["harness-dirty"] === true, label: args.label && args.label !== true ? args.label : null },
    config: { mode, games, scenes, sizes, backends, repeats, order: "repeat-outer, alternating forward/reverse cell order", frames: ctx.frames, warmupFrames: ctx.warmup, percentileMethod: "nearest-rank", rawSamples: ctx.keepSamples, capacityProbe: ctx.probe, capacityArgs: ctx.probeArgs },
    environment: { host: hostFingerprint(), ...(ctx.fingerprint || {}) },
    runs,
    aggregates: aggregate(runs),
  };
  const errors = validate(result, JSON.parse(readFileSync(join(HERE, "results.schema.v1.json"), "utf8")));
  if (errors.length) throw new Error(`results do not satisfy the schema:\n${errors.slice(0, 20).join("\n")}`);
  const out = args.out && args.out !== true ? args.out : null;
  if (out) writeFileSync(out, serialise(result));
  else process.stdout.write(serialise(result));
  process.stderr.write(table([result]) + "\n");
  const failed = runs.filter((r) => r.status !== "ok").length;
  if (failed) log(`${failed} of ${runs.length} runs did not complete`);
  return failed ? 1 : 0;
}

async function commandProbe(args) {
  if (!args.browser) throw new Error("--browser <chrome executable> is required");
  const { mode, flags } = modeFlags(args);
  const server = createServer((q, res) => {
    res.setHeader("content-type", "text/html");
    res.end("<!doctype html><title>render-bench probe</title>");
  });
  await new Promise((r) => server.listen(0, "127.0.0.1", r));
  const browser = await Browser.launch(args.browser, flags);
  try {
    await browser.openPage();
    await browser.page("Page.navigate", { url: `http://127.0.0.1:${server.address().port}/` });
    await sleep(500);
    const fp = await fingerprint(browser, args.browser, flags);
    const renderer = fp.webgl?.webgl2?.renderer ?? fp.webgl?.webgl?.renderer ?? null;
    process.stdout.write(JSON.stringify({ mode, expect: MODES[mode].expect, rendererClass: rendererClass(renderer), host: hostFingerprint(), ...fp }, null, 1) + "\n");
    return MODES[mode].expect === "any" || rendererClass(renderer) === MODES[mode].expect ? 0 : 1;
  } finally {
    await browser.close();
    server.close();
  }
}

function commandValidate(args) {
  const schemaPath = args.schema && args.schema !== true ? args.schema : join(HERE, "results.schema.v1.json");
  const schema = JSON.parse(readFileSync(schemaPath, "utf8"));
  let failed = 0;
  for (const file of args._) {
    const errors = validate(JSON.parse(readFileSync(file, "utf8")), schema);
    if (errors.length) {
      failed++;
      process.stderr.write(`${file}: INVALID\n  ${errors.slice(0, 20).join("\n  ")}\n`);
    } else process.stdout.write(`${file}: valid ${SCHEMA}\n`);
  }
  return failed ? 1 : 0;
}

async function main() {
  const [command, ...rest] = process.argv.slice(2);
  const args = parseArgs(rest);
  switch (command) {
    case "run":
      return commandRun(args);
    case "probe":
      return commandProbe(args);
    case "validate":
      return commandValidate(args);
    case "table":
      process.stdout.write(table(args._.map((f) => JSON.parse(readFileSync(f, "utf8")))) + "\n");
      return 0;
    default:
      process.stderr.write("usage: driver.mjs run|probe|validate|table ... (see tools/render-bench/README.md)\n");
      return 2;
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().then(
    (code) => process.exit(code),
    (error) => {
      process.stderr.write(`render-bench: ${error.stack || error}\n`);
      process.exit(2);
    },
  );
}

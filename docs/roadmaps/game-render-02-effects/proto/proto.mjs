import * as PIXI from "pixi.js";
import { Engine } from "@babylonjs/core/Engines/engine.js";
import { EffectRenderer, EffectWrapper } from "@babylonjs/core/Materials/effectRenderer.js";
import { ThinTexture } from "@babylonjs/core/Materials/Textures/thinTexture.js";
import { RawTexture } from "@babylonjs/core/Materials/Textures/rawTexture.js";
import { Scene } from "@babylonjs/core/scene.js";
import "@babylonjs/core/Shaders/postprocess.vertex.js";
import { lattice, fractal } from "./ref.mjs";

const W = 160, H = 96;
const ground = [34, 40, 49];
// Shapes: [kind, x, y, w, h, [r,g,b]]
const glyph = [["rect", 40, 30, 32, 32, [80, 140, 230]], ["rect", 44, 34, 24, 24, [60, 64, 72]], ["ellipse", 50, 40, 12, 12, [240, 240, 240]],
               ["rect", 100, 34, 24, 24, [235, 80, 70]], ["rect", 104, 38, 16, 16, [60, 64, 72]]];
const sel = [255, 214, 0];
const P = { r: 2, sigma: 1.5, glowOpacity: 0.85, seed: 7, freq: 0.09, octaves: 2, N: 20, T: 9, E: 2, edge: [255, 140, 0] };

// ---------------- the effect graphs, as GLSL ES 3.00 bodies (pass list) ----------------
const lat = lattice(P.seed);
const latticeData = new Float32Array(256 * 4);
for (let i = 0; i < 256; i++) { latticeData[i * 4] = lat.sel[i]; latticeData[i * 4 + 1] = lat.grad[3][i * 2]; latticeData[i * 4 + 2] = lat.grad[3][i * 2 + 1]; }
const gauss = (s) => { const r = Math.ceil(3 * s); const w = []; let t = 0; for (let i = -r; i <= r; i++) { const v = Math.exp(-(i * i) / (2 * s * s)); w.push(v); t += v; } return w.map(v => v / t); };
const glsl = {
  dilate: (dx, dy, r) => `vec4 fx(vec2 p){ float a = 0.0; for (int i = -${r}; i <= ${r}; i++) a = max(a, src(p + vec2(float(i) * ${dx}.0, float(i) * ${dy}.0)).a); return vec4(0.0, 0.0, 0.0, a); }`,
  blur: (dx, dy, s) => { const w = gauss(s); const r = (w.length - 1) / 2; return `const float W[${w.length}] = float[](${w.map(v => v.toFixed(9)).join(",")});
vec4 fx(vec2 p){ float a = 0.0; for (int i = -${r}; i <= ${r}; i++) a += W[i + ${r}] * src(p + vec2(float(i) * ${dx}.0, float(i) * ${dy}.0)).a; return vec4(0.0, 0.0, 0.0, a); }` },
  tint: (c, o) => `vec4 fx(vec2 p){ float a = src(p).a * ${o.toFixed(6)}; return vec4(vec3(${c.map(v => (v / 255).toFixed(6)).join(",")}) * a, a); }`,
  matrix: (m) => `vec4 fx(vec2 p){ vec4 c = src(p); if (c.a <= 0.0) return vec4(0.0); vec3 u = c.rgb / c.a; vec4 v = vec4(u, c.a);
  vec4 o = vec4(dot(vec4(${m.slice(0, 4).join(",")}), v) + ${m[4]}, dot(vec4(${m.slice(5, 9).join(",")}), v) + ${m[9]}, dot(vec4(${m.slice(10, 14).join(",")}), v) + ${m[14]}, dot(vec4(${m.slice(15, 19).join(",")}), v) + ${m[19]});
  o = clamp(o, 0.0, 1.0); return vec4(o.rgb * o.a, o.a); }`,
  teleport: () => `
float sc(float t){ return t * t * (3.0 - 2.0 * t); }
vec4 L(int i){ return texelFetch(uLattice, ivec2(i & 255, 0), 0); }
float noise2(vec2 v){
  vec2 t = v + 4096.0; ivec2 b0 = ivec2(floor(t)) & 255; ivec2 b1 = (b0 + 1) & 255; vec2 r0 = t - floor(t); vec2 r1 = r0 - 1.0;
  int i = int(L(b0.x).x); int j = int(L(b1.x).x);
  vec4 q00 = L(int(L(i + b0.y).x)); vec4 q10 = L(int(L(j + b0.y).x)); vec4 q01 = L(int(L(i + b1.y).x)); vec4 q11 = L(int(L(j + b1.y).x));
  float sx = sc(r0.x), sy = sc(r0.y);
  float a = mix(r0.x * q00.y + r0.y * q00.z, r1.x * q10.y + r0.y * q10.z, sx);
  float b = mix(r0.x * q01.y + r1.y * q01.z, r1.x * q11.y + r1.y * q11.z, sx);
  return mix(a, b, sy);
}
float fractalNoise(vec2 p){ float s = 0.0; float f = ${P.freq}; float r = 1.0; for (int o = 0; o < ${P.octaves}; o++){ s += noise2(p * f) / r; f *= 2.0; r *= 2.0; } return clamp((s + 1.0) * 0.5, 0.0, 1.0); }
vec4 fx(vec2 p){ vec4 c = src(p); float n = floor(fractalNoise(screen(p)) * 255.0 + 0.5) / 255.0; float k = min(floor(n * ${P.N}.0), ${P.N - 1}.0);
  if (k < ${P.T}.0) return vec4(0.0); if (k < ${P.T + P.E}.0) return vec4(vec3(${P.edge.map(v => (v / 255).toFixed(6)).join(",")}) * c.a, c.a); return c; }`
};
const desatM = (() => { const s = 0, d = 0.8; const m = [0.213 + 0.787 * s, 0.715 - 0.715 * s, 0.072 - 0.072 * s, 0.213 - 0.213 * s, 0.715 + 0.285 * s, 0.072 - 0.072 * s, 0.213 - 0.213 * s, 0.715 - 0.715 * s, 0.072 + 0.928 * s];
  return [m[0] * d, m[1] * d, m[2] * d, 0, 0, m[3] * d, m[4] * d, m[5] * d, 0, 0, m[6] * d, m[7] * d, m[8] * d, 0, 0, 0, 0, 0, 1, 0]; })();
const effects = {
  outline: [glsl.dilate(1, 0, P.r), glsl.dilate(0, 1, P.r), glsl.tint(sel, 1)],
  glow: [glsl.dilate(1, 0, P.r), glsl.dilate(0, 1, P.r), glsl.blur(1, 0, P.sigma), glsl.blur(0, 1, P.sigma), glsl.tint(sel, P.glowOpacity)],
  desat: [glsl.matrix(desatM)],
  teleport: [glsl.teleport()],
};
const svgFilter = {
  outline: `<feMorphology in="SourceAlpha" operator="dilate" radius="${P.r}"/><feFlood flood-color="rgb(${sel})"/><feComposite operator="in" in2="r0"/>`.replace('radius', 'result="r0" radius'),
  glow: `<feMorphology in="SourceAlpha" operator="dilate" radius="${P.r}" result="d"/><feGaussianBlur in="d" stdDeviation="${P.sigma}" result="b"/><feFlood flood-color="rgb(${sel})" flood-opacity="${P.glowOpacity}"/><feComposite operator="in" in2="b"/>`,
  desat: `<feColorMatrix type="matrix" values="${desatM.join(" ")}"/>`,
  teleport: (() => { const tv = (pred) => Array.from({ length: P.N }, (_, k) => pred(k) ? 1 : 0).join(" ");
    return `<feTurbulence type="fractalNoise" baseFrequency="${P.freq}" numOctaves="${P.octaves}" seed="${P.seed}" result="noise"/>
<feComponentTransfer in="noise" result="vis"><feFuncA type="discrete" tableValues="${tv(k => k >= P.T)}"/></feComponentTransfer>
<feComponentTransfer in="noise" result="core"><feFuncA type="discrete" tableValues="${tv(k => k >= P.T + P.E)}"/></feComponentTransfer>
<feComposite in="SourceGraphic" in2="core" operator="in" result="body"/>
<feComposite in="vis" in2="core" operator="out" result="band"/>
<feFlood flood-color="rgb(${P.edge})" result="fl"/>
<feComposite in="fl" in2="band" operator="in" result="ef"/>
<feComposite in="ef" in2="SourceAlpha" operator="in" result="eos"/>
<feMerge><feMergeNode in="eos"/><feMergeNode in="body"/></feMerge>` })(),
};
// A case: layers drawn in order; each layer = shapes + optional effect. Underlay effects use the glyph's shapes.
const cases = {
  none: [{ shapes: glyph }],
  outline: [{ shapes: glyph.slice(0, 3), effect: "outline" }, { shapes: glyph }],
  glow: [{ shapes: glyph.slice(0, 3), effect: "glow" }, { shapes: glyph }],
  desat: [{ shapes: glyph.slice(0, 3), effect: "desat" }, { shapes: glyph.slice(3) }],
  teleport: [{ shapes: glyph.slice(0, 3), effect: "teleport" }, { shapes: glyph.slice(3) }],
};

// ---------------- SVG ----------------
function svgOf(layers) {
  const shape = ([k, x, y, w, h, c]) => k === "rect" ? `<rect x="${x}" y="${y}" width="${w}" height="${h}" fill="rgb(${c})"/>` : `<ellipse cx="${x + w / 2}" cy="${y + h / 2}" rx="${w / 2}" ry="${h / 2}" fill="rgb(${c})"/>`;
  let defs = "", body = "";
  layers.forEach((l, i) => {
    if (l.effect) { defs += `<filter id="f${i}" filterUnits="userSpaceOnUse" primitiveUnits="userSpaceOnUse" x="0" y="0" width="${W}" height="${H}" color-interpolation-filters="sRGB">${svgFilter[l.effect]}</filter>`; body += `<g filter="url(#f${i})">${l.shapes.map(shape).join("")}</g>`; }
    else body += l.shapes.map(shape).join("");
  });
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" shape-rendering="crispEdges"><defs>${defs}</defs><rect width="${W}" height="${H}" fill="rgb(${ground})"/>${body}</svg>`;
}
async function rasterSvg(text) {
  const img = new Image(); await new Promise((res, rej) => { img.onload = res; img.onerror = rej; img.src = "data:image/svg+xml;charset=utf-8," + encodeURIComponent(text); });
  const c = document.createElement("canvas"); c.width = W; c.height = H; const g = c.getContext("2d"); g.drawImage(img, 0, 0); return g.getImageData(0, 0, W, H).data;
}
function readCanvas(cv) { const t = document.createElement("canvas"); t.width = W; t.height = H; const g = t.getContext("2d"); g.drawImage(cv, 0, 0); return g.getImageData(0, 0, W, H).data; }

// ---------------- Pixi ----------------
const pixiVertex = PIXI.defaultFilterVert;
const pixiPrelude = `in vec2 vTextureCoord; out vec4 finalColor; uniform sampler2D uTexture; uniform sampler2D uLattice; uniform vec4 uInputSize; uniform vec4 uOutputFrame;
vec4 src(vec2 p){ return texture(uTexture, p * uInputSize.zw); }
vec2 screen(vec2 p){ return p + uOutputFrame.xy; }
`;
const pixiMain = `void main(){ finalColor = fx(vTextureCoord * uInputSize.xy); }`;
let latTex;
async function pixiRender(layers) {
  const app = new PIXI.Application();
  await app.init({ width: W, height: H, background: `rgb(${ground})`, antialias: false, preference: "webgl", preserveDrawingBuffer: true, resolution: 1, autoDensity: false });
  latTex ??= new PIXI.Texture({ source: new PIXI.BufferImageSource({ resource: latticeData, width: 256, height: 1, format: "rgba32float", scaleMode: "nearest" }) });
  for (const l of layers) {
    const c = new PIXI.Container();
    for (const [k, x, y, w, h, col] of l.shapes) { const g = new PIXI.Graphics(); if (k === "rect") g.rect(x, y, w, h); else g.ellipse(x + w / 2, y + h / 2, w / 2, h / 2); g.fill({ color: (col[0] << 16) | (col[1] << 8) | col[2] }); c.addChild(g); }
    if (l.effect) {
      c.filters = effects[l.effect].map(body => new PIXI.Filter({ glProgram: PIXI.GlProgram.from({ vertex: pixiVertex, fragment: pixiPrelude + body + pixiMain, name: "fx" }), resources: { uLattice: latTex.source }, padding: 8, resolution: 1, antialias: "off" }));
      c.filterArea = new PIXI.Rectangle(0, 0, W, H);
    }
    app.stage.addChild(c);
  }
  app.render();
  const px = readCanvas(app.canvas); app.destroy(true); return px;
}

// ---------------- Babylon ----------------
const babPrelude = `precision highp float; varying vec2 vUV; uniform sampler2D textureSampler; uniform sampler2D uLattice; uniform vec2 uSize;
vec4 src(vec2 p){ return texture2D(textureSampler, p / uSize); }
vec2 screen(vec2 p){ return vec2(p.x, uSize.y - p.y); }
`;
const babMain = `void main(void){ gl_FragColor = fx(vUV * uSize); }`;
// Babylon: shapes rasterised by a flat-colour EffectWrapper per shape into a render target (unlit, exact colours), passes
// through EffectWrappers, then composited by premultiplied "over" into the output.
async function babylonRender(layers) {
  const cv = document.createElement("canvas"); cv.width = W; cv.height = H; document.body.appendChild(cv);
  const engine = new Engine(cv, false, { preserveDrawingBuffer: true, premultipliedAlpha: false, antialias: false });
  const scene = new Scene(engine);
  const er = new EffectRenderer(engine);
  const rt = () => engine.createRenderTargetTexture({ width: W, height: H }, { generateMipMaps: false, generateDepthBuffer: false, samplingMode: 1, type: 0 });
  const ready = (w) => new Promise(res => { const tick = () => w.isReady() ? res() : requestAnimationFrame(tick); tick(); });
  const shapeFx = new EffectWrapper({ engine, name: "shape", fragmentShader: `precision highp float; varying vec2 vUV; uniform vec2 uSize; uniform vec4 uBox; uniform vec3 uCol; uniform float uRound;
void main(void){ vec2 p = vec2(vUV.x * uSize.x, (1.0 - vUV.y) * uSize.y); vec2 c = uBox.xy + uBox.zw * 0.5; vec2 d = (p - c) / (uBox.zw * 0.5);
 bool inside = uRound > 0.5 ? dot(d, d) <= 1.0 : (p.x >= uBox.x && p.x <= uBox.x + uBox.z && p.y >= uBox.y && p.y <= uBox.y + uBox.w);
 if (!inside) discard; gl_FragColor = vec4(uCol, 1.0); }`, uniformNames: ["uSize", "uBox", "uCol", "uRound"] });
  const over = new EffectWrapper({ engine, name: "over", fragmentShader: `precision highp float; varying vec2 vUV; uniform sampler2D textureSampler; uniform sampler2D uBase;
void main(void){ vec4 s = texture2D(textureSampler, vUV); vec4 b = texture2D(uBase, vUV); gl_FragColor = s + b * (1.0 - s.a); }`, samplerNames: ["uBase"] });
  const lat = new RawTexture(latticeData, 256, 1, 5, scene, false, false, 1, 1);
  const wrappers = {}; for (const [n, ps] of Object.entries(effects)) wrappers[n] = ps.map(body => new EffectWrapper({ engine, name: n, fragmentShader: babPrelude + body + babMain, uniformNames: ["uSize"], samplerNames: ["uLattice"] }));
  await ready(shapeFx); await ready(over); for (const ws of Object.values(wrappers)) for (const w of ws) await ready(w);
  const tex = (t) => new ThinTexture(t.texture);
  let base = rt();
  const clear = (target, col) => { engine.bindFramebuffer(target); engine.clear({ r: col[0], g: col[1], b: col[2], a: col[3] }, true, false, false); engine.unBindFramebuffer(target); };
  clear(base, [...ground.map(v => v / 255), 1]);
  for (const l of layers) {
    let layer = rt(); clear(layer, [0, 0, 0, 0]);
    for (const [k, x, y, w, h, col] of l.shapes) {
      const o = shapeFx.onApplyObservable.add(() => { const e = shapeFx.effect; e.setFloat2("uSize", W, H); e.setFloat4("uBox", x, y, w, h); e.setFloat3("uCol", col[0] / 255, col[1] / 255, col[2] / 255); e.setFloat("uRound", k === "rect" ? 0 : 1); });
      er.render(shapeFx, layer); shapeFx.onApplyObservable.remove(o);
    }
    if (l.effect) for (const w of wrappers[l.effect]) {
      const out = rt(); const input = layer;
      const o = w.onApplyObservable.add(() => { w.effect.setFloat2("uSize", W, H); w.effect.setTexture("textureSampler", tex(input)); w.effect.setTexture("uLattice", lat); });
      er.render(w, out); w.onApplyObservable.remove(o); layer = out;
    }
    const merged = rt(); const s = layer, b = base;
    const o = over.onApplyObservable.add(() => { over.effect.setTexture("textureSampler", tex(s)); over.effect.setTexture("uBase", tex(b)); });
    er.render(over, merged); over.onApplyObservable.remove(o); base = merged;
  }
  const fb = base; const o2 = over.onApplyObservable.add(() => { const t = rt(); clear(t, [0, 0, 0, 0]); over.effect.setTexture("textureSampler", tex(fb)); over.effect.setTexture("uBase", tex(t)); });
  er.render(over, null); over.onApplyObservable.remove(o2);
  const px = readCanvas(cv); engine.dispose(); cv.remove(); return px;
}

// ---------------- compare ----------------
function diff(a, b) { let max = 0, n = 0, hist = {}; for (let i = 0; i < W * H; i++) { let d = 0; for (let c = 0; c < 3; c++) d = Math.max(d, Math.abs(a[i * 4 + c] - b[i * 4 + c])); if (d > 0) n++; max = Math.max(max, d); const bucket = d === 0 ? 0 : d <= 2 ? 2 : d <= 8 ? 8 : d <= 32 ? 32 : 255; hist[bucket] = (hist[bucket] || 0) + 1; } return { max, n, hist }; }
function worst(a, b, k = 5) { const r = []; for (let i = 0; i < W * H; i++) { let d = 0; for (let c = 0; c < 3; c++) d = Math.max(d, Math.abs(a[i * 4 + c] - b[i * 4 + c])); if (d > 0) r.push([d, i % W, Math.floor(i / W), [...a.slice(i * 4, i * 4 + 3)], [...b.slice(i * 4, i * 4 + 3)]]); } return r.sort((x, y) => y[0] - x[0]).slice(0, k); }
// CPU noise check against the SVG: which sample convention matches feTurbulence's alpha best.
async function noiseConvention() {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}"><filter id="n" filterUnits="userSpaceOnUse" x="0" y="0" width="${W}" height="${H}" color-interpolation-filters="sRGB"><feTurbulence type="fractalNoise" baseFrequency="${P.freq}" numOctaves="${P.octaves}" seed="${P.seed}"/><feColorMatrix type="matrix" values="0 0 0 1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 0 1"/></filter><rect width="${W}" height="${H}" filter="url(#n)"/></svg>`;
  // the colour matrix moves A into RGB with A' = 1: the canvas holds the alpha noise as grey.
  const px = await rasterSvg(svg); const L = lattice(P.seed);
  const out = {};
  for (const [name, off] of [["centre", 0.5], ["corner", 0]]) {
    let max = 0, sum = 0; for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) { const n = fractal(L, 3, x + off, y + off, P.freq, P.freq, P.octaves) * 255; const d = Math.abs(n - px[(y * W + x) * 4]); max = Math.max(max, d); sum += d; }
    out[name] = { max: +max.toFixed(2), mean: +(sum / (W * H)).toFixed(3) };
  }
  return out;
}
window.runProto = async () => {
  const report = { noise: await noiseConvention(), cases: {} };
  for (const [name, layers] of Object.entries(cases)) {
    const svg = await rasterSvg(svgOf(layers)); const pixi = await pixiRender(layers); let bab;
    try { bab = await babylonRender(layers); } catch (e) { report.cases[name] = { error: String(e && e.stack || e) }; continue; }
    report.cases[name] = { svgPixi: diff(svg, pixi), svgBabylon: diff(svg, bab), pixiBabylon: diff(pixi, bab), worstSvgPixi: worst(svg, pixi, 4), worstPixiBab: worst(pixi, bab, 3) };
  }
  const gl = document.createElement("canvas").getContext("webgl2"); const ext = gl.getExtension("WEBGL_debug_renderer_info");
  report.renderer = ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER);
  return report;
};

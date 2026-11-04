// CPU references for the prototype: feTurbulence (Filter Effects 1 reference code), dilation, gaussian, colour matrix.
const BSize = 256, BM = 255, PerlinN = 4096;
const RAND_m = 2147483647, RAND_a = 16807, RAND_q = 127773, RAND_r = 2836;
function setupSeed(s) { if (s <= 0) s = -(s % (RAND_m - 1)) + 1; if (s > RAND_m - 1) s = RAND_m - 1; return s; }
function rnd(s) { let r = RAND_a * (s % RAND_q) - RAND_r * Math.trunc(s / RAND_q); if (r <= 0) r += RAND_m; return r; }
export function lattice(seed) {
  const sel = new Int32Array(BSize + BSize + 2);
  const grad = [0, 1, 2, 3].map(() => new Float64Array((BSize + BSize + 2) * 2));
  let s = setupSeed(seed), i = 0;
  for (let k = 0; k < 4; k++) {
    for (i = 0; i < BSize; i++) {
      sel[i] = i;
      for (let j = 0; j < 2; j++) { s = rnd(s); grad[k][i * 2 + j] = ((s % (BSize + BSize)) - BSize) / BSize; }
      const g0 = grad[k][i * 2], g1 = grad[k][i * 2 + 1], n = Math.sqrt(g0 * g0 + g1 * g1);
      grad[k][i * 2] = g0 / n; grad[k][i * 2 + 1] = g1 / n;
    }
  }
  while (--i) { const k = sel[i]; s = rnd(s); const j = s % BSize; sel[i] = sel[j]; sel[j] = k; }
  for (i = 0; i < BSize + 2; i++) {
    sel[BSize + i] = sel[i];
    for (let k = 0; k < 4; k++) for (let j = 0; j < 2; j++) grad[k][(BSize + i) * 2 + j] = grad[k][i * 2 + j];
  }
  return { sel, grad };
}
const sc = t => t * t * (3 - 2 * t), lerp = (t, a, b) => a + t * (b - a);
function noise2(L, ch, x, y) {
  let t = x + PerlinN; let bx0 = Math.trunc(t) & BM, bx1 = (bx0 + 1) & BM; const rx0 = t - Math.trunc(t), rx1 = rx0 - 1;
  t = y + PerlinN; let by0 = Math.trunc(t) & BM, by1 = (by0 + 1) & BM; const ry0 = t - Math.trunc(t), ry1 = ry0 - 1;
  const i = L.sel[bx0], j = L.sel[bx1];
  const b00 = L.sel[i + by0], b10 = L.sel[j + by0], b01 = L.sel[i + by1], b11 = L.sel[j + by1];
  const sx = sc(rx0), sy = sc(ry0), g = L.grad[ch];
  let u = rx0 * g[b00 * 2] + ry0 * g[b00 * 2 + 1], v = rx1 * g[b10 * 2] + ry0 * g[b10 * 2 + 1];
  const a = lerp(sx, u, v);
  u = rx0 * g[b01 * 2] + ry1 * g[b01 * 2 + 1]; v = rx1 * g[b11 * 2] + ry1 * g[b11 * 2 + 1];
  const b = lerp(sx, u, v);
  return lerp(sy, a, b);
}
export function fractal(L, ch, px, py, fx, fy, octaves) {
  let sum = 0, ratio = 1;
  for (let o = 0; o < octaves; o++) { sum += noise2(L, ch, px * fx, py * fy) / ratio; fx *= 2; fy *= 2; ratio *= 2; }
  return Math.min(1, Math.max(0, (sum + 1) / 2));
}

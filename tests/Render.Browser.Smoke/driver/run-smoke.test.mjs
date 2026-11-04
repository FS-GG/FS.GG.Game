// Finite protocol control, without Chromium: execute the actual driver with a Playwright boundary double.
// Chromium 156.0.8078.0 PageHandler::CaptureScreenshot/ScreenshotCaptured saves/restores the issuing
// session's EmulationHandler params; SetDeviceMetricsOverride skips unchanged per-session params.
// https://github.com/chromium/chromium/blob/156.0.8078.0/content/browser/devtools/protocol/page_handler.cc
// https://github.com/chromium/chromium/blob/156.0.8078.0/content/browser/devtools/protocol/emulation_handler.cc
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const source = readFileSync(new URL('./run-smoke.mjs', import.meta.url), 'utf8')
  .replace(/^import .*;\n/gm, '')
  .replace('createRequire(import.meta.url)', 'createRequire()');
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;

for (const screenshotFirst of [true, false]) {
  test(`screenshots preserve metrics, screenshot-first=${screenshotFirst}`, async () => {
    let input, onConsole, ratio = 1, width = 1024, sessions = 0, closed = false;
    const lines = [];
    const page = {
      on(event, callback) { if (event === 'console') onConsole = callback; },
      async exposeFunction(name, callback) { assert.equal(name, '__fsggInput'); input = callback; },
      context() { return { async newCDPSession() {
        sessions++;
        let own = null;
        return { async send(method, params) {
          if (method === 'Emulation.setDeviceMetricsOverride') {
            if (JSON.stringify(own) !== JSON.stringify(params)) {
              own = { ...params }; ratio = own.deviceScaleFactor; width = own.width;
            }
            return {};
          }
          assert.equal(method, 'Page.captureScreenshot');
          // A clipped capture temporarily emulates its issuing session's metrics, then restores them.
          const captureRatio = own?.deviceScaleFactor ?? 1;
          ratio = captureRatio;
          return { data: JSON.stringify({ ratio: captureRatio, width: params.clip.width * captureRatio }) };
        } };
      } }; },
      async evaluate() { return ratio; },
      async goto() {
        const shot = async (expected) => {
          const data = JSON.parse(await input({ kind: 'shot', x: 0, y: 0, width: 120, height: 80 }));
          assert.equal(data.ratio, expected, 'capture must use requested DPR');
          assert.equal(data.width, 120 * expected, 'capture retains physical pixel scale');
          assert.equal(ratio, expected, 'capture must restore requested DPR');
        };
        if (screenshotFirst) await shot(1);
        for (const [factor, viewport] of [[2, 1024], [2, 1024], [1, 800], [2, 1024]]) {
          assert.equal(await input({ kind: 'scale', factor, width: viewport }), factor);
          assert.equal(width, viewport);
          await shot(factor);
        }
        assert.equal(sessions, 1, 'metrics and screenshots must share their session state');
        onConsole({ text: () => 'RENDER-SMOKE-RESULT PASS protocol control' });
      }
    };
    const chromium = { async launch() { return { async newPage() { return page; }, async close() { closed = true; } }; } };
    const fs = { createWriteStream() { return { write(line) { lines.push(line); }, end(callback) { callback(); } }; } };
    const run = new AsyncFunction('createRequire', 'fs', 'path', 'pathToFileURL', 'process', source);
    await run(() => () => ({ chromium }), fs, path, pathToFileURL,
      { argv: ['node', 'driver', '/finite/index.html', '/unused/chrome', '/unused/log', '1'], env: {} });
    assert.equal(closed, true);
    assert.ok(lines.some(line => line.includes('RENDER-SMOKE-RESULT PASS')));
    assert.ok(!lines.some(line => line.includes('timeout')));
  });
}

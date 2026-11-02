// Real-input driver for scripts/test-render-browser-host.sh (FS.GG.Game#751).
//
// Loads the smoke page in the given Chromium through Playwright and exposes `window.__fsggInput`, which performs the
// page's input requests with Playwright's trusted `page.mouse` and `page.keyboard`: the interaction checks need real
// pointer and keyboard events and real focus, never synthesised DOM events. Every console line is written to the
// log the script parses (`CONSOLE ...`, page errors as `Uncaught ...`), and the driver ends once the page reports
// RENDER-SMOKE-RESULT or the timeout passes.
//
//   node run-smoke.mjs <index.html> <chromium binary> <log file> <timeout seconds> [chromium flags...]
//   (RENDER_SMOKE_PART=all|main|layout selects the page's part.)
import { createRequire } from "node:module";
import { pathToFileURL } from "node:url";
import fs from "node:fs";
import path from "node:path";

const require = createRequire(import.meta.url);
const { chromium } = require("playwright-core");
const [pagePath, executablePath, logFile, timeoutSeconds, ...flags] = process.argv.slice(2);
const log = fs.createWriteStream(logFile);
const write = (line) => log.write(line + "\n");

// Playwright starts the browser headless itself.
const args = [...flags.filter((flag) => !flag.startsWith("--headless")), "--allow-file-access-from-files"];
const browser = await chromium.launch({ executablePath, headless: true, args });
const page = await browser.newPage({ viewport: { width: 1024, height: 768 } });
let finish;
const done = new Promise((resolve) => (finish = resolve));

page.on("console", (message) => {
  const text = message.text();
  write("CONSOLE " + text);
  if (text.includes("RENDER-SMOKE-RESULT")) finish(true);
});
page.on("pageerror", (error) => write("Uncaught " + error));

const keys = { " ": "Space" };
// The CDP session that holds the device-metrics override; it must outlive the request.
let emulation = null;
// The CDP session that dispatches touch input (FS.GG.Game#788).
let touch = null;
// The CDP session that captures presented pixels (FS.GG.Game#788).
let shots = null;
await page.exposeFunction("__fsggInput", async (request) => {
  switch (request.kind) {
    case "click":
      await page.mouse.click(request.x, request.y);
      break;
    case "move":
      await page.mouse.move(request.x, request.y);
      break;
    case "key":
      await page.keyboard.press(keys[request.key] ?? request.key);
      break;
    // FS.GG.Game#788, the input host's smoke and the kit's input section: a secondary press, a touch tap and long-press
    // (CDP touch events with touch emulation on, so Chromium produces pointer events of type "touch"), a press with a
    // modifier held, and a held key (Playwright sends repeat: true for every keydown of a key already down).
    case "rightClick":
      await page.mouse.click(request.x, request.y, { button: "right" });
      break;
    case "touchHold": {
      touch ??= await page.context().newCDPSession(page);
      await touch.send("Emulation.setTouchEmulationEnabled", { enabled: true, maxTouchPoints: 1 });
      await touch.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: [{ x: request.x, y: request.y }] });
      await new Promise((resolve) => setTimeout(resolve, request.ms));
      await touch.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
      await touch.send("Emulation.setTouchEmulationEnabled", { enabled: false });
      break;
    }
    case "touchTap": {
      touch ??= await page.context().newCDPSession(page);
      await touch.send("Emulation.setTouchEmulationEnabled", { enabled: true, maxTouchPoints: 1 });
      await touch.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: [{ x: request.x, y: request.y }] });
      await touch.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
      await touch.send("Emulation.setTouchEmulationEnabled", { enabled: false });
      break;
    }
    case "clickWith":
      await page.keyboard.down(request.modifier);
      await page.mouse.click(request.x, request.y);
      await page.keyboard.up(request.modifier);
      break;
    case "keyHold": {
      const key = keys[request.key] ?? request.key;
      for (let i = 0; i < request.count; i++) await page.keyboard.down(key);
      await page.keyboard.up(key);
      break;
    }
    case "shot": {
      // FS.GG.Game#788: the PRESENTED pixels of a page region (what the compositor shows, at the device pixel ratio), as
      // a base64 PNG the page decodes; unlike reading a canvas's backing store, it sees a scaled or blurred image.
      // Through CDP, so the device-metrics override the "scale" request sets (DPR 2) is honoured: Playwright's own
      // screenshot scale follows the context's device scale factor, not the override.
      shots ??= emulation ?? (await page.context().newCDPSession(page));
      const shot = await shots.send("Page.captureScreenshot", { format: "png", clip: { x: request.x, y: request.y, width: request.width, height: request.height, scale: 1 } });
      return shot.data;
    }
    case "scale": {
      // The page's device pixel ratio (FS.GG.Game#753), so one page can run a scenario at DPR 1 and 2. CSS pixels, and so
      // the mouse coordinates, are unchanged. The session stays attached: detaching it drops the override. The driver
      // answers with the ratio the page then has, so the page can assert it.
      emulation ??= await page.context().newCDPSession(page);
      // FS.GG.Game#788: an optional viewport width, so a page media query can be turned off and on.
      await emulation.send("Emulation.setDeviceMetricsOverride", { width: request.width ?? 1024, height: 768, deviceScaleFactor: request.factor, mobile: false });
      return await page.evaluate(() => window.devicePixelRatio);
    }
    case "screenshot": {
      // FS.GG.Game#789: an element screenshot for the PR's evidence, only when RENDER_SMOKE_SCREENSHOTS names a
      // directory; otherwise nothing is written and the page is told so.
      const dir = process.env.RENDER_SMOKE_SCREENSHOTS;
      if (!dir || !/^[A-Za-z0-9._-]+\.png$/.test(request.name)) return false;
      fs.mkdirSync(dir, { recursive: true });
      await page.locator(request.selector).screenshot({ path: path.join(dir, request.name) });
      return true;
    }
    default:
      throw new Error("unknown input request " + JSON.stringify(request));
  }
});

// FS.GG.Game#788: RENDER_SMOKE_PART selects the page's part (`all`, `main` or `layout`; see Program.fs).
const pageUrl = pathToFileURL(pagePath);
if (process.env.RENDER_SMOKE_PART) pageUrl.searchParams.set("part", process.env.RENDER_SMOKE_PART);
const started = Date.now();
await page.goto(pageUrl.href);
let timeout;
let reported;
try {
  reported = await Promise.race([done, new Promise((resolve) => { timeout = setTimeout(() => resolve(false), Number(timeoutSeconds) * 1000); })]);
} finally {
  // A successful page must not retain an idle Node process until the unused page deadline.
  clearTimeout(timeout);
}
if (!reported) write("DRIVER timeout after " + timeoutSeconds + "s");
else write("DRIVER page-seconds " + ((Date.now() - started) / 1000).toFixed(1) + " of " + timeoutSeconds);
await browser.close();
await new Promise((resolve) => log.end(resolve));

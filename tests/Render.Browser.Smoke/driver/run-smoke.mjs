// Real-input driver for scripts/test-render-browser-host.sh (FS.GG.Game#751).
//
// Loads the smoke page in the given Chromium through Playwright and exposes `window.__fsggInput`, which performs the
// page's input requests with Playwright's trusted `page.mouse` and `page.keyboard`: the interaction checks need real
// pointer and keyboard events and real focus, never synthesised DOM events. Every console line is written to the
// log the script parses (`CONSOLE ...`, page errors as `Uncaught ...`), and the driver ends once the page reports
// RENDER-SMOKE-RESULT or the timeout passes.
//
//   node run-smoke.mjs <index.html> <chromium binary> <log file> <timeout seconds> [chromium flags...]
import { createRequire } from "node:module";
import { pathToFileURL } from "node:url";
import fs from "node:fs";

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
    case "scale": {
      // The page's device pixel ratio (FS.GG.Game#753), so one page can run a scenario at DPR 1 and 2. CSS pixels, and so
      // the mouse coordinates, are unchanged.
      const session = await page.context().newCDPSession(page);
      await session.send("Emulation.setDeviceMetricsOverride", { width: 1024, height: 768, deviceScaleFactor: request.factor, mobile: false });
      await session.detach();
      break;
    }
    default:
      throw new Error("unknown input request " + JSON.stringify(request));
  }
});

await page.goto(pathToFileURL(pagePath).href);
const reported = await Promise.race([done, new Promise((resolve) => setTimeout(() => resolve(false), Number(timeoutSeconds) * 1000))]);
if (!reported) write("DRIVER timeout after " + timeoutSeconds + "s");
await browser.close();
await new Promise((resolve) => log.end(resolve));

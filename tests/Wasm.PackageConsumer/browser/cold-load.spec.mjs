import { test, expect } from "@playwright/test";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

// Cold first loads as a product receiver performs them: a fresh browser profile
// (no HTTP, module or compile cache), a fresh package Worker, its readiness
// report, then load/initialize/advice under the unchanged 250 ms policy budget.
// Page-clock timings are measurements; the F# producer alone decides deadlines.
const samples = Number(process.env.WASM_COLD_LOAD_SAMPLES ?? 20);
const startupBound = 5000, policyBudget = 250;
const quantile = (values, q) => { const sorted = [...values].sort((a, b) => a - b); return sorted[Math.max(0, Math.ceil(q * sorted.length) - 1)]; };
const round = value => Math.round(value * 10) / 10;

test("cold loads under a hosted-like CPU budget keep visible startup and policy margins", async ({ browser, browserName }, testInfo) => {
  test.setTimeout(30_000 + samples * 10_000);
  expect(Number.isInteger(samples) && samples >= 20, "N >= 20 cold loads").toBe(true);
  const rows = [];
  for (let index = 0; index < samples; index++) {
    const context = await browser.newContext();
    try {
      const page = await context.newPage();
      await page.goto("http://127.0.0.1:41784/sub/app/index.html");
      await expect.poll(() => page.evaluate(() => window.packageConsumerReady === true)).toBe(true);
      rows.push(await page.evaluate(() => window.runColdLoad("c-sc2.wasm")));
      await expect.poll(() => page.workers().length).toBe(0);
    } finally { await context.close(); }
  }
  const summarize = key => {
    const values = rows.map(row => row[key]).filter(Number.isFinite);
    return { n: values.length, min: round(Math.min(...values)), p50: round(quantile(values, 0.5)), p95: round(quantile(values, 0.95)), max: round(Math.max(...values)) };
  };
  const startup = summarize("startupMilliseconds"), policy = summarize("policyLoadMilliseconds");
  const report = {
    schema: "fsgg.wasm.cold-load/v1", browser: browserName, browserVersion: browser.version(), samples,
    cpu: { limit: process.env.WASM_BROWSER_CPU_LIMIT ?? "unrecorded", runnerAvailableParallelism: os.availableParallelism(), pageHardwareConcurrency: rows[0]?.hardwareConcurrency ?? null },
    bounds: { workerStartMilliseconds: startupBound, policyMilliseconds: policyBudget },
    startup, policyLoad: policy, endToEndLoad: summarize("endToEndLoadMilliseconds"),
    initialize: summarize("initializeMilliseconds"), advice: summarize("adviceMilliseconds"),
    margin: { startupMilliseconds: round(startupBound - startup.max), startupBoundOverMax: round(startupBound / startup.max),
      policyMilliseconds: round(policyBudget - policy.max), policyBudgetOverMax: round(policyBudget / policy.max) },
    rows
  };
  const { rows: _, ...summary } = report;
  console.log(JSON.stringify({ stage: "cold-load summary", ...summary }));
  await testInfo.attach(`cold-load-${browserName}.json`, { body: JSON.stringify(report, null, 2), contentType: "application/json" });
  if (process.env.WASM_COLD_LOAD_REPORT_DIR) {
    fs.mkdirSync(process.env.WASM_COLD_LOAD_REPORT_DIR, { recursive: true });
    fs.writeFileSync(path.join(process.env.WASM_COLD_LOAD_REPORT_DIR, `cold-load-${browserName}.json`), JSON.stringify(report, null, 2) + "\n");
  }
  for (const row of rows) {
    expect(row, JSON.stringify(row)).toMatchObject({ loaded: "succeeded", initialized: "succeeded", advice: "succeeded" });
    expect(row.adviceOutput).toEqual([83, 67, 50, 67]);
  }
  expect(startup.n).toBe(samples); expect(policy.n).toBe(samples);
  expect(startup.max, "every Worker reported readiness within its own bound").toBeLessThan(startupBound);
  expect(policy.max, "every policy load completed within the unchanged policy budget").toBeLessThan(policyBudget);
});

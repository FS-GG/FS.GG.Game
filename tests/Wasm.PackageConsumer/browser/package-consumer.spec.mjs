import { test, expect } from "@playwright/test";

test.beforeEach(async ({page}) => {
  await page.goto("http://127.0.0.1:41784/sub/app/index.html");
  await expect.poll(() => page.evaluate(() => window.packageConsumerReady === true)).toBe(true);
});
for (const [name,prefix,version,output] of [
  ["rust-bar.wasm","barc",1,[66,65,82,51]],
  ["rust-sc2.wasm","sc2c",0x10000,[83,67,50,51]],
  ["c-bar.wasm","barc",1,[66,65,82,67]],
  ["c-sc2.wasm","sc2c",0x10000,[83,67,50,67]],
]) test(`${name} calls through package Worker assets`, async ({page}) => {
  const value = await page.evaluate(args => window.runGuest(...args), [name,prefix,version]);
  expect(value.loaded.version).toBe(version);
  expect(value.result).toMatchObject({status:0,output});
  expect(value.shutdown.status).toBe(0);
  expect(value.disposedBefore).toBe(false);
});
test("trap remains inside the package Worker", async ({page}) => {
  await expect(page.evaluate(() => window.runGuest("c-trap.wasm","sc2c",0x10000))).rejects.toThrow();
  await expect(page.evaluate(() => 42)).resolves.toBe(42);
});
test("deadline terminates and repeated disposal is harmless", async ({page}) => {
  const value=await page.evaluate(() => window.runDeadline("c-loop.wasm"));
  expect(value.error).toContain("deadline"); expect(value.disposed).toBe(true);
  await expect(page.evaluate(() => 43)).resolves.toBe(43);
});

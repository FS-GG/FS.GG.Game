// Raw module-Worker conformance runs in all three engine families; a local subset
// may be selected explicitly, while qualification runs the complete roster.
const families = ["chromium", "firefox", "webkit"];
const selected = (process.env.WASM_BROWSER_PROJECTS ?? families.join(",")).split(",");
if (selected.some(name => !families.includes(name))) throw new Error("unknown browser family selection");
export default {
  testDir: ".",
  testMatch: "conformance.spec.mjs",
  workers: 1,
  fullyParallel: false,
  timeout: 15_000,
  use: { headless: true },
  projects: families.filter(name => selected.includes(name)).map(name => ({ name, use: { browserName: name } }))
};

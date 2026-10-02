export default {
  testDir: ".",
  testMatch: "conformance.spec.mjs",
  workers: 1,
  fullyParallel: false,
  timeout: 15_000,
  use: { headless: true }
};

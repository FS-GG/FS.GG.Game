export default {
  testDir: ".",
  testMatch: "package-consumer.spec.mjs",
  workers: 1,
  fullyParallel: false,
  timeout: 15_000,
  use: { headless: true }
};

import { defineConfig, devices } from "@playwright/test";

// The stack under test is started by ./erp verify (compose) and passed in as ERP_BASE_URL.
export default defineConfig({
  testDir: "./specs",
  timeout: 30_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [["list"], ["json", { outputFile: process.env.ERP_E2E_REPORT ?? "test-results/report.json" }]],
  use: {
    baseURL: process.env.ERP_BASE_URL ?? "http://localhost:8080",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    locale: "en-US",
    ...(process.env.ERP_CHROMIUM ? { launchOptions: { executablePath: process.env.ERP_CHROMIUM } } : {}),
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});

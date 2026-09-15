import { defineConfig } from "@playwright/test";

// Runs against an already running stack (see TESTING.md); not part of `npm test`.
export default defineConfig({
  testDir: "e2e",
  timeout: 300_000,
  retries: 0,
  reporter: "list",
  use: { headless: true, ignoreHTTPSErrors: true }
});

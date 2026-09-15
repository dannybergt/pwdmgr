import { expect, test, type Page, type Request } from "@playwright/test";

/**
 * Golden path against a running compose stack (E2E_BASE_URL, default https://localhost:8443):
 * login → enrol → create a secret → reload → unlock → read it back — while a route interceptor
 * proves that neither the passphrase nor the secret plaintext ever leaves the browser.
 * Uses the dev seed user (E2E_TENANT / E2E_EMAIL / E2E_PASSWORD).
 */
const baseUrl = process.env.E2E_BASE_URL ?? "https://localhost:8443";
const tenant = process.env.E2E_TENANT ?? "dev";
const email = process.env.E2E_EMAIL ?? "admin@dev.local";
const loginSecret = process.env.E2E_PASSWORD ?? "dev-only-admin-password";
// Deterministic so repeated runs against the same dev database can unlock again.
const passphrase = process.env.E2E_PASSPHRASE ?? "e2e dev-seed passphrase 2026";
const marker = `MARKER-${Date.now()}-plaintext-secret`;

test.use({ baseURL: baseUrl, ignoreHTTPSErrors: true });

function watchRequests(page: Page): Request[] {
  const requests: Request[] = [];
  page.on("request", (request) => {
    if (request.url().includes("/api/")) {
      requests.push(request);
    }
  });
  return requests;
}

async function login(page: Page) {
  await page.goto("/login");
  await page.getByLabel("Tenant").fill(tenant);
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Password").fill(loginSecret);
  await page.getByRole("button", { name: "Sign in" }).click();
}

test("login → enrol or unlock → create → reload → unlock → read; nothing sensitive on the wire", async ({ page }) => {
  const requests = watchRequests(page);
  const consoleErrors: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error") {
      consoleErrors.push(message.text());
    }
  });

  await login(page);
  const heading = page.getByRole("heading", { name: /Create your vault passphrase|Unlock your vault/ });
  await expect(heading).toBeVisible({ timeout: 15_000 });
  const enrolling = (await heading.textContent())?.startsWith("Create") ?? false;
  await page.getByLabel("Passphrase", { exact: true }).fill(passphrase);
  if (enrolling) {
    await page.getByLabel("Repeat passphrase").fill(passphrase);
    await page.getByRole("button", { name: "Create vault" }).click();
  } else {
    await page.getByRole("button", { name: "Unlock" }).click();
  }
  await expect(page.getByRole("heading", { name: "Secrets" })).toBeVisible({ timeout: 30_000 });

  const name = `e2e ${Date.now()}`;
  await page.getByLabel("Name", { exact: true }).fill(name);
  await page.getByLabel("Username").fill("alice");
  await page.getByLabel("Password", { exact: true }).fill(marker);
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByRole("list", { name: "Secrets" }).getByText(name)).toBeVisible({ timeout: 15_000 });

  // Reload = locked (memory gone), server session still valid → unlock screen.
  await page.reload();
  await expect(page.getByRole("heading", { name: "Unlock your vault" })).toBeVisible({ timeout: 15_000 });
  await page.getByLabel("Passphrase", { exact: true }).fill(passphrase);
  await page.getByRole("button", { name: "Unlock" }).click();
  await expect(page.getByRole("heading", { name: "Secrets" })).toBeVisible({ timeout: 30_000 });

  await page.getByRole("list", { name: "Secrets" }).getByRole("button", { name }).click();
  await expect(page.getByTestId("password-value")).toHaveText("••••••••");
  await page.getByRole("button", { name: "Reveal" }).click();
  await expect(page.getByTestId("password-value")).toHaveText(marker);

  // Nothing secret on the wire: no request body or URL contains the passphrase or the marker.
  for (const request of requests) {
    const body = request.postData() ?? "";
    expect(body, request.url()).not.toContain(passphrase);
    expect(body, request.url()).not.toContain(marker);
    expect(request.url()).not.toContain(marker);
  }
  expect(requests.length).toBeGreaterThan(5);

  // Nothing persisted in the browser.
  const storage = await page.evaluate(() => ({
    local: Object.keys(localStorage).length,
    session: Object.keys(sessionStorage).length,
    secure: window.isSecureContext
  }));
  expect(storage.local).toBe(0);
  expect(storage.session).toBe(0);
  expect(storage.secure).toBe(true);
  // Designed probes: the first /auth/me (no cookie yet) is 401 and /me/keyring before enrolment
  // is 404; Chromium logs both as resource errors. Everything else is a real error.
  expect(consoleErrors.filter((line) => !/status of 40[14]/.test(line))).toEqual([]);
});

test("wrong passphrase is rejected without a server round-trip", async ({ page }) => {
  const requests = watchRequests(page);
  await login(page);
  await expect(page.getByRole("heading", { name: "Unlock your vault" })).toBeVisible({ timeout: 15_000 });
  const before = requests.length;
  await page.getByLabel("Passphrase", { exact: true }).fill("definitely not the passphrase");
  await page.getByRole("button", { name: "Unlock" }).click();
  await expect(page.getByRole("alert")).toHaveText("Wrong passphrase.", { timeout: 30_000 });
  expect(requests.length).toBe(before);
});

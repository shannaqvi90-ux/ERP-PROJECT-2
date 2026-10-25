import { expect, test, type Page } from "@playwright/test";
import { checkAccessibility } from "./a11y";
import { freshStart, signIn, users } from "./demo";

/** The rows of the shared list grid (not the tables inside the open record's panel). */
const listRows = (page: Page) => page.locator("table[role=grid] tbody tr");

/** Today in the workspace's time zone (the demo's: Asia/Dubai), as the server dates a rate. */
const today = () => new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Dubai" }).format(new Date());

/** Signs in and waits until the app shows its navigation (the session cookie is set by then). */
async function signedIn(page: Page, email: string, language: "en" | "ar" = "en") {
  await freshStart(page, language);
  await signIn(page, email);
  await expect(page.locator("main h1")).toBeVisible();
}

/** Removes the euro's rate of today, so a test can enter it (outside what the test checks). */
async function clearTodaysEuroRate(page: Page) {
  const filter = encodeURIComponent(`currencyCode eq 'EUR' and rateDate eq '${today()}'`);
  const answer = await page.request.get(`/api/currency/rates?filter=${filter}`);
  expect(answer.status(), await answer.text()).toBe(200);
  const rows = ((await answer.json()) as { items: { id: string }[] }).items;
  for (const row of rows) {
    const deleted = await page.request.delete(`/api/currency/rates/${row.id}`, { headers: { "X-Erp-Request": "1" } });
    expect(deleted.status()).toBe(204);
  }
}

test.describe("currencies and exchange rates", () => {
  test("an administrator opens the euro, types today's rate and Enter saves it; the converter uses it at once", async ({ page }) => {
    await signedIn(page, users.admin);
    await clearTodaysEuroRate(page);

    await page.getByRole("navigation", { name: "Main navigation" }).getByRole("link", { name: "Currencies" }).click();
    await expect(page).toHaveURL(/\/currency\/currencies$/);
    // Those in use first: the dirham and the demo's currencies, the euro among them.
    await expect(listRows(page).first()).toContainText("AED");
    await listRows(page).filter({ has: page.locator("td", { hasText: /^EUR$/ }) }).click();
    const rate = page.locator('.cur-quick input[name="rate"]');
    await expect(rate).toBeFocused();
    await page.keyboard.type("4.2875");
    await page.keyboard.press("Enter");
    await expect(page.locator(".cur-quick [role=status]")).toContainText("Saved: 1 EUR = 4.2875 AED on");
    // The currency's own form was not saved by that Enter, and the latest rates show the new one.
    await expect(page.locator(".cur-recent tbody tr").first()).toContainText("4.2875");

    // Convert: the amount in AED, the rate and its date, as it is typed.
    await page.locator('.cur-convert input[name="amount"]').click();
    await page.keyboard.type("1250");
    await expect(page.getByTestId("conversion")).toContainText(/AED\s5,359\.38/);
    await expect(page.getByTestId("conversion")).toContainText("At 4.2875 AED per EUR, the rate of");

    // The palette converts too: Ctrl+K, "1250 eur".
    await page.keyboard.press("Escape");
    await page.keyboard.press("Control+KeyK");
    await page.keyboard.type("1250 eur");
    await expect(page.locator('.palette [role="option"]').filter({ hasText: /AED\s5,359\.38/ })).toBeVisible();
    await page.keyboard.press("Escape");

    expect(await checkAccessibility(page, "currencies with the euro open")).toBeGreaterThan(20);
    await clearTodaysEuroRate(page);
  });

  test("the rates list holds the demo's rates newest first, and a rate is entered and corrected from its form", async ({ page }) => {
    await signedIn(page, users.admin);
    await page.locator("main").focus();
    await page.keyboard.press("g");
    await page.keyboard.press("x");
    await expect(page).toHaveURL(/\/currency\/rates$/);
    await expect(listRows(page).first()).toBeVisible();
    const total = ((await (await page.request.get("/api/currency/rates?take=1")).json()) as { total: number }).total;
    expect(total).toBeGreaterThanOrEqual(1000);

    // A rate of a past day for the pound, from the keyboard: Alt+N, the currency, the date, the rate.
    const filter = encodeURIComponent("currencyCode eq 'GBP' and rateDate eq '2001-02-03'");
    for (const row of ((await (await page.request.get(`/api/currency/rates?filter=${filter}`)).json()) as { items: { id: string }[] }).items) {
      await page.request.delete(`/api/currency/rates/${row.id}`, { headers: { "X-Erp-Request": "1" } });
    }
    await page.keyboard.press("Alt+KeyN");
    const currency = page.locator('.record-form select[name="currency"]');
    await expect(currency).toBeFocused();
    await currency.selectOption("GBP");
    await page.locator('.record-form input[name="rateDate"]').fill("2001-02-03");
    await page.locator('.record-form input[name="rate"]').fill("5.4321");
    await page.locator('.record-form input[name="rate"]').press("Enter");
    await expect(page.locator(".record-form .notice")).toHaveText("Saved.");
    await expect(page.locator(".record-header h2")).toContainText("GBP");
    // Corrected: the rate changes, the source becomes "Entered by hand".
    await page.locator('.record-form input[name="rate"]').fill("5.4322");
    await page.keyboard.press("Control+KeyS");
    await expect(page.locator(".record-form .notice")).toHaveText("Saved.");
    const saved = ((await (await page.request.get(`/api/currency/rates?filter=${filter}`)).json()) as { items: { exchangeRate: string; source: string; id: string }[] }).items;
    expect(saved).toHaveLength(1);
    expect(saved[0]).toMatchObject({ exchangeRate: "5.4322", source: "manual" });
    await page.request.delete(`/api/currency/rates/${saved[0]!.id}`, { headers: { "X-Erp-Request": "1" } });
  });

  test("in Arabic the currencies screen reads right to left with Arabic names", async ({ page }) => {
    await signedIn(page, users.adminArabic, "ar");
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await page.locator('nav a[href="/currency/currencies"]').click();
    await expect(listRows(page).first()).toBeVisible();
    await expect(page.locator("table[role=grid] tbody")).toContainText("درهم إماراتي");
    await listRows(page).filter({ has: page.locator("td", { hasText: /^EUR$/ }) }).click();
    await expect(page.locator(".cur-convert h3")).toHaveText("التحويل إلى AED");
  });

  test("a user who may only read sees rates and converts, but is offered no rate entry", async ({ page }) => {
    await signedIn(page, users.viewer);
    await page.locator('nav a[href="/currency/currencies"]').click();
    await listRows(page).filter({ has: page.locator("td", { hasText: /^EUR$/ }) }).click();
    await expect(page.locator(".cur-convert")).toBeVisible();
    await expect(page.locator(".cur-quick")).toHaveCount(0);
    await expect(page.getByTestId("record-read-only")).toBeVisible();
  });
});

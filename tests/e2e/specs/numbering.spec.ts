import { expect, test, type Page } from "@playwright/test";
import { checkAccessibility } from "./a11y";
import { freshStart, signIn, users } from "./demo";

/** The rows of the shared list grid (not the tables inside the open record's panel). */
const listRows = (page: Page) => page.locator("table[role=grid] tbody tr");

/** This year in the workspace's time zone (the demo's: Asia/Dubai). */
const year = () => new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Dubai", year: "numeric" }).format(new Date());

async function signedIn(page: Page, email: string, language: "en" | "ar" = "en") {
  await freshStart(page, language);
  await signIn(page, email);
  await expect(page.locator("main h1")).toBeVisible();
}

type Row = { id: string; documentType: string; companyCode: string; workingCompany: boolean; prefix: string; suffix: string; padding: number; reset: string; gapless: boolean; perBranch: boolean; version: number };

/** The working company's purchase order sequence, read through the API as the screen does. */
async function purchaseOrders(page: Page): Promise<Row> {
  const answer = await page.request.get("/api/numbering/sequences?take=200");
  expect(answer.status(), await answer.text()).toBe(200);
  return ((await answer.json()) as { items: Row[] }).items.find((r) => r.documentType === "purchasing.order" && r.workingCompany)!;
}

/** Puts a sequence back as the demo seeded it, outside what the test checks. */
async function restore(page: Page, row: Row) {
  const current = (await (await page.request.get(`/api/numbering/sequences/${row.id}`)).json()) as Row;
  const put = await page.request.put(`/api/numbering/sequences/${row.id}`, {
    headers: { "X-Erp-Request": "1" },
    data: { prefix: row.prefix, suffix: row.suffix, padding: row.padding, reset: row.reset, gapless: row.gapless, perBranch: row.perBranch, nextValue: null, version: current.version },
  });
  expect(put.status(), await put.text()).toBe(200);
}

test.describe("document numbering", () => {
  test("an administrator opens purchase orders by keyboard, types a new prefix and digits, sees the next number and saves with Enter", async ({ page }) => {
    await signedIn(page, users.admin);
    const before = await purchaseOrders(page);
    expect(before.prefix).toBe("PO-{YYYY}-");

    // G then N from anywhere.
    await page.locator("main").focus();
    await page.keyboard.press("g");
    await page.keyboard.press("n");
    await expect(page).toHaveURL(/\/numbering\/sequences$/);
    await expect(page.locator("main h1")).toHaveText("Document numbering");
    // The working company's sequences first; the demo counted its purchase orders to 147 this year.
    const row = listRows(page).filter({ hasText: "Purchase order" }).first();
    await expect(row).toContainText(`PO-${year()}-00148`);
    await row.click();

    const prefix = page.locator('.list-record input[name="prefix"]');
    await expect(prefix).toBeFocused();
    const next = page.getByTestId("next-number");
    await expect(next).toHaveText(`PO-${year()}-00148`);
    await page.keyboard.type("PO-{YY}{MM}-");
    await expect(next).toHaveText(new RegExp(`^PO-${year().slice(2)}\\d{2}-00148$`));
    await page.keyboard.press("Tab");
    await expect(page.locator('.list-record input[name="padding"]')).toBeFocused();
    await page.keyboard.type("6");
    await expect(next).toHaveText(new RegExp(`^PO-${year().slice(2)}\\d{2}-000148$`));
    await expect(page.locator(".nb-preview")).toContainText("Starts again on 1 Jan");
    await page.keyboard.press("Enter");
    await expect(page.locator(".list-record")).toContainText("Saved");
    await expect(row).toContainText(new RegExp(`PO-${year().slice(2)}\\d{2}-000148`));
    const after = await purchaseOrders(page);
    expect([after.prefix, after.padding, after.reset]).toEqual(["PO-{YY}{MM}-", 6, "yearly"]);
    await checkAccessibility(page, "document numbering with a sequence open");
    await restore(page, before);
  });

  test("a pattern that would repeat last year's numbers is refused before it is sent, in Arabic too", async ({ page }) => {
    await signedIn(page, users.adminArabic, "ar");
    await page.goto("/numbering/sequences");
    await expect(page.locator("main h1")).toHaveText("ترقيم المستندات");
    await listRows(page).filter({ hasText: "فاتورة ضريبية" }).first().click();
    const prefix = page.locator('.list-record input[name="prefix"]');
    await expect(prefix).toBeFocused();
    await page.keyboard.type("INV-");
    await expect(page.locator(".nb-preview")).toContainText("صحّح النمط");
    await page.keyboard.press("Enter");
    await expect(page.locator('.list-record [data-field="reset"] .field-error')).toContainText("السنة");
    await expect(page.locator(".nb-preview h3")).toHaveText("الأرقام التالية");
    await checkAccessibility(page, "document numbering in Arabic with a refused pattern");
  });

  test("the read-only user sees the numbering but cannot change it", async ({ page }) => {
    await signedIn(page, users.viewer);
    await page.goto("/numbering/sequences");
    await listRows(page).filter({ hasText: "Purchase order" }).first().click();
    await expect(page.locator('.list-record input[name="prefix"]')).toBeDisabled();
    await expect(page.getByTestId("next-number")).toHaveText(new RegExp(`^PO-${year()}-\\d{5}$`));
    const id = (await purchaseOrders(page)).id;
    const refused = await page.request.put(`/api/numbering/sequences/${id}`, {
      headers: { "X-Erp-Request": "1" },
      data: { prefix: "X-", suffix: "", padding: 5, reset: "never", gapless: false, perBranch: false, nextValue: null, version: 1 },
    });
    expect(refused.status()).toBe(403);
  });
});

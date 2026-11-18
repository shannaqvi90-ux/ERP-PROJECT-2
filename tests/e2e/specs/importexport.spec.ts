import { expect, test, type Page } from "@playwright/test";
import { checkAccessibility } from "./a11y";
import { freshStart, signIn, users } from "./demo";

const stamp = () => `e2e${Date.now() % 100000000}`;

/** Opens the contacts list the way a person does: from the navigation. */
async function openContacts(page: Page) {
  await page.locator('nav a[href="/contacts/contacts"]').click();
  await expect(page.locator(".list-search input")).toBeFocused();
}

/** Removes the contacts a test imported (by the name tag in the file), through the API as the screen does. */
async function clean(page: Page, tag: string) {
  const found = (await (await page.request.get(`/api/contacts/contacts?search=${encodeURIComponent(tag)}&take=100`)).json()) as { items: { id: string; version: number }[] };
  for (const c of found.items) {
    await page.request.post(`/api/contacts/contacts/${c.id}/archive`, { headers: { "X-Erp-Request": "1" }, data: { version: c.version } }).catch(() => undefined);
  }
}

test.describe("import and export", () => {
  test("a CSV file is mapped by its headers, checked, then imported from the keyboard, with an error file for the bad row", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openContacts(page);
    const tag = stamp();
    try {
      // Alt+I opens the import dialog from the list.
      await page.keyboard.press("Alt+KeyI");
      const dialog = page.getByRole("dialog");
      await expect(dialog).toBeVisible();
      await expect(dialog.locator('[data-testid="import-file"]')).toBeFocused();
      const csv = `Name,Email,Phone,Street,City,Country\r\n${tag} One Trading,one@${tag}.example,+971 4 123 4567,12 Street 3,Dubai,United Arab Emirates\r\n${tag} Bad Mail,not-an-email,,,,\r\n${tag} Two Trading,,+971 50 123 4567,Port Road,Muscat,Oman\r\n`;
      await dialog.locator('[data-testid="import-file"]').setInputFiles({ name: "contacts.csv", mimeType: "text/csv", buffer: Buffer.from(csv) });
      // The plain headers are matched to fields at once.
      const mapping = dialog.locator('[data-testid="import-mapping"] tbody tr');
      await expect(mapping).toHaveCount(6);
      await expect(mapping.nth(0).locator("select")).toHaveValue("name");
      await expect(mapping.nth(1).locator("select")).toHaveValue("email");
      await expect(mapping.nth(5).locator("select")).toHaveValue("country");
      expect(await checkAccessibility(page, "import dialog")).toBeGreaterThan(5);

      // Importing refuses the whole file while a row has a problem; the error file names it.
      await dialog.locator('[data-testid="import-run"]').click();
      await expect(dialog.locator('[data-testid="import-failed"]')).toContainText("Nothing was imported");
      await expect(dialog.locator('[data-testid="import-issues"]')).toContainText("e-mail");
      const errors = await page.request.get((await dialog.locator('[data-testid="import-errors"]').getAttribute("href"))!);
      expect(errors.status()).toBe(200);
      expect(await errors.text()).toContain("not-an-email");

      // Skipping the bad row imports the other two.
      await dialog.getByLabel("Skip rows with problems and import the rest").check();
      await dialog.locator('[data-testid="import-run"]').click();
      await expect(dialog.locator('[data-testid="import-done"]')).toContainText("Imported 2 rows");
      await page.keyboard.press("Escape");
      await expect(dialog).toBeHidden();
      // The list shows them.
      await page.locator(".list-search input").fill(tag);
      await expect(page.locator("table[role=grid] tbody tr").filter({ hasText: `${tag} One Trading` })).toHaveCount(1);
      await expect(page.locator("table[role=grid] tbody tr").filter({ hasText: `${tag} Two Trading` })).toHaveCount(1);
    } finally {
      await clean(page, tag);
    }
  });

  test("the same screens work in Arabic and mirror", async ({ page }) => {
    await freshStart(page, "ar");
    await signIn(page, users.adminArabic);
    await page.locator('nav a[href="/contacts/contacts"]').click();
    await page.keyboard.press("Alt+KeyI");
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await expect(dialog).toContainText("استيراد");
    expect(await page.evaluate(() => document.documentElement.dir)).toBe("rtl");
    await page.keyboard.press("Escape");
  });

  test("the full export of the workspace is started, written in the background and downloaded as a zip", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await page.keyboard.press("KeyG");
    await page.keyboard.press("KeyE");
    await expect(page.getByRole("heading", { name: "Full export of the workspace" })).toBeVisible();
    await page.locator('[data-testid="export-start"]').click();
    const ready = page.locator('[data-testid="exports-table"] tbody tr').first().locator('[data-testid="export-download"]');
    await expect(ready).toBeVisible({ timeout: 120_000 });
    const [download] = await Promise.all([page.waitForEvent("download"), ready.click()]);
    expect(download.suggestedFilename()).toMatch(/^workspace-export-.*\.zip$/);
    expect(await checkAccessibility(page, "full export screen")).toBeGreaterThan(5);
  });

  test("a user who may only read contacts sees no Import button and cannot reach the import endpoints", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.viewer);
    await openContacts(page);
    await expect(page.locator('[data-testid="import-open"]')).toHaveCount(0);
    await page.keyboard.press("Alt+KeyI");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    expect((await page.request.get("/api/importexport/contacts.contacts/fields")).status()).toBe(403);
    expect((await page.request.get("/api/importexport/tenant-exports")).status()).toBe(403);
  });
});

import { expect, test, type Page } from "@playwright/test";
import { checkAccessibility } from "./a11y";
import { freshStart, signIn, users } from "./demo";

/** The rows of the shared list grid (not the tables inside the open record's form). */
const listRows = (page: Page) => page.locator("table[role=grid] tbody tr");

/** The contacts screen from the main navigation (in either language). */
async function openContacts(page: Page) {
  await page.locator('nav a[href="/contacts/contacts"]').click();
  await expect(page).toHaveURL(/\/contacts\/contacts/);
  await expect(listRows(page).first()).toBeVisible();
}

test.describe("the contacts directory", () => {
  test("an administrator finds a contact by typing its name, reads its people, and opens one of them", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openContacts(page);
    // The verify stack seeds 2,000 generated contacts and the named ones.
    const total = ((await (await page.request.get("/api/contacts/contacts?take=1")).json()) as { total: number }).total;
    expect(total).toBeGreaterThanOrEqual(2000);
    // The search box has the focus on arrival; Enter opens the best match.
    await expect(page.locator(".list-search input")).toBeFocused();
    await page.keyboard.type("falcon building");
    await expect(listRows(page).filter({ hasText: "Falcon Building Materials LLC" }).first()).toBeVisible();
    await page.keyboard.press("Enter");
    await expect(page.locator(".record-header h2")).toHaveText("Falcon Building Materials LLC");
    await expect(page.locator('[data-field="email"] input')).toHaveValue(/^accounts@falcon\./);
    const people = page.locator(".record-section", { hasText: "Hamad Saeed Al Ketbi" });
    await expect(people).toBeVisible();
    await people.getByRole("link", { name: "Hamad Saeed Al Ketbi" }).click();
    await expect(page.locator(".record-header h2")).toHaveText("Hamad Saeed Al Ketbi");
    await expect(page.locator('input[name="organisationId"]')).toHaveValue("Falcon Building Materials LLC");
    expect(await checkAccessibility(page, "contact form")).toBeGreaterThan(20);
  });

  test("a new contact is created from the keyboard, with a warning when it may already exist", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openContacts(page);
    await page.keyboard.press("Alt+KeyN");
    await expect(page.locator('[data-field="nameEn"] input')).toBeFocused();
    // The name of a contact the directory holds: the form says so, and saving is still allowed.
    await page.keyboard.type("Falcon Building Materials L.L.C.");
    await expect(page.locator(".ct-duplicates")).toContainText("Falcon Building Materials LLC");
    await expect(page.locator(".ct-duplicates")).toContainText("same name");
    const name = `Gulf Horizon Trading ${Date.now() % 1000000} LLC`;
    await page.locator('[data-field="nameEn"] input').fill(name);
    await page.locator('[data-field="nameAr"] input').fill("الخليج الأفق للتجارة");
    await expect(page.locator(".ct-duplicates")).toHaveCount(0);
    await page.locator('[data-field="mobile"] input').fill("0505550188");
    await expect(page.locator('[data-field="mobile"] input')).toHaveValue("+971 50 555 0188");
    await page.keyboard.press("Control+KeyS");
    await expect(page.locator(".record-form .notice")).toHaveText("Saved.");
    await expect(page.locator(".record-header h2")).toHaveText(name);
    await expect(page).toHaveURL(/\/contacts\/contacts\/[0-9a-f-]{36}(\?|$)/);

    // Archive and restore it from the form's toolbar: it is kept, never deleted.
    await page.getByRole("button", { name: "Archive" }).click();
    await expect(page.locator(".ct-archived")).toBeVisible();
    await page.getByRole("button", { name: "Restore" }).click();
    await expect(page.locator(".ct-archived")).toHaveCount(0);
  });

  test("the command palette opens a contact by name from anywhere", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await page.keyboard.press("Control+KeyK");
    await page.keyboard.type("Hamad Saeed Al Ketbi");
    await expect(page.locator('[role="option"]', { hasText: "Hamad Saeed Al Ketbi" }).first()).toBeVisible();
    await page.keyboard.press("Enter");
    await expect(page.locator(".record-header h2")).toHaveText("Hamad Saeed Al Ketbi");
    await expect(page.locator('[data-field="mobile"] input')).toHaveValue("+971 50 555 0102");
  });

  test("in Arabic the contacts read right to left, the Arabic name comes first and a new contact starts there", async ({ page }) => {
    await freshStart(page, "ar");
    await signIn(page, users.adminArabic);
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await openContacts(page);
    await page.keyboard.type("فالكون");
    await expect(listRows(page).filter({ hasText: "فالكون لمواد البناء ذ.م.م" }).first()).toBeVisible();
    await page.keyboard.press("Enter");
    await expect(page.locator(".record-header h2")).toHaveText("فالكون لمواد البناء ذ.م.م");
    await expect(page.locator(".record-form")).toContainText("الاسم (بالعربية)");
    await page.keyboard.press("Escape");
    await page.keyboard.press("Alt+KeyN");
    await expect(page.locator('[data-field="nameAr"] input')).toBeFocused();
    expect(await checkAccessibility(page, "new contact in Arabic")).toBeGreaterThan(20);
  });

  test("a reader is offered no New, no Archive and no editable field", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.viewer);
    await openContacts(page);
    await expect(page.getByRole("button", { name: "New contact" })).toHaveCount(0);
    await page.keyboard.type("falcon building");
    await expect(listRows(page).filter({ hasText: "Falcon Building Materials LLC" }).first()).toBeVisible();
    await page.keyboard.press("Enter");
    await expect(page.locator(".record-header h2")).toHaveText("Falcon Building Materials LLC");
    await expect(page.locator('[data-field="nameEn"] input')).toBeDisabled();
    await expect(page.getByRole("button", { name: "Archive" })).toHaveCount(0);
  });
});

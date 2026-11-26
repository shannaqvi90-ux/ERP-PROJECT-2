import { expect, test, type Page } from "@playwright/test";
import { checkAccessibility } from "./a11y";
import { freshStart, signIn, users } from "./demo";

/** The rows of the shared list grid (not the tables inside the open record's form). */
const listRows = (page: Page) => page.locator("table[role=grid] tbody tr");

/** Open the seeded role named `name` (or a name an earlier run left it with) and return its name now. */
async function openRole(page: Page, name: string): Promise<string> {
  await page.getByRole("navigation", { name: "Main navigation" }).getByRole("link", { name: "Roles" }).click();
  await expect(listRows(page).first()).toBeVisible();
  await listRows(page).filter({ hasText: name }).first().click();
  await expect(page.locator(".record-header h2")).toContainText(name);
  return page.locator('[data-field="nameEn"] input').inputValue();
}

async function renameRole(page: Page, name: string) {
  const field = page.locator('[data-field="nameEn"] input');
  await field.fill(name);
  await field.press("Control+KeyS");
  // Saved: the heading carries the new name and no unsaved-changes mark.
  await expect(page.locator(".record-header h2")).toHaveText(name);
}

test.describe("the audit trail", () => {
  test("a record's form shows its latest change under its heading and its whole history with Alt+Y", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    const old = await openRole(page, "Staff");
    const renamed = `Staff ${Date.now() % 100000}`;
    await renameRole(page, renamed);

    // Under the heading, at once: who, when, and the field's old and new value.
    const latest = page.getByTestId("audit-latest");
    await expect(latest).toContainText("Changed by Mariam Al Mansoori");
    await expect(latest).toContainText(`Name in English: from ${old} to ${renamed}`);
    await expect(latest.locator("time")).toHaveCount(1);

    // The whole history, newest first, from the keyboard; Escape closes it.
    await page.keyboard.press("Alt+KeyY");
    const history = page.getByTestId("audit-history");
    await expect(history).toBeVisible();
    await expect(history.locator(".audit-event").first()).toContainText("Changed by Mariam Al Mansoori");
    await expect(history.locator(".audit-event").first().locator("tbody tr").first()).toContainText(["Name in English", old, renamed].join(""));
    await checkAccessibility(page, "a record's history");
    await history.locator("h3").press("Escape");
    await expect(history).toBeHidden();

    // Put the name back: the latest change is now that one.
    await renameRole(page, "Staff");
    await expect(latest).toContainText(`Name in English: from ${renamed} to Staff`);
  });

  test("a long change under the heading never widens the form or narrows the list beside it", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openRole(page, "Staff");
    const field = page.locator('[data-field="nameAr"] input');
    const before = await field.inputValue();
    const form = page.locator("form").filter({ has: page.locator(".record-header") }).first();
    const widthBefore = await form.evaluate((f) => f.getBoundingClientRect().width);
    // One word longer than the form is wide (a hash, a long name): the latest change shows it on
    // one line, cut at the form's edge (integrator's verify of p07 round 1: a 64-character hash
    // under a user's heading grew the form until the users list beside it kept one column).
    const long = "م".repeat(100);
    await field.fill(long);
    await field.press("Control+KeyS");
    const latest = page.getByTestId("audit-latest");
    await expect(latest).toContainText(long);
    const sizes = await form.evaluate((f) => {
      const line = f.querySelector('[data-testid="audit-latest"]')!.getBoundingClientRect();
      const box = f.getBoundingClientRect();
      return { overflow: f.scrollWidth - f.clientWidth, width: box.width, inside: line.left >= box.left - 1 && line.right <= box.right + 1 };
    });
    expect(sizes.overflow).toBeLessThanOrEqual(1);
    expect(sizes.inside).toBe(true);
    expect(sizes.width).toBeLessThanOrEqual(widthBefore + 1);
    await field.fill(before);
    await field.press("Control+KeyS");
    await expect(latest).toContainText(`to ${before}`);
  });

  test("the audit log lists changes, filters by field and opens a change with its record", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    const old = await openRole(page, "Staff");
    const renamed = `Staff log ${Date.now() % 100000}`;
    await renameRole(page, renamed);

    await page.getByRole("navigation", { name: "Main navigation" }).getByRole("link", { name: "Audit log" }).click();
    await expect(page).toHaveURL(/\/audit\/log/);
    await expect(listRows(page).first()).toBeVisible();
    // The record's name finds its changes.
    await page.keyboard.type("staff log");
    const row = listRows(page).filter({ hasText: `Name in English: from ${old} to ${renamed}` }).first();
    await expect(row).toBeVisible();
    await expect(row).toContainText("Mariam Al Mansoori");
    await row.click();
    const change = page.locator(".audit-change");
    await expect(change).toContainText("Changed by Mariam Al Mansoori");
    await expect(change.getByRole("link", { name: "Open the record" })).toBeVisible();
    await checkAccessibility(page, "the audit log");

    // The link opens the role itself.
    await change.getByRole("link", { name: "Open the record" }).click();
    await expect(page).toHaveURL(/\/identity\/roles\//);
    await renameRole(page, "Staff");
  });

  test("in Arabic: the log, the field names and the change read in Arabic", async ({ page }) => {
    await freshStart(page, "ar");
    await signIn(page, users.adminArabic);
    await page.locator('nav a[href="/audit/log"]').first().click();
    await expect(page).toHaveURL(/\/audit\/log/);
    await expect(listRows(page).first()).toBeVisible();
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await expect(page.getByRole("columnheader", { name: "بواسطة" })).toBeVisible();
    await checkAccessibility(page, "the audit log in Arabic");
  });

  test("the audit log filters by a person chosen by name, from the keyboard, in English and Arabic", async ({ page }) => {
    for (const [language, who, column] of [
      ["en", users.admin, "Changed by"],
      ["ar", users.adminArabic, "بواسطة"],
    ] as const) {
      await page.context().clearCookies();
      await freshStart(page, language);
      await signIn(page, who);
      await page.locator('nav a[href="/audit/log"]').first().click();
      await expect(listRows(page).first()).toBeVisible();
      const requests: number[] = [];
      page.on("response", (r) => r.url().includes("/api/audit/log?") && r.url().includes("actorId") && requests.push(r.status()));
      await page.getByRole("button", { name: new RegExp(column) }).first().click();
      await page.getByRole("menuitem").filter({ hasText: language === "en" ? "Filter" : "تصفية" }).first().click();
      const picker = page.getByRole("combobox");
      await expect(picker).toBeFocused();
      // A name never answers 400 or leaves the list unfiltered: the picker offers the people matching it.
      // Several people share a name in a large workspace: the e-mail is what tells them apart.
      await picker.fill(who);
      await expect(page.getByRole("option")).toHaveCount(1);
      await expect(page.getByRole("option").first()).toContainText(who);
      // The list shows its unfiltered rows until the filtered answer arrives, so the rows being visible says
      // nothing about the request: wait for the filtered answer itself, then judge every one that was sent.
      const filtered = page.waitForResponse((r) => r.url().includes("/api/audit/log?") && r.url().includes("actorId"));
      await page.keyboard.press("Enter");
      await page.keyboard.press("Enter");
      await filtered;
      await expect(page.locator(".list-chips")).toBeVisible();
      await expect(listRows(page).first()).toBeVisible();
      expect(requests.length).toBeGreaterThan(0);
      expect(requests.every((status) => status === 200)).toBe(true);
      await expect(page.getByText(/Some fields need attention|بعض الحقول/)).toHaveCount(0);
    }
  });

  test("a user without the log's permission is not offered it", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.noAccess);
    await expect(page.getByText("No areas are open to you yet.")).toBeVisible();
    await expect(page.getByRole("link", { name: "Audit log" })).toHaveCount(0);
    await page.goto("/audit/log");
    await expect(page.getByRole("heading", { name: "No access" })).toBeVisible();
    await expect(page.locator("table")).toHaveCount(0);
  });
});

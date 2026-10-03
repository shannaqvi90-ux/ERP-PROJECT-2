import { expect, test, type Page } from "@playwright/test";
import { freshStart, signIn, users } from "./demo";

/** Opens the switcher with Alt+C, filters by the typed text and picks the first match with Enter. */
async function switchTo(page: Page, filter: string) {
  await page.keyboard.press("Alt+KeyC");
  const box = page.locator(".workplace-popover input");
  await expect(box).toBeFocused();
  await page.keyboard.type(filter);
  await expect(page.locator('.workplace-popover [role="option"]').first()).toBeVisible();
  await page.keyboard.press("Enter");
  await expect(page.locator(".workplace-popover")).toHaveCount(0);
}

test.describe("companies, branches and the working company", () => {
  test("an administrator creates a company and its first branch from the keyboard, then works in it", async ({ page }) => {
    const code = `E2E-${Date.now() % 1000000}`;
    await freshStart(page, "en");
    await signIn(page, users.admin);
    const workplace = page.getByTestId("workplace");
    await expect(workplace).toHaveText("ALN-DXB · DEIRA-HQ");

    await page.getByRole("navigation", { name: "Main navigation" }).getByRole("link", { name: "Companies" }).click();
    await expect(page).toHaveURL(/\/tenancy\/companies$/);
    await expect(page.locator("table tbody tr").first()).toBeVisible();
    await expect(page.getByText("4 companies", { exact: true })).toBeVisible();

    // Alt+N, type both legal names and the code, Ctrl+S.
    await page.keyboard.press("Alt+KeyN");
    await expect(page.locator('[data-field="legalNameEn"] input')).toBeFocused();
    await page.keyboard.type("Al Noor Logistics LLC");
    await page.keyboard.press("Tab");
    await page.keyboard.type("النور للخدمات اللوجستية ذ.م.م");
    await page.keyboard.press("Tab");
    await page.keyboard.type(code.toLowerCase());
    await page.keyboard.press("Control+KeyS");
    await expect(page.getByRole("status")).toHaveText("Saved.");
    await expect(page.locator(".record-header h2")).toHaveText(`${code} · Al Noor Logistics LLC`);
    await expect(page).toHaveURL(/\?id=/);

    // The first branch: one line, Enter.
    await page.locator('input[name="branchNameEn"]').fill("Head office");
    await page.locator('input[name="branchNameAr"]').fill("المكتب الرئيسي");
    await page.locator('input[name="branchCode"]').fill("hq");
    await page.locator('input[name="branchCode"]').press("Enter");
    await expect(page.locator(".record-section table tbody tr")).toHaveCount(1);
    await expect(page.locator(".record-section table tbody tr").first()).toContainText("HQ");

    // Work in it: Alt+C, type, Enter. The choice survives a reload.
    await switchTo(page, code);
    await expect(workplace).toHaveText(`${code} · HQ`);
    await page.reload();
    await expect(page.getByTestId("workplace")).toHaveText(`${code} · HQ`);

    // Back to the first company in one click (each other company has its own button).
    await page.locator('.workplace-chip[data-company="ALN-DXB"]').click();
    await expect(page.getByTestId("workplace")).toHaveText("ALN-DXB · DEIRA-HQ");
  });

  test("in Arabic the companies read right to left and the switcher works the same way", async ({ page }) => {
    await freshStart(page, "ar");
    await signIn(page, users.adminArabic);
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await page.locator('nav a[href="/tenancy/companies"]').click();
    await expect(page.locator("table tbody tr").first()).toBeVisible();
    await expect(page.locator("table tbody")).toContainText("شركة النور للتجارة ذ.م.م");
    await page.locator("table tbody tr", { hasText: "ALN-SHJ" }).click();
    await expect(page.locator('[data-field="legalNameAr"] input')).toHaveValue("مصانع النور ذ.م.م");
    await expect(page.locator(".record-section h3").last()).toHaveText(/^الفروع \((3|٣) فروع\)$/);

    await switchTo(page, "SHJ-FAC");
    await expect(page.getByTestId("workplace")).toHaveText("ALN-SHJ · SHJ-FAC");
    await switchTo(page, "DEIRA");
    await expect(page.getByTestId("workplace")).toHaveText("ALN-DXB · DEIRA-HQ");
  });

  test("a read-only user works in one company: sees only it, cannot switch or edit", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.viewer);
    const workplace = page.getByTestId("workplace");
    await expect(workplace).toHaveText("ALN-DXB · DEIRA-HQ");
    expect(await workplace.evaluate((el) => el.tagName)).toBe("SPAN");
    await page.goto("/tenancy/companies");
    await expect(page.locator("table tbody tr")).toHaveCount(1);
    await expect(page.getByRole("button", { name: "New" })).toHaveCount(0);
    await page.locator("table tbody tr").first().click();
    await expect(page.locator('[data-field="code"] input')).toBeDisabled();
    await expect(page.getByRole("button", { name: "Save" })).toHaveCount(0);
  });
});

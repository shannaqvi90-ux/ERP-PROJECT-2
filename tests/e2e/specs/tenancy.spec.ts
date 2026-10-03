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

/** The rows of the shared list grid (not the tables inside the open record's form). */
const listRows = (page: Page) => page.locator("table[role=grid] tbody tr");

test.describe("companies, branches and the working company", () => {
  test("an administrator creates a company and its first branch from the keyboard, then works in it", async ({ page }) => {
    const code = `E2E-${Date.now() % 1000000}`;
    await freshStart(page, "en");
    await signIn(page, users.admin);
    const workplace = page.getByTestId("workplace");
    await expect(workplace).toHaveText("ALN-DXB · DEIRA-HQ");

    await page.getByRole("navigation", { name: "Main navigation" }).getByRole("link", { name: "Companies" }).click();
    await expect(page).toHaveURL(/\/tenancy\/companies$/);
    await expect(listRows(page).first()).toBeVisible();
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
    await expect(page.locator(".record-form .notice")).toHaveText("Saved.");
    await expect(page.locator(".record-header h2")).toHaveText(`${code} · Al Noor Logistics LLC`);
    await expect(page).toHaveURL(/[?&]open=[0-9a-f-]{36}/);

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
    await expect(listRows(page).first()).toBeVisible();
    await expect(page.locator("table[role=grid] tbody")).toContainText("شركة النور للتجارة ذ.م.م");
    await listRows(page).filter({ hasText: "ALN-SHJ" }).click();
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
    await expect(listRows(page)).toHaveCount(1);
    await expect(page.getByRole("button", { name: "New" })).toHaveCount(0);
    await listRows(page).first().click();
    await expect(page.locator('[data-field="code"] input')).toBeDisabled();
    await expect(page.getByRole("button", { name: "Save" })).toHaveCount(0);
  });

  test("branches: search word by word, group by company and narrow to one company from the column menu", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await page.locator('nav a[href="/tenancy/branches"]').click();
    await expect(page).toHaveURL(/\/tenancy\/branches/);
    await expect(listRows(page).first()).toBeVisible();
    await expect(page.getByText("12 branches", { exact: true })).toBeVisible();

    // The search box has focus; words in any order, any case.
    await page.keyboard.type("WAREHOUSE south");
    await expect(page.getByText("1 branch", { exact: true })).toBeVisible();
    await expect(listRows(page)).toHaveCount(1);
    await expect(listRows(page).first()).toContainText("JAFZA-WH");
    await expect(listRows(page).first()).toContainText("ALN-FZE · Al Noor General Trading FZE");
    await page.getByRole("searchbox", { name: "Search code or name (/)" }).fill("");
    await expect(page.getByText("12 branches", { exact: true })).toBeVisible();

    // Group by company: one group per company, labelled by code and name.
    await page.getByRole("button", { name: "Options for the column Company" }).click();
    await page.getByRole("menuitem", { name: "Group by this column" }).click();
    const groups = page.locator("tbody.list-groups tr");
    await expect(groups).toHaveCount(4);
    await expect(groups.filter({ hasText: "ALN-SHJ · Al Noor Industries LLC" })).toContainText("3");
    await page.getByRole("button", { name: "Remove grouping" }).click();
    await expect(groups).toHaveCount(0);

    // Filter on one company from the column menu: the chip names it.
    await page.getByRole("button", { name: "Options for the column Company" }).click();
    await page.getByRole("menuitem", { name: "Filter…" }).click();
    await page.getByRole("dialog", { name: "Filter: Company" }).getByLabel("ALN-AUH · Al Noor Technical Services LLC").check();
    await page.getByRole("button", { name: "Apply" }).click();
    await expect(page.getByRole("list", { name: "Filters" })).toContainText("Company is ALN-AUH · Al Noor Technical Services LLC");
    await expect(page.getByText("4 branches", { exact: true })).toBeVisible();
    await expect(listRows(page)).toHaveCount(4);
  });

  test("access: find a user by e-mail, see their companies and limit them to branches", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await page.goto("/tenancy/access");
    await expect(listRows(page).first()).toBeVisible();
    await page.keyboard.type(users.viewer);
    await expect(listRows(page)).toHaveCount(1);
    await expect(listRows(page).first()).toContainText("ALN-DXB (2)");
    await listRows(page).first().click();
    await expect(page).toHaveURL(/[?&]open=[0-9a-f-]{36}/);
    const company = page.locator('.access-company[data-company="ALN-DXB"]');
    await expect(company.getByRole("checkbox").first()).toBeChecked();
    await expect(company.getByLabel("All branches")).not.toBeChecked();
    await expect(page.getByRole("button", { name: "Save" })).toBeVisible();
  });
});

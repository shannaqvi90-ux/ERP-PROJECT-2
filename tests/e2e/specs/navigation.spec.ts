import { expect, test, type Page } from "@playwright/test";
import { freshStart, signIn, users } from "./demo";

/** Opens every entry of the main navigation and checks the screen loads its data. */
async function visitEveryScreen(page: Page, nav: string, noAccess: string) {
  const navigation = page.getByRole("navigation", { name: nav });
  const links = navigation.getByRole("link");
  await expect(links.first()).toBeVisible();
  const targets = await links.evaluateAll((anchors) => anchors.map((a) => a.getAttribute("href") ?? ""));
  expect(targets.length).toBeGreaterThanOrEqual(3);
  for (const target of targets) {
    await navigation.locator(`a[href="${target}"]`).click();
    await expect(page).toHaveURL(new RegExp(`${target.replace(/\//g, "\\/")}$`));
    const main = page.locator("main");
    await expect(main.locator("h1")).toBeVisible();
    await expect(main.getByRole("heading", { name: noAccess })).toHaveCount(0);
    // Each screen shows data from its API: a table with rows or a list of facts.
    await expect(main.locator("table tbody tr, dl dd").first()).toBeVisible();
    await expect(main.locator('[role="alert"]')).toHaveCount(0);
  }
}

test.describe("navigation reaches every screen", () => {
  test("an administrator opens every menu entry in English and each screen loads", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await visitEveryScreen(page, "Main navigation", "No access");
  });

  test("an administrator opens every menu entry in Arabic, right to left, and each screen loads", async ({ page }) => {
    await freshStart(page, "ar");
    await signIn(page, users.adminArabic);
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await visitEveryScreen(page, "التنقل الرئيسي", "لا توجد صلاحية");
  });
});

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

test.describe("go-to keys reach every listed screen", () => {
  /** G, then each entry's letter (data-go), from Home: every screen the navigation lists that has a
   * letter opens, in a real browser and keyboard. */
  async function goEverywhere(page: Page, nav: string) {
    const navigation = page.getByRole("navigation", { name: nav });
    await expect(navigation.getByRole("link").first()).toBeVisible();
    const entries = await navigation
      .locator("li[data-go]")
      .evaluateAll((items) => items.map((li) => ({ href: li.querySelector("a")?.getAttribute("href") ?? "", letter: li.getAttribute("data-go") ?? "" })));
    expect(entries.length).toBeGreaterThanOrEqual(5);
    for (const { href, letter } of entries) {
      await page.keyboard.press("g");
      await page.keyboard.press("h");
      await expect(page).toHaveURL(/\/$/);
      await page.locator("main").focus();
      await page.keyboard.press("g");
      await expect(page.locator("[data-goto-hint]")).toBeVisible();
      await page.keyboard.press(letter.toLowerCase());
      await expect(page).toHaveURL(new RegExp(`${href.replace(/\//g, "\\/")}$`));
      await expect(page.locator("[data-goto-hint]")).toHaveCount(0);
      await expect(page.locator("main h1")).toBeVisible();
      // The link's name is the entry's name alone (the letter is drawn beside it).
      const link = navigation.locator(`a[href="${href}"]`);
      await expect(link).toHaveAccessibleName(((await link.textContent()) ?? "").trim());
      // The next go-to starts from the screen itself, not from a field.
      await page.locator("main").focus();
    }
  }

  test("in English", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await goEverywhere(page, "Main navigation");
  });

  test("in Arabic, by key position", async ({ page }) => {
    await freshStart(page, "ar");
    await signIn(page, users.adminArabic);
    await goEverywhere(page, "التنقل الرئيسي");
  });
});

/**
 * Every navigation opens the screen on its address, the same screen included (critic p04 round 11:
 * the palette opening a second user from the first user's record changed the address only, the
 * first user's search and record stayed on screen, and Ctrl+S then saved the first user; G then U
 * from a record kept the record; the roles screen kept Read-only after the palette opened Staff).
 */
test.describe("a navigation to the screen on show opens it on the new address", () => {
  const strings = {
    en: { user: users.admin, details: "Details", roles: "Roles", readOnly: "قراءة فقط", staff: "الموظفون", readOnlyQuery: "Read-only", staffQuery: "Staff" },
    ar: { user: users.adminArabic, details: "التفاصيل", roles: "الأدوار", readOnly: "قراءة فقط", staff: "الموظفون", readOnlyQuery: "قراءة فقط", staffQuery: "الموظفون" },
  } as const;

  for (const language of ["en", "ar"] as const) {
    test(`record to record by the palette, G then U from a record, role to role (${language})`, async ({ page }) => {
      const s = strings[language];
      await freshStart(page, language);
      await signIn(page, s.user);
      await expect(page.locator("main h1")).toBeVisible();
      const palette = page.locator('[role="dialog"].palette');
      const details = page.getByRole("region", { name: s.details });
      const search = page.locator('main input.search[type="search"]');
      const open = async (query: string, name: RegExp) => {
        await page.keyboard.press("Control+K");
        await expect(palette).toBeVisible();
        await page.keyboard.type(query);
        const option = palette.getByRole("option", { name }).first();
        await expect(option).toBeVisible();
        for (let i = 0; i < 20 && (await option.getAttribute("aria-selected")) !== "true"; i++) await page.keyboard.press("ArrowDown");
        await expect(option).toHaveAttribute("aria-selected", "true");
        await page.keyboard.press("Enter");
        await expect(palette).toBeHidden();
      };

      // One user's record, then another's from it: the second user's search, row and record.
      await open("viewer@alnoor", /viewer@alnoor\.example/);
      await expect(page).toHaveURL(/\/identity\/users\/[0-9a-f-]{36}\?q=viewer%40alnoor\.example$/);
      await expect(details).toContainText(users.viewer);
      await open("noaccess@alnoor", /noaccess@alnoor\.example/);
      await expect(page).toHaveURL(/\/identity\/users\/[0-9a-f-]{36}\?q=noaccess%40alnoor\.example$/);
      await expect(search).toHaveValue("noaccess@alnoor.example");
      await expect(page.locator("table.list-grid tbody tr")).toHaveCount(1);
      await expect(page.locator("table.list-grid tbody tr").first()).toContainText(users.noAccess);
      await expect(details).toContainText(users.noAccess);
      await expect(details).not.toContainText(users.viewer);
      await expect(page.locator('main input[name="email"]')).toHaveValue(users.noAccess);

      // G then U from the record: the users list itself, nothing open, no search.
      await page.locator("main").focus();
      await page.keyboard.press("g");
      await page.keyboard.press("u");
      await expect(page).toHaveURL(/\/identity\/users$/);
      await expect(details).toHaveCount(0);
      await expect(search).toHaveValue("");

      // Back: the record the address names again.
      await page.goBack();
      await expect(page).toHaveURL(/\/identity\/users\/[0-9a-f-]{36}\?q=noaccess%40alnoor\.example$/);
      await expect(details).toContainText(users.noAccess);
      await expect(search).toHaveValue("noaccess@alnoor.example");

      // One role, then another from it.
      await open(s.readOnlyQuery, new RegExp(language === "en" ? "^Read-only" : `^${s.readOnly}`));
      await expect(page.locator("main h1")).toHaveText(s.roles);
      await expect(page.locator('main input[name="nameAr"]')).toHaveValue(s.readOnly);
      await open(s.staffQuery, new RegExp(language === "en" ? "^Staff" : `^${s.staff}`));
      await expect(page.locator('main input[name="nameAr"]')).toHaveValue(s.staff);
      await expect(page.locator(".list-row")).toHaveCount(1);
    });
  }
});

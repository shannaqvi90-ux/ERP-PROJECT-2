import { expect, test, type Page } from "@playwright/test";
import { checkAccessibility, expectFocusRing } from "./a11y";
import { freshStart, signIn, users } from "./demo";

const navigation = (page: Page, name = "Main navigation") => page.getByRole("navigation", { name });

async function setPreferences(page: Page, body: { language?: "en" | "ar"; numerals?: "latn" | "arab" }) {
  const response = await page.request.put("/api/identity/me/preferences", { data: body, headers: { "X-Erp-Request": "1" } });
  expect(response.ok()).toBe(true);
}

test.describe("app shell", () => {
  test("a fresh visit, with or without a stale session cookie, logs no error and no failed request", async ({ page, context }) => {
    const errors: string[] = [];
    page.on("console", (m) => m.type() === "error" && errors.push(m.text()));
    page.on("pageerror", (e) => errors.push(e.message));
    page.on("response", (r) => r.status() >= 400 && errors.push(`${r.status()} ${r.url()}`));
    await page.goto("/");
    await expect(page.locator('input[name="email"]')).toBeFocused();
    await context.addCookies([{ name: "erp_session", value: "A".repeat(43), url: page.url() }]);
    await page.goto("/");
    await expect(page.locator('input[name="email"]')).toBeFocused();
    expect(errors).toEqual([]);
  });

  test("switch to Arabic in one click on a working screen: everything mirrors at once, records stay, and it survives an immediate reload", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.viewer);
    await navigation(page).getByRole("link", { name: "Users" }).click();
    await expect(page.locator("table tbody tr").first()).toBeVisible();
    try {
      const started = Date.now();
      await page.getByRole("button", { name: "العربية" }).click();
      await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
      await expect(page.getByRole("heading", { name: "المستخدمون" })).toBeVisible();
      await expect(page.locator("table thead")).toContainText("البريد الإلكتروني");
      console.log(`switch to Arabic on the users list: ${Date.now() - started} ms`);
      await expect(page.locator("table tbody tr").first()).toBeVisible();
      // Mirrored layout: the navigation pane moves to the right of the screen, the brand to the
      // right end of the top bar, and direction-implying icons flip.
      const nav = (await navigation(page, "التنقل الرئيسي").boundingBox())!;
      const main = (await page.locator("main").boundingBox())!;
      expect(nav.x).toBeGreaterThan(main.x);
      const flipped = await page.locator(".breadcrumbs .icon-directional").first().evaluate((el) => getComputedStyle(el).transform);
      expect(flipped).toContain("matrix(-1");
      // Reload straight away, before the preference request may have returned.
      await page.reload();
      await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
      await expect(page.getByRole("heading", { name: "المستخدمون" })).toBeVisible();
      await expect
        .poll(async () => (await (await page.request.get("/api/auth/session")).json()).user.language)
        .toBe("ar");
    } finally {
      await setPreferences(page, { language: "en" });
    }
  });

  test("keyboard only: the palette reaches any screen, Alt+M walks the navigation, ? lists every shortcut", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.getByRole("heading", { name: /Welcome/ })).toBeVisible();
    const started = Date.now();
    await page.keyboard.press("Control+K");
    await expect(page.getByRole("combobox")).toBeFocused();
    await page.keyboard.type("rol");
    await expect(page.getByRole("option").first()).toContainText("Roles");
    await page.keyboard.press("Enter");
    await expect(page.locator("main h1")).toHaveText("Roles");
    console.log(`palette to the Roles screen: ${Date.now() - started} ms, 3 steps, 6 keystrokes`);
    await expect(page).toHaveURL(/\/identity\/roles$/);
    await expect(page.getByRole("navigation", { name: "Breadcrumb" })).toContainText("Settings");

    await page.keyboard.press("Alt+M");
    await expect(navigation(page).getByRole("link", { name: "Roles" })).toBeFocused();
    await expectFocusRing(page, "navigation entry");
    await page.keyboard.press("ArrowDown");
    await expect(navigation(page).getByRole("link", { name: "Company access" })).toBeFocused();
    await page.keyboard.press("ArrowDown");
    await expect(navigation(page).getByRole("link", { name: "Workspace" })).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.locator("main h1")).toHaveText("Workspace");

    await page.keyboard.press("Alt+H");
    await expect(page.getByRole("heading", { name: /Welcome/ })).toBeVisible();

    await page.keyboard.press("Shift+?");
    const help = page.getByRole("dialog", { name: "Keyboard shortcuts" });
    await expect(help).toBeVisible();
    for (const action of ["Open the command palette", "Switch language", "Move to the navigation pane", "Go to the home screen", "Open my preferences"]) {
      await expect(help).toContainText(action);
    }
    await page.keyboard.press("Escape");
    await expect(help).toHaveCount(0);
  });

  test("shortcuts work with an Arabic keyboard layout (matched by key position)", async ({ page }) => {
    await freshStart(page, "ar");
    await signIn(page, users.adminArabic);
    await expect(page.getByRole("heading", { name: /مرحبًا/ })).toBeVisible();
    // Ctrl and the key that types "ن" on an Arabic layout (the K position).
    await page.evaluate(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "ن", code: "KeyK", ctrlKey: true, bubbles: true, cancelable: true })));
    await expect(page.getByRole("combobox")).toBeFocused();
    await page.keyboard.type("الادوار");
    await expect(page.getByRole("option").first()).toContainText("الأدوار");
    await page.keyboard.press("Enter");
    await expect(page.locator("main h1")).toHaveText("الأدوار");
  });

  test("the palette finds a record and opens it", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.getByRole("heading", { name: /Welcome/ })).toBeVisible();
    await page.keyboard.press("Control+K");
    await page.keyboard.type("viewer@alnoor");
    const option = page.getByRole("option", { name: /Omar Haddad/ });
    await expect(option).toBeVisible();
    await option.click();
    await expect(page).toHaveURL(/\/identity\/users\?search=viewer%40alnoor\.example$/);
    await expect(page.locator("table tbody tr")).toHaveCount(1);
    await expect(page.locator("table tbody tr").first()).toContainText(users.viewer);
  });

  test("the palette offers a user with no roles nothing they cannot open", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.noAccess);
    await expect(page.getByRole("heading", { name: /Welcome/ })).toBeVisible();
    await page.keyboard.press("Control+K");
    await page.keyboard.type("users");
    await expect(page.getByText("Nothing matches “users”.")).toBeVisible();
    await expect(page.getByRole("option")).toHaveCount(0);
    await page.keyboard.press("Escape");
    await page.keyboard.press("Control+K");
    const offered = await page.getByRole("option").allTextContents();
    expect(offered.join(" ")).not.toMatch(/Users|Roles|Workspace/);
  });

  test("Arabic-Indic digits are a per-user choice that follows the user", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.viewer);
    await expect(page.getByRole("heading", { name: /Welcome/ })).toBeVisible();
    try {
      await page.keyboard.press("Alt+P");
      const dialog = page.getByRole("dialog");
      await expect(dialog).toHaveAccessibleName("Preferences");
      await dialog.getByLabel("العربية").check();
      await expect(dialog).toHaveAccessibleName("التفضيلات");
      await dialog.getByLabel("هندية (٠١٢٣)").check();
      await expect(dialog.getByRole("status")).toContainText("حُفظ في ملفك الشخصي");
      await page.keyboard.press("Escape");
      await navigation(page, "التنقل الرئيسي").getByRole("link", { name: "المستخدمون" }).click();
      await expect(page.locator(".screen-header .muted")).toHaveText(/^[٠-٩٬]+ مستخدم/);
      // A new sign-in, on a device that never saw the choice, brings it back.
      await page.getByRole("button", { name: "تسجيل الخروج" }).click();
      await page.evaluate(() => localStorage.clear());
      await page.goto("/");
      await signIn(page, users.viewer);
      await expect(page.getByRole("heading", { name: /مرحبًا/ })).toBeVisible();
      await navigation(page, "التنقل الرئيسي").getByRole("link", { name: "المستخدمون" }).click();
      await expect(page.locator(".screen-header .muted")).toHaveText(/^[٠-٩٬]+ مستخدم/);
    } finally {
      await setPreferences(page, { language: "en", numerals: "latn" });
    }
  });

  test("printing shows the screen's content with a letterhead and none of the app's chrome", async ({ page }) => {
    await freshStart(page, "ar");
    await signIn(page, users.adminArabic);
    await navigation(page, "التنقل الرئيسي").getByRole("link", { name: "الأدوار" }).click();
    await expect(page.locator("table tbody tr").first()).toBeVisible();
    await page.emulateMedia({ media: "print" });
    await expect(page.locator(".topbar")).toBeHidden();
    await expect(page.locator(".navpane")).toBeHidden();
    await expect(page.locator(".statusbar")).toBeHidden();
    await expect(page.locator(".print-screen-head")).toBeVisible();
    await expect(page.locator(".print-screen-head")).toContainText("شركة النور للتجارة ذ.م.م");
    await expect(page.locator("main h1")).toHaveText("الأدوار");
    expect(await page.evaluate(() => getComputedStyle(document.querySelector("main")!).direction)).toBe("rtl");
    await page.emulateMedia({ media: "screen" });
  });

  for (const language of ["en", "ar"] as const) {
    test(`every shell screen and dialog meets the accessibility basics (${language})`, async ({ page }) => {
      await freshStart(page, language);
      let checked = await checkAccessibility(page, `sign-in (${language})`);
      await page.keyboard.press("Tab");
      await expectFocusRing(page, "sign-in, after Tab");
      await page.keyboard.press("Shift+Tab");
      await signIn(page, language === "ar" ? users.adminArabic : users.admin);
      await expect(page.locator("main h1")).toBeVisible();
      checked += await checkAccessibility(page, `home (${language})`);
      const links = await navigation(page, language === "ar" ? "التنقل الرئيسي" : "Main navigation").locator("a").evaluateAll((as) => as.map((a) => a.getAttribute("href")!));
      for (const href of links) {
        await navigation(page, language === "ar" ? "التنقل الرئيسي" : "Main navigation").locator(`a[href="${href}"]`).click();
        await expect(page.locator("main table tbody tr, main dl dd").first()).toBeVisible();
        checked += await checkAccessibility(page, `${href} (${language})`);
      }
      await page.keyboard.press("Control+K");
      // The palette's own box (screens such as the workspace settings have selects too).
      await expect(page.getByRole("dialog").getByRole("combobox")).toBeFocused();
      checked += await checkAccessibility(page, `command palette (${language})`);
      await page.keyboard.press("Escape");
      await page.keyboard.press("Control+/");
      await expect(page.getByRole("dialog")).toBeVisible();
      checked += await checkAccessibility(page, `shortcut help (${language})`);
      await page.keyboard.press("Escape");
      await page.keyboard.press("Alt+P");
      await expect(page.getByRole("dialog")).toBeVisible();
      checked += await checkAccessibility(page, `preferences (${language})`);
      await page.keyboard.press("Escape");
      expect(checked).toBeGreaterThan(100);
    });
  }
});

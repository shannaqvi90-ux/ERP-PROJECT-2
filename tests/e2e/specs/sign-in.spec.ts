import { expect, test } from "@playwright/test";
import { freshStart, paceSignIn, password, signIn, users } from "./demo";

test.describe("sign in to an empty workspace", () => {
  test("keyboard only, in English, lands in the workspace with its menu", async ({ page }) => {
    await freshStart(page, "en");
    await expect(page.locator("html")).toHaveAttribute("dir", "ltr");
    const started = Date.now();
    await signIn(page, users.admin);
    await expect(page.getByRole("heading", { name: "Welcome, Mariam Al Mansoori" })).toBeVisible();
    console.log(`sign-in to workspace: ${Date.now() - started} ms`);
    const nav = page.getByRole("navigation", { name: "Main navigation" });
    await expect(nav.getByRole("link")).toHaveText(["Users", "Roles", "Company access", "Companies", "Branches", "Workspace", "My account"]);
    await expect(page.locator(".workspace-name")).toHaveText("Al Noor Trading LLC");
  });

  test("in Arabic, right to left, from the first screen", async ({ page }) => {
    await freshStart(page, "en");
    await page.getByRole("button", { name: "العربية" }).click();
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await expect(page.locator("html")).toHaveAttribute("lang", "ar");
    await expect(page.getByRole("button", { name: "تسجيل الدخول" })).toBeVisible();
    await expect(page.getByText("البريد الإلكتروني", { exact: true })).toBeVisible();
    await signIn(page, users.adminArabic);
    await expect(page.getByRole("heading", { name: "مرحبًا، فاطمة الزعابي" })).toBeVisible();
    await expect(page.locator(".workspace-name")).toHaveText("شركة النور للتجارة ذ.م.م");
    await expect(page.getByRole("navigation").getByRole("link")).toHaveText(["المستخدمون", "الأدوار", "الوصول إلى الشركات", "الشركات", "الفروع", "مساحة العمل", "حسابي"]);
    const topbar = await page.locator(".topbar").boundingBox();
    const brand = await page.locator(".brand").boundingBox();
    expect(brand!.x).toBeGreaterThan(topbar!.width / 2); // mirrored: the brand sits on the right
  });

  test("a wrong password shows one message in the screen's language and clears the password", async ({ page }) => {
    await freshStart(page, "ar");
    await signIn(page, users.admin, "not-the-password");
    await expect(page.getByRole("alert")).toContainText("تعذّر تسجيل الدخول");
    await expect(page.locator('input[name="password"]')).toHaveValue("");
    await expect(page.locator('input[name="password"]')).toBeFocused();
  });

  test("a failed sign-in message follows a switch to Arabic, right to left, with nothing left in English", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin, "not-the-password");
    await expect(page.getByRole("alert")).toHaveText("Sign-in failed. Check your e-mail and password and try again.");
    await page.getByRole("button", { name: "العربية" }).click();
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await expect(page.getByRole("alert")).toHaveText("تعذّر تسجيل الدخول. تحقّق من البريد الإلكتروني وكلمة المرور ثم حاول مرة أخرى.");
  });

  test("empty fields are explained without a round trip", async ({ page }) => {
    await freshStart(page, "en");
    await page.keyboard.press("Enter");
    await expect(page.getByText("Enter your e-mail.")).toBeVisible();
  });

  test("the device remembers the e-mail, so the next sign-in is password then Enter; signing out forgets it", async ({ page, context }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.getByRole("heading", { name: /Welcome/ })).toBeVisible();
    // The session ends without signing out (it expired, or the browser was closed): the same
    // person returns to this device.
    await context.clearCookies();
    await page.goto("/");
    const passwordField = page.locator('input[name="password"]');
    await expect(passwordField).toBeFocused();
    await expect(page.locator('input[name="email"]')).toHaveValue(users.admin);
    await page.keyboard.type(password);
    await page.keyboard.press("Enter");
    await expect(page.getByRole("heading", { name: /Welcome/ })).toBeVisible();
    // Signing out on a shared device leaves nothing of this person for the next one.
    await page.getByRole("button", { name: "Sign out" }).click();
    await expect(page.locator('input[name="email"]')).toBeFocused();
    await expect(page.locator('input[name="email"]')).toHaveValue("");
    expect(await page.evaluate(() => localStorage.getItem("erp.lastEmail"))).toBeNull();
  });

  test("first visit on the team's sign-in address: the part before @, Enter, the password, Enter", async ({ page }) => {
    await freshStart(page, "en");
    // The address every user of the team is given (My account, set-up hand-over).
    await page.goto("/?domain=alnoor.example");
    const email = page.locator('input[name="email"]');
    await expect(email).toBeFocused();
    await expect(page.locator("#email-domain")).toContainText("@alnoor.example");
    await paceSignIn(page);
    await page.keyboard.type("admin");
    await page.keyboard.press("Enter");
    await expect(page.locator('input[name="password"]')).toBeFocused();
    await expect(page.locator(".field-error")).toHaveCount(0);
    await page.keyboard.type(password);
    await page.keyboard.press("Enter");
    await expect(page.getByRole("heading", { name: "Welcome, Mariam Al Mansoori" })).toBeVisible();
    expect(await page.evaluate(() => localStorage.getItem("erp.lastEmail"))).toBe(users.admin);
    // My account shows the same address to copy and share.
    await page.getByRole("navigation", { name: "Main navigation" }).getByRole("link", { name: "My account" }).click();
    await expect(page.getByTestId("team-address")).toHaveText(/\/\?domain=alnoor\.example$/);
  });

  test("returning on the team's sign-in address: the whole remembered e-mail, the password focused, password then Enter", async ({ page, context }) => {
    await freshStart(page, "en");
    await page.goto("/?domain=alnoor.example");
    await paceSignIn(page);
    await page.keyboard.type("admin");
    await page.keyboard.press("Enter");
    await page.keyboard.type(password);
    await page.keyboard.press("Enter");
    await expect(page.getByRole("heading", { name: /Welcome/ })).toBeVisible();
    // The session ends without signing out; the person comes back to the team's address.
    await context.clearCookies();
    await page.goto("/?domain=alnoor.example");
    await expect(page.locator('input[name="password"]')).toBeFocused();
    await expect(page.locator('input[name="email"]')).toHaveValue(users.admin);
    await expect(page.locator("#email-domain")).toHaveCount(0);
    await paceSignIn(page);
    await page.keyboard.type(password);
    await page.keyboard.press("Enter");
    await expect(page.getByRole("heading", { name: "Welcome, Mariam Al Mansoori" })).toBeVisible();
  });

  test("on the team's sign-in address in Arabic the domain stays left to right after the field", async ({ page }) => {
    await freshStart(page, "ar");
    await page.goto("/?domain=alnoor.example");
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    const field = await page.locator('input[name="email"]').boundingBox();
    const domain = await page.locator("#email-domain").boundingBox();
    expect(domain!.x).toBeGreaterThan(field!.x); // e-mail addresses read left to right in both languages
  });
});

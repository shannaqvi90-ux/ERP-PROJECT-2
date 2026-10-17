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
    await expect(nav.getByRole("link")).toHaveText(["Reports", "Users", "Roles", "Company access", "Companies", "Branches", "Workspace", "My account"]);
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
    await expect(page.getByRole("navigation").getByRole("link")).toHaveText(["التقارير", "المستخدمون", "الأدوار", "الوصول إلى الشركات", "الشركات", "الفروع", "مساحة العمل", "حسابي"]);
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

  // Routed by p00's critics (rounds 7 to 10): after repeated failures the right password got only
  // "Sign-in failed…", with no word that sign-in was paused or for how long. Its own account, so
  // no other test meets the pause; its own browser, never signed in to the account, so the
  // failures count against this client.
  test("after repeated failures the right password is told sign-in is paused and until when, in English and Arabic, until an administrator allows it again", async ({ page, browser }) => {
    const tag = Math.random().toString(36).slice(2, 8);
    const email = `e2e.paused.${tag}@alnoor.example`;
    const own = "Paused-Pass-2026";
    const headers = { "X-Erp-Request": "1" };
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
    const created = await page.request.post("/api/identity/users", {
      headers,
      data: { email, displayName: `Paused ${tag}`, language: "en", password: own, mustChangePassword: false, roleIds: [] },
    });
    expect(created.status()).toBe(201);
    const { id } = (await created.json()) as { id: string };

    const context = await browser.newContext({ baseURL: test.info().project.use.baseURL, locale: "en-US" });
    const other = await context.newPage();
    await freshStart(other, "en");
    const alert = other.getByRole("alert");
    const passwordField = other.locator('input[name="password"]');
    await paceSignIn(other);
    await other.keyboard.type(email);
    await other.keyboard.press("Tab");
    await other.keyboard.type("not-the-password");
    await other.keyboard.press("Enter");
    await expect(alert).toContainText("Sign-in failed");
    let failures = 1;
    while (!(await alert.textContent())?.includes("paused") && failures < 8) {
      await passwordField.focus();
      await paceSignIn(other);
      await other.keyboard.type(`wrong-${failures}`);
      await other.keyboard.press("Enter");
      failures++;
      await expect(passwordField).toHaveValue("");
    }
    // The right password now: paused, until when, and what to do, not "check your password".
    await passwordField.focus();
    await paceSignIn(other);
    await other.keyboard.type(own);
    await other.keyboard.press("Enter");
    await expect(alert).toContainText("Sign-in with it from here is paused until");
    await expect(alert).toContainText("ask your administrator to allow sign-in again");
    await expect(alert).not.toContainText("Sign-in failed");
    await expect(other.getByRole("button", { name: "Sign out" })).toHaveCount(0);
    await other.getByRole("button", { name: "العربية" }).click();
    await expect(other.locator("html")).toHaveAttribute("dir", "rtl");
    await expect(alert).toContainText("توقف تسجيل الدخول به من هنا حتى");

    // The administrator allows sign-in again: the right password signs in from that browser.
    expect((await page.request.post(`/api/identity/users/${id}/unblock`, { headers })).status()).toBe(204);
    await passwordField.focus();
    await paceSignIn(other);
    await other.keyboard.type(own);
    await other.keyboard.press("Enter");
    await expect(other.getByRole("button", { name: /^(Sign out|تسجيل الخروج)$/ })).toBeVisible();
    await context.close();
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

  test("first visit on the team's sign-in address: the whole e-mail moves on by itself, the password, Enter", async ({ page }) => {
    await freshStart(page, "en");
    await page.goto("/?domain=alnoor.example");
    const email = page.locator('input[name="email"]');
    await expect(email).toBeFocused();
    await expect(page.locator("#email-moves-on")).toHaveText("Typing your whole address moves on to the password.");
    await paceSignIn(page);
    // Typed as a person types it, key by key: the screen moves on when the address is whole.
    await page.keyboard.type(users.admin);
    await expect(page.locator('input[name="password"]')).toBeFocused();
    await expect(email).toHaveValue(users.admin);
    await page.keyboard.type(password);
    await page.keyboard.press("Enter");
    await expect(page.getByRole("heading", { name: "Welcome, Mariam Al Mansoori" })).toBeVisible();
    expect(await page.evaluate(() => localStorage.getItem("erp.lastEmail"))).toBe(users.admin);
  });

  test("returning on the team's sign-in address: the whole remembered e-mail, the password focused, password then Enter", async ({ page, context }) => {
    await freshStart(page, "en");
    await page.goto("/?domain=alnoor.example");
    // The screen is ready for the keyboard before anything is typed.
    await expect(page.locator('input[name="email"]')).toBeFocused();
    await expect(page.locator("#email-domain")).toContainText("@alnoor.example");
    await paceSignIn(page);
    await page.keyboard.type("admin");
    await page.keyboard.press("Enter");
    await expect(page.locator('input[name="password"]')).toBeFocused();
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

  // Critic p00 round 8: from the plain address a whole e-mail did not move on (4 steps, as the
  // reference). A shared counter PC, after the Sign out button: the device knows its workspace.
  test("a shared PC, after Sign out, on the plain address: the next person's whole e-mail moves on, the password, Enter", async ({ page, browser }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.getByRole("heading", { name: "Welcome, Mariam Al Mansoori" })).toBeVisible();
    await page.getByRole("button", { name: "Sign out" }).click();
    const email = page.locator('input[name="email"]');
    await expect(email).toBeFocused();
    await expect(email).toHaveValue("");
    // Nothing readable of the last person or the workspace is kept on the device.
    const kept = await page.evaluate(() => Object.keys(localStorage).map((k) => `${k}=${localStorage.getItem(k)}`).join("\n"));
    expect(kept).not.toMatch(/alnoor|admin@|Mariam/i);
    await page.goto("/");
    await expect(email).toBeFocused();
    await expect(page.locator("#email-moves-on")).toHaveText("On this device, typing a whole address of a workspace you signed in to here moves on to the password.");
    await paceSignIn(page);
    // Three actions: the whole e-mail (typed key by key, the screen moves on), the password, Enter.
    await page.keyboard.type(users.viewer);
    await expect(page.locator('input[name="password"]')).toBeFocused();
    await page.keyboard.type(password);
    await page.keyboard.press("Enter");
    await expect(page.getByRole("heading", { name: /Welcome/ })).toBeVisible();
    await expect(page.locator(".workspace-name")).toHaveText("Al Noor Trading LLC");

    // A person who leaves the field by habit (Tab after the e-mail) still lands in the password.
    await page.getByRole("button", { name: "Sign out" }).click();
    await expect(email).toBeFocused();
    await paceSignIn(page);
    await page.keyboard.type(users.admin);
    await page.keyboard.press("Tab");
    await expect(page.locator('input[name="password"]')).toBeFocused();
    await page.keyboard.type(password);
    await page.keyboard.press("Enter");
    await expect(page.getByRole("heading", { name: "Welcome, Mariam Al Mansoori" })).toBeVisible();

    // A new device knows nothing: there the whole e-mail stays in its field.
    const other = await browser.newContext();
    try {
      const fresh = await other.newPage();
      await fresh.goto("/");
      await expect(fresh.locator('input[name="email"]')).toBeFocused();
      await expect(fresh.locator("#email-moves-on")).toHaveCount(0);
      await fresh.keyboard.type(users.viewer);
      await expect(fresh.locator('input[name="email"]')).toBeFocused();
    } finally {
      await other.close();
    }
  });

  test("the password can be shown from the keyboard to check it, then hidden, and still signs in", async ({ page }) => {
    await freshStart(page, "en");
    await page.keyboard.type(users.admin);
    await page.keyboard.press("Tab");
    await page.keyboard.type(password);
    const field = page.locator('input[name="password"]');
    await expect(field).toHaveAttribute("type", "password");
    // Tab reaches the Show button right after the field; Space presses it and the focus returns.
    await page.keyboard.press("Tab");
    await expect(page.getByRole("button", { name: "Show the password" })).toBeFocused();
    await page.keyboard.press("Space");
    await expect(field).toHaveAttribute("type", "text");
    await expect(field).toHaveValue(password);
    await expect(field).toBeFocused();
    await page.getByRole("button", { name: "Hide the password" }).click();
    await expect(field).toHaveAttribute("type", "password");
    await paceSignIn(page);
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

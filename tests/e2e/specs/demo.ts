import { expect, test, type Page } from "@playwright/test";

/** Demo sign-ins seeded by `./erp up` (see README). */
export const password = process.env.ERP_DEMO_PASSWORD ?? "Demo-Pass-2026";
export const users = {
  admin: "admin@alnoor.example",
  adminArabic: "admin.ar@alnoor.example",
  viewer: "viewer@alnoor.example",
  noAccess: "noaccess@alnoor.example",
};

export async function freshStart(page: Page, language: "en" | "ar" = "en") {
  await page.goto("/");
  await page.evaluate((lang) => {
    localStorage.clear();
    localStorage.setItem("erp.language", lang);
  }, language);
  await page.goto("/");
  // The sign-in screen is ready for the keyboard: the e-mail field has focus.
  await expect(page.locator('input[name="email"]')).toBeFocused();
}

/**
 * The server allows 30 sign-in requests a minute from one client (Erp:RateLimits:SignInPerMinute)
 * and the whole suite signs in from one address, one test after another (one worker). The helper
 * keeps its own sign-ins under that budget, with room for the few tests that sign in by hand, so a
 * long suite never meets the limit; the limit itself is tested by the gate suite.
 */
// 20 of the 30: room for the sign-ins some tests send by hand (an API client, a passkey the screen
// asks for), so a minute holding many paced sign-ins never meets the limit.
const signInBudget = 20;
const signInWindowMs = 61_000;
const recentSignIns: number[] = [];

export async function paceSignIn(page: Page) {
  for (;;) {
    const now = Date.now();
    while (recentSignIns.length > 0 && now - recentSignIns[0]! >= signInWindowMs) recentSignIns.shift();
    if (recentSignIns.length < signInBudget) break;
    const wait = signInWindowMs - (now - recentSignIns[0]!) + 50;
    // The pause is the budget's, not the test's: the test keeps its own time limit on top.
    test.info().setTimeout(test.info().timeout + wait);
    await page.waitForTimeout(wait);
  }
  recentSignIns.push(Date.now());
}

/** Keyboard-only sign-in: type the e-mail, Tab, type the password, Enter. */
export async function signIn(page: Page, email: string, secret = password) {
  const emailField = page.locator('input[name="email"]');
  await expect(emailField).toBeFocused();
  await paceSignIn(page);
  await page.keyboard.type(email);
  await page.keyboard.press("Tab");
  await page.keyboard.type(secret);
  await page.keyboard.press("Enter");
}

import { expect, type Page } from "@playwright/test";

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
}

/** Keyboard-only sign-in: type the e-mail, Tab, type the password, Enter. */
export async function signIn(page: Page, email: string, secret = password) {
  const emailField = page.locator('input[name="email"]');
  await expect(emailField).toBeFocused();
  await page.keyboard.type(email);
  await page.keyboard.press("Tab");
  await page.keyboard.type(secret);
  await page.keyboard.press("Enter");
}

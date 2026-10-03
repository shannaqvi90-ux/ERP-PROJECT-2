import { expect, test } from "@playwright/test";
import { freshStart, signIn, users } from "./demo";

const unique = () => Math.random().toString(36).slice(2, 8);

test.describe("users, roles and permissions", () => {
  test("an administrator creates a user with a restricted role from the keyboard, and that user sees and can do only what it grants", async ({ page, browser }) => {
    const local = `e2e.clerk.${unique()}`;
    const email = `${local}@alnoor.example`;
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await page.locator('nav a[href="/identity/users"]').first().click();
    await expect(page.locator("table tbody tr").first()).toBeVisible();

    // n: new user; type only the part before @; Tab completes the address and suggests the name.
    await page.locator("main h1").click();
    await page.keyboard.press("n");
    await expect(page.locator('input[name="email"]')).toBeFocused();
    await page.keyboard.type(local);
    await page.keyboard.press("Tab");
    await expect(page.locator('input[name="email"]')).toHaveValue(email);
    await expect(page.locator('input[name="displayName"]')).toHaveValue(/^E2e Clerk /);
    await page.getByRole("searchbox", { name: "Find a role" }).fill("read-only");
    await page.keyboard.press("Enter");
    await expect(page.getByRole("checkbox", { name: /Read-only/ })).toBeChecked();
    await page.keyboard.press("Control+Enter");

    const code = (await page.getByTestId("setup-code").textContent())!.trim();
    expect(code).toMatch(/^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$/);
    await expect(page.getByText("Invited").first()).toBeVisible();

    // The new user signs in with the set-up code and chooses a password.
    const context = await browser.newContext({ baseURL: test.info().project.use.baseURL, locale: "en-US" });
    const clerk = await context.newPage();
    await clerk.goto(`/?email=${encodeURIComponent(email)}`);
    await expect(clerk.locator('input[name="password"]')).toBeFocused();
    await clerk.keyboard.type(code);
    await clerk.keyboard.press("Enter");
    await expect(clerk.locator('input[name="newPassword"]')).toBeFocused();
    await clerk.keyboard.type("Clerk-Own-Pass-2026");
    await clerk.keyboard.press("Tab");
    await clerk.keyboard.type("Clerk-Own-Pass-2026");
    await clerk.keyboard.press("Enter");
    await expect(clerk.getByRole("button", { name: "Sign out" })).toBeVisible();

    // Read-only: the users screen opens, but nothing that changes data is offered.
    await clerk.locator('nav a[href="/identity/users"]').first().click();
    await expect(clerk.locator("table tbody tr").first()).toBeVisible();
    await expect(clerk.getByRole("button", { name: "New user" })).toHaveCount(0);
    await clerk.locator("table tbody tr").first().click();
    await expect(clerk.locator("aside h2")).toBeVisible();
    await expect(clerk.getByRole("button", { name: "Save" })).toHaveCount(0);
    await expect(clerk.getByRole("button", { name: "Reset password…" })).toHaveCount(0);
    // And the API refuses what the screen does not offer.
    const refused = await clerk.request.post("/api/identity/users", {
      headers: { "X-Erp-Request": "1" },
      data: { email: `x.${unique()}@alnoor.example`, displayName: "X", language: "en", roleIds: [] },
    });
    expect(refused.status()).toBe(403);
    await context.close();

    // Back with the administrator: the clerk is no longer pending and their access is explained.
    await page.reload();
    await page.getByRole("searchbox", { name: "Search by name or e-mail" }).fill(local);
    await expect(page.locator("table tbody tr")).toHaveCount(1);
    await page.getByRole("searchbox", { name: "Search by name or e-mail" }).press("Enter");
    await page.getByRole("tab", { name: "What they can do" }).click();
    await expect(page.getByRole("cell", { name: "View users" })).toBeVisible();
    await expect(page.getByRole("cell", { name: "Create users" })).toHaveCount(0);
    await page.getByRole("tab", { name: "Sign-in history" }).click();
    await expect(page.getByText("Signed in", { exact: true }).first()).toBeVisible();
  });

  test("a role is built in the permission matrix with search and a bulk toggle, then copied and deleted", async ({ page }) => {
    const name = `E2E role ${unique()}`;
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await page.locator('nav a[href="/identity/roles"]').first().click();
    await expect(page.locator("table tbody tr").first()).toBeVisible();
    await page.locator("main h1").click();
    await page.keyboard.press("n");
    await expect(page.locator('input[name="nameEn"]')).toBeFocused();
    await page.keyboard.type(name);
    await page.locator('input[name="nameAr"]').fill(`دور ${name}`);
    await page.getByRole("searchbox", { name: "Search permissions" }).fill("users");
    await page.getByRole("button", { name: "Select all shown" }).click();
    await page.getByRole("searchbox", { name: "Search permissions" }).fill("");
    await expect(page.getByRole("checkbox", { name: "View users" })).toBeChecked();
    await expect(page.getByRole("checkbox", { name: "View roles and permissions" })).not.toBeChecked();
    await page.keyboard.press("Control+Enter");
    await expect(page.getByText(`Role ${name} saved.`)).toBeVisible();

    await page.getByRole("button", { name: "Copy role" }).click();
    await page.keyboard.press("Control+a");
    await page.keyboard.type(`${name} copy`);
    await page.keyboard.press("Enter");
    await expect(page.getByText(`Role copied as ${name} copy.`)).toBeVisible();
    await page.getByRole("button", { name: "Delete role" }).click();
    await page.locator(".id-confirm").getByRole("button", { name: "Delete", exact: true }).click();
    await expect(page.getByText(`Role ${name} copy deleted.`)).toBeVisible();
  });

  test("the Administrator role and the matrix read right to left in Arabic", async ({ page }) => {
    await freshStart(page, "ar");
    await signIn(page, users.adminArabic);
    await page.locator('nav a[href="/identity/roles"]').first().click();
    await page.locator("table tbody tr", { hasText: "مدير النظام" }).click();
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await expect(page.getByRole("button", { name: "نسخ الدور" })).toBeVisible();
    await expect(page.getByRole("button", { name: "حفظ" })).toHaveCount(0);
    await expect(page.getByRole("checkbox", { name: "عرض المستخدمين" })).toBeChecked();
    await expect(page.getByRole("checkbox", { name: "عرض المستخدمين" })).toBeDisabled();
  });
});

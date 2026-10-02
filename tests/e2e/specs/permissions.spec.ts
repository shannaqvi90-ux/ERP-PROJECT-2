import { expect, test } from "@playwright/test";
import { freshStart, signIn, users } from "./demo";

test.describe("screens hide what the user cannot do", () => {
  test("a user whose roles grant nothing sees no menu and cannot open a screen by address", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.noAccess);
    await expect(page.getByText("No areas are open to you yet.")).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Main navigation" }).getByRole("link")).toHaveCount(0);
    await page.goto("/identity/users");
    await expect(page.getByRole("heading", { name: "No access" })).toBeVisible();
    await expect(page.locator("table")).toHaveCount(0);
  });

  test("a read-only user can browse users, and the list is server-paged and searchable", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.viewer);
    await page.getByRole("link", { name: "Users" }).first().click();
    await expect(page).toHaveURL(/\/identity\/users$/);
    await expect(page.locator("table tbody tr").first()).toBeVisible();
    await page.getByRole("searchbox", { name: "Search by name or e-mail" }).fill("viewer@alnoor");
    await expect(page.locator("table tbody tr")).toHaveCount(1);
    await expect(page.locator("table tbody tr").first()).toContainText(users.viewer);
  });

  test("signing out returns to the sign-in screen and the session is gone", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await page.getByRole("button", { name: "Sign out" }).click();
    await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();
    const session = await page.request.get("/api/auth/session");
    expect((await session.json()).authenticated).toBe(false);
  });
});

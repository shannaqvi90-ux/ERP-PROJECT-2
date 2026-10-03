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

    // The list arrives with the cursor in its search box; Alt+N starts a new user from there
    // (a plain n would be typed into the search). Type only the part before @; Tab completes
    // the address and suggests the name.
    await expect(page.getByRole("searchbox", { name: "Search by name or e-mail" })).toBeFocused();
    await page.keyboard.press("Alt+n");
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
    // Roles too: no New role, and neither n nor Alt+N opens a new role.
    await clerk.keyboard.press("Escape");
    await clerk.locator('nav a[href="/identity/roles"]').first().click();
    await expect(clerk.locator("table tbody tr").first()).toBeVisible();
    await expect(clerk.getByRole("button", { name: "New role" })).toHaveCount(0);
    await clerk.keyboard.press("Alt+n");
    await clerk.locator("main h1").click();
    await clerk.keyboard.press("n");
    await expect(clerk.locator('input[name="nameEn"]')).toHaveCount(0);
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
    // A plain n starts a new role when no field has the focus.
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

  test("a user who may change roles but not delete them sees no Delete role, and a role granting more than they hold is read-only", async ({ page, browser }) => {
    const tag = unique();
    const headers = { "X-Erp-Request": "1" };
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
    const create = async (url: string, data: object) => {
      const response = await page.request.post(url, { headers, data });
      expect(response.status(), `POST ${url}`).toBe(201);
      return (await response.json()) as { id: string };
    };
    const keeper = await create("/api/identity/roles", {
      nameEn: `Role keeper ${tag}`,
      nameAr: `حافظ الأدوار ${tag}`,
      permissions: ["identity.roles.read", "identity.roles.update", "identity.roles.create", "identity.users.read"],
    });
    const plain = await create("/api/identity/roles", { nameEn: `Viewers ${tag}`, nameAr: `مشاهدون ${tag}`, permissions: ["identity.users.read"] });
    await create("/api/identity/roles", {
      nameEn: `Password desk ${tag}`,
      nameAr: `مكتب كلمات المرور ${tag}`,
      permissions: ["identity.users.read", "identity.users.resetPassword"],
    });
    const email = `e2e.keeper.${tag}@alnoor.example`;
    await create("/api/identity/users", { email, displayName: `Role Keeper ${tag}`, language: "en", password: "Keeper-Pass-2026", mustChangePassword: false, roleIds: [keeper.id] });

    const context = await browser.newContext({ baseURL: test.info().project.use.baseURL, locale: "en-US" });
    const other = await context.newPage();
    await freshStart(other, "en");
    await signIn(other, email, "Keeper-Pass-2026");
    await other.locator('nav a[href="/identity/roles"]').first().click();
    await other.locator("table tbody tr", { hasText: `Viewers ${tag}` }).click();
    await expect(other.getByRole("button", { name: "Save" })).toBeVisible();
    await expect(other.getByRole("button", { name: "Copy role" })).toBeVisible();
    await expect(other.getByRole("button", { name: "Delete role" })).toHaveCount(0);
    // The API refuses what the screen does not offer.
    expect((await other.request.delete(`/api/identity/roles/${plain.id}`, { headers })).status()).toBe(403);

    await other.keyboard.press("Escape");
    await other.locator("table tbody tr", { hasText: `Password desk ${tag}` }).click();
    await expect(other.getByText("only someone who holds all of them can change, copy or delete it")).toBeVisible();
    await expect(other.getByRole("button", { name: "Save" })).toHaveCount(0);
    await expect(other.getByRole("button", { name: "Copy role" })).toHaveCount(0);
    await context.close();
  });

  test("an invitation sent to a mistyped address is corrected, and a user who never signed in is deleted", async ({ page }) => {
    const tag = unique();
    const headers = { "X-Erp-Request": "1" };
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
    const response = await page.request.post("/api/identity/users", {
      headers,
      data: { email: `e2e.typo.${tag}@alnor.example`, displayName: `Typo ${tag}`, language: "en", roleIds: [] },
    });
    expect(response.status()).toBe(201);
    const { id } = (await response.json()) as { id: string };
    await page.goto(`/identity/users?open=${id}`);
    const address = page.locator('aside input[name="email"]');
    await expect(address).toHaveValue(`e2e.typo.${tag}@alnor.example`);
    await address.fill(`e2e.typo.${tag}@alnoor.example`);
    await page.locator('aside input[name="displayNameAr"]').fill(`خطأ ${tag}`);
    await page.keyboard.press("Control+Enter");
    await expect(page.getByText("Saved.", { exact: false }).first()).toBeVisible();
    const saved = (await (await page.request.get(`/api/identity/users/${id}`)).json()) as { email: string; displayNameAr: string };
    expect(saved.email).toBe(`e2e.typo.${tag}@alnoor.example`);
    expect(saved.displayNameAr).toBe(`خطأ ${tag}`);

    await page.getByRole("button", { name: "Delete user" }).click();
    await page.locator(".id-confirm").getByRole("button", { name: "Delete", exact: true }).click();
    await expect(page.getByText(`Typo ${tag} deleted.`)).toBeVisible();
    expect((await page.request.get(`/api/identity/users/${id}`)).status()).toBe(404);
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

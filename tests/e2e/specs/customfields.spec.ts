import { expect, test, type Page } from "@playwright/test";
import { checkAccessibility } from "./a11y";
import { freshStart, signIn, users } from "./demo";

/** The rows of the shared list grid (not the tables inside the open record's form). */
const listRows = (page: Page) => page.locator("table[role=grid] tbody tr");

type Field = { id: string; key: string; labelEn: string; version: number; fieldType: string; isActive: boolean };

/** The demo's custom fields on contacts, read through the API as the screen does. */
async function fieldsOf(page: Page, filter = "isActive eq true"): Promise<Field[]> {
  const answer = await page.request.get(`/api/customfields/fields?take=200&filter=${encodeURIComponent(filter)}`);
  expect(answer.status(), await answer.text()).toBe(200);
  return ((await answer.json()) as { items: Field[] }).items;
}

/** Switches a field the test added off again (a field is switched off, never deleted). */
async function switchOff(page: Page, id: string) {
  const field = (await (await page.request.get(`/api/customfields/fields/${id}`)).json()) as Record<string, unknown>;
  const put = await page.request.put(`/api/customfields/fields/${id}`, {
    headers: { "X-Erp-Request": "1" },
    data: { labelEn: field.labelEn, labelAr: field.labelAr, required: field.required, default: field.default, options: field.type === "select" ? field.options : null,
      lookupTarget: field.lookupTarget, searchable: field.searchable, sortOrder: field.sortOrder, isActive: false, version: field.version },
  });
  expect(put.status(), await put.text()).toBe(200);
}

test.describe("custom fields", () => {
  test("an administrator adds a field from the keyboard, fills it on a contact and finds the contact by it", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.locator("main h1")).toBeVisible();

    // G then F from anywhere opens the definitions; the demo defines four fields on contacts.
    await page.locator("main").focus();
    await page.keyboard.press("g");
    await page.keyboard.press("f");
    await expect(page).toHaveURL(/\/customfields\/fields$/);
    await expect(page.locator("main h1")).toHaveText("Custom fields");
    await expect(listRows(page).filter({ hasText: "Customer segment" })).toBeVisible();
    await expect(listRows(page).filter({ hasText: "Account manager" })).toBeVisible();
    expect(await checkAccessibility(page, "custom fields list")).toBeGreaterThan(10);

    // Alt+N starts a new field, the cursor is in its label; Enter saves it.
    const run = Date.now() % 1000000;
    const label = `Licence ref ${run}`;
    const value = `LR-${run}`;
    await page.keyboard.press("Alt+KeyN");
    await expect(page.locator('.list-record input[name="labelEn"]')).toBeFocused();
    await page.keyboard.type(label);
    await page.keyboard.press("Enter");
    await expect(page.locator(".record-form .notice")).toHaveText("Saved.");
    const added = (await fieldsOf(page)).find((f) => f.labelEn === label)!;
    expect(added.fieldType).toBe("text");
    try {
      // The contact's form shows it by itself; fill it on the first contact the search finds.
      await page.locator('nav a[href="/contacts/contacts"]').click();
      await expect(page.locator(".list-search input")).toBeFocused();
      await page.keyboard.type("c-demo-1");
      await expect(listRows(page).filter({ hasText: "Falcon Building Materials LLC" }).first()).toBeVisible();
      await page.keyboard.press("Enter");
      await expect(page.locator(".record-header h2")).toHaveText("Falcon Building Materials LLC");
      const field = page.locator(`[data-field="customFields.${added.key}"] input`);
      await expect(field).toBeVisible();
      await field.fill(value);
      await page.keyboard.press("Control+KeyS");
      await expect(page.locator(".record-form .notice")).toHaveText("Saved.");

      // The list's search box finds the contact by the value; the column chooser offers the field.
      await page.locator(".list-search input").fill(value);
      await expect(listRows(page)).toHaveCount(1);
      await expect(listRows(page).first()).toContainText("Falcon Building Materials LLC");
      const filtered = await page.request.get(`/api/contacts/contacts?filter=${encodeURIComponent(`${added.key} eq '${value}'`)}`);
      expect(((await filtered.json()) as { total: number }).total).toBe(1);
    } finally {
      await switchOff(page, added.id);
    }
  });

  test("the demo's fields show on a contact and in the list in Arabic, and the list can be filtered by one", async ({ page }) => {
    await freshStart(page, "ar");
    await signIn(page, users.adminArabic);
    await expect(page.locator("main h1")).toBeVisible();
    await page.locator('nav a[href="/contacts/contacts"]').click();
    await expect(listRows(page).first()).toBeVisible();
    // The workspace's own Arabic labels are the list's column names once chosen; the form shows them too.
    const filter = encodeURIComponent("cfCustomerSegment eq 'government'");
    const government = (await (await page.request.get(`/api/contacts/contacts?take=5&filter=${filter}`)).json()) as { total: number; items: { id: string }[] };
    expect(government.total).toBeGreaterThan(100);
    await page.goto(`/contacts/contacts/${government.items[0]!.id}`);
    await expect(page.locator(".record-header h2")).toBeVisible();
    await expect(page.locator(".record-form")).toContainText("شريحة العميل");
    await expect(page.locator(".record-form")).toContainText("مدير الحساب");
    await expect(page.locator('[data-field="customFields.cfCustomerSegment"] select')).toHaveValue("government");
    expect(await page.evaluate(() => document.documentElement.dir)).toBe("rtl");
  });

  test("a reader sees the definitions but is offered no way to change them", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.viewer);
    await expect(page.locator("main h1")).toBeVisible();
    await page.locator('nav a[href="/customfields/fields"]').click();
    await expect(page.locator("main h1")).toHaveText("Custom fields");
    await expect(listRows(page).first()).toBeVisible();
    await expect(page.getByRole("button", { name: "New custom field" })).toHaveCount(0);
    const denied = await page.request.post("/api/customfields/fields", { headers: { "X-Erp-Request": "1" }, data: { recordType: "contacts.contacts", type: "text", labelEn: "Nope" } });
    expect(denied.status()).toBe(403);
  });
});

import { expect, test, type Page } from "@playwright/test";
import { checkAccessibility } from "./a11y";
import { freshStart, signIn, users } from "./demo";

/** The rows of the shared list grid (not the tables inside the open record's form). */
const listRows = (page: Page) => page.locator("table[role=grid] tbody tr");

async function openCompanies(page: Page) {
  await page.getByRole("navigation", { name: "Main navigation" }).getByRole("link", { name: "Companies" }).click();
  await expect(page).toHaveURL(/\/tenancy\/companies$/);
  await expect(listRows(page).first()).toBeVisible();
}

test.describe("record forms and printed documents", () => {
  test("a form tracks changes, asks before leaving, saves with Ctrl+S and moves to the next record with Alt+PageDown", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openCompanies(page);
    await listRows(page).first().click();
    const phone = page.locator('[data-field="phone"] input');
    await expect(phone).toBeVisible();
    const title = page.locator(".record-header h2");
    const first = (await title.textContent()) ?? "";
    const original = await phone.inputValue();
    const changed = original === "+971 4 555 0101" ? "+971 4 555 0102" : "+971 4 555 0101";

    await phone.fill(changed);
    await expect(page.locator(".record-header")).toContainText("Unsaved changes");
    // Leaving by the menu asks first, in the form's own dialog (as Escape does, never the browser's
    // confirmation): Escape keeps editing, and the change stays on screen.
    const nativeDialogs: string[] = [];
    page.on("dialog", (dialog) => {
      nativeDialogs.push(dialog.message());
      void dialog.dismiss();
    });
    await page.getByRole("navigation", { name: "Main navigation" }).getByRole("link", { name: "Branches" }).click();
    const leaving = page.getByRole("dialog", { name: "Unsaved changes" });
    await expect(leaving).toBeVisible();
    await expect(leaving.getByRole("button")).toHaveText(["Save and close", "Discard changes", "Keep editing"]);
    await page.keyboard.press("Escape");
    await expect(leaving).toBeHidden();
    await expect(page).toHaveURL(/\/tenancy\/companies/);
    await expect(phone).toHaveValue(changed);
    expect(nativeDialogs).toEqual([]);

    await phone.press("Control+KeyS");
    await expect(page.locator(".record-form .notice")).toHaveText("Saved.");
    await expect(page.locator(".record-header")).not.toContainText("Unsaved changes");

    // Next record from the keyboard; the position follows.
    await page.keyboard.press("Alt+PageDown");
    await expect(title).not.toHaveText(first);
    await expect(page.locator(".record-position")).toContainText("2 of");
    await page.keyboard.press("Alt+PageUp");
    await expect(title).toHaveText(first);

    // Ctrl+Enter saves too; then the number goes back with Enter in the field (round 5: Enter in a
    // one-line field saves, as a person finishing an entry expects).
    await phone.fill("+971 4 555 0103");
    await phone.press("Control+Enter");
    await expect(page.locator(".record-form .notice")).toHaveText("Saved.");
    await phone.fill(original);
    await expect(page.locator(".record-header")).toContainText("Unsaved changes");
    await phone.press("Enter");
    await expect(page.locator(".record-form .notice")).toHaveText("Saved.");
    await expect(page.locator(".record-header")).not.toContainText("Unsaved changes");
    await expect(phone).toHaveValue(original);
  });

  test("a user who may only read a record sees it read-only, and no key of the form sends a change", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.viewer);
    // Every request other than a read that leaves the page (the viewer's own settings excepted).
    const writes: string[] = [];
    page.on("request", (request) => {
      const path = new URL(request.url()).pathname;
      if (request.method() !== "GET" && !(request.method() === "PUT" && path === "/api/identity/me/preferences")) writes.push(`${request.method()} ${path}`);
    });
    await openCompanies(page);
    await listRows(page).first().click();
    const phone = page.locator('[data-field="phone"] input');
    await expect(phone).toBeDisabled();
    await expect(page.getByTestId("record-read-only")).toBeVisible();
    await expect(page.locator(".record-form").getByRole("button", { name: "Save", exact: true })).toHaveCount(0);
    // The form's write keys (critic p06 round 2, plant W2), from the form's heading and from the page.
    for (const onHeading of [true, false]) {
      if (onHeading) await page.locator(".record-header h2").focus();
      else await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
      for (const key of ["Control+KeyS", "Control+Enter", "Enter", "Alt+KeyZ", "Alt+KeyN", "Delete"]) await page.keyboard.press(key);
    }
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await page.waitForLoadState("networkidle");
    expect(writes).toEqual([]);
    await expect(phone).toBeDisabled();
  });

  test("a record prints as an Arabic PDF from Alt+R, and its document reads right to left", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openCompanies(page);
    await listRows(page).first().click();
    await expect(page.locator('[data-field="phone"] input')).toBeVisible();

    await page.keyboard.press("Alt+KeyR");
    const english = page.getByRole("menuitem", { name: "PDF in English" });
    await expect(english).toBeFocused();
    await page.keyboard.press("ArrowDown");
    const arabic = page.getByRole("menuitem", { name: "PDF in Arabic" });
    await expect(arabic).toBeFocused();
    expect(await checkAccessibility(page, "company form with its print menu")).toBeGreaterThan(10);
    const href = (await arabic.getAttribute("href"))!;
    expect(href).toMatch(/^\/api\/reports\/run\/tenancy\.companyProfile\?company=[0-9a-f-]{36}&format=pdf&language=ar&/);
    const download = page.waitForEvent("download");
    await page.keyboard.press("Enter");
    expect((await download).suggestedFilename()).toMatch(/\.pdf$/);

    const pdf = await page.request.get(href);
    expect(pdf.status()).toBe(200);
    expect(pdf.headers()["content-type"]).toBe("application/pdf");
    expect((await pdf.body()).subarray(0, 5).toString()).toBe("%PDF-");
    const doc = await (await page.request.get(href.replace("format=pdf", "format=json"))).json();
    expect(doc.direction).toBe("rtl");
    expect(doc.title).toMatch(/[؀-ۿ]/);
  });

  test("the reports screen shows a grouped report as an Arabic document and exports it", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.getByRole("navigation", { name: "Main navigation" })).toBeVisible();
    await page.goto("/reports/catalog");
    await page.getByRole("button", { name: /Branch directory/ }).click();
    await page.locator('[data-field="language"] select').selectOption("ar");
    await page.keyboard.press("Control+Enter");
    const doc = page.getByTestId("report-document").locator("article");
    await expect(doc).toHaveAttribute("dir", "rtl");
    await expect(doc.locator(".report-group").first()).toBeVisible();
    await expect(doc.locator(".report-table thead")).toContainText(/[؀-ۿ]/);
    expect(await checkAccessibility(page, "reports screen with an Arabic document")).toBeGreaterThan(10);

    const excel = await page.getByRole("link", { name: "Excel", exact: true }).getAttribute("href");
    const workbook = await page.request.get(excel!);
    expect(workbook.status()).toBe(200);
    expect(workbook.headers()["content-type"]).toContain("spreadsheetml");
  });

  test("a totalled report: roles and access shows each kind's totals and the grand total, and exports it", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.getByRole("navigation", { name: "Main navigation" })).toBeVisible();
    await page.goto("/reports/catalog");
    await page.getByRole("button", { name: /Roles and access/ }).click();
    await page.keyboard.press("Control+Enter");
    const doc = page.getByTestId("report-document").locator("article");
    await expect(doc.locator(".report-group").first()).toBeVisible();

    // The figures the screen must add up: the same report as data.
    const data = (await (await page.request.get("/api/reports/run/identity.roleSummary?groupBy=kind&format=json&language=en&numerals=latn")).json()) as {
      columns: { key: string }[];
      groups: { rows: { cells: { value: unknown }[] }[] }[];
    };
    const at = (key: string) => data.columns.findIndex((c) => c.key === key);
    const sum = (key: string, rows: { cells: { value: unknown }[] }[]) => rows.reduce((total, row) => total + Number(row.cells[at(key)]!.value), 0);
    const allRows = data.groups.flatMap((g) => g.rows);
    expect(allRows.length).toBeGreaterThan(0);

    const subtotals = doc.locator("tr.report-subtotal");
    await expect(subtotals).toHaveCount(data.groups.length);
    for (const [g, group] of data.groups.entries()) {
      const cells = subtotals.nth(g).locator("td");
      await expect(cells.nth(0)).toContainText("Total");
      await expect(cells.nth(at("users"))).toHaveText(String(sum("users", group.rows)));
      await expect(cells.nth(at("permissions"))).toHaveText(String(sum("permissions", group.rows)));
    }
    const grand = doc.locator("tfoot tr.report-total td");
    await expect(grand.nth(0)).toHaveText("Total");
    await expect(grand.nth(at("users"))).toHaveText(String(sum("users", allRows)));
    await expect(grand.nth(at("permissions"))).toHaveText(String(sum("permissions", allRows)));

    // The export is the same report: the workbook comes from the address the screen offers.
    const excelHref = await page.getByRole("link", { name: "Excel", exact: true }).getAttribute("href");
    expect(excelHref).toMatch(/^\/api\/reports\/run\/identity\.roleSummary\?.*format=xlsx/);
    const workbook = await page.request.get(excelHref!);
    expect(workbook.status()).toBe(200);
    expect(workbook.headers()["content-type"]).toContain("spreadsheetml");
  });

  test("a list prints what it shows: the filtered users list as PDF and CSV", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.getByRole("navigation", { name: "Main navigation" })).toBeVisible();
    await page.goto("/identity/users");
    await expect(listRows(page).first()).toBeVisible();
    // Alt+Shift+R opens the menu from the keyboard (Alt+R prints an open record), and says so.
    const menuButton = page.getByRole("button", { name: "Print or export" });
    await expect(menuButton).toHaveAttribute("aria-keyshortcuts", "Alt+Shift+R");
    await page.keyboard.press("Alt+Shift+KeyR");
    await expect(page.getByRole("menuitem", { name: "PDF in English" })).toBeVisible();
    const csvHref = await page.getByRole("menuitem", { name: "CSV in English" }).getAttribute("href");
    expect(csvHref).toMatch(/^\/api\/reports\/lists\/identity\.users\?/);
    const csv = await page.request.get(csvHref!);
    expect(csv.status()).toBe(200);
    const csvText = await csv.text();
    expect(csvText).toContain("admin@alnoor.example");
    // Roles by name (the administrator may read roles), sign-ins as the wall clock to the second.
    expect(csvText).toMatch(/Administrator/);
    expect(csvText).not.toMatch(/\d{2}:\d{2}:\d{2}\.\d+/);
    // CSV and Excel come in Arabic too: Arabic column titles.
    const arabicCsvHref = await page.getByRole("menuitem", { name: "CSV in Arabic" }).getAttribute("href");
    expect(arabicCsvHref).toContain("language=ar");
    expect(await (await page.request.get(arabicCsvHref!)).text()).toContain("البريد الإلكتروني");
    expect(await page.getByRole("menuitem", { name: "Excel in Arabic" }).getAttribute("href")).toContain("format=xlsx&language=ar");
    const pdf = await page.request.get((await page.getByRole("menuitem", { name: "PDF in Arabic" }).getAttribute("href"))!);
    expect(pdf.headers()["content-type"]).toBe("application/pdf");
  });
});

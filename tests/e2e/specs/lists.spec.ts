import { expect, test, type Page } from "@playwright/test";
import { freshStart, signIn, users } from "./demo";

/** The shared list framework on the users list: everything by keyboard where a user would. */

const grid = (page: Page) => page.getByRole("grid");
const dataRows = (page: Page) => page.locator("table[role=grid] tbody tr");
const search = (page: Page) => page.getByRole("searchbox", { name: "Search by name or e-mail" });

async function openUsers(page: Page) {
  await page.locator('nav a[href="/identity/users"]').first().click();
  await expect(page).toHaveURL(/\/identity\/users/);
  await expect(dataRows(page).first()).toBeVisible();
}

test.describe("list framework", () => {
  test("finds one user by keyboard: the search box has focus, words in any order, Enter opens the only match", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openUsers(page);
    await expect(search(page)).toBeFocused();
    await page.keyboard.type("haddad omar viewer");
    await expect(page.getByText("1 user", { exact: true })).toBeVisible();
    await expect(dataRows(page)).toHaveCount(1);
    await page.keyboard.press("Enter");
    const record = page.getByRole("region", { name: "Details" });
    await expect(record).toContainText(users.viewer);
    await expect(page).toHaveURL(/\/identity\/users\/[0-9a-f-]{36}(\?|$)/);
    // The address reopens the same list and record.
    await page.reload();
    await expect(page.getByRole("region", { name: "Details" })).toContainText(users.viewer);
    await expect(search(page)).toHaveValue("haddad omar viewer");
    await page.keyboard.press("Escape");
    await expect(page.getByRole("region", { name: "Details" })).toHaveCount(0);
  });

  test("lists a search best match first, opens it with Enter, and reads Arabic spelling variants", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openUsers(page);
    // Prefixes of the name's parts, in any order of typing: the best match is marked first.
    await page.keyboard.type("oma had");
    await expect(page.getByText("best match first")).toBeVisible();
    const top = page.locator("tr.list-row.is-tophit");
    await expect(top).toHaveCount(1);
    await expect(top).toHaveAttribute("aria-rowindex", "2");
    await expect(top.locator("td").nth(1)).toHaveText(/^Omar.* Haddad/);
    const email = await top.locator("td").nth(2).innerText();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("region", { name: "Details" })).toContainText(email);
    await page.keyboard.press("Escape");
    // "فاطمه" and "الزعابى" find "فاطمة الزعابي" (teh marbuta / heh, yeh / alef maqsura).
    await search(page).fill("فاطمه الزعابى");
    await expect(dataRows(page).first()).toContainText("admin.ar@alnoor.example");
  });

  test("column header buttons keep Enter and Space; rows keep one column layout", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openUsers(page);
    const sortByEmail = page.getByRole("button", { name: "E-mail", exact: true });
    await sortByEmail.focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("columnheader", { name: /E-mail/ })).toHaveAttribute("aria-sort", "ascending");
    await page.keyboard.press("Space");
    await expect(page.getByRole("columnheader", { name: /E-mail/ })).toHaveAttribute("aria-sort", "descending");
    await expect(page).not.toHaveURL(/open=/);
    await page.getByRole("button", { name: "Options for the column E-mail" }).focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("menu", { name: "Options for the column E-mail" })).toBeVisible();
    await expect(page).not.toHaveURL(/open=/);
    await page.keyboard.press("Escape");
    // Every row's cells start where the header's do, whatever the length of the e-mail.
    const columnStarts = await page.locator("table[role=grid] tr.list-header, table[role=grid] tbody tr").evaluateAll((rows) =>
      rows.slice(0, 15).map((row) => [...row.children].map((cell) => Math.round((cell as HTMLElement).getBoundingClientRect().left)).join(",")),
    );
    expect(new Set(columnStarts).size).toBe(1);
    // The row draws the separator, never a cell: an empty cell (a user with no roles) once drew its
    // own border across the middle of its row (critic p05 round 4).
    const cellBorders = await page.locator("table[role=grid] tbody tr.list-row").evaluateAll((rows) =>
      rows.slice(0, 15).flatMap((row) => [...row.children].map((cell) => getComputedStyle(cell).borderBottomWidth)).filter((w) => w !== "0px"),
    );
    expect(cellBorders).toEqual([]);
  });

  test("moves through rows with the arrow keys, selects with Space and opens with Enter", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openUsers(page);
    await page.keyboard.press("ArrowDown");
    await expect(grid(page)).toBeFocused();
    const active = async () => page.locator(`#${await grid(page).getAttribute("aria-activedescendant")}`);
    await expect(await active()).toHaveAttribute("aria-rowindex", "2");
    await page.keyboard.press("ArrowDown");
    await page.keyboard.press("ArrowDown");
    await expect(await active()).toHaveAttribute("aria-rowindex", "4");
    await page.keyboard.press("Space");
    await page.keyboard.press("Shift+ArrowDown");
    await expect(page.getByText("2 selected")).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(page.getByText("2 selected")).toHaveCount(0);
    await page.keyboard.press("PageDown");
    await page.keyboard.press("End");
    const last = await active();
    await expect(last).toBeVisible();
    await expect(last.locator("td").nth(2)).toContainText("@");
    const email = await last.locator("td").nth(2).innerText();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("region", { name: "Details" })).toContainText(email);
  });

  test("sorts from a header and filters from the column menu; the address keeps the state", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openUsers(page);
    await page.getByRole("button", { name: "E-mail", exact: true }).click();
    await expect(page.getByRole("columnheader", { name: /E-mail/ })).toHaveAttribute("aria-sort", "ascending");
    await expect
      .poll(async () => {
        const emails = (await dataRows(page).locator("td:nth-child(3)").allInnerTexts()).slice(0, 10);
        return emails.length > 1 && emails.join("|") === [...emails].sort().join("|");
      })
      .toBe(true);
    await page.getByRole("button", { name: "Options for the column Language" }).click();
    await page.getByRole("menuitem", { name: "Filter…" }).click();
    await page.getByRole("dialog", { name: "Filter: Language" }).getByLabel("Arabic").check();
    await page.getByRole("button", { name: "Apply" }).click();
    await expect(page.getByRole("list", { name: "Filters" })).toContainText("Language is Arabic");
    // The previous rows stay until the filtered page arrives; then every row is Arabic.
    await expect(dataRows(page).locator("td:nth-child(4)", { hasText: "English" })).toHaveCount(0);
    await expect(dataRows(page).first()).toContainText("Arabic");
    for (const language of await dataRows(page).locator("td:nth-child(4)").allInnerTexts()) {
      expect(language).toBe("Arabic");
    }
    await expect(page).toHaveURL(/filter=language/);
    await page.reload();
    await expect(page.getByRole("list", { name: "Filters" })).toContainText("Language is Arabic");
    await page.getByRole("button", { name: "Remove: Language is Arabic" }).click();
    await expect(page.getByRole("list", { name: "Filters" })).toHaveCount(0);
  });

  test("groups by a column with counts and opens a group's rows", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openUsers(page);
    await page.getByRole("button", { name: "Options for the column Language" }).click();
    await page.getByRole("menuitem", { name: "Group by this column" }).click();
    const groups = page.locator("tbody.list-groups tr");
    await expect(groups).toHaveCount(2);
    await expect(groups.first()).toContainText(/rows?/);
    await grid(page).focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("list", { name: "Filters" })).toContainText("Language is");
    await expect(dataRows(page).first()).toBeVisible();
  });

  test("chooses columns and saves a personal default view that opens next time", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openUsers(page);
    await page.getByRole("button", { name: "Columns" }).click();
    await page.getByLabel("Show Status").uncheck();
    await page.getByLabel("Show Created").check();
    await page.getByRole("button", { name: "Done" }).click();
    await expect(page.getByRole("columnheader", { name: /Created/ })).toBeVisible();
    await expect(page.getByRole("columnheader", { name: /Status/ })).toHaveCount(0);

    await page.getByRole("button", { name: /^View:/ }).click();
    await page.getByRole("menuitem", { name: "Save as a new view…" }).click();
    const dialog = page.getByRole("dialog", { name: "Save view" });
    await dialog.getByLabel("Name").fill("With creation date");
    await dialog.getByLabel("Open this view when I open the list").check();
    await dialog.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("button", { name: "View: With creation date" })).toBeVisible();

    await page.goto("/identity/users");
    await expect(page.getByRole("button", { name: "View: With creation date" })).toBeVisible();
    await expect(page.getByRole("columnheader", { name: /Created/ })).toBeVisible();

    page.once("dialog", (d) => void d.accept());
    await page.getByRole("button", { name: /^View:/ }).click();
    await page.getByRole("menuitem", { name: "Delete this view" }).click();
    await expect(page.getByRole("button", { name: "View: Standard" })).toBeVisible();
    await expect(page.getByRole("columnheader", { name: /Status/ })).toBeVisible();
  });

  test("a read-only user keeps personal views but is not offered sharing", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.viewer);
    await openUsers(page);
    await page.getByRole("button", { name: /^View:/ }).click();
    await page.getByRole("menuitemradio", { name: "Inactive users" }).click();
    await expect(page.getByRole("list", { name: "Filters" })).toContainText("Status is No");
    await page.getByRole("button", { name: /^View:/ }).click();
    await page.getByRole("menuitem", { name: "Save as a new view…" }).click();
    const dialog = page.getByRole("dialog", { name: "Save view" });
    await expect(dialog.getByLabel("Share with everyone who can see this list")).toHaveCount(0);
    await dialog.getByRole("button", { name: "Cancel" }).click();
  });

  test("the list works right to left in Arabic with Arabic counts and headers", async ({ page }) => {
    await freshStart(page, "ar");
    await signIn(page, users.adminArabic);
    await openUsers(page);
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await expect(page.getByRole("columnheader", { name: /البريد الإلكتروني/ })).toBeVisible();
    await page.keyboard.type("viewer@alnoor");
    await expect(page.getByText("مستخدم واحد", { exact: true })).toBeVisible();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("region", { name: "التفاصيل" })).toContainText(users.viewer);
  });
});

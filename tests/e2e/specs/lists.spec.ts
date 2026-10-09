import { expect, test, type Page } from "@playwright/test";
import { freshStart, signIn, users } from "./demo";

/** The shared list framework on the users list: everything by keyboard where a user would. */

const grid = (page: Page) => page.getByRole("grid");
const dataRows = (page: Page) => page.locator("table[role=grid] tbody tr");
const search = (page: Page) => page.getByRole("searchbox", { name: "Search by name, initials or e-mail" });

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
    // No cell is taller than its row (the selection box once was, and crossed the separator).
    const tallCells = await page.locator("table[role=grid] tbody tr.list-row").evaluateAll((rows) =>
      rows.slice(0, 15).flatMap((row) => {
        const box = row.getBoundingClientRect();
        return [...row.querySelectorAll("td, td *")]
          .map((cell) => cell.getBoundingClientRect())
          .filter((r) => r.height > 0 && (r.top < box.top - 0.5 || r.bottom > box.bottom + 0.5))
          .map((r) => `${Math.round(r.top)}-${Math.round(r.bottom)} outside ${Math.round(box.top)}-${Math.round(box.bottom)}`);
      }),
    );
    expect(tallCells).toEqual([]);
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

  test("groups by status under the status's own words, as the rows say them", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openUsers(page);
    await page.getByRole("button", { name: "Options for the column Status" }).click();
    await page.getByRole("menuitem", { name: "Group by this column" }).click();
    const groups = page.locator("tbody.list-groups tr");
    await expect(groups).toHaveCount(2);
    const labels = await groups.allTextContents();
    expect(labels.filter((l) => /^Active(?![A-Za-z])/.test(l)), JSON.stringify(labels)).toHaveLength(1);
    expect(labels.filter((l) => /^Inactive(?![A-Za-z])/.test(l)), JSON.stringify(labels)).toHaveLength(1);
    expect(labels.filter((l) => /(?<![A-Za-z])(Yes|No)(?![A-Za-z])/.test(l)), JSON.stringify(labels)).toEqual([]);
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

    // Asked in the app's own dialog (never the browser's): Enter on the focused Delete confirms.
    page.once("dialog", (d) => {
      throw new Error(`the browser's ${d.type()} dialog was shown: ${d.message()}`);
    });
    await page.getByRole("button", { name: /^View:/ }).click();
    await page.getByRole("menuitem", { name: "Delete this view" }).click();
    const confirm = page.getByRole("alertdialog", { name: "Delete this view" });
    await expect(confirm).toContainText("Delete the view With creation date?");
    await expect(confirm.getByRole("button", { name: "Delete this view" })).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(confirm).toHaveCount(0);
    await expect(page.getByRole("button", { name: "View: Standard" })).toBeVisible();
    await expect(page.getByRole("columnheader", { name: /Status/ })).toBeVisible();
  });

  test("a read-only user keeps personal views but is not offered sharing", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.viewer);
    await openUsers(page);
    await page.getByRole("button", { name: /^View:/ }).click();
    await page.getByRole("menuitemradio", { name: "Inactive users" }).click();
    // A status is called what the column calls it (critic p05 round 5: the group said "Yes").
    await expect(page.getByRole("list", { name: "Filters" })).toContainText("Status is Inactive");
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
    // A Latin e-mail that does not fit is cut at its end (its own direction), so the start that
    // names the person stays visible, and it keeps to the right like the column's other values.
    const placement = await dataRows(page).evaluateAll((rows) =>
      rows.slice(0, 15).flatMap((row) => {
        const cell = row.children[2] as HTMLElement;
        const value = cell.querySelector(".list-text") as HTMLElement | null;
        if (!value) return [];
        const c = cell.getBoundingClientRect();
        const v = value.getBoundingClientRect();
        const padding = parseFloat(getComputedStyle(cell).paddingInlineStart);
        // Right edge of the value at the cell's start (its right, less padding); cut at its own end.
        return [{ atStart: Math.abs(c.right - padding - v.right) <= 1, unicodeBidi: getComputedStyle(value).unicodeBidi, direction: getComputedStyle(value).direction, cut: value.scrollWidth > value.clientWidth }];
      }),
    );
    expect(placement.length).toBeGreaterThan(3);
    // The value box takes the value's own direction (dir="auto"), so the ellipsis of a Latin
    // address or name that does not fit is at its end (round 6: an English company name in the
    // Arabic companies list lost its beginning, "…oor Technical Services LLC").
    expect(placement.every((p) => p.atStart && p.unicodeBidi === "plaintext" && p.direction === "ltr"), JSON.stringify(placement)).toBe(true);
    await page.keyboard.type("viewer@alnoor");
    await expect(page.getByText("مستخدم واحد", { exact: true })).toBeVisible();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("region", { name: "التفاصيل" })).toContainText(users.viewer);
  });

  test("a value that does not fit is cut at its own end in an Arabic list, the screen's own boxes included", async ({ page }) => {
    // Round 6: an English legal name in the Arabic companies list was cut at its beginning
    // ("…oor Technical Services LLC"), because the screen's own <span dir="ltr"> was not a box.
    await freshStart(page, "ar");
    await signIn(page, users.adminArabic);
    await page.setViewportSize({ width: 1024, height: 800 });
    await page.locator('nav a[href="/tenancy/companies"]').first().click();
    await expect(dataRows(page).first()).toBeVisible();
    const boxes = await dataRows(page).evaluateAll((rows) =>
      rows.flatMap((row) =>
        [...row.querySelectorAll<HTMLElement>("[role=gridcell] > span[dir], [role=gridcell] .list-text")].map((value) => {
          const style = getComputedStyle(value);
          const latin = /^[\x20-\x7E]+$/.test(value.textContent ?? "");
          return { text: value.textContent, latin, direction: style.direction, overflow: style.overflow, ellipsis: style.textOverflow, display: style.display };
        }),
      ),
    );
    expect(boxes.filter((b) => b.latin).length).toBeGreaterThan(3);
    const wrong = boxes.filter((b) => (b.latin && b.direction !== "ltr") || b.overflow !== "hidden" || b.ellipsis !== "ellipsis" || b.display !== "inline-block");
    expect(wrong, JSON.stringify(wrong)).toEqual([]);
  });

  for (const [language, user] of [["ar", users.adminArabic], ["en", users.admin]] as const) {
    test(`every header stays inside its own column, wide and narrow (${language})`, async ({ page }) => {
      // Critic p06 round 2: on the Arabic companies list the two legal-name headers overlapped
      // (a long label pushed its column menu into the next header).
      const spills = async () =>
        page.locator("table[role=grid] tr.list-header > th").evaluateAll((cells) =>
          cells.flatMap((cell) => {
            const box = cell.getBoundingClientRect();
            return [...cell.querySelectorAll("button, span")]
              .filter((e) => !e.closest(".list-popover"))
              .map((e) => e.getBoundingClientRect())
              .filter((r) => r.width > 0 && (r.left < box.left - 0.5 || r.right > box.right + 0.5))
              .map((r) => `${cell.textContent?.trim()}: ${Math.round(r.left)}-${Math.round(r.right)} outside ${Math.round(box.left)}-${Math.round(box.right)}`);
          }),
        );
      await freshStart(page, language);
      await signIn(page, user);
      for (const width of [1440, 1024]) {
        await page.setViewportSize({ width, height: 800 });
        for (const href of ["/tenancy/companies", "/identity/users"]) {
          await page.locator(`nav a[href="${href}"]`).first().click();
          await expect(page).toHaveURL(new RegExp(href));
          await expect(dataRows(page).first()).toBeVisible();
          expect(await spills(), `${language} ${width}px ${href}`).toEqual([]);
          // And with a record's form open beside the list, which leaves the list less room
          // (p02 round 6: on the English companies list the two legal-name headers overlapped).
          await dataRows(page).first().click();
          await expect(page.locator("aside.list-record")).toBeVisible();
          expect(await spills(), `${language} ${width}px ${href} with the record open`).toEqual([]);
        }
      }
    });
  }

  for (const [language, user, administrator] of [["en", users.admin, "Administrator"], ["ar", users.adminArabic, "مدير النظام"]] as const) {
    test(`a screen's record form grows with the window and its tables keep whole headers (${language})`, async ({ page }) => {
      // Critic p03 round 5: the record panel was 338 px wide at 1366, 1440 and 1920 px, so the
      // role matrix's headers were cut ("Creat", "Delet") and a user's "Granted by" column ran
      // past the panel's edge.
      const clipped = () =>
        page.evaluate(() => {
          const panel = document.querySelector<HTMLElement>("aside.list-record")!;
          const box = panel.getBoundingClientRect();
          const found: string[] = [];
          for (const th of panel.querySelectorAll<HTMLElement>("table th")) {
            const r = th.getBoundingClientRect();
            if (r.width === 0) continue;
            if (th.scrollWidth > th.clientWidth + 1) found.push(`${th.textContent?.trim()}: cut (${th.scrollWidth} > ${th.clientWidth})`);
            if (r.left < box.left - 1 || r.right > box.right + 1) found.push(`${th.textContent?.trim()}: outside the panel`);
          }
          return { width: Math.round(box.width), sideways: panel.scrollWidth - panel.clientWidth, found };
        });
      await freshStart(page, language);
      await signIn(page, user);
      await expect(page.locator("nav").first()).toBeVisible();
      const session = (await (await page.request.get("/api/auth/session")).json()) as { user: { id: string } };
      const widths: number[] = [];
      for (const width of [1366, 1440, 1920]) {
        await page.setViewportSize({ width, height: 900 });
        await page.locator('nav a[href="/identity/roles"]').first().click();
        await page.locator("table[role=grid] tbody tr", { hasText: administrator }).click();
        await expect(page.locator(".id-matrix-table thead th").first()).toBeVisible();
        const roles = await clipped();
        expect(roles.found, `${language} ${width}px role matrix`).toEqual([]);
        expect(roles.sideways, `${language} ${width}px role panel scrolls sideways`).toBeLessThanOrEqual(0);
        expect(roles.width).toBeGreaterThanOrEqual(380);
        widths.push(roles.width);

        await page.goto(`/identity/users/${session.user.id}`);
        await page.getByRole("tab").nth(1).click();
        await expect(page.locator("aside.list-record table th").first()).toBeVisible();
        const granted = await clipped();
        expect(granted.found, `${language} ${width}px what the user can do`).toEqual([]);
        expect(granted.sideways, `${language} ${width}px user panel scrolls sideways`).toBeLessThanOrEqual(0);
      }
      // Wider windows give the form more room, not the same fixed width.
      expect(widths[2]).toBeGreaterThan(widths[0] + 200);
    });
  }

  for (const [language, user] of [["ar", users.adminArabic], ["en", users.admin]] as const) {
    test(`a list screen is never wider than a desktop window, whatever the top bar holds (${language})`, async ({ page }) => {
      // Round 5: the shell's grid column was as wide as the top bar's contents, so a workspace
      // with several companies (one chip each) pushed every screen past the window's edge: the
      // list's last column, its New button and the sign-out button were cut off.
      const overflow = () => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      const outside = () =>
        page.evaluate(() =>
          // A wide list scrolls sideways inside its own frame, so its cells are not judged here.
          [...document.querySelectorAll(".topbar button, main button")]
            .filter((e) => !e.closest("table"))
            .map((e) => ({ e, r: e.getBoundingClientRect() }))
            .filter(({ r }) => r.width > 0 && (r.left < -0.5 || r.right > document.documentElement.clientWidth + 0.5))
            .map(({ e, r }) => `${e.textContent?.trim() || e.getAttribute("aria-label")}: ${Math.round(r.left)}-${Math.round(r.right)}`),
        );
      await freshStart(page, language);
      await signIn(page, user);
      for (const width of [1440, 1280, 1024]) {
        await page.setViewportSize({ width, height: 800 });
        for (const href of ["/identity/users", "/tenancy/companies"]) {
          await page.locator(`nav a[href="${href}"]`).first().click();
          await expect(page).toHaveURL(new RegExp(href));
          await expect(dataRows(page).first()).toBeVisible();
          expect(await overflow(), `${language} ${width}px ${href} scrolls sideways`).toBeLessThanOrEqual(0);
          expect(await outside(), `${language} ${width}px ${href}`).toEqual([]);
        }
      }
      // Every company stays one keystroke away: the switcher's list opens inside the window.
      await page.keyboard.press("Alt+KeyC");
      const popover = page.locator(".workplace-popover");
      await expect(popover.locator("input")).toBeFocused();
      const box = (await popover.boundingBox())!;
      expect(box.x).toBeGreaterThanOrEqual(0);
      expect(box.x + box.width).toBeLessThanOrEqual(page.viewportSize()!.width);
      await page.keyboard.press("Escape");
    });
  }
});

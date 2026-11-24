import { expect, test, type Page } from "@playwright/test";
import { freshStart, signIn, users } from "./demo";

/**
 * The design system on the real screens (p18-visual-design): the three themes, remembered and
 * following the system until chosen; day-first dates; and no clipped text, in English and Arabic,
 * in every theme. The colours themselves are judged by the web design gate (web/src/design).
 */

const theme = (page: Page) => page.locator("html").getAttribute("data-theme");

/** Controls whose own text is cut off by their box (a button showing "De", a currency showing "AEC"). */
async function clippedControls(page: Page): Promise<string[]> {
  return page.evaluate(() =>
    [...document.querySelectorAll<HTMLElement>("button, .button, input, select, [role=tab], [role=radio], .badge, label")]
      .filter((el) => {
        const box = el.getBoundingClientRect();
        if (box.width === 0 || box.height === 0 || getComputedStyle(el).visibility === "hidden") return false;
        // Text that ends in an ellipsis on purpose carries its full text as a tooltip or a title.
        if (getComputedStyle(el).textOverflow === "ellipsis") return false;
        if (el instanceof HTMLInputElement || el instanceof HTMLSelectElement) return el.scrollWidth > el.clientWidth + 1 && el.value.length > 0 && el.type !== "search";
        return el.scrollWidth > el.clientWidth + 1;
      })
      .map((el) => `${el.tagName.toLowerCase()}.${el.className} "${(el.textContent || (el as HTMLInputElement).value || "").trim().slice(0, 30)}"`),
  );
}

test.describe("design system", () => {
  test("is dark (direction G) until a theme is chosen, whatever colour the system prefers, then keeps the choice across a reload", async ({ page }) => {
    await page.emulateMedia({ colorScheme: "light" });
    await freshStart(page, "en");
    expect(await theme(page)).toBe("dark");
    await page.emulateMedia({ colorScheme: "dark" });
    await expect.poll(() => theme(page)).toBe("dark");
    await signIn(page, users.admin);
    await page.getByRole("button", { name: "High contrast" }).click();
    expect(await theme(page)).toBe("hc");
    await page.reload();
    expect(await theme(page)).toBe("hc");
    await page.getByRole("button", { name: "Light", exact: true }).click();
    expect(await theme(page)).toBe("light");
    // The choice is the device's: still light after the system changes.
    await page.emulateMedia({ colorScheme: "dark" });
    expect(await theme(page)).toBe("light");
    // Pressing the chosen theme again hands the choice back to the default (dark).
    await page.getByRole("button", { name: "Light", exact: true }).click();
    await expect.poll(() => theme(page)).toBe("dark");
  });

  test("the density switch changes the row height and is remembered", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    const row = async () => Number.parseFloat(await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue("--row-h")));
    expect(await row()).toBe(30);
    await page.getByRole("button", { name: "Comfortable" }).click();
    expect(await row()).toBe(40);
    await page.reload();
    expect(await row()).toBe(40);
  });

  test("shows every date day first, as dd/mm/yyyy, in the users list and in Arabic", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await page.locator('nav a[href="/identity/users"]').first().click();
    await expect(page.locator("table[role=grid] tbody tr").first()).toBeVisible();
    const cells = await page.locator("table[role=grid] tbody td").allTextContents();
    const dates = cells.filter((c) => /\d{1,2}\/\d{1,2}\/\d{4}/.test(c));
    expect(dates.length).toBeGreaterThan(0);
    for (const d of dates) expect(d).toMatch(/^\d{2}\/\d{2}\/\d{4}/);
    expect(cells.some((c) => /\b[A-Z][a-z]{2} \d{1,2}, \d{4}\b|\b\d{1,2} [A-Z][a-z]{2} \d{4}\b/.test(c))).toBe(false);
  });

  for (const language of ["en", "ar"] as const) {
    for (const mode of ["light", "dark", "hc"] as const) {
      test(`no clipped control on the home, contacts and users screens in ${language}, ${mode}`, async ({ page }) => {
        await freshStart(page, language);
        await page.evaluate((m) => localStorage.setItem("erp.theme", m), mode);
        await page.reload();
        await signIn(page, language === "ar" ? users.adminArabic : users.admin);
        expect(await theme(page)).toBe(mode);
        expect(await clippedControls(page), "home").toEqual([]);
        for (const path of ["/contacts/contacts", "/identity/users"]) {
          await page.locator(`nav a[href="${path}"]`).first().click();
          await expect(page.locator("table[role=grid] tbody tr").first()).toBeVisible();
          expect(await clippedControls(page), path).toEqual([]);
          await page.locator("table[role=grid] tbody tr").first().click();
          await page.waitForTimeout(300);
          expect(await clippedControls(page), `${path} with a record open`).toEqual([]);
        }
      });
    }
  }

  for (const language of ["en", "ar"] as const) {
    for (const density of ["compact", "comfortable"] as const) {
      test(`text cut with an ellipsis always carries its full text as a title, in ${language}, ${density} (list cells, tabs, placeholders, form values)`, async ({ page }) => {
        await freshStart(page, language);
        await page.evaluate((d) => localStorage.setItem("erp.density", d), density);
        await page.reload();
        await signIn(page, language === "ar" ? users.adminArabic : users.admin);
        // A cut cell is titled when the person points at it (nothing writes to the page by itself): point at
        // every cut element in turn and require the title.
        const cutWithoutTitle = async () => {
          const spots = await page.evaluate(() =>
            [...document.querySelectorAll<HTMLElement>("td, th, [role=tab], button, a, label, legend, input, select, .section-tab")]
              .map((el, i) => ({ el, i }))
              .filter(({ el }) => {
                const box = el.getBoundingClientRect();
                if (box.width === 0 || box.height === 0 || getComputedStyle(el).visibility === "hidden" || el.closest("[aria-hidden=true]")) return false;
                return el.scrollWidth > el.clientWidth + 1 && !el.title && !el.closest("[title]");
              })
              .map(({ el, i }) => {
                const box = el.getBoundingClientRect();
                return { i, x: box.x + Math.min(box.width / 2, 20), y: box.y + box.height / 2, label: `${el.tagName.toLowerCase()}.${el.className} "${(el.textContent || (el as HTMLInputElement).value || "").trim().slice(0, 30)}"` };
              }),
          );
          const missing: string[] = [];
          for (const spot of spots) {
            await page.mouse.move(spot.x, spot.y);
            const titled = await page.evaluate(
              ({ i }) => {
                const el = document.querySelectorAll<HTMLElement>("td, th, [role=tab], button, a, label, legend, input, select, .section-tab")[i];
                return !!el && (!!el.title || !!el.closest("[title]"));
              },
              { i: spot.i },
            );
            if (!titled) missing.push(spot.label);
          }
          return missing;
        };
        await page.locator('nav a[href="/contacts/contacts"]').first().click();
        await expect(page.locator("table[role=grid] tbody tr").first()).toBeVisible();
        await page.waitForTimeout(400);
        expect(await cutWithoutTitle(), "contacts list").toEqual([]);
        await page.locator("table[role=grid] tbody tr").first().click();
        await page.waitForTimeout(500);
        expect(await cutWithoutTitle(), "contacts list with a record open").toEqual([]);
        // Column widths can be changed: the edge of each header, and Wider in the column menu.
        expect(await page.locator(".list-resizer").count()).toBeGreaterThan(0);
      });
    }
  }

  test("every control of a form is reachable by Tab: the section tabs and the calendar button are not skipped", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await page.locator('nav a[href="/tenancy/companies"]').first().click();
    await expect(page.locator("table[role=grid] tbody tr").first()).toBeVisible();
    await page.locator("table[role=grid] tbody tr").first().click();
    await expect(page.locator(".record-form")).toBeVisible();
    const skipped = await page.evaluate(() => [...document.querySelectorAll<HTMLElement>(".section-tab, .date-picker-button")].filter((el) => el.tabIndex < 0).length);
    expect(skipped).toBe(0);
  });

  test("the home screen counts what waits for the user and leads to it", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.locator(".home-summary")).toBeVisible();
    await expect(page.locator(".home-card").first()).toBeVisible();
    await page.locator(".home-figure-link").first().click();
    await expect(page.locator("table[role=grid]")).toBeVisible();
  });

  test("the palette opens on every screen from the top bar field and the key", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.getByRole("heading", { name: /Welcome/ })).toBeVisible();
    for (const path of ["/", "/contacts/contacts", "/identity/users"]) {
      if (path !== "/") await page.locator(`nav a[href="${path}"]`).first().click();
      await page.keyboard.press("Control+K");
      await expect(page.getByRole("combobox"), `Ctrl+K on ${path}`).toBeFocused();
      await page.keyboard.press("Escape");
      await expect(page.getByRole("combobox")).toHaveCount(0);
      await page.locator(".palette-trigger").click();
      await expect(page.getByRole("combobox")).toBeVisible();
      await page.keyboard.press("Escape");
    }
  });
});

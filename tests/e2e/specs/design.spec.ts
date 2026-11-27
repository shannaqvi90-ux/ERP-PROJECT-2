import { expect, test, type Page } from "@playwright/test";
import { freshStart, signIn, users } from "./demo";

/**
 * The design system on the real screens (p18-visual-design): the three themes, remembered and
 * following the system until chosen; day-first dates; and no clipped text, in English and Arabic,
 * in every theme. The colours themselves are judged by the web design gate (web/src/design).
 */

const theme = (page: Page) => page.locator("html").getAttribute("data-theme");

/**
 * Shows a look on this visit without saving it to the user (the choice is the user's own and the demo's
 * users are shared by every spec): the switches are used, and the save they send is answered here.
 */
async function showLook(page: Page, look: { theme?: "light" | "dark" | "hc"; density?: "compact" | "comfortable" }) {
  await page.route("**/api/identity/me/look", (route) => route.fulfill({ status: 200, contentType: "application/json", body: "{}" }));
  if (look.theme && look.theme !== "dark") await page.getByRole("button", { name: look.theme === "hc" ? "High contrast" : "Light", exact: true }).click();
  if (look.density === "comfortable") await page.getByRole("button", { name: "Comfortable" }).click();
}

/** What may be cut: the cell or control itself, and an element inside it (a cell's text span carries the ellipsis, not the cell). */
const cutSelector = "td, th, td span, th span, [role=gridcell] span, [role=tab], button, a, label, legend, input, select, .section-tab";

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
    await expect.poll(row).toBe(40);
    // The demo's users are shared by every spec: hand the choice back.
    await page.getByRole("button", { name: "Compact" }).click();
    await expect.poll(row).toBe(30);
  });

  test("the look belongs to the user: the next person on the same browser gets their own, or the default", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.adminArabic);
    const row = async () => Number.parseFloat(await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue("--row-h")));
    try {
      await page.getByRole("button", { name: "Light", exact: true }).click();
      await page.getByRole("button", { name: "Comfortable" }).click();
      expect(await theme(page)).toBe("light");
      await page.waitForResponse((r) => r.url().includes("/api/identity/me/look") && r.request().method() === "PUT");
      await page.getByRole("button", { name: "Sign out" }).click();
      await expect(page.locator('input[name="email"]')).toBeFocused();
      // Somebody else signs in on this browser: the default, not the previous person's choice.
      await signIn(page, users.admin);
      await expect(page.locator("nav").first()).toBeVisible();
      expect(await theme(page)).toBe("dark");
      expect(await row()).toBe(30);
      await page.getByRole("button", { name: "Sign out" }).click();
      await expect(page.locator('input[name="email"]')).toBeFocused();
      // The first person comes back, on a browser with nothing kept: their choice is on the server.
      await page.evaluate(() => localStorage.clear());
      await page.reload();
      await signIn(page, users.adminArabic);
      await expect(page.locator("nav").first()).toBeVisible();
      expect(await theme(page)).toBe("light");
      expect(await row()).toBe(40);
    } finally {
      // The demo's users are shared by every spec: hand the choices back.
      await page.getByRole("button", { name: "Light", exact: true }).click().catch(() => undefined);
      await page.getByRole("button", { name: "Compact" }).click().catch(() => undefined);
    }
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
        await signIn(page, language === "ar" ? users.adminArabic : users.admin);
        await expect(page.locator("nav").first()).toBeVisible();
        await showLook(page, { theme: mode });
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
        await signIn(page, language === "ar" ? users.adminArabic : users.admin);
        await expect(page.locator("nav").first()).toBeVisible();
        await showLook(page, { density });
        // A cut cell is titled when the person points at it (nothing writes to the page by itself): point at
        // every cut element in turn and require the title.
        const cutWithoutTitle = async () => {
          const spots = await page.evaluate((selector) =>
            [...document.querySelectorAll<HTMLElement>(selector)]
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
            cutSelector,
          );
          const missing: string[] = [];
          for (const spot of spots) {
            await page.mouse.move(spot.x, spot.y);
            const titled = await page.evaluate(
              ({ i, selector }) => {
                const el = document.querySelectorAll<HTMLElement>(selector)[i];
                return !!el && (!!el.title || !!el.closest("[title]"));
              },
              { i: spot.i, selector: cutSelector },
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
        // The settings lists show codes in their own cut spans, the Arabic ones most (the first screenful of each).
        for (const path of ["/tenancy/companies", "/tenancy/branches", "/numbering/sequences"]) {
          await page.goto(path);
          await expect(page.locator("table[role=grid] tbody tr").first()).toBeVisible();
          await page.waitForTimeout(400);
          expect(await cutWithoutTitle(), path).toEqual([]);
        }
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

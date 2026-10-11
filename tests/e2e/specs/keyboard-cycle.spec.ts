import { expect, test, type Page } from "@playwright/test";
import { freshStart, signIn, users } from "./demo";

/** What has the focus: tag and accessible text, BODY when nothing does. */
async function focused(page: Page): Promise<string> {
  return page.evaluate(() => {
    const active = document.activeElement as HTMLElement | null;
    if (!active || active === document.body || active === document.documentElement) return "BODY";
    const label = active.getAttribute("aria-label") ?? active.textContent ?? "";
    const index = Array.prototype.indexOf.call(document.querySelectorAll("*"), active);
    return `${active.tagName.toLowerCase()}#${index}:${label.trim().replace(/\s+/g, " ").slice(0, 40)}`;
  });
}

async function walk(page: Page, presses: number, key = "Tab"): Promise<string[]> {
  const stops: string[] = [];
  for (let i = 0; i < presses; i++) {
    await page.keyboard.press(key);
    stops.push(await focused(page));
  }
  return stops;
}

// Critic p00 rounds 7 to 9: Tab after the last navigation link stopped on the document body
// before "Skip to content", a dead stop on every round of the page (kernel/focusCycle).
test.describe("Tab cycle", () => {
  for (const language of ["en", "ar"] as const) {
    test(`Tab and Shift+Tab go round the shell with no stop outside the controls (${language})`, async ({ page }) => {
      await freshStart(page, language);
      await signIn(page, language === "ar" ? users.adminArabic : users.admin);
      await expect(page.locator("main h1")).toBeVisible();
      const skip = language === "ar" ? /:انتقل إلى المحتوى$/ : /:Skip to content$/;

      const forward = await walk(page, 70);
      expect(forward, forward.join(" > ")).not.toContain("BODY");
      // The control after the last one is the first one: "Skip to content", reached at least twice.
      const firsts = forward.map((stop, i) => ({ stop, i })).filter(({ stop }) => skip.test(stop));
      expect(firsts.length, forward.join(" > ")).toBeGreaterThanOrEqual(2);
      const round = firsts[1]!.i - firsts[0]!.i;
      const seen = Math.min(round, forward.length - firsts[1]!.i);
      expect(forward.slice(firsts[1]!.i, firsts[1]!.i + seen), "every round visits the same controls in the same order").toEqual(forward.slice(firsts[0]!.i, firsts[0]!.i + seen));
      expect(new Set(forward.slice(firsts[0]!.i, firsts[1]!.i)).size, "one round visits each control once").toBe(round);

      const backward = await walk(page, round + 2, "Shift+Tab");
      expect(backward, backward.join(" < ")).not.toContain("BODY");
      expect(new Set(backward).size).toBe(round);
    });
  }

  test("the sign-in screen goes round its own controls with no dead stop", async ({ page }) => {
    await freshStart(page, "en");
    await expect(page.locator('input[name="email"]')).toBeFocused();
    const stops = await walk(page, 20);
    expect(stops, stops.join(" > ")).not.toContain("BODY");
    expect(stops.filter((s) => s.startsWith("input#")).length).toBeGreaterThanOrEqual(2);
  });

  test("a modal dialog keeps its own cycle", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.locator("main h1")).toBeVisible();
    await page.keyboard.press("Control+k");
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    for (let i = 0; i < 12; i++) {
      await page.keyboard.press("Tab");
      expect(await page.evaluate(() => !!document.activeElement?.closest('[role="dialog"]'))).toBe(true);
    }
    await page.keyboard.press("Escape");
    await expect(dialog).toHaveCount(0);
  });
});

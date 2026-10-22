import { expect, test, type Page } from "@playwright/test";
import { freshStart, signIn, users } from "./demo";

/**
 * Paper carries no interaction state (critic p04 round 10: a keyboard user Tabbed to a column
 * header of the Arabic users list, printed, and the printout showed the 2 px focus ring round the
 * header). The shell is keyboard-first, so something nearly always has the focus when the user
 * prints. Whatever has the focus, wherever the pointer rests and whichever rows are selected or
 * current, every element that prints must print exactly as it does with none of that: the same
 * outline, shadow, background, borders, colour and text, its ::before and ::after too. And nothing
 * is blurred: the focus is where it was, during and after printing.
 *
 * The check is generic: it compares the whole printed page, not a list of selectors, so a focus or
 * hover style added later by any module is caught as well.
 */

const navigation = (page: Page, language: "en" | "ar") =>
  page.getByRole("navigation", { name: language === "ar" ? "التنقل الرئيسي" : "Main navigation" });

async function setLanguage(page: Page, language: "en" | "ar") {
  const response = await page.request.put("/api/identity/me/preferences", { data: { language }, headers: { "X-Erp-Request": "1" } });
  expect(response.ok()).toBe(true);
}

/**
 * The printed page as data: for every element that prints (it has a box under print media), the
 * styles a state could change. Elements are tagged once, on the first (clean) snapshot, so later
 * snapshots compare the same elements; an element made later is compared by its printed text.
 */
type Paper = { styles: Record<string, string>; labels: Record<string, string>; text: string };

async function paper(page: Page, tag: boolean): Promise<Paper> {
  return page.evaluate((tagNow) => {
    const props = [
      "outline-style",
      "outline-width",
      "outline-color",
      "box-shadow",
      "background-color",
      "background-image",
      "border-top-color",
      "border-right-color",
      "border-bottom-color",
      "border-left-color",
      "border-top-width",
      "border-right-width",
      "border-bottom-width",
      "border-left-width",
      "color",
      "text-decoration-line",
      "opacity",
      "visibility",
    ];
    const pseudoProps = ["content", "outline-style", "box-shadow", "background-color", "border-top-color", "border-bottom-color", "opacity"];
    const styles: Record<string, string> = {};
    const labels: Record<string, string> = {};
    let next = Number((window as unknown as { __paperNext?: number }).__paperNext ?? 0);
    for (const el of Array.from(document.body.querySelectorAll<HTMLElement>("*"))) {
      if (el.getClientRects().length === 0) continue;
      let id = el.dataset.paperProbe;
      if (!id && tagNow) {
        id = String(next++);
        el.dataset.paperProbe = id;
      }
      if (!id) continue;
      const s = getComputedStyle(el);
      const parts = props.map((p) => `${p}:${s.getPropertyValue(p)}`);
      for (const pseudo of ["::before", "::after"]) {
        const ps = getComputedStyle(el, pseudo);
        if (ps.getPropertyValue("content") === "none" || ps.getPropertyValue("display") === "none") continue;
        parts.push(...pseudoProps.map((p) => `${pseudo} ${p}:${ps.getPropertyValue(p)}`));
      }
      const what = `${el.tagName.toLowerCase()}${el.className && typeof el.className === "string" ? "." + el.className.trim().split(/\s+/).join(".") : ""} "${(el.textContent ?? "").trim().slice(0, 20)}"`;
      // Keyed by the element alone: a state may change its classes (is-active, is-selected).
      styles[id] = parts.join("; ");
      labels[id] = what;
    }
    (window as unknown as { __paperNext?: number }).__paperNext = next;
    // The printed-at time in the footer may turn to the next minute between two prints.
    return { styles, labels, text: document.body.innerText.replace(/[0-9٠-٩]+/g, "#") };
  }, tag);
}

function differences(clean: Paper, now: Paper): string[] {
  const out: string[] = [];
  for (const [id, value] of Object.entries(clean.styles)) {
    const after = now.styles[id];
    if (after === undefined || after === value) continue;
    const was = value.split("; ");
    const is = after.split("; ");
    const changed = is.map((p, i) => (p === was[i] ? null : `${was[i]} -> ${p}`)).filter(Boolean);
    out.push(`${now.labels[id] ?? clean.labels[id]}: ${changed.slice(0, 3).join(", ")}`);
  }
  if (now.text !== clean.text) {
    let at = 0;
    while (at < clean.text.length && clean.text[at] === now.text[at]) at++;
    const from = Math.max(0, at - 30);
    out.push(`printed text changed: "…${clean.text.slice(from, at + 50)}…" -> "…${now.text.slice(from, at + 50)}…"`);
  }
  return out;
}

/** Print with the current state: the printed page must equal the clean one, the focus must stay. */
async function printNow(page: Page, clean: Paper, where: string): Promise<string[]> {
  await page.evaluate(() => {
    (window as unknown as { __paperFocus?: Element | null }).__paperFocus = document.activeElement;
  });
  await page.emulateMedia({ media: "print" });
  // Emulated print media restyles the live page, so the browser drops the focus from an element
  // that does not print (display: none); a real print renders a copy and leaves the page alone.
  // The focus is therefore judged on the elements that print, which are the ones the ring was on.
  const { prints, keptWhilePrinting } = await page.evaluate(() => {
    const held = (window as unknown as { __paperFocus?: Element | null }).__paperFocus;
    return { prints: !!held && held !== document.body && held.getClientRects().length > 0, keptWhilePrinting: document.activeElement === held };
  });
  const now = await paper(page, false);
  await page.emulateMedia({ media: "screen" });
  const keptAfter = await page.evaluate(() => {
    const held = (window as unknown as { __paperFocus?: HTMLElement | null }).__paperFocus;
    const kept = document.activeElement === held;
    // An element the emulation blurred (it does not print) gets the focus back, as after a real
    // print, so the walk goes on from it.
    if (!kept && held && held.isConnected) held.focus({ preventScroll: true });
    return kept;
  });
  const problems = differences(clean, now).slice(0, 4).map((p) => `${where}: ${p}`);
  if (prints && !keptWhilePrinting) problems.push(`${where}: printing moved the focus`);
  if (prints && !keptAfter) problems.push(`${where}: the focus did not come back after printing`);
  return problems;
}

/** The page with nothing focused, hovered or selected, once what the screen loads has arrived. */
async function cleanPaper(page: Page) {
  await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
  await page.mouse.move(0, 0);
  const printedText = () => page.evaluate(() => document.body.innerText.replace(/[0-9٠-٩]+/g, "#"));
  await page.emulateMedia({ media: "print" });
  // A record panel fills in a moment after it opens (a company's branches, a user's sign-ins): the
  // clean page is the one that stays the same for a while.
  let before = await printedText();
  for (let i = 0; i < 20; i++) {
    await page.waitForTimeout(150);
    const now = await printedText();
    if (now === before) break;
    before = now;
  }
  const clean = await paper(page, true);
  await page.emulateMedia({ media: "screen" });
  return clean;
}

const focusName = (page: Page) =>
  page.evaluate(() => {
    const el = document.activeElement as HTMLElement | null;
    if (!el || el === document.body || !el.closest("main")) return null;
    const name = el.getAttribute("aria-label") || el.textContent?.trim().slice(0, 24) || el.getAttribute("name") || "";
    return `${el.tagName.toLowerCase()} "${name}"`;
  });

/** Tab stops of the main region visited per screen: the header row, the grid, the record panel. */
const maxStops = 30;
const maxPanelStops = 12;

for (const language of ["en", "ar"] as const) {
  test(`whatever has the focus, the pointer or the selection, every screen prints as it does with none of it (${language})`, async ({ page }) => {
    test.setTimeout(150_000);
    await page.setViewportSize({ width: 1440, height: 900 });
    await freshStart(page, language);
    await signIn(page, language === "ar" ? users.adminArabic : users.admin);
    await expect(page.locator("main h1")).toBeVisible();
    await setLanguage(page, language);
    await page.reload();
    await expect(page.locator("html")).toHaveAttribute("lang", language);
    const nav = navigation(page, language);
    const links = await nav.locator("a").evaluateAll((as) => as.map((a) => a.getAttribute("href")!));
    const problems: string[] = [];
    let stops = 0;
    let screens = 0;
    let records = 0;
    try {
      for (const href of links) {
        await nav.locator(`a[href="${href}"]`).click();
        await expect(page.locator("main h1")).toBeVisible();
        const list = (await page.locator("main .list-grid, main table tbody tr").count()) > 0;
        if (list) await expect(page.locator("main .list-row, main table tbody tr").first()).toBeVisible();
        await page.evaluate(() => {
          for (const el of Array.from(document.querySelectorAll("[data-paper-probe]"))) el.removeAttribute("data-paper-probe");
        });
        const clean = await cleanPaper(page);
        screens++;

        // Every Tab stop of the screen, as a keyboard user leaves it before Ctrl+P.
        await page.locator("main").focus();
        for (let i = 0; i < maxStops; i++) {
          await page.keyboard.press("Tab");
          const name = await focusName(page);
          // The focus left the main region, or came round to its first stop again.
          if (name === null) break;
          const round = await page.evaluate((first) => {
            const w = window as unknown as { __firstStop?: Element | null };
            if (first) w.__firstStop = document.activeElement;
            return !first && document.activeElement === w.__firstStop;
          }, i === 0);
          if (round) break;
          problems.push(...(await printNow(page, clean, `${href} (${language}), focus on stop ${i + 1} ${name}`)));
          stops++;
        }

        if (list) {
          // The list's own keys: the grid focused, its current row marked, a row selected with Space.
          const grid = page.locator("main .list-grid");
          if ((await grid.count()) > 0) {
            await grid.first().focus();
            await page.keyboard.press("ArrowDown");
            problems.push(...(await printNow(page, clean, `${href} (${language}), current row`)));
            await page.keyboard.press("Space");
            problems.push(...(await printNow(page, clean, `${href} (${language}), selected row`)));
            await page.keyboard.press("Space");
          }
          // The pointer resting on a row.
          await page.locator("main .list-row, main table tbody tr").first().hover();
          problems.push(...(await printNow(page, clean, `${href} (${language}), pointer on a row`)));
          await page.mouse.move(0, 0);

          // A record open beside the list (Enter on the current row): every Tab stop of its form.
          if ((await grid.count()) > 0) {
            await grid.first().focus();
            await page.keyboard.press("Enter");
            const panel = page.locator("main aside.list-record").first();
            if (await panel.isVisible().catch(() => false)) {
              await expect(panel.locator("input, select, textarea").first()).toBeVisible();
              const withRecord = await cleanPaper(page);
              await panel.locator("input, select, textarea").first().focus();
              for (let i = 0; i < maxPanelStops; i++) {
                const inPanel = await page.evaluate(() => !!document.activeElement?.closest("aside.list-record"));
                if (!inPanel) break;
                const name = await focusName(page);
                problems.push(...(await printNow(page, withRecord, `${href} (${language}), record open, focus on panel stop ${i + 1} ${name}`)));
                stops++;
                records++;
                await page.keyboard.press("Tab");
              }
            }
          }
        }
      }
    } finally {
      await page.emulateMedia({ media: "screen" });
    }
    expect(screens, "screens printed").toBeGreaterThanOrEqual(8);
    expect(stops, "Tab stops printed").toBeGreaterThanOrEqual(100);
    expect(records, "Tab stops printed in an open record").toBeGreaterThanOrEqual(20);
    expect(problems).toEqual([]);
  });
}

test("a keyboard user's printout of the Arabic users list carries no focus ring on the column header they left the focus on", async ({ page }) => {
  // The critic's own path, round 10: G then U, Tab to the Name header, print.
  await freshStart(page, "ar");
  await signIn(page, users.adminArabic);
  await expect(page.locator("main h1")).toBeVisible();
  await setLanguage(page, "ar");
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("lang", "ar");
  await page.keyboard.press("g");
  await page.keyboard.press("u");
  await expect(page.locator("main .list-row").first()).toBeVisible();
  const header = page.locator("main .list-sort", { hasText: "الاسم" }).first();
  await header.focus();
  await expect(header).toBeFocused();
  const onScreen = await header.evaluate((el) => getComputedStyle(el).outlineStyle);
  expect(onScreen, "the focus ring shows on screen").not.toBe("none");
  await page.emulateMedia({ media: "print" });
  try {
    const printed = await header.evaluate((el) => {
      const s = getComputedStyle(el);
      return { outline: s.outlineStyle, shadow: s.boxShadow, focused: document.activeElement === el, visible: el.getClientRects().length > 0 };
    });
    expect(printed).toEqual({ outline: "none", shadow: "none", focused: true, visible: true });
  } finally {
    await page.emulateMedia({ media: "screen" });
  }
  await expect(header).toBeFocused();
  expect(await header.evaluate((el) => getComputedStyle(el).outlineStyle)).not.toBe("none");
});

/**
 * A list on paper fits the page (critic p04 round 11: on A4 the users grid was 743 px wide in
 * English and 789 px in Arabic against 718 px of page, so the Roles column was off the page and the
 * last sign-in time printed as "…0:35"); and a record open beside the list prints whole, not as a
 * panel cut at the screen's height. The page is the A4 printable width of the print stylesheet
 * (210 mm less 12 mm margins on each side: 186 mm, 703 px).
 */
const a4PrintableWidth = Math.floor((186 / 25.4) * 96);

for (const language of ["en", "ar"] as const) {
  test(`the users list and an open user record print inside an A4 page, nothing cut (${language})`, async ({ page }) => {
    await freshStart(page, language);
    await signIn(page, language === "ar" ? users.adminArabic : users.admin);
    await expect(page.locator("main h1")).toBeVisible();
    await setLanguage(page, language);
    await page.reload();
    await expect(page.locator("html")).toHaveAttribute("lang", language);
    await page.keyboard.press("g");
    await page.keyboard.press("u");
    await expect(page.locator("main .list-row").first()).toBeVisible();
    await page.setViewportSize({ width: a4PrintableWidth, height: 1000 });

    const onPaper = () =>
      page.evaluate(() => {
        const width = document.documentElement.clientWidth;
        const problems: string[] = [];
        const name = (el: Element) => `${el.className} "${(el.textContent ?? "").trim().slice(0, 24)}"`;
        const cells = Array.from(document.querySelectorAll("main .list-header > *, main .list-row > *")).filter((el) => el.getClientRects().length > 0);
        for (const el of cells) {
          const box = el.getBoundingClientRect();
          if (box.left < -1 || box.right > width + 1) problems.push(`off the page (${Math.round(box.left)}..${Math.round(box.right)} of ${width}): ${name(el)}`);
          if (el.scrollWidth > el.clientWidth + 1) problems.push(`cut (${el.scrollWidth} > ${el.clientWidth}): ${name(el)}`);
          const text = el.querySelector(".list-text, span[dir]");
          if (text && text.scrollWidth > text.clientWidth + 1) problems.push(`value cut: ${name(el)}`);
        }
        const shown = (el: Element) => el.getClientRects().length > 0 && getComputedStyle(el).visibility !== "hidden";
        const headers = Array.from(document.querySelectorAll("main .list-header > *")).filter(shown).map((el) => (el.textContent ?? "").trim());
        // The selection column takes no room on paper.
        for (const el of Array.from(document.querySelectorAll("main .list-select"))) {
          if (shown(el) || el.getBoundingClientRect().width > 0.5) problems.push(`the selection column prints: ${name(el)}`);
        }
        const record = document.querySelector("main aside.list-record");
        let recordFacts: { left: number; right: number; cut: boolean; inputsOff: number } | null = null;
        if (record && record.getClientRects().length > 0) {
          const box = record.getBoundingClientRect();
          const inputsOff = Array.from(record.querySelectorAll("input, select, textarea, h2, h3, legend")).filter((el) => {
            const r = el.getBoundingClientRect();
            return el.getClientRects().length > 0 && (r.left < -1 || r.right > width + 1);
          }).length;
          recordFacts = { left: box.left, right: box.right, cut: record.scrollHeight > record.clientHeight + 1, inputsOff };
        }
        return { width, problems, headers, cells: cells.length, recordFacts };
      });

    await page.emulateMedia({ media: "print" });
    try {
      const list = await onPaper();
      expect(list.problems).toEqual([]);
      // Every column the screen shows prints, with its heading; no selection column.
      expect(list.headers.length).toBeGreaterThanOrEqual(6);
      expect(list.headers.every((h) => h.length > 0)).toBe(true);
      expect(list.cells).toBeGreaterThan(list.headers.length * 3);
    } finally {
      await page.emulateMedia({ media: "screen" });
    }

    // A record open beside the list: on paper it follows the list, at the page's width, whole.
    await page.locator("main .list-grid").focus();
    await page.keyboard.press("Enter");
    const panel = page.locator("main aside.list-record").first();
    await expect(panel.locator("input").first()).toBeVisible();
    await page.emulateMedia({ media: "print" });
    try {
      const withRecord = await onPaper();
      expect(withRecord.problems).toEqual([]);
      expect(withRecord.recordFacts).not.toBeNull();
      const facts = withRecord.recordFacts!;
      expect(facts.left).toBeGreaterThanOrEqual(-1);
      expect(facts.right).toBeLessThanOrEqual(withRecord.width + 1);
      expect(facts.right - facts.left).toBeGreaterThan(withRecord.width * 0.9);
      expect(facts.cut, "the record is cut at a height").toBe(false);
      expect(facts.inputsOff).toBe(0);
    } finally {
      await page.emulateMedia({ media: "screen" });
    }
  });
}

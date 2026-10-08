import { expect, type Page } from "@playwright/test";

/**
 * Accessibility basics checked on a live screen: every control has an accessible name, and text
 * meets the WCAG 2.2 AA contrast ratio (4.5:1, or 3:1 for large text) against its background.
 * Returns the number of elements checked so callers can assert the check saw the screen.
 */
export async function checkAccessibility(page: Page, where: string): Promise<number> {
  const result = await page.evaluate(() => {
    const problems: string[] = [];
    const visible = (el: Element) => {
      const r = (el as HTMLElement).getBoundingClientRect();
      const s = getComputedStyle(el);
      return r.width > 0 && r.height > 0 && s.visibility !== "hidden" && s.display !== "none";
    };
    const describe = (el: Element) => `${el.tagName.toLowerCase()}${el.id ? "#" + el.id : ""}${el.className && typeof el.className === "string" ? "." + el.className.split(" ").join(".") : ""} "${(el.textContent ?? "").trim().slice(0, 40)}"`;
    const nameOf = (el: Element): string => {
      const label = el.getAttribute("aria-label");
      if (label?.trim()) return label;
      const by = el.getAttribute("aria-labelledby");
      if (by) return by.split(/\s+/).map((id) => document.getElementById(id)?.textContent ?? "").join(" ");
      if (el instanceof HTMLInputElement || el instanceof HTMLSelectElement || el instanceof HTMLTextAreaElement) {
        const labels = [...(el.labels ?? [])].map((l) => l.textContent ?? "").join(" ");
        if (labels.trim()) return labels;
      }
      const text = (el.textContent ?? "").trim();
      if (text) return text;
      return el.getAttribute("title") ?? el.getAttribute("alt") ?? "";
    };
    const controls = [...document.querySelectorAll('a[href], button, input:not([type="hidden"]), select, textarea, [role="button"], [role="option"], [role="combobox"], [role="dialog"], nav')].filter(visible);
    for (const el of controls) if (!nameOf(el).trim()) problems.push(`no accessible name: ${describe(el)}`);

    const parse = (c: string) => {
      const m = c.match(/rgba?\(([^)]+)\)/);
      if (!m) return [0, 0, 0, 1];
      const p = m[1]!.split(/[,\s/]+/).filter(Boolean).map(Number);
      return [p[0]!, p[1]!, p[2]!, p[3] ?? 1];
    };
    const lum = ([r, g, b]: number[]) => {
      const f = (v: number) => {
        const s = v / 255;
        return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
      };
      return 0.2126 * f(r!) + 0.7152 * f(g!) + 0.0722 * f(b!);
    };
    const background = (el: Element): number[] => {
      const layers: number[][] = [];
      for (let e: Element | null = el; e; e = e.parentElement) {
        const c = parse(getComputedStyle(e).backgroundColor);
        if (c[3]! > 0) {
          layers.push(c);
          if (c[3] === 1) break;
        }
      }
      let out = [255, 255, 255];
      for (const layer of layers.reverse()) out = out.map((v, i) => layer[i]! * layer[3]! + v * (1 - layer[3]!));
      return out;
    };
    let texts = 0;
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    const seen = new Set<Element>();
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      const el = n.parentElement;
      if (!el || seen.has(el) || !(n.textContent ?? "").trim() || !visible(el)) continue;
      if (el.closest("[aria-hidden='true'], .visually-hidden, button:disabled, [disabled]")) continue;
      seen.add(el);
      texts++;
      const style = getComputedStyle(el);
      if (Number(style.opacity) < 1) continue;
      const fg = parse(style.color);
      const bg = background(el);
      const mixed = fg.slice(0, 3).map((v, i) => v * fg[3]! + bg[i]! * (1 - fg[3]!));
      const [a, b] = [lum(mixed), lum(bg)];
      const ratio = (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
      const size = parseFloat(style.fontSize);
      const large = size >= 24 || (size >= 18.66 && Number(style.fontWeight) >= 700);
      if (ratio < (large ? 3 : 4.5)) problems.push(`contrast ${ratio.toFixed(2)}:1 for ${describe(el)}`);
    }
    return { problems, count: controls.length + texts };
  });
  expect(result.problems, `${where}:\n${result.problems.join("\n")}`).toEqual([]);
  return result.count;
}

/** The element that has focus shows a visible focus indicator. */
export async function expectFocusRing(page: Page, where: string) {
  const ring = await page.evaluate(() => {
    const el = document.activeElement as HTMLElement | null;
    if (!el || el === document.body) return { ok: false, what: "nothing focused" };
    const s = getComputedStyle(el);
    const outline = s.outlineStyle !== "none" && parseFloat(s.outlineWidth) >= 2;
    const shadow = s.boxShadow !== "none";
    return { ok: outline || shadow, what: `${el.tagName} ${s.outlineStyle} ${s.outlineWidth} ${s.boxShadow}` };
  });
  expect(ring.ok, `${where}: focused element has no visible focus ring (${ring.what})`).toBe(true);
}

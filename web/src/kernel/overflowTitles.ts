/**
 * No text is cut without a way to read it. Wherever the screen cuts a value with an ellipsis (list
 * cells, headers, labels, badges, buttons, tabs, input values and placeholders, in either language
 * and at either density), the full text is put in the element's title, so hovering (or focusing)
 * shows it. One sweep serves every screen, the kernel's and the modules': it runs after the page
 * changes (batched to one pass per frame burst), and again when the window or the density changes.
 * An element that already has a title of its own keeps it; a title this sweep set goes again when
 * the text fits.
 */
const candidates = "td, th, [role=gridcell], [role=columnheader], [role=tab], [role=option], [role=menuitem], .list-text, .list-sort-label, .list-headlabel, .badge, label, legend, button, a, h1, h2, h3, dt, dd, li, .truncate, input, select, textarea";
const marker = "data-overflow-title";

function fullText(el: HTMLElement): string {
  if (el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement) return (el.value || el.placeholder || "").trim();
  if (el instanceof HTMLSelectElement) return (el.selectedOptions[0]?.textContent ?? "").trim();
  return (el.textContent ?? "").replace(/\s+/g, " ").trim();
}

let measureContext: CanvasRenderingContext2D | null | undefined;

/** The width a text needs in an element's font, measured off the page so the page itself never changes. */
function textWidth(text: string, style: CSSStyleDeclaration): number | null {
  if (measureContext === undefined) {
    measureContext = /jsdom/i.test(navigator.userAgent) ? null : document.createElement("canvas").getContext("2d");
  }
  if (!measureContext) return null;
  measureContext.font = style.font || `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
  measureContext.letterSpacing = style.letterSpacing && style.letterSpacing !== "normal" ? style.letterSpacing : "0px";
  return measureContext.measureText(text).width;
}

function isCut(el: HTMLElement): boolean {
  if (el instanceof HTMLInputElement && el.value === "" && el.placeholder) {
    // A placeholder is cut when it needs more room than the box has. It is measured on a canvas, not on
    // a hidden clone added to the page: a clone changed the page after every sweep, with no one acting.
    const style = getComputedStyle(el);
    let need = textWidth(el.placeholder, style);
    if (need === null) {
      const probe = document.createElement("span");
      probe.style.cssText = `position:absolute;visibility:hidden;white-space:pre;font:${style.font};letter-spacing:${style.letterSpacing}`;
      probe.textContent = el.placeholder;
      document.body.appendChild(probe);
      need = probe.getBoundingClientRect().width;
      probe.remove();
    }
    return need > el.clientWidth - 12;
  }
  return el.scrollWidth > el.clientWidth + 1 || (el.clientHeight > 0 && el.scrollHeight > el.clientHeight + 2 && getComputedStyle(el).overflowY !== "visible");
}

export function sweepOverflowTitles(root: ParentNode = document): number {
  let set = 0;
  for (const el of root.querySelectorAll<HTMLElement>(candidates)) {
    const own = el.hasAttribute("title") && !el.hasAttribute(marker);
    if (own) continue;
    if (el.closest("[aria-hidden=true]") || el.classList.contains("visually-hidden")) continue;
    if (el.clientWidth === 0 && !(el instanceof HTMLInputElement)) {
      if (el.hasAttribute(marker)) {
        el.removeAttribute("title");
        el.removeAttribute(marker);
      }
      continue;
    }
    if (isCut(el)) {
      const text = fullText(el);
      if (text && el.getAttribute("title") !== text) {
        el.setAttribute("title", text);
        el.setAttribute(marker, "");
        set++;
      }
    } else if (el.hasAttribute(marker)) {
      el.removeAttribute("title");
      el.removeAttribute(marker);
    }
  }
  return set;
}

/** Start the sweep for this document; returns a function that stops it. */
export function installOverflowTitles(): () => void {
  if (typeof MutationObserver === "undefined") return () => {};
  let timer: number | undefined;
  const later = () => {
    if (timer !== undefined) return;
    timer = window.setTimeout(() => {
      timer = undefined;
      sweep();
    }, 120);
  };
  const observer = new MutationObserver(later);
  // The sweep's own changes (the hidden placeholder probe it adds and removes) are dropped, so a sweep
  // never arms the next one: without this, any empty input with a placeholder kept the page changing
  // every 120 ms with no one acting on it.
  const sweep = (root?: ParentNode) => {
    sweepOverflowTitles(root);
    observer.takeRecords();
  };
  observer.observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ["class", "value", "data-density", "dir", "lang"] });
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ["data-density", "data-theme", "dir", "lang"] });
  window.addEventListener("resize", later);
  // The cell under the pointer or focus is checked at once, whatever the sweep has not reached yet.
  const now = (e: Event) => {
    const el = (e.target as HTMLElement | null)?.closest?.<HTMLElement>(candidates);
    if (el) sweep(el.parentElement ?? document);
  };
  document.addEventListener("pointerover", now, true);
  document.addEventListener("focusin", now, true);
  later();
  return () => {
    observer.disconnect();
    window.removeEventListener("resize", later);
    document.removeEventListener("pointerover", now, true);
    document.removeEventListener("focusin", now, true);
    if (timer !== undefined) window.clearTimeout(timer);
  };
}

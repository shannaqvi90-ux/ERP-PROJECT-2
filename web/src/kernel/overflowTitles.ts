/**
 * No text is cut without a way to read it. Wherever the screen cuts a value with an ellipsis (list
 * cells, headers, labels, badges, buttons, tabs, input values and placeholders, in either language
 * and at either density), the full text is put in the element's title, so hovering (or focusing)
 * shows it. One handler serves every screen, the kernel's and the modules': the title is worked out
 * only when the person points at or focuses the element, in the task of that pointer or focus event.
 * Nothing runs from a timer, an observer or a sweep of the page, so the page never changes by itself.
 * An element that already has a title of its own keeps it; a title this handler set goes again when
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

/** Check every candidate under a root (used by tests and tools; the page itself only checks what is pointed at). */
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

/** The elements that may carry a title at the point of an event: its target and the candidates around it. */
function around(target: EventTarget | null): HTMLElement[] {
  const found: HTMLElement[] = [];
  let el: Element | null = target instanceof Element ? target : null;
  for (let depth = 0; el && depth < 4; depth++, el = el.parentElement) {
    if (el instanceof HTMLElement && el.matches(candidates)) found.push(el);
  }
  return found;
}

/** Give one element its title when its text is cut, or take its own title away when the text fits. */
function titleOne(el: HTMLElement): void {
  const own = el.hasAttribute("title") && !el.hasAttribute(marker);
  if (own || el.closest("[aria-hidden=true]") || el.classList.contains("visually-hidden")) return;
  if (el.clientWidth === 0 && !(el instanceof HTMLInputElement)) return;
  if (isCut(el)) {
    const text = fullText(el);
    if (text && el.getAttribute("title") !== text) {
      el.setAttribute("title", text);
      el.setAttribute(marker, "");
    }
  } else if (el.hasAttribute(marker)) {
    el.removeAttribute("title");
    el.removeAttribute(marker);
  }
}

/**
 * Start titling for this document; returns a function that stops it. The title of a cut cell is
 * computed when the person points at or focuses it, in the task of that pointer or focus event, and
 * never from a timer, an observer or a sweep that writes to the page. The browser also dispatches
 * pointer events by itself when the page changes under a still pointer (the pointer is "moved" to where
 * it already is); those events carry no movement and are ignored, so the page never changes by itself
 * and a comparison harness sees no change that no step caused.
 */
export function installOverflowTitles(): () => void {
  const point = (e: Event) => {
    const m = e as PointerEvent;
    if ((!m.movementX && !m.movementY)) return;
    for (const el of around(e.target)) titleOne(el);
  };
  const focus = (e: Event) => {
    for (const el of around(e.target)) titleOne(el);
  };
  document.addEventListener("pointermove", point, true);
  document.addEventListener("focusin", focus, true);
  return () => {
    document.removeEventListener("pointermove", point, true);
    document.removeEventListener("focusin", focus, true);
  };
}

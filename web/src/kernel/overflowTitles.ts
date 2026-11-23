/**
 * No text is cut without a way to read it. Wherever the screen cuts a value with an ellipsis (list
 * cells, headers, labels, badges, buttons, tabs, input values and placeholders, in either language
 * and at either density), the full text is put in the element's title, so hovering (or focusing)
 * shows it. One routine serves every screen, the kernel's and the modules': the title is worked out
 * in the task of a render or a focus, never from a timer, an observer or a pointer move, so the page
 * never changes by itself.
 * An element that already has a title of its own keeps it; a title this handler set goes again when
 * the text fits.
 */
import { useEffect } from "react";

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
 * Titles are worked out in the task that changed the page, never on their own. A screen's component
 * calls this hook, and after each of its renders (the render a step of the person caused, or the
 * answer to a request such a step sent) the cut text under the document is titled, in that same task.
 * Pointer events are not used: the browser delivers pointer moves with the animation frame, and a
 * page that writes there looks, to a comparison harness, like a page changing by itself.
 */
export function useOverflowTitles(): void {
  useEffect(() => {
    sweepOverflowTitles();
  });
}

/**
 * Start titling for this document; returns a function that stops it. A focused element is titled in
 * the task of the key or press that focused it (an input task a step caused), for what no render
 * touched. Nothing runs from a timer, an observer or a pointer move.
 */
export function installOverflowTitles(): () => void {
  const focus = (e: Event) => {
    for (const el of around(e.target)) titleOne(el);
  };
  document.addEventListener("focusin", focus, true);
  return () => document.removeEventListener("focusin", focus, true);
}

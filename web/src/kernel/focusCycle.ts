/**
 * Tab and Shift+Tab cycle through the page without a stop outside it. Past the last control the
 * browser would otherwise move the focus to the document itself (or its own toolbar), a dead stop
 * a keyboard user has to press through on every round (critic p00 rounds 7 to 9). Here Tab on the
 * last control goes to the first one (in the shell, "Skip to content"), and Shift+Tab on the first
 * goes to the last. The browser's own keys still leave the page: F6 and Ctrl+L reach the address
 * bar. A modal dialog cycles within itself (kernel/dialog) and is left alone.
 */

const candidates = [
  "a[href]",
  "area[href]",
  "button:not([disabled])",
  "input:not([disabled]):not([type=hidden])",
  "select:not([disabled])",
  "textarea:not([disabled])",
  "iframe",
  "summary",
  "[contenteditable]:not([contenteditable=false])",
  "[tabindex]",
].join(",");

function visible(element: HTMLElement): boolean {
  if (element.closest("[hidden],[inert]")) return false;
  const check = (element as HTMLElement & { checkVisibility?: (options?: object) => boolean }).checkVisibility;
  return typeof check === "function" ? check.call(element, { visibilityProperty: true }) : true;
}

/** The elements Tab visits, in order, or null when the order is not plain document order (a
 * positive tabindex) and the browser should decide alone. */
export function tabOrder(root: Document | HTMLElement = document): HTMLElement[] | null {
  const items: HTMLElement[] = [];
  for (const element of root.querySelectorAll<HTMLElement>(candidates)) {
    if (element.tabIndex < 0) continue;
    if (element.tabIndex > 0) return null;
    if (element instanceof HTMLInputElement && element.type === "radio" && !element.checked) {
      // A radio group is one stop: the checked radio, or the first of a group with none checked.
      const group = element.name && element.form
        ? [...element.form.querySelectorAll<HTMLInputElement>(`input[type=radio][name="${CSS.escape(element.name)}"]`)]
        : [];
      if (group.some((radio) => radio.checked) || (group.length > 0 && group[0] !== element)) continue;
    }
    if (!visible(element)) continue;
    items.push(element);
  }
  return items;
}

/** Install the page-wide Tab cycle on <paramref name="doc"/>; returns the function that removes it. */
export function installFocusCycle(doc: Document = document): () => void {
  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key !== "Tab" || event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || event.isComposing) return;
    if (doc.querySelector('[role="dialog"][aria-modal="true"]')) return;
    const items = tabOrder(doc);
    if (!items || items.length < 2) return;
    const active = doc.activeElement;
    const first = items[0]!;
    const last = items[items.length - 1]!;
    if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    } else if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    }
  };
  doc.addEventListener("keydown", onKeyDown);
  return () => doc.removeEventListener("keydown", onKeyDown);
}

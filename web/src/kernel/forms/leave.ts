/**
 * Unsaved changes: a form with changes registers a guard while it has them. Leaving the screen
 * (an in-app link, the menu, the palette), opening another record in the same list, closing the
 * panel or reloading the tab first asks whether to throw the changes away. One registry for the
 * whole app, so every way out asks the same question.
 */
type Guard = { message: () => string };

const guards = new Set<Guard>();

/** Register a guard; returns the function that removes it. */
export function addLeaveGuard(guard: Guard): () => void {
  guards.add(guard);
  return () => {
    guards.delete(guard);
  };
}

/** True when nothing unsaved is on screen. */
export const nothingUnsaved = (): boolean => guards.size === 0;

/**
 * True when it is fine to leave: nothing is unsaved, or the user agreed to discard it. The
 * question is the browser's own confirmation (it answers to Enter and Escape on every platform).
 */
export function confirmLeave(): boolean {
  const first = guards.values().next();
  if (first.done) return true;
  return window.confirm(first.value.message());
}

/** Forget every guard (a document that starts over, the tests). */
export function clearLeaveGuards(): void {
  guards.clear();
}

let unloadHooked = false;

/** The browser asks before a reload or closing the tab while anything is unsaved. */
export function hookUnload(): void {
  if (unloadHooked || typeof window === "undefined") return;
  unloadHooked = true;
  window.addEventListener("beforeunload", (event) => {
    if (guards.size === 0) return;
    event.preventDefault();
    // Older browsers show the dialog only when returnValue is set.
    event.returnValue = "";
  });
}

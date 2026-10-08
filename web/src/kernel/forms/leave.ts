/**
 * Unsaved changes: a form with changes registers a guard while it has them. Leaving the screen
 * (an in-app link, the menu, the palette), opening another record in the same list, closing the
 * panel or reloading the tab first asks what to do with them. One registry for the whole app, so
 * every way out asks the same question: the record form's own dialog (save and leave, discard and
 * leave, keep editing) wherever the form can show it, the browser's confirmation otherwise (a
 * reload or closing the tab, which only the browser can ask about).
 */
type Guard = {
  message: () => string;
  /** Asks in the app's own dialog and calls `proceed` once the changes are saved or given up
   * (never, when the user keeps editing). Without it the browser's confirmation asks. */
  ask?: (proceed: () => void) => void;
};

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
 * True when it is fine to leave: nothing is unsaved, or the user agreed to discard it, asked with
 * the browser's own confirmation. For a caller that cannot wait for an answer; every in-app way
 * out uses requestLeave() and the form's dialog instead.
 */
export function confirmLeave(): boolean {
  const first = guards.values().next();
  if (first.done) return true;
  return window.confirm(first.value.message());
}

/**
 * Leaves once it is fine to: at once when nothing is unsaved, else after the form's dialog (or the
 * browser's confirmation) let it go. `proceed` is the leaving itself (go to another screen, open
 * another record, close the panel).
 */
export function requestLeave(proceed: () => void): void {
  const first = guards.values().next();
  if (first.done) {
    proceed();
    return;
  }
  const guard = first.value;
  if (guard.ask) guard.ask(proceed);
  else if (window.confirm(guard.message())) proceed();
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

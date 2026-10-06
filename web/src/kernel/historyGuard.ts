/**
 * The tab's address history belongs to one identity at a time.
 *
 * Screens keep their state in the address so Back, Forward, reload and links work: a list's
 * search text (?q=), the open record (?open=id), filters, the new-record form. The browser keeps
 * every address of the tab in its session history, and a person signing out cannot take those
 * entries out of it: `startOver()` replaces only the current entry. On a shared device (an
 * outsourced bookkeeper serving several client companies from one browser, a shop counter PC) the
 * next person pressing Back would otherwise open their own screen at the previous person's
 * address: the previous tenant's search text in their search box, the previous tenant's record id
 * asked for (critic p04 round 3).
 *
 * So every history entry the app writes is stamped with the identity epoch of this tab: a random
 * value kept in sessionStorage, which `forgetIdentity()` clears whenever an identity ends (sign-out,
 * a session that ends, a change of user or tenant). An entry reached by Back, Forward or a reload
 * is trusted only when it carries the current epoch; any other is replaced by the home address
 * before a screen reads it. A fresh navigation (a typed address, a bookmark, a link) is what the
 * person in front of the tab asks for now, so it is trusted and stamped.
 *
 * The epoch is a random value, never an id, a name or anything of a tenant's.
 */

/** sessionStorage key of the tab's identity epoch (cleared with sessionStorage when an identity ends). */
export const historyEpochKey = "erp.historyEpoch";

/** The property of `history.state` that carries the epoch of the identity that wrote the entry. */
export const historyStampKey = "erpEpoch";

/** Where an untrusted entry goes: the home screen, which reads nothing from the address. */
export const historyFallback = "/";

/** How the browser reached this document (Navigation Timing). */
export type NavigationKind = "navigate" | "reload" | "back_forward" | "prerender";

const guardMark = Symbol.for("erp.historyGuard");

type GuardedFunction = ((data: unknown, unused: string, url?: string | URL | null) => void) & { [guardMark]?: true };

function randomEpoch(): string {
  const bytes = new Uint8Array(12);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
}

/**
 * The identity epoch of this tab, created when first needed. Without sessionStorage (blocked site
 * data) there is no epoch to keep: each document gets its own, so entries of earlier documents are
 * never trusted.
 */
export function historyEpoch(): string {
  try {
    const stored = sessionStorage.getItem(historyEpochKey);
    if (stored) return stored;
    const created = randomEpoch();
    sessionStorage.setItem(historyEpochKey, created);
    return created;
  } catch {
    const holder = window as unknown as Record<symbol, string | undefined>;
    return (holder[guardMark] ??= randomEpoch());
  }
}

/** A state object for an entry written now: the caller's state with the current epoch added. */
export function stampState(state: unknown): Record<string, unknown> {
  const epoch = historyEpoch();
  if (state !== null && typeof state === "object" && !Array.isArray(state)) return { ...(state as Record<string, unknown>), [historyStampKey]: epoch };
  return state === null || state === undefined ? { [historyStampKey]: epoch } : { [historyStampKey]: epoch, value: state };
}

/** The entry was written by the identity this tab holds now. */
export function entryIsCurrent(state: unknown = window.history.state): boolean {
  if (state === null || typeof state !== "object") return false;
  const stamp = (state as Record<string, unknown>)[historyStampKey];
  return typeof stamp === "string" && stamp.length > 0 && stamp === historyEpoch();
}

/** How this document was reached. Unknown (an old browser) counts as Back: never trusted blindly. */
export function navigationKind(): NavigationKind {
  try {
    const entry = performance.getEntriesByType?.("navigation")[0] as PerformanceNavigationTiming | undefined;
    const type = entry?.type as NavigationKind | undefined;
    if (type === "navigate" || type === "reload" || type === "back_forward" || type === "prerender") return type;
  } catch {
    // No Navigation Timing.
  }
  return "back_forward";
}

/** Replace the current entry with the home address, stamped for the current identity. */
function forgetEntry(original: GuardedFunction): void {
  original.call(window.history, { [historyStampKey]: historyEpoch() }, "", historyFallback);
}

/**
 * Install the guard before any screen reads the address (main.tsx, before the first render).
 * Every `history.pushState` / `history.replaceState` of the app (the router, lists, modules) then
 * stamps its entry; the entry this document opened on is checked; and every entry reached by Back
 * or Forward inside this document is checked before the router reacts to it. Returns a function
 * that removes the guard (tests).
 */
export function installHistoryGuard(kind: NavigationKind = navigationKind()): () => void {
  const history = window.history;
  const push = history.pushState as GuardedFunction;
  const replace = history.replaceState as GuardedFunction;
  const originalPush: GuardedFunction = push[guardMark] ? (History.prototype.pushState as GuardedFunction) : push;
  const originalReplace: GuardedFunction = replace[guardMark] ? (History.prototype.replaceState as GuardedFunction) : replace;

  const guardedPush: GuardedFunction = function (data, unused, url) {
    originalPush.call(history, stampState(data), unused, url);
  };
  const guardedReplace: GuardedFunction = function (data, unused, url) {
    originalReplace.call(history, stampState(data), unused, url);
  };
  guardedPush[guardMark] = true;
  guardedReplace[guardMark] = true;
  history.pushState = guardedPush;
  history.replaceState = guardedReplace;

  // The entry this document opened on.
  if (kind === "navigate" || kind === "prerender") {
    // What the person asked for just now: keep it, and stamp it for the identity of this tab.
    originalReplace.call(history, stampState(history.state), "", window.location.href);
  } else if (!entryIsCurrent()) {
    forgetEntry(originalReplace);
  }

  // Back and Forward inside this document. Registered before any screen's listener, so the
  // router and the screens see the home address, never the untrusted one.
  const onPopState = (event: PopStateEvent) => {
    if (entryIsCurrent(event.state)) return;
    forgetEntry(originalReplace);
  };
  window.addEventListener("popstate", onPopState);

  return () => {
    window.removeEventListener("popstate", onPopState);
    history.pushState = originalPush;
    history.replaceState = originalReplace;
  };
}

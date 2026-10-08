/**
 * What this browser keeps for a signed-in person ends with that person.
 *
 * The shell is a single-page app: one document stays in memory while people sign in and out. On
 * a shared device (an outsourced bookkeeper serving several client companies from one browser, a
 * shop counter PC) anything one identity leaves behind in the document's memory, in storage or in
 * a browser cache would be shown to the next person, who may belong to another tenant. So:
 *
 *  1. Signing out, a session that ends, and any change of identity (another user, another tenant)
 *     end the document: `startOver()` replaces it with a fresh one, so no JavaScript memory
 *     (module state, caches, React state) carries over. `replace`, not `assign`: the signed-in
 *     page leaves the history, so Back cannot bring it back (and it is not kept for Back either,
 *     see the pageshow guard in main.tsx).
 *  2. Before that, `forgetIdentity()` clears what the browser stores for this origin: every
 *     localStorage key except the device's own settings (`deviceKeys`), all of sessionStorage
 *     (with it the tab's history epoch, so the ended identity's entries in the tab's history are
 *     never trusted again: kernel/historyGuard), every IndexedDB database, every Cache Storage
 *     cache, every cookie a script can read (for every in-app path; the session cookie is HttpOnly
 *     and ended by the server, whose sign-out answer also drops every cookie of the site) and the
 *     tab's `window.name`, which survives the document.
 *  3. A module that wants an in-memory cache makes it with `identityScoped()`; it is emptied when
 *     the identity ends (and the document is replaced anyway). The client-state gate
 *     (tests/Erp.Gates.Tests/G1/G1ClientStateTests.cs) refuses any other module-level state that
 *     is not reviewed.
 */

/** Settings of the device, not of a person: kept across sign-outs. Nothing here may hold data.
 * `erp.passkeyOffer` ("1"/"0"): whether the sign-in screen asks this device for a passkey as soon
 * as it opens (kernel/passkeys.ts); it names nobody. */
export const deviceKeys: readonly string[] = ["erp.language", "erp.numerals", "erp.navOpen", "erp.passkeyOffer"];

/**
 * The e-mail of the last sign-in on this device, so a returning person types only the password.
 * Kept when a session simply ends (expiry, closing the browser); forgotten when the person signs
 * out, so the next person on a shared device never sees it.
 */
export const rememberedEmailKey = "erp.lastEmail";

/** Resets of the identity-scoped caches (functions only: the caches themselves hold the data). */
const resets = new Set<() => void>();

/** A map for module-level caching that is emptied whenever the signed-in identity ends. */
export function identityScoped<K, V>(): Map<K, V> {
  const map = new Map<K, V>();
  resets.add(() => map.clear());
  return map;
}

function clearLocalStorage(keepEmail: boolean): void {
  try {
    const keep = new Set(keepEmail ? [...deviceKeys, rememberedEmailKey] : deviceKeys);
    for (let i = localStorage.length - 1; i >= 0; i--) {
      const key = localStorage.key(i);
      if (key !== null && !keep.has(key)) localStorage.removeItem(key);
    }
  } catch {
    // Storage unavailable (private mode): nothing was stored.
  }
}

async function clearDatabases(): Promise<void> {
  try {
    if (typeof indexedDB === "undefined" || typeof indexedDB.databases !== "function") return;
    const databases = await indexedDB.databases();
    await Promise.all(
      databases
        .map((d) => d.name)
        .filter((name): name is string => !!name)
        .map(
          (name) =>
            new Promise<void>((resolve) => {
              const request = indexedDB.deleteDatabase(name);
              request.onsuccess = request.onerror = request.onblocked = () => resolve();
            }),
        ),
    );
  } catch {
    // No IndexedDB here.
  }
}

async function clearCaches(): Promise<void> {
  try {
    if (typeof caches === "undefined") return;
    const names = await caches.keys();
    await Promise.all(names.map((name) => caches.delete(name)));
  } catch {
    // No Cache Storage here (or not a secure context).
  }
}

/** Every path a cookie set by the app could be scoped to: each prefix of each in-app address. */
function cookiePaths(paths: readonly string[]): string[] {
  const result = new Set<string>(["/"]);
  for (const path of paths) {
    const parts = path.split("?")[0]!.split("/").filter(Boolean);
    for (let i = 1; i <= parts.length; i++) {
      const prefix = "/" + parts.slice(0, i).join("/");
      result.add(prefix);
      result.add(prefix + "/");
    }
  }
  return [...result];
}

/** The host and every parent domain a cookie of this page could be set on. */
function cookieDomains(): (string | null)[] {
  const host = window.location.hostname;
  const labels = host.split(".");
  const domains: (string | null)[] = [null];
  if (/^[\d.]+$/.test(host) || host.includes(":")) return domains;
  for (let i = 0; i < labels.length - 1; i++) domains.push(labels.slice(i).join("."));
  if (labels.length === 1) domains.push(host);
  return domains;
}

const expired = "Thu, 01 Jan 1970 00:00:00 GMT";

function cookieNames(): string[] {
  try {
    return document.cookie
      .split(";")
      .map((part) => part.split("=")[0]!.trim())
      .filter((name) => name.length > 0);
  } catch {
    return [];
  }
}

/**
 * Expire every cookie a script of this document can see, for every in-app path and every domain
 * it could have been set with. A browser shows a document only the cookies of the address the
 * document was loaded at (an address changed later with pushState does not count), so a cookie
 * scoped to another path stays invisible here: signing out also asks the browser to drop every
 * cookie of the site (the sign-out answer's Clear-Site-Data header). The session cookie is
 * HttpOnly: scripts never see it, and the server ends it.
 */
export function clearCookies(appPaths: readonly string[] = []): void {
  try {
    const paths = cookiePaths([...appPaths, window.location.pathname]);
    const domains = cookieDomains();
    for (const name of cookieNames()) {
      for (const domain of domains) {
        const scope = domain ? `; domain=${domain}` : "";
        // Without a path: the document's default path, wherever the cookie was set from.
        document.cookie = `${name}=; expires=${expired}${scope}`;
        for (const cookiePath of paths) document.cookie = `${name}=; expires=${expired}; path=${cookiePath}${scope}`;
      }
    }
  } catch {
    // No cookies here.
  }
}

/**
 * Forget everything this browser holds for the identity that is ending. `keepEmail`: keep the
 * remembered sign-in e-mail (a session that ended by itself; the same person usually returns).
 */
export async function forgetIdentity({ keepEmail, appPaths = [] }: { keepEmail: boolean; appPaths?: readonly string[] }): Promise<void> {
  for (const reset of resets) reset();
  clearLocalStorage(keepEmail);
  try {
    sessionStorage.clear();
  } catch {
    // Storage unavailable.
  }
  clearCookies(appPaths);
  // window.name belongs to the tab, not the document: it would greet the next document.
  window.name = "";
  await Promise.all([clearDatabases(), clearCaches()]);
}

/** Replace this document with a fresh one at `target` (the sign-in screen by default). */
export function startOver(target = "/"): void {
  window.location.replace(target);
}

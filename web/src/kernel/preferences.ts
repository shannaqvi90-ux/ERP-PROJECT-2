import { api, ApiError } from "./api";
import { isNumerals, type Language, type Numerals } from "./format";

/**
 * The signed-in user's interface preferences (language, digits) follow the user from device to
 * device through PUT /api/identity/me/preferences. A change applies on screen at once; saving it
 * must survive a reload that happens before the server answers, so:
 *
 *  1. the change is written to this device first, as "pending" for that user;
 *  2. the request is sent with keepalive, so the browser finishes it even if the page unloads;
 *  3. when a session loads, pending changes of that user win over the session's (older) values
 *     and are sent again; they are cleared once the server has them, or when the server refuses
 *     them for good (validation or permission), never on a network failure.
 */
export type Preferences = { language: Language; numerals: Numerals };
export type PreferenceChange = Partial<Preferences>;

const pendingKey = "erp.pendingPreferences";
export const preferencesPath = "/api/identity/me/preferences";
export const preferencesPermission = "identity.profile.update";

type PendingStore = { userId: string; changes: PreferenceChange };

function readStore(): PendingStore | null {
  try {
    const raw = localStorage.getItem(pendingKey);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as PendingStore;
    if (typeof parsed?.userId !== "string" || typeof parsed.changes !== "object" || parsed.changes === null) return null;
    const changes: PreferenceChange = {};
    if (parsed.changes.language === "en" || parsed.changes.language === "ar") changes.language = parsed.changes.language;
    if (isNumerals(parsed.changes.numerals)) changes.numerals = parsed.changes.numerals;
    return { userId: parsed.userId, changes };
  } catch {
    return null;
  }
}

function writeStore(store: PendingStore | null): void {
  try {
    if (!store || Object.keys(store.changes).length === 0) localStorage.removeItem(pendingKey);
    else localStorage.setItem(pendingKey, JSON.stringify(store));
  } catch {
    // Storage unavailable: the keepalive request is the only carrier.
  }
}

/** Changes this device made for the user that the server may not have yet. */
export function pendingFor(userId: string): PreferenceChange | null {
  const store = readStore();
  if (!store || store.userId !== userId || Object.keys(store.changes).length === 0) return null;
  return store.changes;
}

export function rememberPending(userId: string, change: PreferenceChange): void {
  const store = readStore();
  const changes = store && store.userId === userId ? { ...store.changes, ...change } : { ...change };
  writeStore({ userId, changes });
}

/** Forget the pending fields the server now holds (a newer change made meanwhile stays). */
export function settlePending(userId: string, sent: PreferenceChange): void {
  const store = readStore();
  if (!store || store.userId !== userId) return;
  const changes = { ...store.changes };
  for (const key of Object.keys(sent) as (keyof Preferences)[]) {
    if (changes[key] === sent[key]) delete changes[key];
  }
  writeStore({ userId, changes });
}

export type SaveOutcome = "saved" | "refused" | "offline";

/** Send a change for the user; the pending copy keeps it safe until the server confirms. */
export async function savePreferences(userId: string, change: PreferenceChange): Promise<SaveOutcome> {
  rememberPending(userId, change);
  try {
    await api("PUT", preferencesPath, change, { keepalive: true });
    settlePending(userId, change);
    return "saved";
  } catch (error) {
    if (error instanceof ApiError && error.status >= 400 && error.status < 500 && error.status !== 408 && error.status !== 429) {
      // Refused for good (invalid, or the user may not change preferences): retrying cannot help.
      settlePending(userId, change);
      return "refused";
    }
    return "offline";
  }
}

/** The session's preferences with this device's pending changes for the same user on top. */
export function effectivePreferences(userId: string, fromSession: { language: Language; numerals?: Numerals }): {
  preferences: Preferences;
  pending: PreferenceChange | null;
} {
  const pending = pendingFor(userId);
  return {
    preferences: { language: fromSession.language, numerals: fromSession.numerals ?? "latn", ...(pending ?? {}) },
    pending,
  };
}

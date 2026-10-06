import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { installHistoryGuard, type NavigationKind } from "../../kernel/historyGuard";
import { navigate } from "../../kernel/router";
import { mockFetch, render, settle, setInput, submit, type Rendered } from "../../test/render";
import { listReply } from "../../test/lists";
import { App } from "./App";

/**
 * G1 on the client: one browser tab, two tenants. Tenant B uses the shell (every screen of its
 * menu, the palette with every record source, a record, the dialogs) and signs out; tenant A signs
 * in in the same tab and does the same. Nothing of B may reach A: not on screen, not in an input,
 * not in storage.
 *
 * The test DOM cannot replace the document the way the product does when an identity ends
 * (kernel/deviceState.startOver). Here the App is only unmounted and mounted again, so every
 * module's memory (module-level caches, stores, memoised answers) survives from B to A: the harder
 * case. A module cache that is not identity-scoped therefore fails this gate even though the real
 * product would also replace the document. The end-to-end gate (tests/e2e/specs/client-isolation)
 * judges the real browser, its storage and its JavaScript heap.
 *
 * Besides the page and storage, the judges read the carriers that outlive a document in a tab:
 * cookies a script can read, `window.name`, the address and `history.state`. B's history entries
 * stay in the tab after B signs out (the browser keeps them; nothing can take them out), so after A
 * signs in, the test goes Back into each of B's entries, as a new document (Back across documents,
 * or a reload) and inside A's document (popstate), and judges what A then sees.
 */

type Tenant = {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  admin: { id: string; email: string; displayName: string };
  users: { id: string; email: string; displayName: string }[];
  roleName: string;
  /** What the tenant's person types in the users list's search box. */
  searchWord: string;
};

const bravo: Tenant = {
  id: "tb-0b0b",
  code: "gulfsteel",
  nameEn: "Gulf Steel Fabrication LLC",
  nameAr: "الخليج لتصنيع الصلب ذ.م.م",
  admin: { id: "ub-admin-7q2", email: "admin@gulfsteel.example", displayName: "Khalid Bravocanary" },
  users: [
    { id: "ub-admin-7q2", email: "admin@gulfsteel.example", displayName: "Khalid Bravocanary" },
    { id: "ub-ar-7q3", email: "admin.ar@gulfsteel.example", displayName: "Salem Bravocanary" },
    { id: "ub-canary-7q4", email: "canary.zx81@gulfsteel.example", displayName: "Canary Zx81 Bravo" },
  ],
  roleName: "Bravocanary Steel Clerk",
  searchWord: "Bravocanary Salem",
};

const alpha: Tenant = {
  id: "ta-0a0a",
  code: "alnoor",
  nameEn: "Al Noor Trading LLC",
  nameAr: "شركة النور للتجارة ذ.م.م",
  admin: { id: "ua-admin-1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori" },
  users: [
    { id: "ua-admin-1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori" },
    { id: "ua-ar-2", email: "admin.ar@alnoor.example", displayName: "Fatima Al Zaabi" },
    { id: "ua-canary-3", email: "canary.amal@alnoor.example", displayName: "Canary Amal Alpha" },
  ],
  roleName: "Trading Clerk",
  searchWord: "Mansoori",
};

/** Everything that identifies tenant B: none of it may ever appear in tenant A's tab. */
const bravoMarkers = [
  "gulfsteel",
  "Gulf Steel",
  "الخليج لتصنيع",
  "Bravocanary",
  "Zx81",
  bravo.id,
  ...bravo.users.map((u) => u.id),
];

const permissions = [
  "identity.profile.update",
  "identity.roles.read",
  "identity.users.read",
  "identity.signIns.read",
  "tenancy.tenant.read",
];

const menu = [
  { key: "identity.users", labelKey: "identity.menu.users", path: "/identity/users", group: "settings" },
  { key: "identity.roles", labelKey: "identity.menu.roles", path: "/identity/roles", group: "settings" },
  { key: "tenancy.tenant", labelKey: "tenancy.menu.tenant", path: "/tenancy/tenant", group: "settings" },
  { key: "identity.me", labelKey: "identity.menu.me", path: "/identity/me", group: null },
];

function sessionOf(tenant: Tenant) {
  return {
    authenticated: true,
    user: { ...tenant.admin, language: "en", numerals: "latn" },
    tenant: { id: tenant.id, code: tenant.code, nameEn: tenant.nameEn, nameAr: tenant.nameAr },
    permissions,
    menu,
    expiresAt: "2026-10-03T09:00:00Z",
  };
}

function userDetail(tenant: Tenant, id: string) {
  const user = tenant.users.find((u) => u.id === id);
  return user
    ? { ...user, language: "en", isActive: true, roleIds: [], version: 1, lastSignInAt: null, createdAt: "2026-10-01T08:00:00Z", displayNameAr: null }
    : undefined;
}

/** A fake server holding one session cookie: who is signed in decides every answer. */
function serveTwoTenants() {
  let current: Tenant | null = null;
  const byEmail = (email: string) => [alpha, bravo].find((t) => t.admin.email === email) ?? null;
  return mockFetch((method, url, body) => {
    const path = url.split("?")[0]!;
    const query = new URLSearchParams(url.split("?")[1] ?? "");
    if (url === "/api/auth/session") return { status: 200, body: current ? sessionOf(current) : { authenticated: false } };
    if (method === "POST" && url === "/api/auth/sign-in") {
      current = byEmail((body as { email: string }).email);
      return current ? { status: 200, body: sessionOf(current) } : { status: 401, body: { title: "Sign-in failed", code: "auth.failed" } };
    }
    if (method === "POST" && url === "/api/auth/sign-out") {
      current = null;
      return { status: 204 };
    }
    if (!current) return { status: 401, body: { title: "Sign in first", code: "auth.required" } };
    const tenant = current;
    const list = listReply(method, url);
    if (list) return list;
    if (method === "PUT" && path === "/api/identity/me/preferences") return { status: 200, body: { ...tenant.admin, ...(body as object) } };
    const detail = /^\/api\/identity\/users\/([^/]+)(\/[a-z-]+)?$/.exec(path);
    if (detail) {
      const user = userDetail(tenant, detail[1]!);
      if (!user) return { status: 404, body: { title: "Not found", code: "identity.userNotFound" } };
      if (detail[2] === "/access") return { status: 200, body: { roles: [], permissions: [] } };
      if (detail[2] === "/sign-ins") return { status: 200, body: { items: [], blocked: false } };
      return { status: 200, body: user };
    }
    if (path === "/api/identity/users") {
      const search = (query.get("search") ?? "").toLowerCase();
      const items = tenant.users
        .filter((u) => !search || u.displayName.toLowerCase().includes(search) || u.email.includes(search))
        .map((u) => ({ ...u, language: "en", isActive: true, roleIds: [], lastSignInAt: null, createdAt: "2026-10-01T08:00:00Z" }));
      return { status: 200, body: { items, total: items.length } };
    }
    if (path === "/api/identity/roles") {
      return { status: 200, body: { items: [{ id: `${tenant.id}-role`, nameEn: tenant.roleName, nameAr: tenant.roleName, isSystem: false, userCount: 1, permissions: [] }], total: 1 } };
    }
    if (path === "/api/identity/permissions") return { status: 200, body: [] };
    if (path === "/api/tenancy/tenant") return { status: 200, body: { id: tenant.id, code: tenant.code, nameEn: tenant.nameEn, nameAr: tenant.nameAr, status: "active" } };
    return { status: 404, body: { title: "Not found", code: "http.404" } };
  });
}

function press(init: KeyboardEventInit & { code: string }, target: EventTarget = document.activeElement ?? document.body) {
  act(() => {
    target.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init }));
  });
}

async function wait(ms: number) {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, ms));
  });
}

/** Everything the tab shows or holds that a person (or a script) could read. */
function visibleState(): string {
  const inputs = [...document.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>("input, textarea")].map((i) => i.value);
  const storage = (store: Storage) => Array.from({ length: store.length }, (_, i) => `${store.key(i)}=${store.getItem(store.key(i)!)}`);
  const address = decodeURIComponent(window.location.pathname + window.location.search + window.location.hash);
  const cookies = decodeURIComponent(document.cookie);
  return [document.title, document.body.innerHTML, ...inputs, ...storage(localStorage), ...storage(sessionStorage), address, JSON.stringify(window.history.state), cookies, window.name].join("\n");
}

function leaks(): string[] {
  const state = visibleState();
  return bravoMarkers.filter((marker) => state.includes(marker));
}

let view: Rendered | undefined;
let removeGuard: (() => void) | undefined;

/**
 * A new document opens on the given entry (its address and state, as the browser keeps them), the
 * way main.tsx starts: the history guard first, then the App. `kind` is how the browser got there.
 */
async function newDocumentAt(kind: NavigationKind, url: string, state: unknown) {
  view?.unmount();
  removeGuard?.();
  History.prototype.replaceState.call(window.history, state, "", url);
  removeGuard = installHistoryGuard(kind);
  view = await render(<App language="en" />);
  await settle();
}

function expireAllCookies() {
  for (const name of document.cookie.split(";").map((c) => c.split("=")[0]!.trim()).filter(Boolean)) {
    for (const path of ["/", "/identity", "/identity/", "/identity/users", "/tenancy"]) document.cookie = `${name}=; max-age=0; path=${path}`;
  }
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  expireAllCookies();
  window.name = "";
  History.prototype.replaceState.call(window.history, null, "", "/");
  removeGuard = installHistoryGuard("navigate");
  vi.mocked(window.location.replace).mockClear();
});

afterEach(() => {
  view?.unmount();
  view = undefined;
  removeGuard?.();
  removeGuard = undefined;
});

async function signInAs(tenant: Tenant) {
  const container = view!.container;
  const email = container.querySelector<HTMLInputElement>('input[name="email"]')!;
  const password = container.querySelector<HTMLInputElement>('input[name="password"]')!;
  setInput(email, tenant.admin.email);
  setInput(password, "Demo-Pass-2026");
  await submit(container);
  await settle();
  expect(document.querySelector(".workspace-name")?.textContent).toBe(tenant.nameEn);
}

/**
 * The journey both tenants take, step by step; `judge` runs after every step (for A: no B marker
 * anywhere). Returns how many steps ran.
 */
async function journey(tenant: Tenant, judge: (step: string) => void): Promise<number> {
  let steps = 0;
  const step = (name: string) => {
    steps++;
    judge(name);
  };
  // The palette: empty (screens, actions, recent), then every record source with a query that
  // matches records in both tenants ("admin", "canary"), then opening a record from it.
  for (const query of ["", "admin", "canary", "ad"]) {
    press({ code: "KeyK", key: "k", ctrlKey: true });
    await settle();
    const input = document.querySelector<HTMLInputElement>('[role="dialog"].palette input[role="combobox"]')!;
    expect(input).not.toBeNull();
    setInput(input, query);
    await wait(300);
    step(`palette "${query}"`);
    if (query === "canary") {
      const option = [...document.querySelectorAll<HTMLElement>('[role="option"]')].find((o) => o.textContent?.includes(tenant.users[2]!.displayName));
      expect(option, `the palette finds ${tenant.users[2]!.displayName}`).toBeTruthy();
      act(() => option!.click());
      await wait(300);
      step("record opened from the palette");
      press({ code: "Escape", key: "Escape" });
      await settle();
    } else {
      press({ code: "Escape", key: "Escape" }, input);
      await settle();
    }
  }
  // Every screen of the menu; on the users list, the person searches (the list keeps the search
  // text in the address, ?q=).
  for (const item of menu) {
    act(() => navigate(item.path));
    await wait(300);
    step(`screen ${item.path}`);
    if (item.path === "/identity/users") {
      const search = document.querySelector<HTMLInputElement>('main input[type="search"]');
      expect(search, "the users list has a search box").not.toBeNull();
      setInput(search!, tenant.searchWord);
      await wait(600);
      step("users searched");
    }
  }
  // A record by address, and the dialogs.
  act(() => navigate(`/identity/users?open=${tenant.users[1]!.id}`));
  await wait(300);
  step("record by address");
  press({ code: "Slash", key: "?", shiftKey: true }, document.body);
  await settle();
  step("shortcut help");
  press({ code: "Escape", key: "Escape" });
  press({ code: "KeyP", key: "p", altKey: true }, document.body);
  await settle();
  step("preferences");
  press({ code: "Escape", key: "Escape" });
  await settle();
  return steps;
}

async function signOut() {
  const button = document.querySelector<HTMLButtonElement>('button[aria-label="Sign out"]')!;
  await act(async () => button.click());
  await wait(50);
  expect(window.location.replace).toHaveBeenCalledWith("/");
  // The product now loads a fresh document at "/" (location.replace: a navigation). Here the App
  // is mounted again in the same JavaScript memory (see the comment at the top).
  await newDocumentAt("navigate", "/", null);
}

/** The tab's current history entry, as the browser would keep it for Back. */
const entry = () => ({ url: window.location.pathname + window.location.search + window.location.hash, state: window.history.state as unknown });

// The first journey sleeps in fixed steps for about 20 s; the plant self-test runs several copies
// at once beside the other verify stages, so 30 s timed out on a loaded machine (the checks are unchanged).
describe("G1 in the browser: one tab, tenant B then tenant A", { timeout: 120_000 }, () => {
  it("tenant A sees, and the tab keeps, nothing of tenant B after B signs out", async () => {
    serveTwoTenants();
    view = await render(<App language="en" />);
    await settle();

    // B works, and its markers are on screen while it does (the journey really touched B's data).
    // Every address B's journey leaves in the tab's history is kept, with its state.
    await signInAs(bravo);
    const seenByB = new Set<string>();
    const bEntries = new Map<string, unknown>();
    const bSteps = await journey(bravo, () => {
      bravoMarkers.forEach((m) => visibleState().includes(m) && seenByB.add(m));
      const now = entry();
      bEntries.set(now.url, now.state);
    });
    expect(seenByB.has("Zx81")).toBe(true);
    expect(seenByB.has("Bravocanary")).toBe(true);
    expect(seenByB.has("Gulf Steel")).toBe(true);
    // B's own addresses carry B's search text and record ids: the history test below has teeth.
    const bAddresses = [...bEntries.keys()].join("\n");
    expect(bravoMarkers.filter((m) => decodeURIComponent(bAddresses).includes(m)).length, `B's addresses: ${bAddresses}`).toBeGreaterThan(0);
    await signOut();

    // The signed-out tab: an empty sign-in, nothing of B stored.
    expect(leaks(), "after B signed out").toEqual([]);
    expect(document.querySelector<HTMLInputElement>('input[name="email"]')!.value).toBe("");

    // A works in the same tab: after every step, nothing of B anywhere.
    await signInAs(alpha);
    const found: string[] = [];
    const aSteps = await journey(alpha, (name) => {
      for (const marker of leaks()) found.push(`${name}: ${marker}`);
    });
    expect(aSteps).toBe(bSteps);
    expect(found).toEqual([]);

    // A presses Back (or Forward, or reloads) into each of B's entries, which the tab still holds.
    for (const [url, state] of bEntries) {
      for (const kind of ["back_forward", "reload"] as const) {
        await newDocumentAt(kind, url, state);
        await wait(300);
        for (const marker of leaks()) found.push(`${kind} to B's ${url}: ${marker}`);
        expect(document.querySelector(".workspace-name")?.textContent, "A is still signed in").toBe(alpha.nameEn);
      }
      // Inside A's document: the browser restores B's entry and fires popstate.
      History.prototype.replaceState.call(window.history, state, "", url);
      act(() => {
        window.dispatchEvent(new PopStateEvent("popstate", { state }));
      });
      await wait(300);
      for (const marker of leaks()) found.push(`popstate to B's ${url}: ${marker}`);
    }
    expect(found).toEqual([]);

    // A's own entries still work: Back to A's own search keeps it (the guard is not a blunt wipe).
    act(() => navigate(`/identity/users?q=${encodeURIComponent(alpha.searchWord)}`));
    await wait(300);
    const own = entry();
    await newDocumentAt("back_forward", own.url, own.state);
    await wait(300);
    expect(window.location.search).toBe(`?q=${alpha.searchWord}`);
    expect(document.querySelector<HTMLInputElement>('main input[type="search"]')?.value).toBe(alpha.searchWord);
  });

  it("an address someone types or follows is kept, and stamped for the person in the tab", async () => {
    serveTwoTenants();
    await newDocumentAt("navigate", `/identity/users?q=${encodeURIComponent(alpha.searchWord)}`, null);
    await signInAs(alpha);
    await wait(300);
    expect(window.location.search).toBe(`?q=${alpha.searchWord}`);
    expect(document.querySelector<HTMLInputElement>('main input[type="search"]')?.value).toBe(alpha.searchWord);
  });

  it("signing out forgets every cookie the document can read, whatever its path, and the tab's window name", async () => {
    serveTwoTenants();
    view = await render(<App language="en" />);
    await settle();
    await signInAs(bravo);
    act(() => navigate("/identity/users"));
    await wait(300);
    document.cookie = `erp.recentRecord=${encodeURIComponent(bravo.users[2]!.email)}; path=/; max-age=31536000`;
    document.cookie = `erp.listCache=${bravo.users[1]!.id}; path=/identity; max-age=31536000`;
    document.cookie = `erp.lastRole=${encodeURIComponent(bravo.roleName)}; max-age=31536000`;
    window.name = JSON.stringify({ palette: bravo.users.map((u) => u.email) });
    expect(decodeURIComponent(document.cookie)).toContain("gulfsteel");
    // Signed out from the screen the cookies were written on (cookies scoped to paths this
    // document cannot see are dropped by the sign-out answer's Clear-Site-Data, judged end to end).
    await signOut();
    expect(document.cookie).toBe("");
    for (const path of ["/identity", "/identity/users"]) {
      History.prototype.replaceState.call(window.history, null, "", path);
      expect(document.cookie, `cookies seen at ${path}`).toBe("");
    }
    expect(window.name).toBe("");
  });

  it("signing out forgets what the tab stored for the person and keeps only the device's settings", async () => {
    serveTwoTenants();
    view = await render(<App language="en" />);
    await settle();
    await signInAs(bravo);
    localStorage.setItem("erp.someModuleCache", JSON.stringify({ q: "admin", rows: bravo.users }));
    localStorage.setItem("thirdParty.state", bravo.admin.email);
    sessionStorage.setItem("erp.draft", bravo.roleName);
    const bravoEpoch = sessionStorage.getItem("erp.historyEpoch");
    expect(bravoEpoch).toBeTruthy();
    await signOut();
    const keys = Array.from({ length: localStorage.length }, (_, i) => localStorage.key(i)).sort();
    expect(keys.every((k) => ["erp.language", "erp.numerals", "erp.navOpen"].includes(k!))).toBe(true);
    // The fresh document holds only its own new history epoch (kernel/historyGuard), not B's.
    expect(Array.from({ length: sessionStorage.length }, (_, i) => sessionStorage.key(i))).toEqual(["erp.historyEpoch"]);
    expect(sessionStorage.getItem("erp.historyEpoch")).not.toBe(bravoEpoch);
  });

  it("a session that ends by itself (401) starts over and forgets the person, keeping only the e-mail", async () => {
    const calls = serveTwoTenants();
    view = await render(<App language="en" />);
    await settle();
    await signInAs(bravo);
    localStorage.setItem("erp.recent.ub-admin-7q2", JSON.stringify(["/identity/users"]));
    // The server forgets the session (expiry, or signed out everywhere): the next request is 401.
    const signOutServer = calls.length;
    await act(async () => {
      await fetch("/api/auth/sign-out", { method: "POST" });
    });
    expect(calls.length).toBe(signOutServer + 1);
    act(() => navigate("/identity/roles"));
    await wait(300);
    expect(window.location.replace).toHaveBeenCalledWith("/");
    expect(localStorage.getItem("erp.recent.ub-admin-7q2")).toBeNull();
    expect(localStorage.getItem("erp.lastEmail")).toBe(bravo.admin.email);
    expect(document.querySelector(".workspace-name")).toBeNull();
  });

  it("another tab signing in as someone else ends this tab's identity too", async () => {
    serveTwoTenants();
    view = await render(<App language="en" />);
    await settle();
    await signInAs(bravo);
    expect(localStorage.getItem("erp.session")).toBe(`${bravo.id}/${bravo.admin.id}`);
    // The other tab signs in as A: the browser's cookie is now A's, and the shared mark changes.
    const signInAsAlpha = await fetch("/api/auth/sign-in", { method: "POST", body: JSON.stringify({ email: alpha.admin.email, password: "x" }) });
    expect(signInAsAlpha.status).toBe(200);
    await act(async () => {
      window.dispatchEvent(new StorageEvent("storage", { key: "erp.session", oldValue: `${bravo.id}/${bravo.admin.id}`, newValue: `${alpha.id}/${alpha.admin.id}` }));
    });
    await wait(100);
    expect(window.location.replace).toHaveBeenCalledWith("/");
    // This tab forgot B's stored state (the other tab's sign-in remembers its own e-mail).
    expect(localStorage.getItem("erp.session")).toBeNull();
    expect(document.querySelector(".workspace-name")).toBeNull();
  });
});

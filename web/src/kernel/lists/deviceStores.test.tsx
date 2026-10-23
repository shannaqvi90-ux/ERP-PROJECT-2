import { act, useEffect, useState } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockFetch, render, setInput, type Rendered } from "../../test/render";
import type { ListDefinition, ListPage, Row } from "./model";
import type { ListRows } from "./useListRows";

// G1 for the list component, by behaviour (critic p05 round 14, plant L14): a list's rows must
// never be kept on the device. The device here offers every store a page can keep data in beyond
// the document (the origin private file system, IndexedDB, Cache Storage, localStorage and
// sessionStorage, cookies, window.name, history, broadcast channels, workers, service workers,
// beacons, other windows) and records everything put in them. A list is driven through what a
// person does with it (first page, search as you type, sort, filter, group, scrolling deep by
// keyset and by offset, selection, Select all, open a record, widen it, save a view), and:
//
//   1. no store is even opened that holds whole values (file system, databases, caches, channels,
//      workers), and nothing put in any store carries a row's value or id (the address may carry
//      the open record's id: that is the record's own address, guarded per identity by
//      kernel/historyGuard);
//   2. every request stays on the product's own /api;
//   3. the device is poisoned: every store answers every key with another tenant's first page,
//      and the server answers slowly; the list never shows a poisoned row or count, before or
//      after its own page arrives.
//
// The clipboard is the one store a list writes rows into, and only when the person presses Copy:
// the journey checks that it holds nothing before and the rows after (the recorder is not blind).
// The static side of this gate is browserReach.test.ts; the real browser's side is the e2e
// 'keeps no row of a list on the device' in tests/e2e/specs/lists.spec.ts.

const definition: ListDefinition = {
  key: "identity.users",
  labelKey: "identity.users.title",
  endpoint: "/api/identity/users",
  columns: [
    { key: "displayName", labelKey: "identity.users.name", type: "text", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "contains"] },
    { key: "email", labelKey: "identity.users.email", type: "text", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "contains"] },
    { key: "language", labelKey: "identity.users.language", type: "choice", sortable: false, filterable: true, groupable: true, aggregate: false, hidden: false, choices: [{ value: "en", labelKey: "identity.language.en" }, { value: "ar", labelKey: "identity.language.ar" }], operators: ["eq", "in"] },
  ],
  searchFields: ["displayName", "email"],
  defaultSort: "displayName",
  presets: [],
  canShare: true,
  maxTake: 200,
};

/** The signed-in tenant's rows: every name and e-mail carries a marker no store may receive. */
const marker = (i: number) => `qz${i}vk`;
const markerPattern = /qz\d+vk/;
const people: Row[] = Array.from({ length: 260 }, (_, i) => ({
  id: `0a0a0a0a-0000-7000-8000-${String(i).padStart(12, "0")}`,
  displayName: `Member ${i} Al ${marker(i)}`,
  email: `member${i}.${marker(i)}@alnoor.example`,
  language: i % 3 === 0 ? "ar" : "en",
}));
const idPattern = /0a0a0a0a-0000-7000-8000-\d{12}/;

/** Another tenant's first page, as a store that kept one would hand it back. */
const poisonPage: ListPage = {
  items: Array.from({ length: 40 }, (_, i) => ({ id: `0b0b0b0b-0000-7000-8000-${String(i).padStart(12, "0")}`, displayName: `Gulfsteel Poisoned ${i}`, email: `poisoned${i}@gulfsteel.example`, language: "en" })),
  total: 987_654,
  next: null,
  groups: null,
};
const poison = JSON.stringify(poisonPage);
const poisonOnScreen = /Gulfsteel Poisoned|gulfsteel\.example|987,654|987654|٩٨٧/;

type Write = { store: string; payload: string };
type Device = { writes: Write[]; opened: string[]; clipboard: string[]; restore: () => void };

/** Installs a device whose every store records what is put in it and answers every read with `answer`. */
function installDevice(answer: string | null): Device {
  const writes: Write[] = [];
  const opened: string[] = [];
  const clipboard: string[] = [];
  const undo: (() => void)[] = [];
  const text = (value: unknown) => (typeof value === "string" ? value : value instanceof Blob ? "[blob]" : JSON.stringify(value) ?? String(value));
  const define = (target: object, name: string, descriptor: PropertyDescriptor) => {
    const own = Object.getOwnPropertyDescriptor(target, name);
    Object.defineProperty(target, name, { configurable: true, ...descriptor });
    undo.push(() => (own ? Object.defineProperty(target, name, own) : delete (target as Record<string, unknown>)[name]));
  };

  // The origin private file system and the file pickers.
  const fileHandle = (name: string) => ({
    kind: "file",
    name,
    getFile: async () => new File([answer ?? ""], name),
    createWritable: async () => ({
      write: async (data: unknown) => void writes.push({ store: `opfs ${name}`, payload: text(data) }),
      close: async () => {},
      truncate: async () => {},
      seek: async () => {},
    }),
    createSyncAccessHandle: async () => {
      opened.push("opfs sync access");
      return { write: (data: unknown) => writes.push({ store: `opfs ${name}`, payload: text(data) }), flush() {}, close() {}, getSize: () => 0, read: () => 0, truncate() {} };
    },
  });
  const directory = (name: string): Record<string, unknown> => ({
    kind: "directory",
    name,
    getFileHandle: async (file: string, options?: { create?: boolean }) => {
      opened.push(`opfs file ${file}${options?.create ? " (create)" : ""}`);
      if (!options?.create && answer === null) throw new DOMException("not found", "NotFoundError");
      return fileHandle(file);
    },
    getDirectoryHandle: async (dir: string) => {
      opened.push(`opfs directory ${dir}`);
      return directory(dir);
    },
    removeEntry: async () => {},
    resolve: async () => null,
    keys: async function* () {
      if (answer !== null) yield "list-remembered";
    },
    values: async function* () {
      if (answer !== null) yield fileHandle("list-remembered");
    },
    entries: async function* () {
      if (answer !== null) yield ["list-remembered", fileHandle("list-remembered")];
    },
  });
  define(navigator, "storage", {
    value: {
      getDirectory: async () => {
        opened.push("opfs root");
        return directory("");
      },
      persist: async () => {
        opened.push("storage.persist");
        return true;
      },
      persisted: async () => false,
      estimate: async () => ({ usage: 0, quota: 1e9 }),
    },
  });
  for (const picker of ["showSaveFilePicker", "showOpenFilePicker", "showDirectoryPicker"]) {
    define(window, picker, { value: async () => { opened.push(picker); return fileHandle(picker); } });
  }

  // IndexedDB: opening any database is already a use of a store that outlives the document.
  const request = () => {
    const r: Record<string, unknown> = { onsuccess: null, onerror: null, onupgradeneeded: null, onblocked: null, result: undefined, error: new DOMException("refused", "UnknownError") };
    setTimeout(() => (r.onerror as ((e: unknown) => void) | null)?.({ target: r }), 0);
    return r;
  };
  define(window, "indexedDB", {
    value: {
      open: (name: string) => {
        opened.push(`indexedDB ${name}`);
        return request();
      },
      deleteDatabase: () => request(),
      databases: async () => (answer === null ? [] : [{ name: "list-remembered", version: 1 }]),
      cmp: () => 0,
    },
  });

  // Cache Storage.
  const cache = (name: string) => ({
    put: async (key: unknown, response: Response) => void writes.push({ store: `cache ${name} ${text(key)}`, payload: await response.clone().text() }),
    add: async (key: unknown) => void writes.push({ store: `cache ${name}`, payload: text(key) }),
    addAll: async (keys: unknown[]) => void writes.push({ store: `cache ${name}`, payload: text(keys) }),
    match: async () => (answer === null ? undefined : new Response(answer)),
    matchAll: async () => (answer === null ? [] : [new Response(answer)]),
    keys: async () => [],
    delete: async () => true,
  });
  define(window, "caches", {
    value: {
      open: async (name: string) => {
        opened.push(`caches ${name}`);
        return cache(name);
      },
      match: async () => (answer === null ? undefined : new Response(answer)),
      has: async () => answer !== null,
      keys: async () => (answer === null ? [] : ["list-remembered"]),
      delete: async () => true,
    },
  });

  // localStorage and sessionStorage, replaced whole (the test DOM's storages cache their methods,
  // so patching Storage.prototype misses calls): every key the page never set answers with the
  // poisoned page.
  const storage = (name: string) => {
    const items = new Map<string, string>();
    return {
      get length() {
        return items.size;
      },
      key: (index: number) => [...items.keys()][index] ?? null,
      getItem: (key: string) => items.get(key) ?? answer,
      setItem: (key: string, value: string) => {
        writes.push({ store: `${name} ${key}`, payload: `${key}=${value}` });
        items.set(key, String(value));
      },
      removeItem: (key: string) => void items.delete(key),
      clear: () => items.clear(),
    };
  };
  const stores = { localStorage: storage("localStorage"), sessionStorage: storage("sessionStorage") };
  for (const [name, value] of Object.entries(stores)) {
    define(window, name, { get: () => value });
    if ((globalThis as object) !== window) define(globalThis, name, { get: () => value });
  }

  // Cookies and window.name.
  define(document, "cookie", {
    get: () => (answer === null ? "" : `erp.rows=${encodeURIComponent(answer)}`),
    set: (value: string) => void writes.push({ store: "cookie", payload: value }),
  });
  define(window, "name", {
    get: () => answer ?? "",
    set: (value: string) => void writes.push({ store: "window.name", payload: String(value) }),
  });

  // History: entries outlive the document in the tab. Their state and address are recorded.
  for (const method of ["pushState", "replaceState"] as const) {
    const original = window.history[method].bind(window.history);
    const spy = vi.spyOn(window.history, method).mockImplementation((state: unknown, unused: string, url?: string | URL | null) => {
      writes.push({ store: `history.${method} state`, payload: text(state) });
      if (url !== undefined && url !== null) writes.push({ store: `history.${method} address`, payload: String(url) });
      original(state, unused, url);
    });
    undo.push(() => spy.mockRestore());
  }
  define(window.history, "state", { get: () => (answer === null ? null : poisonPage) });

  // Channels, workers, other windows, beacons, service workers.
  define(window, "BroadcastChannel", {
    value: class {
      constructor(name: string) {
        opened.push(`BroadcastChannel ${name}`);
      }
      postMessage(message: unknown) {
        writes.push({ store: "BroadcastChannel", payload: text(message) });
      }
      close() {}
      addEventListener() {}
      removeEventListener() {}
    },
  });
  for (const kind of ["Worker", "SharedWorker"]) {
    define(window, kind, {
      value: class {
        constructor(url: unknown) {
          opened.push(`${kind} ${text(url)}`);
        }
        postMessage(message: unknown) {
          writes.push({ store: kind, payload: text(message) });
        }
        terminate() {}
        addEventListener() {}
        removeEventListener() {}
      },
    });
  }
  define(window, "open", { value: (url?: unknown) => { opened.push(`window.open ${text(url)}`); return null; } });
  define(window, "postMessage", { value: (message: unknown) => void writes.push({ store: "postMessage", payload: text(message) }) });
  define(navigator, "sendBeacon", { value: (url: unknown, data: unknown) => { writes.push({ store: `sendBeacon ${text(url)}`, payload: text(data) }); return true; } });
  define(navigator, "serviceWorker", { value: { register: async (url: unknown) => { opened.push(`serviceWorker ${text(url)}`); return {}; }, controller: null, ready: new Promise(() => {}) } });
  define(navigator, "locks", { value: { request: async (name: string) => { opened.push(`locks ${name}`); }, query: async () => ({}) } });
  define(navigator, "clipboard", {
    value: {
      writeText: async (value: string) => void clipboard.push(value),
      write: async (items: unknown) => void clipboard.push(text(items)),
      readText: async () => answer ?? "",
      read: async () => [],
    },
  });

  return {
    writes,
    opened,
    clipboard,
    restore: () => {
      for (const step of undo.reverse()) step();
    },
  };
}

/** The server: the tenant's rows by keyset or offset, searched, filtered and grouped; the rows `delay` ms late. */
function serve(delay: number) {
  return mockFetch(async (method, url, body) => {
    const parsed = new URL(url, "http://localhost");
    if (delay > 0 && parsed.pathname === "/api/identity/users") await new Promise((resolve) => setTimeout(resolve, delay));
    if (parsed.pathname.endsWith("/definition")) return { status: 200, body: definition };
    if (parsed.pathname.endsWith("/views") && method === "GET") return { status: 200, body: { items: [], total: 0 } };
    if (parsed.pathname.endsWith("/views") && method === "POST") {
      return { status: 201, body: { id: "v1", listKey: "identity.users", name: (body as { name: string }).name, isShared: false, isDefault: false, isMine: true, columns: [], sort: null, filter: null, search: null, groupBy: null, updatedAt: "", version: 1 } };
    }
    if (parsed.pathname !== "/api/identity/users") return { status: 404, body: { title: "not found" } };
    const search = (parsed.searchParams.get("search") ?? "").toLowerCase();
    const filter = parsed.searchParams.get("filter");
    let rows = people.filter((p) => search.split(" ").every((w) => String(p.displayName).toLowerCase().includes(w)));
    if (filter === "language eq 'ar'") rows = rows.filter((p) => p.language === "ar");
    if (parsed.searchParams.get("sort") === "-email") rows = [...rows].reverse();
    const take = Number(parsed.searchParams.get("take") ?? 50);
    const start = Number(parsed.searchParams.get("after") ?? parsed.searchParams.get("skip") ?? 0);
    const groups = parsed.searchParams.get("groupBy")
      ? [
          { key: "ar", count: rows.filter((p) => p.language === "ar").length, totals: null },
          { key: "en", count: rows.filter((p) => p.language === "en").length, totals: null },
        ]
      : null;
    return { status: 200, body: { items: rows.slice(start, start + take), total: rows.length, next: start + take < rows.length ? String(start + take) : null, groups, ranked: false } };
  });
}

async function wait(ms: number) {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, ms));
  });
}
const key = async (target: Element, k: string, init: KeyboardEventInit = {}) =>
  act(async () => {
    target.dispatchEvent(new KeyboardEvent("keydown", { key: k, bubbles: true, ...init }));
  });

type Screen = { view: Rendered; button: (label: string | RegExp, scope?: string) => HTMLButtonElement | undefined };

/** Renders the list (the modules are loaded afresh, so a self-test can plant a different row loader). */
async function showList(): Promise<Screen> {
  const [{ I18nProvider }, { ShortcutProvider }, { ListView }] = await Promise.all([import("../i18n"), import("../shortcuts"), import("./ListView")]);
  const view = await render(
    <I18nProvider initial="en">
      <ShortcutProvider>
        <ListView
          listKey="identity.users"
          titleKey="identity.users.title"
          countKey="identity.users.count"
          searchPlaceholderKey="identity.users.search"
          renderRecord={(id) => <div className="test-form">Form of {id}</div>}
        />
      </ShortcutProvider>
    </I18nProvider>,
  );
  const button = (label: string | RegExp, scope = "button") =>
    [...view.container.querySelectorAll<HTMLButtonElement>(scope)].find((b) => (typeof label === "string" ? b.textContent === label : label.test(b.textContent ?? "")));
  return { view, button };
}

/** Lets the screen work until `done` holds (requests answered, timers run), at most `cap` ms. */
async function until(done: () => boolean, cap = 1500): Promise<boolean> {
  const start = Date.now();
  while (!done()) {
    if (Date.now() - start > cap) return false;
    await wait(10);
  }
  return true;
}

/**
 * What a person does with a list, start to end, before Copy. `look` judges the screen after every
 * step (and while the first page is still on its way). Returns what it saw go wrong.
 */
async function journey(screen: Screen, delay: number, full = true): Promise<string[]> {
  const seen: string[] = [];
  const text = () => screen.view.container.textContent ?? "";
  const look = (step: string) => {
    if (poisonOnScreen.test(text())) seen.push(`${step}: another tenant's row or count is on screen`);
  };
  /** `askedOnly`: `done` only says the request went out; give its answer time to arrive. */
  const step = async (name: string, done: () => boolean, askedOnly = false) => {
    // While a slow answer is on its way the screen must not show what the device handed back.
    if (delay > 0) {
      await wait(delay / 3);
      look(`${name} (waiting for the server)`);
    }
    if (!(await until(done, delay + 5000))) seen.push(`${name}: the screen did not get there (${text().slice(0, 160)})`);
    // Let the answers of this step arrive before the next one.
    if (delay > 0 && askedOnly) await wait(delay + 20);
    look(name);
  };
  const calls = () => (globalThis as { __calls?: { url: string }[] }).__calls ?? [];
  const asked = (pattern: RegExp) => () => calls().some((c) => pattern.test(c.url));

  look("opening");
  await step("first page", () => /260 users/.test(text()));
  if (!full) return seen;
  const grid = () => screen.view.container.querySelector<HTMLElement>("[role=grid]")!;

  const search = screen.view.container.querySelector<HTMLInputElement>("input[type=search]")!;
  setInput(search, "member 1");
  look("typing");
  await step("searched", () => /134 users/.test(text()));
  await key(search, "Escape");
  await step("search cleared", () => /260 users/.test(text()));

  const sortEmail = [...screen.view.container.querySelectorAll<HTMLButtonElement>(".list-sort")].find((b) => b.textContent?.startsWith("E-mail"));
  await act(async () => sortEmail?.click());
  await step("sorted", asked(/sort=email/), true);
  await act(async () => sortEmail?.click());
  await step("sorted back", asked(/sort=-email/), true);

  const menu = () => screen.view.container.querySelector<HTMLButtonElement>("[aria-label='Options for the column Language']");
  await act(async () => menu()?.click());
  await act(async () => screen.button("Filter…", "[role=menuitem]")?.click());
  const arabic = [...screen.view.container.querySelectorAll<HTMLLabelElement>(".list-filter label")].find((l) => l.textContent === "Arabic");
  await act(async () => arabic?.querySelector("input")?.click());
  await act(async () => screen.view.container.querySelector<HTMLFormElement>(".list-filter form")?.requestSubmit());
  await step("filtered", () => /87 users/.test(text()));
  await act(async () => menu()?.click());
  await act(async () => screen.button("Group by this column", "[role=menuitem]")?.click());
  await step("grouped", () => asked(/groupBy=language/)() && /Arabic\s*87 rows/.test(text()));
  grid().focus();
  const before = calls().length;
  await key(grid(), "Enter");
  await step("group opened", () => calls().slice(before).some((c) => c.url.startsWith("/api/identity/users?") && !c.url.includes("groupBy")));

  // Every row again (a fresh screen), then deep: End (by offset), Home, and down by keyset.
  window.history.replaceState(null, "", "/identity/users");
  screen.view.unmount();
  Object.assign(screen, await showList());
  await step("reopened", () => /260 users/.test(text()) && grid() !== null);
  grid().focus();
  await key(grid(), "End");
  await step("end of the list", asked(/skip=200/), true);
  await key(grid(), "Home");
  for (let i = 0; i < 6; i++) await key(grid(), "PageDown");
  await step("paged down", asked(/after=100/), true);
  await key(grid(), " ");
  await key(grid(), "ArrowDown", { shiftKey: true });
  await key(grid(), "a", { ctrlKey: true });
  await key(grid(), "a", { ctrlKey: true });
  await step("all selected", () => /All 260 matching rows selected|260 selected/.test(text()));
  await key(grid(), "Escape");
  await key(grid(), "ArrowDown");
  await key(grid(), "Enter");
  await step("record open", () => screen.view.container.querySelector("[role=separator]") !== null);
  const splitter = screen.view.container.querySelector<HTMLElement>("[role=separator]");
  if (splitter) {
    await key(splitter, "ArrowLeft");
    await key(splitter, "End");
  }
  await key(document.body, "Escape");

  await act(async () => screen.button(/^View:/)?.click());
  await act(async () => screen.button("Save as a new view…", "[role=menuitem]")?.click());
  const name = screen.view.container.querySelector<HTMLInputElement>(".list-save input:not([type=checkbox])");
  if (name) {
    setInput(name, "Arabic speakers");
    await act(async () => screen.view.container.querySelector<HTMLFormElement>(".list-save form")?.requestSubmit());
    await step("view saved", asked(/\/views$/), true);
  } else seen.push("views: no save dialog");
  return seen;
}

/** What the device and the network received that they must not have. */
function judgeDevice(device: Device, calls: { url: string }[]): string[] {
  const problems: string[] = [];
  for (const store of device.opened) problems.push(`opened ${store}: a store that outlives the document`);
  for (const { store, payload } of device.writes) {
    if (markerPattern.test(payload)) problems.push(`${store} received a row's value: ${payload.slice(0, 120)}`);
    const address = store.endsWith(" address");
    const ids = payload.match(new RegExp(idPattern, "g")) ?? [];
    if (ids.length > (address ? 1 : 0)) problems.push(`${store} received row ids: ${payload.slice(0, 120)}`);
  }
  for (const call of calls) if (!call.url.startsWith("/api/")) problems.push(`a request left the product's /api: ${call.url}`);
  return problems;
}

let device: Device | undefined;
let screen: Screen | undefined;

beforeEach(() => {
  vi.resetModules();
  window.history.replaceState(null, "", "/identity/users");
  window.localStorage.clear();
  window.sessionStorage.clear();
});

afterEach(() => {
  screen?.view.unmount();
  screen = undefined;
  device?.restore();
  device = undefined;
  vi.doUnmock("./useListRows");
  delete (globalThis as { __calls?: unknown }).__calls;
  window.localStorage.clear();
});

/** One run: the journey (the whole one, or only the first page), then Copy. */
async function run(answer: string | null, delay: number, full = true): Promise<{ seen: string[]; problems: string[]; device: Device }> {
  device = installDevice(answer);
  const calls = serve(delay);
  (globalThis as { __calls?: unknown }).__calls = calls;
  // Every change of the page is judged as it happens, not only after each step: a kept page shown
  // for a moment while the server is slow is seen however the steps are timed.
  let sighted = 0;
  const watcher = new MutationObserver(() => {
    if (poisonOnScreen.test(document.body.textContent ?? "")) sighted++;
  });
  watcher.observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true });
  screen = await showList();
  const seen = await journey(screen, delay, full);
  await wait(0);
  watcher.disconnect();
  if (sighted > 0) seen.push(`another tenant's row or count is on screen (${sighted} changes of the page showed it)`);
  const problems = judgeDevice(device, calls);
  if (!full) return { seen, problems, device };
  if (device.clipboard.length > 0) problems.push("the clipboard received rows before the person pressed Copy");
  screen.view.container.querySelector<HTMLElement>("[role=grid]")!.focus();
  await key(screen.view.container.querySelector<HTMLElement>("[role=grid]")!, " ");
  await act(async () => screen!.button("Copy", ".list-selectionbar button")?.click());
  await until(() => device!.clipboard.length > 0, 500);
  if (!device.clipboard.some((c) => markerPattern.test(c))) seen.push("Copy did not reach the recorded clipboard (the recorder is blind)");
  return { seen, problems, device };
}

// A journey of some twenty steps through a real list: its own budget, like the shell's G1 journey.
describe("a list keeps no row on the device (G1, device stores)", { timeout: 20_000 }, () => {
  it("puts no row value or id in any store that outlives the document, and asks only the product's /api", async () => {
    const { seen, problems, device: used } = await run(null, 0);
    expect(seen, seen.join("\n")).toEqual([]);
    expect(problems, problems.join("\n")).toEqual([]);
    // Not blind: the width choice and the address did reach the recorder.
    expect(used.writes.some((w) => w.store === "localStorage erp.lists.recordShare.identity.users")).toBe(true);
    expect(used.writes.some((w) => w.store === "history.replaceState address" && idPattern.test(w.payload))).toBe(true);
  });

  it("never shows another tenant's rows that a poisoned device hands back for every key, while its own page is slow", async () => {
    const { seen, problems } = await run(poison, 60);
    expect(seen, seen.join("\n")).toEqual([]);
    expect(problems, problems.join("\n")).toEqual([]);
  });

  // Self-test: the critic's plant L14 (the first page of each query kept in the origin private file
  // system, keyed by endpoint and query only, and shown at once while the fresh page loads), as a
  // row loader wrapped around the real one.
  const plantL14 = () =>
    vi.doMock("./useListRows", async (importOriginal) => {
      const real = await importOriginal<typeof import("./useListRows")>();
      return {
        ...real,
        useListRows(endpoint: string | null, query: URLSearchParams | null, groupBy: string | null): ListRows {
          const rows = real.useListRows(endpoint, query, groupBy);
          const file = `list-${encodeURIComponent(`${endpoint}?${query?.toString()}|${groupBy ?? ""}`).slice(0, 180)}`;
          const [kept, setKept] = useState<ListPage | null>(null);
          useEffect(() => {
            let live = true;
            void (async () => {
              try {
                const dir = await navigator.storage.getDirectory();
                const page = JSON.parse(await (await (await dir.getFileHandle(file)).getFile()).text()) as ListPage;
                if (live) setKept(page);
              } catch {
                // nothing kept
              }
            })();
            return () => void (live = false);
          }, [file]);
          useEffect(() => {
            if (rows.total === null) return;
            void (async () => {
              try {
                const dir = await navigator.storage.getDirectory();
                const writable = await (await dir.getFileHandle(file, { create: true })).createWritable();
                await writable.write(JSON.stringify({ items: rows.loadedRows().slice(0, 100), total: rows.total, next: null, groups: rows.groups }));
                await writable.close();
              } catch {
                // no file system
              }
            })();
          }, [rows.version, file]);
          if (rows.loadedKey === null && kept) return { ...rows, total: kept.total, loadedKey: `${endpoint}?${query?.toString()}|${groupBy ?? ""}`, rowAt: (i) => kept.items[i] };
          return rows;
        },
      };
    });

  it("self-test: catches plant L14 on the device (the first page written to the origin private file system)", async () => {
    plantL14();
    const { problems } = await run(null, 0, false);
    await until(() => device!.writes.some((w) => w.store.startsWith("opfs")), 500);
    const found = judgeDevice(device!, []);
    expect([...problems, ...found].some((p) => /opened opfs/.test(p)), problems.join("\n")).toBe(true);
    expect(found.some((p) => /^opfs .* received a row's value/.test(p)), found.join("\n")).toBe(true);
  });

  it("self-test: catches plant L14 on screen (another tenant's kept page shown while the server is slow)", async () => {
    plantL14();
    // The server answers 400 ms late: the kept page has time to show, however busy the machine.
    const { seen } = await run(poison, 400, false);
    expect(seen.some((s) => /another tenant's row or count is on screen/.test(s)), seen.join("\n")).toBe(true);
  });

  it("self-test: catches a list that keeps its rows in sessionStorage, a cookie or window.name", async () => {
    vi.doMock("./useListRows", async (importOriginal) => {
      const real = await importOriginal<typeof import("./useListRows")>();
      return {
        ...real,
        useListRows(endpoint: string | null, query: URLSearchParams | null, groupBy: string | null): ListRows {
          const rows = real.useListRows(endpoint, query, groupBy);
          useEffect(() => {
            const first = rows.rowAt(0);
            if (!first) return;
            window.sessionStorage.setItem("erp.lists.last", JSON.stringify(first));
            document.cookie = `erp.last=${first.id}`;
            window.name = String(first.email);
          }, [rows.version]);
          return rows;
        },
      };
    });
    const { problems } = await run(null, 0);
    expect(problems.some((p) => p.startsWith("sessionStorage erp.lists.last received")), problems.join("\n")).toBe(true);
    expect(problems.some((p) => p.startsWith("cookie received row ids")), problems.join("\n")).toBe(true);
    expect(problems.some((p) => p.startsWith("window.name received a row's value")), problems.join("\n")).toBe(true);
  });
});

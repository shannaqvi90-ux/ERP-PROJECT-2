import { expect, request as apiRequest, test, type APIRequestContext, type Page } from "@playwright/test";
import { freshStart, password, signIn } from "./demo";

/**
 * G1 in the real browser: one tab, two tenants, the way an outsourced bookkeeper serving several
 * client companies works. Tenant B (gulfsteel) uses every screen of its menu, the command palette
 * with every record source, a record and the dialogs, with marker data of its own, and signs out.
 * Tenant A (alnoor) signs in in the same tab and does the same. After every step of A, and on the
 * signed-out screen between them, the judges look for B's markers in:
 *
 *  - the page: its HTML, every input's value and the title;
 *  - the tab's storage: localStorage, sessionStorage, every IndexedDB database and Cache Storage;
 *  - the tab's JavaScript heap (a V8 heap snapshot), where a module-level cache would keep them;
 *  - and every API response the tab received must carry Cache-Control: no-store, so the browser's
 *    HTTP cache keeps nothing for the next person either.
 *
 * Markers are never sent into the page by the test (comparisons happen here, in Node), so the heap
 * holds them only if the product does. The last test is the judges' self-test: each carrier,
 * planted on purpose, must be found.
 */

const bravoAdmin = "admin@gulfsteel.example";
const alphaAdmin = "admin@alnoor.example";
const unique = () => Math.random().toString(36).slice(2, 8);

type Carrier = "page" | "input" | "title" | "localStorage" | "sessionStorage" | "indexedDB" | "cacheStorage" | "heap";
type Finding = { step: string; carrier: Carrier; marker: string };

/** Signs in through the API (a separate client, not the tab) and returns it with its session. */
async function apiAs(email: string) {
  const baseURL = test.info().project.use.baseURL;
  const context = await apiRequest.newContext({ baseURL, extraHTTPHeaders: { "X-Erp-Request": "1" } });
  const signedIn = await context.post("/api/auth/sign-in", { data: { email, password } });
  expect(signedIn.ok(), `API sign-in as ${email}: ${signedIn.status()}`).toBe(true);
  const session = (await signedIn.json()) as { user: { id: string }; tenant: { id: string; code: string; nameEn: string; nameAr: string } };
  return { context, session };
}

/** A marker user in the tenant, found by the palette's "canary" query. */
async function createMarkerUser(api: APIRequestContext, domain: string, token: string, label: string) {
  const created = await api.post("/api/identity/users", {
    data: { email: `canary.${token}@${domain}`, displayName: `Canary ${token} ${label}`, language: "en", roleIds: [] },
  });
  expect(created.ok(), `marker user in ${domain}: ${created.status()} ${await created.text()}`).toBe(true);
  return (await created.json()) as { id: string; email: string; displayName: string };
}

/** Everything the tab holds, read in the page and compared here. */
async function tabState(page: Page): Promise<Record<Exclude<Carrier, "heap">, string>> {
  const html = await page.content();
  const state = await page.evaluate(async () => {
    const dump = (store: Storage) => Array.from({ length: store.length }, (_, i) => `${store.key(i)}=${store.getItem(store.key(i)!)}`).join("\n");
    const inputs = [...document.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>("input, textarea")].map((e) => e.value).join("\n");
    let indexed = "";
    for (const info of (await indexedDB.databases?.()) ?? []) {
      if (!info.name) continue;
      indexed += `db ${info.name}\n`;
      const db = await new Promise<IDBDatabase>((resolve, reject) => {
        const open = indexedDB.open(info.name!);
        open.onsuccess = () => resolve(open.result);
        open.onerror = () => reject(open.error);
      });
      for (const name of [...db.objectStoreNames]) {
        const all = await new Promise<unknown[]>((resolve) => {
          const r = db.transaction(name, "readonly").objectStore(name).getAll();
          r.onsuccess = () => resolve(r.result);
          r.onerror = () => resolve([]);
        });
        indexed += `${name} ${JSON.stringify(all)}\n`;
      }
      db.close();
    }
    let cached = "";
    if (typeof caches !== "undefined") {
      for (const name of await caches.keys()) {
        const cache = await caches.open(name);
        for (const req of await cache.keys()) cached += `${name} ${req.url} ${await (await cache.match(req))!.text()}\n`;
      }
    }
    return { input: inputs, title: document.title, localStorage: dump(localStorage), sessionStorage: dump(sessionStorage), indexedDB: indexed, cacheStorage: cached };
  });
  return { page: html, ...state };
}

/** The strings of the tab's JavaScript heap, after a full garbage collection. */
async function heapStrings(page: Page): Promise<string> {
  const cdp = await page.context().newCDPSession(page);
  const chunks: string[] = [];
  cdp.on("HeapProfiler.addHeapSnapshotChunk", (event) => chunks.push(event.chunk));
  await cdp.send("HeapProfiler.enable");
  await cdp.send("HeapProfiler.collectGarbage");
  await cdp.send("HeapProfiler.takeHeapSnapshot", { reportProgress: false });
  await cdp.send("HeapProfiler.disable");
  await cdp.detach();
  const snapshot = JSON.parse(chunks.join("")) as { strings: string[] };
  return snapshot.strings.join("\n");
}

function judge(step: string, state: Partial<Record<Carrier, string>>, markers: string[]): Finding[] {
  const findings: Finding[] = [];
  for (const [carrier, text] of Object.entries(state) as [Carrier, string][]) {
    for (const marker of markers) if (text.includes(marker)) findings.push({ step, carrier, marker });
  }
  return findings;
}

async function settled(page: Page) {
  await page.waitForLoadState("networkidle").catch(() => undefined);
}

/**
 * The journey both tenants take in the tab, step by step; `after` runs after every step. Returns
 * the steps taken (both tenants must take the same ones).
 */
async function journey(page: Page, token: string, after: (step: string) => Promise<void>): Promise<string[]> {
  const steps: string[] = [];
  const step = async (name: string) => {
    await settled(page);
    steps.push(name);
    await after(name);
  };
  await expect(page.getByRole("navigation", { name: "Main navigation" })).toBeVisible();
  await step("signed in");

  // Every screen of the menu.
  const paths = await page.locator("nav.navpane a").evaluateAll((links) => links.map((a) => a.getAttribute("href")!));
  expect(paths.length).toBeGreaterThanOrEqual(4);
  for (const path of paths) {
    await page.locator(`nav.navpane a[href="${path}"]`).first().click();
    await expect(page.locator("main h1").first()).toBeVisible();
    await step(`screen ${path}`);
  }

  // The palette: empty (recent screens, screens, actions), then every record source with queries
  // that match records in both tenants, then a record opened from it.
  const palette = page.locator('[role="dialog"].palette');
  await page.keyboard.press("Control+k");
  await expect(palette).toBeVisible();
  await step("palette, empty");
  await page.keyboard.type("admin");
  await expect(palette.locator('[role="option"]', { hasText: "@" }).first()).toBeVisible();
  await step('palette "admin"');
  await page.keyboard.press("Escape");
  await page.keyboard.press("Control+k");
  await page.keyboard.type("canary");
  // Judged as typed, before the tenant's own answer is awaited (a cached answer shows at once).
  await step('palette "canary", typed');
  const own = palette.locator('[role="option"]', { hasText: token });
  await expect(own).toBeVisible();
  await step('palette "canary"');
  await own.click();
  await expect(page.locator("aside h2")).toBeVisible();
  await step("record opened from the palette");
  await page.keyboard.press("Escape");
  await expect(page.locator("aside h2")).toHaveCount(0);

  // The dialogs, each closed before the next.
  const dialog = page.getByRole("dialog");
  await page.locator("main h1").first().click();
  await page.keyboard.press("Shift+Slash");
  await expect(dialog).toHaveCount(1);
  await step("shortcut help");
  await page.keyboard.press("Escape");
  await expect(dialog).toHaveCount(0);
  await page.keyboard.press("Alt+p");
  await expect(dialog).toHaveCount(1);
  await step("preferences");
  await page.keyboard.press("Escape");
  await expect(dialog).toHaveCount(0);
  return steps;
}

test.describe("G1 in the browser: one tab, tenant B then tenant A", () => {
  test("after B signs out, the tab holds nothing of B, and A sees nothing of B on any screen, in storage or in memory", async ({ page }) => {
    test.setTimeout(240_000);
    const bravo = await apiAs(bravoAdmin);
    const alpha = await apiAs(alphaAdmin);
    const bravoToken = `zb${unique()}`;
    const alphaToken = `za${unique()}`;
    const bravoMarker = await createMarkerUser(bravo.context, "gulfsteel.example", bravoToken, "Bravo");
    const alphaMarker = await createMarkerUser(alpha.context, "alnoor.example", alphaToken, "Alpha");
    const markers = [bravoToken, "gulfsteel", "Gulf Steel", "الخليج لتصنيع", bravo.session.tenant.id, bravo.session.user.id, bravoMarker.id];
    const uncached: string[] = [];
    page.on("response", (response) => {
      if (new URL(response.url()).pathname.startsWith("/api/") && !/no-store/.test(response.headers()["cache-control"] ?? ""))
        uncached.push(`${response.status()} ${response.url()}`);
    });

    try {
      // B works in the tab; its markers are on screen while it does (the journey touched B's data).
      await freshStart(page, "en");
      await signIn(page, bravoAdmin);
      const seenByB = new Set<string>();
      const bravoSteps = await journey(page, bravoToken, async () => {
        for (const finding of judge("B", await tabState(page), markers)) seenByB.add(finding.marker);
      });
      expect([...seenByB]).toEqual(expect.arrayContaining([bravoToken, "Gulf Steel"]));
      expect(await heapStrings(page)).toContain(bravoToken);

      // B signs out: a fresh, empty sign-in screen in the same tab.
      await page.getByRole("button", { name: "Sign out" }).click();
      await expect(page.locator('input[name="email"]')).toBeFocused();
      await expect(page.locator('input[name="email"]')).toHaveValue("");
      const findings: Finding[] = [];
      findings.push(...judge("signed out", await tabState(page), markers));
      findings.push(...judge("signed out", { heap: await heapStrings(page) }, markers));

      // A works in the same tab: after every step nothing of B, anywhere.
      await signIn(page, alphaAdmin);
      // A leak can also break A's journey (B's answer shown instead of A's): the leak is reported
      // first, then the journey's own failure.
      let broken: unknown = null;
      const alphaSteps = await journey(page, alphaToken, async (step) => {
        findings.push(...judge(step, await tabState(page), markers));
      }).catch((error: unknown) => {
        broken = error;
        return [] as string[];
      });
      findings.push(...judge("A, end of journey", { heap: await heapStrings(page) }, markers));
      expect(findings, "tenant B's markers found in tenant A's tab").toEqual([]);
      if (broken) throw broken;
      expect(alphaSteps).toEqual(bravoSteps);
      expect(uncached, "API responses without Cache-Control: no-store").toEqual([]);
      console.log(`client isolation: ${alphaSteps.length} steps per tenant judged on ${markers.length} markers in 8 carriers; ${alphaMarker.email} (A) and ${bravoMarker.email} (B)`);
    } finally {
      await bravo.context.delete(`/api/identity/users/${bravoMarker.id}`).catch(() => undefined);
      await alpha.context.delete(`/api/identity/users/${alphaMarker.id}`).catch(() => undefined);
      await bravo.context.dispose();
      await alpha.context.dispose();
    }
  });

  test("after signing out, Back does not bring the signed-out person's screens back", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, bravoAdmin);
    await page.locator('nav.navpane a[href="/identity/users"]').first().click();
    await expect(page.locator("table tbody tr").first()).toBeVisible();
    await page.locator('nav.navpane a[href="/identity/roles"]').first().click();
    await expect(page.locator("table tbody tr").first()).toBeVisible();
    await page.getByRole("button", { name: "Sign out" }).click();
    await expect(page.locator('input[name="email"]')).toBeFocused();
    for (let i = 0; i < 2; i++) {
      await page.goBack();
      await expect(page.locator('input[name="email"]')).toBeFocused();
      await settled(page);
      const state = await tabState(page);
      expect(judge(`Back ${i + 1}`, { page: state.page, input: state.input, title: state.title }, ["gulfsteel", "Gulf Steel", "الخليج لتصنيع"])).toEqual([]);
    }
  });

  test("signing out in one tab ends the session in every other tab of the browser at once", async ({ page, context }) => {
    await freshStart(page, "en");
    await signIn(page, bravoAdmin);
    await page.locator('nav.navpane a[href="/identity/users"]').first().click();
    await expect(page.locator("table tbody tr").first()).toBeVisible();
    const other = await context.newPage();
    await other.goto("/");
    await expect(other.getByRole("button", { name: "Sign out" })).toBeVisible();
    await other.getByRole("button", { name: "Sign out" }).click();
    await expect(other.locator('input[name="email"]')).toBeFocused();
    // The first tab did nothing, yet it shows the sign-in screen and nothing of B.
    await expect(page.locator('input[name="email"]')).toBeVisible();
    await settled(page);
    const state = await tabState(page);
    expect(judge("other tab signed out", { page: state.page, input: state.input, title: state.title }, ["gulfsteel", "Gulf Steel", "الخليج لتصنيع"])).toEqual([]);
    await other.close();
  });

  test("the judges' self-test: a marker planted in each carrier of the tab is found", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, alphaAdmin);
    await expect(page.getByRole("navigation", { name: "Main navigation" })).toBeVisible();
    const token = `zp${unique()}`;
    // Each carrier gets its own marker, so each judge is shown to see its own carrier.
    const marker = (carrier: Carrier) => `${token}-${carrier}`;
    await page.evaluate(async (m) => {
      const node = document.createElement("span");
      node.textContent = m.page;
      document.querySelector("main")!.append(node);
      const input = document.createElement("input");
      input.value = m.input;
      document.querySelector("main")!.append(input);
      document.title = m.title;
      localStorage.setItem("erp.plantedCache", m.localStorage);
      sessionStorage.setItem("erp.plantedDraft", m.sessionStorage);
      await new Promise<void>((resolve, reject) => {
        const open = indexedDB.open("erp-planted", 1);
        open.onupgradeneeded = () => open.result.createObjectStore("rows", { autoIncrement: true });
        open.onerror = () => reject(open.error);
        open.onsuccess = () => {
          const tx = open.result.transaction("rows", "readwrite");
          tx.objectStore("rows").add({ name: m.indexedDB });
          tx.oncomplete = () => {
            open.result.close();
            resolve();
          };
        };
      });
      if (typeof caches !== "undefined") await (await caches.open("erp-planted")).put("/planted", new Response(m.cacheStorage));
      // A module-level cache: only in memory.
      (window as unknown as { plantedCache: Map<string, string> }).plantedCache = new Map([["q", m.heap.split("").join("")]]);
    }, Object.fromEntries((["page", "input", "title", "localStorage", "sessionStorage", "indexedDB", "cacheStorage", "heap"] as Carrier[]).map((c) => [c, marker(c)])));

    const state = await tabState(page);
    const heap = await heapStrings(page);
    const expected: Carrier[] = ["page", "input", "title", "localStorage", "sessionStorage", "indexedDB"];
    // Cache Storage exists only on secure origins (https or localhost).
    const secure = await page.evaluate(() => window.isSecureContext && typeof caches !== "undefined");
    if (secure) expected.push("cacheStorage");
    for (const carrier of expected) {
      expect(judge("self-test", { [carrier]: state[carrier as Exclude<Carrier, "heap">] }, [marker(carrier)]), `the ${carrier} judge finds its plant`).toHaveLength(1);
    }
    expect(judge("self-test", { heap }, [marker("heap")]), "the heap judge finds a module-level cache").toHaveLength(1);

    // And signing out clears every stored plant before the next person.
    await page.getByRole("button", { name: "Sign out" }).click();
    await expect(page.locator('input[name="email"]')).toBeFocused();
    const after = await tabState(page);
    const stored = judge("after sign-out", { localStorage: after.localStorage, sessionStorage: after.sessionStorage, indexedDB: after.indexedDB, cacheStorage: after.cacheStorage }, expected.map(marker));
    expect(stored).toEqual([]);
    expect(judge("after sign-out", { heap: await heapStrings(page) }, [marker("heap"), marker("page")])).toEqual([]);
  });
});

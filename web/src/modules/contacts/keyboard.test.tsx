import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { recordInAddress } from "../../kernel/router";
import { mockFetch, render, type Rendered } from "../../test/render";
import { type Call, settleUntilQuiet, sweepKeys, sweepTimeLimit } from "../../test/keySweep";
import { App } from "../shell/App";

// G2 on screen for the contacts screen, by keyboard: the whole app is shown to a user who may read
// contacts and change nothing; with a contact open, and on the list alone with every row chosen,
// every key is pressed alone and with every modifier, whatever a key opens is accepted, and every
// control the keyboard reaches is activated: no request other than a read leaves (the user's own
// list views excepted), and no key opens a new contact.

let view: Rendered | undefined;
let calls: Call[] = [];

beforeEach(() => localStorage.clear());
afterEach(() => closeView());

function closeView() {
  view?.unmount();
  view = undefined;
}

const reads = ["contacts.contacts.read"];
const organisationId = "0190a000-0000-7000-8000-0000000000c1";

const session = (permissions: string[]) => ({
  authenticated: true,
  user: { id: "u1", email: "viewer@alnoor.example", displayName: "Huda Viewer", language: "en" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: [{ key: "contacts.contacts", labelKey: "contacts.menu.contacts", path: "/contacts/contacts", group: null }],
  expiresAt: null,
});

const column = (key: string, labelKey: string) => ({
  key, labelKey, type: "text", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "contains"],
});
const definition = {
  key: "contacts.contacts", labelKey: "contacts.contacts.title", endpoint: "/api/contacts/contacts",
  columns: [column("nameEn", "contacts.contact.name")], searchFields: ["nameEn"], defaultSort: "nameEn", presets: [], canShare: false, maxTake: 200,
};
const organisation = {
  id: organisationId, kind: "organisation", reference: "C-DEMO-1", nameEn: "Falcon Building Materials LLC", nameAr: "فالكون لمواد البناء ذ.م.م",
  organisationId: null, organisationName: null, organisationNameAr: null, jobTitle: null, email: "accounts@falcon.example", phone: "+971 4 555 0100",
  mobile: null, website: null, addressLine1: null, addressLine2: null, area: null, city: "Dubai", emirate: "dubai", poBox: null, country: "AE",
  addressAr: null, tags: [], tradeLicenceNumber: null, taxRegistrationNumber: null, defaultCurrency: "AED", creditLimit: null, notes: null,
  isActive: true, peopleCount: 0, createdAt: "2026-10-03T00:00:00Z", updatedAt: "2026-10-03T00:00:00Z", version: 7,
};

async function open(path: string, permissions: string[]): Promise<Call[]> {
  closeView();
  window.history.replaceState(null, "", path);
  calls = mockFetch((method, url) => {
    const p = new URL(url, "http://localhost").pathname;
    if (method !== "GET") return { status: 403, body: { code: "forbidden", title: "Not allowed." } };
    if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
    if (p === "/api/lists/contacts.contacts/definition") return { status: 200, body: definition };
    if (p.endsWith("/views")) return { status: 200, body: { items: [] } };
    if (p === "/api/contacts/contacts/defaults") return { status: 200, body: { country: "AE", defaultCurrency: "AED", baseCurrency: "AED" } };
    if (p === "/api/contacts/contacts/duplicates") return { status: 200, body: [] };
    if (p === "/api/contacts/contacts") return { status: 200, body: { items: [organisation], total: 1, next: null } };
    if (p === `/api/contacts/contacts/${organisationId}`) return { status: 200, body: organisation };
    return { status: 404, body: {} };
  });
  view = await render(<App language="en" />);
  await settleUntilQuiet(() => calls, () => view?.container.querySelector("main h1, main h2, main [role=grid]") != null);
  return calls;
}

const here = () => window.location.pathname + window.location.search;

const grid = () => view?.container.querySelector<HTMLElement>("main [role=grid]") ?? null;

/** Every row of the list chosen, as Ctrl+A on the list does. */
const allRowsChosen = {
  name: "every row chosen",
  enter: () => {
    const g = grid();
    if (!g || view!.container.querySelector(".list-selectionbar")) return;
    g.focus();
    act(() => {
      g.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ctrlKey: true, key: "a", code: "KeyA" }));
    });
  },
};

async function sweep(path: string, permissions = reads, exhaustive = false) {
  await open(path, permissions);
  expect(view!.container.querySelector("main"), `${path} shows its screen`).not.toBeNull();
  const list = !path.includes("open=") && grid() !== null;
  return sweepKeys({
    exhaustive,
    calls,
    shown: () => here() === path && view?.container.querySelector("main") !== null,
    reopen: () => open(path, permissions),
    targets: [() => view!.container.querySelector<HTMLElement>("main h2[tabindex], main h1, main [role=grid], main table") ?? null],
    states: list ? [allRowsChosen] : [],
    forbidden: () => (recordInAddress() === "new" ? "a new record offered to a user who may not create one" : null),
  });
}

describe("the contacts screen by keyboard, for a user who may read contacts and change nothing", () => {
  for (const path of [`/contacts/contacts?open=${organisationId}`, "/contacts/contacts"]) {
    it(`${path}: no key, and no control the keyboard reaches, sends anything but reads or opens a new record`, async () => {
      expect(await sweep(path)).toEqual([]);
    }, sweepTimeLimit);
  }

  it("the control: the same sweep for a user who may change and archive contacts finds the save keys' and Archive's writes", async () => {
    const found = await sweep(`/contacts/contacts?open=${organisationId}`, [...reads, "contacts.contacts.update", "contacts.contacts.archive"], true);
    const saves = new Set(found.filter((w) => w.method === "PUT" && w.url === `/api/contacts/contacts/${organisationId}`).map((w) => w.key.split(" ")[0]));
    expect(saves).toContain("Ctrl+KeyS");
    expect(found.some((w) => w.method === "POST" && w.url === `/api/contacts/contacts/${organisationId}/archive`), "Archive reached by keyboard").toBe(true);
  }, sweepTimeLimit);
});

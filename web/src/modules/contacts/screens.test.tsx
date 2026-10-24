import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, setInput, settle, type Rendered } from "../../test/render";
import { App } from "../shell/App";
import { extensions } from "./extensions";

// G2 on screen for the contacts screen: with a contact open, every contacts permission is taken
// away in turn, and exactly the controls that need it disappear (New contact, the form's fields and
// Save, Archive and Restore, New person for an organisation), nothing else does, and Alt+N opens
// nothing without the create permission. The server refuses all of them anyway; this keeps the
// screen from offering what will be refused. scripts/contacts-plant-self-test.mjs plants each such
// fault and requires this file to fail.

let view: Rendered | undefined;
let generation = 0;

beforeEach(() => {
  generation += 1;
  localStorage.clear();
});
afterEach(() => closeView());

function closeView() {
  view?.unmount();
  view = undefined;
}

const shown = (): Rendered => view!;

const all = ["contacts.contacts.archive", "contacts.contacts.create", "contacts.contacts.read", "contacts.contacts.update"];

const organisationId = "0190a000-0000-7000-8000-0000000000c1";
const personId = "0190a000-0000-7000-8000-0000000000c2";
const duplicateId = "0190a000-0000-7000-8000-0000000000c3";

const session = (permissions: string[], language: "en" | "ar") => ({
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori", language },
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
  columns: [column("nameEn", "contacts.contact.name"), column("email", "contacts.contact.email")],
  searchFields: ["nameEn", "email"], defaultSort: "nameEn", presets: [], canShare: false, maxTake: 200,
};

const organisation = {
  id: organisationId, kind: "organisation", code: "C-DEMO-1", nameEn: "Falcon Building Materials LLC", nameAr: "فالكون لمواد البناء ذ.م.م",
  organisationId: null, organisationName: null, organisationNameAr: null, jobTitle: null, email: "accounts@falcon.example", phone: "+971 4 555 0100",
  mobile: null, website: null, addressLine1: null, addressLine2: null, area: "Al Quoz", city: "Dubai", emirate: "dubai", poBox: null, country: "AE",
  addressAr: null, tags: ["Customer"], tradeLicenceNumber: null, taxRegistrationNumber: null, defaultCurrency: "AED",
  creditLimit: { amount: "250000.00", currency: "AED", exchangeRate: "1", baseAmount: "250000.00", baseCurrency: "AED" },
  notes: null, isActive: true, peopleCount: 1, createdAt: "2026-10-03T00:00:00Z", updatedAt: "2026-10-03T00:00:00Z", version: 7,
};
const person = {
  id: personId, kind: "person", code: "C-DEMO-2", nameEn: "Hamad Saeed Al Ketbi", nameAr: "حمد سعيد الكتبي", organisationId,
  organisationName: organisation.nameEn, organisationNameAr: organisation.nameAr, jobTitle: "Purchasing Manager", email: "hamad@falcon.example",
  mobile: "+971 50 555 0102", phone: null, city: "Dubai", emirate: "dubai", isActive: true, version: 3,
};

type Options = { path?: string; archived?: boolean; language?: "en" | "ar"; duplicates?: boolean };

function mockServer(permissions: string[], options: Options) {
  return mockFetch((_method, url) => {
    const parsed = new URL(url, "http://localhost");
    const path = parsed.pathname;
    if (url === "/api/auth/session") return { status: 200, body: session(permissions, options.language ?? "en") };
    if (path === "/api/lists/contacts.contacts/definition") return { status: 200, body: definition };
    if (path.endsWith("/views")) return { status: 200, body: { items: [] } };
    if (path === "/api/contacts/contacts/defaults") return { status: 200, body: { country: "AE", defaultCurrency: "AED", baseCurrency: "AED" } };
    if (path === "/api/contacts/contacts/duplicates") {
      const body = options.duplicates && parsed.searchParams.get("nameEn")?.toLowerCase().startsWith("falcon")
        ? [{ id: duplicateId, kind: "organisation", code: "C-OLD", nameEn: "Falcon Building Materials L.L.C.", nameAr: "", email: null, phone: null, mobile: null, taxRegistrationNumber: null, isActive: false, matches: ["name"] }]
        : [];
      return { status: 200, body };
    }
    if (path === "/api/contacts/contacts") {
      if (parsed.searchParams.get("filter")?.startsWith("organisationId eq")) return { status: 200, body: { items: [person], total: 1, next: null } };
      return { status: 200, body: { items: [{ ...organisation, isActive: !options.archived }, person], total: 2, next: null } };
    }
    if (path === `/api/contacts/contacts/${organisationId}`) return { status: 200, body: { ...organisation, isActive: !options.archived } };
    if (path === `/api/contacts/contacts/${personId}`) return { status: 200, body: { ...person, website: null, addressLine1: null, addressLine2: null, area: null, poBox: null, country: "AE", addressAr: null, tags: [], tradeLicenceNumber: null, taxRegistrationNumber: null, defaultCurrency: "AED", creditLimit: null, notes: null, peopleCount: 0, createdAt: "2026-10-03T00:00:00Z", updatedAt: "2026-10-03T00:00:00Z" } };
    return { status: 404, body: {} };
  });
}

async function open(permissions: string[], options: Options = {}) {
  window.history.replaceState(null, "", options.path ?? `/contacts/contacts?open=${organisationId}`);
  mockServer(permissions, options);
  const mine = generation;
  const rendered = await render(<App language={options.language ?? "en"} />);
  if (mine !== generation) return stale(rendered);
  view = rendered;
  for (let i = 0; i < 4; i++) await settle();
  if (mine !== generation) return stale(rendered);
  return controls();
}

function stale(rendered: Rendered): never {
  if (view === rendered) view = undefined;
  rendered.unmount();
  throw new Error("This test ended before its screen opened; the screen was closed.");
}

/** Every control the screen's main area offers: buttons and editable fields outside the list grid
 * (a field in a disabled fieldset, or disabled itself, is not offered). */
function controls(): string[] {
  const main = shown().container.querySelector("main");
  if (!main) return [];
  const out = new Set<string>();
  const offered = (el: Element) => !(el as HTMLInputElement).disabled && !el.closest("fieldset:disabled");
  for (const b of main.querySelectorAll("button")) if (offered(b) && !b.closest("[role=search], thead, table")) out.add(`button:${b.textContent?.trim()}`);
  for (const f of main.querySelectorAll("input, select, textarea")) {
    if (f.closest(".list-toolbar, .lv-toolbar, [role=search], thead, table")) continue;
    if (offered(f)) out.add(`field:${f.getAttribute("name") ?? f.getAttribute("aria-label") ?? f.tagName}`);
  }
  return [...out].sort();
}

const without = (permission: string) => all.filter((p) => p !== permission);

/** What disappears when one permission is taken away (everything else stays). */
const needs: Record<string, (c: string) => boolean> = {
  "contacts.contacts.create": (c) => c === "button:New contact" || c === "button:New person here",
  "contacts.contacts.update": (c) => c === "button:Save" || c === "button:Discard" || c.startsWith("field:"),
  "contacts.contacts.archive": (c) => c === "button:Archive" || c === "button:Restore",
};

const pressAltN = () =>
  act(() => {
    (document.activeElement ?? window).dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, altKey: true, code: "KeyN", key: "n" }));
  });

describe("the contacts screen offers exactly what the user may do", () => {
  it("with every contacts permission, every action is offered", async () => {
    const full = await open(all);
    for (const permission of Object.keys(needs)) {
      expect(full.filter(needs[permission]!), `offers something that needs ${permission}`).not.toEqual([]);
    }
    expect(full).toEqual(expect.arrayContaining(["button:New contact", "button:Archive", "button:New person here", "field:nameEn", "field:nameAr"]));
  });

  for (const permission of all) {
    it(`without ${permission}, exactly the actions that need it disappear`, async () => {
      const full = await open(all);
      closeView();
      const less = await open(without(permission));
      if (permission === "contacts.contacts.read") {
        expect(less.filter((c) => c.startsWith("button:Save") || c.startsWith("field:") || c === "button:New contact" || c === "button:Archive"), "without the read permission").toEqual([]);
        return;
      }
      expect(less, `without ${permission}`).toEqual(full.filter((c) => !needs[permission]!(c)));
    });
  }

  it("an archived contact offers Restore, not Archive, and says it is archived", async () => {
    const offered = await open(all, { archived: true });
    expect(offered).toContain("button:Restore");
    expect(offered).not.toContain("button:Archive");
    // An archived organisation takes no new people.
    expect(offered).not.toContain("button:New person here");
    expect(shown().container.querySelector(".ct-archived")?.textContent).toContain("archived");
    closeView();
    const reader = await open(without("contacts.contacts.archive"), { archived: true });
    expect(reader).not.toContain("button:Restore");
  });

  it("Alt+N opens a new contact only with the create permission", async () => {
    await open(without("contacts.contacts.create"), { path: "/contacts/contacts" });
    pressAltN();
    await settle();
    expect(new URLSearchParams(window.location.search).get("open"), "Alt+N without contacts.contacts.create").toBeNull();
    expect(shown().container.querySelector(".record-form"), "Alt+N without contacts.contacts.create").toBeNull();
    closeView();
    await open(all, { path: "/contacts/contacts" });
    pressAltN();
    for (let i = 0; i < 3; i++) await settle();
    expect(shown().container.querySelector(".record-form"), "Alt+N with contacts.contacts.create").not.toBeNull();
  });

  it("a person's form names their organisation and offers no people of its own", async () => {
    const offered = await open(all, { path: `/contacts/contacts?open=${personId}` });
    expect(offered).toContain("field:organisationId");
    expect(offered).toContain("field:jobTitle");
    expect(offered).not.toContain("button:New person here");
    expect((shown().container.querySelector('input[name="organisationId"]') as HTMLInputElement).value).toBe("Falcon Building Materials LLC");
  });

  it("palette: New contact needs the create permission and opens the contacts screen's own command", () => {
    const action = extensions.actions!.find((a) => a.key === "contacts.contacts.new")!;
    expect(action.permission).toBe("contacts.contacts.create");
    expect(action.command).toBe("contacts.screen.new");
    expect(action.path).toBe("/contacts/contacts");
    const source = extensions.palette!.find((s) => s.key === "contacts.contacts")!;
    expect(source.permission).toBe("contacts.contacts.read");
  });
});

describe("the contact form", () => {
  it("names possible duplicates while the name is typed, as a warning that does not stop the save", async () => {
    await open(all, { path: "/contacts/contacts?open=new", duplicates: true });
    const name = shown().container.querySelector<HTMLInputElement>('input[name="nameEn"]')!;
    setInput(name, "Falcon building materials");
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 500));
    });
    await settle();
    const warning = shown().container.querySelector(".ct-duplicates");
    expect(warning?.textContent).toContain("Falcon Building Materials L.L.C.");
    expect(warning?.textContent).toContain("same name");
    expect(controls()).toContain("button:Save");
  });

  it("on an Arabic screen the Arabic name comes first and has the focus, and the labels are Arabic", async () => {
    await open(all, { path: "/contacts/contacts?open=new", language: "ar" });
    const fields = [...shown().container.querySelectorAll<HTMLInputElement>('.record-form input[name^="name"]')].map((i) => i.name);
    expect(fields.slice(0, 2)).toEqual(["nameAr", "nameEn"]);
    expect(document.activeElement?.getAttribute("name")).toBe("nameAr");
    expect(shown().container.querySelector(".record-form")?.textContent).toContain("الاسم (بالعربية)");
  });
});

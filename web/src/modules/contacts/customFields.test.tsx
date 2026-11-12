import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, setInput, settle, type Rendered } from "../../test/render";
import { App } from "../shell/App";

// The workspace's custom fields in the contacts form (p09): they come with the contact and with the
// new-contact defaults, show in the screen's language, and are sent back with the contact.

let view: Rendered | undefined;
let calls: { method: string; url: string; body?: unknown }[] = [];

beforeEach(() => localStorage.clear());
afterEach(() => {
  view?.unmount();
  view = undefined;
});

const id = "0190a000-0000-7000-8000-0000000000c1";
const parentId = "0190a000-0000-7000-8000-0000000000c9";

const entries = () => [
  { key: "cfCustomerSegment", type: "select", labelEn: "Customer segment", labelAr: "شريحة العميل", required: false, decimals: 0, lookupTarget: null, lookupEndpoint: null, lookupLabelProperty: null, lookupLabelPropertyAr: null,
    options: [{ value: "retail", labelEn: "Retail", labelAr: "تجزئة" }, { value: "wholesale", labelEn: "Wholesale", labelAr: "جملة" }], value: "retail", valueLabel: null, valueLabelAr: null, searchable: false },
  { key: "cfAccountManager", type: "text", labelEn: "Account manager", labelAr: "مدير الحساب", required: true, decimals: 0, options: [], lookupTarget: null, lookupEndpoint: null, lookupLabelProperty: null, lookupLabelPropertyAr: null, value: "Rashid", valueLabel: null, valueLabelAr: null, searchable: true },
  { key: "cfTurnover", type: "number", labelEn: "Annual turnover", labelAr: "حجم الأعمال", required: false, decimals: 2, options: [], lookupTarget: null, lookupEndpoint: null, lookupLabelProperty: null, lookupLabelPropertyAr: null, value: "1250.50", valueLabel: null, valueLabelAr: null, searchable: false },
  { key: "cfParent", type: "lookup", labelEn: "Parent group", labelAr: "المجموعة الأم", required: false, decimals: 0, options: [], lookupTarget: "contacts.contacts", lookupEndpoint: "/api/contacts/contacts", lookupLabelProperty: "nameEn", lookupLabelPropertyAr: "nameAr", value: parentId, valueLabel: "Gulf Holdings", valueLabelAr: "الخليج القابضة", searchable: false },
];

const contact = () => ({
  id, kind: "organisation", code: null, nameEn: "Falcon Building Materials LLC", nameAr: "فالكون لمواد البناء", organisationId: null, organisationName: null, organisationNameAr: null,
  jobTitle: null, email: null, phone: null, mobile: null, website: null, addressLine1: null, addressLine2: null, area: null, city: "Dubai", emirate: "dubai", poBox: null, country: "AE", addressAr: null,
  tags: [], tradeLicenceNumber: null, taxRegistrationNumber: null, defaultCurrency: "AED", creditLimit: null, notes: null, isActive: true, peopleCount: 0,
  createdAt: "2026-10-03T00:00:00Z", updatedAt: "2026-10-03T00:00:00Z", version: 7, customFields: { cfCustomerSegment: "retail", cfAccountManager: "Rashid" }, customFieldDefinitions: entries(),
});

async function open(path: string, language: "en" | "ar" = "en") {
  window.history.replaceState(null, "", path);
  calls = mockFetch((method, url, body) => {
    const p = new URL(url, "http://localhost").pathname;
    if (url === "/api/auth/session") {
      return { status: 200, body: { authenticated: true, user: { id: "u1", email: "a@x.example", displayName: "Mariam", language }, tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor", nameAr: "النور" },
        permissions: ["contacts.contacts.read", "contacts.contacts.create", "contacts.contacts.update"], menu: [{ key: "contacts.contacts", labelKey: "contacts.menu.contacts", path: "/contacts/contacts", group: null }], expiresAt: null } };
    }
    if (p === "/api/lists/contacts.contacts/definition") {
      return { status: 200, body: { key: "contacts.contacts", labelKey: "contacts.contacts.title", endpoint: "/api/contacts/contacts", searchFields: ["nameEn"], defaultSort: "nameEn", presets: [], canShare: false, maxTake: 200,
        columns: [{ key: "nameEn", labelKey: "contacts.contact.name", type: "text", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "contains"] },
          { key: "cfCustomerSegment", labelKey: "customField.abc.label", type: "choice", sortable: false, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [{ value: "retail", labelKey: "customField.abc.option.retail" }], operators: ["eq"] }],
        strings: { "customField.abc.label": { en: "Customer segment", ar: "شريحة العميل" }, "customField.abc.option.retail": { en: "Retail", ar: "تجزئة" } } } };
    }
    if (p.endsWith("/views")) return { status: 200, body: { items: [] } };
    if (p === "/api/contacts/contacts/defaults") return { status: 200, body: { country: "AE", defaultCurrency: "AED", baseCurrency: "AED", customFieldDefinitions: entries().map((e) => ({ ...e, value: e.key === "cfAccountManager" ? "North team" : null })) } };
    if (p === "/api/contacts/contacts/duplicates") return { status: 200, body: [] };
    if (p === "/api/contacts/contacts" && method === "GET") return { status: 200, body: { items: [{ ...contact(), cfCustomerSegment: "retail" }], total: 1, next: null } };
    if (p === `/api/contacts/contacts/${id}`) return { status: 200, body: method === "PUT" ? { ...contact(), version: 8 } : contact() };
    if (p === "/api/contacts/contacts" && method === "POST") return { status: 201, body: { ...contact(), id: "new1", customFieldDefinitions: entries() } };
    void body;
    return { status: 404, body: {} };
  });
  view = await render(<App language={language} />);
  for (let i = 0; i < 4; i++) await settle();
}

const form = () => view!.container.querySelector<HTMLElement>(".list-record")!;
const named = (name: string) => form().querySelector<HTMLInputElement | HTMLSelectElement>(`[name="customFields.${name}"]`)!;

describe("custom fields in the contacts form", () => {
  it("shows the contact's fields in the screen's language with their values", async () => {
    await open(`/contacts/contacts?open=${id}`);
    expect(form().textContent).toContain("Custom fields");
    expect(form().textContent).toContain("Customer segment");
    expect(form().textContent).toContain("Account manager *");
    expect(named("cfCustomerSegment").value).toBe("retail");
    expect(named("cfAccountManager").value).toBe("Rashid");
    expect(named("cfTurnover").value).toBe("1250.50");
  });

  it("shows the Arabic labels on an Arabic screen", async () => {
    await open(`/contacts/contacts?open=${id}`, "ar");
    expect(form().textContent).toContain("حقول مخصصة");
    expect(form().textContent).toContain("شريحة العميل");
    expect(form().textContent).toContain("مدير الحساب *");
  });

  it("sends the values with the contact, a cleared one as null and the version read", async () => {
    await open(`/contacts/contacts?open=${id}`);
    setInput(named("cfAccountManager") as HTMLInputElement, "Mariam");
    setInput(named("cfTurnover") as HTMLInputElement, "");
    const select = named("cfCustomerSegment") as HTMLSelectElement;
    act(() => {
      Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")!.set!.call(select, "wholesale");
      select.dispatchEvent(new Event("change", { bubbles: true }));
    });
    await act(async () => {
      form().querySelector<HTMLElement>("[name='nameEn']")!.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, key: "s", code: "KeyS", ctrlKey: true }));
      await new Promise((r) => setTimeout(r, 20));
    });
    for (let i = 0; i < 3; i++) await settle();
    const put = calls.find((c) => c.method === "PUT" && c.url === `/api/contacts/contacts/${id}`);
    expect((put?.body as { customFields: unknown; version: number }).version).toBe(7);
    expect((put?.body as { customFields: unknown }).customFields).toEqual({
      cfCustomerSegment: "wholesale", cfAccountManager: "Mariam", cfTurnover: null, cfParent: parentId,
    });
  });

  it("starts a new contact with the fields' defaults", async () => {
    await open("/contacts/contacts?open=new");
    expect(named("cfAccountManager").value).toBe("North team");
    expect(named("cfCustomerSegment").value).toBe("");
  });

  it("the list gets the field as a column, labelled with the tenant's own words", async () => {
    await open("/contacts/contacts");
    expect(view!.container.textContent).toContain("Customer segment");
    expect(view!.container.textContent).toContain("Retail");
  });
});

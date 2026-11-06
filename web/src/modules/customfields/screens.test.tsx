import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, setInput, type Rendered } from "../../test/render";
import { settleUntilQuiet, type Call } from "../../test/keySweep";
import { App } from "../shell/App";
import { recordTypes, select, selectRow, text, textRow } from "./testing";

let view: Rendered | undefined;
let calls: (Call & { body: unknown })[] = [];

beforeEach(() => localStorage.clear());
afterEach(() => {
  view?.unmount();
  view = undefined;
});

const all = ["customfields.fields.read", "customfields.fields.manage"];

const session = (permissions: string[], language = "en") => ({
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori", language, numerals: "latn" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: [{ key: "customfields.fields", labelKey: "customfields.menu.fields", path: "/customfields/fields", group: "settings" }],
  expiresAt: null,
});

const column = (key: string, labelKey: string, type: string, extra: object = {}) => ({
  key, labelKey, type, sortable: false, filterable: false, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq"], ...extra,
});
const list = {
  key: "customfields.fields", labelKey: "customfields.fields.title", endpoint: "/api/customfields/fields",
  columns: [column("labelEn", "customfields.field.label", "text"), column("fieldType", "customfields.field.type", "choice", { choices: [{ value: "select", labelKey: "customfields.type.select" }, { value: "text", labelKey: "customfields.type.text" }] }),
    column("required", "customfields.field.required", "boolean", { trueLabelKey: "customfields.required.true", falseLabelKey: "customfields.required.false" })],
  searchFields: ["labelEn"], defaultSort: "recordType", presets: [], canShare: false, maxTake: 200,
};

async function open(path: string, permissions = all, language: "en" | "ar" = "en") {
  view?.unmount();
  window.history.replaceState(null, "", path);
  calls = [];
  const recorded = mockFetch((method, url, body) => {
    const p = new URL(url, "http://localhost").pathname;
    if (url === "/api/auth/session") return { status: 200, body: session(permissions, language) };
    if (p === "/api/lists/customfields.fields/definition") return { status: 200, body: list };
    if (p.endsWith("/views")) return { status: 200, body: { items: [] } };
    if (p === "/api/customfields/fields" && method === "GET") return { status: 200, body: { items: [selectRow(), textRow()], total: 2, next: null } };
    if (p === "/api/customfields/record-types") return { status: 200, body: recordTypes() };
    if (p === "/api/customfields/fields/f1" && method === "GET") return { status: 200, body: select() };
    if (p === "/api/customfields/fields/f2" && method === "GET") return { status: 200, body: text() };
    if (p === "/api/customfields/fields" && method === "POST") return { status: 201, body: { ...select(), id: "f9", labelEn: (body as { labelEn: string }).labelEn } };
    if (p === "/api/customfields/fields/f1" && method === "PUT") return { status: 200, body: { ...select(), version: 4 } };
    return { status: 404, body: {} };
  });
  calls = recorded as typeof calls;
  view = await render(<App language={language} />);
  await settleUntilQuiet(() => calls, () => view?.container.querySelector("main [role=grid]") != null);
}

const cells = () => Array.from(view!.container.querySelectorAll("table[role=grid] tbody tr td")).map((td) => td.textContent ?? "");
const input = (name: string) => view!.container.querySelector<HTMLInputElement>(`.list-record [name="${name}"]`)!;
const area = (name: string) => view!.container.querySelector<HTMLTextAreaElement>(`.list-record textarea[name="${name}"]`)!;
const typeInto = (box: HTMLTextAreaElement, value: string) => {
  act(() => {
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!.call(box, value);
    box.dispatchEvent(new Event("input", { bubbles: true }));
  });
};
const choose = (name: string, value: string) => {
  const select = view!.container.querySelector<HTMLSelectElement>(`.list-record select[name="${name}"]`)!;
  act(() => {
    Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")!.set!.call(select, value);
    select.dispatchEvent(new Event("change", { bubbles: true }));
  });
};

async function wait(ms = 50) {
  await act(async () => {
    await new Promise((r) => setTimeout(r, ms));
  });
}

async function key(target: HTMLElement, init: KeyboardEventInit & { key: string }) {
  await act(async () => {
    target.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init }));
    await new Promise((r) => setTimeout(r, 0));
  });
  await settleUntilQuiet(() => calls);
}

describe("custom fields screen", () => {
  it("lists the workspace's fields with their type and whether a value is required", async () => {
    await open("/customfields/fields");
    expect(cells()).toContain("Customer segment");
    expect(cells()).toContain("Account manager");
    expect(cells()).toContain("Single select");
    expect(cells()).toContain("Required");
    expect(cells()).toContain("Optional");
  });

  it("shows the fields' Arabic labels on an Arabic screen", async () => {
    await open("/customfields/fields", all, "ar");
    expect(cells()).toContain("شريحة العميل");
    expect(cells()).toContain("مدير الحساب");
    expect(document.documentElement.dir).toBe("rtl");
  });

  it("adds a single select: the options appear when the type is chosen and Enter saves what the API is sent", async () => {
    await open("/customfields/fields?open=new");
    await wait();
    setInput(input("labelEn"), "Credit tier");
    setInput(input("labelAr"), "فئة الائتمان");
    choose("type", "select");
    await wait();
    typeInto(area("options"), "Gold | ذهبي\nSilver | فضي");
    await wait();
    choose("defaultValue", "Silver");
    await key(input("labelEn"), { key: "Enter", code: "Enter" });
    const post = calls.find((c) => c.method === "POST" && c.url === "/api/customfields/fields");
    expect(post?.body).toEqual({
      recordType: "contacts.contacts", type: "select", labelEn: "Credit tier", labelAr: "فئة الائتمان", required: false, default: null, decimals: null,
      options: [{ value: null, labelEn: "Gold", labelAr: "ذهبي" }, { value: null, labelEn: "Silver", labelAr: "فضي" }],
      lookupTarget: null, searchable: false, sortOrder: null, isActive: true, version: null,
    });
  });

  it("refuses options with a missing English label before sending anything", async () => {
    await open("/customfields/fields?open=new");
    await wait();
    setInput(input("labelEn"), "Credit tier");
    setInput(input("labelAr"), "فئة");
    choose("type", "select");
    await wait();
    typeInto(area("options"), "| ذهبي");
    await key(input("labelEn"), { key: "Enter", code: "Enter" });
    expect(calls.some((c) => c.method === "POST")).toBe(false);
    expect(view!.container.querySelector('[data-field="options"] .field-error')?.textContent).toContain("English | Arabic");
  });

  it("opens a field with its type fixed and saves a change with the version read", async () => {
    await open("/customfields/fields?open=f1");
    await wait();
    expect(view!.container.querySelector<HTMLSelectElement>('.list-record select[name="type"]')!.disabled).toBe(true);
    expect(area("options").value).toBe("Retail | تجزئة\nWholesale | جملة");
    setInput(input("labelEn"), "Customer class");
    await key(input("labelEn"), { key: "Enter", code: "Enter" });
    const put = calls.find((c) => c.method === "PUT" && c.url === "/api/customfields/fields/f1");
    expect(put?.body).toMatchObject({ labelEn: "Customer class", type: "select", default: "retail", version: 3, options: [{ value: "retail" }, { value: "wholesale" }] });
  });

  it("offers no New button to someone who may only read", async () => {
    await open("/customfields/fields", ["customfields.fields.read"]);
    expect(Array.from(view!.container.querySelectorAll("button")).some((b) => b.textContent === "New custom field")).toBe(false);
  });
});

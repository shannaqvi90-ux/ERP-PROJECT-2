import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, setInput, type Rendered } from "../../test/render";
import { settleUntilQuiet, type Call } from "../../test/keySweep";
import { App } from "../shell/App";
import { invoice, invoiceRow, order, orderRow } from "./testing";

let view: Rendered | undefined;
let calls: (Call & { body: unknown })[] = [];

beforeEach(() => localStorage.clear());
afterEach(() => {
  view?.unmount();
  view = undefined;
});

const all = ["numbering.sequences.read", "numbering.sequences.update"];

const session = (permissions: string[], language = "en") => ({
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori", language, numerals: "latn" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: [{ key: "numbering.sequences", labelKey: "numbering.menu.sequences", path: "/numbering/sequences", group: "settings" }],
  expiresAt: null,
});

const column = (key: string, labelKey: string, type: string, extra: object = {}) => ({
  key, labelKey, type, sortable: false, filterable: false, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq"], ...extra,
});
const list = {
  key: "numbering.sequences", labelKey: "numbering.sequences.title", endpoint: "/api/numbering/sequences",
  columns: [column("nameEn", "numbering.sequence.documentType", "text"), column("companyId", "numbering.sequence.company", "reference"), column("nextNumber", "numbering.sequence.nextNumber", "text"),
    column("prefix", "numbering.sequence.prefix", "text"), column("gapless", "numbering.sequence.gapless", "boolean")],
  searchFields: ["nameEn"], defaultSort: "nameEn", presets: [], canShare: false, maxTake: 200,
};

type Reply = { status: number; body?: unknown };

async function open(path: string, permissions = all, override?: (method: string, path: string, body: unknown) => Reply | undefined, language: "en" | "ar" = "en") {
  view?.unmount();
  window.history.replaceState(null, "", path);
  calls = [];
  const recorded = mockFetch((method, url, body) => {
    const p = new URL(url, "http://localhost").pathname;
    const custom = override?.(method, p, body);
    if (custom) return custom;
    if (url === "/api/auth/session") return { status: 200, body: session(permissions, language) };
    if (p === "/api/lists/numbering.sequences/definition") return { status: 200, body: list };
    if (p.endsWith("/views")) return { status: 200, body: { items: [] } };
    if (p === "/api/numbering/sequences") return { status: 200, body: { items: [orderRow(), invoiceRow()], total: 2, next: null } };
    if (p === "/api/numbering/sequences/s1" && method === "GET") return { status: 200, body: order() };
    if (p === "/api/numbering/sequences/s2" && method === "GET") return { status: 200, body: invoice() };
    if (p === "/api/numbering/sequences/s1" && method === "PUT") {
      const sent = body as { prefix: string; padding: number; reset: string };
      return { status: 200, body: { ...order(), prefix: sent.prefix, padding: sent.padding, reset: sent.reset, nextNumber: `${sent.prefix}${"148".padStart(sent.padding, "0")}`, version: 8 } };
    }
    return { status: 404, body: {} };
  });
  calls = recorded as typeof calls;
  view = await render(<App language={language} />);
  await settleUntilQuiet(() => calls, () => view?.container.querySelector("main [role=grid]") != null);
}

const cells = () => Array.from(view!.container.querySelectorAll("table[role=grid] tbody tr td")).map((td) => td.textContent ?? "");
const field = (name: string) => view!.container.querySelector<HTMLInputElement>(`.list-record [name="${name}"]`)!;
const preview = () => view!.container.querySelector("[data-testid=next-number]")?.textContent;

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

describe("document numbering screen", () => {
  it("lists each company's sequences with the number the next document gets", async () => {
    await open("/numbering/sequences");
    expect(cells()).toContain("Purchase order");
    expect(cells()).toContain("P00148");
    expect(cells()).toContain("INV-2026-01203");
    expect(cells()).toContain("AN-DXB");
    expect(cells()).toContain("Gapless");
  });

  it("opens a sequence with its prefix chosen: typing replaces it, the preview follows, Tab reaches the digits and Enter saves", async () => {
    await open("/numbering/sequences?open=s1");
    await wait();
    const prefix = field("prefix");
    expect(document.activeElement).toBe(prefix);
    expect([prefix.selectionStart, prefix.selectionEnd]).toEqual([0, 1]);
    expect(preview()).toBe("P00148");
    setInput(prefix, "PO-2026-");
    await wait();
    expect(preview()).toBe("PO-2026-00148");
    setInput(field("padding"), "6");
    await wait();
    expect(preview()).toBe("PO-2026-000148");
    expect(view!.container.querySelector(".nb-preview")?.textContent).toContain("then PO-2026-000149, PO-2026-000150");
    expect(view!.container.querySelector(".nb-preview")?.textContent).toContain("Never starts again.");
    await key(field("padding"), { key: "Enter", code: "Enter" });
    const put = calls.find((c) => c.method === "PUT" && c.url === "/api/numbering/sequences/s1");
    expect(put?.body).toEqual({ prefix: "PO-2026-", suffix: "", padding: 6, reset: "never", gapless: false, perBranch: false, nextNumber: null, version: 7 });
  });

  it("previews the next year's first number of a yearly sequence and its counters and last numbers", async () => {
    await open("/numbering/sequences?open=s2");
    await wait();
    expect(preview()).toBe("INV-2026-01203");
    const text = view!.container.querySelector(".nb-preview")?.textContent ?? "";
    expect(text).toContain("Starts again on 1 Jan 2027 with INV-2027-00001.");
    expect(text).toContain("No number is ever skipped.");
    expect(view!.container.querySelector(".nb-history")?.textContent).toContain("1,202");
  });

  it("refuses a yearly pattern with no year before sending, in the screen's language, and a typed next number is previewed", async () => {
    await open("/numbering/sequences?open=s2");
    await wait();
    setInput(field("prefix"), "INV-");
    await wait();
    expect(view!.container.querySelector(".nb-preview")?.textContent).toContain("Correct the pattern");
    await key(field("prefix"), { key: "Enter", code: "Enter" });
    expect(calls.some((c) => c.method === "PUT")).toBe(false);
    expect(view!.container.querySelector('[data-field="reset"] .field-error')?.textContent).toContain("{YYYY}");
    setInput(field("prefix"), "INV-{YY}-");
    setInput(field("nextNumber"), "1500");
    await wait();
    expect(preview()).toBe("INV-26-01500");
  });

  it("shows the server's refusal on its field", async () => {
    await open("/numbering/sequences?open=s1", all, (method, p) =>
      method === "PUT" && p === "/api/numbering/sequences/s1"
        ? { status: 400, body: { code: "validation", title: "Check the fields.", errors: { nextNumber: [{ code: "numberingNextNumberBelowIssued", message: "Number 147 has already been given. Enter a number above it." }] } } }
        : undefined);
    await wait();
    setInput(field("nextNumber"), "100");
    await key(field("nextNumber"), { key: "Enter", code: "Enter" });
    expect(view!.container.querySelector('[data-field="nextNumber"] .field-error')?.textContent).toBe("Number 147 has already been given. Enter a number above it.");
  });

  it("is read-only, with the reason, for an administrator who works in only some branches of the company", async () => {
    await open("/numbering/sequences?open=s1", all, (method, p) =>
      method === "GET" && p === "/api/numbering/sequences/s1" ? { status: 200, body: { ...order(), canUpdate: false } } : method === "GET" && p === "/api/numbering/sequences" ? { status: 200, body: { items: [{ ...orderRow(), canUpdate: false }], total: 1, next: null } } : undefined);
    await wait();
    expect(field("prefix").disabled).toBe(true);
    expect(view!.container.querySelector(".list-record")?.textContent).toContain("Only someone who works in every branch");
  });

  it("speaks Arabic, numbers kept left to right", async () => {
    await open("/numbering/sequences?open=s2", all, undefined, "ar");
    await wait();
    expect(cells()).toContain("فاتورة ضريبية");
    expect(view!.container.querySelector(".nb-preview h3")?.textContent).toBe("الأرقام التالية");
    expect(view!.container.querySelector("[data-testid=next-number]")?.getAttribute("dir")).toBe("ltr");
    expect(view!.container.querySelector(".nb-preview")?.textContent).toContain("لا يُتخطّى أي رقم أبدًا.");
  });
});

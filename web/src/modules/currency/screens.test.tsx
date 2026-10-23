import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { extensions } from "../../kernel/extensions";
import { mockFetch, render, settle, setInput, type Rendered } from "../../test/render";
import { settleUntilQuiet, type Call } from "../../test/keySweep";
import { App } from "../shell/App";
import { parseConversion } from "./extensions";
import { conversion, euro, euroRow, rate, rateRow } from "./testing";

let view: Rendered | undefined;
let calls: (Call & { body: unknown })[] = [];

beforeEach(() => localStorage.clear());
afterEach(() => {
  view?.unmount();
  view = undefined;
});

const all = ["currency.currencies.read", "currency.currencies.update", "currency.rates.read", "currency.rates.create", "currency.rates.update", "currency.rates.delete", "currency.rates.import"];

const session = (permissions: string[], language = "en") => ({
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori", language, numerals: "latn" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: [
    { key: "currency.currencies", labelKey: "currency.menu.currencies", path: "/currency/currencies", group: "settings" },
    { key: "currency.rates", labelKey: "currency.menu.rates", path: "/currency/rates", group: "settings" },
  ],
  expiresAt: null,
});

const column = (key: string, labelKey: string, type: string, extra: object = {}) => ({
  key, labelKey, type, sortable: false, filterable: false, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq"], ...extra,
});
const currenciesList = {
  key: "currency.currencies", labelKey: "currency.currencies.title", endpoint: "/api/currency/currencies",
  columns: [column("code", "currency.currency.code", "text"), column("nameEn", "currency.currency.name", "text"), column("latestRate", "currency.currency.latestRate", "money", { currencyField: "quoteCurrency" }),
    column("latestRateDate", "currency.currency.latestRateDate", "date")],
  searchFields: ["code", "nameEn"], defaultSort: "code", presets: [], canShare: false, maxTake: 200,
};
const ratesList = {
  key: "currency.rates", labelKey: "currency.rates.title", endpoint: "/api/currency/rates",
  columns: [column("rateDate", "currency.rate.date", "date"), column("currencyCode", "currency.rate.currency", "text"), column("exchangeRate", "currency.rate.rate", "money", { currencyField: "quoteCurrency" })],
  searchFields: ["currencyCode"], defaultSort: "-rateDate", presets: [], canShare: false, maxTake: 200,
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
    if (p === "/api/lists/currency.currencies/definition") return { status: 200, body: currenciesList };
    if (p === "/api/lists/currency.rates/definition") return { status: 200, body: ratesList };
    if (p.endsWith("/views")) return { status: 200, body: { items: [] } };
    if (p === "/api/currency/currencies") return { status: 200, body: { items: [euroRow(), { ...euroRow(), id: "c2", code: "AED", nameEn: "UAE Dirham", nameAr: "درهم إماراتي", latestRate: null, latestRateDate: null }], total: 2, next: null } };
    if (p === "/api/currency/currencies/c1") return { status: 200, body: euro() };
    if (p === "/api/currency/rates" && method === "GET") return { status: 200, body: { items: [rateRow()], total: 1, next: null } };
    if (p === "/api/currency/rates/r1" && method === "GET") return { status: 200, body: rate() };
    if (p === "/api/currency/convert") return { status: 200, body: conversion() };
    if (method === "POST" && p === "/api/currency/rates") return { status: 201, body: { ...rate(), id: "r2", rateDate: "2026-10-24", rate: (body as { rate: string }).rate, source: "manual" } };
    return { status: 404, body: {} };
  });
  calls = recorded as typeof calls;
  view = await render(<App language={language} />);
  await settleUntilQuiet(() => calls, () => view?.container.querySelector("main [role=grid]") != null);
}

const cells = () => Array.from(view!.container.querySelectorAll("table[role=grid] tbody tr td")).map((td) => td.textContent ?? "");

async function wait(ms = 50) {
  await act(async () => {
    await new Promise((r) => setTimeout(r, ms));
  });
}

describe("currencies screen", () => {
  it("shows each currency's latest rate in the base currency, and the base currency as such", async () => {
    await open("/currency/currencies");
    expect(cells()).toContain("4.2875 AED");
    expect(cells()).toContain("Base currency");
    expect(cells()).toContain("23 Oct 2026");
  });

  it("opens a currency with today's rate field focused: typing a rate and Enter saves it, dated by the server", async () => {
    await open("/currency/currencies?open=c1");
    await wait();
    const input = view!.container.querySelector<HTMLInputElement>('.cur-quick input[name="rate"]')!;
    expect(input).not.toBeNull();
    expect(document.activeElement).toBe(input);
    setInput(input, "4.2875");
    await act(async () => {
      input.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, key: "Enter", code: "Enter" }));
      await new Promise((r) => setTimeout(r, 0));
    });
    await settleUntilQuiet(() => calls);
    const post = calls.find((c) => c.method === "POST" && c.url === "/api/currency/rates");
    expect(post?.body).toEqual({ currency: "EUR", quoteCurrency: "AED", rate: "4.2875" });
    // The currency form itself was not saved by that Enter.
    expect(calls.some((c) => c.method === "PUT")).toBe(false);
    expect(view!.container.querySelector(".cur-quick [role=status]")?.textContent).toBe("Saved: 1 EUR = 4.2875 AED on 24 Oct 2026.");
  });

  it("asks before replacing today's rate, and replaces it with its version on a second Enter", async () => {
    await open("/currency/currencies?open=c1", all, (method, p) =>
      method === "POST" && p === "/api/currency/rates"
        ? { status: 409, body: { code: "currency.rateExists", title: "A rate for these currencies and this date already exists." } }
        : method === "PUT" && p === "/api/currency/rates/r1"
          ? { status: 200, body: { ...rate(), rate: "4.3" } }
          : undefined);
    await wait();
    const input = view!.container.querySelector<HTMLInputElement>('.cur-quick input[name="rate"]')!;
    setInput(input, "4.3");
    const enter = async () => {
      await act(async () => {
        input.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, key: "Enter", code: "Enter" }));
        await new Promise((r) => setTimeout(r, 0));
      });
      await settleUntilQuiet(() => calls);
    };
    await enter();
    expect(view!.container.querySelector(".cur-quick [role=alert]")?.textContent).toContain("Today already has a rate: 4.2875");
    await enter();
    const put = calls.find((c) => c.method === "PUT" && c.url === "/api/currency/rates/r1");
    expect(put?.body).toMatchObject({ currency: "EUR", quoteCurrency: "AED", rateDate: "2026-10-23", rate: "4.3", version: 2 });
  });

  it("converts an amount as it is typed, with the rate and its date", async () => {
    await open("/currency/currencies?open=c1");
    await wait();
    const amount = view!.container.querySelector<HTMLInputElement>('.cur-convert input[name="amount"]')!;
    setInput(amount, "1250");
    await settleUntilQuiet(() => calls);
    await settle();
    const get = calls.filter((c) => c.url.startsWith("/api/currency/convert?")).at(-1)!;
    expect(new URL(get.url, "http://x").searchParams.get("amount")).toBe("1250");
    expect(new URL(get.url, "http://x").searchParams.get("currency")).toBe("EUR");
    const output = (view!.container.querySelector("[data-testid=conversion]")!.textContent ?? "").replace(/\s/g, " ");
    expect(output).toContain("AED 5,359.38");
    expect(output).toContain("the latest rate before then (23 Oct 2026)");
  });

  it("offers no rate entry to a user who may only read, and the form is read-only", async () => {
    await open("/currency/currencies?open=c1", ["currency.currencies.read", "currency.rates.read"]);
    await wait();
    expect(view!.container.querySelector(".cur-quick")).toBeNull();
    expect(view!.container.querySelector(".cur-convert")).not.toBeNull();
    expect(view!.container.querySelector('[data-testid="record-read-only"]')).not.toBeNull();
  });

  it("offers no rate entry, from the row or the record, to someone who does not work in every company", async () => {
    const limited = { ...euroRow(), everyCompany: false };
    await open("/currency/currencies", all, (_method, p) =>
      p === "/api/currency/currencies" ? { status: 200, body: { items: [limited], total: 1, next: null } }
        : p === "/api/currency/currencies/c1" ? { status: 200, body: { ...euro(), everyCompany: false } } : undefined);
    const row = Array.from(view!.container.querySelectorAll<HTMLElement>("table[role=grid] tbody tr")).find((r) => r.textContent?.includes("EUR"))!;
    act(() => row.click());
    await settleUntilQuiet(() => calls);
    expect(view!.container.querySelector(".cur-convert")).not.toBeNull();
    expect(view!.container.querySelector(".cur-quick")).toBeNull();
    expect(view!.container.querySelector('[data-testid="record-read-only"]')).not.toBeNull();
  });

  it("speaks Arabic: the panel's headings and the converter in Arabic", async () => {
    await open("/currency/currencies?open=c1", all, undefined, "ar");
    await wait();
    expect(view!.container.querySelector(".cur-quick h3")?.textContent).toBe("سعر اليوم");
    expect(view!.container.querySelector(".cur-convert h3")?.textContent).toBe("التحويل إلى AED");
  });
});

describe("exchange rates screen", () => {
  it("enters a new rate with today's date and the working company's base currency", async () => {
    await open("/currency/rates/new");
    await wait();
    const form = view!.container.querySelector("form.record-form")!;
    const select = form.querySelector<HTMLSelectElement>('select[name="currency"]')!;
    await act(async () => {
      select.value = "EUR";
      select.dispatchEvent(new Event("change", { bubbles: true }));
    });
    setInput(form.querySelector<HTMLInputElement>('input[name="rate"]')!, "4.2875");
    await act(async () => {
      form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
      await new Promise((r) => setTimeout(r, 0));
    });
    await settleUntilQuiet(() => calls);
    const post = calls.find((c) => c.method === "POST" && c.url === "/api/currency/rates");
    expect(post?.body).toMatchObject({ currency: "EUR", quoteCurrency: "AED", rate: "4.2875", reference: null });
    expect((post?.body as { rateDate: string }).rateDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it("imports a file: its name and text are sent, and each line's problem is listed", async () => {
    await open("/currency/rates", all, (method, p) =>
      method === "POST" && p === "/api/currency/rates/import"
        ? { status: 400, body: { code: "validation", title: "Some fields need attention.", errors: { content: [{ code: "currencyImportRate", message: "Line 3: 'abc' is not a rate." }] } } }
        : undefined);
    const button = Array.from(view!.container.querySelectorAll<HTMLButtonElement>("button")).find((b) => b.textContent === "Import")!;
    act(() => button.click());
    const file = new File(["currency,date,rate\nEUR,2026-10-23,4.2875\nEUR,2026-10-24,abc\n"], "rates.csv", { type: "text/csv" });
    const picker = document.querySelector<HTMLInputElement>("#cur-import-file")!;
    Object.defineProperty(picker, "files", { value: [file] });
    await act(async () => {
      picker.dispatchEvent(new Event("change", { bubbles: true }));
    });
    const run = Array.from(document.querySelectorAll<HTMLButtonElement>(".cur-import button")).find((b) => b.textContent === "Import")!;
    await act(async () => {
      run.click();
      await new Promise((r) => setTimeout(r, 10));
    });
    await settleUntilQuiet(() => calls);
    const post = calls.find((c) => c.method === "POST" && c.url === "/api/currency/rates/import");
    expect(post?.body).toMatchObject({ fileName: "rates.csv", replaceExisting: false, quoteCurrency: "AED" });
    expect((post?.body as { content: string }).content).toContain("EUR,2026-10-24,abc");
    expect(document.querySelector(".cur-import-errors")?.textContent).toBe("Line 3: 'abc' is not a rate.");
  });
});

describe("the command palette converts as it is typed", () => {
  it("reads an amount and a currency code in either order, with an optional date", () => {
    expect(parseConversion("1250 eur")).toEqual({ amount: "1250", currency: "EUR", date: null });
    expect(parseConversion("EUR 1,250.50")).toEqual({ amount: "1250.50", currency: "EUR", date: null });
    expect(parseConversion("١٢٥٠ usd 2026-09-30")).toEqual({ amount: "1250", currency: "USD", date: "2026-09-30" });
    expect(parseConversion("eur")).toBeNull();
    expect(parseConversion("new rate")).toBeNull();
    expect(parseConversion("1250 euros")).toBeNull();
  });

  it("answers with the amount in the base currency, the rate and its date, opening the rate used", async () => {
    const source = extensions.palette.find((s) => s.key === "currency.convert")!;
    expect(source.permission).toBe("currency.rates.read");
    calls = mockFetch((_method, url) => (url.startsWith("/api/currency/convert?") ? { status: 200, body: conversion() } : { status: 404, body: {} })) as typeof calls;
    const items = (await source.search("1250 eur", { language: "en", signal: new AbortController().signal })) as { title: string; subtitle: string; path: string }[];
    expect(items).toHaveLength(1);
    expect(items[0]!.title.replace(/\s/g, " ")).toBe("EUR 1,250.00 = AED 5,359.38");
    expect(items[0]!.path).toBe("/currency/rates?open=r1");
    expect(items[0]!.subtitle).toContain("4.2875 AED per EUR");
    expect(new URL(calls[0]!.url, "http://x").searchParams.get("amount")).toBe("1250");
    expect(await source.search("hello world", { language: "en", signal: new AbortController().signal })).toEqual([]);
  });
});

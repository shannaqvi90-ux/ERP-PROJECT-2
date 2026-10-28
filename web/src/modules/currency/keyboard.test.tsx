import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { recordInAddress } from "../../kernel/router";
import { mockFetch, render, type Rendered } from "../../test/render";
import { type Call, settleUntilQuiet, sweepKeys, sweepTimeLimit } from "../../test/keySweep";
import { App } from "../shell/App";
import { conversion, euro, euroRow, rate, rateRow } from "./testing";

// G2 on screen for the currency screens, by keyboard: the whole app is shown to a user who may read
// currencies and rates and change nothing; on each screen, with a record open and on the list alone,
// and again with every row of the list chosen, every key a keyboard has is pressed alone and with
// every modifier, whatever a key opens is accepted, and every control the keyboard reaches is
// activated: no request other than a read leaves (the user's own list views excepted), and no key
// opens a new rate. The converter in a currency's panel reads only. The controls: a user who may
// change currencies saves with the form's keys, and one who may enter rates saves today's rate with
// Enter in its field.

let view: Rendered | undefined;
let calls: Call[] = [];

beforeEach(() => localStorage.clear());
afterEach(() => closeView());

function closeView() {
  view?.unmount();
  view = undefined;
}

const reads = ["currency.currencies.read", "currency.rates.read"];

const session = (permissions: string[]) => ({
  authenticated: true,
  user: { id: "u1", email: "viewer@alnoor.example", displayName: "Huda Viewer", language: "en" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: [
    { key: "currency.currencies", labelKey: "currency.menu.currencies", path: "/currency/currencies", group: "settings" },
    { key: "currency.rates", labelKey: "currency.menu.rates", path: "/currency/rates", group: "settings" },
  ],
  expiresAt: null,
});

const column = (key: string, labelKey: string, type = "text") => ({
  key, labelKey, type, sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "contains"],
});
const definition = (key: string, endpoint: string, first: string, labelKey: string) => ({
  key, labelKey: `${key}.title`, endpoint, columns: [column(first, labelKey)], searchFields: [first], defaultSort: first, presets: [], canShare: false, maxTake: 200,
});

async function open(path: string, permissions: string[]): Promise<Call[]> {
  closeView();
  window.history.replaceState(null, "", path);
  calls = mockFetch((method, url) => {
    const p = new URL(url, "http://localhost").pathname;
    if (method !== "GET") return { status: 403, body: { code: "forbidden", title: "Not allowed." } };
    if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
    if (p === "/api/lists/currency.currencies/definition") return { status: 200, body: definition("currency.currencies", "/api/currency/currencies", "code", "currency.currency.code") };
    if (p === "/api/lists/currency.rates/definition") return { status: 200, body: definition("currency.rates", "/api/currency/rates", "currencyCode", "currency.rate.currency") };
    if (p.endsWith("/views")) return { status: 200, body: { items: [] } };
    if (p === "/api/currency/currencies") return { status: 200, body: { items: [euroRow()], total: 1, next: null } };
    if (p === "/api/currency/currencies/c1") return { status: 200, body: euro() };
    if (p === "/api/currency/rates") return { status: 200, body: { items: [rateRow()], total: 1, next: null } };
    if (p === "/api/currency/rates/r1") return { status: 200, body: rate() };
    if (p === "/api/currency/convert") return { status: 200, body: conversion() };
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
    forbidden: () => (recordInAddress() === "new" ? "a new rate offered to a user who may not enter one" : null),
  });
}

describe("currency screens by keyboard, for a user who may read them and change nothing", () => {
  for (const path of ["/currency/currencies?open=c1", "/currency/rates?open=r1", "/currency/currencies", "/currency/rates"]) {
    it(`${path}: no key, and no control the keyboard reaches, sends anything but reads or opens a new rate`, async () => {
      expect(await sweep(path)).toEqual([]);
    }, sweepTimeLimit);
  }

  it("the control: the same sweep for a user who may change currencies finds the save keys' writes", async () => {
    const found = await sweep("/currency/currencies?open=c1", [...reads, "currency.currencies.update"], true);
    const keys = new Set(found.filter((w) => w.method === "PUT" && w.url === "/api/currency/currencies/c1").map((w) => w.key.split(" ")[0]));
    expect(keys).toContain("Ctrl+KeyS");
    expect(keys).toContain("Ctrl+Enter");
  }, sweepTimeLimit);
});

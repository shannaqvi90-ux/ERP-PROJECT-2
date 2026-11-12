import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, type Rendered } from "../../test/render";
import { type Call, settleUntilQuiet, sweepKeys, sweepTimeLimit } from "../../test/keySweep";
import { App } from "../shell/App";
import { recordTypes, select, selectRow, text, textRow } from "./testing";

// G2 on screen for Custom fields, by keyboard: the whole app is shown to a user who may read custom
// fields and change nothing; with a field open and on the list alone, and again with every row of the
// list chosen, every key a keyboard has is pressed alone and with every modifier, whatever a key opens
// is accepted, and every control the keyboard reaches is activated: no request other than a read
// leaves (the user's own list views excepted). The control: a user who may manage fields saves with
// the form's keys.

let view: Rendered | undefined;
let calls: Call[] = [];

beforeEach(() => localStorage.clear());
afterEach(() => closeView());

function closeView() {
  view?.unmount();
  view = undefined;
}

const reads = ["customfields.fields.read"];

const session = (permissions: string[]) => ({
  authenticated: true,
  user: { id: "u1", email: "viewer@alnoor.example", displayName: "Huda Viewer", language: "en" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: [{ key: "customfields.fields", labelKey: "customfields.menu.fields", path: "/customfields/fields", group: "settings" }],
  expiresAt: null,
});

const definition = {
  key: "customfields.fields", labelKey: "customfields.fields.title", endpoint: "/api/customfields/fields",
  columns: [{ key: "labelEn", labelKey: "customfields.field.label", type: "text", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "contains"] }],
  searchFields: ["labelEn"], defaultSort: "recordType", presets: [], canShare: false, maxTake: 200,
};

async function open(path: string, permissions: string[]): Promise<Call[]> {
  closeView();
  window.history.replaceState(null, "", path);
  calls = mockFetch((method, url) => {
    const p = new URL(url, "http://localhost").pathname;
    if (method !== "GET") return { status: 403, body: { code: "forbidden", title: "Not allowed." } };
    if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
    if (p === "/api/lists/customfields.fields/definition") return { status: 200, body: definition };
    if (p.endsWith("/views")) return { status: 200, body: { items: [] } };
    if (p === "/api/customfields/fields") return { status: 200, body: { items: [selectRow(), textRow()], total: 2, next: null } };
    if (p === "/api/customfields/record-types") return { status: 200, body: recordTypes() };
    if (p === "/api/customfields/fields/f1") return { status: 200, body: select() };
    if (p === "/api/customfields/fields/f2") return { status: 200, body: text() };
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
  });
}

describe("custom fields by keyboard, for a user who may read them and change nothing", () => {
  for (const path of ["/customfields/fields?open=f1", "/customfields/fields?open=f2", "/customfields/fields"]) {
    it(`${path}: no key, and no control the keyboard reaches, sends anything but reads`, async () => {
      expect(await sweep(path)).toEqual([]);
    }, sweepTimeLimit);
  }

  it("the control: the same sweep for a user who may manage fields finds the save keys' writes", async () => {
    const found = await sweep("/customfields/fields?open=f1", [...reads, "customfields.fields.manage"], true);
    const keys = new Set(found.filter((w) => w.method === "PUT" && w.url === "/api/customfields/fields/f1").map((w) => w.key.split(" ")[0]));
    expect(keys).toContain("Ctrl+KeyS");
    expect(keys).toContain("Ctrl+Enter");
  }, sweepTimeLimit);
});

import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { recordInAddress } from "../../kernel/router";
import { mockFetch, render, type Rendered } from "../../test/render";
import { type Call, settleUntilQuiet, sweepKeys, sweepTimeLimit } from "../../test/keySweep";
import { App } from "../shell/App";

// G2 on screen for the tenancy screens, by keyboard (critic p06 round 2, plant W2: a read-only
// user's Ctrl+S sent a write while every screen gate passed, because the gates only looked at the
// controls drawn). The whole app is shown to a user who may read every tenancy screen and change
// nothing; on each screen, with a record open and on the list alone, and again with every row of the
// list chosen, every key a keyboard has is pressed alone and with every modifier, whatever a key
// opens is accepted, and every control the keyboard reaches is activated: no request other than a
// read leaves (the user's own settings and list views excepted), and no key opens a new company or
// branch. scripts/forms-plant-self-test.mjs plants faults that this file must catch.

let view: Rendered | undefined;
let calls: Call[] = [];

beforeEach(() => localStorage.clear());
afterEach(() => closeView());

function closeView() {
  view?.unmount();
  view = undefined;
}

const reads = ["tenancy.access.read", "tenancy.branches.read", "tenancy.companies.read", "tenancy.tenant.read", "tenancy.workplace.read"];

const session = (permissions: string[]) => ({
  authenticated: true,
  user: { id: "u1", email: "viewer@alnoor.example", displayName: "Huda Viewer", language: "en" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: [
    { key: "tenancy.access", labelKey: "tenancy.menu.access", path: "/tenancy/access", group: "settings" },
    { key: "tenancy.companies", labelKey: "tenancy.menu.companies", path: "/tenancy/companies", group: "settings" },
    { key: "tenancy.branches", labelKey: "tenancy.menu.branches", path: "/tenancy/branches", group: "settings" },
    { key: "tenancy.tenant", labelKey: "tenancy.menu.tenant", path: "/tenancy/tenant", group: "settings" },
  ],
  expiresAt: null,
});

const column = (key: string, labelKey: string) => ({
  key, labelKey, type: "text", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "contains"],
});
const definition = (key: string, endpoint: string, first: string) => ({
  key, labelKey: `${key}.title`, endpoint, columns: [column(first, "tenancy.company.code")], searchFields: [first], defaultSort: first, presets: [], canShare: false, maxTake: 200,
});

const company = {
  id: "c9", code: "AN-AJM", legalNameEn: "Al Noor Ajman LLC", legalNameAr: "النور عجمان ذ.م.م", tradeLicenceNumber: null, tradeLicenceAuthority: null,
  taxRegistrationNumber: null, baseCurrency: "AED", fiscalYearStartMonth: 1, fiscalYearStartDay: 1, addressLine1: null, addressLine2: null, city: null,
  emirate: "ajman", poBox: null, country: "AE", addressAr: null, phone: null, email: null, website: null, hasLogo: true, logoHash: "abc", isActive: true,
  branchCount: 1, createdAt: "2026-10-03T00:00:00Z", updatedAt: "2026-10-03T00:00:00Z", version: 1, everyBranch: true,
};
const branch = {
  id: "b1", companyId: "c9", companyCode: "AN-AJM", code: "AJM-HQ", nameEn: "Ajman head office", nameAr: "المكتب الرئيسي عجمان", addressLine1: null, addressLine2: null,
  city: null, emirate: "ajman", poBox: null, country: "AE", addressAr: null, phone: null, email: null, isActive: true, version: 3,
};
const access = {
  userId: "u2", displayName: "New clerk", email: "clerk@alnoor.example", isCaller: false, companies: [{ companyId: "c9", allBranches: true, branchIds: [] }],
  options: [{ id: "c9", code: "AN-AJM", legalNameEn: "Al Noor Ajman LLC", legalNameAr: "النور عجمان ذ.م.م", isActive: true,
    branches: [{ id: "b1", code: "AJM-HQ", nameEn: "Ajman head office", nameAr: "المكتب الرئيسي عجمان", isActive: true }] }],
  canEdit: false, readOnlyReason: "tenancy.access.readOnly.permissions", version: 5,
};
const tenant = {
  id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م", status: "active", defaultLanguage: "en",
  timeZone: "Asia/Dubai", weekStart: "monday", timeZones: ["Asia/Dubai"], version: 2,
};
const workplace = {
  companyId: "c9", branchId: "b1",
  companies: [{ id: "c9", code: "AN-AJM", legalNameEn: "Al Noor Ajman LLC", legalNameAr: "النور عجمان ذ.م.م", branches: [{ id: "b1", code: "AJM-HQ", nameEn: "Ajman head office", nameAr: "المكتب الرئيسي عجمان" }] }],
};

async function open(path: string, permissions: string[]): Promise<Call[]> {
  closeView();
  window.history.replaceState(null, "", path);
  calls = mockFetch((method, url) => {
    const p = new URL(url, "http://localhost").pathname;
    if (method !== "GET") return { status: 403, body: { code: "forbidden", title: "Not allowed." } };
    if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
    if (p === "/api/lists/tenancy.companies/definition") return { status: 200, body: definition("tenancy.companies", "/api/tenancy/companies", "code") };
    if (p === "/api/lists/tenancy.branches/definition") return { status: 200, body: definition("tenancy.branches", "/api/tenancy/branches", "code") };
    if (p === "/api/lists/tenancy.access/definition") return { status: 200, body: definition("tenancy.access", "/api/tenancy/access", "displayName") };
    if (p.endsWith("/views")) return { status: 200, body: { items: [] } };
    if (p === "/api/tenancy/companies") return { status: 200, body: { items: [company], total: 1, next: null } };
    if (p === "/api/tenancy/companies/c9") return { status: 200, body: company };
    if (p === "/api/tenancy/branches") return { status: 200, body: { items: [branch], total: 1, next: null } };
    if (p === "/api/tenancy/branches/b1") return { status: 200, body: branch };
    if (p === "/api/tenancy/access") return { status: 200, body: { items: [{ userId: "u2", displayName: "New clerk", email: "clerk@alnoor.example" }], total: 1, next: null } };
    if (p === "/api/tenancy/access/u2") return { status: 200, body: access };
    if (p === "/api/tenancy/tenant") return { status: 200, body: tenant };
    if (p === "/api/tenancy/workplace") return { status: 200, body: workplace };
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

describe("tenancy screens by keyboard, for a user who may read them and change nothing", () => {
  for (const path of ["/tenancy/companies?open=c9", "/tenancy/branches?open=b1", "/tenancy/access?open=u2", "/tenancy/tenant", "/tenancy/companies", "/tenancy/branches"]) {
    it(`${path}: no key, and no control the keyboard reaches, sends anything but reads or opens a new record`, async () => {
      expect(await sweep(path)).toEqual([]);
    }, sweepTimeLimit);
  }

  it("the control: the same sweep for a user who may change companies finds the save keys' writes", async () => {
    const found = await sweep("/tenancy/companies?open=c9", [...reads, "tenancy.companies.update"], true);
    const keys = new Set(found.filter((w) => w.method === "PUT" && w.url === "/api/tenancy/companies/c9").map((w) => w.key.split(" ")[0]));
    expect(keys).toContain("Ctrl+KeyS");
    expect(keys).toContain("Ctrl+Enter");
  }, sweepTimeLimit);

  it("/tenancy/access?open=u2 for a user who may change access, on a user the server marks read-only (one holding more): no key writes", async () => {
    expect(await sweep("/tenancy/access?open=u2", [...reads, "tenancy.access.update"])).toEqual([]);
  }, sweepTimeLimit);
});

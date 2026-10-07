import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, settle, type Rendered } from "../../test/render";
import { sweepKeys, type Call } from "../../test/keySweep";
import { App } from "../shell/App";

// G2 on screen for the tenancy screens, by keyboard (critic p06 round 2, plant W2: a read-only
// user's Ctrl+S sent a write while every screen gate passed, because the gates only looked at the
// controls drawn). The whole app is shown to a user who may read every tenancy screen and change
// nothing; on each screen, with a record open and on the list alone, every key a keyboard has is
// pressed alone and with every modifier, whatever a key opens is accepted, and every control the
// keyboard reaches is activated: no request other than a read leaves (the user's own settings and
// list views excepted). scripts/forms-plant-self-test.mjs plants faults that this file must catch.

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
  await settle();
  await settle();
  await settle();
  return calls;
}

const here = () => window.location.pathname + window.location.search;

async function sweep(path: string) {
  await open(path, reads);
  expect(view!.container.querySelector("main"), `${path} shows its screen`).not.toBeNull();
  return sweepKeys({
    calls,
    shown: () => here() === path && view?.container.querySelector("main") !== null,
    reopen: () => open(path, reads),
    targets: [() => view!.container.querySelector<HTMLElement>("main h2[tabindex], main h1, main [role=grid], main table") ?? null],
  });
}

describe("tenancy screens by keyboard, for a user who may read them and change nothing", () => {
  for (const path of ["/tenancy/companies?open=c9", "/tenancy/branches?open=b1", "/tenancy/access?open=u2", "/tenancy/tenant", "/tenancy/companies", "/tenancy/branches"]) {
    it(`${path}: no key, and no control the keyboard reaches, sends anything but reads`, async () => {
      expect(await sweep(path)).toEqual([]);
    });
  }
});

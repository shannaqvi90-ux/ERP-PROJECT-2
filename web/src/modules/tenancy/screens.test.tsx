import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, settle, type Rendered } from "../../test/render";
import { App } from "../shell/App";

// G2 on screen for the tenancy screens (critic p02 round 3, plants U1 and U2: the company logo's
// upload and remove, and the branch line and New branch, were offered without their permissions
// and no test looked). For every tenancy screen, with a record open, every tenancy permission is
// taken away in turn: exactly the controls that need that permission disappear (buttons, file
// pickers, editable fields, forms), nothing else does, and the new-record key opens nothing. The
// server refuses all of them anyway; this keeps the screens from offering what will be refused.
// scripts/tenancy-plant-self-test.mjs plants each such fault and requires this file to fail.

let view: Rendered | undefined;
/** Which test is running: a test that timed out keeps running its open() into the next tests, and
 * must not put its screen in their place (one slow test once failed six after it this way). */
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

/** The screen open now (a function, so type narrowing never pins it to a stale value). */
const shown = (): Rendered => view!;

const all = [
  "tenancy.access.read",
  "tenancy.access.update",
  "tenancy.branches.create",
  "tenancy.branches.read",
  "tenancy.branches.update",
  "tenancy.companies.create",
  "tenancy.companies.read",
  "tenancy.companies.update",
  "tenancy.tenant.read",
  "tenancy.tenant.update",
  "tenancy.workplace.read",
  "tenancy.workplace.switch",
];

const session = (permissions: string[]) => ({
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori", language: "en" },
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
  branchCount: 1, createdAt: "2026-10-03T00:00:00Z", updatedAt: "2026-10-03T00:00:00Z", version: 1,
};
const branch = {
  id: "b1", companyId: "c9", companyCode: "AN-AJM", code: "AJM-HQ", nameEn: "Ajman head office", nameAr: "المكتب الرئيسي عجمان", addressLine1: null, addressLine2: null,
  city: null, emirate: "ajman", poBox: null, country: "AE", addressAr: null, phone: null, email: null, isActive: true, version: 3,
};
const access = (canEdit = true) => ({
  userId: "u2", displayName: "New clerk", email: "clerk@alnoor.example", isCaller: false, companies: [{ companyId: "c9", allBranches: true, branchIds: [] }],
  options: [{ id: "c9", code: "AN-AJM", legalNameEn: "Al Noor Ajman LLC", legalNameAr: "النور عجمان ذ.م.م", isActive: true,
    branches: [{ id: "b1", code: "AJM-HQ", nameEn: "Ajman head office", nameAr: "المكتب الرئيسي عجمان", isActive: true }] }],
  canEdit, readOnlyReason: canEdit ? null : "tenancy.access.readOnly.permissions", version: 5,
});
const tenant = {
  id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م", status: "active", defaultLanguage: "en",
  timeZone: "Asia/Dubai", weekStart: "monday", timeZones: ["Asia/Dubai"], version: 2,
};

type Screen = "companies" | "branches" | "access" | "tenant";
const address: Record<Screen, string> = {
  companies: "/tenancy/companies?open=c9",
  branches: "/tenancy/branches?open=b1",
  access: "/tenancy/access?open=u2",
  tenant: "/tenancy/tenant",
};

async function open(screen: Screen, permissions: string[], options: { canEdit?: boolean; path?: string; everyBranch?: boolean; everyCompany?: boolean } = {}) {
  window.history.replaceState(null, "", options.path ?? address[screen]);
  mockFetch((_method, url) => {
    const path = new URL(url, "http://localhost").pathname;
    if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
    if (path === "/api/lists/tenancy.companies/definition") return { status: 200, body: definition("tenancy.companies", "/api/tenancy/companies", "code") };
    if (path === "/api/lists/tenancy.branches/definition") return { status: 200, body: definition("tenancy.branches", "/api/tenancy/branches", "code") };
    if (path === "/api/lists/tenancy.access/definition") return { status: 200, body: definition("tenancy.access", "/api/tenancy/access", "displayName") };
    if (path.endsWith("/views")) return { status: 200, body: { items: [] } };
    if (path === "/api/tenancy/companies") return { status: 200, body: { items: [{ ...company, everyBranch: options.everyBranch ?? true }], total: 1, next: null } };
    if (path === "/api/tenancy/companies/c9") return { status: 200, body: { ...company, everyBranch: options.everyBranch ?? true } };
    if (path === "/api/tenancy/branches") return { status: 200, body: { items: [branch], total: 1, next: null } };
    if (path === "/api/tenancy/branches/b1") return { status: 200, body: { ...branch, everyBranch: options.everyBranch ?? true } };
    if (path === "/api/tenancy/access") return { status: 200, body: { items: [], total: 0, next: null } };
    if (path === "/api/tenancy/access/u2") return { status: 200, body: access(options.canEdit ?? true) };
    if (path === "/api/tenancy/tenant") return { status: 200, body: { ...tenant, everyCompany: options.everyCompany ?? true } };
    if (path === "/api/tenancy/workplace") return { status: 200, body: { companyId: "c9", branchId: "b1", companies: [] } };
    return { status: 404, body: {} };
  });
  const mine = generation;
  const rendered = await render(<App language="en" />);
  if (mine !== generation) return stale(rendered);
  view = rendered;
  await settle();
  await settle();
  await settle();
  if (mine !== generation) return stale(rendered);
  return controls();
}

/** A screen opened by a test that has already ended (timed out): take it away and stop that test. */
function stale(rendered: Rendered): never {
  if (view === rendered) view = undefined;
  rendered.unmount();
  throw new Error("This test ended before its screen opened; the screen was closed.");
}

/** Every control the screen's main area offers: buttons, file pickers, forms, and editable fields
 * (a field in a disabled fieldset, or disabled itself, is not offered). */
function controls(): string[] {
  const main = shown().container.querySelector("main");
  if (!main) return [];
  const out = new Set<string>();
  const offered = (el: Element) => !(el as HTMLInputElement).disabled && !el.closest("fieldset:disabled");
  for (const b of main.querySelectorAll("button")) if (offered(b)) out.add(`button:${b.textContent?.trim()}`);
  for (const f of main.querySelectorAll('input[type="file"]')) if (offered(f)) out.add(`file:${f.closest("label")?.textContent?.trim()}`);
  for (const f of main.querySelectorAll("form[aria-label]")) out.add(`form:${f.getAttribute("aria-label")}`);
  for (const f of main.querySelectorAll("input:not([type=file]), select, textarea")) {
    // The list's own search and filter inputs are the same for everyone who may open the list.
    // (Row selection boxes in the list grid select rows; they change nothing.)
    if (f.closest(".list-toolbar, .lv-toolbar, [role=search], thead, table")) continue;
    if (offered(f)) out.add(`field:${f.getAttribute("name") ?? f.getAttribute("aria-label") ?? f.tagName}`);
  }
  return [...out].sort();
}

const without = (permission: string) => all.filter((p) => p !== permission);

/** What disappears from each screen when one permission is taken away (everything else stays).
 * A screen's read permission takes the whole screen away. */
const needs: Record<Screen, Record<string, (c: string) => boolean>> = {
  companies: {
    "tenancy.companies.create": (c) => c === "button:New",
    // The company form's fields, Save, and the logo's upload and remove (plant U1).
    "tenancy.companies.update": (c) =>
      c === "button:Save" || c === "file:Upload logo" || c === "button:Remove logo" ||
      (c.startsWith("field:") && !c.startsWith("field:branch")),
    // The company's branch section, and with it the branch line.
    "tenancy.branches.read": (c) => c.startsWith("field:branch") || c === "form:Add branch" || c === "button:Add branch",
    // The branch line (plant U2).
    "tenancy.branches.create": (c) => c.startsWith("field:branch") || c === "form:Add branch" || c === "button:Add branch",
  },
  branches: {
    // New branch (plant U2).
    "tenancy.branches.create": (c) => c === "button:New",
    // A new branch is added to a company the user picks from the companies they may read.
    "tenancy.companies.read": (c) => c === "button:New",
    "tenancy.branches.update": (c) => c === "button:Save" || c.startsWith("field:"),
  },
  access: {
    "tenancy.access.update": (c) => c === "button:Save" || c.startsWith("field:"),
  },
  tenant: {
    "tenancy.tenant.update": (c) => c === "button:Save" || c.startsWith("field:") || c === "form:Workspace settings",
  },
};
const reads: Record<Screen, string> = {
  companies: "tenancy.companies.read",
  branches: "tenancy.branches.read",
  access: "tenancy.access.read",
  tenant: "tenancy.tenant.read",
};

describe("tenancy screens offer exactly what the user may do", () => {
  for (const screen of ["companies", "branches", "access", "tenant"] as const) {
    it(`${screen}: with every tenancy permission, every action is offered`, async () => {
      const full = await open(screen, all);
      for (const permission of Object.keys(needs[screen])) {
        expect(full.filter(needs[screen][permission]!), `${screen} offers nothing that needs ${permission}`).not.toEqual([]);
      }
    });
    for (const permission of all) {
      it(`${screen}: without ${permission}, exactly the actions that need it disappear`, async () => {
        const full = await open(screen, all);
        closeView();
        const less = await open(screen, without(permission));
        if (permission === reads[screen]) {
          expect(less.filter((c) => c.startsWith("button:Save") || c.startsWith("field:") || c === "button:New"), `${screen} without its read permission`).toEqual([]);
          return;
        }
        const gone = needs[screen][permission] ?? (() => false);
        expect(less, `${screen} without ${permission}`).toEqual(full.filter((c) => !gone(c)));
      });
    }
  }

  it("companies and branches: the new-record key opens nothing without the create permission", async () => {
    for (const [screen, permission, path] of [
      ["companies", "tenancy.companies.create", "/tenancy/companies"],
      ["branches", "tenancy.branches.create", "/tenancy/branches"],
    ] as const) {
      await open(screen, without(permission), { path });
      act(() => {
        (document.activeElement ?? window).dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, altKey: true, code: "KeyN", key: "n" }));
      });
      await settle();
      expect(new URLSearchParams(window.location.search).get("open"), `${screen}: Alt+N without ${permission}`).toBeNull();
      expect(shown().container.querySelector(".record-form"), `${screen}: Alt+N without ${permission}`).toBeNull();
      closeView();
      await open(screen, all, { path });
      act(() => {
        (document.activeElement ?? window).dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, altKey: true, code: "KeyN", key: "n" }));
      });
      await settle();
      expect(shown().container.querySelector(".record-form"), `${screen}: Alt+N with ${permission}`).not.toBeNull();
      closeView();
    }
  });

  // Critic p02 round 4: a user given the Administrator role inside one branch of the company was
  // offered the company's branch line, New on the Branches screen and an editable branch code, and
  // the server refused all three (tenancy.branchNeedsEveryBranch). Records every branch of the
  // company shares (the company itself, its logo, its set of branches and their codes) are offered
  // only to someone who works in every branch of it; their own branch stays theirs to change.
  const someBranches: Record<"companies" | "branches", (c: string) => boolean> = {
    companies: (c) =>
      c === "button:Save" || c === "file:Upload logo" || c === "button:Remove logo" || c.startsWith("field:") ||
      c === "form:Add branch" || c === "button:Add branch",
    branches: (c) => c === "button:New" || c === "field:code",
  };
  for (const screen of ["companies", "branches"] as const) {
    it(`${screen}: where the user works in only some branches of the company, exactly what every branch shares disappears`, async () => {
      const full = await open(screen, all);
      expect(full.filter(someBranches[screen]).length, `${screen} with every branch offers what every branch shares`).toBeGreaterThan(screen === "companies" ? 4 : 1);
      closeView();
      const limited = await open(screen, all, { everyBranch: false });
      expect(limited, `${screen} for a user who works in only some branches`).toEqual(full.filter((c) => !someBranches[screen](c)));
    });
  }

  it("companies: a company where the user works in only some branches says why it cannot be changed", async () => {
    await open("companies", all, { everyBranch: false });
    expect(shown().container.querySelector('[data-testid="record-read-only"]')!.textContent).toContain("only some branches of this company");
  });

  it("branches: Alt+N opens nothing when the user works in only some branches of every company", async () => {
    await open("branches", all, { everyBranch: false, path: "/tenancy/branches" });
    act(() => {
      (document.activeElement ?? window).dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, altKey: true, code: "KeyN", key: "n" }));
    });
    await settle();
    expect(shown().container.querySelector(".record-form"), "Alt+N with only some branches").toBeNull();
  });

  it("branches: a branch code the user may not change says why", async () => {
    await open("branches", all, { everyBranch: false });
    expect(shown().container.querySelector("main")!.textContent).toContain("may change a branch code");
  });

  // Critic p02 round 6: an administrator limited to one company (or one branch) was offered the
  // workspace's settings and the server saved them for every company. The workspace is shared by
  // every company: its form is offered only to someone who works in all of them, every branch of each.
  it("tenant: where the user works in only some companies or branches, the workspace form disappears and says why", async () => {
    const full = await open("tenant", all);
    expect(full.filter((c) => c === "button:Save" || c.startsWith("field:")).length, "the workspace form with every company").toBeGreaterThan(3);
    closeView();
    const limited = await open("tenant", all, { everyCompany: false });
    expect(limited.filter((c) => c === "button:Save" || c.startsWith("field:")), "the workspace form for a user of some companies").toEqual([]);
    expect(shown().container.querySelector('[data-testid="tenant-some-companies-only"]')!.textContent).toContain("only some companies or branches");
  });

  it("access: a user the server marks read-only for the caller offers no change, even with every permission", async () => {
    const offered = await open("access", all, { canEdit: false });
    expect(offered.filter((c) => c === "button:Save" || c.startsWith("field:"))).toEqual([]);
  });
});

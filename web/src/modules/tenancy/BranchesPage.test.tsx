import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, settle, type Rendered } from "../../test/render";
import { App } from "../shell/App";

let view: Rendered | undefined;

afterEach(() => view?.unmount());

const session = {
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori", language: "en" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions: ["tenancy.companies.read", "tenancy.branches.read", "tenancy.branches.create", "tenancy.branches.update", "tenancy.access.read"],
  menu: [
    { key: "tenancy.branches", labelKey: "tenancy.menu.branches", path: "/tenancy/branches", group: "settings" },
    { key: "tenancy.access", labelKey: "tenancy.menu.access", path: "/tenancy/access", group: "settings" },
  ],
  expiresAt: null,
};

const column = (key: string, type: string, extra: object = {}) => ({
  key, labelKey: `tenancy.branch.${key}`, type, sortable: false, filterable: false, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "in"], ...extra,
});

const branchList = {
  key: "tenancy.branches",
  labelKey: "tenancy.branches.title",
  endpoint: "/api/tenancy/branches",
  columns: [column("code", "text", { sortable: true }), column("nameEn", "text"), { ...column("companyId", "reference", { filterable: true, groupable: true }), labelKey: "tenancy.branch.company" }],
  searchFields: ["code", "nameEn"],
  defaultSort: "code",
  presets: [],
  canShare: false,
  maxTake: 200,
};

const accessList = {
  key: "tenancy.access",
  labelKey: "tenancy.access.title",
  endpoint: "/api/tenancy/access",
  columns: [
    { ...column("displayName", "text", { sortable: true }), labelKey: "tenancy.access.user" },
    { ...column("companies", "reference"), labelKey: "tenancy.access.companies" },
  ],
  searchFields: ["displayName"],
  defaultSort: "displayName",
  presets: [],
  canShare: false,
  maxTake: 200,
};

const dxb = { id: "00000000-0000-7000-8000-000000000001", code: "ALN-DXB", legalNameEn: "Al Noor Trading LLC", legalNameAr: "شركة النور للتجارة ذ.م.م", isActive: true };
const shj = { id: "00000000-0000-7000-8000-000000000002", code: "ALN-SHJ", legalNameEn: "Al Noor Industries LLC", legalNameAr: "مصانع النور ذ.م.م", isActive: true };

function serve() {
  return mockFetch((_method, url) => {
    const parsed = new URL(url, "http://localhost");
    if (url === "/api/auth/session") return { status: 200, body: session };
    if (parsed.pathname === "/api/lists/tenancy.branches/definition") return { status: 200, body: branchList };
    if (parsed.pathname === "/api/lists/tenancy.access/definition") return { status: 200, body: accessList };
    if (parsed.pathname.endsWith("/views")) return { status: 200, body: { items: [] } };
    if (parsed.pathname === "/api/tenancy/companies") {
      // Two pages: the screen follows "next" until it has every company.
      return parsed.searchParams.get("after")
        ? { status: 200, body: { items: [shj], total: 2, next: null } }
        : { status: 200, body: { items: [dxb], total: 2, next: "page-2" } };
    }
    if (parsed.pathname === "/api/tenancy/branches") {
      const grouped = parsed.searchParams.get("groupBy") === "companyId";
      return {
        status: 200,
        body: {
          items: [
            { id: "b1", code: "DEIRA-HQ", nameEn: "Deira head office", companyId: dxb.id, companyCode: dxb.code, isActive: true },
            { id: "b2", code: "SHJ-FAC", nameEn: "Sharjah factory", companyId: shj.id, companyCode: shj.code, isActive: true },
          ],
          total: 2,
          next: null,
          groups: grouped ? [{ key: dxb.id, count: 1, totals: null }, { key: shj.id, count: 1, totals: null }] : null,
        },
      };
    }
    if (parsed.pathname === "/api/tenancy/access") {
      return {
        status: 200,
        body: {
          items: [{ id: "u2", displayName: "Omar Haddad", email: "omar@alnoor.example", companies: [{ companyId: dxb.id, code: "ALN-DXB", allBranches: false, branchCount: 2 }, { companyId: shj.id, code: "ALN-SHJ", allBranches: true, branchCount: 3 }] }],
          total: 1,
          next: null,
        },
      };
    }
    return { status: 404, body: {} };
  });
}

const cells = (root: HTMLElement) => Array.from(root.querySelectorAll("table[role=grid] tbody tr td")).map((td) => td.textContent ?? "");

describe("branches and access screens on the shared list", () => {
  beforeEach(() => localStorage.clear());

  it("shows each branch's company by code and name, groups by company with those labels and offers the companies as filter choices", async () => {
    window.history.replaceState(null, "", "/tenancy/branches");
    const calls = serve();
    view = await render(<App language="en" />);
    await settle();
    await settle();
    await settle();
    expect(calls.filter((c) => c.url.startsWith("/api/tenancy/companies?")).length).toBe(2);
    expect(cells(view.container)).toContain("ALN-DXB · Al Noor Trading LLC");
    expect(cells(view.container)).toContain("ALN-SHJ · Al Noor Industries LLC");

    const options = view.container.querySelector<HTMLButtonElement>('button[aria-label="Options for the column Company"]')!;
    act(() => options.click());
    const filter = Array.from(view.container.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')).find((b) => b.textContent === "Filter…")!;
    act(() => filter.click());
    const choices = Array.from(view.container.querySelectorAll(".list-filter .list-check")).map((l) => l.textContent);
    expect(choices).toEqual(["ALN-DXB · Al Noor Trading LLC", "ALN-SHJ · Al Noor Industries LLC"]);
    act(() => view!.container.querySelector<HTMLInputElement>(".list-filter .list-check input")!.click());
    await act(async () => {
      view!.container.querySelector<HTMLFormElement>(".list-filter form")!.requestSubmit();
      await new Promise((r) => setTimeout(r, 0));
    });
    await settle();
    const filtered = calls.filter((c) => c.url.startsWith("/api/tenancy/branches?")).at(-1)!;
    expect(new URL(filtered.url, "http://x").searchParams.get("filter")).toBe(`companyId eq '${dxb.id}'`);
    expect(view.container.querySelector('[aria-label="Filters"]')!.textContent).toContain("ALN-DXB · Al Noor Trading LLC");
  });

  it("labels the groups of branches by company", async () => {
    window.history.replaceState(null, "", "/tenancy/branches?group=companyId");
    serve();
    view = await render(<App language="en" />);
    await settle();
    await settle();
    await settle();
    const groups = Array.from(view.container.querySelectorAll("tbody.list-groups tr .list-group-key")).map((g) => g.textContent ?? "");
    expect(groups.some((g) => g.includes("ALN-DXB · Al Noor Trading LLC"))).toBe(true);
    expect(groups.some((g) => g.includes("ALN-SHJ · Al Noor Industries LLC"))).toBe(true);
  });

  it("lists users with the companies they may work in (branch counts where limited to branches)", async () => {
    window.history.replaceState(null, "", "/tenancy/access");
    serve();
    view = await render(<App language="en" />);
    await settle();
    await settle();
    expect(cells(view.container)).toContain("ALN-DXB (2), ALN-SHJ");
  });
});

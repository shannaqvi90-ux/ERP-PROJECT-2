import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, settle } from "../../test/render";
import { App } from "../shell/App";
import type { Rendered } from "../../test/render";

let view: Rendered | undefined;

beforeEach(() => localStorage.clear());
afterEach(() => view?.unmount());

const session = {
  authenticated: true,
  user: { id: "u1", email: "manager@alnoor.example", displayName: "Branch manager", language: "en" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions: ["tenancy.access.read", "tenancy.access.update"],
  menu: [{ key: "tenancy.access", labelKey: "tenancy.menu.access", path: "/tenancy/access", group: "settings" }],
  expiresAt: null,
};

const definition = {
  key: "tenancy.access",
  labelKey: "tenancy.access.title",
  endpoint: "/api/tenancy/access",
  columns: [{ key: "displayName", labelKey: "tenancy.access.user", type: "text", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "contains"] }],
  searchFields: ["displayName", "email"],
  defaultSort: "displayName",
  presets: [],
  canShare: false,
  maxTake: 200,
};

const dubai = {
  id: "c1", code: "AN-DXB", legalNameEn: "Al Noor Dubai LLC", legalNameAr: "النور دبي ذ.م.م", isActive: true,
  branches: [{ id: "b1", code: "DXB-1", nameEn: "Deira", nameAr: "ديرة", isActive: true }],
};

function serve(access: object, puts: unknown[], conflict = false) {
  let reads = 0;
  return mockFetch((method, url, body) => {
    if (url === "/api/auth/session") return { status: 200, body: session };
    if (url === "/api/lists/tenancy.access/definition") return { status: 200, body: definition };
    if (url === "/api/lists/tenancy.access/views") return { status: 200, body: { items: [] } };
    if (url.startsWith("/api/tenancy/access?")) return { status: 200, body: { items: [], total: 0, next: null } };
    if (url === "/api/tenancy/access/u2" && method === "GET") return { status: 200, body: { ...access, version: 70 + reads++ } };
    if (url === "/api/tenancy/access/u2" && method === "PUT") {
      puts.push(body);
      if (conflict && puts.length === 1) return { status: 409, body: { code: "concurrency", title: "Someone else changed this record." } };
      return { status: 200, body: { ...access, companies: (body as { companies: unknown[] }).companies } };
    }
    return { status: 404, body: {} };
  });
}

describe("company access screen", () => {
  it("says why a stronger user's access is read-only and offers no way to change it", async () => {
    window.history.replaceState(null, "", "/tenancy/access?open=u2");
    serve({ userId: "u2", displayName: "Mariam Al Mansoori", email: "admin@alnoor.example", isCaller: false,
      companies: [{ companyId: "c1", allBranches: true, branchIds: [] }], options: [dubai], canEdit: false, readOnlyReason: "tenancy.access.readOnly.permissions" }, []);
    view = await render(<App language="en" />);
    await settle();
    expect(view.container.querySelector('[data-testid="access-read-only"]')!.textContent).toBe(
      "This user holds permissions you do not hold, so you cannot change their access.");
    expect(view.container.querySelector('.record-form button[type="submit"]')).toBeNull();
    expect(view.container.querySelector<HTMLFieldSetElement>("fieldset.access-list")!.disabled).toBe(true);
  });

  it("lets a branch-limited manager give only their own branch", async () => {
    window.history.replaceState(null, "", "/tenancy/access?open=u2");
    const puts: unknown[] = [];
    serve({ userId: "u2", displayName: "New clerk", email: "clerk@alnoor.example", isCaller: false, companies: [],
      options: [{ ...dubai, canGiveAllBranches: false }], canEdit: true, readOnlyReason: null }, puts);
    view = await render(<App language="en" />);
    await settle();
    const company = view.container.querySelector<HTMLInputElement>('[data-company="AN-DXB"] input[type="checkbox"]')!;
    await act(async () => {
      company.click();
    });
    const all = view.container.querySelectorAll<HTMLInputElement>('[data-company="AN-DXB"] .access-branches input[type="checkbox"]');
    expect(all[0]!.checked).toBe(false);
    expect(all[0]!.disabled).toBe(true);
    expect(view.container.querySelector('[data-company="AN-DXB"] .access-branches')!.textContent).toContain("you can give only those");
    await act(async () => {
      all[1]!.click();
    });
    await act(async () => {
      view!.container.querySelector<HTMLFormElement>(".record-form")!.requestSubmit();
      await new Promise((r) => setTimeout(r, 0));
    });
    // The save carries the version that was read.
    expect(puts).toEqual([{ companies: [{ companyId: "c1", allBranches: false, branchIds: ["b1"] }], version: 70 }]);
  });

  it("says when another administrator saved first and reloads their change on request", async () => {
    window.history.replaceState(null, "", "/tenancy/access?open=u2");
    const puts: unknown[] = [];
    serve({ userId: "u2", displayName: "New clerk", email: "clerk@alnoor.example", isCaller: false, companies: [],
      options: [dubai], canEdit: true, readOnlyReason: null }, puts, true);
    view = await render(<App language="en" />);
    await settle();
    await act(async () => {
      view!.container.querySelector<HTMLInputElement>('[data-company="AN-DXB"] input[type="checkbox"]')!.click();
    });
    await act(async () => {
      view!.container.querySelector<HTMLFormElement>(".record-form")!.requestSubmit();
      await new Promise((r) => setTimeout(r, 0));
    });
    const alert = view.container.querySelector('.record-form [role="alert"]')!;
    expect(alert.textContent).toContain("Another administrator changed this user's access after you opened it.");
    expect(view.container.querySelector('[data-company="AN-DXB"] input[type="checkbox"]')!.matches(":checked")).toBe(true);
    const reload = [...alert.querySelectorAll("button")].find((b) => b.textContent === "Reload")!;
    await act(async () => {
      reload.click();
      await new Promise((r) => setTimeout(r, 0));
    });
    await settle();
    // The draft is replaced by what is saved now, with the new version for the next save.
    expect(view.container.querySelector('.record-form [role="alert"]')).toBeNull();
    expect(view.container.querySelector<HTMLInputElement>('[data-company="AN-DXB"] input[type="checkbox"]')!.checked).toBe(false);
    await act(async () => {
      view!.container.querySelector<HTMLInputElement>('[data-company="AN-DXB"] input[type="checkbox"]')!.click();
    });
    await act(async () => {
      view!.container.querySelector<HTMLFormElement>(".record-form")!.requestSubmit();
      await new Promise((r) => setTimeout(r, 0));
    });
    expect(puts).toEqual([
      { companies: [{ companyId: "c1", allBranches: true, branchIds: [] }], version: 70 },
      { companies: [{ companyId: "c1", allBranches: true, branchIds: [] }], version: 71 },
    ]);
  });
});

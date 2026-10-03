import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, settle, setInput, type Rendered } from "../../test/render";
import { App } from "../shell/App";

let view: Rendered | undefined;

beforeEach(() => {
  localStorage.clear();
  window.history.replaceState(null, "", "/tenancy/companies");
});

afterEach(() => view?.unmount());

const permissions = ["tenancy.companies.read", "tenancy.companies.create", "tenancy.companies.update", "tenancy.branches.read", "tenancy.branches.create"];
const session = {
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori", language: "en" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: [{ key: "tenancy.companies", labelKey: "tenancy.menu.companies", path: "/tenancy/companies", group: "settings" }],
  expiresAt: null,
};

const saved = {
  id: "c9", code: "AN-AJM", legalNameEn: "Al Noor Ajman LLC", legalNameAr: "النور عجمان ذ.م.م", tradeLicenceNumber: null, tradeLicenceAuthority: null,
  taxRegistrationNumber: null, baseCurrency: "AED", fiscalYearStartMonth: 1, fiscalYearStartDay: 1, addressLine1: null, addressLine2: null, city: null,
  emirate: null, poBox: null, country: "AE", addressAr: null, phone: null, email: null, website: null, hasLogo: false, logoHash: null, isActive: true,
  branchCount: 0, createdAt: "2026-10-03T00:00:00Z", updatedAt: "2026-10-03T00:00:00Z", version: 1,
};

function press(init: KeyboardEventInit) {
  act(() => {
    (document.activeElement ?? window).dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init }));
  });
}

const field = (root: HTMLElement, name: string) => root.querySelector<HTMLInputElement>(`[data-field="${name}"] input, input[name="${name}"]`)!;

describe("companies screen", () => {
  it("creates a company from the keyboard (Alt+N, type, Ctrl+S), shows server validation, then adds its first branch with Enter", async () => {
    let posts = 0;
    const calls = mockFetch((method, url, body) => {
      if (url === "/api/auth/session") return { status: 200, body: session };
      if (url.startsWith("/api/tenancy/companies?")) return { status: 200, body: { items: [], total: 0 } };
      if (url === "/api/tenancy/companies" && method === "POST") {
        posts++;
        return posts === 1
          ? { status: 400, body: { code: "validation", title: "Some fields need attention.", errors: { code: [{ code: "tenancyCode", message: "Use 2 to 20 capital letters." }] } } }
          : { status: 201, body: { ...saved, ...(body as object) } };
      }
      if (url === "/api/tenancy/companies/c9") return { status: 200, body: saved };
      if (url.startsWith("/api/tenancy/branches?")) return { status: 200, body: { items: [], total: 0 } };
      if (url === "/api/tenancy/branches" && method === "POST") return { status: 201, body: { id: "b9" } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();

    press({ altKey: true, code: "KeyN", key: "n" });
    await settle();
    const code = field(view.container, "code");
    expect(document.activeElement).toBe(code);
    setInput(code, "a");
    expect(code.value).toBe("A");
    setInput(field(view.container, "legalNameEn"), "Al Noor Ajman LLC");
    setInput(field(view.container, "legalNameAr"), "النور عجمان ذ.م.م");
    press({ ctrlKey: true, key: "s" });
    await settle();
    expect(view.container.querySelector('[data-field="code"] .field-error')!.textContent).toContain("Use 2 to 20 capital letters.");
    expect(code.getAttribute("aria-invalid")).toBe("true");

    setInput(code, "an-ajm");
    press({ ctrlKey: true, key: "s" });
    await settle();
    const post = calls.filter((c) => c.method === "POST" && c.url === "/api/tenancy/companies")[1]!;
    expect(post.body).toMatchObject({ code: "AN-AJM", legalNameEn: "Al Noor Ajman LLC", legalNameAr: "النور عجمان ذ.م.م", baseCurrency: "AED", country: "AE", isActive: true });
    expect(window.location.search).toBe("?id=c9");

    const branchCode = view.container.querySelector<HTMLInputElement>('input[name="branchCode"]')!;
    setInput(branchCode, "hq");
    setInput(view.container.querySelector<HTMLInputElement>('input[name="branchNameEn"]')!, "Head office");
    setInput(view.container.querySelector<HTMLInputElement>('input[name="branchNameAr"]')!, "المكتب الرئيسي");
    await act(async () => {
      view!.container.querySelector<HTMLFormElement>(".quick-add")!.requestSubmit();
      await new Promise((r) => setTimeout(r, 0));
    });
    const branch = calls.find((c) => c.method === "POST" && c.url === "/api/tenancy/branches")!;
    expect(branch.body).toMatchObject({ companyId: "c9", code: "HQ", nameEn: "Head office", nameAr: "المكتب الرئيسي", isActive: true });
  });
});

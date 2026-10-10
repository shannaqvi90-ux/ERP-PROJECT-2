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

const text = (key: string) => ({ key, labelKey: `tenancy.company.${key}`, type: "text", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "contains"] });
const definition = {
  key: "tenancy.companies",
  labelKey: "tenancy.companies.title",
  endpoint: "/api/tenancy/companies",
  columns: [text("code"), text("legalNameEn"), text("legalNameAr")],
  searchFields: ["code", "legalNameEn", "legalNameAr"],
  defaultSort: "code",
  presets: [],
  canShare: false,
  maxTake: 200,
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
      if (url === "/api/lists/tenancy.companies/definition") return { status: 200, body: definition };
      if (url === "/api/lists/tenancy.companies/views") return { status: 200, body: { items: [] } };
      // The administrator works in every company of the workspace (the screen reads one row first).
      if (url === "/api/tenancy/companies?take=1") return { status: 200, body: { items: [{ ...saved, everyCompany: true }], total: 1, next: null } };
      if (url.startsWith("/api/tenancy/companies?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/companies" && method === "POST") {
        posts++;
        return posts === 1
          ? { status: 400, body: { code: "validation", title: "Some fields need attention.", errors: { code: [{ code: "tenancyCode", message: "Use 2 to 20 capital letters." }] } } }
          : { status: 201, body: { ...saved, ...(body as object) } };
      }
      if (url === "/api/tenancy/companies/c9") return { status: 200, body: saved };
      if (url.startsWith("/api/tenancy/branches?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/branches" && method === "POST") return { status: 201, body: { id: "b9" } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();

    press({ altKey: true, code: "KeyN", key: "n" });
    await settle();
    expect(document.activeElement).toBe(field(view.container, "legalNameEn"));
    // The English legal name is required (p06 round 3), the Arabic one recommended.
    expect(field(view.container, "legalNameEn").required).toBe(true);
    expect(field(view.container, "legalNameAr").required).toBe(false);
    const code = field(view.container, "code");
    setInput(code, "a");
    expect(code.value).toBe("A");
    setInput(field(view.container, "legalNameEn"), "Al Noor Ajman LLC");
    setInput(field(view.container, "legalNameAr"), "النور عجمان ذ.م.م");
    press({ ctrlKey: true, key: "s", code: "KeyS" });
    await settle();
    expect(view.container.querySelector('[data-field="code"] .field-error')!.textContent).toContain("Use 2 to 20 capital letters.");
    expect(code.getAttribute("aria-invalid")).toBe("true");

    setInput(code, "an-ajm");
    press({ ctrlKey: true, key: "s", code: "KeyS" });
    await settle();
    const post = calls.filter((c) => c.method === "POST" && c.url === "/api/tenancy/companies")[1]!;
    expect(post.body).toMatchObject({ code: "AN-AJM", legalNameEn: "Al Noor Ajman LLC", legalNameAr: "النور عجمان ذ.م.م", baseCurrency: "AED", country: "AE", isActive: true });
    expect(new URLSearchParams(window.location.search).get("open")).toBe("c9");
    // The same form stays open on the saved company and says so.
    expect(view.container.querySelector('.notice[role="status"]')!.textContent).toBe("Saved.");
    expect(document.activeElement).not.toBe(document.body);
    // The next step, the first branch, has the focus: no click to reach it.
    expect((document.activeElement as HTMLInputElement).name).toBe("branchNameEn");
    // It starts with the company's name, the caret after it: only the branch's own part is typed.
    const line = document.activeElement as HTMLInputElement;
    expect(line.value).toBe("Al Noor Ajman LLC - ");
    expect(line.selectionStart).toBe(line.value.length);

    const branchCode = view.container.querySelector<HTMLInputElement>('input[name="branchCode"]')!;
    setInput(branchCode, "hq");
    setInput(view.container.querySelector<HTMLInputElement>('input[name="branchNameEn"]')!, "Head office");
    setInput(view.container.querySelector<HTMLInputElement>('input[name="branchNameAr"]')!, "المكتب الرئيسي");
    await act(async () => {
      view!.container.querySelector<HTMLFormElement>(".quick-add")!.requestSubmit();
      await new Promise((r) => setTimeout(r, 0));
    });
    // The company's branches are read through the list contract (filter on the company).
    const branchQuery = calls.find((c) => c.method === "GET" && c.url.startsWith("/api/tenancy/branches?"))!;
    expect(new URL(branchQuery.url, "http://x").searchParams.get("filter")).toBe("companyId eq 'c9'");
    const branch = calls.find((c) => c.method === "POST" && c.url === "/api/tenancy/branches")!;
    expect(branch.body).toMatchObject({ companyId: "c9", code: "HQ", nameEn: "Head office", nameAr: "المكتب الرئيسي", isActive: true });
  });

  it("on an Arabic screen, the new company's branch line starts with the Arabic name, after the company's Arabic name, and has the focus", async () => {
    // Critic p02 round 6: in Arabic, Ctrl+S moved to the English name (already holding the English
    // company name), so an Arabic branch name typed there was saved as the English name.
    const calls = mockFetch((method, url, body) => {
      if (url === "/api/auth/session") return { status: 200, body: { ...session, user: { ...session.user, language: "ar" } } };
      if (url === "/api/lists/tenancy.companies/definition") return { status: 200, body: definition };
      if (url === "/api/lists/tenancy.companies/views") return { status: 200, body: { items: [] } };
      // The administrator works in every company of the workspace (the screen reads one row first).
      if (url === "/api/tenancy/companies?take=1") return { status: 200, body: { items: [{ ...saved, everyCompany: true }], total: 1, next: null } };
      if (url.startsWith("/api/tenancy/companies?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/companies" && method === "POST") return { status: 201, body: { ...saved, ...(body as object) } };
      if (url === "/api/tenancy/companies/c9") return { status: 200, body: saved };
      if (url.startsWith("/api/tenancy/branches?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/branches" && method === "POST") return { status: 201, body: { id: "b9" } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="ar" />);
    await settle();
    press({ altKey: true, code: "KeyN", key: "n" });
    await settle();
    setInput(field(view.container, "legalNameEn"), "Al Noor Ajman LLC");
    setInput(field(view.container, "legalNameAr"), "النور عجمان ذ.م.م");
    press({ ctrlKey: true, key: "s", code: "KeyS" });
    await settle();
    const line = document.activeElement as HTMLInputElement;
    expect(line.name).toBe("branchNameAr");
    expect(line.value).toBe("النور عجمان ذ.م.م - ");
    expect(line.selectionStart).toBe(line.value.length);
    // The Arabic name comes first on the line (Tab goes on to the English one).
    const names = [...view.container.querySelectorAll<HTMLInputElement>(".quick-add input")].map((i) => i.name);
    expect(names.indexOf("branchNameAr")).toBeLessThan(names.indexOf("branchNameEn"));
    setInput(line, line.value + "فرع العين");
    await act(async () => {
      view!.container.querySelector<HTMLFormElement>(".quick-add")!.requestSubmit();
      await new Promise((r) => setTimeout(r, 0));
    });
    const branch = calls.find((c) => c.method === "POST" && c.url === "/api/tenancy/branches")!;
    expect(branch.body).toMatchObject({ nameAr: "النور عجمان ذ.م.م - فرع العين", nameEn: "Al Noor Ajman LLC" });
  });

  it("names a new branch after its company: the typed part follows the company's name, and the name alone is the company's", async () => {
    window.history.replaceState(null, "", "/tenancy/companies?open=c9");
    const calls = mockFetch((method, url) => {
      if (url === "/api/auth/session") return { status: 200, body: session };
      if (url === "/api/lists/tenancy.companies/definition") return { status: 200, body: definition };
      if (url === "/api/lists/tenancy.companies/views") return { status: 200, body: { items: [] } };
      // The administrator works in every company of the workspace (the screen reads one row first).
      if (url === "/api/tenancy/companies?take=1") return { status: 200, body: { items: [{ ...saved, everyCompany: true }], total: 1, next: null } };
      if (url.startsWith("/api/tenancy/companies?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/companies/c9") return { status: 200, body: saved };
      if (url.startsWith("/api/tenancy/branches?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/branches" && method === "POST") return { status: 201, body: { id: "b9" } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    const line = () => view!.container.querySelector<HTMLInputElement>('input[name="branchNameEn"]')!;
    expect(line().value).toBe("Al Noor Ajman LLC - ");
    setInput(line(), line().value + "Jebel Ali Branch");
    await act(async () => {
      view!.container.querySelector<HTMLFormElement>(".quick-add")!.requestSubmit();
      await new Promise((r) => setTimeout(r, 0));
    });
    // The line is ready for the next one, again with the company's name.
    expect(line().value).toBe("Al Noor Ajman LLC - ");
    await act(async () => {
      view!.container.querySelector<HTMLFormElement>(".quick-add")!.requestSubmit();
      await new Promise((r) => setTimeout(r, 0));
    });
    const posts = calls.filter((c) => c.method === "POST" && c.url === "/api/tenancy/branches");
    expect(posts.map((p) => (p.body as { nameEn: string }).nameEn)).toEqual(["Al Noor Ajman LLC - Jebel Ali Branch", "Al Noor Ajman LLC"]);
  });

  it("keeps the new company's branch line, and what is typed in it, right after the save: the saved answer is the record, not read again (p01 round 6 health check)", async () => {
    // A read of the company right after its creation used to swap the form for "Loading" and back:
    // the branch line was dropped and drawn again with only the company's name in it, so a quick
    // typist's branch name was lost and Enter added a branch named after the company. Here that
    // read would never answer.
    const companyReads: string[] = [];
    const calls = mockFetch((method, url, body) => {
      if (url === "/api/auth/session") return { status: 200, body: session };
      if (url === "/api/lists/tenancy.companies/definition") return { status: 200, body: definition };
      if (url === "/api/lists/tenancy.companies/views") return { status: 200, body: { items: [] } };
      // The administrator works in every company of the workspace (the screen reads one row first).
      if (url === "/api/tenancy/companies?take=1") return { status: 200, body: { items: [{ ...saved, everyCompany: true }], total: 1, next: null } };
      if (url.startsWith("/api/tenancy/companies?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/companies" && method === "POST") return { status: 201, body: { ...saved, ...(body as object) } };
      if (url === "/api/tenancy/companies/c9") {
        companyReads.push(method);
        return new Promise(() => {});
      }
      if (url.startsWith("/api/tenancy/branches?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/branches" && method === "POST") return { status: 201, body: { id: "b9" } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();

    press({ altKey: true, code: "KeyN", key: "n" });
    await settle();
    setInput(field(view.container, "legalNameEn"), "Al Noor Ajman LLC");
    press({ ctrlKey: true, key: "s", code: "KeyS" });
    await settle();
    const line = view.container.querySelector<HTMLInputElement>('input[name="branchNameEn"]');
    expect(new URLSearchParams(window.location.search).get("open")).toBe("c9");
    expect(view.container.querySelector(".record-form[aria-busy]")).toBeNull();
    expect(line).not.toBeNull();
    expect(document.activeElement).toBe(line);
    setInput(line!, "Al Noor Ajman LLC - Head office");
    await settle();
    await settle();
    expect(companyReads).toEqual([]);
    // The same line, still focused, still holding what was typed.
    expect(view.container.querySelector('input[name="branchNameEn"]')).toBe(line);
    expect(line!.isConnected).toBe(true);
    expect(document.activeElement).toBe(line);
    expect(line!.value).toBe("Al Noor Ajman LLC - Head office");
    await act(async () => {
      view!.container.querySelector<HTMLFormElement>(".quick-add")!.requestSubmit();
      await new Promise((r) => setTimeout(r, 0));
    });
    const branch = calls.find((c) => c.method === "POST" && c.url === "/api/tenancy/branches")!;
    expect(branch.body).toMatchObject({ companyId: "c9", nameEn: "Al Noor Ajman LLC - Head office" });
  });

  it("shows a branch added from the keyboard in the company's branch table, even when the first read of the table answers last (lead, p06 round 2)", async () => {
    // The first read of the branch table (made when the form opens) answers only after the read
    // that follows the new branch: an answer arriving last must not put the stale, empty table back.
    let releaseFirst: () => void = () => {};
    let branchReads = 0;
    const headOffice = { id: "b9", companyId: "c9", companyCode: "AN-AJM", code: "HQ", nameEn: "Head office", nameAr: "المكتب الرئيسي", city: null, emirate: null, isActive: true, version: 1 };
    mockFetch((method, url) => {
      if (url === "/api/auth/session") return { status: 200, body: session };
      if (url === "/api/lists/tenancy.companies/definition") return { status: 200, body: definition };
      if (url === "/api/lists/tenancy.companies/views") return { status: 200, body: { items: [] } };
      // The administrator works in every company of the workspace (the screen reads one row first).
      if (url === "/api/tenancy/companies?take=1") return { status: 200, body: { items: [{ ...saved, everyCompany: true }], total: 1, next: null } };
      if (url.startsWith("/api/tenancy/companies?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/companies/c9") return { status: 200, body: saved };
      if (url.startsWith("/api/tenancy/branches?")) {
        branchReads++;
        if (branchReads === 1) {
          return new Promise((resolve) => {
            releaseFirst = () => resolve({ status: 200, body: { items: [], total: 0, next: null } });
          });
        }
        return { status: 200, body: { items: [headOffice], total: 1, next: null } };
      }
      if (url === "/api/tenancy/branches" && method === "POST") return { status: 201, body: headOffice };
      return { status: 404, body: {} };
    });
    window.history.replaceState(null, "", "/tenancy/companies?open=c9");
    view = await render(<App language="en" />);
    await settle();
    const nameEn = view.container.querySelector<HTMLInputElement>('input[name="branchNameEn"]')!;
    nameEn.focus();
    setInput(nameEn, "Head office");
    setInput(view.container.querySelector<HTMLInputElement>('input[name="branchNameAr"]')!, "المكتب الرئيسي");
    setInput(view.container.querySelector<HTMLInputElement>('input[name="branchCode"]')!, "hq");
    await act(async () => {
      view!.container.querySelector<HTMLFormElement>(".quick-add")!.requestSubmit();
      await new Promise((r) => setTimeout(r, 0));
    });
    await settle();
    const rows = () => [...view!.container.querySelectorAll('section[aria-label="Branches"] tbody tr')].map((r) => r.textContent ?? "");
    expect(rows().some((r) => r.includes("Head office"))).toBe(true);
    await act(async () => {
      releaseFirst();
      await new Promise((r) => setTimeout(r, 0));
    });
    await settle();
    expect(rows().some((r) => r.includes("Head office"))).toBe(true);
  });

  it("keeps a branch just added when the branches read on opening the form answers after it (a busy server)", async () => {
    window.history.replaceState(null, "", "/tenancy/companies?open=c9");
    let added = false;
    mockFetch((method, url) => {
      if (url === "/api/auth/session") return { status: 200, body: session };
      if (url === "/api/lists/tenancy.companies/definition") return { status: 200, body: definition };
      if (url === "/api/lists/tenancy.companies/views") return { status: 200, body: { items: [] } };
      // The administrator works in every company of the workspace (the screen reads one row first).
      if (url === "/api/tenancy/companies?take=1") return { status: 200, body: { items: [{ ...saved, everyCompany: true }], total: 1, next: null } };
      if (url.startsWith("/api/tenancy/companies?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/companies/c9") return { status: 200, body: saved };
      if (url.startsWith("/api/tenancy/branches?")) {
        const items = added ? [{ id: "b9", code: "JA", nameEn: "Al Noor Ajman LLC - Jebel Ali Branch", nameAr: "", city: null, emirate: null, isActive: true }] : [];
        return { status: 200, body: { items, total: items.length, next: null } };
      }
      if (url === "/api/tenancy/branches" && method === "POST") {
        added = true;
        return { status: 201, body: { id: "b9" } };
      }
      return { status: 404, body: {} };
    });
    // The first read of the company's branches (made when the form opens) is held back.
    const answer = globalThis.fetch;
    let release: () => void = () => {};
    const held = new Promise<void>((r) => (release = r));
    let first = true;
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      if (first && String(input).startsWith("/api/tenancy/branches?")) {
        first = false;
        const response = await answer(input, init);
        await held;
        return response;
      }
      return answer(input, init);
    }) as typeof fetch;
    view = await render(<App language="en" />);
    await settle();
    const line = () => view!.container.querySelector<HTMLInputElement>('input[name="branchNameEn"]')!;
    setInput(line(), line().value + "Jebel Ali Branch");
    await act(async () => {
      view!.container.querySelector<HTMLFormElement>(".quick-add")!.requestSubmit();
      await new Promise((r) => setTimeout(r, 0));
    });
    const rows = () => [...view!.container.querySelectorAll(".record-section table tbody tr")].map((r) => r.textContent ?? "");
    expect(rows().some((r) => r.includes("Jebel Ali Branch"))).toBe(true);
    // The older, empty answer arrives last and must not replace the list.
    await act(async () => {
      release();
      await new Promise((r) => setTimeout(r, 0));
    });
    expect(rows().some((r) => r.includes("Jebel Ali Branch"))).toBe(true);
  });

  it("saves with Ctrl+Enter as well as Ctrl+S, the save keys of every identity form, and announces them and Enter (in a one-line field) on the save button", async () => {
    const calls = mockFetch((method, url, body) => {
      if (url === "/api/auth/session") return { status: 200, body: session };
      if (url === "/api/lists/tenancy.companies/definition") return { status: 200, body: definition };
      if (url === "/api/lists/tenancy.companies/views") return { status: 200, body: { items: [] } };
      // The administrator works in every company of the workspace (the screen reads one row first).
      if (url === "/api/tenancy/companies?take=1") return { status: 200, body: { items: [{ ...saved, everyCompany: true }], total: 1, next: null } };
      if (url.startsWith("/api/tenancy/companies?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/companies" && method === "POST") return { status: 201, body: { ...saved, ...(body as object) } };
      if (url === "/api/tenancy/companies/c9") return { status: 200, body: saved };
      if (url.startsWith("/api/tenancy/branches?")) return { status: 200, body: { items: [], total: 0, next: null } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    press({ altKey: true, code: "KeyN", key: "n" });
    await settle();
    setInput(field(view.container, "code"), "an-ajm");
    setInput(field(view.container, "legalNameEn"), "Al Noor Ajman LLC");
    setInput(field(view.container, "legalNameAr"), "النور عجمان ذ.م.م");
    press({ ctrlKey: true, key: "Enter", code: "Enter" });
    await settle();
    const posts = calls.filter((c) => c.method === "POST" && c.url === "/api/tenancy/companies");
    expect(posts).toHaveLength(1);
    expect(posts[0]!.body).toMatchObject({ code: "AN-AJM", legalNameEn: "Al Noor Ajman LLC" });
    expect(view.container.querySelector('button[type="submit"][aria-keyshortcuts]')!.getAttribute("aria-keyshortcuts")).toBe("Enter Control+S Control+Enter");
  });

  it("shows the company under its Arabic name on Arabic screens and starts a new branch in the company's emirate", async () => {
    window.history.replaceState(null, "", "/tenancy/companies?open=c9");
    const dubai = { ...saved, emirate: "dubai", city: "Dubai" };
    const calls = mockFetch((method, url) => {
      if (url === "/api/auth/session") return { status: 200, body: { ...session, user: { ...session.user, language: "ar" } } };
      if (url === "/api/lists/tenancy.companies/definition") return { status: 200, body: definition };
      if (url === "/api/lists/tenancy.companies/views") return { status: 200, body: { items: [] } };
      // The administrator works in every company of the workspace (the screen reads one row first).
      if (url === "/api/tenancy/companies?take=1") return { status: 200, body: { items: [{ ...saved, everyCompany: true }], total: 1, next: null } };
      if (url.startsWith("/api/tenancy/companies?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/companies/c9") return { status: 200, body: dubai };
      if (url.startsWith("/api/tenancy/branches?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/branches" && method === "POST") return { status: 201, body: { id: "b9" } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="ar" />);
    await settle();
    expect(view.container.querySelector(".record-header h2")!.textContent).toBe("AN-AJM · النور عجمان ذ.م.م");
    const emirate = view.container.querySelector<HTMLSelectElement>('select[name="branchEmirate"]')!;
    expect(emirate.value).toBe("dubai");
    setInput(view.container.querySelector<HTMLInputElement>('input[name="branchNameEn"]')!, "Deira shop");
    await act(async () => {
      view!.container.querySelector<HTMLFormElement>(".quick-add")!.requestSubmit();
      await new Promise((r) => setTimeout(r, 0));
    });
    const branch = calls.find((c) => c.method === "POST" && c.url === "/api/tenancy/branches")!;
    expect(branch.body).toMatchObject({ companyId: "c9", nameEn: "Deira shop", emirate: "dubai", country: "AE" });
    // The line is ready for the next branch, again in the company's emirate.
    expect(view.container.querySelector<HTMLSelectElement>('select[name="branchEmirate"]')!.value).toBe("dubai");
  });

  it("adds the typed branch with Ctrl+S or Ctrl+Enter on the branch line, as Enter does, and leaves an untouched line's Ctrl+S to the company form", async () => {
    // Critic p02 round 8: after typing the branch name, Ctrl+S (the save key of every form) left the
    // line pending ("Branches (0 branches)").
    let companyPuts = 0;
    const calls = mockFetch((method, url) => {
      if (url === "/api/auth/session") return { status: 200, body: session };
      if (url === "/api/lists/tenancy.companies/definition") return { status: 200, body: definition };
      if (url === "/api/lists/tenancy.companies/views") return { status: 200, body: { items: [] } };
      // The administrator works in every company of the workspace (the screen reads one row first).
      if (url === "/api/tenancy/companies?take=1") return { status: 200, body: { items: [{ ...saved, everyCompany: true }], total: 1, next: null } };
      if (url.startsWith("/api/tenancy/companies?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/companies/c9" && method === "PUT") {
        companyPuts++;
        return { status: 200, body: saved };
      }
      if (url === "/api/tenancy/companies/c9") return { status: 200, body: saved };
      if (url.startsWith("/api/tenancy/branches?")) return { status: 200, body: { items: [], total: 0, next: null } };
      if (url === "/api/tenancy/branches" && method === "POST") return { status: 201, body: { id: "b9" } };
      return { status: 404, body: {} };
    });
    window.history.replaceState(null, "", "/tenancy/companies?open=c9");
    view = await render(<App language="en" />);
    await settle();
    const line = () => view!.container.querySelector<HTMLInputElement>('input[name="branchNameEn"]')!;
    const branchPosts = () => calls.filter((c) => c.method === "POST" && c.url === "/api/tenancy/branches");

    // Untouched (only the company's name the line starts with): Ctrl+S adds nothing.
    act(() => line().focus());
    press({ ctrlKey: true, key: "s", code: "KeyS" });
    await settle();
    expect(branchPosts()).toEqual([]);
    const putsBefore = companyPuts;

    setInput(line(), line().value + "Jebel Ali Branch");
    act(() => line().focus());
    press({ ctrlKey: true, key: "s", code: "KeyS" });
    await settle();
    expect(branchPosts().map((c) => c.body)).toMatchObject([{ companyId: "c9", nameEn: "Al Noor Ajman LLC - Jebel Ali Branch" }]);
    // The line is ready for the next branch, the focus on it; the company itself was not saved.
    expect(line().value).toBe("Al Noor Ajman LLC - ");
    expect(document.activeElement).toBe(line());
    expect(companyPuts).toBe(putsBefore);

    // Ctrl+Enter too, and on an Arabic keyboard layout (the S by key position).
    setInput(line(), line().value + "Deira Shop");
    act(() => line().focus());
    press({ ctrlKey: true, key: "Enter", code: "Enter" });
    await settle();
    setInput(line(), line().value + "Karama Shop");
    act(() => line().focus());
    press({ ctrlKey: true, key: "س", code: "KeyS" });
    await settle();
    expect(branchPosts().map((c) => (c.body as { nameEn: string }).nameEn)).toEqual([
      "Al Noor Ajman LLC - Jebel Ali Branch",
      "Al Noor Ajman LLC - Deira Shop",
      "Al Noor Ajman LLC - Karama Shop",
    ]);
  });
});

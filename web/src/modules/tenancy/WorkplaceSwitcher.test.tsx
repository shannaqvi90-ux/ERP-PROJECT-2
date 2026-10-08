import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, settle, setInput, type Rendered } from "../../test/render";
import { App } from "../shell/App";
import { collectExtensions } from "../../kernel/extensions";
import { extensions } from "./extensions";

let view: Rendered | undefined;

beforeEach(() => {
  localStorage.clear();
  window.history.replaceState(null, "", "/");
});

afterEach(() => view?.unmount());

const session = (permissions: string[], language: "en" | "ar" = "en") => ({
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori", language },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: [],
  expiresAt: null,
});

const workplace = {
  companyId: "c1",
  branchId: "b1",
  companies: [
    { id: "c1", code: "ALN-DXB", legalNameEn: "Al Noor Trading LLC", legalNameAr: "شركة النور للتجارة ذ.م.م", baseCurrency: "AED", branches: [{ id: "b1", code: "DEIRA-HQ", nameEn: "Deira head office", nameAr: "المكتب الرئيسي - ديرة" }] },
    { id: "c2", code: "ALN-SHJ", legalNameEn: "Al Noor Industries LLC", legalNameAr: "مصانع النور ذ.م.م", baseCurrency: "AED", branches: [{ id: "b2", code: "SHJ-FAC", nameEn: "Sharjah factory", nameAr: "مصنع الشارقة" }] },
  ],
};

function key(init: KeyboardEventInit, target: EventTarget = window) {
  act(() => {
    target.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init }));
  });
}

describe("working company switcher", () => {
  it("is contributed to the top bar and the command palette through the shell's extension points", () => {
    const all = collectExtensions({ "../modules/tenancy/extensions.tsx": { extensions } });
    expect(all.topbar.map((i) => [i.key, i.permission])).toEqual([["tenancy.workplace", "tenancy.workplace.read"]]);
    expect(all.palette.map((i) => [i.key, i.permission])).toEqual([["tenancy.workplace", "tenancy.workplace.switch"]]);
  });

  it("shows the working company and branch, and switches by keyboard: Alt+C, type, Enter", async () => {
    let current = workplace;
    const calls = mockFetch((method, url, body) => {
      if (url === "/api/auth/session") return { status: 200, body: session(["tenancy.workplace.read", "tenancy.workplace.switch"]) };
      if (url === "/api/tenancy/workplace" && method === "GET") return { status: 200, body: current };
      if (url === "/api/tenancy/workplace" && method === "PUT") {
        const { companyId, branchId } = body as { companyId: string; branchId: string };
        current = { ...workplace, companyId, branchId };
        return { status: 200, body: current };
      }
      return { status: 404, body: {} };
    });
    let changed: unknown = null;
    window.addEventListener("erp:workplace-changed", (e) => (changed = (e as CustomEvent).detail), { once: true });
    view = await render(<App language="en" />);
    await settle();
    const button = view.container.querySelector<HTMLButtonElement>('[data-testid="workplace"]')!;
    expect(button.textContent).toBe("ALN-DXB · DEIRA-HQ");
    expect(button.getAttribute("aria-keyshortcuts")).toBe("Alt+C");

    key({ altKey: true, code: "KeyC", key: "c" });
    const filter = view.container.querySelector<HTMLInputElement>(".workplace-popover input")!;
    expect(document.activeElement).toBe(filter);
    expect(view.container.querySelectorAll('[role="option"]')).toHaveLength(2);

    setInput(filter, "shj");
    expect(view.container.querySelectorAll('[role="option"]')).toHaveLength(1);
    key({ key: "Enter" }, filter);
    await settle();

    const put = calls.find((c) => c.method === "PUT");
    expect(put?.body).toEqual({ companyId: "c2", branchId: "b2" });
    expect(put?.headers["X-Erp-Request"]).toBe("1");
    expect(view.container.querySelector('[data-testid="workplace"]')!.textContent).toBe("ALN-SHJ · SHJ-FAC");
    expect(view.container.querySelector(".workplace-popover")).toBeNull();
    expect(changed).toEqual({ companyId: "c2", branchId: "b2" });
  });

  it("offers each other company as a one-click button", async () => {
    let current = workplace;
    const calls = mockFetch((method, url, body) => {
      if (url === "/api/auth/session") return { status: 200, body: session(["tenancy.workplace.read", "tenancy.workplace.switch"]) };
      if (url === "/api/tenancy/workplace" && method === "GET") return { status: 200, body: current };
      if (url === "/api/tenancy/workplace" && method === "PUT") {
        current = { ...workplace, ...(body as object) };
        return { status: 200, body: current };
      }
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    const quick = view.container.querySelectorAll<HTMLButtonElement>(".workplace-chip");
    expect([...quick].map((b) => b.textContent)).toEqual(["ALN-SHJ"]);
    await act(async () => {
      quick[0]!.click();
      await new Promise((r) => setTimeout(r, 0));
    });
    expect(calls.find((c) => c.method === "PUT")?.body).toEqual({ companyId: "c2", branchId: "b2" });
    expect(view.container.querySelector('[data-testid="workplace"]')!.textContent).toBe("ALN-SHJ · SHJ-FAC");
    expect([...view.container.querySelectorAll(".workplace-chip")].map((b) => b.textContent)).toEqual(["ALN-DXB"]);
  });

  it("with more than six companies, keeps the ones switched between most recently one click away (critic p02 round 4)", async () => {
    const many = {
      companyId: "k1",
      branchId: "kb1",
      companies: Array.from({ length: 9 }, (_, i) => ({
        id: `k${i + 1}`, code: `GRP-${i + 1}`, legalNameEn: `Group entity ${i + 1} LLC`, legalNameAr: `كيان المجموعة ${i + 1} ذ.م.م`, baseCurrency: "AED",
        branches: [{ id: `kb${i + 1}`, code: "HQ", nameEn: "Head office", nameAr: "المكتب الرئيسي" }],
      })),
    };
    let current = many;
    mockFetch((method, url, body) => {
      if (url === "/api/auth/session") return { status: 200, body: session(["tenancy.workplace.read", "tenancy.workplace.switch"]) };
      if (url === "/api/tenancy/workplace" && method === "GET") return { status: 200, body: current };
      if (url === "/api/tenancy/workplace" && method === "PUT") {
        current = { ...many, ...(body as object) };
        return { status: 200, body: current };
      }
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    const chips = () => [...view!.container.querySelectorAll<HTMLButtonElement>(".workplace-chip")];
    // Nothing switched yet on this device: no arbitrary chips.
    expect(chips()).toEqual([]);

    key({ altKey: true, code: "KeyC", key: "c" });
    const filter = view.container.querySelector<HTMLInputElement>(".workplace-popover input")!;
    setInput(filter, "GRP-7");
    key({ key: "Enter" }, filter);
    await settle();
    expect(view.container.querySelector('[data-testid="workplace"]')!.textContent).toBe("GRP-7 · HQ");
    // The company left is one click away.
    expect(chips().map((b) => b.textContent)).toEqual(["GRP-1"]);
    await act(async () => {
      chips()[0]!.click();
      await new Promise((r) => setTimeout(r, 0));
    });
    expect(view.container.querySelector('[data-testid="workplace"]')!.textContent).toBe("GRP-1 · HQ");
    expect(chips().map((b) => b.textContent)).toEqual(["GRP-7"]);

    // Kept for this user on this device: a new document offers the same chips.
    view.unmount();
    view = await render(<App language="en" />);
    await settle();
    expect(chips().map((b) => b.textContent)).toEqual(["GRP-7"]);
  });

  it("is a plain label for a user who may not switch, and absent without the read permission", async () => {
    mockFetch((_, url) =>
      url === "/api/auth/session"
        ? { status: 200, body: session(["tenancy.workplace.read"], "ar") }
        : url === "/api/tenancy/workplace"
          ? { status: 200, body: workplace }
          : { status: 404, body: {} },
    );
    view = await render(<App language="ar" />);
    await settle();
    const label = view.container.querySelector('[data-testid="workplace"]')!;
    expect(label.tagName).toBe("SPAN");
    expect(label.getAttribute("title")).toContain("شركة النور للتجارة ذ.م.م");
    view.unmount();

    const calls = mockFetch((_, url) => (url === "/api/auth/session" ? { status: 200, body: session([]) } : { status: 404, body: {} }));
    view = await render(<App language="en" />);
    await settle();
    expect(view.container.querySelector('[data-testid="workplace"]')).toBeNull();
    expect(calls.some((c) => c.url === "/api/tenancy/workplace")).toBe(false);
  });
});

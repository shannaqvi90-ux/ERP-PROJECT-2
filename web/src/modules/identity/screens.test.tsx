import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { listReply } from "../../test/lists";
import { mockFetch, render, settle, setInput, submit, type Rendered } from "../../test/render";
import { App } from "../shell/App";

let view: Rendered | undefined;

afterEach(() => view?.unmount());

beforeEach(() => {
  localStorage.clear();
});

const all = [
  "identity.profile.update",
  "identity.roles.create",
  "identity.roles.delete",
  "identity.roles.read",
  "identity.roles.update",
  "identity.signIns.read",
  "identity.users.create",
  "identity.users.read",
  "identity.users.resetPassword",
  "identity.users.update",
];

const session = (permissions: string[], language = "en") => ({
  authenticated: true,
  user: { id: "me", email: "admin@demo-trading.example", displayName: "Mariam", language },
  tenant: { id: "t1", code: "demo", nameEn: "Demo Trading", nameAr: "ديمو" },
  permissions,
  menu: [
    { key: "identity.users", labelKey: "identity.menu.users", path: "/identity/users", group: "settings" },
    { key: "identity.roles", labelKey: "identity.menu.roles", path: "/identity/roles", group: "settings" },
  ],
  expiresAt: null,
});

const clerk = { id: "r-clerk", nameEn: "Contacts clerk", nameAr: "كاتب جهات الاتصال", permissions: ["identity.users.read"], isSystem: false, userCount: 0, version: 1 };
const admin = { id: "r-admin", nameEn: "Administrator", nameAr: "مدير النظام", permissions: all, isSystem: true, userCount: 1, version: 1 };
const catalogue = [
  { key: "identity.users.read", module: "identity", label: "View users", moduleLabel: "Users and access", resource: "users", action: "read", resourceLabel: "Users" },
  { key: "identity.users.create", module: "identity", label: "Create users", moduleLabel: "Users and access", resource: "users", action: "create", resourceLabel: "Users" },
  { key: "identity.users.resetPassword", module: "identity", label: "Reset passwords", moduleLabel: "Users and access", resource: "users", action: "resetPassword", resourceLabel: "Users" },
  { key: "identity.roles.read", module: "identity", label: "View roles", moduleLabel: "Users and access", resource: "roles", action: "read", resourceLabel: "Roles" },
  { key: "tenancy.tenant.read", module: "tenancy", label: "View the workspace", moduleLabel: "Workspace", resource: "tenant", action: "read", resourceLabel: "View the workspace" },
];

function key(target: EventTarget, init: KeyboardEventInit) {
  act(() => {
    target.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init }));
  });
}

describe("users screen", () => {
  it("creates an invited user from the keyboard: n, local part only, a role by filter, Ctrl+Enter, and shows the set-up code once", async () => {
    window.history.replaceState(null, "", "/identity/users");
    const calls = mockFetch((method, url, body) => {
      if (url === "/api/auth/session") return { status: 200, body: session(all) };
      const list = listReply(method, url);
      if (list) return list;
      if (url.startsWith("/api/identity/users?")) return { status: 200, body: { items: [], total: 0 } };
      if (url === "/api/identity/roles" || url.startsWith("/api/identity/roles?")) return { status: 200, body: { items: [admin, clerk], total: 2 } };
      if (method === "POST" && url === "/api/identity/users") {
        const b = body as Record<string, unknown>;
        return { status: 201, body: { id: "u-new", ...b, isActive: true, lastSignInAt: null, createdAt: "2026-10-03T00:00:00Z", version: 1, pendingSetup: true, setupCode: "K7QM-3XRA-PZ9D", setupCodeExpiresAt: "2026-10-10T00:00:00Z" } };
      }
      if (url === "/api/identity/users/u-new")
        return { status: 200, body: { id: "u-new", email: "hessa.clerk@demo-trading.example", displayName: "Hessa Clerk", language: "en", isActive: true, roleIds: ["r-clerk"], lastSignInAt: null, createdAt: "2026-10-03T00:00:00Z", version: 1, pendingSetup: true } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    await act(async () => new Promise((r) => setTimeout(r, 200)));

    key(document.body, { key: "n" });
    await settle();
    const email = view.container.querySelector<HTMLInputElement>('input[name="email"]')!;
    expect(document.activeElement).toBe(email);
    expect(view.container.textContent).toContain("@demo-trading.example");
    setInput(email, "hessa.clerk");
    act(() => email.dispatchEvent(new FocusEvent("blur")));
    act(() => email.dispatchEvent(new FocusEvent("focusout", { bubbles: true })));
    await settle();
    expect(view.container.querySelector<HTMLInputElement>('input[name="displayName"]')!.value).toBe("Hessa Clerk");

    // The Administrator role grants nothing more than the caller holds here, but filter picks the clerk.
    const filter = view.container.querySelector<HTMLInputElement>('input[aria-label="Find a role"]')!;
    setInput(filter, "clerk");
    key(filter, { key: "Enter" });
    await settle();
    key(filter, { key: "Enter", ctrlKey: true });
    await settle();
    await settle();

    const post = calls.find((c) => c.method === "POST" && c.url === "/api/identity/users")!;
    expect(post.body).toEqual({ email: "hessa.clerk@demo-trading.example", displayName: "Hessa Clerk", language: "en", roleIds: ["r-clerk"] });
    expect(view.container.querySelector('[data-testid="setup-code"]')!.textContent).toBe("K7QM-3XRA-PZ9D");
    expect(window.location.search).toBe("?open=u-new");
  });

  it("hides creation and account actions from a read-only user", async () => {
    window.history.replaceState(null, "", "/identity/users?open=u1");
    mockFetch((_m, url) => {
      if (url === "/api/auth/session") return { status: 200, body: session(["identity.users.read"]) };
      const list = listReply(_m, url);
      if (list) return list;
      if (url.startsWith("/api/identity/users?")) return { status: 200, body: { items: [], total: 0 } };
      if (url === "/api/identity/users/u1")
        return { status: 200, body: { id: "u1", email: "x@y.example", displayName: "Someone", language: "en", isActive: true, roleIds: [], lastSignInAt: null, createdAt: "2026-10-03T00:00:00Z", version: 1, pendingSetup: false } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    await settle();
    expect(view.container.textContent).toContain("Someone");
    const buttons = [...view.container.querySelectorAll("button")].map((b) => b.textContent);
    expect(buttons).not.toContain("New user");
    expect(buttons).not.toContain("Save");
    expect(buttons).not.toContain("Reset password…");
    expect(buttons).not.toContain("Sign out everywhere");
    expect(buttons).not.toContain("Sign-in history");
  });
});

describe("roles screen", () => {
  it("edits a role's permissions with search and bulk toggles, and never offers a permission the caller lacks", async () => {
    window.history.replaceState(null, "", "/identity/roles");
    const mine = all.filter((p) => p !== "identity.users.resetPassword");
    const calls = mockFetch((method, url, body) => {
      if (url === "/api/auth/session") return { status: 200, body: session(mine) };
      const list = listReply(method, url);
      if (list) return list;
      if (url === "/api/identity/roles" || url.startsWith("/api/identity/roles?")) return { status: 200, body: { items: [admin, clerk], total: 2 } };
      if (url === "/api/identity/permissions") return { status: 200, body: catalogue };
      if (method === "PUT") return { status: 200, body: { ...clerk, ...(body as object) } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    await settle();
    const row = [...view.container.querySelectorAll("tbody tr")].find((r) => r.textContent?.includes("Contacts clerk")) as HTMLElement;
    await act(async () => row.click());
    await settle();

    const reset = view.container.querySelector<HTMLInputElement>('input[aria-label="Reset passwords"]')!;
    expect(reset.disabled).toBe(true);
    // Search narrows to the roles row; the module's bulk toggle then ticks what is shown and allowed.
    setInput(view.container.querySelector<HTMLInputElement>('input[aria-label="Search permissions"]')!, "roles");
    await settle();
    expect(view.container.querySelector('input[aria-label="View users"]')).toBeNull();
    const selectShown = [...view.container.querySelectorAll("button")].find((b) => b.textContent === "Select all shown")!;
    await act(async () => selectShown.click());
    setInput(view.container.querySelector<HTMLInputElement>('input[aria-label="Search permissions"]')!, "");
    await settle();
    expect(view.container.querySelector<HTMLInputElement>('input[aria-label="View roles"]')!.checked).toBe(true);
    expect(view.container.querySelector<HTMLInputElement>('input[aria-label="View the workspace"]')!.checked).toBe(false);

    await submit(view.container.querySelector("aside")!);
    await settle();
    const put = calls.find((c) => c.method === "PUT")!;
    expect(put.url).toBe("/api/identity/roles/r-clerk");
    expect(put.body).toEqual({ nameEn: "Contacts clerk", nameAr: "كاتب جهات الاتصال", permissions: ["identity.roles.read", "identity.users.read"], version: 1 });
  });

  it("shows the Administrator system role read-only, with copy offered", async () => {
    window.history.replaceState(null, "", "/identity/roles");
    mockFetch((_m, url) => {
      if (url === "/api/auth/session") return { status: 200, body: session(all, "ar") };
      const list = listReply(_m, url);
      if (list) return list;
      if (url === "/api/identity/roles" || url.startsWith("/api/identity/roles?")) return { status: 200, body: { items: [admin, clerk], total: 2 } };
      if (url === "/api/identity/permissions") return { status: 200, body: catalogue };
      return { status: 404, body: {} };
    });
    view = await render(<App language="ar" />);
    await settle();
    await settle();
    const row = [...view.container.querySelectorAll("tbody tr")].find((r) => r.textContent?.includes("مدير النظام")) as HTMLElement;
    await act(async () => row.click());
    await settle();
    const buttons = [...view.container.querySelectorAll("aside button")].map((b) => b.textContent);
    expect(buttons).toContain("نسخ الدور");
    expect(buttons).not.toContain("حفظ");
    expect(buttons).not.toContain("حذف الدور");
    expect(view.container.querySelector<HTMLInputElement>('aside input[name="nameEn"]')!.disabled).toBe(true);
  });
});

describe("sign-in with a set-up code", () => {
  it("asks for a new password, then signs in with it", async () => {
    window.history.replaceState(null, "", "/?email=hessa.clerk%40demo-trading.example");
    const calls = mockFetch((_m, url, body) => {
      if (url === "/api/auth/session") return { status: 200, body: { authenticated: false } };
      const list = listReply(_m, url);
      if (list) return list;
      const b = body as Record<string, string>;
      if (!b.newPassword) return { status: 409, body: { title: "Choose a new password to finish signing in.", code: "auth.passwordChangeRequired" } };
      return { status: 200, body: session(["identity.users.read"]) };
    });
    view = await render(<App language="en" />);
    await settle();
    const password = view.container.querySelector<HTMLInputElement>('input[name="password"]')!;
    expect(document.activeElement).toBe(password);
    setInput(password, "K7QM-3XRA-PZ9D");
    await submit(view.container);
    await settle();
    const next = view.container.querySelector<HTMLInputElement>('input[name="newPassword"]')!;
    expect(document.activeElement).toBe(next);
    setInput(next, "Hessa-Own-Pass-1");
    setInput(view.container.querySelector<HTMLInputElement>('input[name="repeatPassword"]')!, "Different-Pass-1");
    await submit(view.container);
    expect(view.container.textContent).toContain("The two new passwords differ.");
    setInput(view.container.querySelector<HTMLInputElement>('input[name="repeatPassword"]')!, "Hessa-Own-Pass-1");
    await submit(view.container);
    await settle();
    const last = calls.filter((c) => c.url === "/api/auth/sign-in").at(-1)!;
    expect(last.body).toEqual({ email: "hessa.clerk@demo-trading.example", password: "K7QM-3XRA-PZ9D", newPassword: "Hessa-Own-Pass-1" });
    expect(view.container.textContent).toContain("Welcome");
  });
});

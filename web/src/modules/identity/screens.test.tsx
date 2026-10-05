import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { listReply } from "../../test/lists";
import { mockFetch, render, settle, setInput, submit, type Rendered } from "../../test/render";
import { App } from "../shell/App";
import { formKeys } from "./UserPanel";

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
  "identity.users.delete",
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

// G2 on screen: for every action of the roles and users screens, a user holding everything but
// that action's permission does not see that action, and still sees the others. The server
// refuses all of them anyway; this keeps the screens from offering what will be refused.
async function openRole(permissions: string[], role = clerk) {
  window.history.replaceState(null, "", "/identity/roles");
  mockFetch((m, url) => {
    if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
    const list = listReply(m, url);
    if (list) return list;
    if (url === "/api/identity/roles" || url.startsWith("/api/identity/roles?")) return { status: 200, body: { items: [admin, clerk, role], total: 3 } };
    if (url === "/api/identity/permissions") return { status: 200, body: catalogue };
    return { status: 404, body: {} };
  });
  view = await render(<App language="en" />);
  await settle();
  await settle();
  const row = [...view.container.querySelectorAll("tbody tr")].find((r) => r.textContent?.includes(role.nameEn)) as HTMLElement;
  await act(async () => row.click());
  await settle();
  return [...view.container.querySelectorAll("aside button")].map((b) => b.textContent);
}

describe("screens hide exactly what a missing permission refuses", () => {
  const roleCases: [string, string][] = [
    ["identity.roles.delete", "Delete role"],
    ["identity.roles.update", "Save"],
    ["identity.roles.create", "Copy role"],
  ];
  for (const [permission, button] of roleCases) {
    it(`roles: without ${permission} there is no "${button}", and the other role actions remain`, async () => {
      const buttons = await openRole(all.filter((x) => x !== permission));
      expect(buttons).not.toContain(button);
      for (const [other, otherButton] of roleCases) if (other !== permission) expect(buttons).toContain(otherButton);
      view?.unmount();
      view = undefined;
      expect(await openRole(all)).toContain(button);
    });
  }

  it("roles: a role granting a permission the user lacks is shown read-only, with no copy or delete", async () => {
    const strong = { ...clerk, id: "r-strong", nameEn: "Password desk", nameAr: "مكتب كلمات المرور", permissions: ["identity.users.read", "identity.users.resetPassword"] };
    const buttons = await openRole(all.filter((x) => x !== "identity.users.resetPassword"), strong);
    expect(buttons).not.toContain("Save");
    expect(buttons).not.toContain("Copy role");
    expect(buttons).not.toContain("Delete role");
    expect(view!.container.textContent).toContain("only someone who holds all of them can change, copy or delete it");
    expect(view!.container.querySelector<HTMLInputElement>('aside input[name="nameEn"]')!.disabled).toBe(true);
  });

  async function openUser(permissions: string[], target: Record<string, unknown>, roles: Record<string, unknown>[] = [admin, clerk]) {
    window.history.replaceState(null, "", `/identity/users?open=${target.id as string}`);
    mockFetch((m, url) => {
      if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
      const list = listReply(m, url);
      if (list) return list;
      if (url.startsWith("/api/identity/users?")) return { status: 200, body: { items: [], total: 0 } };
      if (url === "/api/identity/roles" || url.startsWith("/api/identity/roles?")) return { status: 200, body: { items: roles, total: roles.length } };
      if (url === `/api/identity/users/${target.id as string}`) return { status: 200, body: target };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    await settle();
    return [...view.container.querySelectorAll("aside button")].map((b) => b.textContent);
  }

  const invited = { id: "u-inv", email: "hesa.clerk@demo-trading.example", displayName: "Hesa Clerk", language: "en", isActive: true, roleIds: ["r-clerk"], lastSignInAt: null, createdAt: "2026-10-03T00:00:00Z", version: 1, pendingSetup: true };
  const userCases: [string, string][] = [
    ["identity.users.update", "Save"],
    ["identity.users.update", "Sign out everywhere"],
    ["identity.users.resetPassword", "Reset password…"],
    ["identity.users.delete", "Delete user"],
    ["identity.signIns.read", "Sign-in history"],
  ];
  for (const [permission, button] of userCases) {
    it(`users: without ${permission} there is no "${button}"`, async () => {
      const buttons = await openUser(all.filter((x) => x !== permission), invited);
      expect(buttons).not.toContain(button);
      for (const [other, otherButton] of userCases) if (other !== permission) expect(buttons).toContain(otherButton);
    });
  }

  it("users: nothing acts on someone whose roles grant more than the user holds, and nobody who signed in can be deleted", async () => {
    const stronger = { ...invited, id: "u-admin", displayName: "Mariam", roleIds: ["r-admin"], pendingSetup: false };
    let buttons = await openUser(all.filter((x) => x !== "identity.users.resetPassword"), stronger);
    for (const hidden of ["Save", "Reset password…", "Sign out everywhere", "Delete user"]) expect(buttons).not.toContain(hidden);
    expect(view!.container.textContent).toContain("only someone who holds all of them can change their account");
    view?.unmount();
    view = undefined;
    buttons = await openUser(all, { ...invited, lastSignInAt: "2026-10-02T08:00:00Z", pendingSetup: false });
    expect(buttons).not.toContain("Delete user");
    expect(buttons).toContain("Reset password…");
  });
  // Critic p03 round 3, plant U2: the screens compared only identity permissions, so someone
  // holding every identity permission was offered Save, Reset password and Delete on a workspace
  // manager whose role grants only tenancy permissions. Every permission of another module (and
  // of a module of a later wave) alone, and on top of what the user holds, makes the record read-only.
  const elsewhere = ["tenancy.tenant.read", "tenancy.tenant.update", "tenancy.access.update", "lists.views.share", "ledger.entries.post"];
  for (const permission of elsewhere) {
    it(`users: someone whose role grants ${permission} is read-only to a user holding every identity permission`, async () => {
      for (const grants of [[permission], ["identity.users.read", permission]]) {
        const role = { ...clerk, id: "r-elsewhere", nameEn: "Workspace manager", permissions: grants };
        const buttons = await openUser(all, { ...invited, id: "u-manager", displayName: "Manager", roleIds: ["r-elsewhere"] }, [admin, clerk, role]);
        for (const hidden of ["Save", "Reset password…", "Sign out everywhere", "Delete user"]) expect(buttons).not.toContain(hidden);
        expect(view!.container.textContent).toContain("only someone who holds all of them can change their account");
        view?.unmount();
        view = undefined;
      }
      // Control: holding that permission too, the same account is editable.
      const role = { ...clerk, id: "r-elsewhere", nameEn: "Workspace manager", permissions: [permission] };
      const buttons = await openUser([...all, permission], { ...invited, id: "u-manager", displayName: "Manager", roleIds: ["r-elsewhere"] }, [admin, clerk, role]);
      for (const shown of ["Save", "Reset password…", "Sign out everywhere", "Delete user"]) expect(buttons).toContain(shown);
    });

    it(`roles: a role granting ${permission} is read-only to a user holding every identity permission`, async () => {
      for (const grants of [[permission], ["identity.users.read", permission]]) {
        const buttons = await openRole(all, { ...clerk, id: "r-elsewhere", nameEn: "Workspace manager", permissions: grants });
        for (const hidden of ["Save", "Copy role", "Delete role"]) expect(buttons).not.toContain(hidden);
        expect(view!.container.textContent).toContain("only someone who holds all of them can change, copy or delete it");
        view?.unmount();
        view = undefined;
      }
      const buttons = await openRole([...all, permission], { ...clerk, id: "r-elsewhere", nameEn: "Workspace manager", permissions: [permission] });
      for (const shown of ["Save", "Copy role", "Delete role"]) expect(buttons).toContain(shown);
    });
  }
});

// G2 on screen, the list toolbars (critic p03 round 2, plant U1: New role shown without
// identity.roles.create). Every button outside the open record, for every identity permission
// taken away in turn: exactly the toolbar actions that need that permission disappear, nothing
// else does, and the new-record keys (n, Alt+N) open nothing.
async function openList(screen: "users" | "roles", permissions: string[]) {
  window.history.replaceState(null, "", `/identity/${screen}`);
  mockFetch((m, url) => {
    if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
    const list = listReply(m, url);
    if (list) return list;
    if (url.startsWith("/api/identity/users?")) return { status: 200, body: { items: [], total: 0 } };
    if (url === "/api/identity/roles" || url.startsWith("/api/identity/roles?")) return { status: 200, body: { items: [admin, clerk], total: 2 } };
    if (url === "/api/identity/permissions") return { status: 200, body: catalogue };
    return { status: 404, body: {} };
  });
  view = await render(<App language="en" />);
  await settle();
  await settle();
  const screenEl = view.container.querySelector(".id-screen");
  // Without the screen's read permission the screen itself is not offered.
  if (!screenEl) return [];
  return [...screenEl.querySelectorAll("button")].filter((b) => !b.closest("aside")).map((b) => b.textContent ?? "").sort();
}

const newRecordForm = () => view!.container.querySelector('aside input[name="email"], aside input[name="nameEn"]');

describe("list toolbars offer exactly what the user may do", () => {
  const toolbarActions: Record<"users" | "roles", Record<string, string>> = {
    users: { "New user": "identity.users.create" },
    roles: { "New role": "identity.roles.create" },
  };
  for (const screen of ["users", "roles"] as const) {
    for (const permission of all) {
      it(`${screen}: without ${permission}, exactly the toolbar actions needing it are gone`, async () => {
        const full = await openList(screen, all);
        for (const action of Object.keys(toolbarActions[screen])) expect(full).toContain(action);
        view?.unmount();
        view = undefined;
        const without = await openList(screen, all.filter((x) => x !== permission));
        const screenRead = screen === "users" ? "identity.users.read" : "identity.roles.read";
        const expected = permission === screenRead ? [] : full.filter((b) => toolbarActions[screen][b] !== permission);
        expect(without).toEqual(expected);
        if (Object.values(toolbarActions[screen]).includes(permission) || permission === screenRead) {
          key(document.body, { key: "n" });
          await settle();
          key(document.body, { key: "n", code: "KeyN", altKey: true });
          await settle();
          expect(newRecordForm()).toBeNull();
        }
      });
    }
  }

  it("roles: a read-only user sees no New role, and n or Alt+N opens nothing", async () => {
    const buttons = await openList("roles", ["identity.roles.read"]);
    expect(buttons).not.toContain("New role");
    key(document.body, { key: "n" });
    key(document.body, { key: "n", code: "KeyN", altKey: true });
    await settle();
    expect(newRecordForm()).toBeNull();
  });

  for (const screen of ["users", "roles"] as const) {
    it(`${screen}: Alt+N starts a new record while the search box the list focuses on arrival has the focus`, async () => {
      await openList(screen, all);
      const search = view!.container.querySelector<HTMLInputElement>(".list-search input")!;
      act(() => search.focus());
      // A plain n is typed into the search (the shortcut stays out of fields)…
      key(search, { key: "n", code: "KeyN" });
      await settle();
      expect(newRecordForm()).toBeNull();
      // …Alt+N opens the new record and moves the focus into it.
      key(search, { key: "n", code: "KeyN", altKey: true });
      await settle();
      await settle();
      expect(newRecordForm()).not.toBeNull();
      const button = [...view!.container.querySelectorAll("button")].find((b) => b.textContent === (screen === "users" ? "New user" : "New role"))!;
      expect(button.getAttribute("aria-keyshortcuts")).toBe("Alt+N N");
    });
  }
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

describe("identity form keys", () => {
  it("save with Ctrl+Enter or Ctrl+S, the S matched by key position so an Arabic layout saves too, and close with Escape", () => {
    const run = (init: KeyboardEventInit) => {
      let saved = 0;
      let closed = 0;
      const event = new KeyboardEvent("keydown", { cancelable: true, ...init });
      formKeys(event as unknown as Parameters<typeof formKeys>[0], () => saved++, () => closed++);
      return { saved, closed, prevented: event.defaultPrevented };
    };
    expect(run({ ctrlKey: true, key: "Enter", code: "Enter" })).toEqual({ saved: 1, closed: 0, prevented: true });
    expect(run({ ctrlKey: true, key: "s", code: "KeyS" })).toEqual({ saved: 1, closed: 0, prevented: true });
    // Arabic layout: the S key types "س".
    expect(run({ ctrlKey: true, key: "س", code: "KeyS" })).toEqual({ saved: 1, closed: 0, prevented: true });
    expect(run({ metaKey: true, key: "س", code: "KeyS" })).toEqual({ saved: 1, closed: 0, prevented: true });
    expect(run({ key: "Escape", code: "Escape" })).toEqual({ saved: 0, closed: 1, prevented: true });
    // Typing an "s", or AltGr (Ctrl+Alt) characters, never saves.
    expect(run({ key: "s", code: "KeyS" })).toEqual({ saved: 0, closed: 0, prevented: false });
    expect(run({ ctrlKey: true, altKey: true, key: "s", code: "KeyS" })).toEqual({ saved: 0, closed: 0, prevented: false });
  });
});

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

describe("users screen bulk actions", () => {
  const people = [
    { id: "me", email: "admin@demo-trading.example", displayName: "Mariam", language: "en", isActive: true, roleIds: ["r-admin"], lastSignInAt: null, createdAt: "2026-10-03T00:00:00Z", version: 3, pendingSetup: false },
    { id: "u2", email: "hessa@demo-trading.example", displayName: "Hessa", language: "ar", isActive: true, roleIds: ["r-clerk"], lastSignInAt: null, createdAt: "2026-10-02T00:00:00Z", version: 7, pendingSetup: false },
    { id: "u3", email: "omar@demo-trading.example", displayName: "Omar", language: "en", isActive: false, roleIds: [], lastSignInAt: null, createdAt: "2026-10-01T00:00:00Z", version: 2, pendingSetup: false },
  ];

  async function showUsers(
    permissions: string[],
    put: (url: string) => { status: number; body: unknown },
    more: { total?: number; address?: string; post?: (url: string, body: unknown) => { status: number; body: unknown } } = {},
  ) {
    window.history.replaceState(null, "", more.address ?? "/identity/users");
    const calls = mockFetch((method, url, body) => {
      if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
      const list = listReply(method, url);
      if (list) return list;
      if (method === "GET" && url.startsWith("/api/identity/users?"))
        return { status: 200, body: { items: people, total: more.total ?? people.length, next: more.total ? "cursor" : null, groups: null } };
      if (url === "/api/identity/roles" || url.startsWith("/api/identity/roles?")) return { status: 200, body: { items: [admin, clerk], total: 2 } };
      if (method === "PUT") return put(url);
      if (method === "POST" && more.post) return more.post(url, body);
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    await act(async () => new Promise((r) => setTimeout(r, 200)));
    return calls;
  }

  const grid = () => view!.container.querySelector<HTMLElement>('[role="grid"]')!;
  const barButton = (label: string) => [...view!.container.querySelectorAll<HTMLButtonElement>(".list-selectionbar button")].find((b) => b.textContent === label);

  it("deactivates the chosen users from the keyboard selection, skips those already inactive and reports what the API refused", async () => {
    const calls = await showUsers(all, (url) =>
      url.endsWith("/me")
        ? { status: 403, body: { type: "about:blank", title: "Forbidden", status: 403, code: "identity.cannotChangeOwnAccess" } }
        : { status: 200, body: { ...people[1], isActive: false, version: 8 } },
    );
    act(() => grid().focus());
    key(grid(), { key: "a", ctrlKey: true });
    await settle();
    expect(view!.container.textContent).toContain("3 selected");
    const deactivate = barButton("Deactivate")!;
    expect(deactivate).toBeTruthy();
    await act(async () => deactivate.click());
    await settle();
    await settle();

    const puts = calls.filter((c) => c.method === "PUT");
    expect(puts.map((c) => c.url).sort()).toEqual(["/api/identity/users/me", "/api/identity/users/u2"]);
    // The same change the user panel saves: name, language, roles and version kept, only the state changed.
    expect(puts.find((c) => c.url.endsWith("/u2"))!.body).toEqual({ displayName: "Hessa", language: "ar", isActive: false, roleIds: ["r-clerk"], version: 7 });
    expect(view!.container.textContent).toContain("1 user changed.");
    expect(view!.container.textContent).toContain("1 user was not changed");
    expect(view!.container.querySelector(".list-selectionbar")).toBeNull();
  });

  it("activates only inactive users", async () => {
    const calls = await showUsers(all, () => ({ status: 200, body: { ...people[2], isActive: true, version: 3 } }));
    act(() => grid().focus());
    key(grid(), { key: "a", ctrlKey: true });
    await settle();
    await act(async () => barButton("Activate")!.click());
    await settle();
    await settle();
    const puts = calls.filter((c) => c.method === "PUT");
    expect(puts.map((c) => c.url)).toEqual(["/api/identity/users/u3"]);
    expect(view!.container.textContent).toContain("1 user changed.");
    expect(view!.container.textContent).not.toContain("was not changed");
  });

  describe("on all that match", () => {
    const realConfirm = window.confirm;
    afterEach(() => {
      window.confirm = realConfirm;
    });

    async function selectAllMatching(post: (url: string, body: unknown) => { status: number; body: unknown }, confirmed: boolean) {
      const asked: string[] = [];
      window.confirm = (message?: string) => {
        asked.push(String(message));
        return confirmed;
      };
      const calls = await showUsers(all, () => ({ status: 500, body: {} }), { total: 3265, address: "/identity/users?q=pillai&filter=language%20eq%20'en'", post });
      act(() => grid().focus());
      key(grid(), { key: "a", ctrlKey: true });
      await settle();
      key(grid(), { key: "a", ctrlKey: true });
      await settle();
      expect(view!.container.textContent).toContain("All 3,265 matching rows selected");
      return { calls, asked };
    }

    it("deactivates every matching user in one request with the list's search, filter and count, after a confirmation", async () => {
      const { calls, asked } = await selectAllMatching(() => ({ status: 200, body: { matched: 3265, changed: 3200, unchanged: 63, refusedSelf: 1, refusedBeyondOwn: 1 } }), true);
      const deactivate = barButton("Deactivate")!;
      expect(deactivate.disabled).toBe(false);
      await act(async () => deactivate.click());
      await settle();
      await settle();
      expect(asked).toEqual(["Deactivate all 3,265 matching users? They will no longer be able to sign in."]);
      const posts = calls.filter((c) => c.method === "POST");
      expect(posts.map((c) => c.url)).toEqual(["/api/identity/users/matching/active"]);
      expect(posts[0]!.body).toEqual({ active: false, search: "pillai", filter: "language eq 'en'", expectedCount: 3265 });
      // Never one request per row.
      expect(calls.filter((c) => c.method === "PUT")).toEqual([]);
      expect(view!.container.textContent).toContain("3,200 users changed.");
      expect(view!.container.textContent).toContain("63 users needed no change.");
      expect(view!.container.textContent).toContain("2 users were not changed");
      expect(view!.container.querySelector(".list-selectionbar")).toBeNull();
    });

    it("changes nothing and keeps the selection when the confirmation is cancelled", async () => {
      const { calls, asked } = await selectAllMatching(() => ({ status: 200, body: {} }), false);
      await act(async () => barButton("Activate")!.click());
      await settle();
      expect(asked).toEqual(["Activate all 3,265 matching users?"]);
      expect(calls.filter((c) => c.method === "POST" || c.method === "PUT")).toEqual([]);
      expect(view!.container.textContent).toContain("All 3,265 matching rows selected");
    });

    it("shows the API's reason when the matching rows changed meanwhile", async () => {
      await selectAllMatching(
        () => ({ status: 409, body: { type: "urn:erp:problem:list.matchingChanged", title: "The rows that match changed since you chose them (3265 then, 3266 now). Nothing was changed: look at the list again and retry.", status: 409, code: "list.matchingChanged" } }),
        true,
      );
      await act(async () => barButton("Deactivate")!.click());
      await settle();
      await settle();
      expect(view!.container.textContent).toContain("The rows that match changed since you chose them");
    });
  });

  it("offers no state change to a user who may not update users", async () => {
    await showUsers(["identity.users.read"], () => ({ status: 403, body: {} }));
    act(() => grid().focus());
    key(grid(), { key: "a", ctrlKey: true });
    await settle();
    expect(barButton("Copy")).toBeTruthy();
    expect(barButton("Deactivate")).toBeUndefined();
    expect(barButton("Activate")).toBeUndefined();
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
    // The role opened is listed once, whether it is the clerk or another role.
    const items = [admin, clerk, role].filter((r, i, all) => all.findIndex((x) => x.id === r.id) === i);
    if (url === "/api/identity/roles" || url.startsWith("/api/identity/roles?")) return { status: 200, body: { items, total: items.length } };
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

describe("my account", () => {
  it("shows the team's sign-in address for the user's own e-mail domain, in both languages", async () => {
    for (const language of ["en", "ar"] as const) {
      window.history.replaceState(null, "", "/identity/me");
      mockFetch((_m, url) => {
        if (url === "/api/auth/session") return { status: 200, body: session(all, language) };
        return listReply(_m, url) ?? { status: 404, body: {} };
      });
      view = await render(<App language={language} />);
      await settle();
      const address = view.container.querySelector('[data-testid="team-address"]')!;
      expect(address.textContent).toBe(`${window.location.origin}/?domain=demo-trading.example`);
      expect(address.getAttribute("dir")).toBe("ltr");
      expect(view.container.textContent).toContain(language === "en" ? "Team sign-in address" : "عنوان تسجيل الدخول لفريقك");
      view.unmount();
      view = undefined;
    }
  });

  it("changes the password with the form keys and shows each refusal on its field, linked to the input", async () => {
    window.history.replaceState(null, "", "/identity/me");
    const calls = mockFetch((method, url) => {
      if (url === "/api/auth/session") return { status: 200, body: session(["identity.profile.update"]) };
      if (method === "POST" && url === "/api/auth/sign-in")
        return { status: 400, body: { title: "The request is not valid.", status: 400, errors: { newPassword: [{ code: "passwordReused", message: "Choose a password you have not used before." }] } } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    await act(async () => new Promise((r) => setTimeout(r, 50)));

    const input = (name: string) => view!.container.querySelector<HTMLInputElement>(`form input[name="${name}"]`)!;
    const message = (name: string) => {
      const el = input(name);
      const ids = (el.getAttribute("aria-describedby") ?? "").split(" ").filter(Boolean);
      return ids.map((id) => document.getElementById(id)?.textContent ?? "").join(" ");
    };
    expect(view.container.querySelector('label[for="' + input("current").id + '"]')?.textContent).toBe("Current password");

    // Ctrl+Enter submits; the local checks speak on their fields.
    setInput(input("new"), "short");
    key(input("new"), { key: "Enter", ctrlKey: true });
    await settle();
    expect(message("current")).toBe("Enter your current password.");
    expect(input("current").getAttribute("aria-invalid")).toBe("true");
    expect(message("new")).toContain("10");
    expect(calls.some((c) => c.url === "/api/auth/sign-in")).toBe(false);

    // The server's refusal of the new password lands on the new password's field (Ctrl+S, by key position).
    setInput(input("current"), "Old-Pass-2026");
    setInput(input("new"), "Demo-Pass-2026");
    setInput(input("repeat"), "Demo-Pass-2026");
    key(input("repeat"), { key: "س", code: "KeyS", ctrlKey: true });
    await settle();
    await settle();
    expect(calls.filter((c) => c.method === "POST" && c.url === "/api/auth/sign-in")).toHaveLength(1);
    expect(message("new")).toBe("Choose a password you have not used before.");
    expect(message("current")).toBe("");
  });
});

// Roles per company on screen (p03 round 4): the user panel lists the roles a person holds in one
// company, adds and removes them, saves them with the record, sets where the person starts work,
// and the access view says in which company each permission counts.
describe("roles in one company and the default company", () => {
  const dxb = { id: "c-dxb", code: "DXB", legalNameEn: "Dubai Trading LLC", legalNameAr: "دبي للتجارة ذ.م.م" };
  const jafza = { id: "c-jfz", code: "JFZ", legalNameEn: "Jebel Ali FZE", legalNameAr: "جبل علي م.م.ح" };
  const target = { id: "u-acc", email: "hessa@demo-trading.example", displayName: "Hessa", language: "en", isActive: true, roleIds: [], lastSignInAt: null, createdAt: "2026-10-03T00:00:00Z", version: 7, pendingSetup: false, companyRoles: [{ roleId: "r-clerk", companyId: "c-dxb" }], rolesElsewhere: false };

  async function openPanel(permissions: string[], user: Record<string, unknown>, extra?: (m: string, url: string, body: unknown) => { status: number; body: unknown } | undefined) {
    window.history.replaceState(null, "", `/identity/users?open=${user.id as string}`);
    const calls = mockFetch((m, url, body) => {
      const special = extra?.(m, url, body);
      if (special) return special;
      if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
      const list = listReply(m, url);
      if (list) return list;
      if (url.startsWith("/api/identity/users?")) return { status: 200, body: { items: [], total: 0 } };
      if (url === "/api/identity/roles" || url.startsWith("/api/identity/roles?")) return { status: 200, body: { items: [admin, clerk], total: 2 } };
      if (url === "/api/identity/companies") return { status: 200, body: [dxb, jafza] };
      if (url === `/api/identity/users/${user.id as string}/default-company` && m === "GET") return { status: 200, body: { userId: user.id, companyId: null, companies: [dxb, jafza], version: 0 } };
      if (url === `/api/identity/users/${user.id as string}` && m === "GET") return { status: 200, body: user };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    await settle();
    return calls;
  }

  it("lists, adds and saves roles in one company, and the company the person starts in", async () => {
    const calls = await openPanel(all, target, (m, url, body) => {
      if (m === "PUT" && url === "/api/identity/users/u-acc") return { status: 200, body: { ...target, ...(body as object), version: 8 } };
      if (m === "PUT" && url === "/api/identity/users/u-acc/default-company") return { status: 200, body: { userId: "u-acc", companyId: "c-jfz", companies: [dxb, jafza], version: 3 } };
      return undefined;
    });
    const aside = view!.container.querySelector("aside")!;
    const table = aside.querySelector(".id-company-roles-table")!;
    expect(table.textContent).toContain("DXB · Dubai Trading LLC");
    expect(table.textContent).toContain("Contacts clerk");
    // Add the clerk role in JAFZA from the add line.
    const company = aside.querySelector<HTMLSelectElement>('select[name="companyRoleCompany"]')!;
    await act(async () => {
      company.value = "c-jfz";
      company.dispatchEvent(new Event("change", { bubbles: true }));
    });
    const role = aside.querySelector<HTMLSelectElement>('select[name="companyRoleRole"]')!;
    await act(async () => {
      role.value = "r-clerk";
      role.dispatchEvent(new Event("change", { bubbles: true }));
    });
    const add = [...aside.querySelectorAll("button")].find((b) => b.textContent === "Add")!;
    await act(async () => add.click());
    expect(aside.querySelector(".id-company-roles-table")!.textContent).toContain("JFZ · Jebel Ali FZE");
    // Start work in JAFZA.
    const starts = aside.querySelector<HTMLSelectElement>('select[name="defaultCompany"]')!;
    await act(async () => {
      starts.value = "c-jfz";
      starts.dispatchEvent(new Event("change", { bubbles: true }));
    });
    await submit(aside);
    await settle();
    const put = calls.find((c) => c.method === "PUT" && c.url === "/api/identity/users/u-acc")!;
    expect((put.body as { companyRoles: unknown[] }).companyRoles).toEqual([
      { roleId: "r-clerk", companyId: "c-dxb" },
      { companyId: "c-jfz", roleId: "r-clerk" },
    ]);
    const start = calls.find((c) => c.method === "PUT" && c.url === "/api/identity/users/u-acc/default-company")!;
    expect(start.body).toEqual({ companyId: "c-jfz", version: 0 });
  });

  it("removes a role in one company by keyboard-reachable button and sends no company roles when they did not change", async () => {
    const calls = await openPanel(all, target, (m, url, body) =>
      m === "PUT" && url === "/api/identity/users/u-acc" ? { status: 200, body: { ...target, ...(body as object), version: 8 } } : undefined,
    );
    const aside = view!.container.querySelector("aside")!;
    await submit(aside);
    await settle();
    expect(calls.find((c) => c.method === "PUT")!.body).not.toHaveProperty("companyRoles");
    const remove = aside.querySelector<HTMLButtonElement>('button[aria-label="Remove DXB · Dubai Trading LLC · Contacts clerk"]')!;
    await act(async () => remove.click());
    expect(aside.querySelector(".id-company-roles-table")).toBeNull();
    await submit(aside);
    await settle();
    expect(calls.filter((c) => c.method === "PUT").at(-1)!.body).toHaveProperty("companyRoles", []);
  });

  it("shows read-only, with the reason, someone holding roles in companies the user does not work in", async () => {
    await openPanel(all, { ...target, companyRoles: [], rolesElsewhere: true });
    const buttons = [...view!.container.querySelectorAll("aside button")].map((b) => b.textContent);
    for (const hidden of ["Save", "Reset password…", "Sign out everywhere", "Add"]) expect(buttons).not.toContain(hidden);
    expect(view!.container.textContent).toContain("also holds roles in companies you do not work in");
    expect(view!.container.querySelector<HTMLSelectElement>('select[name="defaultCompany"]')!.disabled).toBe(true);
  });

  it("shows read-only someone whose role in one company grants a permission the user lacks", async () => {
    await openPanel(all.filter((p) => p !== "identity.users.resetPassword"), { ...target, companyRoles: [{ roleId: "r-admin", companyId: "c-dxb" }] });
    const buttons = [...view!.container.querySelectorAll("aside button")].map((b) => b.textContent);
    for (const hidden of ["Save", "Reset password…", "Sign out everywhere", "Remove"]) expect(buttons).not.toContain(hidden);
    expect(view!.container.textContent).toContain("only someone who holds all of them can change their account");
  });

  it("says in which company each permission counts", async () => {
    await openPanel(all, target, (_m, url) =>
      url === "/api/identity/users/u-acc/access"
        ? {
            status: 200,
            body: {
              userId: "u-acc",
              roles: [{ id: "r-clerk", nameEn: "Contacts clerk", nameAr: "كاتب جهات الاتصال", isSystem: false, companyId: "c-dxb" }],
              permissions: [{ key: "identity.users.read", module: "identity", label: "View users", moduleLabel: "Users and access", grantedBy: ["r-clerk"], grants: [{ roleId: "r-clerk", companyId: "c-dxb" }] }],
              companies: [dxb],
              rolesElsewhere: true,
            },
          }
        : undefined,
    );
    const tab = [...view!.container.querySelectorAll('aside [role="tab"]')].find((b) => b.textContent === "What they can do") as HTMLElement;
    await act(async () => tab.click());
    await settle();
    const text = view!.container.querySelector(".id-access")!.textContent!;
    expect(text).toContain("Contacts clerk (only in DXB · Dubai Trading LLC)");
    expect(text).toContain("count only while working in that company");
    expect(text).toContain("also holds roles in companies you do not work in");
  });
});

describe("the session follows the working company", () => {
  it("re-reads permissions and menu when the working company changes, so a role held in one company shows and hides its screens", async () => {
    window.history.replaceState(null, "", "/identity/users");
    let sessions = 0;
    mockFetch((m, url) => {
      if (url === "/api/auth/session") {
        sessions++;
        // In the second company the roles screen is not granted.
        return { status: 200, body: sessions === 1 ? session(all) : { ...session(all.filter((p) => !p.startsWith("identity.roles."))), menu: [session(all).menu[0]] } };
      }
      const list = listReply(m, url);
      if (list) return list;
      if (url.startsWith("/api/identity/users?")) return { status: 200, body: { items: [], total: 0 } };
      if (url === "/api/identity/roles" || url.startsWith("/api/identity/roles?")) return { status: 200, body: { items: [admin, clerk], total: 2 } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    const links = () => [...view!.container.querySelectorAll("nav a")].map((a) => a.getAttribute("href"));
    expect(links()).toContain("/identity/roles");
    await act(async () => {
      window.dispatchEvent(new CustomEvent("erp:workplace-changed", { detail: { companyId: "c-jfz" } }));
    });
    await settle();
    expect(sessions).toBe(2);
    expect(links()).not.toContain("/identity/roles");
  });
});

import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { recordInAddress } from "../../kernel/router";
import { listReply } from "../../test/lists";
import { mockFetch, render, settle, type Rendered } from "../../test/render";
import { type Call, settleUntilQuiet, sweepKeys, sweepTimeLimit } from "../../test/keySweep";
import { App } from "../shell/App";

// G2 on screen for the users and roles screens, by keyboard (critic p06 round 2, plant W2: a
// read-only user's Ctrl+S sent a write while every screen gate passed, because the gates only looked
// at the controls drawn). The whole app is shown to a user who may read users, roles and sign-ins
// and change nothing. On each screen, with a record open and on the list alone, and again with every
// row of the list chosen (where the bulk actions appear), every key a keyboard has is pressed alone
// and with every modifier, whatever a key opens is accepted, and every control the keyboard reaches
// is activated: no request other than a read leaves (the user's own settings and list views
// excepted), and no key opens a new user or role. scripts/forms-plant-self-test.mjs plants faults
// that this file must catch.

let view: Rendered | undefined;
let calls: Call[] = [];

beforeEach(() => localStorage.clear());
afterEach(() => closeView());

function closeView() {
  view?.unmount();
  view = undefined;
}

const reads = ["identity.roles.read", "identity.signIns.read", "identity.users.read"];

const session = (permissions: string[]) => ({
  authenticated: true,
  user: { id: "me", email: "viewer@demo-trading.example", displayName: "Huda Viewer", language: "en" },
  tenant: { id: "t1", code: "demo", nameEn: "Demo Trading", nameAr: "ديمو" },
  permissions,
  menu: [
    { key: "identity.users", labelKey: "identity.menu.users", path: "/identity/users", group: "settings" },
    { key: "identity.roles", labelKey: "identity.menu.roles", path: "/identity/roles", group: "settings" },
  ],
  expiresAt: null,
});

const clerk = { id: "r-clerk", nameEn: "Contacts clerk", nameAr: "كاتب جهات الاتصال", permissions: ["identity.users.read"], isSystem: false, userCount: 1, version: 1 };
const admin = { id: "r-admin", nameEn: "Administrator", nameAr: "مدير النظام", permissions: reads, isSystem: true, userCount: 1, version: 1 };
const catalogue = [
  { key: "identity.users.read", module: "identity", label: "View users", moduleLabel: "Users and access", resource: "users", action: "read", resourceLabel: "Users" },
  { key: "identity.users.update", module: "identity", label: "Change users", moduleLabel: "Users and access", resource: "users", action: "update", resourceLabel: "Users" },
  { key: "identity.roles.read", module: "identity", label: "View roles", moduleLabel: "Users and access", resource: "roles", action: "read", resourceLabel: "Roles" },
];
const user = {
  id: "u1", email: "clerk@demo-trading.example", displayName: "Salem Clerk", language: "en", isActive: true, roleIds: ["r-clerk"],
  lastSignInAt: "2026-10-03T08:00:00Z", createdAt: "2026-10-01T00:00:00Z", version: 4, pendingSetup: false,
};
const access = {
  userId: "u1",
  roles: [{ id: "r-clerk", nameEn: "Contacts clerk", nameAr: "كاتب جهات الاتصال", isSystem: false }],
  permissions: [{ key: "identity.users.read", module: "identity", label: "View users", moduleLabel: "Users and access", grantedBy: ["r-clerk"] }],
};
const signIns = {
  items: [{ id: "s1", occurredAt: "2026-10-03T08:00:00Z", outcome: "succeeded", ipAddress: "10.0.0.1", userAgent: "Firefox", sessionActive: true }],
  total: 1,
  paused: [{ source: "10.0.0.9", until: "2026-10-03T09:00:00Z", failures: 5 }],
};

async function open(path: string, permissions: string[]): Promise<Call[]> {
  closeView();
  window.history.replaceState(null, "", path);
  calls = mockFetch((method, url) => {
    const p = new URL(url, "http://localhost").pathname;
    if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
    const list = listReply(method, url);
    if (list) return list;
    if (method !== "GET") return { status: 403, body: { code: "forbidden", title: "Not allowed." } };
    if (p === "/api/identity/users") return { status: 200, body: { items: [user], total: 1, next: null } };
    if (p === "/api/identity/users/u1") return { status: 200, body: user };
    if (p === "/api/identity/users/u1/access") return { status: 200, body: access };
    if (p === "/api/identity/users/u1/sign-ins") return { status: 200, body: signIns };
    if (p === "/api/identity/roles") return { status: 200, body: { items: [admin, clerk], total: 2, next: null } };
    if (p === "/api/identity/roles/r-clerk") return { status: 200, body: clerk };
    if (p === "/api/identity/permissions") return { status: 200, body: catalogue };
    return { status: 404, body: {} };
  });
  view = await render(<App language="en" />);
  await settleUntilQuiet(() => calls, () => view?.container.querySelector("main [role=grid], main .record-header") != null);
  return calls;
}

const here = () => window.location.pathname + window.location.search;
const grid = () => view?.container.querySelector<HTMLElement>("main [role=grid]") ?? null;

/** Every row of the list chosen, as Ctrl+A on the list does: the bulk actions appear. */
const allRowsChosen = {
  name: "every row chosen",
  enter: () => {
    const g = grid();
    if (!g || view!.container.querySelector(".list-selectionbar")) return;
    g.focus();
    act(() => {
      g.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ctrlKey: true, key: "a", code: "KeyA" }));
    });
  },
};

async function sweep(path: string, list: boolean) {
  await open(path, reads);
  expect(view!.container.querySelector("main"), `${path} shows its screen`).not.toBeNull();
  if (list) expect(grid(), `${path} shows its list`).not.toBeNull();
  return sweepKeys({
    calls,
    shown: () => here() === path && view?.container.querySelector("main") !== null,
    reopen: () => open(path, reads),
    targets: [() => view!.container.querySelector<HTMLElement>("main .record-header h2, main [role=grid], main h1") ?? null],
    states: list ? [allRowsChosen] : [],
    forbidden: () => (recordInAddress() === "new" || view?.container.querySelector('input[name="email"]:not([disabled])') ? "a new record offered to a user who may not create one" : null),
  });
}

describe("users and roles screens by keyboard, for a user who may read them and change nothing", () => {
  for (const [path, list] of [
    ["/identity/users?open=u1", false],
    ["/identity/roles?open=r-clerk", false],
    ["/identity/users", true],
    ["/identity/roles", true],
  ] as const) {
    it(`${path}: no key, and no control the keyboard reaches, sends anything but reads or opens a new record`, async () => {
      expect(await sweep(path, list)).toEqual([]);
    }, sweepTimeLimit);
  }

  it("the control: every row chosen shows the selection bar, and an editor's sweep finds the bulk deactivation", async () => {
    await open("/identity/users", reads);
    await allRowsChosen.enter();
    await settle();
    expect(view!.container.querySelector(".list-selectionbar")).not.toBeNull();
    const path = "/identity/users";
    const editor = [...reads, "identity.users.update"];
    await open(path, editor);
    const found = await sweepKeys({
      exhaustive: true,
      calls,
      shown: () => here() === path,
      reopen: () => open(path, editor),
      targets: [],
      states: [allRowsChosen],
    });
    expect(found.some((w) => w.method === "PUT" && w.url === "/api/identity/users/u1")).toBe(true);
  }, sweepTimeLimit);
});

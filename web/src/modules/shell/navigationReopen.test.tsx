import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { navigate } from "../../kernel/router";
import { listReply } from "../../test/lists";
import { mockFetch, render, settle, type Rendered } from "../../test/render";
import { App } from "./App";

/**
 * Every navigation opens the screen on its address, the same screen included (critic p04 round
 * 11): the palette opening a second user from the first user's record changed the address only,
 * the first user's search and record stayed on screen, and Ctrl+S then saved the first user. The
 * same with G then U from a user record, and on Roles. The shell keys the screen by the navigation
 * (kernel/router useNavigationKey), so the screen reads the new address as it opens.
 */

const latifa = { id: "0196a000-0000-7000-8000-000000000011", email: "latifa.alsuwaidi@alnoor.example", displayName: "Latifa Al Suwaidi" };
const mariam = { id: "0196a000-0000-7000-8000-000000000012", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori" };
const readOnly = { id: "0196a000-0000-7000-8000-000000000021", nameEn: "Read-only", nameAr: "قراءة فقط" };
const staff = { id: "0196a000-0000-7000-8000-000000000022", nameEn: "Staff", nameAr: "موظفون" };
const users = [latifa, mariam];
const roles = [readOnly, staff];

const menu = [
  { key: "identity.users", labelKey: "identity.menu.users", path: "/identity/users", group: "settings" },
  { key: "identity.roles", labelKey: "identity.menu.roles", path: "/identity/roles", group: "settings" },
];

/** Holds the list definitions back until released (a busy machine: the record opens first). */
let holdDefinitions: Promise<void> | null = null;

function serve(language: "en" | "ar") {
  const session = {
    authenticated: true,
    user: { ...mariam, language, numerals: "latn" },
    tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
    permissions: ["identity.profile.update", "identity.users.read", "identity.users.update", "identity.roles.read", "identity.roles.update"],
    menu,
    expiresAt: "2026-10-03T09:00:00Z",
  };
  const row = (u: (typeof users)[number]) => ({ ...u, language: "en", isActive: true, roleIds: [], lastSignInAt: null, createdAt: "2026-10-01T08:00:00Z" });
  const roleRow = (r: (typeof roles)[number]) => ({ ...r, isSystem: false, userCount: 1, permissions: [] });
  return mockFetch((method, url) => {
    const path = url.split("?")[0]!;
    const query = new URLSearchParams(url.split("?")[1] ?? "");
    if (url === "/api/auth/session") return { status: 200, body: session };
    const list = listReply(method, url);
    if (list && holdDefinitions && url.includes("/definition")) return holdDefinitions.then(() => list);
    if (list) return list;
    const user = /^\/api\/identity\/users\/([^/]+)(\/[a-z-]+)?$/.exec(path);
    if (user) {
      const found = users.find((u) => u.id === user[1]);
      if (!found) return { status: 404, body: { title: "Not found", code: "identity.userNotFound" } };
      if (user[2] === "/access") return { status: 200, body: { roles: [], permissions: [] } };
      if (user[2] === "/sign-ins") return { status: 200, body: { items: [], blocked: false } };
      return { status: 200, body: { ...row(found), version: 1, displayNameAr: null } };
    }
    if (path === "/api/identity/users") {
      const search = (query.get("search") ?? "").toLowerCase();
      const items = users.filter((u) => !search || u.displayName.toLowerCase().includes(search) || u.email.includes(search)).map(row);
      return { status: 200, body: { items, total: items.length } };
    }
    const role = /^\/api\/identity\/roles\/([^/]+)$/.exec(path);
    if (role) {
      const found = roles.find((r) => r.id === role[1]);
      return found ? { status: 200, body: { ...roleRow(found), version: 1 } } : { status: 404, body: { title: "Not found", code: "identity.roleNotFound" } };
    }
    if (path === "/api/identity/roles") return { status: 200, body: { items: roles.map(roleRow), total: roles.length } };
    if (path === "/api/identity/permissions") return { status: 200, body: [] };
    return { status: 404, body: { title: "Not found", code: "http.404" } };
  });
}

let view: Rendered | undefined;

beforeEach(() => {
  localStorage.clear();
  window.history.replaceState(null, "", "/");
});
afterEach(() => {
  view?.unmount();
  view = undefined;
});

async function wait(ms: number) {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, ms));
  });
}

async function go(path: string) {
  act(() => navigate(path));
  await wait(250);
}

/** The values of the inputs in the screen's record panel (empty when no record is open). */
const recordValues = () => [...document.querySelectorAll<HTMLInputElement>("main form input")].map((i) => i.value);
const searchBox = () => document.querySelector<HTMLInputElement>('main input[type="search"]');

describe.each(["en", "ar"] as const)("a navigation to the screen on show opens it on the new address (%s)", (language) => {
  it("opens the second user from the first user's record, with its own search", async () => {
    serve(language);
    view = await render(<App language={language} />);
    await settle();
    await go(`/identity/users/${latifa.id}?q=latifa`);
    expect(searchBox()?.value).toBe("latifa");
    expect(recordValues()).toContain(latifa.displayName);

    // The palette opens a record by its address, as here.
    await go(`/identity/users/${mariam.id}?q=${encodeURIComponent(mariam.email)}`);
    expect(window.location.pathname).toBe(`/identity/users/${mariam.id}`);
    expect(searchBox()?.value).toBe(mariam.email);
    expect(recordValues()).toContain(mariam.displayName);
    expect(recordValues()).not.toContain(latifa.displayName);

    // G then U from a record: the list, nothing open, no search.
    await go("/identity/users");
    expect(searchBox()?.value).toBe("");
    expect(recordValues()).toEqual([]);

    // Back returns to the record the address names.
    await act(async () => {
      window.history.back();
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
    await wait(250);
    expect(window.location.pathname).toBe(`/identity/users/${mariam.id}`);
    expect(recordValues()).toContain(mariam.displayName);
  });

  it("opens the second role from the first role's record", async () => {
    serve(language);
    view = await render(<App language={language} />);
    await settle();
    await go(`/identity/roles/${readOnly.id}`);
    expect(recordValues()).toContain(readOnly.nameEn);
    await go(`/identity/roles/${staff.id}`);
    expect(recordValues()).toContain(staff.nameEn);
    expect(recordValues()).not.toContain(readOnly.nameEn);
  });
});

it("a record closed before its list's definition arrives stays closed when it does", async () => {
  let release: () => void = () => {};
  holdDefinitions = new Promise<void>((resolve) => (release = resolve));
  try {
    serve("en");
    view = await render(<App language="en" />);
    await settle();
    await go(`/identity/users/${latifa.id}?q=latifa`);
    expect(recordValues()).toContain(latifa.displayName);
    // Escape in the record panel closes it, before the list knows its columns.
    const panel = document.querySelector<HTMLElement>("main aside")!;
    act(() => {
      panel.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", code: "Escape", bubbles: true, cancelable: true }));
    });
    await wait(50);
    expect(recordValues()).toEqual([]);
    expect(window.location.pathname).toBe("/identity/users");
    release();
    await wait(250);
    expect(recordValues()).toEqual([]);
    expect(window.location.pathname).toBe("/identity/users");
    expect(searchBox()?.value).toBe("latifa");
  } finally {
    release();
    holdDefinitions = null;
  }
});

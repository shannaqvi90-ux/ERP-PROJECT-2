import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, settle, setInput, type Rendered } from "../../test/render";
import { App } from "./App";

let view: Rendered | undefined;

beforeEach(() => {
  localStorage.clear();
  window.history.replaceState(null, "", "/");
});
afterEach(() => {
  view?.unmount();
  view = undefined;
});

const admin = {
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori", language: "en", numerals: "latn" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions: ["identity.profile.update", "identity.roles.read", "identity.users.read", "tenancy.tenant.read"],
  menu: [
    { key: "identity.users", labelKey: "identity.menu.users", path: "/identity/users", group: "settings" },
    { key: "identity.roles", labelKey: "identity.menu.roles", path: "/identity/roles", group: "settings" },
    { key: "tenancy.tenant", labelKey: "tenancy.menu.tenant", path: "/tenancy/tenant", group: "settings" },
  ],
  expiresAt: "2026-10-03T09:00:00Z",
};

const nobody = { ...admin, user: { ...admin.user, id: "u9", displayName: "Layla Nasser" }, permissions: [], menu: [] };

type Session = typeof admin;

function serve(session: Session, extra?: (method: string, url: string, body: unknown) => { status: number; body?: unknown } | undefined) {
  return mockFetch((method, url, body) => {
    const custom = extra?.(method, url, body);
    if (custom) return custom;
    if (url === "/api/auth/session") return { status: 200, body: session };
    if (method === "PUT" && url === "/api/identity/me/preferences") return { status: 200, body: { ...session.user, ...(body as object) } };
    if (url.startsWith("/api/identity/users")) return { status: 200, body: { items: [{ id: "u2", email: "omar@alnoor.example", displayName: "Omar Haddad", language: "en", isActive: true, roleIds: [], lastSignInAt: null }], total: 1 } };
    if (url.startsWith("/api/identity/roles")) return { status: 200, body: { items: [], total: 0 } };
    if (url.startsWith("/api/tenancy/tenant")) return { status: 200, body: { id: "t1", code: "alnoor", nameEn: "Al Noor", nameAr: "النور", status: "active" } };
    return { status: 404, body: {} };
  });
}

function press(init: KeyboardEventInit & { code: string }, target: EventTarget = document.activeElement ?? document.body) {
  act(() => {
    target.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init }));
  });
}

const palette = () => document.querySelector<HTMLElement>('[role="dialog"].palette');
const options = () => [...document.querySelectorAll<HTMLElement>('[role="option"]')].map((o) => o.querySelector(".palette-option-title")!.textContent);

async function openPalette() {
  press({ code: "KeyK", key: "k", ctrlKey: true });
  await settle();
  expect(palette()).not.toBeNull();
  return palette()!.querySelector<HTMLInputElement>('input[role="combobox"]')!;
}

describe("command palette", () => {
  it("opens with Ctrl+K, finds a screen as the user types, and opens it with Enter", async () => {
    serve(admin);
    view = await render(<App language="en" />);
    await settle();
    const input = await openPalette();
    expect(document.activeElement).toBe(input);
    setInput(input, "rol");
    await settle();
    expect(options()[0]).toBe("Roles");
    press({ code: "Enter", key: "Enter" }, input);
    await settle();
    expect(window.location.pathname).toBe("/identity/roles");
    expect(palette()).toBeNull();
    expect(document.querySelector("main h1")!.textContent).toBe("Roles");
    expect(document.querySelector(".breadcrumbs")!.textContent).toContain("Settings");
  });

  it("finds an English screen by its Arabic name and records through module sources", async () => {
    const calls = serve(admin);
    view = await render(<App language="en" />);
    await settle();
    const input = await openPalette();
    setInput(input, "المستخدمون");
    await settle();
    expect(options()[0]).toBe("Users");
    setInput(input, "omar");
    await act(async () => {
      await new Promise((r) => setTimeout(r, 200));
    });
    await settle();
    expect(calls.some((c) => c.url.startsWith("/api/identity/users?search=omar"))).toBe(true);
    expect(options()).toContain("Omar Haddad");
    const index = options().indexOf("Omar Haddad");
    for (let i = 0; i < index; i++) press({ code: "ArrowDown", key: "ArrowDown" }, input);
    press({ code: "Enter", key: "Enter" }, input);
    await settle();
    expect(window.location.pathname + window.location.search).toBe("/identity/users?search=omar%40alnoor.example");
    expect(document.querySelector<HTMLInputElement>('input[type="search"]')!.value).toBe("omar@alnoor.example");
  });

  it("offers a user with no roles no screens and never asks a record source they may not use", async () => {
    const calls = serve(nobody);
    view = await render(<App language="en" />);
    await settle();
    const input = await openPalette();
    setInput(input, "users");
    await act(async () => {
      await new Promise((r) => setTimeout(r, 200));
    });
    expect(options()).not.toContain("Users");
    expect(calls.filter((c) => c.url.startsWith("/api/identity/users"))).toHaveLength(0);
    setInput(input, "");
    await settle();
    expect(options()).toEqual(expect.arrayContaining(["Home", "Sign out"]));
    expect(options()).not.toContain("Roles");
    expect(document.querySelectorAll("nav.navpane a")).toHaveLength(0);
  });

  it("closes with Escape and gives the focus back", async () => {
    serve(admin);
    view = await render(<App language="en" />);
    await settle();
    const trigger = document.querySelector<HTMLButtonElement>(".palette-trigger")!;
    trigger.focus();
    await act(async () => trigger.click());
    await settle();
    expect(palette()).not.toBeNull();
    press({ code: "Escape", key: "Escape" }, palette()!.querySelector("input")!);
    await settle();
    expect(palette()).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });
});

describe("language and digits", () => {
  it("switches to Arabic in one keystroke, mirrors at once and saves with a request that survives a reload", async () => {
    const calls = serve(admin);
    view = await render(<App language="en" />);
    await settle();
    press({ code: "KeyL", key: "l", altKey: true });
    await settle();
    expect(document.documentElement.dir).toBe("rtl");
    expect(document.documentElement.lang).toBe("ar");
    expect(document.querySelector("nav.navpane")!.textContent).toContain("المستخدمون");
    const put = calls.find((c) => c.method === "PUT")!;
    expect(put).toMatchObject({ url: "/api/identity/me/preferences", body: { language: "ar" }, keepalive: true });
    expect(document.querySelector('[data-testid="announcer"]')).not.toBeNull();
  });

  it("keeps the new language after a reload that happened before the server saved it", async () => {
    // The device switched to Arabic, then the page reloaded before PUT returned: the session
    // still says English.
    localStorage.setItem("erp.pendingPreferences", JSON.stringify({ userId: "u1", changes: { language: "ar" } }));
    const calls = serve(admin);
    view = await render(<App language="en" />);
    await settle();
    await settle();
    expect(document.documentElement.dir).toBe("rtl");
    expect(calls.find((c) => c.method === "PUT")?.body).toEqual({ language: "ar" });
    expect(localStorage.getItem("erp.pendingPreferences")).toBeNull();
  });

  it("does not apply another user's unsaved change", async () => {
    localStorage.setItem("erp.pendingPreferences", JSON.stringify({ userId: "someone-else", changes: { language: "ar" } }));
    const calls = serve(admin);
    view = await render(<App language="en" />);
    await settle();
    expect(document.documentElement.dir).toBe("ltr");
    expect(calls.some((c) => c.method === "PUT")).toBe(false);
  });

  it("shows Arabic-Indic digits on Arabic screens when the user chooses them", async () => {
    window.history.replaceState(null, "", "/identity/users");
    const arabic = { ...admin, user: { ...admin.user, language: "ar", numerals: "arab" } };
    serve(arabic, (_m, url) => (url.startsWith("/api/identity/users") ? { status: 200, body: { items: [], total: 1234 } } : undefined));
    view = await render(<App language="ar" />);
    await act(async () => {
      await new Promise((r) => setTimeout(r, 250));
    });
    await settle();
    expect(document.querySelector(".screen-header")!.textContent).toContain("١٬٢٣٤");
    expect(document.documentElement.dataset.numerals).toBe("arab");
  });

  it("changes digits from the preferences dialog and saves them to the profile", async () => {
    const calls = serve({ ...admin, user: { ...admin.user, language: "ar" } });
    view = await render(<App language="ar" />);
    await settle();
    press({ code: "KeyP", key: "p", altKey: true });
    await settle();
    const dialog = document.querySelector('[role="dialog"].preferences')!;
    const arab = dialog.querySelector<HTMLInputElement>('input[name="numerals"][value="arab"]')!;
    await act(async () => arab.click());
    await settle();
    expect(calls.find((c) => c.method === "PUT")?.body).toEqual({ numerals: "arab" });
    expect(dialog.querySelector(".prefs-sample")!.textContent).toContain("١٬٢٣٤٬٥٦٧٫٨٩");
    expect(dialog.querySelector(".prefs-status")!.textContent).toContain("حُفظ في ملفك الشخصي");
  });
});

describe("keyboard and focus", () => {
  it("lists every active shortcut in the help sheet", async () => {
    serve(admin);
    view = await render(<App language="en" />);
    await settle();
    press({ code: "Slash", key: "?", shiftKey: true });
    await settle();
    const rows = [...document.querySelectorAll("[data-shortcut]")].map((r) => r.getAttribute("data-shortcut"));
    expect(rows).toEqual(expect.arrayContaining(["shell.palette", "shell.language", "shell.help", "shell.helpAnywhere", "shell.navigation", "shell.home", "shell.preferences", "shell.toggleNavigation"]));
    for (const row of document.querySelectorAll("[data-shortcut] td:last-child")) expect(row.textContent).not.toMatch(/^shell\./);
  });

  it("moves to the navigation with Alt+M and through it with the arrow keys", async () => {
    serve(admin);
    view = await render(<App language="en" />);
    await settle();
    press({ code: "KeyM", key: "m", altKey: true });
    await act(async () => {
      await new Promise((r) => setTimeout(r, 10));
    });
    const links = [...document.querySelectorAll<HTMLAnchorElement>("nav.navpane a")];
    expect(document.activeElement).toBe(links[0]);
    press({ code: "ArrowDown", key: "ArrowDown" });
    expect(document.activeElement).toBe(links[1]);
    press({ code: "End", key: "End" });
    expect(document.activeElement).toBe(links[2]);
    // One Tab stop for the whole pane.
    expect(links.filter((l) => l.tabIndex === 0)).toHaveLength(1);
  });

  it("keeps the focus where it is when the language changes", async () => {
    window.history.replaceState(null, "", "/identity/users");
    serve(admin);
    view = await render(<App language="en" />);
    await settle();
    const search = document.querySelector<HTMLInputElement>('input[type="search"]')!;
    search.focus();
    press({ code: "KeyL", key: "l", altKey: true }, search);
    await settle();
    expect(document.documentElement.dir).toBe("rtl");
    expect(document.activeElement).toBe(search);
  });

  it("hides and shows the navigation pane and remembers it on this device", async () => {
    serve(admin);
    view = await render(<App language="en" />);
    await settle();
    press({ code: "KeyB", key: "b", altKey: true });
    await settle();
    expect(document.querySelector<HTMLElement>("nav.navpane")!.hidden).toBe(true);
    expect(localStorage.getItem("erp.navOpen")).toBe("0");
    const toggle = document.querySelector<HTMLButtonElement>('button[aria-controls="navpane"]')!;
    expect(toggle.getAttribute("aria-expanded")).toBe("false");
  });
});

import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { collectExtensions } from "../../kernel/extensions";
import { listReply } from "../../test/lists";
import { mockFetch, render, settle, type Rendered } from "../../test/render";
import { App } from "./App";
import { goToTargets, goToWindowMs } from "./goTo";

let view: Rendered | undefined;

beforeEach(() => {
  localStorage.clear();
  window.history.replaceState(null, "", "/");
});
afterEach(() => {
  view?.unmount();
  view = undefined;
  vi.useRealTimers();
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
type Session = typeof admin;
const rolesOnly: Session = { ...admin, permissions: ["identity.roles.read"], menu: [admin.menu[1]!] };

function serve(session: Session) {
  return mockFetch((method, url) => {
    if (url === "/api/auth/session") return { status: 200, body: session };
    const list = listReply(method, url);
    if (list) return list;
    if (url.startsWith("/api/identity/users")) return { status: 200, body: { items: [], total: 0 } };
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

const hint = () => document.querySelector<HTMLElement>("[data-goto-hint]");

async function start(session: Session = admin, language: "en" | "ar" = "en") {
  serve({ ...session, user: { ...session.user, language } });
  view = await render(<App language={language} />);
  await settle();
}

describe("go to a screen with G and a letter", () => {
  it("opens Users with G then U from Home, showing what G offers while it waits", async () => {
    await start();
    press({ code: "KeyG", key: "g" });
    await settle();
    expect(hint()).not.toBeNull();
    expect([...hint()!.querySelectorAll("li")].map((li) => li.textContent)).toEqual(["H Home", "U Users", "R Roles", "W Workspace"]);
    expect(document.querySelector(".app")!.getAttribute("data-goto")).toBe("waiting");
    press({ code: "KeyU", key: "u" });
    await settle();
    expect(window.location.pathname).toBe("/identity/users");
    expect(hint()).toBeNull();
  });

  it("works by key position with an Arabic layout and in Arabic", async () => {
    await start(admin, "ar");
    // The keys G and R on an Arabic layout type ل and ق.
    press({ code: "KeyG", key: "ل" });
    await settle();
    expect(hint()!.textContent).toContain("الأدوار");
    press({ code: "KeyR", key: "ق" });
    await settle();
    expect(window.location.pathname).toBe("/identity/roles");
    // The list puts the focus in its search box on arrival, where G is a letter typed: from the
    // screen itself it is the go-to key.
    act(() => document.querySelector<HTMLElement>("main")!.focus());
    press({ code: "KeyG", key: "ل" });
    press({ code: "KeyH", key: "ا" });
    await settle();
    expect(window.location.pathname).toBe("/");
  });

  it("types G into a field instead, and leaves alone a G with a modifier", async () => {
    await start();
    const input = document.createElement("input");
    document.querySelector("main")!.append(input);
    input.focus();
    press({ code: "KeyG", key: "g" }, input);
    press({ code: "KeyU", key: "u" }, input);
    await settle();
    expect(hint()).toBeNull();
    expect(window.location.pathname).toBe("/");
    input.remove();
    press({ code: "KeyG", key: "G", shiftKey: true });
    press({ code: "KeyU", key: "u" });
    press({ code: "KeyG", key: "g", ctrlKey: true });
    press({ code: "KeyU", key: "u" });
    await settle();
    expect(window.location.pathname).toBe("/");
  });

  it("lets Escape, another key or the time running out end the wait", async () => {
    await start();
    press({ code: "KeyG", key: "g" });
    press({ code: "Escape", key: "Escape" });
    press({ code: "KeyU", key: "u" });
    await settle();
    expect(window.location.pathname).toBe("/");
    expect(hint()).toBeNull();
    press({ code: "KeyG", key: "g" });
    press({ code: "KeyX", key: "x" });
    press({ code: "KeyU", key: "u" });
    await settle();
    expect(window.location.pathname).toBe("/");
    vi.useFakeTimers({ toFake: ["Date", "setTimeout", "clearTimeout"] });
    press({ code: "KeyG", key: "g" });
    act(() => vi.advanceTimersByTime(goToWindowMs + 1));
    expect(hint()).toBeNull();
    press({ code: "KeyU", key: "u" });
    vi.useRealTimers();
    await settle();
    expect(window.location.pathname).toBe("/");
  });

  it("offers only the screens the user's navigation lists", async () => {
    await start(rolesOnly);
    press({ code: "KeyG", key: "g" });
    await settle();
    expect([...hint()!.querySelectorAll("li")].map((li) => li.textContent)).toEqual(["H Home", "R Roles"]);
    press({ code: "KeyU", key: "u" });
    await settle();
    expect(window.location.pathname).toBe("/");
  });

  it("does nothing while a dialog is open", async () => {
    await start();
    press({ code: "KeyK", key: "k", ctrlKey: true });
    await settle();
    const input = document.querySelector<HTMLInputElement>('[role="dialog"] input')!;
    press({ code: "KeyG", key: "g" }, document.body);
    press({ code: "KeyU", key: "u" }, document.body);
    await settle();
    expect(hint()).toBeNull();
    expect(window.location.pathname).toBe("/");
    expect(input).not.toBeNull();
  });

  it("takes the second key before the screen's own keys see it", async () => {
    await start();
    window.history.pushState(null, "", "/identity/users");
    window.dispatchEvent(new PopStateEvent("popstate"));
    await settle();
    expect(window.location.pathname).toBe("/identity/users");
    act(() => document.querySelector<HTMLElement>("main")!.focus());
    const seen: string[] = [];
    const listener = (event: KeyboardEvent) => seen.push(event.code);
    window.addEventListener("keydown", listener);
    press({ code: "KeyG", key: "g" });
    press({ code: "KeyR", key: "r" });
    await settle();
    window.removeEventListener("keydown", listener);
    expect(seen).toEqual([]);
    expect(window.location.pathname).toBe("/identity/roles");
  });

  it("names each entry's letter in the navigation pane and the help sheet", async () => {
    await start();
    const users = document.querySelector<HTMLAnchorElement>('nav.navpane a[href="/identity/users"]')!;
    expect(users.getAttribute("data-go")).toBe("U");
    expect(users.getAttribute("aria-keyshortcuts")).toBe("G U");
    expect(users.textContent).toBe("Users");
    press({ code: "Slash", key: "?", shiftKey: true });
    await settle();
    const goTo = [...document.querySelectorAll(".shortcut-table")].find((t) => t.querySelector("caption")!.textContent === "Go to a screen")!;
    expect([...goTo.querySelectorAll("tbody tr")].map((r) => r.textContent)).toEqual(["G then HHome", "G then UUsers", "G then RRoles", "G then WWorkspace"]);
  });
});

describe("go-to keys the modules declare", () => {
  const ext = (goTo: { key: string; menu: string; code: string }[], module = "identity") => ({ [`../modules/${module}/extensions.ts`]: { extensions: { goTo } } });

  it("are letters, one screen each, never the shell's own, and only for the module's own entries", () => {
    expect(() => collectExtensions(ext([{ key: "identity.users", menu: "identity.users", code: "Digit1" }]))).toThrow(/letter key/);
    expect(() => collectExtensions(ext([{ key: "identity.users", menu: "identity.users", code: "KeyH" }]))).toThrow(/keeps for itself/);
    expect(() => collectExtensions(ext([{ key: "identity.users", menu: "identity.users", code: "KeyG" }]))).toThrow(/keeps for itself/);
    expect(() => collectExtensions(ext([{ key: "identity.users", menu: "tenancy.companies", code: "KeyU" }]))).toThrow(/its own navigation entries/);
    expect(() =>
      collectExtensions({
        ...ext([{ key: "identity.users", menu: "identity.users", code: "KeyU" }]),
        ...ext([{ key: "tenancy.units", menu: "tenancy.units", code: "KeyU" }], "tenancy"),
      }),
    ).toThrow(/already used/);
  });

  it("lead from the menu to its screens in the menu's order, after Home", () => {
    const targets = goToTargets(admin.menu, [
      { key: "tenancy.tenant", menu: "tenancy.tenant", code: "KeyW" },
      { key: "identity.users", menu: "identity.users", code: "KeyU" },
    ]);
    expect(targets.map((t) => [t.code, t.path])).toEqual([
      ["KeyH", "/"],
      ["KeyU", "/identity/users"],
      ["KeyW", "/tenancy/tenant"],
    ]);
  });
});

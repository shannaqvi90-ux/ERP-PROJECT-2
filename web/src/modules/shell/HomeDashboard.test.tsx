import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, settle, type Rendered } from "../../test/render";
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

const session = (permissions: string[], language = "en") => ({
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori", language, numerals: "latn" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: [
    { key: "contacts.contacts", labelKey: "contacts.menu.contacts", path: "/contacts/contacts" },
    { key: "jobs.jobs", labelKey: "jobs.jobs.title", path: "/jobs/jobs", group: "settings" },
  ],
  expiresAt: "2026-10-03T09:00:00Z",
});

function serve(permissions: string[], failedJobs: number, language = "en") {
  const asked: string[] = [];
  mockFetch((method, url) => {
    if (url === "/api/auth/session") return { status: 200, body: session(permissions, language) };
    if (method === "GET" && url.startsWith("/api/")) asked.push(url);
    if (url.startsWith("/api/jobs/jobs")) return { status: 200, body: { items: [], total: url.includes("failed") ? failedJobs : 3 } };
    if (url.startsWith("/api/contacts/contacts")) return { status: 200, body: { items: [], total: url.includes("false") ? 4 : 120 } };
    return { status: 404, body: {} };
  });
  return asked;
}

describe("home dashboard", () => {
  it("counts what waits for the user from the real list endpoints and says how many things need them, in English", async () => {
    const asked = serve(["contacts.contacts.read", "jobs.jobs.read", "jobs.jobs.run"], 2);
    view = await render(<App language="en" />);
    await settle();
    await settle();
    const text = view.container.textContent ?? "";
    expect(text).toContain("2 things need you today");
    expect(text).toContain("Failed jobs");
    expect(view.container.querySelector('.home-figure[data-attention]')).not.toBeNull();
    expect(asked.some((u) => u.startsWith("/api/jobs/jobs?") && u.includes("take=1") && decodeURIComponent(u.replace(/\+/g, " ")).includes("jobStatus eq 'failed'"))).toBe(true);
  });

  it("shows a read-only user the counts but does not tell them to fix what they cannot act on, and lists what to do first", async () => {
    serve(["contacts.contacts.read", "jobs.jobs.read"], 7);
    view = await render(<App language="en" />);
    await settle();
    await settle();
    expect(view.container.textContent).toContain("Failed jobs");
    expect(view.container.textContent).toContain("Nothing is waiting for you right now.");
    expect(view.container.querySelector(".home-first")).toBeNull();
  });

  it("lists what needs doing first, largest first, for a user who can act", async () => {
    serve(["contacts.contacts.read", "jobs.jobs.read", "jobs.jobs.run"], 7);
    view = await render(<App language="en" />);
    await settle();
    await settle();
    const first = view.container.querySelector(".home-first");
    expect(first?.textContent).toContain("Failed jobs");
    expect(first?.textContent).toContain("7");
  });

  it("says nothing waits when every figure is zero, and in Arabic", async () => {
    serve(["contacts.contacts.read", "jobs.jobs.read"], 0, "ar");
    view = await render(<App language="ar" />);
    await settle();
    await settle();
    expect(view.container.textContent).toContain("لا شيء ينتظرك الآن.");
  });

  it("offers only the cards the user's roles allow", async () => {
    const asked = serve(["contacts.contacts.read"], 5);
    view = await render(<App language="en" />);
    await settle();
    await settle();
    expect(view.container.textContent).not.toContain("Failed jobs");
    expect(asked.some((u) => u.startsWith("/api/jobs/jobs"))).toBe(false);
    expect(view.container.textContent).toContain("Nothing is waiting for you right now.");
  });

  it("offers the people-and-access card only to a user who may read users, and asks no users endpoint otherwise (gate backlog: identity home card guard)", async () => {
    const without = serve(["contacts.contacts.read", "jobs.jobs.read"], 1);
    view = await render(<App language="en" />);
    await settle();
    await settle();
    expect(view.container.textContent).not.toContain("People and access");
    expect(without.some((u) => u.startsWith("/api/identity/users"))).toBe(false);
  });

  it("shows the people-and-access card to a user who may read users", async () => {
    const asked = serve(["contacts.contacts.read", "identity.users.read"], 1);
    view = await render(<App language="en" />);
    await settle();
    await settle();
    expect(view.container.textContent).toContain("People and access");
    expect(asked.some((u) => u.startsWith("/api/identity/users"))).toBe(true);
  });

  // Every figure that counts as work waiting (data-attention) is shown to a user who may act on it, and only to them.
  const attentionFigures: { name: string; read: string[]; act: string; label: string; endpoint: string }[] = [
    { name: "users who never signed in", read: ["identity.users.read"], act: "identity.users.update", label: "never signed in", endpoint: "/api/identity/users" },
    { name: "failed jobs", read: ["jobs.jobs.read"], act: "jobs.jobs.run", label: "Failed jobs", endpoint: "/api/jobs/jobs" },
  ];
  for (const figure of attentionFigures) {
    it(`does not call ${figure.name} work waiting for a user who cannot act on it, and does for one who can`, async () => {
      for (const [permissions, expected] of [[figure.read, false], [[...figure.read, figure.act], true]] as [string[], boolean][]) {
        mockFetch((method, url) => {
          if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
          if (method === "GET" && url.startsWith(figure.endpoint)) return { status: 200, body: { items: [], total: 9 } };
          return { status: 404, body: {} };
        });
        view = await render(<App language="en" />);
        await settle();
        await settle();
        const attention = [...view.container.querySelectorAll(".home-figure[data-attention]")].map((el) => el.textContent ?? "");
        expect(attention.some((t) => t.toLowerCase().includes(figure.label.toLowerCase())), `${figure.name} with ${permissions.join(", ")}`).toBe(expected);
        view.unmount();
        view = undefined;
      }
    });
  }

  // Whatever a module adds later: every figure that counts as work waiting names the permission that acts on it
  // (a read-only user is not told to fix what they cannot), and each such figure is in the list above.
  it("has an act permission on every attention figure of every module, and a behaviour test for each", () => {
    const sources = import.meta.glob("../*/home.tsx", { query: "?raw", import: "default", eager: true }) as Record<string, string>;
    const found: { module: string; key: string; hasAct: boolean }[] = [];
    for (const [path, text] of Object.entries(sources)) {
      for (const figure of text.matchAll(/\{[^{}]*attention:\s*true[^{}]*\}/g)) {
        found.push({ module: path, key: /key:\s*"([^"]+)"/.exec(figure[0])?.[1] ?? figure[0], hasAct: /actPermission:\s*"[^"]+"/.test(figure[0]) });
      }
    }
    expect(found.length).toBeGreaterThanOrEqual(2);
    expect(found.filter((f) => !f.hasAct).map((f) => `${f.module} ${f.key}`)).toEqual([]);
    expect(found.map((f) => f.key).sort()).toEqual(["identity.neverSignedIn", "jobs.failed"]);
  });
});

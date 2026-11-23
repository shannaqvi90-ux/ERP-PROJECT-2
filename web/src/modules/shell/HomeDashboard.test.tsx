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
});

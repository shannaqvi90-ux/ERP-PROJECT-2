import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, settle, type Rendered } from "../../test/render";
import { chooseDensity, chooseTheme } from "../../kernel/theme";
import { App } from "../shell/App";

let view: Rendered | undefined;
beforeEach(() => {
  localStorage.clear();
  delete document.documentElement.dataset.theme;
  delete document.documentElement.dataset.density;
  window.history.replaceState(null, "", "/");
});
afterEach(() => {
  view?.unmount();
  view = undefined;
});

const session = (user: Record<string, unknown>, permissions = ["identity.profile.update"]) => ({
  authenticated: true,
  user: { id: "u1", email: "a@alnoor.example", displayName: "Mariam", language: "en", numerals: "latn", ...user },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor", nameAr: "النور" },
  permissions,
  menu: [],
  expiresAt: "2026-10-03T09:00:00Z",
});

describe("a user's look", () => {
  it("is the user's own saved look at sign-in, not the one this device last showed", async () => {
    localStorage.setItem("erp.theme", "light");
    localStorage.setItem("erp.density", "comfortable");
    mockFetch((_m, url) => (url === "/api/auth/session" ? { status: 200, body: session({ theme: "hc", density: "comfortable" }) } : { status: 404, body: {} }));
    view = await render(<App language="en" />);
    await settle();
    expect(document.documentElement.dataset.theme).toBe("hc");
    expect(document.documentElement.dataset.density).toBe("comfortable");
  });

  it("is the default for a user who never chose, even when the previous person on this device chose another", async () => {
    localStorage.setItem("erp.theme", "light");
    localStorage.setItem("erp.density", "comfortable");
    mockFetch((_m, url) => (url === "/api/auth/session" ? { status: 200, body: session({}) } : { status: 404, body: {} }));
    view = await render(<App language="en" />);
    await settle();
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(document.documentElement.dataset.density).toBe("compact");
  });

  it("is saved to the user when chosen, and to nobody else's when the user may not change preferences", async () => {
    const puts: { url: string; body: unknown }[] = [];
    let permissions = ["identity.profile.update"];
    mockFetch((method, url, body) => {
      if (url === "/api/auth/session") return { status: 200, body: session({}, permissions) };
      if (method === "PUT") {
        puts.push({ url, body });
        return { status: 200, body: session({}) .user };
      }
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    act(() => {
      chooseTheme("light");
      chooseDensity("comfortable");
    });
    await settle();
    expect(puts.map((p) => p.url)).toEqual(["/api/identity/me/look", "/api/identity/me/look"]);
    expect(puts.map((p) => p.body)).toEqual([{ theme: "light" }, { density: "comfortable" }]);
    view.unmount();
    view = undefined;
    puts.length = 0;
    permissions = [];
    view = await render(<App language="en" />);
    await settle();
    act(() => chooseTheme("dark"));
    await settle();
    expect(puts).toEqual([]);
  });
});

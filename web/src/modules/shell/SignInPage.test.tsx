import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, settle, setInput, submit, type Rendered } from "../../test/render";
import { App } from "./App";

let view: Rendered | undefined;

beforeEach(() => {
  localStorage.clear();
  window.history.replaceState(null, "", "/");
});

afterEach(() => view?.unmount());

const session = {
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori", language: "en" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions: ["identity.users.read"],
  menu: [{ key: "identity.users", labelKey: "identity.menu.users", path: "/identity/users", group: "settings" }],
  expiresAt: "2026-10-03T09:00:00Z",
};

describe("sign-in screen", () => {
  it("starts in Arabic, right to left, when the device prefers Arabic", async () => {
    localStorage.setItem("erp.language", "ar");
    mockFetch(() => ({ status: 200, body: { authenticated: false } }));
    view = await render(<App />);
    await settle();
    expect(document.documentElement.dir).toBe("rtl");
    expect(document.documentElement.lang).toBe("ar");
    expect(view.container.textContent).toContain("تسجيل الدخول");
  });

  it("validates in the screen language without calling the server", async () => {
    const calls = mockFetch(() => ({ status: 200, body: { authenticated: false } }));
    view = await render(<App language="ar" />);
    await settle();
    await submit(view.container);
    expect(view.container.textContent).toContain("أدخل بريدك الإلكتروني.");
    expect(calls.filter((c) => c.url.includes("sign-in"))).toHaveLength(0);
  });

  it("focuses the password when this device remembers the e-mail, then signs in to the shell", async () => {
    localStorage.setItem("erp.lastEmail", "admin@alnoor.example");
    const calls = mockFetch((method, url) => {
      if (url === "/api/auth/session") return { status: 200, body: { authenticated: false } };
      if (method === "POST" && url === "/api/auth/sign-in") return { status: 200, body: session };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    const password = view.container.querySelector<HTMLInputElement>('input[name="password"]')!;
    expect(document.activeElement).toBe(password);
    setInput(password, "Demo-Pass-2026");
    await submit(view.container);
    await settle();
    const signIn = calls.find((c) => c.url === "/api/auth/sign-in")!;
    expect(signIn.body).toEqual({ email: "admin@alnoor.example", password: "Demo-Pass-2026" });
    expect(signIn.headers["X-Erp-Request"]).toBe("1");
    expect(view.container.textContent).toContain("Welcome, Mariam Al Mansoori");
    expect(view.container.querySelector("nav")!.textContent).toBe("Users");
  });

  it("keeps the keyboard in the form after switching language", async () => {
    mockFetch(() => ({ status: 200, body: { authenticated: false } }));
    view = await render(<App language="en" />);
    await settle();
    const email = view.container.querySelector<HTMLInputElement>('input[name="email"]')!;
    expect(document.activeElement).toBe(email);
    const toggle = view.container.querySelector<HTMLButtonElement>("button.lang-toggle")!;
    toggle.focus();
    await act(async () => toggle.click());
    await settle();
    expect(document.documentElement.dir).toBe("rtl");
    expect(document.activeElement).toBe(email);
    setInput(email, "admin@alnoor.example");
    await act(async () => view!.container.querySelector<HTMLButtonElement>("button.lang-toggle")!.click());
    await settle();
    expect(document.documentElement.dir).toBe("ltr");
    expect(document.activeElement).toBe(view.container.querySelector('input[name="password"]'));
  });

  it("shows the server's message after a failed sign-in and clears the password", async () => {
    mockFetch((_method, url) => {
      if (url === "/api/auth/session") return { status: 200, body: { authenticated: false } };
      return { status: 401, body: { title: "Sign-in failed. Check your e-mail and password.", code: "auth.signInFailed" } };
    });
    view = await render(<App language="en" />);
    await settle();
    setInput(view.container.querySelector<HTMLInputElement>('input[name="email"]')!, "x@y.example");
    setInput(view.container.querySelector<HTMLInputElement>('input[name="password"]')!, "wrong-password");
    await submit(view.container);
    await settle();
    expect(view.container.querySelector('[role="alert"]')!.textContent).toContain("Sign-in failed");
    expect(view.container.querySelector<HTMLInputElement>('input[name="password"]')!.value).toBe("");
  });
});

describe("shell", () => {
  it("offers only granted screens and refuses an address the roles do not allow", async () => {
    window.history.replaceState(null, "", "/identity/roles");
    mockFetch((_m, url) => (url === "/api/auth/session" ? { status: 200, body: session } : { status: 200, body: { items: [], total: 0 } }));
    view = await render(<App language="en" />);
    await settle();
    expect(view.container.querySelector("nav")!.textContent).toBe("Users");
    expect(view.container.textContent).toContain("No access");
  });
});

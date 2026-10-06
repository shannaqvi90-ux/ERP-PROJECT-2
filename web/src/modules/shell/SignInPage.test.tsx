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

  it("re-renders a failed sign-in in the new language after switching, with nothing left in the old one", async () => {
    mockFetch((method, url) => {
      if (url === "/api/auth/session") return { status: 200, body: { authenticated: false } };
      if (method === "POST" && url === "/api/auth/sign-in")
        return { status: 401, body: { title: "Sign-in failed. Check your e-mail and password and try again.", code: "auth.signInFailed" } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    setInput(view.container.querySelector<HTMLInputElement>('input[name="email"]')!, "admin@alnoor.example");
    setInput(view.container.querySelector<HTMLInputElement>('input[name="password"]')!, "wrong-password");
    await submit(view.container);
    await settle();
    const alert = () => view!.container.querySelector('[role="alert"]')!;
    expect(alert().textContent).toBe("Sign-in failed. Check your e-mail and password and try again.");
    await act(async () => view!.container.querySelector<HTMLButtonElement>("button.lang-toggle")!.click());
    await settle();
    expect(document.documentElement.dir).toBe("rtl");
    expect(alert().textContent).toBe("تعذّر تسجيل الدخول. تحقّق من البريد الإلكتروني وكلمة المرور ثم حاول مرة أخرى.");
    expect(alert().getAttribute("lang")).toBeNull();
  });

  it("keeps an unrecognised server message in its own language and direction after switching", async () => {
    mockFetch((method, url) => {
      if (url === "/api/auth/session") return { status: 200, body: { authenticated: false } };
      if (method === "POST" && url === "/api/auth/sign-in") return { status: 503, body: { title: "Service unavailable. Try again shortly.", code: "http.503" } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="en" />);
    await settle();
    setInput(view.container.querySelector<HTMLInputElement>('input[name="email"]')!, "admin@alnoor.example");
    setInput(view.container.querySelector<HTMLInputElement>('input[name="password"]')!, "x");
    await submit(view.container);
    await settle();
    await act(async () => view!.container.querySelector<HTMLButtonElement>("button.lang-toggle")!.click());
    await settle();
    const alert = view.container.querySelector('[role="alert"]')!;
    expect(alert.textContent).toBe("Service unavailable. Try again shortly.");
    expect(alert.getAttribute("lang")).toBe("en");
    expect(alert.getAttribute("dir")).toBe("auto");
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

  function pressEnter(target: HTMLElement): boolean {
    const event = new KeyboardEvent("keydown", { key: "Enter", bubbles: true, cancelable: true });
    act(() => {
      target.dispatchEvent(event);
    });
    return event.defaultPrevented;
  }

  const signedOut = () => (method: string, url: string) => {
    if (url === "/api/auth/session") return { status: 200, body: { authenticated: false } };
    if (method === "POST" && url === "/api/auth/sign-in") return { status: 200, body: session };
    return { status: 404, body: {} };
  };

  it("goes on to the password with Enter in the e-mail field, without an error or a call", async () => {
    const calls = mockFetch(signedOut());
    view = await render(<App language="en" />);
    await settle();
    const email = view.container.querySelector<HTMLInputElement>('input[name="email"]')!;
    setInput(email, "admin@alnoor.example");
    expect(pressEnter(email)).toBe(true);
    expect(document.activeElement).toBe(view.container.querySelector('input[name="password"]'));
    expect(view.container.querySelector(".field-error")).toBeNull();
    expect(calls.filter((c) => c.url.includes("sign-in"))).toHaveLength(0);
  });

  it("stays in the e-mail field on Enter when the e-mail is not valid, and says why", async () => {
    mockFetch(signedOut());
    view = await render(<App language="ar" />);
    await settle();
    const email = view.container.querySelector<HTMLInputElement>('input[name="email"]')!;
    setInput(email, "admin");
    expect(pressEnter(email)).toBe(true);
    expect(document.activeElement).toBe(email);
    expect(view.container.querySelector(".field-error")!.textContent).toBe("أدخل عنوان بريد إلكتروني صحيحًا.");
  });

  it("on the team's sign-in address fills in the domain: the local part signs in", async () => {
    window.history.replaceState(null, "", "/?domain=Alnoor.Example");
    const calls = mockFetch(signedOut());
    view = await render(<App language="en" />);
    await settle();
    const email = view.container.querySelector<HTMLInputElement>('input[name="email"]')!;
    expect(document.activeElement).toBe(email);
    expect(view.container.querySelector("#email-domain")!.textContent).toContain("@alnoor.example");
    expect(email.getAttribute("aria-describedby")).toBe("email-domain email-moves-on");
    setInput(email, "admin");
    pressEnter(email);
    const password = view.container.querySelector<HTMLInputElement>('input[name="password"]')!;
    expect(document.activeElement).toBe(password);
    setInput(password, "Demo-Pass-2026");
    await submit(view.container);
    await settle();
    expect(calls.find((c) => c.url === "/api/auth/sign-in")!.body).toEqual({ email: "admin@alnoor.example", password: "Demo-Pass-2026" });
    // The device remembers the whole e-mail.
    expect(localStorage.getItem("erp.lastEmail")).toBe("admin@alnoor.example");
  });

  it("on the team's sign-in address, typing @ signs in with the address as typed", async () => {
    window.history.replaceState(null, "", "/?domain=alnoor.example");
    const calls = mockFetch(signedOut());
    view = await render(<App language="en" />);
    await settle();
    const email = view.container.querySelector<HTMLInputElement>('input[name="email"]')!;
    setInput(email, "auditor@outside.example");
    expect(view.container.querySelector("#email-domain")).toBeNull();
    setInput(view.container.querySelector<HTMLInputElement>('input[name="password"]')!, "Demo-Pass-2026");
    await submit(view.container);
    await settle();
    expect(calls.find((c) => c.url === "/api/auth/sign-in")!.body).toEqual({ email: "auditor@outside.example", password: "Demo-Pass-2026" });
  });

  it("on the team's sign-in address, a remembered e-mail of the team shows whole with the password focused, and signs in as itself", async () => {
    window.history.replaceState(null, "", "/?domain=alnoor.example");
    localStorage.setItem("erp.lastEmail", "admin@alnoor.example");
    const calls = mockFetch(signedOut());
    view = await render(<App language="en" />);
    await settle();
    expect(view.container.querySelector<HTMLInputElement>('input[name="email"]')!.value).toBe("admin@alnoor.example");
    // The domain is not shown a second time after an address that already has one.
    expect(view.container.querySelector("#email-domain")).toBeNull();
    expect(document.activeElement).toBe(view.container.querySelector('input[name="password"]'));
    setInput(view.container.querySelector<HTMLInputElement>('input[name="password"]')!, "Demo-Pass-2026");
    await submit(view.container);
    await settle();
    expect(calls.find((c) => c.url === "/api/auth/sign-in")!.body).toEqual({ email: "admin@alnoor.example", password: "Demo-Pass-2026" });
  });

  it("on the team's sign-in address, typing the whole e-mail moves on to the password, and the note says so first", async () => {
    window.history.replaceState(null, "", "/?domain=alnoor.example");
    const calls = mockFetch(signedOut());
    view = await render(<App language="en" />);
    await settle();
    const email = view.container.querySelector<HTMLInputElement>('input[name="email"]')!;
    const password = view.container.querySelector<HTMLInputElement>('input[name="password"]')!;
    expect(view.container.querySelector("#email-moves-on")!.textContent).toBe("Typing your whole address moves on to the password.");
    expect(email.getAttribute("aria-describedby")).toBe("email-domain email-moves-on");
    // Named by its label alone, not by the domain or the note inside the label.
    expect(document.getElementById(email.getAttribute("aria-labelledby")!)!.textContent).toBe("E-mail");
    setInput(email, "admin@alnoor.exampl");
    expect(document.activeElement).toBe(email);
    setInput(email, "admin@alnoor.example");
    expect(document.activeElement).toBe(password);
    expect(view.container.querySelector(".field-error")).toBeNull();
    setInput(password, "Demo-Pass-2026");
    await submit(view.container);
    await settle();
    expect(calls.find((c) => c.url === "/api/auth/sign-in")!.body).toEqual({ email: "admin@alnoor.example", password: "Demo-Pass-2026" });
  });

  it("after moving on, Backspace in the empty password goes back to the end of the e-mail", async () => {
    window.history.replaceState(null, "", "/?domain=alnoor.example");
    mockFetch(signedOut());
    view = await render(<App language="ar" />);
    await settle();
    expect(view.container.querySelector("#email-moves-on")!.textContent).toBe("كتابة عنوانك كاملًا تنقلك إلى كلمة المرور.");
    const email = view.container.querySelector<HTMLInputElement>('input[name="email"]')!;
    const password = view.container.querySelector<HTMLInputElement>('input[name="password"]')!;
    setInput(email, "admin@alnoor.example");
    expect(document.activeElement).toBe(password);
    const backspace = new KeyboardEvent("keydown", { key: "Backspace", bubbles: true, cancelable: true });
    act(() => {
      password.dispatchEvent(backspace);
    });
    expect(backspace.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(email);
    expect(email.selectionStart).toBe("admin@alnoor.example".length);
    // A Backspace that was not after moving on stays an ordinary key in the password field.
    password.focus();
    const again = new KeyboardEvent("keydown", { key: "Backspace", bubbles: true, cancelable: true });
    act(() => {
      password.dispatchEvent(again);
    });
    expect(again.defaultPrevented).toBe(false);
    expect(document.activeElement).toBe(password);
  });

  it("does not move on while the password already holds text, nor for another domain, nor without the team's address", async () => {
    window.history.replaceState(null, "", "/?domain=alnoor.example");
    mockFetch(signedOut());
    view = await render(<App language="en" />);
    await settle();
    let email = view.container.querySelector<HTMLInputElement>('input[name="email"]')!;
    let password = view.container.querySelector<HTMLInputElement>('input[name="password"]')!;
    setInput(email, "admin@alnoor.example.ae");
    expect(document.activeElement).toBe(email);
    setInput(password, "Demo-Pass-2026");
    email.focus();
    setInput(email, "admin@alnoor.example");
    expect(document.activeElement).toBe(email);
    view.unmount();

    window.history.replaceState(null, "", "/");
    view = await render(<App language="en" />);
    await settle();
    email = view.container.querySelector<HTMLInputElement>('input[name="email"]')!;
    password = view.container.querySelector<HTMLInputElement>('input[name="password"]')!;
    expect(view.container.querySelector("#email-moves-on")).toBeNull();
    setInput(email, "admin@alnoor.example");
    expect(document.activeElement).toBe(email);
  });

  it("shows and hides the password from a button after the field, keeping the focus in the field, in both languages", async () => {
    mockFetch(signedOut());
    view = await render(<App language="ar" />);
    await settle();
    const password = view.container.querySelector<HTMLInputElement>('input[name="password"]')!;
    const reveal = view.container.querySelector<HTMLButtonElement>("button.signin-reveal")!;
    expect(password.type).toBe("password");
    expect(reveal.getAttribute("aria-label")).toBe("إظهار كلمة المرور");
    expect(reveal.textContent).toBe("إظهار");
    await act(async () => reveal.click());
    expect(password.type).toBe("text");
    expect(document.activeElement).toBe(password);
    expect(reveal.getAttribute("aria-label")).toBe("إخفاء كلمة المرور");
    await act(async () => reveal.click());
    expect(password.type).toBe("password");
    // The field's own label names only the field, so the button does not change its name.
    expect(view.container.querySelector('label[for="signin-password"]')!.textContent).toBe("كلمة المرور");
  });

  it("says Caps Lock is on while typing the password with it on, and stops saying it once it is off", async () => {
    mockFetch(signedOut());
    view = await render(<App language="en" />);
    await settle();
    const password = view.container.querySelector<HTMLInputElement>('input[name="password"]')!;
    const key = (capsLock: boolean) =>
      act(async () => {
        const event = new KeyboardEvent("keydown", { key: "a", bubbles: true });
        Object.defineProperty(event, "getModifierState", { value: (name: string) => name === "CapsLock" && capsLock });
        password.dispatchEvent(event);
      });
    await key(true);
    expect(view.container.querySelector("#caps-lock")!.textContent).toBe("Caps Lock is on.");
    expect(password.getAttribute("aria-describedby")).toContain("caps-lock");
    await key(false);
    expect(view.container.querySelector("#caps-lock")).toBeNull();
  });

  it("ignores an address whose domain is not a host name", async () => {
    window.history.replaceState(null, "", "/?domain=%3Cscript%3E.example");
    mockFetch(signedOut());
    view = await render(<App language="en" />);
    await settle();
    expect(view.container.querySelector("#email-domain")).toBeNull();
    expect(view.container.querySelector<HTMLInputElement>('input[name="email"]')!.type).toBe("email");
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

import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockFetch, render, settle, type Rendered } from "../../test/render";
import { App } from "./App";

let view: Rendered | undefined;

const session = {
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori", language: "en" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions: ["identity.users.read"],
  menu: [{ key: "identity.users", labelKey: "identity.menu.users", path: "/identity/users", group: "settings" }],
  expiresAt: "2026-10-03T09:00:00Z",
};

const challenge = { challenge: "AAECAwQFBgcICQoLDA0ODw", rpId: "localhost", timeout: 120000 };

/** A device that answers (or refuses) navigator.credentials.get. */
function device(answer: () => Promise<unknown>) {
  const get = vi.fn(answer);
  Object.defineProperty(window, "PublicKeyCredential", { configurable: true, value: function PublicKeyCredential() {} });
  Object.defineProperty(navigator, "credentials", { configurable: true, value: { get, create: vi.fn() } });
  return get;
}

const bytes = (...values: number[]) => new Uint8Array(values).buffer;
const credential = () =>
  Promise.resolve({
    rawId: bytes(1, 2, 3),
    response: { clientDataJSON: bytes(4), authenticatorData: bytes(5), signature: bytes(6), userHandle: bytes(7, 8) },
  });

function server() {
  return mockFetch((method, url) => {
    if (url === "/api/auth/session") return { status: 200, body: { authenticated: false, passkey: challenge } };
    if (method === "POST" && url === "/api/auth/sign-in") return { status: 200, body: session };
    return { status: 404, body: {} };
  });
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  window.history.replaceState(null, "", "/");
});

afterEach(() => {
  view?.unmount();
  view = undefined;
  Reflect.deleteProperty(window, "PublicKeyCredential");
  Reflect.deleteProperty(navigator, "credentials");
});

describe("sign-in with a passkey", () => {
  it("on a device that uses a passkey, asks for it as the screen opens and signs in with the device's answer alone", async () => {
    localStorage.setItem("erp.passkeyOffer", "1");
    const get = device(credential);
    const calls = server();
    view = await render(<App language="en" />);
    await settle();
    await settle();
    expect(get).toHaveBeenCalledTimes(1);
    const options = get.mock.calls[0][0] as unknown as { publicKey: PublicKeyCredentialRequestOptions };
    expect(options.publicKey.userVerification).toBe("required");
    expect(options.publicKey.rpId).toBe("localhost");
    const signIn = calls.find((c) => c.url === "/api/auth/sign-in")!;
    expect(signIn.body).toEqual({ passkey: { credentialId: "AQID", clientDataJson: "BA", authenticatorData: "BQ", signature: "Bg", userHandle: "Bwg" } });
    expect(view.container.textContent).toContain("Welcome, Mariam Al Mansoori");
    // The device keeps asking at once; the e-mail is remembered as after a password sign-in.
    expect(localStorage.getItem("erp.passkeyOffer")).toBe("1");
    expect(localStorage.getItem("erp.lastEmail")).toBe("admin@alnoor.example");
  });

  it("asks from any address: a personal bookmark with the e-mail and the team's address too", async () => {
    localStorage.setItem("erp.passkeyOffer", "1");
    for (const address of ["/?email=admin%40alnoor.example", "/?domain=alnoor.example"]) {
      window.history.replaceState(null, "", address);
      const get = device(() => new Promise(() => {}));
      server();
      view = await render(<App language="en" />);
      await settle();
      expect(get).toHaveBeenCalledTimes(1);
      expect(view.container.textContent).toContain("Confirm on your device");
      view.unmount();
      view = undefined;
    }
  });

  it("does not ask at once on a device that never used a passkey, nor right after the Sign out button; the button asks", async () => {
    const get = device(credential);
    server();
    view = await render(<App language="en" />);
    await settle();
    expect(get).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(view.container.querySelector('input[name="email"]'));
    view.unmount();

    localStorage.setItem("erp.passkeyOffer", "1");
    sessionStorage.setItem("erp.signedOut", "1");
    view = await render(<App language="en" />);
    await settle();
    expect(get).not.toHaveBeenCalled();
    // The mark is read once: the next visit asks again.
    expect(sessionStorage.getItem("erp.signedOut")).toBeNull();

    const button = [...view.container.querySelectorAll("button")].find((b) => b.textContent === "Sign in with a passkey")!;
    await act(async () => {
      button.click();
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    await settle();
    expect(get).toHaveBeenCalledTimes(1);
    expect(view.container.textContent).toContain("Welcome, Mariam Al Mansoori");
  });

  it("when the person cancels on the device, says so and leaves the keyboard in the e-mail field", async () => {
    localStorage.setItem("erp.passkeyOffer", "1");
    device(() => Promise.reject(new DOMException("cancelled", "NotAllowedError")));
    const calls = server();
    view = await render(<App language="en" />);
    await settle();
    await settle();
    expect(calls.some((c) => c.url === "/api/auth/sign-in")).toBe(false);
    expect(view.container.textContent).toContain("The passkey was not used.");
    expect(document.activeElement).toBe(view.container.querySelector('input[name="email"]'));
  });

  it("says in Arabic when the server refuses the passkey", async () => {
    localStorage.setItem("erp.passkeyOffer", "1");
    device(credential);
    mockFetch((method, url) => {
      if (url === "/api/auth/session") return { status: 200, body: { authenticated: false, passkey: challenge } };
      if (method === "POST" && url === "/api/auth/sign-in")
        return { status: 401, body: { type: "urn:erp:problem:auth.passkeyFailed", title: "Passkey sign-in failed.", status: 401, code: "auth.passkeyFailed" } };
      return { status: 404, body: {} };
    });
    view = await render(<App language="ar" />);
    await settle();
    await settle();
    const alert = view.container.querySelector('[role="alert"]')!;
    expect(alert.textContent).not.toMatch(/[A-Za-z]{3}/);
    expect(alert.textContent!.length).toBeGreaterThan(10);
  });
});

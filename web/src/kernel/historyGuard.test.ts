import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { entryIsCurrent, historyEpoch, historyEpochKey, historyStampKey, installHistoryGuard, stampState } from "./historyGuard";

const at = () => window.location.pathname + window.location.search;
const raw = (state: unknown, url: string) => History.prototype.replaceState.call(window.history, state, "", url);

let remove: (() => void) | undefined;

beforeEach(() => {
  sessionStorage.clear();
  raw(null, "/");
});

afterEach(() => {
  remove?.();
  remove = undefined;
});

describe("history guard", () => {
  it("keeps a typed or followed address and stamps it for the tab's identity", () => {
    raw(null, "/identity/users?q=Mansoori&open=u1");
    remove = installHistoryGuard("navigate");
    expect(at()).toBe("/identity/users?q=Mansoori&open=u1");
    expect(entryIsCurrent()).toBe(true);
  });

  it("treats a prerendered page like a fresh navigation", () => {
    raw(null, "/identity/users?q=Mansoori");
    remove = installHistoryGuard("prerender");
    expect(at()).toBe("/identity/users?q=Mansoori");
  });

  for (const kind of ["back_forward", "reload"] as const) {
    it(`replaces an entry of another identity reached by ${kind} with the home address`, () => {
      raw({ [historyStampKey]: "an-ended-identity" }, "/identity/users?q=Khalifa+Steel&open=b-id");
      remove = installHistoryGuard(kind);
      expect(at()).toBe("/");
      expect(entryIsCurrent()).toBe(true);
    });

    it(`replaces an unstamped entry reached by ${kind}`, () => {
      raw(null, "/identity/users?q=Khalifa");
      remove = installHistoryGuard(kind);
      expect(at()).toBe("/");
    });

    it(`keeps the identity's own entry reached by ${kind}`, () => {
      raw({ [historyStampKey]: historyEpoch(), view: "v1" }, "/identity/users?q=Mansoori");
      remove = installHistoryGuard(kind);
      expect(at()).toBe("/identity/users?q=Mansoori");
      expect((window.history.state as Record<string, unknown>).view).toBe("v1");
    });
  }

  it("an entry of the identity is no longer trusted once the identity has ended (sessionStorage cleared)", () => {
    remove = installHistoryGuard("navigate");
    window.history.pushState(null, "", "/identity/users?q=Mansoori");
    const kept = window.history.state;
    remove();
    sessionStorage.clear();
    raw(kept, "/identity/users?q=Mansoori");
    remove = installHistoryGuard("back_forward");
    expect(at()).toBe("/");
  });

  it("stamps every entry the app writes, keeping the caller's own state", () => {
    remove = installHistoryGuard("navigate");
    window.history.pushState({ scroll: 3 }, "", "/identity/roles");
    expect(window.history.state).toEqual({ scroll: 3, [historyStampKey]: historyEpoch() });
    window.history.replaceState(null, "", "/identity/roles?q=x");
    expect(entryIsCurrent()).toBe(true);
    expect(stampState("plain")).toEqual({ [historyStampKey]: historyEpoch(), value: "plain" });
  });

  it("checks Back and Forward inside the document before the screens hear of it", () => {
    remove = installHistoryGuard("navigate");
    const heard: string[] = [];
    const listener = () => heard.push(at());
    window.addEventListener("popstate", listener);
    try {
      raw({ [historyStampKey]: "an-ended-identity" }, "/identity/users?q=Khalifa");
      window.dispatchEvent(new PopStateEvent("popstate", { state: window.history.state }));
      expect(heard).toEqual(["/"]);
      raw({ [historyStampKey]: historyEpoch() }, "/identity/users?q=Mansoori");
      window.dispatchEvent(new PopStateEvent("popstate", { state: window.history.state }));
      expect(heard).toEqual(["/", "/identity/users?q=Mansoori"]);
    } finally {
      window.removeEventListener("popstate", listener);
    }
  });

  it("the epoch is a random value, new for each identity, never an id or a name", () => {
    const first = historyEpoch();
    expect(first).toMatch(/^[0-9a-f]{24}$/);
    expect(historyEpoch()).toBe(first);
    sessionStorage.removeItem(historyEpochKey);
    expect(historyEpoch()).not.toBe(first);
  });

  it("installing twice wraps the browser's own functions once", () => {
    remove = installHistoryGuard("navigate");
    const again = installHistoryGuard("navigate");
    window.history.pushState({ a: 1 }, "", "/x");
    expect(window.history.state).toEqual({ a: 1, [historyStampKey]: historyEpoch() });
    again();
  });
});

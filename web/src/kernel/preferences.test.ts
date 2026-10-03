import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch } from "../test/render";
import { effectivePreferences, pendingFor, rememberPending, savePreferences, settlePending } from "./preferences";

beforeEach(() => localStorage.clear());
afterEach(() => localStorage.clear());

describe("preferences survive a reload before the server answers", () => {
  it("keeps a change pending for its user until the server confirms it", async () => {
    const calls = mockFetch(() => ({ status: 200, body: {} }));
    const outcome = await savePreferences("u1", { language: "ar" });
    expect(outcome).toBe("saved");
    expect(calls[0]).toMatchObject({ method: "PUT", url: "/api/identity/me/preferences", body: { language: "ar" }, keepalive: true });
    expect(pendingFor("u1")).toBeNull();
  });

  it("keeps the change when the network fails, and the session's values give way to it", async () => {
    globalThis.fetch = (() => Promise.reject(new TypeError("Failed to fetch"))) as typeof fetch;
    expect(await savePreferences("u1", { language: "ar" })).toBe("offline");
    expect(pendingFor("u1")).toEqual({ language: "ar" });
    const { preferences, pending } = effectivePreferences("u1", { language: "en", numerals: "latn" });
    expect(preferences).toEqual({ language: "ar", numerals: "latn" });
    expect(pending).toEqual({ language: "ar" });
  });

  it("drops a change the server refuses for good", async () => {
    mockFetch(() => ({ status: 403, body: { code: "auth.forbidden" } }));
    expect(await savePreferences("u1", { numerals: "arab" })).toBe("refused");
    expect(pendingFor("u1")).toBeNull();
  });

  it("sends a change again when it met another change of the same user (409), and saves it", async () => {
    let answered = 0;
    const calls = mockFetch(() => (++answered === 1 ? { status: 409, body: { code: "concurrency" } } : { status: 200, body: {} }));
    expect(await savePreferences("u1", { numerals: "arab" })).toBe("saved");
    expect(calls.filter((c) => c.method === "PUT")).toHaveLength(2);
    expect(pendingFor("u1")).toBeNull();
  });

  it("gives up on a change that keeps conflicting, and keeps it pending for the next session", async () => {
    const calls = mockFetch(() => ({ status: 409, body: { code: "concurrency" } }));
    expect(await savePreferences("u1", { numerals: "arab" })).toBe("offline");
    expect(calls.filter((c) => c.method === "PUT")).toHaveLength(4);
    expect(pendingFor("u1")).toEqual({ numerals: "arab" });
  });

  it("never applies one user's pending change to another user", () => {
    rememberPending("u1", { language: "ar" });
    expect(pendingFor("u2")).toBeNull();
    expect(effectivePreferences("u2", { language: "en" }).preferences).toEqual({ language: "en", numerals: "latn" });
  });

  it("keeps a newer change made while an older one was in flight", () => {
    rememberPending("u1", { language: "ar" });
    rememberPending("u1", { language: "en", numerals: "arab" });
    settlePending("u1", { language: "ar" });
    expect(pendingFor("u1")).toEqual({ language: "en", numerals: "arab" });
    settlePending("u1", { language: "en", numerals: "arab" });
    expect(pendingFor("u1")).toBeNull();
  });

  it("ignores a tampered or corrupt stored value", () => {
    localStorage.setItem("erp.pendingPreferences", JSON.stringify({ userId: "u1", changes: { language: "fr", numerals: "roman" } }));
    expect(pendingFor("u1")).toBeNull();
    localStorage.setItem("erp.pendingPreferences", "{not json");
    expect(pendingFor("u1")).toBeNull();
  });
});

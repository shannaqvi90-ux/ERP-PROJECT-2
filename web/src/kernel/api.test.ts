import { describe, expect, it } from "vitest";
import { mockFetch } from "../test/render";
import { api, ApiError } from "./api";
import { applyLanguage } from "./i18n";

describe("api", () => {
  it("sends the request header on changes and the screen language on every call", async () => {
    applyLanguage("ar");
    const calls = mockFetch(() => ({ status: 200, body: { ok: true } }));
    await api("POST", "/api/x", { a: 1 });
    await api("GET", "/api/y");
    expect(calls[0]!.headers["X-Erp-Request"]).toBe("1");
    expect(calls[0]!.headers["Accept-Language"]).toBe("ar");
    expect(calls[1]!.headers["X-Erp-Request"]).toBeUndefined();
    applyLanguage("en");
  });

  it("turns problem responses into ApiError with code, title and field errors", async () => {
    mockFetch(() => ({
      status: 400,
      body: { title: "Some fields need attention.", code: "validation", errors: { email: [{ code: "email", message: "Enter a valid e-mail address." }] } },
    }));
    const error = await api("POST", "/api/x", {}).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    const problem = error as ApiError;
    expect(problem.status).toBe(400);
    expect(problem.code).toBe("validation");
    expect(problem.message).toBe("Some fields need attention.");
    expect(problem.fieldErrors.email![0]!.code).toBe("email");
  });
});

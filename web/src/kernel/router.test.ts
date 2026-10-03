import { describe, expect, it } from "vitest";
import { matchRoute, routes } from "./router";

describe("routes", () => {
  it("collects every module's screens with their permissions", () => {
    expect(matchRoute("/")).toBeDefined();
    expect(matchRoute("/identity/users")?.permission).toBe("identity.users.read");
    expect(matchRoute("/identity/users/")?.permission).toBe("identity.users.read");
    expect(matchRoute("/tenancy/tenant")?.permission).toBe("tenancy.tenant.read");
    expect(matchRoute("/nope")).toBeUndefined();
    expect(routes.every((r) => r.path === "/" || r.permission)).toBe(true);
  });
});

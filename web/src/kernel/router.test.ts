import { describe, expect, it } from "vitest";
import { matchRoute, recordAddress, recordInAddress, routes, splitPath } from "./router";

describe("routes", () => {
  it("collects every module's screens with their permissions", () => {
    expect(matchRoute("/")).toBeDefined();
    expect(matchRoute("/identity/users")?.permission).toBe("identity.users.read");
    expect(matchRoute("/identity/users/")?.permission).toBe("identity.users.read");
    expect(matchRoute("/tenancy/tenant")?.permission).toBe("tenancy.tenant.read");
    expect(matchRoute("/nope")).toBeUndefined();
    expect(routes.every((r) => r.path === "/" || r.permission)).toBe(true);
  });


  it("gives each record of a list screen its own address: the screen's path and the record's id, or new", () => {
    const id = "0190a000-0000-7000-8000-000000000009";
    expect(splitPath(`/tenancy/companies/${id}`)).toMatchObject({ screen: "/tenancy/companies", record: id });
    expect(matchRoute(`/tenancy/companies/${id}`)?.permission).toBe("tenancy.companies.read");
    expect(splitPath("/identity/users/new")).toMatchObject({ screen: "/identity/users", record: "new" });
    // Only an id or "new": any other segment is not a screen.
    expect(matchRoute("/identity/users/nope")).toBeUndefined();
    expect(matchRoute(`/nope/${id}`)).toBeUndefined();

    window.history.replaceState(null, "", `/tenancy/companies/${id}?q=noor`);
    expect(recordInAddress()).toBe(id);
    expect(recordAddress(null, "q=noor")).toBe("/tenancy/companies?q=noor");
    expect(recordAddress("new", "open=x&q=noor")).toBe("/tenancy/companies/new?q=noor");
    // An older link's ?open= still opens the record.
    window.history.replaceState(null, "", `/tenancy/companies?open=${id}`);
    expect(recordInAddress()).toBe(id);
    expect(recordAddress(id, `open=${id}`)).toBe(`/tenancy/companies/${id}`);
    // So does the users and roles screens' older ?new (one new-record address: <screen>/new).
    window.history.replaceState(null, "", "/identity/users?new&q=noor");
    expect(recordInAddress()).toBe("new");
    expect(recordAddress("new", "new=&q=noor")).toBe("/identity/users/new?q=noor");
    expect(recordAddress(null, "new=&q=noor")).toBe("/identity/users?q=noor");
  });
});

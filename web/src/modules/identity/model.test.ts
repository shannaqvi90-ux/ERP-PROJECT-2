import { describe, expect, it } from "vitest";
import { allSelected, buildMatrix, completeEmail, domainOf, nameFromEmail, toggleAll, type Permission } from "./model";

const p = (key: string, resourceLabel: string, label: string): Permission => {
  const [module = "", resource = "", action = ""] = key.split(".");
  return { key, module, resource, action, label, resourceLabel, moduleLabel: module === "identity" ? "Users and access" : "Workspace" };
};

const catalogue = [
  p("identity.users.read", "Users", "View users"),
  p("identity.users.create", "Users", "Create users"),
  p("identity.users.resetPassword", "Users", "Reset other users' passwords"),
  p("identity.roles.read", "Roles", "View roles"),
  p("identity.roles.delete", "Roles", "Delete roles"),
  p("tenancy.tenant.read", "View the workspace", "View the workspace"),
];

describe("permission matrix", () => {
  it("groups by module, one row per resource, common actions in columns and the rest in other", () => {
    const matrix = buildMatrix(catalogue);
    expect(matrix.map((m) => m.module)).toEqual(["identity", "tenancy"]);
    const users = matrix[0]!.rows.find((r) => r.resource === "users")!;
    expect(users.cells.read?.key).toBe("identity.users.read");
    expect(users.cells.create?.key).toBe("identity.users.create");
    expect(users.cells.delete).toBeUndefined();
    expect(users.other.map((o) => o.key)).toEqual(["identity.users.resetPassword"]);
    expect(matrix[0]!.permissions).toHaveLength(5);
  });

  it("filters rows by any word of the label, resource, module or key, in any script", () => {
    expect(buildMatrix(catalogue, "reset").flatMap((m) => m.rows.map((r) => r.resource))).toEqual(["users"]);
    expect(buildMatrix(catalogue, "workspace").map((m) => m.module)).toEqual(["tenancy"]);
    expect(buildMatrix(catalogue, "roles delete")[0]!.permissions.map((x) => x.key)).toEqual(["identity.roles.read", "identity.roles.delete"]);
    expect(buildMatrix([p("identity.users.read", "المستخدمون", "عرض المستخدمين")], "المستخدمين")).toHaveLength(1);
    expect(buildMatrix(catalogue, "nothing-like-this")).toEqual([]);
  });

  it("bulk toggles add or remove exactly the given keys", () => {
    const start = new Set(["identity.users.read", "tenancy.tenant.read"]);
    const on = toggleAll(start, ["identity.roles.read", "identity.roles.delete"], true);
    expect([...on].sort()).toEqual(["identity.roles.delete", "identity.roles.read", "identity.users.read", "tenancy.tenant.read"]);
    expect(allSelected(on, ["identity.roles.read", "identity.roles.delete"])).toBe(true);
    const off = toggleAll(on, ["identity.users.read"], false);
    expect(off.has("identity.users.read")).toBe(false);
    expect(start.size).toBe(2);
    expect(allSelected(start, [])).toBe(false);
  });
});

describe("new user helpers", () => {
  it("suggests a display name from the e-mail", () => {
    expect(nameFromEmail("hessa.clerk@demo-trading.example")).toBe("Hessa Clerk");
    expect(nameFromEmail("omar_al-haddad")).toBe("Omar Al Haddad");
    expect(nameFromEmail("")).toBe("");
  });

  it("completes a bare local part with the workspace's domain and leaves full addresses alone", () => {
    expect(completeEmail("hessa.clerk", "demo-trading.example")).toBe("hessa.clerk@demo-trading.example");
    expect(completeEmail(" x@y.example ", "demo-trading.example")).toBe("x@y.example");
    expect(completeEmail("hessa", null)).toBe("hessa");
    expect(completeEmail("", "a.example")).toBe("");
    expect(domainOf("admin@alnoor.example")).toBe("alnoor.example");
    expect(domainOf("broken")).toBeNull();
  });
});

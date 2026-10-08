import { describe, expect, it } from "vitest";
import {
  allSelected,
  buildMatrix,
  completeEmail,
  domainOf,
  nameFromEmail,
  roleActions,
  toggleAll,
  userActions,
  userName,
  withImpliedReads,
  type Permission,
} from "./model";

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

// Every permission of the identity module, as the server's catalogue holds them today.
const identity = [
  "identity.profile.update",
  "identity.roles.create",
  "identity.roles.delete",
  "identity.roles.read",
  "identity.roles.update",
  "identity.signIns.read",
  "identity.users.create",
  "identity.users.delete",
  "identity.users.read",
  "identity.users.resetPassword",
  "identity.users.update",
];
const without = (...missing: string[]) => new Set(identity.filter((x) => !missing.includes(x)));

describe("what the screens offer for a role", () => {
  const custom = { isSystem: false, permissions: ["identity.users.read"] };

  it("needs the permission of each action, exactly: removing one hides exactly that action", () => {
    expect(roleActions(custom, new Set(identity))).toEqual({ edit: true, copy: true, delete: true, beyondOwn: false });
    expect(roleActions(custom, without("identity.roles.delete"))).toEqual({ edit: true, copy: true, delete: false, beyondOwn: false });
    expect(roleActions(custom, without("identity.roles.update"))).toEqual({ edit: false, copy: true, delete: true, beyondOwn: false });
    expect(roleActions(custom, without("identity.roles.create"))).toEqual({ edit: true, copy: false, delete: true, beyondOwn: false });
    expect(roleActions(undefined, without("identity.roles.create")).edit).toBe(false);
    expect(roleActions(undefined, new Set(identity)).edit).toBe(true);
  });

  it("offers nothing but viewing for a role granting a permission the user lacks, and only copying for a system role", () => {
    const strong = { isSystem: false, permissions: ["identity.users.read", "identity.users.resetPassword"] };
    expect(roleActions(strong, without("identity.users.resetPassword"))).toEqual({ edit: false, copy: false, delete: false, beyondOwn: true });
    expect(roleActions({ isSystem: true, permissions: identity }, new Set(identity))).toEqual({ edit: false, copy: true, delete: false, beyondOwn: false });
  });
});

describe("what the screens offer for another user", () => {
  const clerkRole = { id: "r-clerk", permissions: ["identity.users.read"] };
  const adminRole = { id: "r-admin", permissions: identity };
  const clerk = { id: "u-clerk", roleIds: ["r-clerk"], lastSignInAt: null };

  it("needs the permission of each action, exactly", () => {
    const all = userActions(clerk, [clerkRole, adminRole], new Set(identity), "me");
    expect(all).toMatchObject({ edit: true, resetPassword: true, signOutEverywhere: true, unblock: true, delete: true, beyondOwn: false });
    expect(userActions(clerk, [clerkRole], without("identity.users.delete"), "me")).toMatchObject({ delete: false, resetPassword: true, edit: true });
    expect(userActions(clerk, [clerkRole], without("identity.users.resetPassword"), "me")).toMatchObject({ resetPassword: false, delete: true, edit: true });
    expect(userActions(clerk, [clerkRole], without("identity.users.update"), "me")).toMatchObject({ edit: false, signOutEverywhere: false, unblock: false, resetPassword: true });
  });

  it("offers nothing that acts on someone stronger, on oneself, or deletes someone who has signed in", () => {
    const admin = { id: "u-admin", roleIds: ["r-admin"], lastSignInAt: null };
    const weaker = without("identity.users.delete");
    expect(userActions(admin, [clerkRole, adminRole], weaker, "me")).toMatchObject({ beyondOwn: true, edit: false, resetPassword: false, signOutEverywhere: false, delete: false });
    expect(userActions({ ...clerk, id: "me" }, [clerkRole], new Set(identity), "me")).toMatchObject({ self: true, resetPassword: false, delete: false, edit: true });
    expect(userActions({ ...clerk, lastSignInAt: "2026-10-01T08:00:00Z" }, [clerkRole], new Set(identity), "me").delete).toBe(false);
  });

  it("treats roles it cannot read as beyond the signed-in user (a clerk who may not read roles opens the Administrator)", () => {
    const admin = { id: "u-admin", roleIds: ["r-admin"], lastSignInAt: null };
    const clerkOnly = new Set(["identity.users.read", "identity.users.update"]);
    expect(userActions(admin, [], clerkOnly, "me")).toMatchObject({ beyondOwn: true, edit: false, signOutEverywhere: false, unblock: false });
    const inOneCompany = { id: "u-mgr", roleIds: [], companyRoles: [{ roleId: "r-manager", companyId: "c-x" }], lastSignInAt: null };
    expect(userActions(inOneCompany, [], clerkOnly, "me")).toMatchObject({ beyondOwn: true, edit: false, signOutEverywhere: false });
    const noRoles = { id: "u-plain", roleIds: [], lastSignInAt: null };
    expect(userActions(noRoles, [], clerkOnly, "me")).toMatchObject({ beyondOwn: false, edit: true, signOutEverywhere: true });
  });

  it("names a user in the screen's language", () => {
    expect(userName({ displayName: "Majid Anil Pillai", displayNameAr: "ماجد أنيل بيلاي" }, "ar")).toBe("ماجد أنيل بيلاي");
    expect(userName({ displayName: "Majid Anil Pillai", displayNameAr: "ماجد أنيل بيلاي" }, "en")).toBe("Majid Anil Pillai");
    expect(userName({ displayName: "Mark Smith", displayNameAr: null }, "ar")).toBe("Mark Smith");
  });
});

describe("permissions of modules that arrive later", () => {
  // A contacts module (wave 2) brings its own permissions; the matrix and the helpers take them as they come.
  const contacts = [
    p("contacts.contacts.read", "Contacts", "View contacts"),
    p("contacts.contacts.create", "Contacts", "Create contacts"),
    p("contacts.contacts.update", "Contacts", "Change contacts"),
    p("contacts.contacts.delete", "Contacts", "Delete contacts"),
    p("contacts.contacts.export", "Contacts", "Export contacts"),
  ];

  it("gets a block of its own with the usual columns", () => {
    const matrix = buildMatrix([...catalogue, ...contacts]);
    const block = matrix.find((m) => m.module === "contacts")!;
    expect(block.rows).toHaveLength(1);
    expect(block.rows[0]!.cells.create?.key).toBe("contacts.contacts.create");
    expect(block.rows[0]!.other.map((o) => o.key)).toEqual(["contacts.contacts.export"]);
  });

  it("turns on viewing with any other action, never the reverse, and only what may be granted", () => {
    const all = [...catalogue, ...contacts];
    const may = () => true;
    expect([...withImpliedReads(new Set(["contacts.contacts.create"]), ["contacts.contacts.create"], all, may)].sort()).toEqual([
      "contacts.contacts.create",
      "contacts.contacts.read",
    ]);
    expect([...withImpliedReads(new Set(["contacts.contacts.read"]), ["contacts.contacts.read"], all, may)]).toEqual(["contacts.contacts.read"]);
    expect([...withImpliedReads(new Set(["contacts.contacts.create"]), ["contacts.contacts.create"], all, (k) => k !== "contacts.contacts.read")]).toEqual([
      "contacts.contacts.create",
    ]);
    // No view permission in the catalogue for the resource: nothing is invented.
    expect([...withImpliedReads(new Set(["identity.users.resetPassword"]), ["identity.users.resetPassword"], [p("identity.users.resetPassword", "Users", "Reset")], may)]).toEqual([
      "identity.users.resetPassword",
    ]);
  });
});

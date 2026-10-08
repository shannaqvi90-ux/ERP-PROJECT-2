/** Shapes of the identity API (see /api/openapi/v1.json) and pure helpers the screens share. */

export type User = {
  id: string;
  email: string;
  displayName: string;
  /** The name in Arabic script, shown on Arabic screens when given. */
  displayNameAr?: string | null;
  language: "en" | "ar";
  isActive: boolean;
  roleIds: string[];
  lastSignInAt: string | null;
  createdAt: string;
  version: number;
  pendingSetup: boolean;
  /** Roles held in one company only (those of the companies the signed-in user works in). */
  companyRoles?: CompanyRole[];
  /** The user also holds roles in companies the signed-in user does not work in. */
  rolesElsewhere?: boolean;
  setupCode?: string;
  setupCodeExpiresAt?: string;
};

/** A role held in one company only. */
export type CompanyRole = { roleId: string; companyId: string };

/** A company the signed-in user works in. */
export type Company = { id: string; code: string; legalNameEn: string; legalNameAr: string };

/** The company a user starts in (null: their first company by code) and the companies they may work in. */
export type DefaultCompany = { userId: string; companyId: string | null; companies: Company[]; version: number };

export type UserPage = { items: User[]; total: number };

export type Role = {
  id: string;
  nameEn: string;
  nameAr: string;
  permissions: string[];
  isSystem: boolean;
  userCount: number;
  version: number;
};

export type RolePage = { items: Role[]; total: number };

export type Permission = {
  key: string;
  module: string;
  label: string;
  moduleLabel: string;
  resource: string;
  action: string;
  resourceLabel: string;
};

export type AccessView = {
  userId: string;
  /** Roles held in every company (companyId null) or in one. */
  roles: { id: string; nameEn: string; nameAr: string; isSystem: boolean; companyId?: string | null }[];
  permissions: {
    key: string;
    module: string;
    label: string;
    moduleLabel: string;
    grantedBy: string[];
    grants?: { roleId: string; companyId: string | null }[];
  }[];
  /** The companies named by roles held in one company. */
  companies?: Company[];
  /** The user also holds roles in companies the signed-in user does not work in (not shown). */
  rolesElsewhere?: boolean;
};

/** A company's short name in the screen's language. */
export const companyName = (company: Pick<Company, "code" | "legalNameEn" | "legalNameAr">, language: string) =>
  `${company.code} · ${language === "ar" ? company.legalNameAr || company.legalNameEn : company.legalNameEn || company.legalNameAr}`;

/** True when the two lists hold the same company roles, in any order. */
export function sameCompanyRoles(a: CompanyRole[], b: CompanyRole[]) {
  const key = (x: CompanyRole) => `${x.roleId}/${x.companyId}`;
  const left = new Set(a.map(key));
  return left.size === new Set(b.map(key)).size && b.every((x) => left.has(key(x)));
}

export type SignIn = {
  id: string;
  occurredAt: string;
  outcome: "succeeded" | "failed" | "throttled" | "inactive" | "expired";
  ipAddress: string | null;
  userAgent: string | null;
  sessionActive: boolean;
};

export type SignInHistory = {
  items: SignIn[];
  total: number;
  paused: { source: string; until: string; failures: number }[];
};

export type ResetResult = { mustChangePassword: boolean; setupCode?: string; setupCodeExpiresAt?: string; sessionsEnded: number };

/** The actions the matrix gives a column of their own, in order; any other action goes to "other". */
export const matrixActions = ["read", "create", "update", "delete"] as const;

export type MatrixRow = {
  resource: string;
  label: string;
  cells: Partial<Record<(typeof matrixActions)[number], Permission>>;
  other: Permission[];
  /** The row's permissions the filter matches (every one without a filter): the ones shown, and
   * the only ones a bulk toggle changes. */
  matching: Permission[];
};

/** A module's block: its rows, every permission of those rows, and the ones the filter matches. */
export type MatrixModule = { module: string; label: string; rows: MatrixRow[]; permissions: Permission[]; matching: Permission[] };

/**
 * The permission matrix: one block per module, one row per resource, a column per common action
 * and an "other" cell for the rest. With a filter, a permission matches when its label, key or its
 * resource's name contain every word of the filter (case-insensitive, any script), and only rows
 * with a matching permission are kept. Only matching permissions are shown and toggled in bulk
 * (critic p03 round 5: searching "view" kept whole rows, so "Select all shown" also ticked
 * deleting users, resetting passwords and changing the workspace).
 */
export function buildMatrix(permissions: Permission[], filter = ""): MatrixModule[] {
  const words = filter.toLocaleLowerCase().split(/\s+/).filter(Boolean);
  const modules = new Map<string, MatrixModule>();
  for (const p of permissions) {
    let block = modules.get(p.module);
    if (!block) {
      block = { module: p.module, label: p.moduleLabel, rows: [], permissions: [], matching: [] };
      modules.set(p.module, block);
    }
    let row = block.rows.find((r) => r.resource === p.resource);
    if (!row) {
      row = { resource: p.resource, label: p.resourceLabel, cells: {}, other: [], matching: [] };
      block.rows.push(row);
    }
    if ((matrixActions as readonly string[]).includes(p.action)) row.cells[p.action as (typeof matrixActions)[number]] = p;
    else row.other.push(p);
  }
  const result: MatrixModule[] = [];
  for (const block of modules.values()) {
    const rows = block.rows
      .map((row) => ({
        ...row,
        matching: rowPermissions(row).filter((p) => {
          const text = [row.label, row.resource, p.label, p.key].join(" ").toLocaleLowerCase();
          return words.every((w) => text.includes(w));
        }),
      }))
      .filter((row) => row.matching.length > 0);
    if (rows.length === 0) continue;
    result.push({ ...block, rows, permissions: rows.flatMap(rowPermissions), matching: rows.flatMap((row) => row.matching) });
  }
  return result;
}

export function rowPermissions(row: MatrixRow): Permission[] {
  return [...matrixActions.map((a) => row.cells[a]).filter((p): p is Permission => !!p), ...row.other];
}

/** Turn every permission in `keys` on or off, leaving the rest of the selection alone. */
export function toggleAll(selected: ReadonlySet<string>, keys: string[], on: boolean): Set<string> {
  const next = new Set(selected);
  for (const key of keys) {
    if (on) next.add(key);
    else next.delete(key);
  }
  return next;
}

/** True when every key is selected (a bulk toggle then clears them). */
export function allSelected(selected: ReadonlySet<string>, keys: string[]): boolean {
  return keys.length > 0 && keys.every((k) => selected.has(k));
}

/** "hessa.clerk" -> "Hessa Clerk": a display name suggested from an e-mail's local part. */
export function nameFromEmail(email: string): string {
  const local = email.split("@")[0] ?? "";
  return local
    .split(/[._\-+]+/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toLocaleUpperCase() + part.slice(1))
    .join(" ");
}

/** An address typed without a domain gets the workspace's usual one (the domain is shown while typing). */
export function completeEmail(typed: string, domain: string | null): string {
  const value = typed.trim();
  if (!value || value.includes("@") || !domain) return value;
  return `${value}@${domain}`;
}

export function domainOf(email: string | undefined): string | null {
  const at = email?.lastIndexOf("@") ?? -1;
  return email && at > 0 ? email.slice(at + 1) : null;
}

export const isEmail = (value: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());

/** The role name in the screen's language. */
export const roleName = (role: { nameEn: string; nameAr: string }, language: string) => (language === "ar" ? role.nameAr : role.nameEn);

/** True when the key event should not be taken as a screen shortcut (the user is typing). */
export function isTyping(event: KeyboardEvent | { target: EventTarget | null }): boolean {
  const target = event.target as HTMLElement | null;
  if (!target || typeof target.closest !== "function") return false;
  return !!target.closest("input, textarea, select, [contenteditable='true']");
}

/** What the signed-in user may do with a role on screen, mirroring the server's rules. */
export type RoleActions = {
  /** Change the names and the permission matrix (or, for a new role, create it). */
  edit: boolean;
  copy: boolean;
  delete: boolean;
  /** The role grants something the user does not hold: only someone who holds all of it may change, copy or delete it. */
  beyondOwn: boolean;
};

/**
 * The actions offered for a role (undefined: a new one). A system role is only ever copied. A
 * role granting a permission the user lacks is shown read-only and can be neither copied nor
 * deleted, because the server refuses both. Each action also needs its own permission.
 */
export function roleActions(role: Pick<Role, "isSystem" | "permissions"> | undefined, held: ReadonlySet<string>): RoleActions {
  if (!role) return { edit: held.has("identity.roles.create"), copy: false, delete: false, beyondOwn: false };
  const beyondOwn = role.permissions.some((p) => !held.has(p));
  return {
    edit: !role.isSystem && !beyondOwn && held.has("identity.roles.update"),
    copy: !beyondOwn && held.has("identity.roles.create"),
    delete: !role.isSystem && !beyondOwn && held.has("identity.roles.delete"),
    beyondOwn,
  };
}

/** The user's name in the screen's language: the Arabic name on Arabic screens when there is one. */
export const userName = (user: { displayName: string; displayNameAr?: string | null }, language: string) =>
  language === "ar" && user.displayNameAr ? user.displayNameAr : user.displayName;

/**
 * What the signed-in user may do to another user's account on screen, mirroring the server: every
 * action needs its own permission, none acts on oneself here, and none acts on someone whose roles
 * (in every company or in one) grant a permission the signed-in user lacks, or who holds roles in
 * companies the signed-in user does not work in (either would be a way to take the account over).
 * Deleting is only for someone who has never signed in. A role that is not loaded (the user may
 * not read roles) cannot be judged from here, so it counts as beyond too (critic p03 round 5: a
 * clerk who may not read roles was offered Save and "Sign out everywhere" on the Administrator,
 * and the server refused both).
 */
export function userActions(
  user: Pick<User, "id" | "roleIds" | "lastSignInAt" | "companyRoles" | "rolesElsewhere">,
  roles: Pick<Role, "id" | "permissions">[],
  held: ReadonlySet<string>,
  selfId: string | null,
) {
  const self = user.id === selfId;
  // Roles in every company and roles in one company alike; roles that are not loaded and roles in
  // companies the signed-in user does not work in cannot be judged from here, so they count as beyond.
  const grantsBeyond = (id: string) => roles.find((r) => r.id === id)?.permissions.some((p) => !held.has(p)) ?? true;
  const beyondOwn = user.roleIds.some(grantsBeyond) || (user.companyRoles ?? []).some((c) => grantsBeyond(c.roleId)) || user.rolesElsewhere === true;
  const others = !self && !beyondOwn;
  return {
    self,
    beyondOwn,
    edit: held.has("identity.users.update") && !beyondOwn,
    resetPassword: others && held.has("identity.users.resetPassword"),
    signOutEverywhere: others && held.has("identity.users.update"),
    unblock: others && held.has("identity.users.update"),
    delete: others && held.has("identity.users.delete") && user.lastSignInAt === null,
  };
}

/**
 * Turning on any action of a resource (create, change, delete, …) also turns on viewing it, when
 * the catalogue has a view permission for that resource and the user may grant it: a role that may
 * create contacts but not see them is never what anyone means. Turning permissions off never
 * removes anything else.
 */
export function withImpliedReads(
  next: Set<string>,
  turnedOn: string[],
  permissions: Pick<Permission, "key">[],
  canChange: (key: string) => boolean,
): Set<string> {
  const known = new Set(permissions.map((p) => p.key));
  for (const key of turnedOn) {
    const parts = key.split(".");
    if (parts.length !== 3 || parts[2] === "read") continue;
    const read = `${parts[0]}.${parts[1]}.read`;
    if (known.has(read) && canChange(read)) next.add(read);
  }
  return next;
}

/** New user / new role: Alt+N works while typing (the lists focus their search on arrival); a
 * plain "n" works when no field has the focus. */
export const newRecordChord = "Alt+KeyN";

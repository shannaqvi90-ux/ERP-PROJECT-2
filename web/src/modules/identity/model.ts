/** Shapes of the identity API (see /api/openapi/v1.json) and pure helpers the screens share. */

export type User = {
  id: string;
  email: string;
  displayName: string;
  language: "en" | "ar";
  isActive: boolean;
  roleIds: string[];
  lastSignInAt: string | null;
  createdAt: string;
  version: number;
  pendingSetup: boolean;
  setupCode?: string;
  setupCodeExpiresAt?: string;
};

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
  roles: { id: string; nameEn: string; nameAr: string; isSystem: boolean }[];
  permissions: { key: string; module: string; label: string; moduleLabel: string; grantedBy: string[] }[];
};

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
};

export type MatrixModule = { module: string; label: string; rows: MatrixRow[]; permissions: Permission[] };

/**
 * The permission matrix: one block per module, one row per resource, a column per common action
 * and an "other" cell for the rest. With a filter, only rows whose resource name, permission labels
 * or keys contain every word of it are kept (case-insensitive, any script).
 */
export function buildMatrix(permissions: Permission[], filter = ""): MatrixModule[] {
  const words = filter.toLocaleLowerCase().split(/\s+/).filter(Boolean);
  const modules = new Map<string, MatrixModule>();
  for (const p of permissions) {
    let block = modules.get(p.module);
    if (!block) {
      block = { module: p.module, label: p.moduleLabel, rows: [], permissions: [] };
      modules.set(p.module, block);
    }
    let row = block.rows.find((r) => r.resource === p.resource);
    if (!row) {
      row = { resource: p.resource, label: p.resourceLabel, cells: {}, other: [] };
      block.rows.push(row);
    }
    if ((matrixActions as readonly string[]).includes(p.action)) row.cells[p.action as (typeof matrixActions)[number]] = p;
    else row.other.push(p);
  }
  const result: MatrixModule[] = [];
  for (const block of modules.values()) {
    const rows = block.rows.filter((row) => {
      if (words.length === 0) return true;
      const text = [row.label, row.resource, ...rowPermissions(row).flatMap((p) => [p.label, p.key])]
        .join(" ")
        .toLocaleLowerCase();
      return words.every((w) => text.includes(w));
    });
    if (rows.length === 0) continue;
    result.push({ ...block, rows, permissions: rows.flatMap(rowPermissions) });
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

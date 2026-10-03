import type { ListColumn, ListDefinition } from "../kernel/lists/model";

/** Test fixtures: the list definitions the identity screens fetch (mirrors UsersList.cs and RolesList.cs). */
function column(key: string, labelKey: string, type: ListColumn["type"], flags: Partial<ListColumn> = {}): ListColumn {
  return { key, labelKey, type, sortable: false, filterable: false, groupable: false, aggregate: false, hidden: false, choices: [], operators: [], ...flags };
}

export const listDefinitions: Record<string, ListDefinition> = {
  "identity.users": {
    key: "identity.users",
    labelKey: "identity.users.title",
    endpoint: "/api/identity/users",
    columns: [
      column("displayName", "identity.users.name", "text", { sortable: true, filterable: true }),
      column("email", "identity.users.email", "text", { sortable: true, filterable: true }),
      column("language", "identity.users.language", "choice", {
        filterable: true,
        groupable: true,
        choices: [
          { value: "en", labelKey: "identity.language.en" },
          { value: "ar", labelKey: "identity.language.ar" },
        ],
      }),
      column("isActive", "identity.users.status", "boolean", { filterable: true, groupable: true }),
      column("lastSignInAt", "identity.users.lastSignIn", "dateTime", { sortable: true, filterable: true }),
      column("roleIds", "identity.users.roles", "choice"),
      column("createdAt", "identity.users.created", "dateTime", { sortable: true, filterable: true, hidden: true }),
    ],
    searchFields: ["displayName", "email"],
    defaultSort: "-createdAt",
    presets: [],
    canShare: false,
    maxTake: 200,
  },
  "identity.roles": {
    key: "identity.roles",
    labelKey: "identity.roles.title",
    endpoint: "/api/identity/roles",
    columns: [
      column("nameEn", "identity.roles.name", "text", { sortable: true, filterable: true }),
      column("nameAr", "identity.roles.nameAr", "text", { sortable: true, filterable: true, hidden: true }),
      column("isSystem", "identity.roles.kind", "boolean", { sortable: true, filterable: true, groupable: true }),
      column("userCount", "identity.roles.users", "number", { sortable: true, filterable: true, aggregate: true }),
      column("permissions", "identity.roles.permissions", "choice"),
    ],
    searchFields: ["nameEn", "nameAr"],
    defaultSort: "-isSystem,nameEn",
    presets: [],
    canShare: false,
    maxTake: 200,
  },
};

/** Replies for the list framework's own endpoints (definition, saved views), or undefined. */
export function listReply(method: string, url: string): { status: number; body: unknown } | undefined {
  const match = /^\/api\/lists\/([^/?]+)\/(definition|views)(\?|$)/.exec(url);
  if (!match) return undefined;
  if (match[2] === "views") return method === "GET" ? { status: 200, body: { items: [] } } : undefined;
  const definition = listDefinitions[match[1] ?? ""];
  return definition ? { status: 200, body: definition } : { status: 404, body: {} };
}

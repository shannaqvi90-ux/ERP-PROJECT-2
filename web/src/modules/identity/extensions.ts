import { api } from "../../kernel/api";
import type { ModuleExtensions } from "../../kernel/extensions";
import { recordPath } from "../../kernel/router";

type UserRow = { id: string; email: string; displayName: string };
type RoleRow = { id: string; nameEn: string; nameAr: string };

/** The command palette finds users by name or e-mail, and roles by their English or Arabic name,
 * for those allowed to see them; opens the one chosen in its list (narrowed to it, its record
 * open), and leads to the list narrowed to every match. */
export const extensions: ModuleExtensions = {
  palette: [
    {
      key: "identity.users",
      labelKey: "identity.menu.users",
      permission: "identity.users.read",
      minLength: 2,
      search: async (query, { signal }) => {
        const params = new URLSearchParams({ search: query, take: "5" });
        const page = await api<{ items: UserRow[]; total?: number }>("GET", `/api/identity/users?${params}`, undefined, { signal });
        return {
          total: page.total,
          items: page.items.map((u) => ({
            id: u.id,
            title: u.displayName,
            subtitle: u.email,
            // The users list narrowed to this user, with the user's details open.
            path: recordPath("/identity/users", u.id, `${new URLSearchParams({ q: u.email })}`),
          })),
        };
      },
      showAll: (query) => `/identity/users?${new URLSearchParams({ q: query })}`,
    },
    {
      key: "identity.roles",
      labelKey: "identity.menu.roles",
      permission: "identity.roles.read",
      minLength: 2,
      search: async (query, { language, signal }) => {
        const params = new URLSearchParams({ search: query, take: "5" });
        const page = await api<{ items: RoleRow[]; total?: number }>("GET", `/api/identity/roles?${params}`, undefined, { signal });
        return {
          total: page.total,
          items: page.items.map((r) => {
            // The name in the screen's language first, the other language's beside it.
            const [first, second] = language === "ar" ? [r.nameAr || r.nameEn, r.nameEn] : [r.nameEn || r.nameAr, r.nameAr];
            return {
              id: r.id,
              title: first,
              subtitle: second && second !== first ? second : undefined,
              path: recordPath("/identity/roles", r.id, `${new URLSearchParams({ q: first })}`),
            };
          }),
        };
      },
      showAll: (query) => `/identity/roles?${new URLSearchParams({ q: query })}`,
    },
  ],
};

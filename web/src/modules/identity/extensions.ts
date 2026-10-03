import { api } from "../../kernel/api";
import type { ModuleExtensions } from "../../kernel/extensions";

type UserRow = { id: string; email: string; displayName: string };

/** The command palette finds users by name or e-mail for those allowed to see users, opens the
 * one chosen, and leads to the users list narrowed to every match. */
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
            path: `/identity/users?${new URLSearchParams({ q: u.email, open: u.id })}`,
          })),
        };
      },
      showAll: (query) => `/identity/users?${new URLSearchParams({ q: query })}`,
    },
  ],
};

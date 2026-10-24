import { api } from "../../kernel/api";
import type { ModuleExtensions } from "../../kernel/extensions";
import { recordPath } from "../../kernel/router";
import { contactName, contactsPath, type ContactRow, type ListPage } from "./types";

/**
 * What the contacts module adds to the shell: in the command palette, contacts themselves (found by
 * name in either script, e-mail, number, reference or tax number, best match first; Enter opens the
 * contact's form), New contact and the list printed or exported from anywhere; and G then O opens
 * Contacts.
 */
export const extensions: ModuleExtensions = {
  actions: [
    {
      key: "contacts.contacts.new",
      labelKey: "contacts.contact.new",
      keywordsKey: "contacts.action.newKeywords",
      permission: "contacts.contacts.create",
      path: contactsPath,
      command: "contacts.screen.new",
    },
    { key: "contacts.contacts.print", labelKey: "contacts.action.printList", keywordsKey: "contacts.action.printKeywords", permission: "contacts.contacts.read", path: contactsPath, command: "lists.print" },
  ],
  goTo: [{ key: "contacts.contacts", menu: "contacts.contacts", code: "KeyO" }],
  palette: [
    {
      key: "contacts.contacts",
      labelKey: "contacts.menu.contacts",
      permission: "contacts.contacts.read",
      minLength: 2,
      search: async (query, { language, signal }) => {
        const params = new URLSearchParams({ search: query, take: "5" });
        const page = await api<ListPage<ContactRow>>("GET", `/api/contacts/contacts?${params}`, undefined, { signal });
        return {
          total: page.total,
          items: page.items.map((c) => ({
            id: c.id,
            title: contactName(c, language),
            subtitle: [c.reference, c.kind === "person" ? contactName({ nameEn: c.organisationName ?? "", nameAr: c.organisationNameAr ?? "" }, language) : null, c.email ?? c.mobile ?? c.phone]
              .filter(Boolean)
              .join(" · "),
            path: recordPath(contactsPath, c.id, `${new URLSearchParams({ q: query })}`),
          })),
        };
      },
      showAll: (query) => `${contactsPath}?${new URLSearchParams({ q: query })}`,
    },
  ],
};

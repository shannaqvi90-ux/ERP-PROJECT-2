import type { ModuleExtensions } from "../../kernel/extensions";
import { ImportButton } from "./ImportButton";

/** What the import and export module adds to the shell: an Import button on every list of records that can be imported
 * (its toolbar; Alt+I), and the full export screen (G then E opens it; its Alt+N starts an export). */
export const extensions: ModuleExtensions = {
  // Kinds of record that can be imported, with the permission that creates them (the same as the server's
  // registration). Kept inside the registration: a module-level map is state the G1 client-state gate judges.
  listActions: Object.entries({
    "contacts.contacts": { create: "contacts.contacts.create", titleKey: "importexport.kind.contacts" },
  } as Record<string, { create: string; titleKey: string }>).map(([table, kind]) => ({
    key: `importexport.import.${table}`,
    listKey: table,
    order: 50,
    permission: kind.create,
    component: ({ reload }) => <ImportButton table={table} titleKey={kind.titleKey} reload={reload} />,
  })),
  goTo: [{ key: "importexport.exports", menu: "importexport.exports", code: "KeyE" }],
};

import type { ModuleExtensions } from "../../kernel/extensions";
import { ImportButton } from "./ImportButton";
import { importKinds } from "./types";

/** What the import and export module adds to the shell: an Import button on every list of records that can be imported
 * (its toolbar; Alt+I), and the full export screen (G then E opens it; its Alt+N starts an export). */
export const extensions: ModuleExtensions = {
  listActions: Object.entries(importKinds).map(([table, kind]) => ({
    key: `importexport.import.${table}`,
    listKey: table,
    order: 50,
    permission: kind.create,
    component: ({ reload }) => <ImportButton table={table} titleKey={kind.titleKey} reload={reload} />,
  })),
  goTo: [{ key: "importexport.exports", menu: "importexport.exports", code: "KeyE" }],
};

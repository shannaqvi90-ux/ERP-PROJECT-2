import type { ModuleExtensions } from "../../kernel/extensions";
import { ImportButton } from "./ImportButton";
import { importKinds } from "./types";

/** What the import and export module adds to the shell: an Import button on every list of records that can be imported
 * (its toolbar; Alt+I), and the full export screen found by typing its name in the command palette (G then X opens it). */
export const extensions: ModuleExtensions = {
  listActions: Object.entries(importKinds).map(([table, kind]) => ({
    key: `importexport.import.${table}`,
    listKey: table,
    order: 50,
    permission: kind.create,
    component: ({ reload }) => <ImportButton table={table} titleKey={kind.titleKey} reload={reload} />,
  })),
  actions: [
    { key: "importexport.exports.start", labelKey: "importexport.exports.start", keywordsKey: "importexport.exports.keywords", permission: "importexport.exports.create", path: "/importexport/exports", command: "importexport.exports.start" },
  ],
  goTo: [{ key: "importexport.exports", menu: "importexport.exports", code: "KeyX" }],
};

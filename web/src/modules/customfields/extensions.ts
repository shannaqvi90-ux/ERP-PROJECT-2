import type { ModuleExtensions } from "../../kernel/extensions";
import { fieldsPath } from "./types";

/**
 * What the custom fields module adds to the shell: G then F opens Custom fields; New custom field
 * and the list printed from anywhere in the command palette.
 */
export const extensions: ModuleExtensions = {
  actions: [
    {
      key: "customfields.fields.new",
      labelKey: "customfields.field.new",
      keywordsKey: "customfields.action.newKeywords",
      permission: "customfields.fields.manage",
      path: fieldsPath,
      command: "customfields.screen.new",
    },
    { key: "customfields.fields.print", labelKey: "customfields.action.printList", keywordsKey: "customfields.action.printKeywords", permission: "customfields.fields.read", path: fieldsPath, command: "lists.print" },
  ],
  goTo: [{ key: "customfields.fields", menu: "customfields.fields", code: "KeyF" }],
};

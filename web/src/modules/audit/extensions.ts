import type { ModuleExtensions } from "../../kernel/extensions";
import { HistoryPanel, LastChange } from "./RecordHistory";

/** Every record form shows its record's latest change under its heading and its whole history on
 * demand (the toolbar's History, Alt+Y, or the command palette). The server answers only a reader
 * who may read the record. The audit log is reached by the navigation (G then Y). */
export const extensions: ModuleExtensions = {
  record: [{ key: "audit.history", labelKey: "audit.history.toggle", chord: "Alt+KeyY", summary: LastChange, panel: HistoryPanel }],
  goTo: [{ key: "audit.log", menu: "audit.log", code: "KeyY" }],
};

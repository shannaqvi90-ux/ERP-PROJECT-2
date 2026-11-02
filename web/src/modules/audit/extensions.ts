import type { ModuleExtensions } from "../../kernel/extensions";
import { HistoryPanel, LastChange } from "./RecordHistory";

/** Every record form shows its record's latest change under its heading and its whole history on
 * demand (the toolbar's History, Alt+Y, or the command palette), to a user whose roles let them read
 * the audit trail; the server answers only what they may read of the record. The audit log is
 * reached by the navigation (G then L, for log). */
export const extensions: ModuleExtensions = {
  record: [{ key: "audit.history", labelKey: "audit.history.toggle", chord: "Alt+KeyY", permission: "audit.log.read", summary: LastChange, panel: HistoryPanel }],
  goTo: [{ key: "audit.log", menu: "audit.log", code: "KeyL" }],
};

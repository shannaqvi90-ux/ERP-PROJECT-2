import type { ModuleExtensions } from "../../kernel/extensions";
import { FileNames, FilesButton, FilesPanel } from "./RecordFiles";

/** Every record form of a kind that takes files shows them: the count on the toolbar (a paperclip, Alt+A goes to the files), the
 * newest names under the heading, and the list of files below the form. */
export const extensions: ModuleExtensions = {
  // The record's history lists its files' uploads and deletes (the audit trail serves them by the record's id).
  history: [{ key: "attachments.history", parent: "contacts.contacts", child: "attachments.files", labelKey: "attachments.record.file", hide: ["record_id", "record_type"] }],
  record: [{ key: "attachments.files", order: 10, labelKey: "attachments.reveal", always: true, toolbar: FilesButton, summary: FileNames, panel: FilesPanel }],
};

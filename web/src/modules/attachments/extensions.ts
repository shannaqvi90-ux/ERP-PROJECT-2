import type { ModuleExtensions } from "../../kernel/extensions";
import { api } from "../../kernel/api";
import { navigate } from "../../kernel/router";
import { showAttachmentWhenLoaded } from "../../kernel/forms/attachments";
import { attachmentKinds, FileNames, FilesButton, FilesPanel, recordRoutes } from "./RecordFiles";

type Matches = { matches: { id: string; name: string; recordId: string; contentType: string }[] };

/** Every record form of a kind that takes files shows them: the count on the toolbar (a paperclip, Alt+A goes to the files), the
 * newest names under the heading, and the list of files below the form. */
export const extensions: ModuleExtensions = {
  // The command palette (Ctrl+K) finds a file by its name: the choice opens the record and shows the file.
  palette: Object.entries(attachmentKinds).map(([type, kind]) => ({
    key: `attachments.files.${type}`,
    labelKey: "attachments.palette",
    permission: kind.read,
    minLength: 2,
    search: async (query: string, { signal }: { signal: AbortSignal }) => {
      const page = await api<Matches>("GET", `/api/attachments/${type}/files?${new URLSearchParams({ search: query, take: "5" })}`, undefined, { signal });
      return page.matches.map((m) => ({
        id: m.id,
        title: m.name,
        ltr: true,
        run: () => {
          showAttachmentWhenLoaded(type, m.recordId, m.id);
          navigate(`${recordRoutes[type]}/${m.recordId}`);
        },
      }));
    },
  })),
  // The record's history lists its files' uploads and deletes (the audit trail serves them by the record's id).
  history: [{ key: "attachments.history", parent: "contacts.contacts", child: "attachments.files", labelKey: "attachments.record.file", hide: ["record_id", "record_type"] }],
  record: [{ key: "attachments.files", order: 10, labelKey: "attachments.reveal", always: true, toolbar: FilesButton, summary: FileNames, panel: FilesPanel }],
};

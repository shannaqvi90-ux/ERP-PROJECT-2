import type { RecordPanelProps } from "../../kernel/extensions";
import { AttachmentsSection, attachmentUrl, openAttachment, revealAttachments, useStoredAttachments } from "../../kernel/forms/attachments";
import { useI18n } from "../../kernel/i18n";
import { Icon } from "../../kernel/icons";
import { useSession } from "../../kernel/session";
import { chordForAria, chordKeys, useCommand } from "../../kernel/shortcuts";
import "./attachments.css";

/** The kinds of record that take files, with the permission that reads their files and the one that adds and deletes them: the
 * same as the server's registration (`ModuleBuilder.AttachmentRecord`), which checks both again. A module that registers a kind
 * on the server adds it here. */
export const attachmentKinds: Record<string, { read: string; write: string }> = {
  "contacts.contacts": { read: "contacts.contacts.read", write: "contacts.contacts.update" },
};

/** The key that takes a user to the record's files from anywhere on its screen. */
export const filesChord = "Alt+KeyA";

/** How many names the line under the heading holds; the rest are behind "more". */
const shownNames = 3;

function useKind(type: string) {
  const { can } = useSession();
  const kind = attachmentKinds[type];
  return kind && can(kind.read) ? { canWrite: can(kind.write) } : null;
}

/**
 * The record's toolbar button for its files: a paperclip with how many files the record holds, next to Print and History.
 * It takes the user to the files and puts the cursor on the first, so Enter opens it; the key (Alt+A) does the same from
 * anywhere on the screen and is listed in the shortcut help and the command palette.
 */
export function FilesButton({ type, id }: RecordPanelProps) {
  const { t } = useI18n();
  const kind = useKind(type);
  const files = useStoredAttachments(type, id);
  // A command of the open record (the palette lists it), with its key.
  useCommand({ id: "attachments.reveal", chord: filesChord, labelKey: "attachments.reveal", groupKey: "forms.shortcut.group", enabled: Boolean(kind), run: revealAttachments });
  if (!kind) return null;
  const label = t("attachments.button", { count: files?.total ?? 0 });
  return (
    <button
      type="button"
      className="button attachments-toggle"
      onClick={revealAttachments}
      title={`${label} (${chordKeys(filesChord).join("+")})`}
      aria-label={label}
      aria-keyshortcuts={chordForAria(filesChord)}
      data-record-panel="attachments.files"
    >
      <Icon name="attach" size={14} />
      <span className="attachments-count" aria-hidden="true">
        {files?.total ?? ""}
      </span>
    </button>
  );
}

/**
 * Under the record's heading: the names of its newest files, each one opening the file at a click (a picture, PDF or text in the
 * viewer; any other kind is saved), so a file is found again without scrolling. Older files are behind "more", which goes to the list.
 */
export function FileNames({ type, id }: RecordPanelProps) {
  const { t } = useI18n();
  const kind = useKind(type);
  const files = useStoredAttachments(type, id);
  if (!kind || !files || files.items.length === 0) return null;
  const names = files.items.slice(0, shownNames);
  return (
    <div className="attachments-names" data-testid="attachments-names">
      <Icon name="attach" size={12} className="attachments-names-icon" />
      {names.map((file) =>
        file.previewable ? (
          <button key={file.id} type="button" className="button link" onClick={() => openAttachment(type, id, file.id)} aria-label={t("attachments.previewFile", { name: file.name })}>
            <bdi>{file.name}</bdi>
          </button>
        ) : (
          <a key={file.id} href={attachmentUrl(type, file.id)} download={file.name} aria-label={t("attachments.downloadFile", { name: file.name })}>
            <bdi>{file.name}</bdi>
          </a>
        ),
      )}
      {files.total > names.length && (
        <button type="button" className="button link" onClick={revealAttachments}>
          {t("attachments.more", { count: files.total - names.length })}
        </button>
      )}
    </div>
  );
}

/** The files below the form (always drawn; the paperclip and the line of names lead to it). */
export function FilesPanel({ type, id }: RecordPanelProps) {
  const { can } = useSession();
  const kind = attachmentKinds[type];
  if (!kind) return null;
  return <AttachmentsSection recordType={type} recordId={id} canRead={can(kind.read)} canWrite={can(kind.write)} />;
}

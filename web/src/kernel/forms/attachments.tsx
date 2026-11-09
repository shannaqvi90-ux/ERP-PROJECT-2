import { useCallback, useEffect, useId, useRef, useState, type DragEvent } from "react";
import { api, ApiError, sessionEndedEvent } from "../api";
import { Dialog } from "../dialog";
import { requestLanguage, useI18n } from "../i18n";
import "./attachments.css";

/**
 * The files attached to a record (p11), as a section under its form: a list of the files with their
 * size, who added them and when, Add files (a button, and files dropped on the section; several at
 * once), Preview for images and PDF, Download and Delete. Reading follows the record's read
 * permission (`canRead`), adding and deleting its write permission (`canWrite`); the server checks
 * both again, with the size and the kind of every file. The record's module renders it with
 * its own kind of record (`schema.table`), the one it registered with `AttachmentRecord`.
 */
export type Attachment = {
  id: string;
  name: string;
  contentType: string;
  sizeBytes: number;
  sha256: string;
  previewable: boolean;
  createdAt: string;
  createdBy: string | null;
  createdByName: string | null;
  createdByNameAr: string | null;
};

type AttachmentList = { items: Attachment[]; total: number; maxFiles: number; maxFileBytes: number; extensions: string[] };

type Progress = { key: number; name: string; state: "uploading" | "failed"; message?: string };

const base = (recordType: string, recordId: string) => `/api/attachments/${recordType}/${recordId}/files`;

/** The address of a file's bytes (inline shows an image or PDF in the page instead of saving it). */
export const attachmentUrl = (recordType: string, recordId: string, fileId: string, inline = false) =>
  `${base(recordType, recordId)}/${fileId}/content${inline ? "?inline=true" : ""}`;

/** bytes / unit rounded to one decimal, as the decimal string "12.3" (whole-number arithmetic, no binary fractions shown). */
const tenths = (bytes: number, unit: number) => {
  const n = Math.round((bytes * 10) / unit);
  return `${Math.floor(n / 10)}.${n % 10}`;
};

const extensionOf = (name: string) => {
  const dot = name.lastIndexOf(".");
  return dot > 0 && dot < name.length - 1 ? name.slice(dot + 1).toLowerCase() : "";
};

export function AttachmentsSection({ recordType, recordId, canRead, canWrite }: { recordType: string; recordId: string; canRead: boolean; canWrite: boolean }) {
  const { t, language, format } = useI18n();
  const [list, setList] = useState<AttachmentList | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [progress, setProgress] = useState<Progress[]>([]);
  const [dragging, setDragging] = useState(false);
  const [previewing, setPreviewing] = useState<Attachment | null>(null);
  const [removing, setRemoving] = useState<Attachment | null>(null);
  const [removeFailed, setRemoveFailed] = useState(false);
  const [announce, setAnnounce] = useState("");
  const input = useRef<HTMLInputElement>(null);
  const counter = useRef(0);
  const depth = useRef(0);
  const headingId = useId();

  const load = useCallback(
    async (signal?: AbortSignal) => {
      try {
        const page = await api<AttachmentList>("GET", base(recordType, recordId), undefined, signal ? { signal } : {});
        setList(page);
        setLoadFailed(false);
      } catch (error) {
        if (!(error instanceof DOMException && error.name === "AbortError")) setLoadFailed(true);
      }
    },
    [recordType, recordId],
  );

  useEffect(() => {
    if (!canRead) return;
    const controller = new AbortController();
    setList(null);
    void load(controller.signal);
    return () => controller.abort();
  }, [canRead, load]);

  if (!canRead) return null;

  const maxBytes = list?.maxFileBytes ?? 20 * 1024 * 1024;
  const extensions = list?.extensions ?? [];
  const size = (bytes: number) =>
    bytes >= 1024 * 1024
      ? t("attachments.size.mb", { value: format.decimal(tenths(bytes, 1024 * 1024), 1) })
      : bytes >= 1024
        ? t("attachments.size.kb", { value: format.decimal(tenths(bytes, 1024), 1) })
        : t("attachments.size.bytes", { count: bytes });

  /** The reason a file cannot be attached, before anything is sent (the server checks again). */
  function refusal(file: File): string | null {
    if (file.size === 0) return t("attachments.empty");
    if (file.size > maxBytes) return t("attachments.tooLarge", { megabytes: Math.floor(maxBytes / (1024 * 1024)) });
    if (extensions.length > 0 && !extensions.includes(extensionOf(file.name))) return t("attachments.type");
    return null;
  }

  async function send(file: File) {
    const response = await fetch(base(recordType, recordId), {
      method: "POST",
      headers: { "X-Erp-Request": "1", "X-File-Name": encodeURIComponent(file.name), "Content-Type": "application/octet-stream", Accept: "application/json", "Accept-Language": requestLanguage() },
      credentials: "same-origin",
      body: file,
    });
    if (response.ok) return;
    if (response.status === 401) window.dispatchEvent(new Event(sessionEndedEvent));
    let message = `HTTP ${response.status}`;
    try {
      const body = (await response.json()) as { title?: string };
      if (body.title) message = body.title;
    } catch {
      // A body that is not a problem: the status says it.
    }
    throw new ApiError(response.status, { title: message });
  }

  async function add(files: File[]) {
    if (!canWrite || files.length === 0) return;
    setAnnounce("");
    const entries = files.map((file) => ({ file, key: ++counter.current }));
    const first: Progress[] = [];
    const sendable = entries.filter(({ file, key }) => {
      const reason = refusal(file);
      if (reason) first.push({ key, name: file.name, state: "failed", message: reason });
      return reason === null;
    });
    setProgress((current) => [...current, ...first, ...sendable.map(({ file, key }) => ({ key, name: file.name, state: "uploading" as const }))]);
    // A few at a time: several files go up together without one tying up the connection.
    let next = 0;
    let added = 0;
    const worker = async () => {
      while (next < sendable.length) {
        const { file, key } = sendable[next++]!;
        try {
          await send(file);
          added++;
          setProgress((current) => current.filter((p) => p.key !== key));
        } catch (error) {
          setProgress((current) => current.map((p) => (p.key === key ? { ...p, state: "failed", message: error instanceof Error ? error.message : String(error) } : p)));
        }
      }
    };
    await Promise.all(Array.from({ length: Math.min(3, sendable.length) }, worker));
    await load();
    if (added > 0) setAnnounce(t("attachments.added", { count: added }));
  }

  function onDrag(event: DragEvent, entering: boolean | null) {
    if (!canWrite || !event.dataTransfer?.types.includes("Files")) return;
    event.preventDefault();
    if (entering === true) depth.current++;
    if (entering === false) depth.current = Math.max(0, depth.current - 1);
    setDragging(entering === null ? true : depth.current > 0);
  }

  function onDrop(event: DragEvent) {
    if (!canWrite || !event.dataTransfer?.types.includes("Files")) return;
    event.preventDefault();
    depth.current = 0;
    setDragging(false);
    void add([...event.dataTransfer.files]);
  }

  async function remove(file: Attachment) {
    try {
      await api<void>("DELETE", `${base(recordType, recordId)}/${file.id}`);
      setRemoving(null);
      setRemoveFailed(false);
      setAnnounce(t("attachments.removed", { name: file.name }));
      await load();
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        setRemoving(null);
        await load();
      } else {
        setRemoveFailed(true);
      }
    }
  }

  const items = list?.items ?? [];
  const previewable = items.filter((i) => i.previewable);
  const step = (direction: 1 | -1) => {
    if (!previewing || previewable.length < 2) return;
    const index = previewable.findIndex((i) => i.id === previewing.id);
    setPreviewing(previewable[(index + direction + previewable.length) % previewable.length]!);
  };
  const who = (file: Attachment) => (language === "ar" ? file.createdByNameAr || file.createdByName : file.createdByName) ?? "";

  return (
    <section
      className={`record-section attachments${dragging ? " dragging" : ""}`}
      aria-labelledby={headingId}
      onDragEnter={(e) => onDrag(e, true)}
      onDragOver={(e) => onDrag(e, null)}
      onDragLeave={(e) => onDrag(e, false)}
      onDrop={onDrop}
      data-testid="attachments"
    >
      <div className="attachments-head">
        <h3 id={headingId}>{t("attachments.title", { count: list?.total ?? 0 })}</h3>
        {canWrite && (
          <>
            <button type="button" className="button" onClick={() => input.current?.click()}>
              {t("attachments.add")}
            </button>
            <input
              ref={input}
              type="file"
              multiple
              hidden
              tabIndex={-1}
              aria-label={t("attachments.add")}
              accept={extensions.map((e) => `.${e}`).join(",")}
              onChange={(event) => {
                const files = [...(event.target.files ?? [])];
                event.target.value = "";
                void add(files);
              }}
            />
          </>
        )}
      </div>
      {canWrite && <p className="muted attachments-hint">{t("attachments.hint", { megabytes: Math.floor(maxBytes / (1024 * 1024)) })}</p>}
      {dragging && (
        <div className="attachments-drop" role="presentation">
          {t("attachments.drop")}
        </div>
      )}
      <div className="visually-hidden" role="status" aria-live="polite">
        {announce}
      </div>
      {progress.length > 0 && (
        <ul className="attachments-progress" aria-label={t("attachments.progress")}>
          {progress.map((p) => (
            <li key={p.key} className={p.state === "failed" ? "failed" : undefined} role={p.state === "failed" ? "alert" : undefined}>
              <bdi>{p.name}</bdi>: {p.state === "uploading" ? t("attachments.uploading") : p.message}
              {p.state === "failed" && (
                <button type="button" className="button link" onClick={() => setProgress((current) => current.filter((x) => x.key !== p.key))}>
                  {t("attachments.dismiss")}
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {loadFailed && (
        <p className="alert" role="alert">
          {t("attachments.loadFailed")}{" "}
          <button type="button" className="button link" onClick={() => void load()}>
            {t("attachments.retry")}
          </button>
        </p>
      )}
      {list && items.length === 0 && !loadFailed && <p className="muted">{t("attachments.none")}</p>}
      {items.length > 0 && (
        <table className="grid compact">
          <thead>
            <tr>
              <th scope="col">{t("attachments.name")}</th>
              <th scope="col">{t("attachments.size")}</th>
              <th scope="col">{t("attachments.addedBy")}</th>
              <th scope="col">{t("attachments.addedOn")}</th>
              <th scope="col">
                <span className="visually-hidden">{t("attachments.actions")}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {items.map((file) => (
              <tr key={file.id}>
                <td>
                  {file.previewable ? (
                    <button type="button" className="button link" onClick={() => setPreviewing(file)} aria-label={t("attachments.previewFile", { name: file.name })}>
                      <bdi>{file.name}</bdi>
                    </button>
                  ) : (
                    <a href={attachmentUrl(recordType, recordId, file.id)} download={file.name}>
                      <bdi>{file.name}</bdi>
                    </a>
                  )}
                </td>
                <td>{size(file.sizeBytes)}</td>
                <td>{who(file)}</td>
                <td>{format.dateTime(file.createdAt)}</td>
                <td className="attachments-actions">
                  {file.previewable && (
                    <button type="button" className="button" onClick={() => setPreviewing(file)} aria-label={t("attachments.previewFile", { name: file.name })}>
                      {t("attachments.preview")}
                    </button>
                  )}
                  <a className="button" href={attachmentUrl(recordType, recordId, file.id)} download={file.name} aria-label={t("attachments.downloadFile", { name: file.name })}>
                    {t("attachments.download")}
                  </a>
                  {canWrite && (
                    <button
                      type="button"
                      className="button"
                      onClick={() => {
                        setRemoveFailed(false);
                        setRemoving(file);
                      }}
                      aria-label={t("attachments.deleteFile", { name: file.name })}
                    >
                      {t("attachments.delete")}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {previewing && (
        <Dialog title={previewing.name} onClose={() => setPreviewing(null)} className="attachments-preview">
          <div
            className="attachments-view"
            onKeyDown={(event) => {
              // Next and previous file in the reading direction.
              if (event.key === "ArrowRight" || event.key === "ArrowLeft") {
                event.preventDefault();
                step((event.key === "ArrowRight") === (language !== "ar") ? 1 : -1);
              }
            }}
          >
            {previewing.contentType.startsWith("image/") ? (
              <img src={attachmentUrl(recordType, recordId, previewing.id, true)} alt={previewing.name} />
            ) : (
              <iframe src={attachmentUrl(recordType, recordId, previewing.id, true)} title={previewing.name} />
            )}
          </div>
          <div className="dialog-actions">
            {previewable.length > 1 && (
              <>
                <button type="button" className="button" onClick={() => step(-1)}>
                  {t("attachments.previous")}
                </button>
                <button type="button" className="button" onClick={() => step(1)}>
                  {t("attachments.next")}
                </button>
              </>
            )}
            <a className="button" href={attachmentUrl(recordType, recordId, previewing.id)} download={previewing.name}>
              {t("attachments.download")}
            </a>
            <button type="button" className="button primary" onClick={() => setPreviewing(null)}>
              {t("attachments.close")}
            </button>
          </div>
        </Dialog>
      )}
      {removing && (
        <Dialog title={t("attachments.deleteTitle")} onClose={() => setRemoving(null)}>
          <p>{t("attachments.deleteAsk", { name: removing.name })}</p>
          {removeFailed && (
            <p className="alert" role="alert">
              {t("attachments.deleteFailed")}
            </p>
          )}
          <div className="dialog-actions">
            <button type="button" className="button" onClick={() => setRemoving(null)}>
              {t("attachments.cancel")}
            </button>
            <button type="button" className="button primary" onClick={() => void remove(removing)}>
              {t("attachments.delete")}
            </button>
          </div>
        </Dialog>
      )}
    </section>
  );
}

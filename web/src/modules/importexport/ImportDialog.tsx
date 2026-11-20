import { useCallback, useEffect, useRef, useState, type ChangeEvent, type DragEvent } from "react";
import { api, ApiError, sessionEndedEvent } from "../../kernel/api";
import { Dialog } from "../../kernel/dialog";
import { requestLanguage, useI18n } from "../../kernel/i18n";
import "./importexport.css";
import { importBase, type ImportFields, type ImportSession } from "./types";

/** How often a file that runs in the background is looked at again. */
const pollMilliseconds = 1200;

/** Uploads the file's bytes (the name goes in a header) and answers the session, or the problem the server gave. */
async function upload(table: string, file: File): Promise<ImportSession> {
  const response = await fetch(`${importBase(table)}/imports`, {
    method: "POST",
    headers: { "X-Erp-Request": "1", "X-File-Name": encodeURIComponent(file.name), "Content-Type": "application/octet-stream", Accept: "application/json", "Accept-Language": requestLanguage() },
    credentials: "same-origin",
    body: file,
  });
  const text = await response.text();
  if (response.ok) return JSON.parse(text) as ImportSession;
  if (response.status === 401) window.dispatchEvent(new Event(sessionEndedEvent));
  let body: Record<string, unknown> = {};
  try {
    body = JSON.parse(text) as Record<string, unknown>;
  } catch {
    // Not a problem document: the status says it.
  }
  const errors = body.errors as Record<string, { message: string }[]> | undefined;
  throw new ApiError(response.status, { ...body, title: errors?.file?.[0]?.message ?? (body.title as string | undefined) ?? `HTTP ${response.status}` });
}

/**
 * Imports a CSV or XLSX file into a kind of record (p14): choose the file (it is read at once and its columns are matched to the
 * record's fields by name and by what earlier imports chose), look at the preview and change any column's field, check the file
 * (nothing is written) or import it, read the report and take the error file. Small files are done when the answer comes; large ones run
 * in the background and the dialog looks at them until they end. Keyboard first: the file button has the focus on opening, Enter
 * imports, Escape closes.
 */
export function ImportDialog({ table, titleKey, onClose, onDone }: { table: string; titleKey: string; onClose: () => void; onDone: () => void }) {
  const { t, format } = useI18n();
  const [fields, setFields] = useState<ImportFields | null>(null);
  const [session, setSession] = useState<ImportSession | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [skip, setSkip] = useState(false);
  const [dragging, setDragging] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  const runButton = useRef<HTMLButtonElement>(null);
  const closeButton = useRef<HTMLButtonElement>(null);
  const done = useRef(onDone);
  done.current = onDone;
  const imported = useRef(false);

  const generalProblem = useRef(t("importexport.problem"));
  generalProblem.current = t("importexport.problem");
  useEffect(() => {
    let cancelled = false;
    api<ImportFields>("GET", `${importBase(table)}/fields`)
      .then((f) => !cancelled && setFields(f))
      .catch((e: unknown) => !cancelled && setProblem(e instanceof ApiError ? e.message : generalProblem.current));
    return () => {
      cancelled = true;
    };
  }, [table]);

  // The records the import wrote are on the list behind the dialog: it reads them again once, when the dialog closes.
  const finish = useCallback(() => {
    if (imported.current) done.current();
    onClose();
  }, [onClose]);

  const sessionId = session?.id;
  const running = session?.status === "running";
  useEffect(() => {
    if (!sessionId || !running) return;
    const timer = window.setInterval(() => {
      api<ImportSession>("GET", `${importBase(table)}/imports/${sessionId}`)
        .then((s) => {
          setSession(s);
          if (s.status === "completed") imported.current = true;
        })
        .catch(() => undefined);
    }, pollMilliseconds);
    return () => window.clearInterval(timer);
  }, [sessionId, running, table]);

  async function choose(file: File | undefined) {
    if (!file || busy) return;
    setProblem(null);
    setBusy(true);
    try {
      setSession(await upload(table, file));
    } catch (e) {
      setProblem(e instanceof ApiError ? e.message : t("importexport.problem"));
    } finally {
      setBusy(false);
    }
  }

  async function setField(index: number, field: string) {
    if (!session) return;
    setProblem(null);
    try {
      setSession(await api<ImportSession>("PUT", `${importBase(table)}/imports/${session.id}/mapping`, { columns: [{ index, field: field || null }] }));
    } catch (e) {
      setProblem(e instanceof ApiError ? e.message : t("importexport.problem"));
    }
  }

  async function run(dryRun: boolean) {
    if (!session || busy) return;
    setProblem(null);
    setBusy(true);
    try {
      const next = await api<ImportSession>("POST", `${importBase(table)}/imports/${session.id}/run`, { dryRun, onError: skip ? "skip" : "reject" });
      setSession(next);
      if (next.status === "completed") imported.current = true;
    } catch (e) {
      setProblem(e instanceof ApiError ? e.message : t("importexport.problem"));
    } finally {
      setBusy(false);
    }
  }

  function again() {
    setSession(null);
    setProblem(null);
    window.setTimeout(() => input.current?.focus(), 0);
  }

  const mapped = session?.status === "uploaded" || session?.status === "checked" || session?.status === "failed";
  const finished = session?.status === "completed";
  const ready = mapped && session.missingFields.length === 0;

  // The keyboard follows the dialog's steps: the file chosen, focus goes to Import; the run ended, to Close (a control that
  // disappears or is disabled must not leave the focus on the page behind, where Escape would not reach the dialog).
  const step = session ? `${session.id}:${session.status}` : "";
  useEffect(() => {
    if (!step) return;
    const timer = window.setTimeout(() => {
      if (step.endsWith(":completed") || step.endsWith(":failed") || step.endsWith(":checked")) (runButton.current && !runButton.current.disabled && !step.endsWith(":completed") ? runButton.current : closeButton.current)?.focus();
      else if (step.endsWith(":uploaded")) (runButton.current ?? closeButton.current)?.focus();
    }, 0);
    return () => window.clearTimeout(timer);
  }, [step]);

  return (
    <Dialog title={t("importexport.import.title", { kind: t(titleKey) })} onClose={finish} className="ie-dialog" initialFocus={input}>
      <div className="ie-body">
        {!session && (
          <div
            className={dragging ? "ie-drop ie-dragging" : "ie-drop"}
            onDragOver={(e: DragEvent) => {
              e.preventDefault();
              setDragging(true);
            }}
            onDragLeave={() => setDragging(false)}
            onDrop={(e: DragEvent) => {
              e.preventDefault();
              setDragging(false);
              void choose(e.dataTransfer.files[0]);
            }}
          >
            <p>{t("importexport.import.intro")}</p>
            <input
              ref={input}
              type="file"
              className="ie-file"
              accept=".csv,.xlsx,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              aria-label={t("importexport.import.choose")}
              data-testid="import-file"
              disabled={busy}
              onChange={(e: ChangeEvent<HTMLInputElement>) => void choose(e.target.files?.[0])}
            />
            {fields && (
              <p className="ie-hint">
                {t("importexport.import.limits", { rows: format.number(fields.maxRows), megabytes: format.number(Math.floor(fields.maxFileBytes / (1024 * 1024))) })}{" "}
                <a href={`${importBase(table)}/template`} download>
                  {t("importexport.import.template")}
                </a>
              </p>
            )}
          </div>
        )}

        {problem && (
          <p className="notice error" role="alert" data-testid="import-problem">
            {problem}
          </p>
        )}

        {session && (
          <>
            <p className="ie-file-line">
              <strong dir="auto">{session.fileName}</strong> · {t("importexport.import.rows", { count: session.rowCount })}
              {mapped && (
                <>
                  {" "}
                  <button type="button" className="button ghost" onClick={again}>
                    {t("importexport.import.another")}
                  </button>
                </>
              )}
            </p>

            {mapped && (
              <>
                <table className="ie-map" data-testid="import-mapping">
                  <caption className="ie-caption">{t("importexport.map.caption")}</caption>
                  <thead>
                    <tr>
                      <th scope="col">{t("importexport.map.column")}</th>
                      <th scope="col">{t("importexport.map.preview")}</th>
                      <th scope="col">{t("importexport.map.field")}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {session.columns.map((c) => (
                      <tr key={c.index}>
                        <th scope="row" dir="auto">
                          {c.header || t("importexport.map.noHeader", { number: c.index + 1 })}
                        </th>
                        <td className="ie-sample" dir="auto">
                          {c.sample.filter(Boolean).slice(0, 3).join(" · ")}
                        </td>
                        <td>
                          <select
                            value={c.field ?? ""}
                            aria-label={t("importexport.map.fieldFor", { column: c.header || String(c.index + 1) })}
                            disabled={busy || running}
                            onChange={(e) => void setField(c.index, e.target.value)}
                          >
                            <option value="">{t("importexport.map.leaveOut")}</option>
                            {fields?.items.map((f) => (
                              <option key={f.key} value={f.key}>
                                {f.label}
                                {f.required ? " *" : ""}
                              </option>
                            ))}
                          </select>
                          {c.source === "memory" && <span className="ie-source">{t("importexport.map.memory")}</span>}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {session.missingFields.length > 0 && (
                  <p className="notice" role="status" data-testid="import-missing">
                    {t("importexport.map.missing", { fields: session.missingFields.join(", ") })}
                  </p>
                )}
                <label className="ie-skip">
                  <input type="checkbox" checked={skip} onChange={(e) => setSkip(e.target.checked)} /> {t("importexport.import.skip")}
                </label>
              </>
            )}

            {running && (
              <p className="notice" role="status" data-testid="import-running">
                {t("importexport.import.running")}
              </p>
            )}

            {session.status === "checked" && (
              <p className="notice" role="status" data-testid="import-checked">
                {t("importexport.import.checked", { valid: format.number(session.rowsValid), failed: format.number(session.rowsFailed) })}
              </p>
            )}
            {finished && (
              <p className="notice success" role="status" data-testid="import-done">
                {t("importexport.import.done", { count: session.rowsImported })}
                {session.rowsFailed > 0 && ` ${t("importexport.import.someFailed", { count: session.rowsFailed })}`}
              </p>
            )}
            {session.status === "failed" && session.failure && (
              <p className="notice error" role="alert" data-testid="import-failed">
                {session.failure}
              </p>
            )}

            {session.issues.length > 0 && (
              <table className="ie-issues" data-testid="import-issues">
                <caption className="ie-caption">{t("importexport.issues.caption", { count: session.rowsFailed })}</caption>
                <thead>
                  <tr>
                    <th scope="col">{t("importexport.issues.row")}</th>
                    <th scope="col">{t("importexport.issues.field")}</th>
                    <th scope="col">{t("importexport.issues.problem")}</th>
                  </tr>
                </thead>
                <tbody>
                  {session.issues.map((i, n) => (
                    <tr key={n}>
                      <td>{format.number(i.row)}</td>
                      <td dir="auto">{i.fieldLabel}</td>
                      <td dir="auto">{i.message}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            {session.hasErrorFile && (
              <p>
                <a className="button" href={`${importBase(table)}/imports/${session.id}/errors`} download data-testid="import-errors">
                  {t("importexport.import.errorFile")}
                </a>
              </p>
            )}
          </>
        )}
      </div>

      <div className="dialog-actions">
        {mapped && (
          <>
            <button type="button" className="button" disabled={busy || !ready} onClick={() => void run(true)}>
              {t("importexport.import.check")}
            </button>
            <button ref={runButton} type="button" className="button primary" disabled={busy || !ready} onClick={() => void run(false)} data-testid="import-run">
              {t("importexport.import.run", { count: session?.rowCount ?? 0 })}
            </button>
          </>
        )}
        <button ref={closeButton} type="button" className="button" onClick={finish}>
          {finished ? t("importexport.close") : t("importexport.cancel")}
        </button>
      </div>
    </Dialog>
  );
}

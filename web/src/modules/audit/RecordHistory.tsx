import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from "react";
import type { RecordPanelProps } from "../../kernel/extensions";
import { useI18n } from "../../kernel/i18n";
import { Link } from "../../kernel/router";
import { useSession } from "../../kernel/session";
import { actorText, fieldDecimals, fieldLabel, logAddress, readHistory, shownChanges, valueText, type AuditEvent, type AuditHistory, type ValueFormat } from "./model";
import "./audit.css";

/** The words for who made a change: "Created by …", "Changed by …", "Deleted by …". */
export function actionKey(action: AuditEvent["action"]): string {
  return action === "insert" ? "audit.history.created" : action === "delete" ? "audit.history.deleted" : "audit.history.changed";
}

function useValueFormat(names: AuditHistory["names"] | undefined, fields?: AuditHistory["fields"]): ValueFormat {
  const { t, formatDateTime, formatNumber, language, format } = useI18n();
  return useMemo(() => ({ t, formatDateTime, formatNumber, formatDecimal: format.decimal, language, names, fields }), [t, formatDateTime, formatNumber, format, language, names, fields]);
}

/** One change's field as a line: "E-mail: from old to new" (a creation's "E-mail: value"). */
export function changeLine(event: AuditEvent, field: { field: string; old?: unknown; new?: unknown }, f: ValueFormat): string {
  const name = fieldLabel(field.field, f);
  const decimals = fieldDecimals(field.field, f);
  if (event.action === "insert") return f.t("audit.value.set", { field: name, new: valueText(field.new, f, decimals) });
  if (event.action === "delete") return f.t("audit.value.set", { field: name, new: valueText(field.old, f, decimals) });
  if (field.new === null || field.new === undefined) return f.t("audit.value.cleared", { field: name, old: valueText(field.old, f, decimals) });
  return f.t("audit.value.from", { field: name, old: valueText(field.old, f, decimals), new: valueText(field.new, f, decimals) });
}

/**
 * Under the record's heading: its latest change, who made it and when, with the fields it changed
 * and their old and new values (as many as fit on the line; the history lists them all). Reads the
 * latest change again whenever the form saves. A click (or the toolbar's History, Alt+Y) opens
 * the whole history.
 */
export function LastChange({ type, id, revision, open, setOpen }: RecordPanelProps) {
  const { t, language, formatDateTime } = useI18n();
  const [latest, setLatest] = useState<{ event: AuditEvent; names: AuditHistory["names"]; fields?: AuditHistory["fields"] } | null>(null);
  useEffect(() => {
    const abort = new AbortController();
    readHistory(type, id, { take: 1, signal: abort.signal }).then(
      (page) => setLatest(page.items[0] ? { event: page.items[0], names: page.names, fields: page.fields } : null),
      // The latest change is a convenience of the heading: a record without one (or a reader
      // the history is not for) shows nothing there; the History panel says why.
      () => !abort.signal.aborted && setLatest(null),
    );
    return () => abort.abort();
  }, [type, id, revision]);
  const f = useValueFormat(latest?.names, latest?.fields);
  if (!latest) return null;
  const { event } = latest;
  const lines = shownChanges(event).map((c) => changeLine(event, c, f));
  return (
    <div className="audit-latest" data-testid="audit-latest">
      <button type="button" className="button link audit-latest-open" onClick={() => setOpen(!open)} aria-expanded={open}>
        {t(actionKey(event.action), { name: actorText(event, language) })}
      </button>
      <span className="audit-latest-time">
        {" · "}
        <time dateTime={event.occurredAt}>{formatDateTime(event.occurredAt)}</time>
      </span>
      {event.action === "update" && lines.length > 0 && (
        <span className="audit-latest-fields">
          {" · "}
          {lines.join(t("audit.value.separator"))}
        </span>
      )}
    </div>
  );
}

/**
 * The record's whole history below the form: every change newest first, each with who made it and
 * when and every field's old and new value; a box to find the changes of one field; older changes
 * a page at a time; the same record in the audit log. Escape closes it (the toolbar's History
 * button takes the focus back).
 */
export function HistoryPanel({ type, id, revision, setOpen }: RecordPanelProps) {
  const { t, language, formatDateTime } = useI18n();
  const { can } = useSession();
  const headingId = useId();
  const [pages, setPages] = useState<AuditHistory[]>([]);
  const [status, setStatus] = useState<"loading" | "ready" | "failed">("loading");
  const [find, setFind] = useState("");
  const [attempt, setAttempt] = useState(0);
  const panelRef = useRef<HTMLElement>(null);

  useEffect(() => {
    const abort = new AbortController();
    setStatus("loading");
    readHistory(type, id, { signal: abort.signal }).then(
      (page) => {
        setPages([page]);
        setStatus("ready");
      },
      () => !abort.signal.aborted && setStatus("failed"),
    );
    return () => abort.abort();
  }, [type, id, revision, attempt]);

  useEffect(() => {
    panelRef.current?.querySelector<HTMLElement>("h3")?.focus();
  }, []);

  const names = useMemo(() => Object.assign({}, ...pages.map((p) => p.names)), [pages]);
  const fieldLabels = useMemo(() => Object.assign({}, ...pages.map((p) => p.fields ?? {})), [pages]);
  const f = useValueFormat(names, fieldLabels);
  const events = pages.flatMap((p) => p.items);
  const next = pages.at(-1)?.next ?? null;
  const wanted = find.trim().toLocaleLowerCase();
  const matches = (field: string) => !wanted || fieldLabel(field, f).toLocaleLowerCase().includes(wanted) || field.includes(wanted);
  const shown = events
    .map((event) => ({ event, fields: shownChanges(event).filter((c) => matches(c.field)) }))
    .filter((e) => !wanted || e.fields.length > 0);

  const more = async () => {
    if (!next) return;
    try {
      const page = await readHistory(type, id, { before: next });
      setPages((current) => [...current, page]);
    } catch {
      setStatus("failed");
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    if (event.key === "Escape" && !event.defaultPrevented) {
      event.preventDefault();
      setOpen(false);
      window.requestAnimationFrame(() => document.querySelector<HTMLElement>('[data-record-panel="audit.history"]')?.focus());
    }
  };

  return (
    <section className="audit-history" aria-labelledby={headingId} ref={panelRef} onKeyDown={onKeyDown} data-testid="audit-history">
      <div className="audit-history-head">
        <h3 id={headingId} tabIndex={-1}>
          {t("audit.history.title")}
        </h3>
        {status === "ready" && <span className="muted">{t("audit.history.count", { count: events.length })}</span>}
        <input
          type="search"
          className="audit-history-find"
          value={find}
          onChange={(e) => setFind(e.target.value)}
          placeholder={t("audit.history.filter")}
          aria-label={t("audit.history.filter")}
        />
        {can("audit.log.read") && <Link to={logAddress(id)}>{t("audit.history.inLog")}</Link>}
      </div>
      <p className="hint">{t("audit.history.hint")}</p>
      {status === "loading" && <p className="muted" aria-busy="true">{t("audit.history.loading")}</p>}
      {status === "failed" && (
        <div className="alert" role="alert">
          {t("audit.history.failed")}{" "}
          <button type="button" className="button link" onClick={() => setAttempt((a) => a + 1)}>
            {t("audit.history.retry")}
          </button>
        </div>
      )}
      {status === "ready" && events.length === 0 && <p className="muted">{t("audit.history.empty")}</p>}
      {status === "ready" && events.length > 0 && shown.length === 0 && <p className="muted">{t("audit.history.noMatch")}</p>}
      <ol className="audit-events">
        {shown.map(({ event, fields }) => (
          <li key={event.id} className={`audit-event action-${event.action}`}>
            <div className="audit-event-head">
              <strong>{t(actionKey(event.action), { name: actorText(event, language) })}</strong>
              <time dateTime={event.occurredAt}>{formatDateTime(event.occurredAt)}</time>
            </div>
            {fields.length > 0 && (
              <table className="audit-fields">
                <thead>
                  <tr>
                    <th scope="col">{t("audit.history.field")}</th>
                    {event.action !== "insert" && <th scope="col">{t("audit.history.old")}</th>}
                    {event.action !== "delete" && <th scope="col">{t("audit.history.new")}</th>}
                  </tr>
                </thead>
                <tbody>
                  {fields.map((c) => (
                    <tr key={c.field}>
                      <th scope="row">{fieldLabel(c.field, f)}</th>
                      {event.action !== "insert" && <td className="audit-old"><bdi>{valueText(c.old, f, fieldDecimals(c.field, f))}</bdi></td>}
                      {event.action !== "delete" && <td className="audit-new"><bdi>{valueText(c.new, f, fieldDecimals(c.field, f))}</bdi></td>}
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </li>
        ))}
      </ol>
      {status === "ready" && next && (
        <button type="button" className="button" onClick={() => void more()}>
          {t("audit.history.more")}
        </button>
      )}
    </section>
  );
}

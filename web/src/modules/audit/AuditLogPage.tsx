import { useEffect, useMemo, useState } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { ListView, type ReferenceSource } from "../../kernel/lists/ListView";
import type { Row } from "../../kernel/lists/model";
import { Link } from "../../kernel/router";
import { useSession } from "../../kernel/session";
import { actionKey, changeLine } from "./RecordHistory";
import { actorText, fieldName, logAddress, type AuditEvent, type FieldChange, type RecordName, type ValueFormat } from "./model";
import "./audit.css";

/** A row of the audit log (GET /api/audit/log). */
type LogRow = Row & {
  occurredAt: string;
  actorId: string | null;
  actorName: string;
  actorNameAr: string | null;
  actorKind: string;
  action: AuditEvent["action"];
  recordType: string;
  record: string | null;
  recordAr: string | null;
  recordCode: string | null;
  recordId: string | null;
  recordRoute: string | null;
  fields: string[];
  changes: string;
};

/** One change with every field's stored values (GET /api/audit/log/{id}). */
type Change = { change: LogRow; details: FieldChange[]; names: Record<string, RecordName> };

/**
 * The audit log: every change of the records the user may read, newest first, on the list
 * framework (quick search by the record's name, filters by person, kind of record, record, field,
 * change and time from the column headers, views, grouping, print and export). A row opens the
 * change in the side panel: every field's old and new value, and the record itself.
 */
export function AuditLogPage() {
  const { t, language } = useI18n();
  const { can } = useSession();
  const [people, setPeople] = useState<Record<string, string>>({});
  const remember = (rows: LogRow[]) =>
    setPeople((current) => {
      const next = { ...current };
      for (const row of rows) if (row.actorId) next[row.actorId] = actorText(row, language);
      return next;
    });
  const references = useMemo<Record<string, ReferenceSource>>(
    () => ({ actorId: { label: (id) => people[id] }, recordId: { label: () => undefined } }),
    [people],
  );
  return (
    <div className="audit-log">
      <ListView
        listKey="audit.log"
        titleKey="audit.log.title"
        countKey="audit.history.count"
        searchPlaceholderKey="audit.log.record"
        can={can}
        openOnClick
        references={references}
        renderRecord={(id, close) => <ChangePanel key={id} id={id} onClose={close} onRow={(row) => remember([row])} />}
        renderCell={{
          actorId: (row) => {
            const r = row as LogRow;
            return r.actorId ? <bdi>{actorText(r, language)}</bdi> : <span className="muted">{actorText(r, language)}</span>;
          },
          record: (row) => {
            const r = row as LogRow;
            const name = language === "ar" && r.recordAr ? r.recordAr : r.record;
            return name ? <bdi>{name}</bdi> : <span className="muted">{t("audit.value.empty")}</span>;
          },
          changes: (row) => {
            const r = row as LogRow;
            return <bdi className="audit-change-text">{r.changes}</bdi>;
          },
        }}
      />
    </div>
  );
}

/** The change a row of the log opens: who, when, the record, and every field's old and new value. */
function ChangePanel({ id, onClose, onRow }: { id: string; onClose: () => void; onRow: (row: LogRow) => void }) {
  const { t, language, formatDateTime, formatNumber } = useI18n();
  const [found, setFound] = useState<Change | null | undefined>(undefined);
  const [find, setFind] = useState("");
  useEffect(() => {
    const abort = new AbortController();
    api<Change>("GET", `/api/audit/log/${encodeURIComponent(id)}`, undefined, { signal: abort.signal }).then(
      (change) => {
        setFound(change);
        onRow(change.change);
      },
      () => !abort.signal.aborted && setFound(null),
    );
    return () => abort.abort();
    // onRow only remembers the person's name; the change is read again only for another id.
  }, [id]);
  const f = useMemo<ValueFormat>(() => ({ t, formatDateTime, formatNumber, language, names: found?.names }), [t, formatDateTime, formatNumber, language, found]);
  if (found === undefined) return <p className="muted">{t("audit.history.loading")}</p>;
  if (found === null) return <p className="muted">{t("audit.log.noSelection")}</p>;
  const row = found.change;
  const event: AuditEvent = { ...row, id: String(row.id), changes: found.details };
  // A creation or a deletion lists every field: the box finds one of them by its name.
  const wanted = find.trim().toLocaleLowerCase();
  const fields = found.details.filter((c) => c.field !== "id" && (!wanted || fieldName(c.field, t).toLocaleLowerCase().includes(wanted) || c.field.includes(wanted)));
  const name = language === "ar" && row.recordAr ? row.recordAr : row.record;
  return (
    <section className="audit-change" aria-label={t("audit.log.detail")}>
      <h3>
        {t(`audit.action.${row.action}`)} · <bdi>{name ?? t("audit.value.empty")}</bdi>
      </h3>
      <p>
        {t(actionKey(row.action), { name: actorText(row, language) })}
        {" · "}
        <time dateTime={row.occurredAt}>{formatDateTime(row.occurredAt)}</time>
      </p>
      <input
        type="search"
        className="audit-history-find"
        value={find}
        onChange={(e) => setFind(e.target.value)}
        placeholder={t("audit.history.filter")}
        aria-label={t("audit.history.filter")}
      />
      {fields.length === 0 && <p className="muted">{t("audit.history.noMatch")}</p>}
      <ul className="audit-change-fields">
        {fields.map((c) => (
          <li key={c.field}>
            <bdi>{changeLine(event, c, f)}</bdi>
          </li>
        ))}
      </ul>
      <div className="record-actions">
        {row.recordRoute && <Link to={row.recordRoute}>{t("audit.log.openRecord")}</Link>}
        {row.recordId && <Link to={logAddress(row.recordId)}>{t("audit.log.recordHistory")}</Link>}
        <button type="button" className="button" onClick={onClose}>
          {t("audit.log.close")}
        </button>
      </div>
    </section>
  );
}

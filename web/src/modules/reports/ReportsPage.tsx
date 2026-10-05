import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { api, ApiError } from "../../kernel/api";
import { BooleanField, DateField, LookupField, SelectField, TextField, type LookupRow } from "../../kernel/forms/fields";
import type { FieldBinding } from "../../kernel/forms/useRecordForm";
import { useI18n } from "../../kernel/i18n";
import { PrintDocument } from "../../kernel/print";
import { useShortcut } from "../../kernel/shortcuts";
import type { ReportDocument, ReportCatalog, ReportSummary } from "./model";
import { reportUrl } from "./model";
import "./reports.css";

type Values = Record<string, string>;

/** The report's parameters, grouping and document language from the address (?report=…&company=…). */
function fromAddress(): { key: string | null; values: Values; run: boolean } {
  const query = new URLSearchParams(window.location.search);
  const values: Values = {};
  query.forEach((value, name) => {
    if (name !== "report" && name !== "run") values[name] = value;
  });
  return { key: query.get("report"), values, run: query.get("run") === "1" };
}

/**
 * Reports: every report the user may run, with its parameters, grouping and the document's
 * language; "Show" (Enter or Ctrl+Enter) draws the document on screen exactly as it prints, then it
 * prints from the browser, downloads as PDF (English or Arabic, laid out right to left) or exports
 * to Excel or CSV. The address keeps the report and its parameters, so a link opens the same
 * report, and a record's Print menu opens its document here.
 */
export function ReportsPage() {
  const { t, language, numerals } = useI18n();
  const [catalog, setCatalog] = useState<ReportCatalog | null>(null);
  const [error, setError] = useState<string | null>(null);
  const initial = useMemo(fromAddress, []);
  const [selected, setSelected] = useState<string | null>(initial.key);
  const [values, setValues] = useState<Values>(initial.values);
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [document, setDocument] = useState<ReportDocument | null>(null);
  const [busy, setBusy] = useState(false);
  const autoRun = useRef(initial.run);

  useEffect(() => {
    api<ReportCatalog>("GET", `/api/reports/catalog?language=${language}`).then(setCatalog, (e: Error) => setError(e.message));
  }, [language]);

  const report = catalog?.items.find((r) => r.key === selected) ?? null;
  const documentLanguage = values.language ?? language;

  // The address follows the report and its parameters.
  useEffect(() => {
    if (!selected) return;
    const query = new URLSearchParams({ report: selected, ...Object.fromEntries(Object.entries(values).filter(([, v]) => v !== "")) });
    window.history.replaceState(null, "", `${window.location.pathname}?${query}`);
  }, [selected, values]);

  const choose = (key: string) => {
    setSelected(key);
    setValues({});
    setErrors({});
    setDocument(null);
  };

  const run = useCallback(async () => {
    if (!report) return;
    setBusy(true);
    setErrors({});
    setError(null);
    try {
      setDocument(await api<ReportDocument>("GET", reportUrl(report, values, "json", documentLanguage, numerals, report.defaultGroupBy)));
    } catch (e) {
      setDocument(null);
      if (e instanceof ApiError) setErrors(Object.fromEntries(Object.entries(e.fieldErrors).map(([k, v]) => [k, v.map((x) => x.message)])));
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }, [report, values, documentLanguage, numerals]);

  // A link from a record's Print menu shows the document at once.
  useEffect(() => {
    if (report && autoRun.current) {
      autoRun.current = false;
      void run();
    }
  }, [report, run]);

  useShortcut({ id: "reports.run", chord: "Mod+Enter", labelKey: "reports.action.show", groupKey: "reports.shortcut.group", enabled: Boolean(report), run: () => void run() });

  const bind = (key: string): FieldBinding<string> => ({
    name: key,
    value: values[key] ?? "",
    onChange: (value) => {
      setValues((v) => ({ ...v, [key]: value }));
      setErrors((e) => ({ ...e, [key]: [] }));
    },
    errors: errors[key] ?? [],
    readOnly: false,
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    void run();
  };

  return (
    <section className="reports-screen">
      <div className="screen-header no-print">
        <h1>{t("reports.title")}</h1>
      </div>
      {error && !report && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}
      <div className="reports-layout">
        <nav className="reports-list no-print" aria-label={t("reports.list")}>
          {catalog && catalog.items.length === 0 && <p className="muted">{t("reports.none")}</p>}
          <ul>
            {catalog?.items.map((r) => (
              <li key={r.key}>
                <button type="button" className={r.key === selected ? "report-choice active" : "report-choice"} aria-current={r.key === selected} onClick={() => choose(r.key)}>
                  <span className="report-title">{r.title}</span>
                  {r.description && <span className="muted report-description">{r.description}</span>}
                </button>
              </li>
            ))}
          </ul>
          {catalog && catalog.lists.length > 0 && (
            <>
              <h2 className="reports-subhead">{t("reports.lists")}</h2>
              <p className="muted">{t("reports.listsHint")}</p>
              <table className="reports-lists">
                <thead>
                  <tr>
                    <th scope="col">{t("reports.lists.column.list")}</th>
                    <th scope="col">{t("reports.lists.column.download")}</th>
                  </tr>
                </thead>
                <tbody>
                  {catalog.lists.map((l) => (
                    <tr key={l.key}>
                      <th scope="row">{l.title}</th>
                      <td>
                        <a href={`${l.path}?format=pdf&language=${language}&numerals=${numerals}`} download aria-label={t("reports.lists.download", { list: l.title, format: t("reports.action.pdf") })}>
                          {t("reports.action.pdf")}
                        </a>{" "}
                        <a href={`${l.path}?format=xlsx&language=${language}`} download aria-label={t("reports.lists.download", { list: l.title, format: t("reports.action.xlsx") })}>
                          {t("reports.action.xlsx")}
                        </a>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
        </nav>
        <div className="reports-main">
          {report && (
            <form className="record-form report-parameters no-print" onSubmit={submit} noValidate aria-label={report.title}>
              <div className="record-header">
                <div>
                  <h2>{report.title}</h2>
                  {report.description && <p className="muted">{report.description}</p>}
                </div>
                <div className="record-actions">
                  <button type="submit" className="button primary" disabled={busy} aria-keyshortcuts="Control+Enter">
                    {busy ? t("reports.running") : t("reports.action.show")}
                  </button>
                  {document && (
                    <button type="button" className="button" onClick={() => window.print()}>
                      {t("reports.action.print")}
                    </button>
                  )}
                  <a className="button" href={reportUrl(report, values, "pdf", documentLanguage, numerals, report.defaultGroupBy)} download>
                    {t("reports.action.pdf")}
                  </a>
                  <a className="button" href={reportUrl(report, values, "xlsx", documentLanguage, numerals, report.defaultGroupBy)} download>
                    {t("reports.action.xlsx")}
                  </a>
                  <a className="button" href={reportUrl(report, values, "csv", documentLanguage, numerals, report.defaultGroupBy)} download>
                    {t("reports.action.csv")}
                  </a>
                </div>
              </div>
              {error && (
                <div className="alert" role="alert">
                  {error}
                </div>
              )}
              <div className="form-grid">
                {report.parameters.map((p, index) => (
                  <ParameterField key={p.key} parameter={p} field={bind(p.key)} autoFocus={index === 0} />
                ))}
                {report.columns.some((c) => c.groupable) && (
                  <SelectField
                    field={bind("groupBy")}
                    label={t("reports.groupBy")}
                    empty={report.defaultGroupBy ? undefined : t("reports.noGrouping")}
                    options={[
                      ...(report.defaultGroupBy ? [{ value: "-", label: t("reports.noGrouping") }] : []),
                      ...report.columns.filter((c) => c.groupable).map((c) => ({ value: c.key, label: c.label })),
                    ]}
                  />
                )}
                <SelectField
                  field={{ ...bind("language"), value: documentLanguage }}
                  label={t("reports.documentLanguage")}
                  options={[
                    { value: "en", label: t("reports.language.en") },
                    { value: "ar", label: t("reports.language.ar") },
                  ]}
                />
              </div>
            </form>
          )}
          {!report && catalog && catalog.items.length > 0 && <p className="muted">{t("reports.choose")}</p>}
          {document && <ReportView document={document} />}
        </div>
      </div>
    </section>
  );
}

function ParameterField({ parameter, field, autoFocus }: { parameter: ReportSummary["parameters"][number]; field: FieldBinding<string>; autoFocus?: boolean }) {
  const { t, language } = useI18n();
  switch (parameter.type) {
    case "date":
      return <DateField field={field} label={parameter.label} autoFocus={autoFocus} />;
    case "boolean":
      return (
        <BooleanField
          field={{ ...field, value: field.value === "true", onChange: (v: boolean) => field.onChange(v ? "true" : "") } as unknown as FieldBinding<boolean>}
          label={parameter.label}
        />
      );
    case "choice":
      return <SelectField field={field} label={parameter.label} empty={parameter.required ? undefined : t("reports.any")} options={parameter.choices} autoFocus={autoFocus} />;
    case "reference":
      return parameter.lookupEndpoint ? (
        <LookupField
          field={field}
          label={parameter.label}
          endpoint={parameter.lookupEndpoint}
          autoFocus={autoFocus}
          labelOf={(row: LookupRow) => lookupLabel(row, parameter.lookupLabels, language)}
          hint={parameter.required ? undefined : t("reports.anyHint")}
        />
      ) : (
        <TextField field={field} label={parameter.label} dir="ltr" />
      );
    default:
      return <TextField field={field} label={parameter.label} maxLength={200} autoFocus={autoFocus} />;
  }
}

/** A looked-up record's name: its code and its name in the screen's language when it has both. */
export function lookupLabel(row: LookupRow, fields: string[], language: string): string {
  const values = fields.map((f) => row[f]).filter((v): v is string => typeof v === "string" && v.length > 0);
  const arabic = fields.find((f) => /Ar$/.test(f));
  const english = fields.find((f) => /En$/.test(f) || f === "displayName");
  const code = fields.find((f) => f === "code");
  const name = (language === "ar" && arabic && row[arabic]) || (english && row[english]) || values[0] || row.id;
  return code && row[code] && row[code] !== name ? `${String(row[code])} · ${String(name)}` : String(name);
}

/**
 * A report document as it prints: in its own language and direction (an Arabic document reads
 * right to left on an English screen), with the issuer, title, parameters, a record's facts, the
 * table with group headings, group totals and the grand total, and who printed it and when.
 */
export function ReportView({ document: doc }: { document: ReportDocument }) {
  const grouped = doc.groupBy !== null;
  const totals = doc.columns.some((c) => c.total);
  return (
    <div className="report-view" data-testid="report-document">
      <PrintDocument language={doc.language} numerals={doc.numerals} title={doc.subject ? `${doc.title} · ${doc.subject}` : doc.title} issuer={doc.issuer}>
        {doc.facts.length > 0 && (
          <dl className="print-facts report-facts">
            {doc.facts.map((f) => (
              <div key={f.label} className="print-fact">
                <dt>{f.label}</dt>
                <dd dir="auto">{f.text}</dd>
              </div>
            ))}
          </dl>
        )}
        {doc.parameters.length > 0 && (
          <dl className="print-facts report-parameters-used">
            {doc.parameters.map((f) => (
              <div key={`${f.label}-${f.text}`} className="print-fact">
                <dt>{f.label}</dt>
                <dd dir="auto">{f.text}</dd>
              </div>
            ))}
          </dl>
        )}
        <p className="report-count">{doc.rowCountText}</p>
        {doc.rowCount === 0 ? (
          <p className="muted">{doc.texts.empty}</p>
        ) : (
          <table className="report-table">
            <thead>
              <tr>
                {doc.columns.map((c) => (
                  <th key={c.key} scope="col" className={c.align === "end" ? "num" : undefined}>
                    {c.label}
                  </th>
                ))}
              </tr>
            </thead>
            {doc.groups.map((group, g) => (
              <tbody key={g}>
                {grouped && (
                  <tr className="report-group">
                    <th scope="rowgroup" colSpan={doc.columns.length}>
                      <span dir="auto">{group.label}</span> · {group.countText}
                    </th>
                  </tr>
                )}
                {group.rows.map((row, r) => (
                  <tr key={r}>
                    {row.cells.map((cell, c) => (
                      <td key={c} className={doc.columns[c]!.align === "end" ? "num" : undefined} dir={doc.columns[c]!.align === "end" ? undefined : "auto"}>
                        {cell.text}
                      </td>
                    ))}
                  </tr>
                ))}
                {grouped && totals && (
                  <tr className="report-subtotal">
                    {group.totals.map((cell, c) => (
                      <td key={c} className={doc.columns[c]!.align === "end" ? "num" : undefined}>
                        {cell?.text ?? (c === 0 ? `${doc.texts.total} · ${group.label ?? ""}` : "")}
                      </td>
                    ))}
                  </tr>
                )}
              </tbody>
            ))}
            {totals && (
              <tfoot>
                <tr className="report-total">
                  {doc.totals.map((cell, c) => (
                    <td key={c} className={doc.columns[c]!.align === "end" ? "num" : undefined}>
                      {cell?.text ?? (c === 0 ? doc.texts.total : "")}
                    </td>
                  ))}
                </tr>
              </tfoot>
            )}
          </table>
        )}
        <footer className="print-footer report-footer">
          <span>{doc.texts.printed}</span>
        </footer>
      </PrintDocument>
    </div>
  );
}

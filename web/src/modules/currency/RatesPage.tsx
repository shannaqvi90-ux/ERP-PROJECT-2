import { useEffect, useMemo, useRef, useState } from "react";
import { api, ApiError } from "../../kernel/api";
import { Dialog } from "../../kernel/dialog";
import { DateField, DecimalField, SelectField, TextField } from "../../kernel/forms/fields";
import { FormSection, RecordForm, type RecordNavigation } from "../../kernel/forms/RecordForm";
import { newRecord, useRecordPanel } from "../../kernel/forms/recordPanel";
import { useRecordForm } from "../../kernel/forms/useRecordForm";
import { useI18n } from "../../kernel/i18n";
import { ListView } from "../../kernel/lists/ListView";
import { useSession } from "../../kernel/session";
import { useShortcut } from "../../kernel/shortcuts";
import { useDay, useRateText } from "./parts";
import { ratesChanged, type CurrencyRow, type ImportResult, type ListPage, type Rate } from "./types";

/** Today as a calendar date in the browser (the server dates a rate left without one by the workspace's day). */
export function localToday(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** The workspace's currencies (a short list), for the rate form's choices. */
async function loadCurrencies(signal?: AbortSignal): Promise<CurrencyRow[]> {
  const page = await api<ListPage<CurrencyRow>>("GET", "/api/currency/currencies?take=200", undefined, { signal });
  return page.items;
}

/**
 * Exchange rates: the shared list over every rate (100,000 in the demo), newest first, searchable
 * by currency and reference, filtered or grouped by currency, date or source. Opening a rate shows
 * its form; Alt+N enters a new one (today's date, the working company's base currency), Alt+I
 * imports a CSV file.
 */
export function RatesPage() {
  const { t } = useI18n();
  const { can } = useSession();
  const rateText = useRateText();
  const day = useDay();
  const [currencies, setCurrencies] = useState<CurrencyRow[]>([]);
  const [importing, setImporting] = useState(false);
  const panel = useRecordPanel(can("currency.rates.create"));
  const latest = useRef({ startNew: panel.startNew, canImport: can("currency.rates.import") });
  latest.current = { startNew: panel.startNew, canImport: can("currency.rates.import") };
  useShortcut({
    id: "currency.screen.new",
    chord: "Alt+KeyN",
    labelKey: "currency.rate.new",
    keywordsKey: "currency.action.newRateKeywords",
    groupKey: "currency.shortcut.group",
    enabled: Boolean(panel.startNew),
    run: () => latest.current.startNew?.(),
  });
  useShortcut({
    id: "currency.screen.import",
    chord: "Alt+KeyI",
    labelKey: "currency.import.title",
    keywordsKey: "currency.action.importKeywords",
    groupKey: "currency.shortcut.group",
    enabled: can("currency.rates.import"),
    run: () => latest.current.canImport && setImporting(true),
  });
  useEffect(() => {
    if (!can("currency.currencies.read")) return;
    const controller = new AbortController();
    const load = () => loadCurrencies(controller.signal).then(setCurrencies, () => undefined);
    void load();
    window.addEventListener(ratesChanged, load);
    return () => {
      controller.abort();
      window.removeEventListener(ratesChanged, load);
    };
  }, [can]);
  useEffect(() => {
    window.addEventListener(ratesChanged, panel.refresh);
    return () => window.removeEventListener(ratesChanged, panel.refresh);
  }, [panel.refresh]);
  const quote = currencies[0]?.quoteCurrency ?? "AED";

  return (
    <div className="cur-list">
      <ListView
        listKey="currency.rates"
        titleKey="currency.rates.title"
        countKey="currency.rates.count"
        searchPlaceholderKey="currency.rates.search"
        can={can}
        openOnClick
        reloadKey={panel.reload}
        openId={panel.openId}
        onOpenIdChange={panel.onOpenIdChange}
        renderRecord={(id, close, nav) => (
          <RateForm
            key={panel.formKey}
            id={id === newRecord ? null : id}
            nav={nav}
            currencies={currencies}
            quote={quote}
            onSaved={panel.saved}
            onDeleted={panel.removed}
            onClose={close}
          />
        )}
        actions={
          <>
            {can("currency.rates.import") && (
              <button type="button" className="button" onClick={() => setImporting(true)} title={t("currency.import.hint")} aria-keyshortcuts="Alt+I">
                {t("currency.import.open")}
              </button>
            )}
            {panel.startNew && (
              <button type="button" className="button primary" onClick={panel.startNew} title={t("currency.common.newHint")} aria-keyshortcuts="Alt+N">
                {t("currency.rate.new")}
              </button>
            )}
          </>
        }
        renderCell={{
          rateDate: (r) => day(String(r.rateDate ?? "")),
          currency: (r) => <span dir="ltr">{String(r.currency ?? "")}</span>,
          quoteCurrency: (r) => <span dir="ltr">{String(r.quoteCurrency ?? "")}</span>,
          rate: (r) => (
            <span dir="ltr" className="num">
              {rateText(String(r.rate ?? ""))}
            </span>
          ),
          inverseRate: (r) => (
            <span dir="ltr" className="num">
              {rateText(String(r.inverseRate ?? ""))}
            </span>
          ),
          source: (r) => t(`currency.source.${String(r.source)}`),
        }}
      />
      {importing && <ImportDialog quote={quote} onClose={() => setImporting(false)} />}
    </div>
  );
}

type Draft = { currency: string; quoteCurrency: string; rateDate: string; rate: string; reference: string };

function RateForm({
  id,
  nav,
  currencies,
  quote,
  onSaved,
  onDeleted,
  onClose,
}: {
  id: string | null;
  nav?: RecordNavigation;
  currencies: CurrencyRow[];
  quote: string;
  onSaved: (id: string) => void;
  onDeleted: () => void;
  onClose: () => void;
}) {
  const { t } = useI18n();
  const { can } = useSession();
  const day = useDay();
  const rateText = useRateText();
  const [everyCompany, setEveryCompany] = useState(true);
  const [confirming, setConfirming] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const form = useRecordForm<Rate, Draft>(
    {
      load:
        id === null
          ? undefined
          : (signal) =>
              api<Rate>("GET", `/api/currency/rates/${id}`, undefined, { signal }).then((r) => {
                setEveryCompany(r.everyCompany);
                return r;
              }),
      initial: (r) => ({
        currency: r?.currency ?? "",
        quoteCurrency: r?.quoteCurrency ?? quote,
        rateDate: r?.rateDate ?? localToday(),
        rate: r?.rate ?? "",
        reference: r?.reference ?? "",
      }),
      canEdit: (id === null ? can("currency.rates.create") : can("currency.rates.update")) && everyCompany,
      save: (draft, rate) => {
        const body = {
          currency: draft.currency || null,
          quoteCurrency: draft.quoteCurrency || null,
          rateDate: draft.rateDate || null,
          rate: draft.rate.trim() || null,
          reference: draft.reference.trim() || null,
          version: rate?.version ?? null,
        };
        return rate === null ? api<Rate>("POST", "/api/currency/rates", body) : api<Rate>("PUT", `/api/currency/rates/${rate.id}`, body);
      },
      onSaved: (saved) => {
        onSaved(saved.id);
        window.dispatchEvent(new Event(ratesChanged));
      },
    },
    id,
  );
  const rate = form.record;
  const bind = form.bind;
  const choices = useMemo(
    () =>
      currencies
        .filter((c) => (c.isActive && c.code !== form.draft.quoteCurrency) || c.code === rate?.currency)
        .map((c) => ({ value: c.code, label: `${c.code} · ${c.nameEn}` })),
    [currencies, form.draft.quoteCurrency, rate?.currency],
  );
  const quotes = useMemo(() => {
    const codes = new Set([quote, ...(rate ? [rate.quoteCurrency] : [])]);
    return [...codes].map((c) => ({ value: c, label: c }));
  }, [quote, rate]);
  const remove = async () => {
    if (!rate) return;
    setConfirming(false);
    try {
      await api<void>("DELETE", `/api/currency/rates/${rate.id}`);
      window.dispatchEvent(new Event(ratesChanged));
      onDeleted();
    } catch (e) {
      setDeleteError(e instanceof ApiError || e instanceof Error ? e.message : String(e));
    }
  };
  const canDelete = rate !== null && everyCompany && can("currency.rates.delete");
  return (
    <>
      <RecordForm
        form={form}
        label={t("currency.rate.form")}
        title={rate ? `${rate.currency} · ${day(rate.rateDate)}` : t("currency.rate.new")}
        subtitle={rate ? t("currency.rate.reads", { currency: rate.currency, quote: rate.quoteCurrency, rate: rateText(rate.rate) }) : undefined}
        onClose={onClose}
        nav={nav}
        readOnlyReason={rate && can("currency.rates.update") && !everyCompany ? t("currency.common.needsEveryCompany") : undefined}
        actions={
          canDelete && (
            <button type="button" className="button danger" onClick={() => setConfirming(true)}>
              {t("currency.rate.delete")}
            </button>
          )
        }
      >
        {deleteError && (
          <div className="alert" role="alert">
            {deleteError}
          </div>
        )}
        <FormSection title={t("currency.rate.general")}>
          <SelectField field={bind("currency")} label={t("currency.rate.currency")} options={choices} empty={t("currency.currency.choose")}
            disabled={id !== null} autoFocus={id === null} />
          <SelectField field={bind("quoteCurrency")} label={t("currency.rate.quoteCurrency")} options={quotes} disabled={id !== null} />
          <DateField field={bind("rateDate")} label={t("currency.rate.date")} min="1990-01-01" max="2100-12-31" />
          <DecimalField field={bind("rate")} label={t("currency.rate.rateLabel", { currency: form.draft.currency || "…", quote: form.draft.quoteCurrency || quote })}
            scale={8} hint={rate ? t("currency.rate.inverseHint", { inverse: rateText(rate.inverseRate), currency: rate.currency, quote: rate.quoteCurrency }) : undefined} />
          <TextField field={bind("reference")} label={t("currency.rate.reference")} maxLength={200} hint={t("currency.rate.referenceHint")} />
        </FormSection>
        {rate && (
          <p className="muted cur-facts">
            {t(`currency.source.${rate.source}`)}
          </p>
        )}
      </RecordForm>
      {confirming && rate && (
        <Dialog title={t("currency.rate.deleteTitle")} onClose={() => setConfirming(false)} className="confirm-dialog">
          <p>{t("currency.rate.deleteQuestion", { currency: rate.currency, date: day(rate.rateDate) })}</p>
          <div className="dialog-actions">
            <button type="button" className="button danger" onClick={() => void remove()}>
              {t("currency.rate.delete")}
            </button>
            <button type="button" className="button" onClick={() => setConfirming(false)}>
              {t("currency.common.cancel")}
            </button>
          </div>
        </Dialog>
      )}
    </>
  );
}

/** Import rates from a CSV file: choose it, say whether rates held for the same dates are replaced,
 * and import. Nothing is saved unless every line is valid; the problems are listed by line. */
function ImportDialog({ quote, onClose }: { quote: string; onClose: () => void }) {
  const { t, format } = useI18n();
  const [file, setFile] = useState<File | null>(null);
  const [replace, setReplace] = useState(false);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const run = async () => {
    if (!file || busy) return;
    setBusy(true);
    setErrors([]);
    setResult(null);
    try {
      const content = await file.text();
      const answer = await api<ImportResult>("POST", "/api/currency/rates/import", { fileName: file.name, content, replaceExisting: replace, quoteCurrency: quote });
      setResult(answer);
      window.dispatchEvent(new Event(ratesChanged));
    } catch (e) {
      if (e instanceof ApiError) {
        const lines = Object.values(e.fieldErrors).flat().map((f) => f.message);
        setErrors(lines.length > 0 ? lines : [e.message]);
      } else {
        setErrors([e instanceof Error ? e.message : String(e)]);
      }
    } finally {
      setBusy(false);
    }
  };
  return (
    <Dialog title={t("currency.import.title")} onClose={onClose} className="cur-import">
      <p className="muted">{t("currency.import.format", { quote })}</p>
      <div className="field">
        <label className="field-label" htmlFor="cur-import-file">
          {t("currency.import.file")}
        </label>
        <input id="cur-import-file" type="file" accept=".csv,.txt,text/csv,text/plain" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
      </div>
      <label className="check">
        <input type="checkbox" checked={replace} onChange={(e) => setReplace(e.target.checked)} />
        <span>{t("currency.import.replace")}</span>
      </label>
      {result && (
        <div className="notice" role="status">
          {t("currency.import.done", {
            lines: format.number(result.lines),
            added: format.number(result.added),
            replaced: format.number(result.replaced),
            kept: format.number(result.kept),
            unchanged: format.number(result.unchanged),
          })}
        </div>
      )}
      {errors.length > 0 && (
        <div className="alert" role="alert">
          <ul className="cur-import-errors">
            {errors.map((line, i) => (
              <li key={i}>{line}</li>
            ))}
          </ul>
        </div>
      )}
      <div className="dialog-actions">
        <button type="button" className="button primary" disabled={!file || busy} onClick={() => void run()}>
          {busy ? t("currency.import.running") : t("currency.import.run")}
        </button>
        <button type="button" className="button" onClick={onClose}>
          {t("currency.common.close")}
        </button>
      </div>
    </Dialog>
  );
}

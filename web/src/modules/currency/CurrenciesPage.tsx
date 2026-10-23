import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "../../kernel/api";
import { BooleanField, SelectField, TextField } from "../../kernel/forms/fields";
import { FormSection, RecordForm, type RecordNavigation } from "../../kernel/forms/RecordForm";
import { newRecord, useRecordPanel } from "../../kernel/forms/recordPanel";
import { useRecordForm } from "../../kernel/forms/useRecordForm";
import { useI18n } from "../../kernel/i18n";
import { ListView } from "../../kernel/lists/ListView";
import { Link, recordPath } from "../../kernel/router";
import { useSession } from "../../kernel/session";
import { useShortcut } from "../../kernel/shortcuts";
import { browserCurrencyName, Converter, QuickRate, useDay, useRateText } from "./parts";
import { ratesChanged, type Catalog, type Currency, type ListPage, type RateRow } from "./types";

/**
 * The workspace's currencies: the shared list (search by code or name, filters, views), each row
 * with its latest rate in the working company's base currency. Opening a currency shows its form
 * (names, in use, how old a rate may be) and, below it, today's rate typed in one field (focused
 * as the panel opens: type the rate, press Enter), the amount converter and the latest rates.
 * Alt+N enables another ISO 4217 currency.
 */
export function CurrenciesPage() {
  const { t } = useI18n();
  const { can } = useSession();
  const rateText = useRateText();
  const day = useDay();
  const panel = useRecordPanel(can("currency.currencies.create"));
  const latest = useRef(panel.startNew);
  latest.current = panel.startNew;
  useShortcut({
    id: "currency.screen.new",
    chord: "Alt+KeyN",
    labelKey: "currency.currency.new",
    keywordsKey: "currency.action.newCurrencyKeywords",
    groupKey: "currency.shortcut.group",
    enabled: Boolean(panel.startNew),
    run: () => latest.current?.(),
  });
  useEffect(() => {
    window.addEventListener(ratesChanged, panel.refresh);
    return () => window.removeEventListener(ratesChanged, panel.refresh);
  }, [panel.refresh]);

  return (
    <div className="cur-list">
      <ListView
        listKey="currency.currencies"
        titleKey="currency.currencies.title"
        countKey="currency.currencies.count"
        searchPlaceholderKey="currency.currencies.search"
        can={can}
        openOnClick
        reloadKey={panel.reload}
        openId={panel.openId}
        onOpenIdChange={panel.onOpenIdChange}
        renderRecord={(id, close, nav) =>
          id === newRecord ? (
            <EnableCurrencyForm key={panel.formKey} onSaved={panel.saved} onClose={close} />
          ) : (
            <CurrencyForm key={panel.formKey} id={id} nav={nav} onSaved={panel.saved} onClose={close} />
          )
        }
        actions={
          panel.startNew && (
            <button type="button" className="button primary" onClick={panel.startNew} title={t("currency.common.newHint")} aria-keyshortcuts="Alt+N">
              {t("currency.currency.new")}
            </button>
          )
        }
        renderCell={{
          code: (c) => <span dir="ltr">{String(c.code ?? "")}</span>,
          nameAr: (c) => <span dir="rtl">{String(c.nameAr ?? "")}</span>,
          latestRate: (c) =>
            c.latestRate ? (
              <span dir="ltr" className="num">
                {rateText(String(c.latestRate))} {String(c.quoteCurrency ?? "")}
              </span>
            ) : c.code === c.quoteCurrency ? (
              <span className="muted">{t("currency.currency.isBase")}</span>
            ) : (
              <span className="muted">{t("currency.currency.noRate")}</span>
            ),
          latestRateDate: (c) => (c.latestRateDate ? day(String(c.latestRateDate)) : ""),
          isActive: (c) => (c.isActive ? t("currency.common.active") : <span className="muted">{t("currency.common.inactive")}</span>),
        }}
      />
    </div>
  );
}

type Draft = { nameEn: string; nameAr: string; isActive: boolean; maxRateAgeDays: string };

function CurrencyForm({ id, nav, onSaved, onClose }: { id: string; nav?: RecordNavigation; onSaved: (id: string) => void; onClose: () => void }) {
  const { t, language } = useI18n();
  const { can } = useSession();
  const [everyCompany, setEveryCompany] = useState(true);
  const form = useRecordForm<Currency, Draft>(
    {
      load: (signal) =>
        api<Currency>("GET", `/api/currency/currencies/${id}`, undefined, { signal }).then((c) => {
          setEveryCompany(c.everyCompany);
          return c;
        }),
      initial: (c) => ({ nameEn: c?.nameEn ?? "", nameAr: c?.nameAr ?? "", isActive: c?.isActive ?? true, maxRateAgeDays: String(c?.maxRateAgeDays ?? 7) }),
      canEdit: can("currency.currencies.update") && everyCompany,
      save: (draft, currency) =>
        api<Currency>("PUT", `/api/currency/currencies/${currency!.id}`, {
          nameEn: draft.nameEn,
          nameAr: draft.nameAr,
          isActive: draft.isActive,
          maxRateAgeDays: /^\d{1,3}$/.test(draft.maxRateAgeDays.trim()) ? Number.parseInt(draft.maxRateAgeDays.trim(), 10) : null,
          version: currency!.version,
        }),
      onSaved: (saved) => {
        onSaved(saved.id);
        window.dispatchEvent(new Event(ratesChanged));
      },
    },
    id,
  );
  const currency = form.record;
  const bind = form.bind;
  const name = currency ? (language === "ar" ? currency.nameAr || currency.nameEn : currency.nameEn || currency.nameAr) : "";
  const foreign = currency !== null && currency.code !== currency.quoteCurrency;
  const canAddRate = foreign && currency.isActive && currency.everyCompany && can("currency.rates.create");
  return (
    <RecordForm
      form={form}
      label={t("currency.currency.form")}
      title={currency ? currency.code : ""}
      subtitle={name}
      onClose={onClose}
      nav={nav}
      readOnlyReason={currency && can("currency.currencies.update") && !everyCompany ? t("currency.common.needsEveryCompany") : undefined}
      after={
        currency && (
          <div className="cur-panel-extra">
            {canAddRate && <QuickRate currency={currency.code} quote={currency.quoteCurrency} autoFocus />}
            {foreign && can("currency.rates.read") && <Converter currency={currency.code} quote={currency.quoteCurrency} />}
            {foreign && can("currency.rates.read") && <RecentRates currency={currency.code} quote={currency.quoteCurrency} />}
          </div>
        )
      }
    >
      <FormSection title={t("currency.currency.general")}>
        <TextField field={bind("nameEn")} label={t("currency.currency.nameEn")} dir="ltr" maxLength={100} />
        <TextField field={bind("nameAr")} label={t("currency.currency.nameAr")} dir="rtl" maxLength={100} />
        <TextField field={bind("maxRateAgeDays")} label={t("currency.currency.maxRateAgeDays")} dir="ltr" inputMode="numeric" maxLength={3}
          hint={t("currency.currency.maxRateAgeHint")} />
        <BooleanField field={bind("isActive")} label={t("currency.common.active")} disabled={currency?.isBaseCurrency} />
      </FormSection>
      {currency && (
        <p className="muted cur-facts">
          {t("currency.currency.facts", { number: String(currency.number).padStart(3, "0"), minorUnits: currency.minorUnits })}
          {currency.isBaseCurrency ? ` ${t("currency.currency.baseFact")}` : ""}
        </p>
      )}
    </RecordForm>
  );
}

/** The latest rates of one currency, newest first, with a way to all of them. */
function RecentRates({ currency, quote }: { currency: string; quote: string }) {
  const { t } = useI18n();
  const day = useDay();
  const rateText = useRateText();
  const [rows, setRows] = useState<RateRow[] | null>(null);
  const [version, setVersion] = useState(0);
  useEffect(() => {
    const changed = () => setVersion((v) => v + 1);
    window.addEventListener(ratesChanged, changed);
    return () => window.removeEventListener(ratesChanged, changed);
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    const params = new URLSearchParams({ filter: `currency eq '${currency}' and quoteCurrency eq '${quote}'`, take: "7" });
    api<ListPage<RateRow>>("GET", `/api/currency/rates?${params}`, undefined, { signal: controller.signal }).then(
      (page) => setRows(page.items),
      () => !controller.signal.aborted && setRows([]),
    );
    return () => controller.abort();
  }, [currency, quote, version]);
  if (rows === null) return null;
  return (
    <section className="cur-recent" aria-label={t("currency.recent.title")}>
      <h3>{t("currency.recent.title")}</h3>
      {rows.length === 0 ? (
        <p className="muted">{t("currency.recent.none")}</p>
      ) : (
        <table className="cur-recent-table">
          <thead>
            <tr>
              <th scope="col">{t("currency.rate.date")}</th>
              <th scope="col">{t("currency.rate.rateIn", { quote })}</th>
              <th scope="col">{t("currency.rate.source")}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id}>
                <td>
                  <Link to={recordPath("/currency/rates", r.id)}>{day(r.rateDate)}</Link>
                </td>
                <td className="num" dir="ltr">
                  {rateText(r.rate)}
                </td>
                <td>{t(`currency.source.${r.source}`)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <Link className="button link" to={`/currency/rates?${new URLSearchParams({ q: currency })}`}>
        {t("currency.recent.all", { currency })}
      </Link>
    </section>
  );
}

type NewDraft = { code: string; nameEn: string; nameAr: string; maxRateAgeDays: string };

/** Enable another ISO 4217 currency: choose it, and its names start as ISO 4217's English name and
 * the browser's Arabic name (both may be changed). */
function EnableCurrencyForm({ onSaved, onClose }: { onSaved: (id: string) => void; onClose: () => void }) {
  const { t } = useI18n();
  const { can } = useSession();
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    api<Catalog>("GET", "/api/currency/catalog", undefined, { signal: controller.signal }).then(setCatalog, () => undefined);
    return () => controller.abort();
  }, []);
  const choices = useMemo(
    () => (catalog?.currencies ?? []).filter((c) => !c.enabled).map((c) => ({ value: c.code, label: `${c.code} · ${c.nameEn}` })),
    [catalog],
  );
  const form = useRecordForm<Currency, NewDraft>({
    initial: () => ({ code: "", nameEn: "", nameAr: "", maxRateAgeDays: "7" }),
    canEdit: can("currency.currencies.create"),
    save: (draft) =>
      api<Currency>("POST", "/api/currency/currencies", {
        code: draft.code || null,
        nameEn: draft.nameEn.trim() || null,
        nameAr: draft.nameAr.trim() || null,
        maxRateAgeDays: /^\d{1,3}$/.test(draft.maxRateAgeDays.trim()) ? Number.parseInt(draft.maxRateAgeDays.trim(), 10) : null,
      }),
    onSaved: (saved) => {
      onSaved(saved.id);
      window.dispatchEvent(new Event(ratesChanged));
    },
  });
  const bind = form.bind;
  const code = bind("code");
  return (
    <RecordForm form={form} label={t("currency.currency.form")} title={t("currency.currency.new")} onClose={onClose}>
      <FormSection title={t("currency.currency.general")}>
        <SelectField
          field={{
            ...code,
            onChange: (value: string) => {
              // The names start as ISO 4217's English name and the browser's Arabic one.
              const entry = catalog?.currencies.find((c) => c.code === value);
              form.update((d) => ({
                ...d,
                code: value,
                nameEn: entry?.nameEn ?? d.nameEn,
                nameAr: value ? (browserCurrencyName(value, "ar") ?? d.nameAr) : d.nameAr,
              }));
            },
          }}
          label={t("currency.currency.code")}
          options={choices}
          empty={t("currency.currency.choose")}
          autoFocus
        />
        <TextField field={bind("nameEn")} label={t("currency.currency.nameEn")} dir="ltr" maxLength={100} />
        <TextField field={bind("nameAr")} label={t("currency.currency.nameAr")} dir="rtl" maxLength={100} />
        <TextField field={bind("maxRateAgeDays")} label={t("currency.currency.maxRateAgeDays")} dir="ltr" inputMode="numeric" maxLength={3}
          hint={t("currency.currency.maxRateAgeHint")} />
      </FormSection>
      {catalog && <p className="muted cur-facts">{t("currency.catalog.published", { date: catalog.published })}</p>}
    </RecordForm>
  );
}

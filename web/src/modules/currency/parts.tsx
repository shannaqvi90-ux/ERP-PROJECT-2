import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { api, ApiError } from "../../kernel/api";
import { decimalInput } from "../../kernel/forms/fields";
import { useI18n } from "../../kernel/i18n";
import { ratesChanged, type Conversion, type Rate } from "./types";

/** A currency's name in the screen's language from the browser's own CLDR data, or null. */
export function browserCurrencyName(code: string, language: string): string | null {
  try {
    const name = new Intl.DisplayNames([language], { type: "currency" }).of(code);
    return name && name !== code ? name : null;
  } catch {
    return null;
  }
}

/** A calendar date (yyyy-mm-dd) shown as that day in the screen's language. */
export function useDay() {
  const { format } = useI18n();
  return (value: string | null | undefined) => {
    if (!value) return "";
    const [y, m, d] = value.split("-").map((part) => Number.parseInt(part, 10));
    return y && m && d ? format.date(new Date(y, m - 1, d)) : value;
  };
}

/** A rate as entered (its own decimals), in the screen's digits. */
export function useRateText() {
  const { format } = useI18n();
  return (value: string | null | undefined) => (value ? format.decimal(value) : "");
}

const messageOf = (error: unknown) => {
  if (error instanceof ApiError) {
    const fields = Object.values(error.fieldErrors).flat().map((f) => f.message);
    return fields.length > 0 ? fields.join(" ") : error.message;
  }
  return error instanceof Error ? error.message : String(error);
};

/**
 * Today's rate for one currency, typed straight into the currency's panel: the field has the
 * focus when the panel opens, Enter saves (POST /api/currency/rates, dated today in the workspace's
 * time zone by the server) and says what was saved. When today already has a rate, the first Enter
 * says so and shows it, and a second Enter replaces it (PUT with its version).
 */
export function QuickRate({ currency, quote, autoFocus, onSaved }: { currency: string; quote: string; autoFocus?: boolean; onSaved?: (rate: Rate) => void }) {
  const { t } = useI18n();
  const rateText = useRateText();
  const day = useDay();
  const id = useId();
  const [value, setValue] = useState("");
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [replacing, setReplacing] = useState<Rate | null>(null);
  const input = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!autoFocus) return;
    // After the panel has drawn (the list moves the focus into a panel it opens).
    const timer = window.setTimeout(() => input.current?.focus(), 0);
    return () => window.clearTimeout(timer);
  }, [autoFocus, currency]);

  const save = async () => {
    const rate = value.trim();
    if (busy || rate === "") return;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const saved = replacing
        ? await api<Rate>("PUT", `/api/currency/rates/${replacing.id}`, {
            currency, quoteCurrency: quote, rateDate: replacing.rateDate, rate, reference: replacing.reference, version: replacing.version,
          })
        : await api<Rate>("POST", "/api/currency/rates", { currency, quoteCurrency: quote, rate });
      setReplacing(null);
      setValue("");
      setNotice(t("currency.quick.saved", { currency, quote, rate: rateText(saved.rate), date: day(saved.rateDate) }));
      window.dispatchEvent(new Event(ratesChanged));
      onSaved?.(saved);
    } catch (e) {
      if (e instanceof ApiError && e.code === "currency.rateExists") {
        // Today has a rate already: show it, and let a second Enter replace it.
        try {
          const params = new URLSearchParams({ filter: `currency eq '${currency}' and quoteCurrency eq '${quote}'`, take: "1" });
          const page = await api<{ items: Rate[] }>("GET", `/api/currency/rates?${params}`);
          const held = page.items[0];
          if (held) {
            setReplacing(held);
            setError(t("currency.quick.exists", { rate: rateText(held.rate), date: day(held.rateDate) }));
          } else {
            setError(e.message);
          }
        } catch {
          setError(e.message);
        }
      } else {
        setError(messageOf(e));
      }
    } finally {
      setBusy(false);
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter" && !event.nativeEvent.isComposing) {
      event.preventDefault();
      event.stopPropagation();
      void save();
    }
  };

  return (
    <section className="cur-quick" aria-labelledby={`${id}-title`}>
      <h3 id={`${id}-title`}>{t("currency.quick.title")}</h3>
      <div className="field" data-field="rate">
        <label className="field-label" htmlFor={id}>
          {t("currency.quick.label", { currency, quote })}
        </label>
        <span className="cur-quick-row">
          <span className="cur-unit" dir="ltr">1 {currency} =</span>
          <input
            ref={input}
            id={id}
            name="rate"
            className="num"
            inputMode="decimal"
            dir="ltr"
            value={value}
            autoComplete="off"
            aria-invalid={error ? true : undefined}
            aria-describedby={`${id}-hint`}
            disabled={busy}
            onKeyDown={onKeyDown}
            onChange={(e) => {
              const next = decimalInput(e.target.value, 8);
              if (next !== null) {
                setValue(next);
                setNotice(null);
              }
            }}
          />
          <span className="cur-unit" dir="ltr">{quote}</span>
          <button type="button" className="button" disabled={busy || value.trim() === ""} onClick={() => void save()}>
            {replacing ? t("currency.quick.replace") : t("currency.quick.save")}
          </button>
        </span>
        <span className="field-hint muted" id={`${id}-hint`}>
          {t("currency.quick.hint")}
        </span>
        {error && (
          <span className="field-error" role="alert">
            {error}
          </span>
        )}
        {notice && (
          <span className="notice" role="status">
            {notice}
          </span>
        )}
      </div>
    </section>
  );
}

/**
 * An amount of one currency in the working company's base currency, as it is typed: the rate
 * that applies on the date (today unless another is chosen) and the base amount rounded once to the
 * base currency's minor units, as the server works it out (GET /api/currency/convert).
 */
export function Converter({ currency, quote }: { currency: string; quote: string }) {
  const { t, format } = useI18n();
  const day = useDay();
  const rateText = useRateText();
  const id = useId();
  const [amount, setAmount] = useState("");
  const [date, setDate] = useState("");
  const [result, setResult] = useState<Conversion | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [version, setVersion] = useState(0);

  useEffect(() => {
    const changed = () => setVersion((v) => v + 1);
    window.addEventListener(ratesChanged, changed);
    return () => window.removeEventListener(ratesChanged, changed);
  }, []);

  useEffect(() => {
    const typed = amount.trim();
    if (typed === "" || typed === "-" || typed === ".") {
      setResult(null);
      setError(null);
      return;
    }
    const controller = new AbortController();
    const params = new URLSearchParams({ amount: typed, currency });
    if (date) params.set("date", date);
    api<Conversion>("GET", `/api/currency/convert?${params}`, undefined, { signal: controller.signal }).then(
      (answer) => {
        setResult(answer);
        setError(null);
      },
      (e: unknown) => {
        if (controller.signal.aborted) return;
        setResult(null);
        setError(messageOf(e));
      },
    );
    return () => controller.abort();
  }, [amount, date, currency, version]);

  return (
    <section className="cur-convert" aria-labelledby={`${id}-title`}>
      <h3 id={`${id}-title`}>{t("currency.convert.title", { quote })}</h3>
      <div className="cur-convert-row">
        <div className="field" data-field="amount">
          <label className="field-label" htmlFor={`${id}-amount`}>
            {t("currency.convert.amount", { currency })}
          </label>
          <input
            id={`${id}-amount`}
            name="amount"
            className="num"
            inputMode="decimal"
            dir="ltr"
            value={amount}
            autoComplete="off"
            onChange={(e) => {
              const next = decimalInput(e.target.value, 10);
              if (next !== null) setAmount(next);
            }}
          />
        </div>
        <div className="field" data-field="date">
          <label className="field-label" htmlFor={`${id}-date`}>
            {t("currency.convert.date")}
          </label>
          <input id={`${id}-date`} name="date" type="date" dir="ltr" value={date} onChange={(e) => setDate(e.target.value)} />
        </div>
      </div>
      <output className="cur-convert-result" htmlFor={`${id}-amount ${id}-date`} aria-live="polite" data-testid="conversion">
        {result && (
          <>
            <span className="cur-convert-figure" dir="ltr">
              {format.amount(result.money.amount, result.money.currency)} = <strong>{format.amount(result.money.baseAmount, result.money.baseCurrency)}</strong>
            </span>
            <span className="muted">
              {t(result.exact ? "currency.convert.rateOf" : "currency.convert.rateBefore", {
                rate: rateText(result.money.exchangeRate),
                quote: result.money.baseCurrency,
                currency: result.money.currency,
                date: day(result.rateDate),
              })}
            </span>
          </>
        )}
        {error && (
          <span className="field-error" role="alert">
            {error}
          </span>
        )}
      </output>
    </section>
  );
}

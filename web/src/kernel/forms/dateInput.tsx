import { useEffect, useRef, useState, type InputHTMLAttributes } from "react";
import { Icon } from "../icons";
import { useI18n } from "../i18n";
import { latinDigits } from "./phone";

/**
 * Dates on screen are day/month/year (18/11/2026), never the browser's own order. A date is held
 * as yyyy-mm-dd (what the server takes); the person may type either form, in either set of digits.
 */
const real = (y: number, m: number, d: number): boolean => {
  const date = new Date(Date.UTC(y, m - 1, d));
  return date.getUTCFullYear() === y && date.getUTCMonth() === m - 1 && date.getUTCDate() === d;
};

/** A complete, real calendar date typed as dd/mm/yyyy (also - or .) or yyyy-mm-dd, as yyyy-mm-dd; else null. */
export function parseDate(text: string): string | null {
  const t = latinDigits(text).trim();
  const pad = (n: number, w: number) => String(n).padStart(w, "0");
  let m = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(t);
  if (m) return real(+m[1]!, +m[2]!, +m[3]!) ? `${m[1]}-${pad(+m[2]!, 2)}-${pad(+m[3]!, 2)}` : null;
  m = /^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$/.exec(t);
  if (m) return real(+m[3]!, +m[2]!, +m[1]!) ? `${m[3]}-${pad(+m[2]!, 2)}-${pad(+m[1]!, 2)}` : null;
  return null;
}

/** yyyy-mm-dd as dd/mm/yyyy; anything else as it is. */
export function displayDate(iso: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  return m ? `${m[3]}/${m[2]}/${m[1]}` : iso;
}

type Props = Omit<InputHTMLAttributes<HTMLInputElement>, "value" | "onChange" | "type" | "min" | "max"> & {
  /** yyyy-mm-dd, or what was typed while it is not yet a date. */
  value: string;
  /** yyyy-mm-dd once the text is a real date; the text itself while it is not; "" when empty. */
  onChange: (value: string) => void;
  min?: string;
  max?: string;
  /** No calendar button (a narrow place, such as a filter). */
  noPicker?: boolean;
};

/** A text field for a date, with the calendar picker of the browser as a pointer-only helper. */
export function DateInput({ value, onChange, min, max, noPicker, ...rest }: Props) {
  const { t } = useI18n();
  const [draft, setDraft] = useState<string | null>(null);
  const picker = useRef<HTMLInputElement>(null);

  // A value set from outside (a reset, a loaded record) replaces what was being typed.
  useEffect(() => {
    setDraft((d) => (d !== null && (parseDate(d) ?? d) === value ? d : null));
  }, [value]);

  const shown = draft ?? displayDate(value ?? "");
  return (
    <span className="date-input">
      <input
        {...rest}
        type="text"
        dir="ltr"
        inputMode="numeric"
        autoComplete="off"
        maxLength={10}
        placeholder={rest.placeholder ?? t("forms.date.placeholder")}
        value={shown}
        onBlur={(e) => {
          setDraft(null);
          rest.onBlur?.(e);
        }}
        onChange={(e) => {
          let text = latinDigits(e.target.value).replace(/[^0-9/.-]/g, "");
          // Typing forward, the slash after the day and the month comes by itself.
          if (draft !== null && text.length > draft.length && /^(\d{2}|\d{2}\/\d{2})$/.test(text)) text += "/";
          setDraft(text);
          onChange(text === "" ? "" : (parseDate(text) ?? text));
        }}
      />
      {!noPicker && (
        <>
          <button type="button" className="date-picker-button" tabIndex={-1} aria-label={t("forms.date.pick")} title={t("forms.date.pick")} onClick={() => picker.current?.showPicker?.()}>
            <Icon name="calendar" />
          </button>
          <input
            ref={picker}
            className="date-picker-native"
            type="date"
            tabIndex={-1}
            aria-label={t("forms.date.pick")}
            aria-hidden="true"
            min={min}
            max={max}
            value={parseDate(value) ?? ""}
            onChange={(e) => {
              setDraft(null);
              onChange(e.target.value);
            }}
          />
        </>
      )}
    </span>
  );
}

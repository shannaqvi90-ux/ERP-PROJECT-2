import { useEffect, useRef, useState, type InputHTMLAttributes } from "react";
import { Icon } from "../icons";
import { useI18n } from "../i18n";
import { latinDigits } from "./phone";
import { Calendar } from "./calendar";

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

/**
 * What the box holds after a keystroke. Typing forward, the slash after the day and the month comes by
 * itself, so a slash the person types as well (the way the placeholder shows the date) is not added
 * twice, and a year typed first (2027-06-15) is not broken by a slash that came after its first two
 * digits: 20/27/- becomes 2027-. (A year whose last two digits could be a month, 2006, stays day and month: the
 * slash cannot tell them apart; the calendar takes that year.)
 */
export function typedDate(draft: string | null, raw: string): string {
  let text = latinDigits(raw).replace(/[^0-9/.-]/g, "");
  text = text.replace(/^(1[89]|2[0-2])\/(00|1[3-9]|[2-9]\d)\/-$/, "$1$2-");
  text = text.replace(/([/.-])[/.-]+/g, "$1");
  if (draft !== null && text.length > draft.length && /^(\d{2}|\d{2}\/\d{2})$/.test(text)) text += "/";
  return text;
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
  /** For one end of a date range: both ends, so the calendar draws the days between them. */
  rangeFrom?: string;
  rangeTo?: string;
};

/** A text field for a date, with the product's own calendar (kernel/forms/calendar.tsx), keyboard first. */
export function DateInput({ value, onChange, min, max, noPicker, rangeFrom, rangeTo, ...rest }: Props) {
  const { t } = useI18n();
  const [draft, setDraft] = useState<string | null>(null);
  const opener = useRef<HTMLButtonElement>(null);
  const [open, setOpen] = useState(false);

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
        onKeyDown={(e) => {
          rest.onKeyDown?.(e);
          if (!noPicker && e.altKey && e.key === "ArrowDown") {
            e.preventDefault();
            setOpen(true);
          }
        }}
        onBlur={(e) => {
          setDraft(null);
          rest.onBlur?.(e);
        }}
        onChange={(e) => {
          const text = typedDate(draft, e.target.value);
          setDraft(text);
          onChange(text === "" ? "" : (parseDate(text) ?? text));
        }}
      />
      {!noPicker && (
        <>
          <button
            type="button"
            ref={opener}
            className="date-picker-button"
            aria-label={t("forms.date.pick")}
            aria-haspopup="dialog"
            aria-expanded={open}
            title={t("forms.date.pick")}
            onClick={() => setOpen((o) => !o)}
          >
            <Icon name="calendar" />
          </button>
          {open && (
            <div className="calendar-pop" onBlur={(e) => { if (!e.currentTarget.contains(e.relatedTarget as Node) && e.relatedTarget !== opener.current) setOpen(false); }}>
              <Calendar
                value={parseDate(value) ?? ""}
                min={min}
                max={max}
                rangeFrom={parseDate(rangeFrom ?? "") ?? undefined}
                rangeTo={parseDate(rangeTo ?? "") ?? undefined}
                onClose={() => { setOpen(false); opener.current?.focus(); }}
                onPick={(d) => {
                  setDraft(null);
                  onChange(d);
                  setOpen(false);
                  opener.current?.focus();
                }}
              />
            </div>
          )}
        </>
      )}
    </span>
  );
}

import { useEffect, useMemo, useRef, useState } from "react";
import { Icon } from "../icons";
import { useI18n } from "../i18n";
import { displayDate } from "./dateInput";

/**
 * A calendar month for choosing a date by keyboard or pointer, drawn by the product (the browser's
 * own picker is pointer-only and left-to-right). Arrow keys move by a day or a week, Page Up and
 * Page Down by a month (Shift: a year), Home and End to the start and end of the week, Enter or
 * Space chooses, Escape closes. The week starts on Monday, as the UAE work week does. Dates are
 * yyyy-mm-dd throughout, so a date never passes through a time zone.
 */
const pad = (n: number) => String(n).padStart(2, "0");
const iso = (d: Date) => `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`;
const fromIso = (s: string): Date | null => {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s);
  return m ? new Date(Date.UTC(+m[1]!, +m[2]! - 1, +m[3]!)) : null;
};
const addDays = (d: Date, n: number) => new Date(d.getTime() + n * 86_400_000);
const addMonths = (d: Date, n: number) => {
  const target = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + n, 1));
  const last = new Date(Date.UTC(target.getUTCFullYear(), target.getUTCMonth() + 1, 0)).getUTCDate();
  target.setUTCDate(Math.min(d.getUTCDate(), last));
  return target;
};
/** Monday = 0 … Sunday = 6. */
const weekday = (d: Date) => (d.getUTCDay() + 6) % 7;

export type CalendarProps = {
  /** The chosen date (yyyy-mm-dd), or "" / a half-typed text for none. */
  value: string;
  onPick: (date: string) => void;
  onClose: () => void;
  min?: string;
  max?: string;
  /** For a range: the other end, so the days between are drawn as part of it. */
  rangeFrom?: string;
  rangeTo?: string;
};

export function Calendar({ value, onPick, onClose, min, max, rangeFrom, rangeTo }: CalendarProps) {
  const { t, language } = useI18n();
  const today = useMemo(() => {
    const n = new Date();
    return new Date(Date.UTC(n.getFullYear(), n.getMonth(), n.getDate()));
  }, []);
  const start = fromIso(value) ?? fromIso(rangeFrom ?? "") ?? today;
  const [cursor, setCursor] = useState<Date>(start);
  const grid = useRef<HTMLDivElement>(null);
  const moved = useRef(false);
  const locale = language === "ar" ? "ar-AE-u-nu-latn" : "en-GB";
  const monthName = new Intl.DateTimeFormat(locale, { month: "long", year: "numeric", timeZone: "UTC" }).format(cursor);
  const weekdayNames = useMemo(
    () => Array.from({ length: 7 }, (_, i) => new Intl.DateTimeFormat(locale, { weekday: "short", timeZone: "UTC" }).format(new Date(Date.UTC(2026, 0, 5 + i)))),
    [locale],
  );

  const first = new Date(Date.UTC(cursor.getUTCFullYear(), cursor.getUTCMonth(), 1));
  const gridStart = addDays(first, -weekday(first));
  const days = Array.from({ length: 42 }, (_, i) => addDays(gridStart, i));
  const out = (d: Date) => (min && iso(d) < min) || (max && iso(d) > max);

  useEffect(() => {
    grid.current?.querySelector<HTMLElement>('[tabindex="0"]')?.focus();
  }, [cursor]);

  const go = (next: Date) => {
    moved.current = true;
    setCursor(next);
  };
  const onKey = (e: React.KeyboardEvent) => {
    const big = e.shiftKey;
    const map: Record<string, () => Date> = {
      ArrowLeft: () => addDays(cursor, language === "ar" ? 1 : -1),
      ArrowRight: () => addDays(cursor, language === "ar" ? -1 : 1),
      ArrowUp: () => addDays(cursor, -7),
      ArrowDown: () => addDays(cursor, 7),
      PageUp: () => addMonths(cursor, big ? -12 : -1),
      PageDown: () => addMonths(cursor, big ? 12 : 1),
      Home: () => addDays(cursor, -weekday(cursor)),
      End: () => addDays(cursor, 6 - weekday(cursor)),
    };
    const step = map[e.key];
    if (step) {
      e.preventDefault();
      e.stopPropagation();
      go(step());
    } else if (e.key === "Escape") {
      e.preventDefault();
      e.stopPropagation();
      onClose();
    } else if ((e.key === "Enter" || e.key === " ") && !out(cursor)) {
      e.preventDefault();
      e.stopPropagation();
      onPick(iso(cursor));
    } else if (e.key === "t" || e.key === "T") {
      e.preventDefault();
      e.stopPropagation();
      go(today);
    }
  };

  return (
    <div className="calendar" role="dialog" aria-label={t("forms.date.pick")} onKeyDown={onKey}>
      <div className="calendar-head">
        <button type="button" className="calendar-nav" aria-label={t("forms.date.previousMonth")} title={t("forms.date.previousMonth")} onClick={() => go(addMonths(cursor, -1))}>
          <Icon name="back" />
        </button>
        <strong className="calendar-month" aria-live="polite">{monthName}</strong>
        <button type="button" className="calendar-nav" aria-label={t("forms.date.nextMonth")} title={t("forms.date.nextMonth")} onClick={() => go(addMonths(cursor, 1))}>
          <Icon name="chevron" />
        </button>
      </div>
      <div className="calendar-grid" role="grid" aria-label={monthName} ref={grid}>
        <div role="row" className="calendar-row">
          {weekdayNames.map((n, i) => (
            <span key={i} role="columnheader" className="calendar-weekday">{n}</span>
          ))}
        </div>
        {Array.from({ length: 6 }, (_, w) => (
          <div role="row" className="calendar-row" key={w}>
            {days.slice(w * 7, w * 7 + 7).map((d) => {
              const key = iso(d);
              const inMonth = d.getUTCMonth() === cursor.getUTCMonth();
              const current = key === iso(cursor);
              const inRange = rangeFrom && rangeTo && key >= rangeFrom && key <= rangeTo;
              return (
                <button
                  key={key}
                  type="button"
                  role="gridcell"
                  tabIndex={current ? 0 : -1}
                  disabled={Boolean(out(d))}
                  aria-selected={key === value || key === rangeFrom || key === rangeTo}
                  aria-current={key === iso(today) ? "date" : undefined}
                  aria-label={displayDate(key)}
                  className={`calendar-day${inMonth ? "" : " is-outside"}${key === iso(today) ? " is-today" : ""}${key === value || key === rangeFrom || key === rangeTo ? " is-chosen" : ""}${inRange ? " is-in-range" : ""}`}
                  onClick={() => onPick(key)}
                >
                  {d.getUTCDate()}
                </button>
              );
            })}
          </div>
        ))}
      </div>
      <div className="calendar-foot">
        <button type="button" className="button link" onClick={() => go(today)}>{t("forms.date.today")}</button>
        <span className="calendar-hint">{t("forms.date.keys")}</span>
      </div>
    </div>
  );
}

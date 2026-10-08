import { isDecimalString, scaleOf } from "../format";
import type { Condition, ListColumn, ListDefinition, ListGroup, Row, Value } from "./model";

type Translate = (key: string, params?: Record<string, string | number>) => string;

/** The screen's formatter (kernel/format.ts through useI18n): language and digit choice. */
export type Formatters = {
  t: Translate;
  formatDateTime: (value: string | Date) => string;
  formatDate: (value: string | Date) => string;
  formatNumber: (value: number) => string;
  /** A decimal string at the given scale, never through a binary float. */
  formatDecimal: (value: string, scale?: number) => string;
  /** The label of a reference column's value (an id), when the screen knows it. */
  reference?: (column: string, value: string) => string | undefined;
};

/** A cell value as text in the user's language (dates, numbers, flags, choices). */
export function formatValue(column: ListColumn, value: unknown, f: Formatters): string {
  if (value === null || value === undefined || value === "") return "";
  switch (column.type) {
    case "boolean":
      return value ? f.t(column.trueLabelKey ?? "lists.yes") : f.t(column.falseLabelKey ?? "lists.no");
    case "choice": {
      if (Array.isArray(value)) return f.formatNumber(value.length);
      const choice = column.choices.find((c) => c.value === value);
      return choice ? f.t(choice.labelKey) : String(value);
    }
    case "dateTime":
      return f.formatDateTime(String(value));
    case "date": {
      // A calendar date (yyyy-mm-dd): shown as that local day, whatever the browser's offset.
      const [y, m, d] = String(value).split("-").map(Number);
      return y && m && d ? f.formatDate(new Date(y, m - 1, d)) : String(value);
    }
    case "number":
      return f.formatNumber(Number(value));
    case "money": {
      const text = String(value).trim();
      return isDecimalString(text) ? f.formatDecimal(text, Math.min(6, Math.max(2, scaleOf(text)))) : text;
    }
    case "reference":
      return f.reference?.(column.key, String(value)) ?? String(value);
    default:
      return String(value);
  }
}

/**
 * A group's total of a totalled column: a number column's sum, or a money column's sum in each
 * currency its rows hold ("AED 1,250.00 · USD 40.00"), never amounts of different currencies added.
 */
export function groupTotal(column: ListColumn, group: ListGroup, f: Formatters): string {
  if (column.type !== "money") return formatValue(column, group.totals?.[column.key] ?? "0", f);
  const lines = group.moneyTotals?.[column.key] ?? [];
  if (lines.length === 0) return formatValue(column, "0", f);
  return lines
    .map((line) => (line.currency ? `${line.currency}\u00A0${formatValue(column, line.amount, f)}` : formatValue(column, line.amount, f)))
    .join(" \u00B7 ");
}

export function columnLabel(definition: ListDefinition, key: string, t: Translate): string {
  const column = definition.columns.find((c) => c.key === key);
  return column ? t(column.labelKey) : key;
}

/** A filter condition as a short sentence for its chip ("Language is one of Arabic, English"). */
export function conditionLabel(definition: ListDefinition, condition: Condition, f: Formatters): string {
  const column = definition.columns.find((c) => c.key === condition.column);
  const name = column ? f.t(column.labelKey) : condition.column;
  const show = (v: Value) => (column ? formatValue(column, v, f) : String(v));
  if (condition.op === "isNull" || condition.op === "isNotNull") return `${name} ${f.t(`lists.op.${condition.op}`)}`;
  return `${name} ${f.t(`lists.op.${condition.op}`)} ${condition.values.map(show).join(", ")}`;
}

/** The text of a row's cell for the clipboard. */
export function cellText(definition: ListDefinition, row: Row, key: string, f: Formatters): string {
  const column = definition.columns.find((c) => c.key === key);
  return column ? formatValue(column, row[key], f) : String(row[key] ?? "");
}

/** Start of a local calendar day as an ISO instant with the browser's offset. */
export function localDayStart(date: string): string {
  const [y, m, d] = date.split("-").map(Number);
  const local = new Date(y!, (m ?? 1) - 1, d ?? 1, 0, 0, 0, 0);
  const offset = -local.getTimezoneOffset();
  const sign = offset >= 0 ? "+" : "-";
  const pad = (n: number) => String(Math.trunc(Math.abs(n))).padStart(2, "0");
  return `${date}T00:00:00${sign}${pad(offset / 60)}:${pad(offset % 60)}`;
}

/** The day after a yyyy-mm-dd date. */
export function nextDay(date: string): string {
  const [y, m, d] = date.split("-").map(Number);
  const next = new Date(Date.UTC(y!, (m ?? 1) - 1, (d ?? 1) + 1));
  return next.toISOString().slice(0, 10);
}

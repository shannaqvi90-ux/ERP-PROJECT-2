import type { Language, Numerals } from "../../kernel/i18n";

/** Shapes of the reports API (see /api/openapi/v1.json). */
export type ReportChoice = { value: string; label: string };

export type ReportParameter = {
  key: string;
  label: string;
  type: "text" | "date" | "boolean" | "choice" | "reference";
  required: boolean;
  choices: ReportChoice[];
  lookup: string | null;
  lookupEndpoint: string | null;
  lookupLabels: string[];
};

export type ReportSummary = {
  key: string;
  module: string;
  title: string;
  description: string | null;
  path: string;
  parameters: ReportParameter[];
  columns: { key: string; label: string; type: string; total: boolean; groupable: boolean }[];
  defaultGroupBy: string | null;
  isDocument: boolean;
};

export type ReportCatalog = { items: ReportSummary[]; lists: { key: string; title: string; path: string }[] };

export type ReportCell = { value: unknown; text: string };

export type ReportDocument = {
  key: string;
  title: string;
  subject: string | null;
  issuer: string;
  language: Language;
  direction: "rtl" | "ltr";
  numerals: Numerals;
  parameters: { label: string; text: string }[];
  facts: { label: string; text: string }[];
  columns: { key: string; label: string; type: string; align: "start" | "end"; total: boolean }[];
  groupBy: string | null;
  groupLabel: string | null;
  groups: { label: string | null; count: number; countText: string; rows: { cells: ReportCell[] }[]; totals: (ReportCell | null)[] }[];
  totals: (ReportCell | null)[];
  rowCount: number;
  matchCount: number;
  rowCountText: string;
  truncated: boolean;
  printedAt: string;
  printedAtText: string;
  printedBy: string;
  texts: { total: string; printed: string; page: string; empty: string; noValue: string };
};

/** The address that runs a report with these values in a format and language. A grouping of "-"
 * means none (the report's default grouping is left out). */
export function reportUrl(report: Pick<ReportSummary, "path" | "parameters">, values: Record<string, string>, format: "json" | "pdf" | "csv" | "xlsx", language: string, numerals: string, defaultGroupBy: string | null): string {
  const query = new URLSearchParams();
  for (const parameter of report.parameters) {
    const value = values[parameter.key];
    if (value) query.set(parameter.key, value);
  }
  const group = values.groupBy;
  if (group === "-") query.set("groupBy", "");
  else if (group) query.set("groupBy", group);
  else if (defaultGroupBy) query.set("groupBy", defaultGroupBy);
  query.set("format", format);
  query.set("language", language);
  query.set("numerals", language === "ar" ? numerals : "latn");
  return `${report.path}?${query}`;
}

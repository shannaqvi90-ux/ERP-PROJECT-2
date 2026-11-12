import { api } from "../../kernel/api";

/** One field of a change, as stored: its name and its value before and after (text, number, flag,
 * list or nothing). A creation has only new values, a deletion only old ones. */
export type FieldChange = { field: string; old?: unknown; new?: unknown };

/** One change of a record (GET /api/audit/records/{id}?type=…). */
export type AuditEvent = {
  id: string;
  occurredAt: string;
  actorId: string | null;
  actorName: string;
  actorNameAr: string | null;
  actorKind: string;
  action: "insert" | "update" | "delete";
  changes: FieldChange[];
};

/** A record's name in English and, when it has one, in Arabic. */
export type RecordName = { name: string; nameAr: string | null };

/** A custom field as the trail names it (the tenant's own words, in both languages) and, for a number, its decimals. */
export type FieldLabel = { labelEn: string; labelAr: string; type: string; decimals: number };

export type AuditHistory = { recordType: string | null; items: AuditEvent[]; next: string | null; names: Record<string, RecordName>; fields?: Record<string, FieldLabel> };

/** One page of a record's history, newest first; `before` continues after the last change shown. */
export function readHistory(type: string, id: string, options: { take?: number; before?: string | null; signal?: AbortSignal } = {}): Promise<AuditHistory> {
  const query = new URLSearchParams({ type });
  if (options.take) query.set("take", String(options.take));
  if (options.before) query.set("before", options.before);
  return api<AuditHistory>("GET", `/api/audit/records/${encodeURIComponent(id)}?${query}`, undefined, { signal: options.signal });
}

/** The address of the audit log narrowed to one record's changes. */
export function logAddress(id: string): string {
  return `/audit/log?${new URLSearchParams({ filter: `recordId eq '${id}'` })}`;
}

/** The address of the audit log narrowed to one person's changes. */
export function personAddress(actorId: string): string {
  return `/audit/log?${new URLSearchParams({ filter: `actorId eq '${actorId}'` })}`;
}

/** The value the trigger writes in place of a secret. */
export const redacted = "[redacted]";

const isoInstant = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$/;
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export type ValueFormat = {
  t: (key: string, params?: Record<string, string | number>) => string;
  formatDateTime: (value: string) => string;
  formatNumber: (value: number) => string;
  /** A decimal string at the given scale (never through a binary float). */
  formatDecimal?: (value: string, scale?: number) => string;
  language: string;
  names?: Record<string, RecordName>;
  /** The custom fields the changes mention, by stored name (custom_fields.key). */
  fields?: Record<string, FieldLabel>;
  /** The kind of record the values belong to (schema.table): a choice's option is named by audit.choice.<kind>.<field>.<value>. */
  recordType?: string | null;
};

/** A stored value as the screen shows it: a flag as Yes or No, a list joined, nothing as
 * "(empty)", a secret as "(hidden)", a time in the user's format, a record's id by its name. */
export function valueText(value: unknown, f: ValueFormat, decimals?: number, field?: string): string {
  // A choice shows the chosen option's label (the module registers it as audit.choice.<kind>.<field>.<value>),
  // never its code.
  if (field && f.recordType && (typeof value === "string" || typeof value === "number") && value !== "") {
    const key = `audit.choice.${f.recordType}.${field}.${value}`;
    const text = f.t(key);
    if (text !== key) return text;
  }
  if (value === null || value === undefined || value === "") return f.t("audit.value.empty");
  if (value === true) return f.t("audit.value.yes");
  if (value === false) return f.t("audit.value.no");
  if (typeof value === "number") {
    // A number keeps its fraction: the decimals a custom field was declared with, else those it has
    // (25000.5 is not 25,001).
    if (Number.isInteger(value) && decimals === undefined) return f.formatNumber(value);
    const text = String(value);
    return f.formatDecimal && /^-?\d+(\.\d+)?$/.test(text) ? f.formatDecimal(text, decimals) : f.formatNumber(value);
  }
  if (Array.isArray(value)) return value.length === 0 ? f.t("audit.value.empty") : value.map((v) => valueText(v, f, decimals)).join(", ");
  if (typeof value === "string") {
    if (value === redacted) return f.t("audit.value.hidden");
    if (isoInstant.test(value)) return f.formatDateTime(value);
    const name = uuid.test(value) ? f.names?.[value.toLowerCase()] ?? f.names?.[value] : undefined;
    if (name) return f.language === "ar" && name.nameAr ? name.nameAr : name.name;
    return value;
  }
  return JSON.stringify(value);
}

/** A field's name in the screen's language; a custom field or one without a name shows as stored. */
export function fieldName(field: string, t: (key: string) => string): string {
  const key = `audit.field.${field}`;
  const text = t(key);
  return text === key ? field : text;
}

/** A field's name for the person reading in `language`: a custom field by the tenant's own label in
 * that language, any other by the product's name for it. */
export function fieldLabel(field: string, f: Pick<ValueFormat, "t" | "language" | "fields">): string {
  const custom = f.fields?.[field];
  if (custom) return (f.language === "ar" ? custom.labelAr || custom.labelEn : custom.labelEn || custom.labelAr) || field;
  return fieldName(field, f.t);
}

/** The decimals of a custom number field a change mentions, or undefined. */
export function fieldDecimals(field: string, f: Pick<ValueFormat, "fields">): number | undefined {
  const custom = f.fields?.[field];
  return custom?.type === "number" ? custom.decimals : undefined;
}

/** The person who made a change, in the screen's language. */
export function actorText(event: Pick<AuditEvent, "actorName" | "actorNameAr">, language: string): string {
  return language === "ar" && event.actorNameAr ? event.actorNameAr : event.actorName;
}

/** The fields a change shows (the record's own id is the record, not one of its fields). */
export function shownChanges(event: AuditEvent): FieldChange[] {
  return event.changes.filter((c) => c.field !== "id");
}

import { useI18n } from "../i18n";
import { DateField, DecimalField, LookupField, SelectField, TextField } from "./fields";
import { FormSection } from "./RecordForm";
import type { FieldBinding } from "./useRecordForm";

/**
 * The tenant's custom fields in a record's form (p09). A module whose records have custom fields
 * (contacts) keeps their values in its draft as plain text under <c>customFields.&lt;key&gt;</c>
 * (<see cref="customDraft"/>), shows them with <see cref="CustomFieldsSection"/> and sends them with
 * the record (<see cref="customRequest"/>). The fields come with the record (and with the new-record
 * defaults) from the API: src/Kernel/Erp.Kernel/CustomFields.
 */
export type CustomFieldType = "text" | "number" | "date" | "boolean" | "select" | "lookup";

export type CustomFieldEntry = {
  key: string;
  type: CustomFieldType;
  labelEn: string;
  labelAr: string;
  required: boolean;
  decimals: number;
  options: { value: string; labelEn: string; labelAr: string }[];
  lookupTarget: string | null;
  lookupEndpoint: string | null;
  lookupLabelProperty: string | null;
  lookupLabelPropertyAr: string | null;
  value: string | boolean | null;
  valueLabel: string | null;
  valueLabelAr: string | null;
  searchable: boolean;
};

/** The draft key of a field's value. */
export const customKey = (key: string) => `customFields.${key}` as const;

/** The values a form starts with (the record's, or the defaults of a new record), as text. */
export function customDraft(entries: readonly CustomFieldEntry[] | null | undefined): Record<`customFields.${string}`, string> {
  const draft: Record<`customFields.${string}`, string> = {};
  for (const entry of entries ?? []) {
    draft[customKey(entry.key)] = entry.value === null || entry.value === undefined ? "" : String(entry.value);
  }
  return draft;
}

/** The values the API is sent for a draft: every field of the record, empty text as null. */
export function customRequest(entries: readonly CustomFieldEntry[] | null | undefined, draft: Record<string, unknown>): Record<string, string | boolean | null> | undefined {
  if (!entries || entries.length === 0) return undefined;
  const values: Record<string, string | boolean | null> = {};
  for (const entry of entries) {
    const text = String(draft[customKey(entry.key)] ?? "").trim();
    values[entry.key] = text === "" ? null : entry.type === "boolean" ? text === "true" : text;
  }
  return values;
}

/** The label of a field in the screen's language (the other language's when this one has none). */
export const customLabel = (entry: Pick<CustomFieldEntry, "labelEn" | "labelAr">, language: string) =>
  (language === "ar" ? entry.labelAr || entry.labelEn : entry.labelEn || entry.labelAr);

/** The label of a select's option in the screen's language. */
export const optionLabel = (option: { labelEn: string; labelAr: string }, language: string) => (language === "ar" ? option.labelAr || option.labelEn : option.labelEn || option.labelAr);

/**
 * The custom fields of a record as one section of its form, in the workspace's order: text, number,
 * date, yes/no (a choice of Yes, No or none, so a value can be cleared), single select and lookup.
 * Renders nothing when the record has none.
 */
export function CustomFieldsSection({ entries, bind }: { entries: readonly CustomFieldEntry[] | null | undefined; bind: (key: string) => FieldBinding<string> }) {
  const { t, language } = useI18n();
  if (!entries || entries.length === 0) return null;
  return (
    <FormSection title={t("forms.customFields.title")}>
      {entries.map((entry) => {
        const field = bind(customKey(entry.key));
        const label = customLabel(entry, language) + (entry.required ? " *" : "");
        switch (entry.type) {
          case "number":
            return <DecimalField key={entry.key} field={field} label={label} scale={entry.decimals} />;
          case "date":
            return <DateField key={entry.key} field={field} label={label} />;
          case "boolean":
            return (
              <SelectField
                key={entry.key}
                field={field}
                label={label}
                empty={entry.required ? undefined : t("forms.customFields.none")}
                options={[
                  { value: "true", label: t("forms.customFields.yes") },
                  { value: "false", label: t("forms.customFields.no") },
                ]}
              />
            );
          case "select":
            return (
              <SelectField
                key={entry.key}
                field={field}
                label={label}
                empty={entry.required ? undefined : t("forms.customFields.none")}
                options={entry.options.map((o) => ({ value: o.value, label: optionLabel(o, language) }))}
              />
            );
          case "lookup": {
            const property = (language === "ar" ? entry.lookupLabelPropertyAr : entry.lookupLabelProperty) ?? entry.lookupLabelProperty ?? "id";
            const other = (language === "ar" ? entry.lookupLabelProperty : entry.lookupLabelPropertyAr) ?? property;
            return (
              <LookupField
                key={entry.key}
                field={field}
                label={label}
                endpoint={entry.lookupEndpoint ?? ""}
                labelOf={(row) => String(row[property] || row[other] || row.id)}
                initialLabel={(language === "ar" ? entry.valueLabelAr || entry.valueLabel : entry.valueLabel || entry.valueLabelAr) ?? undefined}
              />
            );
          }
          default:
            return <TextField key={entry.key} field={field} label={label} maxLength={500} />;
        }
      })}
    </FormSection>
  );
}

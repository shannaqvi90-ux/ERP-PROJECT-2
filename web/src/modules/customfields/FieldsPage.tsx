import { useCallback, useEffect, useState } from "react";
import { api } from "../../kernel/api";
import { BooleanField, DateField, DecimalField, SelectField, TextAreaField, TextField } from "../../kernel/forms/fields";
import { FormSection, RecordForm, type RecordNavigation } from "../../kernel/forms/RecordForm";
import { newRecord, useRecordPanel } from "../../kernel/forms/recordPanel";
import { useRecordForm, type FieldBinding, type FormErrors } from "../../kernel/forms/useRecordForm";
import { useI18n } from "../../kernel/i18n";
import { ListView } from "../../kernel/lists/ListView";
import { useSession } from "../../kernel/session";
import { useShortcut } from "../../kernel/shortcuts";
import { optionsText, parseOptions } from "./model";
import { fieldsChanged, fieldTypes, type CustomField, type ListPage, type RecordType } from "./types";
import type { CustomFieldType } from "../../kernel/forms/customFields";

/**
 * Custom fields: the fields the workspace added to its records (contacts for now), per kind of
 * record in display order, on the list framework with the open field's form in the list's panel
 * (<screen>/<id>, a new one at <screen>/new). A new field appears at once in its record's form,
 * list (column chooser, filters, search) and API. Alt+N starts a new field; Enter saves.
 */
export function FieldsPage() {
  const { t, language } = useI18n();
  const { can } = useSession();
  const panel = useRecordPanel(can("customfields.fields.manage"));
  const { refresh, startNew } = panel;
  useEffect(() => {
    window.addEventListener(fieldsChanged, refresh);
    return () => window.removeEventListener(fieldsChanged, refresh);
  }, [refresh]);
  useShortcut({
    id: "customfields.screen.new",
    chord: "Alt+KeyN",
    labelKey: "customfields.field.new",
    keywordsKey: "customfields.action.newKeywords",
    groupKey: "customfields.shortcut.group",
    enabled: Boolean(startNew),
    run: () => startNew?.(),
  });
  return (
    <div className="cf-list">
      <ListView
        listKey="customfields.fields"
        titleKey="customfields.fields.title"
        countKey="customfields.fields.count"
        searchPlaceholderKey="customfields.fields.search"
        can={can}
        openOnClick
        reloadKey={panel.reload}
        openId={panel.openId}
        onOpenIdChange={panel.onOpenIdChange}
        renderRecord={(id, close, nav) => <FieldForm key={panel.formKey} id={id === newRecord ? null : id} nav={nav} onSaved={panel.saved} onClose={close} />}
        actions={
          startNew && (
            <button type="button" className="button primary" onClick={startNew} aria-keyshortcuts="Alt+N">
              {t("customfields.field.new")}
            </button>
          )
        }
        renderCell={{
          labelEn: (row) => <span dir="auto">{language === "ar" ? String(row.labelAr || row.labelEn || "") : String(row.labelEn || row.labelAr || "")}</span>,
          labelAr: (row) => <span dir="rtl">{String(row.labelAr ?? "")}</span>,
          key: (row) => <code dir="ltr">{String(row.key ?? "")}</code>,
        }}
      />
    </div>
  );
}

type Draft = {
  recordType: string;
  type: CustomFieldType;
  labelEn: string;
  labelAr: string;
  required: boolean;
  defaultValue: string;
  decimals: string;
  options: string;
  lookupTarget: string;
  searchable: boolean;
  sortOrder: string;
  isActive: boolean;
};

const draftOf = (f: CustomField | null, first: string): Draft => ({
  recordType: f?.recordType ?? first,
  type: f?.type ?? "text",
  labelEn: f?.labelEn ?? "",
  labelAr: f?.labelAr ?? "",
  required: f?.required ?? false,
  // A select's default is kept as its option's English label while editing.
  defaultValue: f ? (f.type === "select" ? (f.options.find((o) => o.value === f.default)?.labelEn ?? "") : (f.default ?? "")) : "",
  decimals: String(f?.decimals ?? 2),
  options: optionsText(f?.options ?? []),
  lookupTarget: f?.lookupTarget ?? "",
  searchable: f?.searchable ?? false,
  sortOrder: f ? String(f.sortOrder) : "",
  isActive: f?.isActive ?? true,
});

/** What the API is sent for a draft (src/Modules/CustomFields SaveFieldRequest). */
export function requestOf(draft: Draft, field: CustomField | null) {
  const select = draft.type === "select";
  const parsed = select ? parseOptions(draft.options, field?.options ?? []) : null;
  const chosen = parsed?.options.find((o) => o.labelEn.toLowerCase() === draft.defaultValue.trim().toLowerCase());
  const text = draft.defaultValue.trim();
  return {
    recordType: draft.recordType,
    type: draft.type,
    labelEn: draft.labelEn.trim(),
    labelAr: draft.labelAr.trim(),
    required: draft.required,
    default: draft.type === "lookup" || text === "" ? null : select ? (chosen?.value ?? null) : text,
    decimals: draft.type === "number" ? Number.parseInt(draft.decimals, 10) : null,
    options: parsed ? parsed.options : null,
    lookupTarget: draft.type === "lookup" ? draft.lookupTarget : null,
    searchable: draft.type === "text" ? draft.searchable : false,
    sortOrder: draft.sortOrder.trim() === "" ? null : Number.parseInt(draft.sortOrder, 10),
    isActive: draft.isActive,
    version: field?.version ?? null,
  };
}

function FieldForm({ id, nav, onSaved, onClose }: { id: string | null; nav?: RecordNavigation; onSaved: (id: string) => void; onClose: () => void }) {
  const { t, language } = useI18n();
  const { can } = useSession();
  const [types, setTypes] = useState<RecordType[]>([]);
  useEffect(() => {
    const controller = new AbortController();
    api<RecordType[]>("GET", "/api/customfields/record-types", undefined, { signal: controller.signal }).then(setTypes, () => undefined);
    return () => controller.abort();
  }, []);
  const first = types[0]?.table ?? "contacts.contacts";
  const validate = useCallback(
    (draft: Draft): FormErrors => {
      const errors: FormErrors = {};
      if (draft.type === "select") {
        const { options, bad } = parseOptions(draft.options, []);
        if (options.length === 0 || options.length > 50 || bad) errors.options = [t("customfields.validation.options")];
      }
      if (draft.type === "number" && !/^[0-8]$/.test(draft.decimals.trim())) errors.decimals = [t("customfields.validation.decimals")];
      if (draft.type === "lookup" && draft.lookupTarget === "") errors.lookupTarget = [t("customfields.validation.lookupTarget")];
      return errors;
    },
    [t],
  );
  const form = useRecordForm<CustomField, Draft>(
    {
      load: id === null ? undefined : (signal) => api<CustomField>("GET", `/api/customfields/fields/${id}`, undefined, { signal }),
      initial: (f) => draftOf(f, first),
      canEdit: can("customfields.fields.manage"),
      validate,
      save: (draft, field) =>
        field === null
          ? api<CustomField>("POST", "/api/customfields/fields", requestOf(draft, null))
          : api<CustomField>("PUT", `/api/customfields/fields/${field.id}`, requestOf(draft, field)),
      onSaved: (saved) => {
        onSaved(saved.id);
        window.dispatchEvent(new Event(fieldsChanged));
      },
    },
    id,
  );
  const field = form.record;
  const bind = form.bind;
  const draft = form.draft;
  const isNew = field === null;
  // A new field's kind of record follows the first registered one until the user chooses.
  useEffect(() => {
    if (isNew && draft.recordType === "" && types[0]) form.set("recordType")(types[0].table);
    // Once the kinds arrive.
  }, [types]);
  const selectOptions = parseOptions(draft.options, []).options.filter((o) => o.labelEn !== "");
  const readOnlyReason = field && !field.everyCompany ? t("customfields.field.needsEveryCompany") : undefined;
  return (
    <RecordForm
      form={form}
      record={{ type: "customfields.definitions", id }}
      label={t("customfields.field.form")}
      title={field ? (language === "ar" ? field.labelAr || field.labelEn : field.labelEn || field.labelAr) : t("customfields.field.new")}
      subtitle={field ? <span className="muted">{[t(`customfields.type.${field.type}`), field.key, field.isActive ? null : t("customfields.status.inactive")].filter(Boolean).join(" · ")}</span> : undefined}
      onClose={onClose}
      nav={nav}
      readOnlyReason={readOnlyReason}
    >
      <FormSection title={t("customfields.section.field")}>
        <TextField field={bind("labelEn")} label={t("customfields.field.label")} dir="ltr" maxLength={100} autoFocus={isNew} hint={t("customfields.field.labelHint")} />
        <TextField field={bind("labelAr")} label={t("customfields.field.labelAr")} dir="rtl" maxLength={100} />
        <SelectField
          field={bind("recordType") as FieldBinding<string>}
          label={t("customfields.field.recordType")}
          disabled={!isNew}
          options={types.map((r) => ({ value: r.table, label: t(r.labelKey) }))}
        />
        <SelectField
          field={bind("type") as FieldBinding<CustomFieldType | "">}
          label={t("customfields.field.type")}
          disabled={!isNew}
          hint={isNew ? t("customfields.field.typeHint") : t("customfields.field.typeFixed")}
          options={fieldTypes.map((k) => ({ value: k, label: t(`customfields.type.${k}`) }))}
        />
        {draft.type === "number" && <TextField field={bind("decimals")} label={t("customfields.field.decimals")} dir="ltr" inputMode="numeric" maxLength={1} />}
        {draft.type === "lookup" && (
          <SelectField
            field={bind("lookupTarget") as FieldBinding<string>}
            label={t("customfields.field.lookupTarget")}
            empty={t("customfields.field.chooseKind")}
            disabled={!isNew}
            options={types.filter((r) => r.canBePointedAt).map((r) => ({ value: r.table, label: t(r.labelKey) }))}
          />
        )}
        {draft.type === "select" && <TextAreaField field={bind("options")} label={t("customfields.field.options")} rows={5} wide hint={t("customfields.field.optionsHint")} />}
      </FormSection>
      <FormSection title={t("customfields.section.behaviour")}>
        <BooleanField field={bind("required")} label={t("customfields.field.requiredLabel")} />
        {draft.type === "text" && <BooleanField field={bind("searchable")} label={t("customfields.field.searchableLabel")} />}
        {draft.type === "text" && <TextField field={bind("defaultValue")} label={t("customfields.field.default")} maxLength={500} />}
        {draft.type === "number" && <DecimalField field={bind("defaultValue")} label={t("customfields.field.default")} scale={Number.parseInt(draft.decimals, 10) || 0} />}
        {draft.type === "date" && <DateField field={bind("defaultValue")} label={t("customfields.field.default")} />}
        {draft.type === "boolean" && (
          <SelectField
            field={bind("defaultValue") as FieldBinding<string>}
            label={t("customfields.field.default")}
            empty={t("forms.customFields.none")}
            options={[
              { value: "true", label: t("forms.customFields.yes") },
              { value: "false", label: t("forms.customFields.no") },
            ]}
          />
        )}
        {draft.type === "select" && (
          <SelectField
            field={bind("defaultValue") as FieldBinding<string>}
            label={t("customfields.field.default")}
            empty={t("forms.customFields.none")}
            options={selectOptions.map((o) => ({ value: o.labelEn, label: o.labelEn }))}
          />
        )}
        <TextField field={bind("sortOrder")} label={t("customfields.field.sortOrder")} dir="ltr" inputMode="numeric" maxLength={5} hint={t("customfields.field.sortOrderHint")} />
        {!isNew && <BooleanField field={bind("isActive")} label={t("customfields.field.activeLabel")} />}
      </FormSection>
    </RecordForm>
  );
}

export type { Draft as FieldDraft };
export type { ListPage };

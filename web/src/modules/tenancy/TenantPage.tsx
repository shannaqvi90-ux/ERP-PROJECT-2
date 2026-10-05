import { api } from "../../kernel/api";
import { SelectField, TextField } from "../../kernel/forms/fields";
import { FormSection, RecordForm } from "../../kernel/forms/RecordForm";
import { useRecordForm } from "../../kernel/forms/useRecordForm";
import { useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";

type Tenant = {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  status: string;
  defaultLanguage: "en" | "ar";
  timeZone: string;
  weekStart: "monday" | "sunday" | "saturday";
  timeZones: string[];
  version: number;
};

type Draft = { nameEn: string; nameAr: string; defaultLanguage: "en" | "ar" | ""; timeZone: string; weekStart: "monday" | "sunday" | "saturday" | "" };

/** The workspace: its names and settings (default language, time zone, first day of the week). */
export function TenantPage() {
  const { t } = useI18n();
  const { can, refresh } = useSession();
  const editable = can("tenancy.tenant.update");
  const form = useRecordForm<Tenant, Draft>({
    load: (signal) => api<Tenant>("GET", "/api/tenancy/tenant", undefined, { signal }),
    initial: (tenant) => ({
      nameEn: tenant?.nameEn ?? "",
      nameAr: tenant?.nameAr ?? "",
      defaultLanguage: tenant?.defaultLanguage ?? "en",
      timeZone: tenant?.timeZone ?? "",
      weekStart: tenant?.weekStart ?? "monday",
    }),
    canEdit: editable,
    save: (draft, tenant) =>
      api<Tenant>("PUT", "/api/tenancy/tenant", { ...draft, version: tenant?.version ?? null }),
    onSaved: () => void refresh(),
  });
  const tenant = form.record;
  const bind = form.bind;

  return (
    <section>
      <div className="screen-header">
        <h1>{t("tenancy.tenant.title")}</h1>
      </div>
      {tenant && (
        <dl className="facts">
          <dt>{t("tenancy.tenant.code")}</dt>
          <dd dir="ltr">{tenant.code}</dd>
          <dt>{t("tenancy.tenant.nameEn")}</dt>
          <dd dir="ltr" lang="en">
            {tenant.nameEn}
          </dd>
          <dt>{t("tenancy.tenant.nameAr")}</dt>
          <dd dir="rtl" lang="ar">
            {tenant.nameAr}
          </dd>
          <dt>{t("tenancy.tenant.status")}</dt>
          <dd>{t(`tenancy.status.${tenant.status}`)}</dd>
          <dt>{t("tenancy.tenant.defaultLanguage")}</dt>
          <dd>{t(`tenancy.language.${tenant.defaultLanguage}`)}</dd>
          <dt>{t("tenancy.tenant.timeZone")}</dt>
          <dd dir="ltr">{tenant.timeZone}</dd>
          <dt>{t("tenancy.tenant.weekStart")}</dt>
          <dd>{t(`tenancy.weekday.${tenant.weekStart}`)}</dd>
        </dl>
      )}
      {editable && tenant && (
        <RecordForm form={form} title={t("tenancy.tenant.settings")} label={t("tenancy.tenant.settings")} narrow>
          <FormSection>
            <TextField field={bind("nameEn")} label={t("tenancy.tenant.nameEn")} dir="ltr" maxLength={200} required />
            <TextField field={bind("nameAr")} label={t("tenancy.tenant.nameAr")} dir="rtl" maxLength={200} required />
            <SelectField
              field={bind("defaultLanguage")}
              label={t("tenancy.tenant.defaultLanguage")}
              options={[
                { value: "en" as const, label: t("tenancy.language.en") },
                { value: "ar" as const, label: t("tenancy.language.ar") },
              ]}
            />
            <SelectField field={bind("timeZone")} label={t("tenancy.tenant.timeZone")} options={tenant.timeZones.map((z) => ({ value: z, label: z }))} />
            <SelectField
              field={bind("weekStart")}
              label={t("tenancy.tenant.weekStart")}
              options={(["monday", "sunday", "saturday"] as const).map((d) => ({ value: d, label: t(`tenancy.weekday.${d}`) }))}
            />
          </FormSection>
        </RecordForm>
      )}
      {!tenant && form.message && (
        <div className="alert" role="alert">
          {form.message}
        </div>
      )}
    </section>
  );
}

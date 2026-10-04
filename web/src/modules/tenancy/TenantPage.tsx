import { useEffect, useState, type FormEvent } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";
import { problemOf, SelectField, TextField, useScreenKeys, type FieldErrors } from "./ui";

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

/** The workspace: its names and settings (default language, time zone, first day of the week). */
export function TenantPage() {
  const { t } = useI18n();
  const { can, refresh } = useSession();
  const [tenant, setTenant] = useState<Tenant | null>(null);
  const [draft, setDraft] = useState<Tenant | null>(null);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [message, setMessage] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);
  const editable = can("tenancy.tenant.update");

  useEffect(() => {
    api<Tenant>("GET", "/api/tenancy/tenant")
      .then((value) => {
        setTenant(value);
        setDraft(value);
      })
      .catch((e: Error) => setMessage(e.message));
  }, []);

  const set = <K extends keyof Tenant>(key: K) => (value: Tenant[K]) => {
    setDraft((d) => (d ? { ...d, [key]: value } : d));
    setSaved(false);
  };

  const save = async (event?: FormEvent) => {
    event?.preventDefault();
    if (!draft || !editable || busy) return;
    setBusy(true);
    setMessage(null);
    try {
      const result = await api<Tenant>("PUT", "/api/tenancy/tenant", {
        nameEn: draft.nameEn,
        nameAr: draft.nameAr,
        defaultLanguage: draft.defaultLanguage,
        timeZone: draft.timeZone,
        weekStart: draft.weekStart,
        version: draft.version,
      });
      setTenant(result);
      setDraft(result);
      setErrors({});
      setSaved(true);
      await refresh();
    } catch (error) {
      const problem = problemOf(error);
      setErrors(problem.fields);
      setMessage(problem.message);
    } finally {
      setBusy(false);
    }
  };

  useScreenKeys({ onSave: editable ? () => void save() : undefined });

  return (
    <section>
      <div className="screen-header">
        <h1>{t("tenancy.tenant.title")}</h1>
      </div>
      {message && (
        <div className="alert" role="alert">
          {message}
        </div>
      )}
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
      {draft && editable && (
        <form className="record-form narrow" onSubmit={save} noValidate aria-label={t("tenancy.tenant.settings")}>
          <div className="record-header">
            <h2>{t("tenancy.tenant.settings")}</h2>
            <div className="record-actions">
              <button type="submit" className="button primary" disabled={busy} title={t("tenancy.common.saveHint")} aria-keyshortcuts="Control+S Control+Enter">
                {busy ? t("tenancy.common.saving") : t("tenancy.common.save")}
              </button>
            </div>
          </div>
          {saved && (
            <div className="notice" role="status">
              {t("tenancy.common.saved")}
            </div>
          )}
          <div className="form-grid">
            <TextField name="nameEn" label={t("tenancy.tenant.nameEn")} value={draft.nameEn} onChange={set("nameEn")} errors={errors} dir="ltr" maxLength={200} required />
            <TextField name="nameAr" label={t("tenancy.tenant.nameAr")} value={draft.nameAr} onChange={set("nameAr")} errors={errors} dir="rtl" maxLength={200} required />
            <SelectField
              name="defaultLanguage"
              label={t("tenancy.tenant.defaultLanguage")}
              value={draft.defaultLanguage}
              options={[
                { value: "en" as const, label: t("tenancy.language.en") },
                { value: "ar" as const, label: t("tenancy.language.ar") },
              ]}
              onChange={(v) => v && set("defaultLanguage")(v)}
              errors={errors}
            />
            <SelectField
              name="timeZone"
              label={t("tenancy.tenant.timeZone")}
              value={draft.timeZone}
              options={draft.timeZones.map((z) => ({ value: z, label: z }))}
              onChange={(v) => v && set("timeZone")(v)}
              errors={errors}
            />
            <SelectField
              name="weekStart"
              label={t("tenancy.tenant.weekStart")}
              value={draft.weekStart}
              options={(["monday", "sunday", "saturday"] as const).map((d) => ({ value: d, label: t(`tenancy.weekday.${d}`) }))}
              onChange={(v) => v && set("weekStart")(v)}
              errors={errors}
            />
          </div>
        </form>
      )}
    </section>
  );
}

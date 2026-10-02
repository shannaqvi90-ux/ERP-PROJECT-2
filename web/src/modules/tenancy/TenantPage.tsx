import { useEffect, useState } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";

type Tenant = { id: string; code: string; nameEn: string; nameAr: string; status: string };

/** Workspace details (p02 adds companies, branches and editing). */
export function TenantPage() {
  const { t } = useI18n();
  const [tenant, setTenant] = useState<Tenant | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api<Tenant>("GET", "/api/tenancy/tenant")
      .then(setTenant)
      .catch((e: Error) => setError(e.message));
  }, []);

  return (
    <section>
      <div className="screen-header">
        <h1>{t("tenancy.tenant.title")}</h1>
      </div>
      {error && (
        <div className="alert" role="alert">
          {error}
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
        </dl>
      )}
    </section>
  );
}

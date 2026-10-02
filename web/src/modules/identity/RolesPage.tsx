import { useEffect, useState } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";

type Role = { id: string; nameEn: string; nameAr: string; permissions: string[]; isSystem: boolean; userCount: number };

/** Read-only role list (p03 adds the permission matrix and editing). */
export function RolesPage() {
  const { t, language, formatNumber } = useI18n();
  const [roles, setRoles] = useState<Role[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api<Role[]>("GET", "/api/identity/roles")
      .then(setRoles)
      .catch((e: Error) => setError(e.message));
  }, []);

  return (
    <section>
      <div className="screen-header">
        <h1>{t("identity.roles.title")}</h1>
      </div>
      {error && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}
      <table className="grid">
        <thead>
          <tr>
            <th scope="col">{t("identity.roles.name")}</th>
            <th scope="col">{t("identity.roles.permissions")}</th>
            <th scope="col">{t("identity.roles.users")}</th>
            <th scope="col">{t("identity.roles.kind")}</th>
          </tr>
        </thead>
        <tbody>
          {roles?.map((r) => (
            <tr key={r.id}>
              <td>{language === "ar" ? r.nameAr : r.nameEn}</td>
              <td>{formatNumber(r.permissions.length)}</td>
              <td>{formatNumber(r.userCount)}</td>
              <td>{r.isSystem ? t("identity.roles.system") : t("identity.roles.custom")}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

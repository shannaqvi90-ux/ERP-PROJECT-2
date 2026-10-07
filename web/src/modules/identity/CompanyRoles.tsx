import { useId, useState } from "react";
import { useI18n } from "../../kernel/i18n";
import { companyName, roleName, type Company, type CompanyRole, type Role } from "./model";

/**
 * Roles held in one company only: a dense table of company and role, one row each, with a line to
 * add another. Only the signed-in user's companies are offered (the server refuses others), and
 * roles granting something the signed-in user does not hold are not offered. Keyboard: the two
 * selects of the add line, then Enter or the Add button; each row's remove button by Tab.
 */
export function CompanyRolesEditor({
  companies,
  roles,
  value,
  onChange,
  canGrant,
  disabled,
  rolesElsewhere,
}: {
  companies: Company[];
  roles: Role[];
  value: CompanyRole[];
  onChange: (next: CompanyRole[]) => void;
  canGrant: (role: Role) => boolean;
  disabled?: boolean;
  rolesElsewhere?: boolean;
}) {
  const { t, language } = useI18n();
  const id = useId();
  const [companyId, setCompanyId] = useState(companies[0]?.id ?? "");
  const grantable = roles.filter(canGrant);
  const [roleId, setRoleId] = useState("");
  const companyOf = new Map(companies.map((c) => [c.id, c]));
  const roleOf = new Map(roles.map((r) => [r.id, r]));
  const chosenRole = roleId || grantable[0]?.id || "";
  const duplicate = value.some((v) => v.companyId === companyId && v.roleId === chosenRole);

  function add() {
    if (disabled || !companyId || !chosenRole || duplicate) return;
    onChange([...value, { companyId, roleId: chosenRole }]);
  }

  const rows = [...value].sort((a, b) =>
    (companyOf.get(a.companyId)?.code ?? a.companyId).localeCompare(companyOf.get(b.companyId)?.code ?? b.companyId) ||
    roleName(roleOf.get(a.roleId) ?? { nameEn: a.roleId, nameAr: a.roleId }, language).localeCompare(roleName(roleOf.get(b.roleId) ?? { nameEn: b.roleId, nameAr: b.roleId }, language)),
  );

  return (
    <fieldset className="id-company-roles" aria-describedby={`${id}-hint`}>
      <legend className="field-label">{t("identity.companyRoles.title")}</legend>
      <p id={`${id}-hint`} className="id-hint">
        {t("identity.companyRoles.hint")}
      </p>
      {rows.length === 0 ? (
        <p className="muted">{t("identity.companyRoles.none")}</p>
      ) : (
        <table className="grid id-company-roles-table">
          <thead>
            <tr>
              <th scope="col">{t("identity.companyRoles.company")}</th>
              <th scope="col">{t("identity.companyRoles.role")}</th>
              <th scope="col">
                <span className="visually-hidden">{t("identity.companyRoles.actions")}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => {
              const company = companyOf.get(row.companyId);
              const role = roleOf.get(row.roleId);
              const label = `${company ? companyName(company, language) : row.companyId} · ${role ? roleName(role, language) : row.roleId}`;
              return (
                <tr key={`${row.companyId}/${row.roleId}`}>
                  <td>{company ? companyName(company, language) : row.companyId}</td>
                  <td>{role ? roleName(role, language) : row.roleId}</td>
                  <td>
                    {!disabled && (!role || canGrant(role)) && (
                      <button
                        type="button"
                        className="button"
                        aria-label={t("identity.companyRoles.remove", { name: label })}
                        onClick={() => onChange(value.filter((v) => !(v.companyId === row.companyId && v.roleId === row.roleId)))}
                      >
                        {t("identity.companyRoles.removeShort")}
                      </button>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
      {rolesElsewhere && <p className="muted">{t("identity.companyRoles.elsewhere")}</p>}
      {!disabled && companies.length > 0 && grantable.length > 0 && (
        <div
          className="id-company-roles-add"
          onKeyDown={(e) => {
            if (e.key === "Enter" && !e.ctrlKey && !e.metaKey) {
              e.preventDefault();
              add();
            }
          }}
        >
          <label className="field">
            <span className="field-label">{t("identity.companyRoles.company")}</span>
            <select name="companyRoleCompany" value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
              {companies.map((c) => (
                <option key={c.id} value={c.id}>
                  {companyName(c, language)}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span className="field-label">{t("identity.companyRoles.role")}</span>
            <select name="companyRoleRole" value={chosenRole} onChange={(e) => setRoleId(e.target.value)}>
              {grantable.map((r) => (
                <option key={r.id} value={r.id}>
                  {roleName(r, language)}
                </option>
              ))}
            </select>
          </label>
          <button type="button" className="button" disabled={duplicate} onClick={add}>
            {t("identity.companyRoles.add")}
          </button>
        </div>
      )}
    </fieldset>
  );
}

/** Where a user starts work: their first company by code, or the one chosen. */
export function DefaultCompanyField({
  companies,
  value,
  onChange,
  disabled,
}: {
  companies: Company[];
  value: string | null;
  onChange: (companyId: string | null) => void;
  disabled?: boolean;
}) {
  const { t, language } = useI18n();
  const id = useId();
  return (
    <label className="field">
      <span className="field-label">{t("identity.defaultCompany.label")}</span>
      <select name="defaultCompany" value={value ?? ""} disabled={disabled} aria-describedby={`${id}-hint`} onChange={(e) => onChange(e.target.value || null)}>
        <option value="">{t("identity.defaultCompany.first")}</option>
        {companies.map((c) => (
          <option key={c.id} value={c.id}>
            {companyName(c, language)}
          </option>
        ))}
      </select>
      <span id={`${id}-hint`} className="id-hint">
        {companies.length === 0 ? t("identity.defaultCompany.noCompanies") : t("identity.defaultCompany.hint")}
      </span>
    </label>
  );
}

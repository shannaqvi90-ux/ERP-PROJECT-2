import { useId, useMemo, useState, type KeyboardEvent } from "react";
import { useI18n } from "../../kernel/i18n";
import { roleName, type Role } from "./model";

/**
 * Roles as a filterable checklist. Keyboard: type in the filter, Enter toggles the first role
 * shown, Tab moves into the list where Space toggles. Roles that grant something the signed-in
 * user does not hold are shown but cannot be given (the server refuses them anyway).
 */
export function RolePicker({
  roles,
  selected,
  onChange,
  canGrant,
  disabled,
}: {
  roles: Role[];
  selected: string[];
  onChange: (ids: string[]) => void;
  canGrant: (role: Role) => boolean;
  disabled?: boolean;
}) {
  const { t, language, formatNumber } = useI18n();
  const [filter, setFilter] = useState("");
  const id = useId();
  const shown = useMemo(() => {
    const words = filter.toLocaleLowerCase().split(/\s+/).filter(Boolean);
    return roles.filter((r) => words.every((w) => `${r.nameEn} ${r.nameAr}`.toLocaleLowerCase().includes(w)));
  }, [roles, filter]);

  function toggle(role: Role) {
    if (disabled || !canGrant(role)) return;
    onChange(selected.includes(role.id) ? selected.filter((x) => x !== role.id) : [...selected, role.id]);
  }

  function onFilterKey(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Enter" && !event.ctrlKey && !event.metaKey) {
      event.preventDefault();
      const first = shown.find(canGrant);
      if (first) toggle(first);
    }
  }

  return (
    <fieldset className="id-roles" disabled={disabled}>
      <legend className="field-label">{t("identity.form.roles")}</legend>
      <input
        type="search"
        className="id-roles-filter"
        value={filter}
        onChange={(e) => setFilter(e.target.value)}
        onKeyDown={onFilterKey}
        placeholder={t("identity.form.findRole")}
        aria-label={t("identity.form.findRole")}
        aria-describedby={`${id}-hint`}
      />
      <span id={`${id}-hint`} className="id-hint">
        {t("identity.form.findRoleHint")}
      </span>
      <ul className="id-roles-list">
        {shown.map((role) => {
          const allowed = canGrant(role);
          return (
            <li key={role.id}>
              <label className={allowed ? undefined : "id-disabled"} title={allowed ? undefined : t("identity.form.roleBeyondOwn")}>
                <input type="checkbox" checked={selected.includes(role.id)} disabled={!allowed} onChange={() => toggle(role)} />
                <span>{roleName(role, language)}</span>
                <span className="muted">{t("identity.roles.permissionCount", { count: role.permissions.length })}</span>
              </label>
            </li>
          );
        })}
        {shown.length === 0 && <li className="muted">{t("identity.form.noRoleMatches")}</li>}
      </ul>
      <span className="muted">{t("identity.form.rolesChosen", { count: selected.length, total: formatNumber(roles.length) })}</span>
    </fieldset>
  );
}

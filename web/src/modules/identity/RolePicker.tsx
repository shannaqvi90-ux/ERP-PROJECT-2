import { useId, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { useI18n } from "../../kernel/i18n";
import { useShortcut } from "../../kernel/shortcuts";
import { roleName, type Role } from "./model";

/** The key that moves to the role filter (O, by its position, for the rOles). */
export const findRoleChord = "Alt+KeyO";

/**
 * Roles as a filterable checklist. Keyboard: Alt+O comes to the filter from anywhere in the form
 * (past the suggested name and the language), type in it, Enter toggles the first role shown, Tab
 * moves into the list where Space toggles. Roles that grant something the signed-in
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
  const filterRef = useRef<HTMLInputElement>(null);
  useShortcut({
    id: "identity.roles.find",
    chord: findRoleChord,
    labelKey: "identity.form.findRole",
    groupKey: "identity.shortcuts.group",
    enabled: !disabled,
    run: () => {
      filterRef.current?.focus();
      filterRef.current?.select();
    },
  });
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
        ref={filterRef}
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
              {/* Greyed whenever it cannot be ticked: a role beyond the signed-in user's grants, or
                  every role of a record shown read-only (critic p03 round 8: the roles the reader
                  could grant looked editable on a read-only record). */}
              <label className={allowed && !disabled ? undefined : "id-disabled"} title={allowed ? undefined : t("identity.form.roleBeyondOwn")}>
                <input type="checkbox" checked={selected.includes(role.id)} disabled={!allowed || disabled} onChange={() => toggle(role)} />
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

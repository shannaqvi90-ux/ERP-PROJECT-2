import { useId, useMemo, useState } from "react";
import { useI18n } from "../../kernel/i18n";
import { allSelected, buildMatrix, matrixActions, rowPermissions, toggleAll, type Permission } from "./model";

/**
 * Permissions as a matrix: a block per module, a row per resource, a column per common action
 * (view, create, change, delete) and the rest in "other". Search narrows the rows; each module,
 * column and row has a bulk toggle, and "all shown" toggles everything the search left. A
 * permission the signed-in user does not hold cannot be ticked or cleared (no escalation).
 */
export function PermissionMatrix({
  permissions,
  selected,
  onChange,
  canChange,
  readOnly,
}: {
  permissions: Permission[];
  selected: ReadonlySet<string>;
  onChange: (next: Set<string>) => void;
  canChange: (key: string) => boolean;
  readOnly?: boolean;
}) {
  const { t } = useI18n();
  const [filter, setFilter] = useState("");
  const id = useId();
  const matrix = useMemo(() => buildMatrix(permissions, filter), [permissions, filter]);
  const shown = matrix.flatMap((m) => m.permissions).filter((p) => canChange(p.key));

  function bulk(keys: string[]) {
    const changeable = keys.filter(canChange);
    onChange(toggleAll(selected, changeable, !allSelected(selected, changeable)));
  }

  function cell(p: Permission | undefined, showLabel = false) {
    if (!p) return null;
    const allowed = !readOnly && canChange(p.key);
    return (
      <label key={p.key} className={allowed ? "id-cell" : "id-cell id-disabled"} title={allowed ? p.key : t("identity.matrix.notHeld")}>
        <input
          type="checkbox"
          checked={selected.has(p.key)}
          disabled={!allowed}
          aria-label={p.label}
          onChange={() => onChange(toggleAll(selected, [p.key], !selected.has(p.key)))}
        />
        {showLabel && <span>{p.label}</span>}
      </label>
    );
  }

  return (
    <div className="id-matrix">
      <div className="id-matrix-tools">
        <input
          type="search"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          placeholder={t("identity.matrix.search")}
          aria-label={t("identity.matrix.search")}
          aria-controls={`${id}-blocks`}
        />
        {!readOnly && (
          <button type="button" className="button" onClick={() => bulk(shown.map((p) => p.key))} disabled={shown.length === 0}>
            {allSelected(selected, shown.map((p) => p.key)) ? t("identity.matrix.clearShown") : t("identity.matrix.selectShown")}
          </button>
        )}
        <span className="muted" aria-live="polite">
          {t("identity.matrix.selected", { count: selected.size })}
        </span>
      </div>
      <div id={`${id}-blocks`}>
        {matrix.length === 0 && <p className="muted">{t("identity.matrix.noMatch")}</p>}
        {matrix.map((block) => (
          <table key={block.module} className="grid id-matrix-table">
            <caption>
              <span>{block.label}</span>
              {!readOnly && (
                <button type="button" className="button ghost id-link" onClick={() => bulk(block.permissions.map((p) => p.key))}>
                  {allSelected(selected, block.permissions.filter((p) => canChange(p.key)).map((p) => p.key)) ? t("identity.matrix.none") : t("identity.matrix.all")}
                </button>
              )}
            </caption>
            <thead>
              <tr>
                <th scope="col">{t("identity.matrix.resource")}</th>
                {matrixActions.map((action) => (
                  <th key={action} scope="col">
                    {readOnly ? (
                      t(`identity.action.${action}`)
                    ) : (
                      <button
                        type="button"
                        className="button ghost id-link"
                        title={t("identity.matrix.toggleColumn")}
                        onClick={() => bulk(block.rows.map((r) => r.cells[action]?.key).filter((k): k is string => !!k))}
                      >
                        {t(`identity.action.${action}`)}
                      </button>
                    )}
                  </th>
                ))}
                <th scope="col">{t("identity.action.other")}</th>
              </tr>
            </thead>
            <tbody>
              {block.rows.map((row) => (
                <tr key={row.resource}>
                  <th scope="row">
                    {readOnly ? (
                      row.label
                    ) : (
                      <button type="button" className="button ghost id-link" title={t("identity.matrix.toggleRow")} onClick={() => bulk(rowPermissions(row).map((p) => p.key))}>
                        {row.label}
                      </button>
                    )}
                  </th>
                  {matrixActions.map((action) => (
                    <td key={action}>{cell(row.cells[action])}</td>
                  ))}
                  <td className="id-other">{row.other.map((p) => cell(p, true))}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ))}
      </div>
    </div>
  );
}

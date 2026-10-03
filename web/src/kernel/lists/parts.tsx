import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { useI18n } from "../i18n";
import { localDayStart, nextDay } from "./format";
import type { Condition, ListColumn, ListDefinition, Operator, SavedView } from "./model";

/** A small dialog or menu anchored in the list toolbar: Escape and a click outside close it and
 * return focus to the control that opened it. */
export function Popover({
  label,
  onClose,
  role = "dialog",
  children,
  className,
}: {
  label: string;
  onClose: () => void;
  role?: "dialog" | "menu";
  children: ReactNode;
  className?: string;
}) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null;
    const first = ref.current?.querySelector<HTMLElement>("input, select, button, [role=menuitem], [role=menuitemradio]");
    first?.focus();
    const onPointer = (event: MouseEvent) => {
      if (ref.current && !ref.current.contains(event.target as Node)) onClose();
    };
    document.addEventListener("mousedown", onPointer);
    return () => {
      document.removeEventListener("mousedown", onPointer);
      opener?.focus?.();
    };
    // Mount and unmount only: the opener is the element focused when the popover opened.
  }, []);
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Escape") {
      event.stopPropagation();
      onClose();
      return;
    }
    if (role === "menu" && (event.key === "ArrowDown" || event.key === "ArrowUp")) {
      const items = [...(ref.current?.querySelectorAll<HTMLElement>("[role=menuitem], [role=menuitemradio]") ?? [])];
      const index = items.indexOf(document.activeElement as HTMLElement);
      const next = items[(index + (event.key === "ArrowDown" ? 1 : -1) + items.length) % items.length];
      next?.focus();
      event.preventDefault();
    }
  };
  return (
    <div ref={ref} role={role} aria-label={label} aria-modal={role === "dialog" ? false : undefined} className={`list-popover ${className ?? ""}`} onKeyDown={onKeyDown}>
      {children}
    </div>
  );
}

const textOperators: Operator[] = ["contains", "eq", "ne", "startsWith", "endsWith", "isNull", "isNotNull"];

/** The filter editor of one column; it replaces that column's conditions. */
export function FilterEditor({
  column,
  current,
  onApply,
  onClose,
}: {
  column: ListColumn;
  current: Condition[];
  onApply: (conditions: Condition[]) => void;
  onClose: () => void;
}) {
  const { t } = useI18n();
  const id = useId();
  const label = t(column.labelKey);
  const first = current[0];
  const [op, setOp] = useState<Operator>(first && column.type === "text" ? first.op : "contains");
  const [text, setText] = useState(first && typeof first.values[0] === "string" && column.type !== "dateTime" && column.type !== "date" ? String(first.values[0]) : "");
  const [choices, setChoices] = useState<string[]>(current.flatMap((c) => (c.op === "in" || c.op === "eq" ? c.values.map(String) : [])));
  const [flag, setFlag] = useState<"any" | "true" | "false">(first?.op === "eq" ? (first.values[0] ? "true" : "false") : "any");
  const lower = current.find((c) => c.op === "ge");
  const upper = current.find((c) => c.op === "le" || c.op === "lt");
  const asDate = (v: unknown, minusDay: boolean) => {
    if (typeof v !== "string") return "";
    const day = v.slice(0, 10);
    if (!minusDay) return day;
    const [y, m, d] = day.split("-").map(Number);
    return new Date(Date.UTC(y!, (m ?? 1) - 1, (d ?? 1) - 1)).toISOString().slice(0, 10);
  };
  const [from, setFrom] = useState(column.type === "date" || column.type === "dateTime" ? asDate(lower?.values[0], false) : lower ? String(lower.values[0]) : "");
  const [to, setTo] = useState(
    column.type === "dateTime" ? asDate(upper?.values[0], upper?.op === "lt") : column.type === "date" ? asDate(upper?.values[0], false) : upper ? String(upper.values[0]) : "",
  );

  const build = (): Condition[] => {
    const key = column.key;
    switch (column.type) {
      case "text":
        if (op === "isNull" || op === "isNotNull") return [{ column: key, op, values: [] }];
        return text.trim() ? [{ column: key, op, values: [text.trim()] }] : [];
      case "choice":
        if (choices.length === 0) return [];
        return choices.length === 1 ? [{ column: key, op: "eq", values: [choices[0]!] }] : [{ column: key, op: "in", values: choices }];
      case "boolean":
        return flag === "any" ? [] : [{ column: key, op: "eq", values: [flag === "true"] }];
      case "number":
      case "money": {
        const result: Condition[] = [];
        if (from.trim() && Number.isFinite(Number(from))) result.push({ column: key, op: "ge", values: [Number(from)] });
        if (to.trim() && Number.isFinite(Number(to))) result.push({ column: key, op: "le", values: [Number(to)] });
        return result;
      }
      case "date": {
        const result: Condition[] = [];
        if (from) result.push({ column: key, op: "ge", values: [from] });
        if (to) result.push({ column: key, op: "le", values: [to] });
        return result;
      }
      case "dateTime": {
        const result: Condition[] = [];
        if (from) result.push({ column: key, op: "ge", values: [localDayStart(from)] });
        if (to) result.push({ column: key, op: "lt", values: [localDayStart(nextDay(to))] });
        return result;
      }
      default:
        return text.trim() ? [{ column: key, op: "eq", values: [text.trim()] }] : [];
    }
  };

  const apply = () => {
    onApply(build());
    onClose();
  };

  let body: ReactNode;
  switch (column.type) {
    case "text":
      body = (
        <>
          <label className="field">
            <span className="field-label">{t("lists.filter.operator")}</span>
            <select value={op} onChange={(e) => setOp(e.target.value as Operator)}>
              {textOperators
                .filter((o) => column.operators.includes(o))
                .map((o) => (
                  <option key={o} value={o}>
                    {t(`lists.op.${o}`)}
                  </option>
                ))}
            </select>
          </label>
          {op !== "isNull" && op !== "isNotNull" && (
            <label className="field">
              <span className="field-label">{t("lists.filter.value")}</span>
              <input value={text} onChange={(e) => setText(e.target.value)} autoComplete="off" />
            </label>
          )}
        </>
      );
      break;
    case "choice":
      body = (
        <fieldset className="list-choices">
          <legend className="field-label">{t("lists.op.in")}</legend>
          {column.choices.map((choice) => (
            <label key={choice.value} className="list-check">
              <input
                type="checkbox"
                checked={choices.includes(choice.value)}
                onChange={(e) => setChoices(e.target.checked ? [...choices, choice.value] : choices.filter((c) => c !== choice.value))}
              />
              {t(choice.labelKey)}
            </label>
          ))}
        </fieldset>
      );
      break;
    case "boolean":
      body = (
        <fieldset className="list-choices">
          <legend className="field-label">{label}</legend>
          {(["any", "true", "false"] as const).map((value) => (
            <label key={value} className="list-check">
              <input type="radio" name={`${id}-flag`} checked={flag === value} onChange={() => setFlag(value)} />
              {t(value === "any" ? "lists.filter.any" : value === "true" ? "lists.filter.yes" : "lists.filter.no")}
            </label>
          ))}
        </fieldset>
      );
      break;
    case "number":
    case "money":
    case "date":
    case "dateTime": {
      const type = column.type === "number" || column.type === "money" ? "number" : "date";
      body = (
        <div className="list-range">
          <label className="field">
            <span className="field-label">{t("lists.filter.from")}</span>
            <input type={type} value={from} onChange={(e) => setFrom(e.target.value)} dir="ltr" />
          </label>
          <label className="field">
            <span className="field-label">{t("lists.filter.to")}</span>
            <input type={type} value={to} onChange={(e) => setTo(e.target.value)} dir="ltr" />
          </label>
        </div>
      );
      break;
    }
    default:
      body = (
        <label className="field">
          <span className="field-label">{t("lists.filter.value")}</span>
          <input value={text} onChange={(e) => setText(e.target.value)} dir="ltr" />
        </label>
      );
  }

  return (
    <Popover label={t("lists.filter.title", { column: label })} onClose={onClose} className="list-filter">
      <form
        onSubmit={(e) => {
          e.preventDefault();
          apply();
        }}
      >
        <h2 className="list-popover-title">{t("lists.filter.title", { column: label })}</h2>
        {body}
        <div className="list-popover-actions">
          <button type="submit" className="button primary">
            {t("lists.filter.apply")}
          </button>
          {current.length > 0 && (
            <button
              type="button"
              className="button"
              onClick={() => {
                onApply([]);
                onClose();
              }}
            >
              {t("lists.filter.clear")}
            </button>
          )}
          <button type="button" className="button ghost-dark" onClick={onClose}>
            {t("lists.filter.cancel")}
          </button>
        </div>
      </form>
    </Popover>
  );
}

/** Show, hide and reorder columns. */
export function ColumnChooser({
  definition,
  columns,
  onChange,
  onReset,
  onClose,
}: {
  definition: ListDefinition;
  columns: string[];
  onChange: (columns: string[]) => void;
  onReset: () => void;
  onClose: () => void;
}) {
  const { t } = useI18n();
  const ordered = [...columns, ...definition.columns.map((c) => c.key).filter((k) => !columns.includes(k))];
  const move = (key: string, delta: number) => {
    const index = columns.indexOf(key);
    const target = index + delta;
    if (index < 0 || target < 0 || target >= columns.length) return;
    const next = [...columns];
    [next[index], next[target]] = [next[target]!, next[index]!];
    onChange(next);
  };
  return (
    <Popover label={t("lists.columns.title")} onClose={onClose} className="list-columns">
      <h2 className="list-popover-title">{t("lists.columns.title")}</h2>
      <ul className="list-column-list">
        {ordered.map((key) => {
          const column = definition.columns.find((c) => c.key === key)!;
          const label = t(column.labelKey);
          const shown = columns.includes(key);
          return (
            <li key={key}>
              <label className="list-check">
                <input
                  type="checkbox"
                  checked={shown}
                  disabled={shown && columns.length === 1}
                  aria-label={t("lists.columns.show", { column: label })}
                  onChange={(e) => onChange(e.target.checked ? [...columns, key] : columns.filter((c) => c !== key))}
                />
                {label}
              </label>
              {shown && (
                <span className="list-column-move">
                  <button type="button" className="button icon" aria-label={t("lists.columns.up", { column: label })} onClick={() => move(key, -1)}>
                    ↑
                  </button>
                  <button type="button" className="button icon" aria-label={t("lists.columns.down", { column: label })} onClick={() => move(key, 1)}>
                    ↓
                  </button>
                </span>
              )}
            </li>
          );
        })}
      </ul>
      <div className="list-popover-actions">
        <button type="button" className="button primary" onClick={onClose}>
          {t("lists.columns.done")}
        </button>
        <button type="button" className="button" onClick={onReset}>
          {t("lists.columns.reset")}
        </button>
      </div>
    </Popover>
  );
}

export type ViewChoice = { id: string; label: string; group: "builtIn" | "shared" | "mine"; isDefault: boolean };

/** The views menu: standard, built-in, shared and personal views, and saving the current state. */
export function ViewsMenu({
  choices,
  current,
  modified,
  canUpdate,
  canDelete,
  onPick,
  onSaveAs,
  onUpdate,
  onDelete,
  onClose,
}: {
  choices: ViewChoice[];
  current: string | null;
  modified: boolean;
  canUpdate: boolean;
  canDelete: boolean;
  onPick: (id: string | null) => void;
  onSaveAs: () => void;
  onUpdate: () => void;
  onDelete: () => void;
  onClose: () => void;
}) {
  const { t } = useI18n();
  const groups: ViewChoice["group"][] = ["builtIn", "shared", "mine"];
  const pick = (id: string | null) => {
    onPick(id);
    onClose();
  };
  return (
    <Popover label={t("lists.views.open")} onClose={onClose} role="menu" className="list-views">
      <button type="button" role="menuitemradio" aria-checked={current === null} className="list-menuitem" onClick={() => pick(null)}>
        {t("lists.views.standard")}
      </button>
      {groups.map((group) => {
        const items = choices.filter((c) => c.group === group);
        if (items.length === 0) return null;
        return (
          <div key={group} role="group" aria-label={t(`lists.views.${group}`)}>
            <div className="list-menu-heading">{t(`lists.views.${group}`)}</div>
            {items.map((item) => (
              <button
                key={item.id}
                type="button"
                role="menuitemradio"
                aria-checked={current === item.id}
                className="list-menuitem"
                onClick={() => pick(item.id)}
              >
                {item.label}
                {item.isDefault && <span className="muted"> · {t("lists.views.default")}</span>}
              </button>
            ))}
          </div>
        );
      })}
      <hr />
      <button type="button" role="menuitem" className="list-menuitem" onClick={() => { onClose(); onSaveAs(); }}>
        {t("lists.views.saveAs")}
      </button>
      {canUpdate && modified && (
        <button type="button" role="menuitem" className="list-menuitem" onClick={() => { onClose(); onUpdate(); }}>
          {t("lists.views.update")}
        </button>
      )}
      {canDelete && (
        <button type="button" role="menuitem" className="list-menuitem" onClick={() => { onClose(); onDelete(); }}>
          {t("lists.views.delete")}
        </button>
      )}
    </Popover>
  );
}

/** Name a new view; sharing is offered only to users who may share. */
export function SaveViewDialog({
  canShare,
  error,
  onSave,
  onClose,
}: {
  canShare: boolean;
  error: string | null;
  onSave: (name: string, shared: boolean, isDefault: boolean) => void;
  onClose: () => void;
}) {
  const { t } = useI18n();
  const [name, setName] = useState("");
  const [shared, setShared] = useState(false);
  const [isDefault, setDefault] = useState(false);
  return (
    <Popover label={t("lists.views.dialog")} onClose={onClose} className="list-save">
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (name.trim()) onSave(name.trim(), shared, isDefault);
        }}
      >
        <h2 className="list-popover-title">{t("lists.views.dialog")}</h2>
        <label className="field">
          <span className="field-label">{t("lists.views.name")}</span>
          <input value={name} maxLength={100} required onChange={(e) => setName(e.target.value)} aria-invalid={error ? true : undefined} />
          {error && <span className="field-error">{error}</span>}
        </label>
        {canShare && (
          <label className="list-check">
            <input type="checkbox" checked={shared} onChange={(e) => setShared(e.target.checked)} />
            {t("lists.views.share")}
          </label>
        )}
        <label className="list-check">
          <input type="checkbox" checked={isDefault} onChange={(e) => setDefault(e.target.checked)} />
          {t("lists.views.makeDefault")}
        </label>
        <div className="list-popover-actions">
          <button type="submit" className="button primary">
            {t("lists.views.save")}
          </button>
          <button type="button" className="button" onClick={onClose}>
            {t("lists.views.cancel")}
          </button>
        </div>
      </form>
    </Popover>
  );
}

export type { SavedView };

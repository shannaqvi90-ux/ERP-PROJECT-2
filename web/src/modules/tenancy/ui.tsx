import { useEffect, useId, useRef, type ReactNode } from "react";
import { ApiError, type FieldError } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { useShortcut } from "../../kernel/shortcuts";

/** The seven emirates, in the order the API documents them. */
export const emirates = Object.freeze(["abuDhabi", "dubai", "sharjah", "ajman", "ummAlQuwain", "rasAlKhaimah", "fujairah"] as const);
export type Emirate = (typeof emirates)[number];

export type FieldErrors = Record<string, FieldError[]>;

/** Field errors and a summary message from an API failure. */
export function problemOf(error: unknown): { message: string; fields: FieldErrors } {
  if (error instanceof ApiError) return { message: error.message, fields: error.fieldErrors };
  return { message: error instanceof Error ? error.message : String(error), fields: {} };
}

type Keys = {
  /** Alt+N: new record. */
  onNew?: () => void;
  /** Ctrl+S (Cmd+S): save. */
  onSave?: () => void;
  /** Escape: close the form. */
  onClose?: () => void;
  /** "/": focus the search box. */
  search?: React.RefObject<HTMLInputElement | null>;
};

/** Screen keyboard shortcuts. Alt+N and Ctrl+S go through the shell's shortcut registry (so they
 * appear in its help sheet); Escape closes the form and "/" focuses the search box. */
export function useScreenKeys({ onNew, onSave, onClose, search }: Keys) {
  const latest = useRef({ onNew, onSave, onClose, search });
  latest.current = { onNew, onSave, onClose, search };
  useShortcut({
    id: "tenancy.screen.new",
    chord: "Alt+KeyN",
    labelKey: "tenancy.shortcut.new",
    groupKey: "tenancy.shortcut.group",
    enabled: Boolean(onNew),
    run: () => latest.current.onNew?.(),
  });
  useShortcut({
    id: "tenancy.form.save",
    chord: "Mod+KeyS",
    labelKey: "tenancy.shortcut.save",
    groupKey: "tenancy.shortcut.group",
    enabled: Boolean(onSave),
    run: () => latest.current.onSave?.(),
  });
  useEffect(() => {
    const handle = (event: KeyboardEvent) => {
      const { onClose, search } = latest.current;
      const typing = event.target instanceof HTMLElement && /^(INPUT|TEXTAREA|SELECT)$/.test(event.target.tagName);
      if (onClose && event.key === "Escape" && !event.defaultPrevented && !document.querySelector('[aria-modal="true"]')) {
        onClose();
      } else if (search?.current && event.key === "/" && !typing) {
        event.preventDefault();
        search.current.focus();
        search.current.select();
      }
    };
    window.addEventListener("keydown", handle);
    return () => window.removeEventListener("keydown", handle);
  }, []);
}

/** The id selected in the screen's address (?id=…, or ?new=1), kept in step without reloading. */
export function readSelection(): { id: string | null; isNew: boolean } {
  const params = new URLSearchParams(window.location.search);
  return { id: params.get("id"), isNew: params.get("new") === "1" };
}

export function writeSelection(id: string | null, isNew = false) {
  const params = new URLSearchParams(window.location.search);
  params.delete("id");
  params.delete("new");
  if (id) params.set("id", id);
  if (isNew) params.set("new", "1");
  const query = params.toString();
  window.history.replaceState(null, "", window.location.pathname + (query ? `?${query}` : ""));
}

type FieldProps = {
  name: string;
  label: string;
  error?: FieldError[];
  hint?: string;
  children: (props: { id: string; "aria-invalid": boolean; "aria-describedby"?: string }) => ReactNode;
  wide?: boolean;
};

/** A labelled field with its validation messages (from the API, in the screen's language). */
export function Field({ name, label, error, hint, children, wide }: FieldProps) {
  const id = useId();
  const errorId = `${id}-error`;
  const hintId = `${id}-hint`;
  const described = [error?.length ? errorId : null, hint ? hintId : null].filter(Boolean).join(" ") || undefined;
  return (
    <div className={wide ? "field wide" : "field"} data-field={name}>
      <label className="field-label" htmlFor={id}>
        {label}
      </label>
      {children({ id, "aria-invalid": Boolean(error?.length), "aria-describedby": described })}
      {hint && (
        <span className="field-hint muted" id={hintId}>
          {hint}
        </span>
      )}
      {error?.length ? (
        <span className="field-error" id={errorId} role="alert">
          {error.map((e) => e.message).join(" ")}
        </span>
      ) : null}
    </div>
  );
}

type TextProps = {
  name: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  errors: FieldErrors;
  dir?: "ltr" | "rtl" | "auto";
  maxLength?: number;
  required?: boolean;
  autoFocus?: boolean;
  type?: "text" | "email" | "tel" | "url" | "number";
  hint?: string;
  list?: string;
  disabled?: boolean;
  inputMode?: "text" | "numeric" | "tel" | "email" | "url";
  upper?: boolean;
  wide?: boolean;
  multiline?: boolean;
};

export function TextField(p: TextProps) {
  return (
    <Field name={p.name} label={p.label} error={p.errors[p.name]} hint={p.hint} wide={p.wide}>
      {(a11y) =>
        p.multiline ? (
          <textarea
            {...a11y}
            name={p.name}
            value={p.value}
            dir={p.dir}
            maxLength={p.maxLength}
            required={p.required}
            rows={2}
            disabled={p.disabled}
            onChange={(e) => p.onChange(e.target.value)}
          />
        ) : (
          <input
            {...a11y}
            name={p.name}
            type={p.type ?? "text"}
            value={p.value}
            dir={p.dir}
            maxLength={p.maxLength}
            required={p.required}
            autoFocus={p.autoFocus}
            list={p.list}
            disabled={p.disabled}
            inputMode={p.inputMode}
            autoComplete="off"
            onChange={(e) => p.onChange(p.upper ? e.target.value.toUpperCase() : e.target.value)}
          />
        )
      }
    </Field>
  );
}

type SelectProps<T extends string> = {
  name: string;
  label: string;
  value: T | "";
  options: { value: T; label: string }[];
  onChange: (value: T | "") => void;
  errors: FieldErrors;
  empty?: string;
  disabled?: boolean;
};

export function SelectField<T extends string>(p: SelectProps<T>) {
  return (
    <Field name={p.name} label={p.label} error={p.errors[p.name]}>
      {(a11y) => (
        <select {...a11y} name={p.name} value={p.value} disabled={p.disabled} onChange={(e) => p.onChange(e.target.value as T | "")}>
          {p.empty !== undefined && <option value="">{p.empty}</option>}
          {p.options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      )}
    </Field>
  );
}

export function CheckField({ name, label, checked, onChange }: { name: string; label: string; checked: boolean; onChange: (value: boolean) => void }) {
  return (
    <label className="check" data-field={name}>
      <input type="checkbox" name={name} checked={checked} onChange={(e) => onChange(e.target.checked)} />
      <span>{label}</span>
    </label>
  );
}

/** Month names for the fiscal-year selector, in the screen's language. */
export function useMonths(): { value: string; label: string }[] {
  const { t } = useI18n();
  return Array.from({ length: 12 }, (_, i) => ({ value: String(i + 1), label: t(`tenancy.month.${i + 1}`) }));
}

/** English or Arabic name of a record, by screen language. */
export function useLocalName() {
  const { language } = useI18n();
  return (en: string, ar: string) => (language === "ar" ? ar || en : en || ar);
}

/** Moves a row selection with the arrow keys and opens it with Enter. */
export function gridKeys<T extends { id: string }>(rows: T[], current: string | null, select: (id: string) => void, open: (id: string) => void) {
  return (event: React.KeyboardEvent) => {
    if (rows.length === 0) return;
    const index = rows.findIndex((r) => r.id === current);
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      const next = event.key === "ArrowDown" ? Math.min(rows.length - 1, index + 1) : Math.max(0, index - 1);
      const row = rows[next < 0 ? 0 : next];
      if (row) select(row.id);
    } else if (event.key === "Enter" && current) {
      event.preventDefault();
      open(current);
    }
  };
}

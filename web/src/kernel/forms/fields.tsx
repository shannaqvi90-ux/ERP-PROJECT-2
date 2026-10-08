import { useEffect, useId, useRef, useState, type KeyboardEvent, type MouseEvent, type ReactNode } from "react";
import { api } from "../api";
import { useI18n } from "../i18n";
import { minorUnitsOf } from "../format";
import type { FieldBinding } from "./useRecordForm";

type A11y = { id: string; "aria-invalid": boolean; "aria-describedby"?: string };

/**
 * A labelled field with its hint and validation messages (from the server, in the screen's
 * language). Every field of every form has this shape: <c>div.field[data-field=name]</c>, so the
 * form can put the cursor on the first field the server refused.
 */
export function Field({ name, label, errors = [], hint, wide, children }: {
  name: string;
  label: string;
  errors?: string[];
  hint?: string;
  wide?: boolean;
  children: (a11y: A11y) => ReactNode;
}) {
  const id = useId();
  const errorId = `${id}-error`;
  const hintId = `${id}-hint`;
  const described = [errors.length ? errorId : null, hint ? hintId : null].filter(Boolean).join(" ") || undefined;
  return (
    <div className={wide ? "field wide" : "field"} data-field={name}>
      <label className="field-label" htmlFor={id}>
        {label}
      </label>
      {children({ id, "aria-invalid": errors.length > 0, "aria-describedby": described })}
      {hint && (
        <span className="field-hint muted" id={hintId}>
          {hint}
        </span>
      )}
      {errors.length > 0 && (
        <span className="field-error" id={errorId} role="alert">
          {errors.join(" ")}
        </span>
      )}
    </div>
  );
}

type Common = { label: string; hint?: string; wide?: boolean; disabled?: boolean; autoFocus?: boolean };

/**
 * Clicking into a field that is not being edited selects its whole value, so what is typed
 * replaces it, as it does when the field is reached with Tab; a second click, or a drag that
 * selects part of the value, places the caret or keeps that selection as usual.
 */
export const replaceOnEntry = {
  onMouseDown: (event: MouseEvent<HTMLInputElement>) => {
    if (document.activeElement !== event.currentTarget) event.currentTarget.dataset.entering = "1";
  },
  onMouseUp: (event: MouseEvent<HTMLInputElement>) => {
    const input = event.currentTarget;
    if (!input.dataset.entering) return;
    delete input.dataset.entering;
    if (input.selectionStart === input.selectionEnd) {
      input.select();
      event.preventDefault();
    }
  },
};

/** One line of text. `dir` follows the value (ltr for codes, e-mails, numbers; rtl for Arabic names); free text
 *  without one takes the direction of what is typed, so an English name in an Arabic form reads from its start. */
export function TextField({ field, dir, maxLength, type = "text", inputMode, upper, list, required, ...p }: Common & {
  field: FieldBinding<string>;
  dir?: "ltr" | "rtl" | "auto";
  maxLength?: number;
  type?: "text" | "email" | "tel" | "url";
  inputMode?: "text" | "numeric" | "tel" | "email" | "url" | "decimal";
  upper?: boolean;
  list?: string;
  required?: boolean;
}) {
  return (
    <Field name={field.name} label={p.label} errors={field.errors} hint={p.hint} wide={p.wide}>
      {(a11y) => (
        <input
          {...a11y}
          name={field.name}
          type={type}
          value={field.value ?? ""}
          dir={dir ?? "auto"}
          maxLength={maxLength}
          required={required}
          autoFocus={p.autoFocus}
          list={list}
          inputMode={inputMode}
          disabled={p.disabled || field.readOnly}
          autoComplete="off"
          {...replaceOnEntry}
          onChange={(e) => field.onChange(upper ? e.target.value.toUpperCase() : e.target.value)}
        />
      )}
    </Field>
  );
}

/** Several lines of text. */
export function TextAreaField({ field, dir, maxLength, rows = 2, ...p }: Common & { field: FieldBinding<string>; dir?: "ltr" | "rtl" | "auto"; maxLength?: number; rows?: number }) {
  return (
    <Field name={field.name} label={p.label} errors={field.errors} hint={p.hint} wide={p.wide ?? true}>
      {(a11y) => (
        <textarea
          {...a11y}
          name={field.name}
          value={field.value ?? ""}
          dir={dir ?? "auto"}
          rows={rows}
          maxLength={maxLength}
          disabled={p.disabled || field.readOnly}
          onChange={(e) => field.onChange(e.target.value)}
        />
      )}
    </Field>
  );
}

/** Only the characters a decimal number is typed with: digits, one dot, a leading minus. Arabic-Indic
 * digits typed on an Arabic keyboard become Latin digits, so the value sent is always a plain decimal. */
export function decimalInput(text: string, scale?: number): string | null {
  const latin = text.replace(/[٠-٩]/g, (d) => String("٠١٢٣٤٥٦٧٨٩".indexOf(d))).replace(/[٫,]/g, ".").replace(/\s/g, "");
  if (!/^-?\d*(\.\d*)?$/.test(latin)) return null;
  if (scale !== undefined) {
    const dot = latin.indexOf(".");
    if (dot >= 0 && latin.length - dot - 1 > scale) return null;
  }
  return latin;
}

/** A decimal number kept as text (never a binary float): digits, a dot, an optional minus. */
export function DecimalField({ field, scale, ...p }: Common & { field: FieldBinding<string>; scale?: number }) {
  return (
    <Field name={field.name} label={p.label} errors={field.errors} hint={p.hint} wide={p.wide}>
      {(a11y) => (
        <input
          {...a11y}
          name={field.name}
          className="num"
          inputMode="decimal"
          dir="ltr"
          value={field.value ?? ""}
          disabled={p.disabled || field.readOnly}
          autoFocus={p.autoFocus}
          autoComplete="off"
          {...replaceOnEntry}
          onChange={(e) => {
            const next = decimalInput(e.target.value, scale);
            if (next !== null) field.onChange(next);
          }}
        />
      )}
    </Field>
  );
}

/** An amount with its currency (CLAUDE.md rule 2): a decimal kept as text and an ISO currency code. */
/** a × b, both decimal text, rounded half away from zero to `scale` places: exact (BigInt), never a
 * binary float (CLAUDE.md rule 2). Null when either is not a number. */
export function multiplyDecimal(a: string, b: string, scale: number): string | null {
  const parse = (text: string) => {
    const m = /^(-?)(\d*)(?:\.(\d*))?$/.exec(text.trim());
    if (!m || (m[2] === "" && (m[3] ?? "") === "")) return null;
    const fraction = m[3] ?? "";
    return { negative: m[1] === "-", digits: BigInt((m[2] || "0") + fraction), places: fraction.length };
  };
  const x = parse(a);
  const y = parse(b);
  if (!x || !y) return null;
  const places = x.places + y.places;
  let product = x.digits * y.digits;
  if (places > scale) {
    const divisor = 10n ** BigInt(places - scale);
    product = (product + divisor / 2n) / divisor;
  } else {
    product *= 10n ** BigInt(scale - places);
  }
  const negative = x.negative !== y.negative && product !== 0n;
  const text = product.toString().padStart(scale + 1, "0");
  const whole = text.slice(0, text.length - scale);
  const fraction = scale > 0 ? "." + text.slice(text.length - scale) : "";
  return (negative ? "-" : "") + whole + fraction;
}

/**
 * An amount with its currency. Where the record keeps the exchange rate used and the amount in
 * the company's base currency (CLAUDE.md rule 2), pass `rate` and `baseCurrency`: for an amount in
 * another currency the field then takes the rate (units of the base currency per unit) and shows
 * the base amount it gives, worked out exactly in decimal.
 */
export function MoneyField({
  amount,
  currency,
  currencies = [],
  rate,
  baseCurrency,
  ...p
}: Common & { amount: FieldBinding<string>; currency: FieldBinding<string>; currencies?: readonly string[]; rate?: FieldBinding<string>; baseCurrency?: string }) {
  const { t, format } = useI18n();
  const listId = useId();
  const foreign = Boolean(rate && baseCurrency && currency.value && currency.value !== baseCurrency);
  // At the base currency's own decimals (KWD 3, AED 2, JPY 0), rounded half away from zero as the server stores it.
  const base = foreign && rate ? multiplyDecimal(amount.value ?? "", rate.value ?? "", minorUnitsOf(baseCurrency!)) : null;
  return (
    <Field name={amount.name} label={p.label} errors={[...amount.errors, ...currency.errors]} hint={p.hint} wide={p.wide}>
      {(a11y) => (
        <span className="money-input" dir="ltr">
          <input
            {...a11y}
            name={amount.name}
            className="num"
            inputMode="decimal"
            value={amount.value ?? ""}
            disabled={p.disabled || amount.readOnly}
            autoComplete="off"
            {...replaceOnEntry}
            onChange={(e) => {
              const next = decimalInput(e.target.value, 6);
              if (next !== null) amount.onChange(next);
            }}
          />
          <input
            name={currency.name}
            className="currency"
            aria-label={t("forms.field.currency")}
            value={currency.value ?? ""}
            maxLength={3}
            list={listId}
            disabled={p.disabled || currency.readOnly}
            autoComplete="off"
            onChange={(e) => currency.onChange(e.target.value.toUpperCase())}
          />
          <datalist id={listId}>
            {currencies.map((c) => (
              <option key={c} value={c} />
            ))}
          </datalist>
          {foreign && rate && (
            <>
              <input
                name={rate.name}
                className="num rate"
                inputMode="decimal"
                aria-label={t("forms.field.rate", { currency: baseCurrency! })}
                aria-invalid={rate.errors.length > 0 || undefined}
                value={rate.value ?? ""}
                disabled={p.disabled || rate.readOnly}
                autoComplete="off"
                {...replaceOnEntry}
                onChange={(e) => {
                  const next = decimalInput(e.target.value, 10);
                  if (next !== null) rate.onChange(next);
                }}
              />
              <output className="money-base" aria-label={t("forms.field.baseAmount", { currency: baseCurrency! })}>
                {base === null ? "" : format.amount(base, baseCurrency!)}
              </output>
            </>
          )}
        </span>
      )}
    </Field>
  );
}

/** A calendar date (yyyy-mm-dd), with the browser's date picker. */
export function DateField({ field, min, max, ...p }: Common & { field: FieldBinding<string>; min?: string; max?: string }) {
  return (
    <Field name={field.name} label={p.label} errors={field.errors} hint={p.hint} wide={p.wide}>
      {(a11y) => (
        <input
          {...a11y}
          name={field.name}
          type="date"
          dir="ltr"
          value={field.value ?? ""}
          min={min}
          max={max}
          disabled={p.disabled || field.readOnly}
          autoFocus={p.autoFocus}
          onChange={(e) => field.onChange(e.target.value)}
        />
      )}
    </Field>
  );
}

/** A yes/no value as a checkbox (Space toggles it). */
export function BooleanField({ field, label, disabled }: { field: FieldBinding<boolean>; label: string; disabled?: boolean }) {
  return (
    <label className="check" data-field={field.name}>
      <input type="checkbox" name={field.name} checked={Boolean(field.value)} disabled={disabled || field.readOnly} onChange={(e) => field.onChange(e.target.checked)} />
      <span>{label}</span>
      {field.errors.length > 0 && (
        <span className="field-error" role="alert">
          {field.errors.join(" ")}
        </span>
      )}
    </label>
  );
}

/** One of a few values. With `empty`, the field may be left blank. */
export function SelectField<V extends string>({ field, options, empty, ...p }: Common & { field: FieldBinding<V | "">; options: readonly { value: V; label: string }[]; empty?: string }) {
  return (
    <Field name={field.name} label={p.label} errors={field.errors} hint={p.hint} wide={p.wide}>
      {(a11y) => (
        <select {...a11y} name={field.name} value={field.value ?? ""} disabled={p.disabled || field.readOnly} autoFocus={p.autoFocus} onChange={(e) => field.onChange(e.target.value as V | "")}>
          {empty !== undefined && <option value="">{empty}</option>}
          {options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      )}
    </Field>
  );
}

export type LookupRow = { id: string } & Record<string, unknown>;

/**
 * A reference to another record, chosen by typing part of its name: the field searches the
 * referenced list (its endpoint, the list query contract), offers the matches below it (arrow keys
 * and Enter, or the mouse) and keeps the chosen record's id; Escape or Delete on an empty search
 * clears it. The chosen record's label is read once from the list's endpoint when not given.
 */
export function LookupField({ field, endpoint, labelOf, initialLabel, ...p }: Common & {
  field: FieldBinding<string>;
  /** The referenced list's endpoint, for example /api/tenancy/companies. */
  endpoint: string;
  labelOf: (row: LookupRow) => string;
  initialLabel?: string;
}) {
  const { t } = useI18n();
  const listId = useId();
  const [label, setLabel] = useState(initialLabel ?? "");
  const [text, setText] = useState<string | null>(null);
  const [options, setOptions] = useState<LookupRow[]>([]);
  const [active, setActive] = useState(0);
  const [open, setOpen] = useState(false);
  const labelOfRef = useRef(labelOf);
  labelOfRef.current = labelOf;

  // The chosen record's label, when only its id is known.
  useEffect(() => {
    if (!field.value) {
      setLabel("");
      return;
    }
    if (initialLabel) {
      setLabel(initialLabel);
      return;
    }
    let live = true;
    api<LookupRow>("GET", `${endpoint}/${encodeURIComponent(field.value)}`)
      .then((row) => live && setLabel(labelOfRef.current(row)))
      .catch(() => live && setLabel(field.value));
    return () => {
      live = false;
    };
  }, [field.value, endpoint, initialLabel]);

  // Search as the user types, a moment after the last key.
  useEffect(() => {
    if (text === null) return;
    const timer = window.setTimeout(() => {
      const query = new URLSearchParams({ take: "20" });
      if (text.trim()) query.set("search", text.trim());
      api<{ items: LookupRow[] }>("GET", `${endpoint}?${query}`)
        .then((page) => {
          setOptions(page.items);
          setActive(0);
          setOpen(true);
        })
        .catch(() => setOptions([]));
    }, 150);
    return () => window.clearTimeout(timer);
  }, [text, endpoint]);

  const choose = (row: LookupRow | null) => {
    field.onChange(row?.id ?? "");
    setLabel(row ? labelOfRef.current(row) : "");
    setText(null);
    setOpen(false);
  };

  const onKey = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      if (!open) setText(text ?? "");
      setActive((a) => Math.min(a + 1, Math.max(0, options.length - 1)));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActive((a) => Math.max(0, a - 1));
    } else if (event.key === "Enter" && open && options[active]) {
      event.preventDefault();
      choose(options[active]!);
    } else if (event.key === "Escape" && (open || text !== null)) {
      event.preventDefault();
      event.stopPropagation();
      setText(null);
      setOpen(false);
    } else if ((event.key === "Delete" || event.key === "Backspace") && text === null && field.value) {
      event.preventDefault();
      choose(null);
    }
  };

  return (
    <Field name={field.name} label={p.label} errors={field.errors} hint={p.hint ?? t("forms.lookup.hint")} wide={p.wide}>
      {(a11y) => (
        <span className="lookup">
          <input
            {...a11y}
            name={field.name}
            role="combobox"
            aria-autocomplete="list"
            aria-expanded={open}
            aria-controls={listId}
            aria-activedescendant={open && options[active] ? `${listId}-${active}` : undefined}
            value={text ?? label}
            placeholder={t("forms.lookup.placeholder")}
            disabled={p.disabled || field.readOnly}
            autoFocus={p.autoFocus}
            autoComplete="off"
            onChange={(e) => setText(e.target.value)}
            onFocus={(e) => e.currentTarget.select()}
            onBlur={() => window.setTimeout(() => setOpen(false), 150)}
            onKeyDown={onKey}
          />
          {open && (
            <ul className="lookup-options" role="listbox" id={listId}>
              {options.length === 0 && <li className="muted">{t("forms.lookup.none")}</li>}
              {options.map((row, i) => (
                <li
                  key={row.id}
                  id={`${listId}-${i}`}
                  role="option"
                  aria-selected={i === active}
                  className={i === active ? "is-active" : undefined}
                  onMouseDown={(e) => {
                    e.preventDefault();
                    choose(row);
                  }}
                >
                  {labelOfRef.current(row)}
                </li>
              ))}
            </ul>
          )}
        </span>
      )}
    </Field>
  );
}

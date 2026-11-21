import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { useI18n } from "../i18n";
import { fold } from "../fold";

export type Choice = { value: string; label: string };

/**
 * A drop-down with search: the field shows the chosen name; typing narrows the list (in either
 * language, ignoring accents, Arabic vowel marks and letter variants), Up and Down move, Enter
 * chooses, Escape closes, Alt+Down or a click opens the whole list. The choices are drawn by the
 * product, so every name stays readable (a long name is given its full text as a title) and the
 * list mirrors right to left. A native select, hidden from the screen and the Tab order, carries
 * the same name and value, so forms, autofill and assistive tools that read a select still work.
 */
export function SearchSelect({
  id,
  name,
  value,
  options,
  empty,
  disabled,
  autoFocus,
  invalid,
  describedBy,
  onChange,
}: {
  id: string;
  name: string;
  value: string;
  options: readonly Choice[];
  /** Label of the blank choice; without it the field cannot be left empty. */
  empty?: string;
  disabled?: boolean;
  autoFocus?: boolean;
  invalid?: boolean;
  describedBy?: string;
  onChange: (value: string) => void;
}) {
  const { t } = useI18n();
  const listId = useId();
  const [text, setText] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const input = useRef<HTMLInputElement>(null);
  const list = useRef<HTMLUListElement>(null);
  const all = useMemo<Choice[]>(() => (empty !== undefined ? [{ value: "", label: empty }, ...options] : [...options]), [options, empty]);
  const chosen = all.find((o) => o.value === value)?.label ?? "";
  const shown = useMemo(() => {
    const q = text === null ? "" : fold(text);
    if (!q) return all;
    const words = q.split(" ");
    const starts = all.filter((o) => fold(o.label).startsWith(q));
    const rest = all.filter((o) => !starts.includes(o) && words.every((w) => fold(o.label).includes(w) || o.value.toLowerCase() === w));
    return [...starts, ...rest];
  }, [all, text]);

  useEffect(() => {
    list.current?.querySelector('[aria-selected="true"]')?.scrollIntoView?.({ block: "nearest" });
  }, [active, open]);

  const choose = (choice: Choice | undefined) => {
    if (!choice) return;
    onChange(choice.value);
    setText(null);
    setOpen(false);
  };
  const openList = () => {
    setActive(Math.max(0, shown.findIndex((o) => o.value === value)));
    setOpen(true);
  };

  const onKey = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      if (!open) openList();
      else setActive((a) => Math.min(a + 1, shown.length - 1));
    } else if (e.key === "ArrowUp" && open) {
      e.preventDefault();
      setActive((a) => Math.max(0, a - 1));
    } else if (e.key === "PageDown" && open) {
      e.preventDefault();
      setActive((a) => Math.min(a + 8, shown.length - 1));
    } else if (e.key === "PageUp" && open) {
      e.preventDefault();
      setActive((a) => Math.max(0, a - 8));
    } else if (e.key === "Enter" && open) {
      e.preventDefault();
      e.stopPropagation();
      choose(shown[active]);
    } else if (e.key === "Escape" && (open || text !== null)) {
      e.preventDefault();
      e.stopPropagation();
      setText(null);
      setOpen(false);
    } else if (e.key === "Tab" && open && text !== null && shown[active]) {
      choose(shown[active]);
    }
  };

  return (
    <span className="search-select">
      <input
        ref={input}
        id={id}
        type="text"
        role="combobox"
        aria-autocomplete="list"
        aria-expanded={open}
        aria-controls={listId}
        aria-invalid={invalid}
        aria-describedby={describedBy}
        aria-activedescendant={open && shown[active] ? `${listId}-${active}` : undefined}
        value={text ?? chosen}
        title={chosen}
        placeholder={t("forms.select.search")}
        disabled={disabled}
        autoFocus={autoFocus}
        autoComplete="off"
        spellCheck={false}
        onChange={(e) => {
          setText(e.target.value);
          setActive(0);
          setOpen(true);
        }}
        onFocus={(e) => e.currentTarget.select()}
        onClick={() => !open && openList()}
        onBlur={() => {
          window.setTimeout(() => {
            setOpen(false);
            setText(null);
          }, 120);
        }}
        onKeyDown={onKey}
      />
      <span className="search-select-caret" aria-hidden="true" onMouseDown={(e) => { e.preventDefault(); input.current?.focus(); if (!open) openList(); else setOpen(false); }}>▾</span>
      {open && (
        <ul ref={list} className="lookup-options search-select-options" role="listbox" id={listId}>
          {shown.length === 0 && <li className="muted">{t("forms.lookup.none")}</li>}
          {shown.map((o, i) => (
            <li
              key={o.value || "_empty"}
              id={`${listId}-${i}`}
              role="option"
              title={o.label}
              aria-selected={i === active}
              data-chosen={o.value === value || undefined}
              className={i === active ? "is-active" : undefined}
              onMouseDown={(e) => {
                e.preventDefault();
                choose(o);
              }}
            >
              {o.label}
            </li>
          ))}
        </ul>
      )}
      <select className="search-select-native" name={name} value={value} tabIndex={-1} aria-hidden="true" disabled={disabled} onChange={(e) => onChange(e.target.value)}>
        {empty !== undefined && <option value="">{empty}</option>}
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </span>
  );
}

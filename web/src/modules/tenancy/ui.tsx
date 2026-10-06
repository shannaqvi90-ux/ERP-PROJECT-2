import { useEffect, useRef } from "react";
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
  /** "/": focus the search box. */
  search?: React.RefObject<HTMLInputElement | null>;
};

/** Screen keyboard shortcuts of the tenancy list screens: Alt+N through the shell's shortcut
 * registry (listed in its help sheet, matched by key position on an Arabic layout) and "/" for the
 * search box. Saving, discarding, closing and moving between records are the shared record form's
 * keys (kernel/forms/RecordForm). */
export function useScreenKeys({ onNew, search }: Keys) {
  const latest = useRef({ onNew, search });
  latest.current = { onNew, search };
  useShortcut({
    id: "tenancy.screen.new",
    chord: "Alt+KeyN",
    labelKey: "tenancy.shortcut.new",
    groupKey: "tenancy.shortcut.group",
    enabled: Boolean(onNew),
    run: () => latest.current.onNew?.(),
  });
  useEffect(() => {
    const handle = (event: KeyboardEvent) => {
      const { search } = latest.current;
      const typing = event.target instanceof HTMLElement && /^(INPUT|TEXTAREA|SELECT)$/.test(event.target.tagName);
      if (search?.current && event.key === "/" && !typing) {
        event.preventDefault();
        search.current.focus();
        search.current.select();
      }
    };
    window.addEventListener("keydown", handle);
    return () => window.removeEventListener("keydown", handle);
  }, []);
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

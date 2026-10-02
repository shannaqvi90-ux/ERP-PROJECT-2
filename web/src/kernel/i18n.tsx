import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { formatMessage, type MessageParams } from "./messageFormat";

export type Language = "en" | "ar";
export const languages: readonly Language[] = ["en", "ar"];

export type Strings = Record<string, string>;
type StringModule = { default: Strings };

/**
 * Every module keeps its own strings in `src/modules/<module>/i18n/en.json` and `ar.json`.
 * They are merged here; a module adds strings without touching any central file.
 */
const files = import.meta.glob<StringModule>("../modules/*/i18n/*.json", { eager: true });

export function buildCatalog(sources: Record<string, StringModule>): Record<Language, Strings> {
  const catalog: Record<Language, Strings> = { en: {}, ar: {} };
  for (const [path, module] of Object.entries(sources)) {
    const match = /\/i18n\/(en|ar)\.json$/.exec(path);
    if (!match) continue;
    const language = match[1] as Language;
    for (const [key, value] of Object.entries(module.default)) {
      if (key in catalog[language]) {
        throw new Error(`String key "${key}" is defined twice (${path}).`);
      }
      catalog[language][key] = value;
    }
  }
  return catalog;
}

export const catalog = buildCatalog(files);

export const localeOf = (language: Language): string => (language === "ar" ? "ar-AE" : "en-AE");

/** Text for a key, with `{name}` placeholders and `{count, plural, …}` filled (numbers formatted
 * for the language). A missing key shows the key itself. */
export function translate(language: Language, key: string, params?: MessageParams): string {
  const text = catalog[language][key] ?? catalog.en[key] ?? key;
  if (!params) return text;
  return formatMessage(text, localeOf(language), params);
}

export const direction = (language: Language): "rtl" | "ltr" => (language === "ar" ? "rtl" : "ltr");

const storageKey = "erp.language";

export function isLanguage(value: unknown): value is Language {
  return value === "en" || value === "ar";
}

/** The device's remembered choice, else the browser's preference, else English. */
export function initialLanguage(): Language {
  try {
    const stored = localStorage.getItem(storageKey);
    if (isLanguage(stored)) return stored;
  } catch {
    // Storage unavailable (private mode); fall through.
  }
  const preferred = (navigator.languages ?? [navigator.language]).map((l) => l.slice(0, 2).toLowerCase());
  return preferred.find(isLanguage) ?? "en";
}

let currentLanguage: Language = "en";

/** The language API requests ask for (Accept-Language), so server messages match the screen. */
export const requestLanguage = (): Language => currentLanguage;

export function applyLanguage(language: Language): void {
  currentLanguage = language;
  const root = document.documentElement;
  root.lang = language;
  root.dir = direction(language);
  document.title = translate(language, "shell.app.title");
  try {
    localStorage.setItem(storageKey, language);
  } catch {
    // Not persisted on this device; the user's profile still carries it.
  }
}

type I18n = {
  language: Language;
  dir: "rtl" | "ltr";
  setLanguage: (language: Language) => void;
  t: (key: string, params?: Record<string, string | number>) => string;
  formatDateTime: (value: string | Date) => string;
  formatNumber: (value: number) => string;
};

const I18nContext = createContext<I18n | null>(null);

export function I18nProvider({ initial, children }: { initial?: Language; children: ReactNode }) {
  const [language, setLanguageState] = useState<Language>(() => initial ?? initialLanguage());

  useEffect(() => applyLanguage(language), [language]);

  const setLanguage = useCallback((next: Language) => setLanguageState(next), []);

  const value = useMemo<I18n>(() => {
    const locale = localeOf(language);
    const dateTime = new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short" });
    const number = new Intl.NumberFormat(locale);
    return {
      language,
      dir: direction(language),
      setLanguage,
      t: (key, params) => translate(language, key, params),
      formatDateTime: (v) => dateTime.format(typeof v === "string" ? new Date(v) : v),
      formatNumber: (v) => number.format(v),
    };
  }, [language, setLanguage]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18n {
  const context = useContext(I18nContext);
  if (!context) throw new Error("useI18n must be used inside I18nProvider");
  return context;
}

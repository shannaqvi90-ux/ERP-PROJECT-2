import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { createFormatter, intlLocale, isNumerals, type Formatter, type Language, type Numerals } from "./format";
import { formatMessage, type MessageParams } from "./messageFormat";

export type { Language, Numerals } from "./format";
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

/** Intl locale of a language and digit choice (ar-AE-u-ca-gregory-nu-arab, …). */
export const localeOf = (language: Language, numerals: Numerals = "latn"): string => intlLocale(language, numerals);

/** Text for a key, with `{name}` placeholders and `{count, plural, …}` filled (numbers formatted
 * for the language and digit choice). A missing key shows the key itself. */
export function translate(language: Language, key: string, params?: MessageParams, numerals: Numerals = "latn"): string {
  const text = catalog[language][key] ?? catalog.en[key] ?? key;
  if (!params) return text;
  return formatMessage(text, localeOf(language, numerals), params);
}

/** True when a key has text (in English, the reference language). */
export const hasString = (key: string): boolean => key in catalog.en;

export const direction = (language: Language): "rtl" | "ltr" => (language === "ar" ? "rtl" : "ltr");

const storageKey = "erp.language";
const numeralsKey = "erp.numerals";

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

/** The device's remembered digit choice, else Latin digits (the common choice in the UAE). */
export function initialNumerals(): Numerals {
  try {
    const stored = localStorage.getItem(numeralsKey);
    if (isNumerals(stored)) return stored;
  } catch {
    // Storage unavailable; fall through.
  }
  return "latn";
}

let currentLanguage: Language = "en";

/** The language API requests ask for (Accept-Language), so server messages match the screen. */
export const requestLanguage = (): Language => currentLanguage;

export function applyLanguage(language: Language): void {
  currentLanguage = language;
  const root = document.documentElement;
  root.lang = language;
  root.dir = direction(language);
  try {
    localStorage.setItem(storageKey, language);
  } catch {
    // Not persisted on this device; the user's profile still carries it.
  }
}

export function applyNumerals(numerals: Numerals): void {
  document.documentElement.dataset.numerals = numerals;
  try {
    localStorage.setItem(numeralsKey, numerals);
  } catch {
    // Not persisted on this device; the user's profile still carries it.
  }
}

type I18n = {
  language: Language;
  /** The user's digit choice for Arabic screens (English screens always use Latin digits). */
  numerals: Numerals;
  dir: "rtl" | "ltr";
  setLanguage: (language: Language) => void;
  setNumerals: (numerals: Numerals) => void;
  t: (key: string, params?: Record<string, string | number>) => string;
  /** Every number, amount and date on a screen goes through this (see kernel/format.ts). */
  format: Formatter;
  formatDateTime: (value: string | Date) => string;
  formatNumber: (value: number) => string;
};

const I18nContext = createContext<I18n | null>(null);

export function I18nProvider({ initial, initialDigits, children }: { initial?: Language; initialDigits?: Numerals; children: ReactNode }) {
  const [language, setLanguageState] = useState<Language>(() => initial ?? initialLanguage());
  const [numerals, setNumeralsState] = useState<Numerals>(() => initialDigits ?? initialNumerals());

  useEffect(() => applyLanguage(language), [language]);
  useEffect(() => applyNumerals(numerals), [numerals]);

  const setLanguage = useCallback((next: Language) => setLanguageState(next), []);
  const setNumerals = useCallback((next: Numerals) => setNumeralsState(next), []);

  const value = useMemo<I18n>(() => {
    const format = createFormatter(language, numerals);
    return {
      language,
      numerals,
      dir: direction(language),
      setLanguage,
      setNumerals,
      t: (key, params) => translate(language, key, params, numerals),
      format,
      formatDateTime: format.dateTime,
      formatNumber: format.number,
    };
  }, [language, numerals, setLanguage, setNumerals]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

/**
 * The tab's title: the screen's name, then the product's, in the screen language. Kept current
 * when the language changes (Alt+L) without a reload. `screenKey` null: the product's name alone.
 */
export function documentTitle(language: Language, screenKey: string | null, numerals: Numerals = "latn"): string {
  const product = translate(language, "shell.app.title", undefined, numerals);
  return screenKey ? `${translate(language, screenKey, undefined, numerals)} · ${product}` : product;
}

export function useDocumentTitle(screenKey: string | null): void {
  const { language, numerals } = useI18n();
  useEffect(() => {
    document.title = documentTitle(language, screenKey, numerals);
  }, [language, numerals, screenKey]);
}

export function useI18n(): I18n {
  const context = useContext(I18nContext);
  if (!context) throw new Error("useI18n must be used inside I18nProvider");
  return context;
}

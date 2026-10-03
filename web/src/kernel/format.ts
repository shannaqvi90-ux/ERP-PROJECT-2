/**
 * Number, amount and date formatting for the screen's language and the user's digit choice.
 * Screens never call toLocaleString, toFixed or Intl directly (a gate checks it); they use these
 * through useI18n(), so every number on every screen follows the same rules:
 *
 *  - English screens: en-AE conventions, Latin digits (1,234.50; 3 Oct 2026).
 *  - Arabic screens: ar-AE conventions with the user's digits: Latin (1,234.50) or Arabic-Indic
 *    (١٬٢٣٤٫٥٠). Always the Gregorian calendar.
 *  - Amounts arrive from the API as decimal strings (never floating point, CLAUDE.md rule 2) and
 *    are formatted from the string, so no digit is ever rounded through a binary float.
 */
export type Language = "en" | "ar";
export type Numerals = "latn" | "arab";
export const numeralSystems: readonly Numerals[] = ["latn", "arab"];

export function isNumerals(value: unknown): value is Numerals {
  return value === "latn" || value === "arab";
}

/** The digits a screen uses: the user's choice on Arabic screens, Latin on English screens. */
export function effectiveNumerals(language: Language, numerals: Numerals): Numerals {
  return language === "ar" ? numerals : "latn";
}

/** BCP 47 locale for Intl: region AE, Gregorian calendar, explicit numbering system. */
export function intlLocale(language: Language, numerals: Numerals = "latn"): string {
  const base = language === "ar" ? "ar-AE" : "en-AE";
  return `${base}-u-ca-gregory-nu-${effectiveNumerals(language, numerals)}`;
}

const decimalPattern = /^-?\d+(\.\d+)?$/;

/** True for a plain decimal string such as "1234.5", "-0.25" or "12". */
export function isDecimalString(value: string): boolean {
  return decimalPattern.test(value.trim());
}

/** Digits after the decimal point of a decimal string ("12.50" -> 2). */
export function scaleOf(value: string): number {
  const dot = value.indexOf(".");
  return dot < 0 ? 0 : value.length - dot - 1;
}

/** Currency minor units (ISO 4217) for the currencies this market uses most; others use Intl's. */
const minorUnits: Record<string, number> = { AED: 2, USD: 2, EUR: 2, GBP: 2, SAR: 2, INR: 2, OMR: 3, KWD: 3, BHD: 3, JPY: 0 };

export function minorUnitsOf(currency: string): number {
  const known = minorUnits[currency.toUpperCase()];
  if (known !== undefined) return known;
  try {
    return new Intl.NumberFormat("en", { style: "currency", currency }).resolvedOptions().maximumFractionDigits ?? 2;
  } catch {
    return 2;
  }
}

export type Formatter = {
  locale: string;
  numerals: Numerals;
  /** An integer or a count: 100,004 / ١٠٠٬٠٠٤. */
  number: (value: number | bigint) => string;
  /** A decimal string at its own scale (or `scale` digits): "1234.5" -> 1,234.5. */
  decimal: (value: string, scale?: number) => string;
  /** A decimal-string amount with its ISO currency code, at the currency's minor units unless the amount carries more. */
  amount: (value: string, currency: string) => string;
  /** A ratio as a percentage: 0.05 -> 5%. */
  percent: (value: string, scale?: number) => string;
  date: (value: string | Date) => string;
  dateTime: (value: string | Date) => string;
  time: (value: string | Date) => string;
  /** Digits only (no grouping), for codes and reference numbers that must still follow the digit choice. */
  digits: (text: string) => string;
};

const toDate = (value: string | Date): Date => (typeof value === "string" ? new Date(value) : value);

function assertDecimal(value: string): string {
  const trimmed = value.trim();
  if (!decimalPattern.test(trimmed)) throw new RangeError(`Not a decimal string: "${value}"`);
  return trimmed;
}

const arabicIndic = "٠١٢٣٤٥٦٧٨٩";

export function createFormatter(language: Language, numerals: Numerals): Formatter {
  const locale = intlLocale(language, numerals);
  const shown = effectiveNumerals(language, numerals);
  const integer = new Intl.NumberFormat(locale, { maximumFractionDigits: 0 });
  const date = new Intl.DateTimeFormat(locale, { dateStyle: "medium" });
  const dateTime = new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short" });
  const time = new Intl.DateTimeFormat(locale, { timeStyle: "short" });
  // Intl.NumberFormat.format accepts decimal strings and keeps every digit (ECMA-402 "exact
  // decimal" formatting); no binary floating point is involved.
  const formatDecimalString = (value: string, options: Intl.NumberFormatOptions) =>
    new Intl.NumberFormat(locale, options).format(value as Intl.StringNumericLiteral);

  return {
    locale,
    numerals: shown,
    number: (value) => integer.format(value),
    decimal: (value, scale) => {
      const v = assertDecimal(value);
      const digits = scale ?? scaleOf(v);
      return formatDecimalString(v, { minimumFractionDigits: digits, maximumFractionDigits: digits, roundingMode: "halfExpand" });
    },
    amount: (value, currency) => {
      const v = assertDecimal(value);
      const digits = Math.max(minorUnitsOf(currency), scaleOf(v));
      return formatDecimalString(v, {
        style: "currency",
        currency: currency.toUpperCase(),
        currencyDisplay: "code",
        minimumFractionDigits: digits,
        maximumFractionDigits: digits,
      });
    },
    percent: (value, scale) => {
      const v = assertDecimal(value);
      const digits = scale ?? Math.max(0, scaleOf(v) - 2);
      return formatDecimalString(v, { style: "percent", minimumFractionDigits: digits, maximumFractionDigits: digits });
    },
    date: (value) => date.format(toDate(value)),
    dateTime: (value) => dateTime.format(toDate(value)),
    time: (value) => time.format(toDate(value)),
    digits: (text) => (shown === "arab" ? text.replace(/[0-9]/g, (d) => arabicIndic[Number(d)] ?? d) : text),
  };
}

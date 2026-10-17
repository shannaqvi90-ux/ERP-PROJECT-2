/**
 * Telephone numbers as people in the UAE type them. The field keeps the country code (+971) and
 * takes the number that follows it, grouped as it is read aloud: a mobile number 50 555 0199, a
 * fixed line 4 555 0100, a toll-free or shared-cost number 800 1234 / 600 522222. A number typed
 * with its trunk 0 (050 555 0199), with 00 or +971 in front, or pasted as 971505550199, is kept the
 * same way; a number of another country is typed with + and its code and kept as typed. Arabic-Indic
 * digits typed on an Arabic keyboard become 0 to 9.
 *
 * The grouping and the lengths below follow the UAE's numbering plan (ITU-T E.164 country code 971,
 * plan kept by the TDRA; docs/decisions/p06-form-report-phone-field.md): a mobile number is 5 and
 * eight more digits, a fixed-line number an area digit (2, 3, 4, 6, 7, 9) and seven more. Those two
 * kinds are the ones the field can call complete; any other number is kept as typed and never
 * judged complete, so nothing happens on its own for it.
 */

/** The country code the field keeps (the UAE). */
export const HOME_CODE = "971";
export const HOME_PREFIX = `+${HOME_CODE} `;

const ARABIC_INDIC = "٠١٢٣٤٥٦٧٨٩";
const EASTERN_ARABIC_INDIC = "۰۱۲۳۴۵۶۷۸۹";

/** 0 to 9 for the Arabic-Indic (and Persian) digits typed on an Arabic keyboard. */
export function latinDigits(text: string): string {
  return text.replace(/[٠-٩۰-۹]/g, (d) => String(ARABIC_INDIC.includes(d) ? ARABIC_INDIC.indexOf(d) : EASTERN_ARABIC_INDIC.indexOf(d)));
}

/** A UAE number after +971, grouped: 50 555 0199, 4 555 0100, 800 1234. */
export function groupNational(digits: string): string {
  const parts = (...cuts: number[]) => {
    const out: string[] = [];
    let at = 0;
    for (const cut of cuts) {
      if (digits.length <= at) break;
      out.push(digits.slice(at, cut));
      at = cut;
    }
    if (digits.length > at) out.push(digits.slice(at));
    return out.join(" ");
  };
  if (/^[68]00/.test(digits)) return parts(3);
  if (digits.startsWith("5")) return parts(2, 5);
  if (/^[234679]/.test(digits)) return parts(1, 4);
  return digits;
}

/** The digits after +971 of a stored UAE number, or null for another country's (or no) number. */
export function nationalDigits(value: string): string | null {
  return value.startsWith(HOME_PREFIX) ? value.slice(HOME_PREFIX.length).replace(/\D/g, "") : null;
}

/** A complete UAE mobile or fixed-line number (the two kinds whose length is fixed). */
export function isCompletePhone(value: string): boolean {
  const national = nationalDigits(value);
  return national !== null && (/^5\d{8}$/.test(national) || /^[234679][1-9]\d{6}$/.test(national));
}

/**
 * What the field holds after the user typed or pasted `raw` into it: "" for no number, "+971 "
 * and the grouped national number for a UAE number, "+" and the typed text for another country's.
 */
export function phoneValue(raw: string): string {
  const text = latinDigits(raw);
  const plus = text.lastIndexOf("+");
  let international: string | null = plus >= 0 ? text.slice(plus + 1) : null;
  let digits = (international ?? text).replace(/\D/g, "");
  if (international === null) {
    // No +: 00 and a country code, 971 and a whole number pasted without its +, or the national number.
    if (/^\s*00/.test(text)) {
      international = text.replace(/^\s*00/, "");
      digits = digits.slice(2);
    } else if (digits.startsWith(HOME_CODE) && digits.length >= HOME_CODE.length + 8) {
      international = text;
    }
  }
  if (international !== null) {
    if (digits.startsWith(HOME_CODE)) return national(digits.slice(HOME_CODE.length));
    // Still typing the code itself (+, +9, +97).
    if (HOME_CODE.startsWith(digits)) return "+" + digits;
    // Another country: kept as typed (digits, spaces, brackets and dashes).
    return ("+" + international.replace(/[^0-9 ()-]/g, "").replace(/\s+/g, " ").trimStart()).slice(0, 30);
  }
  return national(digits);
}

function national(digits: string): string {
  // The trunk 0 (050 ..., 04 ...) is not part of the number after +971.
  const number = digits.replace(/^0/, "").slice(0, 15);
  return number === "" ? "" : HOME_PREFIX + groupNational(number);
}

/** Where the caret goes in `shown` so that as many digits follow it as followed it in what was typed. */
export function caretAfter(shown: string, digitsAfter: number): number {
  if (digitsAfter <= 0) return shown.length;
  let seen = 0;
  for (let i = shown.length - 1; i >= 0; i--) {
    if (/\d/.test(shown[i]!)) {
      seen++;
      if (seen === digitsAfter) return i;
    }
  }
  return 0;
}

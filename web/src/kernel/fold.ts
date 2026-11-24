/**
 * Matching for the command palette. Every entry is matched against its text in both languages
 * (so "users" finds the Users screen on an Arabic screen and "المستخدمون" on an English one).
 * Text is compared after folding: lower case, Latin accents removed, Arabic short vowels and
 * tatweel removed, and the Arabic letter variants people type interchangeably unified
 * (أ إ آ ٱ → ا, ى → ي, ة → ه, ؤ → و, ئ → ي). Arabic-Indic digits match Latin digits.
 */
const arabicMarks = /[ؐ-ًؚ-ٰٟۖ-ۭـ]/g;
const latinMarks = /[̀-ͯ]/g;

export function fold(text: string): string {
  return text
    .normalize("NFKD")
    .replace(latinMarks, "")
    .replace(arabicMarks, "")
    .replace(/[أإآٱ]/g, "ا")
    .replace(/ى/g, "ي")
    .replace(/ة/g, "ه")
    .replace(/ؤ/g, "و")
    .replace(/ئ/g, "ي")
    .replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660))
    .replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0))
    .toLowerCase()
    .replace(/\s+/g, " ")
    .trim();
}


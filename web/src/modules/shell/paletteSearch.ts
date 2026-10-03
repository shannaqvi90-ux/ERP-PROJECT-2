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

const wordsOf = (text: string): string[] => text.split(/[\s\-_/·:.,()«»"“”]+/).filter(Boolean);

/** How well one query word matches one text (0 = not at all). */
function wordScore(word: string, text: string): number {
  if (text === word) return 100;
  if (text.startsWith(word)) return 80;
  const words = wordsOf(text);
  if (words.some((w) => w.startsWith(word))) return 60;
  if (text.includes(word)) return 30;
  // Initials: "ur" matches "user roles".
  if (word.length >= 2 && word.length <= words.length) {
    let i = 0;
    for (const w of words) if (i < word.length && w.startsWith(word[i]!)) i++;
    if (i === word.length) return 15;
  }
  return 0;
}

/** Score of an entry for a query: every query word must match one of the texts. 0 = no match. */
export function score(query: string, texts: string[]): number {
  const q = fold(query);
  if (!q) return 1;
  const folded = texts.map(fold).filter(Boolean);
  let total = 0;
  for (const word of q.split(" ")) {
    let best = 0;
    for (const text of folded) best = Math.max(best, wordScore(word, text));
    if (best === 0) return 0;
    total += best;
  }
  // The whole query at the start of a text beats the same words scattered.
  if (folded.some((t) => t.startsWith(q))) total += 50;
  return total;
}

export type Ranked<T> = { item: T; score: number };

/** Entries that match, best first; ties keep their original order. */
export function rank<T>(query: string, items: T[], texts: (item: T) => string[]): T[] {
  return items
    .map((item, index) => ({ item, index, score: score(query, texts(item)) }))
    .filter((r) => r.score > 0)
    .sort((a, b) => b.score - a.score || a.index - b.index)
    .map((r) => r.item);
}

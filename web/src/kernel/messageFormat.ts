/**
 * A small subset of ICU MessageFormat shared by every module's strings (the server uses the same
 * syntax, see Erp.Kernel.Localization.MessageFormat):
 *   {name}                                         placeholder
 *   {count, plural, =0 {…} one {# item} other {# items}}   plural; # is the formatted number
 * Plural categories come from the Unicode CLDR rules through Intl.PluralRules. English needs
 * one/other; Arabic needs zero/one/two/few/many/other (checked by the string gate).
 */
export type MessageParams = Record<string, string | number>;

export const pluralCategories: Record<"en" | "ar", readonly Intl.LDMLPluralRule[]> = {
  en: ["one", "other"],
  ar: ["zero", "one", "two", "few", "many", "other"],
};

function matching(text: string, open: number): number {
  let depth = 0;
  for (let i = open; i < text.length; i++) {
    if (text[i] === "{") depth++;
    else if (text[i] === "}" && --depth === 0) return i;
  }
  return -1;
}

/** Branches of a plural body: `one {…} other {…}`. */
export function branches(text: string): { selector: string; text: string }[] {
  const result: { selector: string; text: string }[] = [];
  let i = 0;
  while (i < text.length) {
    while (i < text.length && /\s/.test(text.charAt(i))) i++;
    const start = i;
    while (i < text.length && text.charAt(i) !== "{" && !/\s/.test(text.charAt(i))) i++;
    const selector = text.slice(start, i);
    while (i < text.length && /\s/.test(text.charAt(i))) i++;
    if (i >= text.length || text.charAt(i) !== "{") break;
    const end = matching(text, i);
    if (end < 0) break;
    result.push({ selector, text: text.slice(i + 1, end) });
    i = end + 1;
  }
  return result;
}

/** The plural messages in a text: their variable and the selectors they cover. */
export function plurals(message: string): { variable: string; selectors: string[] }[] {
  const found: { variable: string; selectors: string[] }[] = [];
  for (let i = 0; i < message.length; i++) {
    if (message[i] !== "{") continue;
    const end = matching(message, i);
    if (end < 0) break;
    const parts = message.slice(i + 1, end).split(",");
    if (parts.length >= 3 && parts[1]?.trim() === "plural") {
      const list = branches(parts.slice(2).join(","));
      found.push({ variable: (parts[0] ?? "").trim(), selectors: list.map((b) => b.selector) });
      for (const branch of list) found.push(...plurals(branch.text));
    }
    i = end;
  }
  return found;
}

export function formatMessage(message: string, locale: string, params: MessageParams = {}, pound?: string): string {
  const numberFormat = new Intl.NumberFormat(locale);
  const show = (value: string | number) => (typeof value === "number" ? numberFormat.format(value) : value);
  let out = "";
  let i = 0;
  while (i < message.length) {
    const c = message[i];
    if (c === "#" && pound !== undefined) {
      out += pound;
      i++;
      continue;
    }
    if (c !== "{") {
      out += c;
      i++;
      continue;
    }
    const end = matching(message, i);
    if (end < 0) {
      out += message.slice(i);
      break;
    }
    const body = message.slice(i + 1, end);
    const parts = body.split(",");
    if (parts.length >= 3 && parts[1]?.trim() === "plural") {
      const value = params[(parts[0] ?? "").trim()];
      const n = typeof value === "number" ? value : Number(String(value ?? "0").replace(/,/g, ""));
      const list = branches(parts.slice(2).join(","));
      const category = new Intl.PluralRules(locale).select(n);
      const chosen =
        list.find((b) => b.selector === `=${n}`) ?? list.find((b) => b.selector === category) ?? list.find((b) => b.selector === "other");
      out += formatMessage(chosen?.text ?? "", locale, params, show(value ?? n));
    } else {
      const name = body.trim();
      const value = params[name];
      out += value === undefined ? `{${body}}` : show(value);
    }
    i = end + 1;
  }
  return out;
}

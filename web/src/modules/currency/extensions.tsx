import "./currency.css";
import { api } from "../../kernel/api";
import type { ModuleExtensions } from "../../kernel/extensions";
import { createFormatter } from "../../kernel/format";
import { recordPath } from "../../kernel/router";
import { translate } from "../../kernel/i18n";
import type { Conversion, CurrencyRow, ListPage } from "./types";

/**
 * "1250 eur", "EUR 1,250.50" or "1250 eur 2026-09-30" typed in the command palette: an amount, a
 * currency code and optionally a date (yyyy-mm-dd). Null when the text is not that.
 */
export function parseConversion(query: string): { amount: string; currency: string; date: string | null } | null {
  const words = query.trim().replace(/[٠-٩]/g, (d) => String("٠١٢٣٤٥٦٧٨٩".indexOf(d))).split(/\s+/).filter(Boolean);
  if (words.length < 2 || words.length > 3) return null;
  let amount: string | null = null;
  let currency: string | null = null;
  let date: string | null = null;
  for (const word of words) {
    if (/^\d{4}-\d{2}-\d{2}$/.test(word) && date === null) date = word;
    else if (/^[a-z]{3}$/i.test(word) && currency === null) currency = word.toUpperCase();
    else if (/^-?[\d,]*\.?\d+$/.test(word) && amount === null && /\d/.test(word)) amount = word.replace(/,/g, "");
    else return null;
  }
  return amount !== null && currency !== null ? { amount, currency, date } : null;
}

/**
 * What the currency module adds to the shell: G then X opens exchange rates and G then Y the
 * currencies; from anywhere, a new rate or either list printed (the import is the rates screen's own
 * command, Alt+I there); and in
 * the command palette the workspace's currencies by code or name, and an amount converted to the
 * working company's base currency as it is typed ("1250 eur").
 */
export const extensions: ModuleExtensions = {
  actions: [
    { key: "currency.rates.new", labelKey: "currency.rate.new", keywordsKey: "currency.action.newRateKeywords", permission: "currency.rates.create", path: "/currency/rates", command: "currency.screen.new" },
    { key: "currency.rates.print", labelKey: "currency.action.printRates", keywordsKey: "currency.action.printKeywords", permission: "currency.rates.read", path: "/currency/rates", command: "lists.print" },
    { key: "currency.currencies.print", labelKey: "currency.action.printCurrencies", keywordsKey: "currency.action.printKeywords", permission: "currency.currencies.read", path: "/currency/currencies", command: "lists.print" },
  ],
  goTo: [
    { key: "currency.rates", menu: "currency.rates", code: "KeyX" },
    { key: "currency.currencies", menu: "currency.currencies", code: "KeyY" },
  ],
  palette: [
    {
      key: "currency.convert",
      labelKey: "currency.palette.convert",
      permission: "currency.rates.read",
      minLength: 4,
      search: async (query, { language, signal }) => {
        const asked = parseConversion(query);
        if (!asked) return [];
        const params = new URLSearchParams({ amount: asked.amount, currency: asked.currency });
        if (asked.date) params.set("date", asked.date);
        let answer: Conversion;
        try {
          answer = await api<Conversion>("GET", `/api/currency/convert?${params}`, undefined, { signal });
        } catch {
          return [];
        }
        // The screen's digits (Latin or Arabic-Indic on Arabic screens), as the shell applied them.
        const f = createFormatter(language, document.documentElement.dataset.numerals === "arab" ? "arab" : "latn");
        const [y, m, d] = answer.rateDate.split("-").map((part) => Number.parseInt(part, 10));
        return [
          {
            id: `currency.convert:${answer.money.currency}:${answer.money.amount}:${answer.requestedDate}`,
            title: `${f.amount(answer.money.amount, answer.money.currency)} = ${f.amount(answer.money.baseAmount, answer.money.baseCurrency)}`,
            subtitle: translate(language, answer.exact ? "currency.convert.rateOf" : "currency.convert.rateBefore", {
              rate: f.decimal(answer.money.exchangeRate),
              quote: answer.money.baseCurrency,
              currency: answer.money.currency,
              date: y && m && d ? f.date(new Date(y, m - 1, d)) : answer.rateDate,
            }),
            ltr: true,
            path: answer.rateId ? recordPath("/currency/rates", answer.rateId) : undefined,
          },
        ];
      },
    },
    {
      key: "currency.currencies",
      labelKey: "currency.menu.currencies",
      permission: "currency.currencies.read",
      minLength: 2,
      search: async (query, { language, signal }) => {
        const params = new URLSearchParams({ search: query, take: "5" });
        const page = await api<ListPage<CurrencyRow>>("GET", `/api/currency/currencies?${params}`, undefined, { signal });
        return {
          total: page.total,
          items: page.items.map((c) => ({
            id: c.id,
            title: language === "ar" ? c.nameAr || c.nameEn : c.nameEn || c.nameAr,
            subtitle: c.code,
            path: recordPath("/currency/currencies", c.id, `${new URLSearchParams({ q: c.code })}`),
          })),
        };
      },
      showAll: (query) => `/currency/currencies?${new URLSearchParams({ q: query })}`,
    },
  ],
};

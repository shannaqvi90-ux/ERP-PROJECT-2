/** API shapes of the currency module (decimals travel as strings, CLAUDE.md rule 2). */

export type CurrencyRow = {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  minorUnits: number;
  isActive: boolean;
  maxRateAgeDays: number;
  quoteCurrency: string;
  latestRate: string | null;
  latestRateDate: string | null;
  version: number;
};

export type Currency = CurrencyRow & {
  number: number;
  isBaseCurrency: boolean;
  createdAt: string;
  updatedAt: string;
  everyCompany: boolean;
};

export type CatalogCurrency = { code: string; number: number; minorUnits: number; nameEn: string; enabled: boolean };
export type Catalog = { published: string; currencies: CatalogCurrency[] };

export type RateRow = {
  id: string;
  rateDate: string;
  currency: string;
  rate: string;
  quoteCurrency: string;
  inverseRate: string;
  source: "manual" | "import";
  reference: string | null;
  updatedAt: string;
  version: number;
};

export type Rate = RateRow & { createdAt: string; everyCompany: boolean };

export type Money = { amount: string; currency: string; exchangeRate: string; baseAmount: string; baseCurrency: string };

export type Conversion = {
  money: Money;
  exactBaseAmount: string;
  inverseRate: string;
  requestedDate: string;
  rateDate: string;
  exact: boolean;
  source: string;
  rateId: string | null;
  companyId: string;
};

export type ImportResult = { lines: number; added: number; replaced: number; kept: number; unchanged: number };

export type ListPage<T> = { items: T[]; total: number; next?: string | null };

/** Sent when rates or currencies change, so open screens and panels read them again. */
export const ratesChanged = "currency:rates-changed";

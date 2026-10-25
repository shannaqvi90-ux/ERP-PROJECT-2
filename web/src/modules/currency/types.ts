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
  latestRateReference: string | null;
  version: number;
  /** The caller works in every company and branch of the workspace (the same on every row). */
  everyCompany: boolean;
};

export type Currency = CurrencyRow & {
  number: number;
  isBaseCurrency: boolean;
  createdAt: string;
  updatedAt: string;
  isoPublished: string;
};

/** A row of the exchange rates list (its keys are the list's column keys). */
export type RateRow = {
  id: string;
  rateDate: string;
  currencyCode: string;
  exchangeRate: string;
  quoteCurrency: string;
  inverseRate: string;
  source: "manual" | "import";
  reference: string | null;
  updatedAt: string;
  version: number;
};

/** An exchange rate as its form reads and sends it. */
export type Rate = {
  id: string;
  rateDate: string;
  currency: string;
  rate: string;
  quoteCurrency: string;
  inverseRate: string;
  source: "manual" | "import";
  reference: string | null;
  createdAt: string;
  updatedAt: string;
  version: number;
  everyCompany: boolean;
};

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

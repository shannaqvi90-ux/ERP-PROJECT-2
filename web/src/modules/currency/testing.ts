/** Records the currency screens' tests serve (API shapes as the server answers them). */
export const euroRow = () => ({
  id: "c1", code: "EUR", nameEn: "Euro", nameAr: "يورو", minorUnits: 2, isActive: true, maxRateAgeDays: 7, quoteCurrency: "AED",
  latestRate: "4.2875", latestRateDate: "2026-10-23", version: 4,
});
export const euro = () => ({ ...euroRow(), number: 978, isBaseCurrency: false, createdAt: "2026-10-01T00:00:00Z", updatedAt: "2026-10-01T00:00:00Z", everyCompany: true, isoPublished: "2026-09-17" });
export const rateRow = () => ({
  id: "r1", rateDate: "2026-10-23", currencyCode: "EUR", exchangeRate: "4.2875", quoteCurrency: "AED", inverseRate: "0.23323615", source: "import",
  reference: "CBUAE 23 Oct 2026", updatedAt: "2026-10-23T08:00:00Z", version: 2,
});
export const rate = () => ({
  id: "r1", rateDate: "2026-10-23", currency: "EUR", rate: "4.2875", quoteCurrency: "AED", inverseRate: "0.23323615", source: "import",
  reference: "CBUAE 23 Oct 2026", createdAt: "2026-10-23T08:00:00Z", updatedAt: "2026-10-23T08:00:00Z", version: 2, everyCompany: true,
});
export const conversion = () => ({
  money: { amount: "1250", currency: "EUR", exchangeRate: "4.2875", baseAmount: "5359.38", baseCurrency: "AED" },
  exactBaseAmount: "5359.375", inverseRate: "0.23323615", requestedDate: "2026-10-24", rateDate: "2026-10-23", exact: false, source: "import", rateId: "r1", companyId: "c9",
});

import { describe, expect, it } from "vitest";
import { createFormatter } from "../format";
import { formatValue } from "./format";
import type { ListColumn } from "./model";

const column = (decimals?: number): ListColumn => ({
  key: "cfCreditLimit", labelKey: "x", type: "number", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: [], decimals,
});

function formatters() {
  const f = createFormatter("en", "latn");
  return { t: (k: string) => k, formatDateTime: (v: string | Date) => String(v), formatDate: (v: string | Date) => String(v), formatNumber: (v: number) => f.number(v), formatDecimal: (v: string, s?: number) => f.decimal(v, s) };
}

describe("a custom number column shows the decimals it was declared with", () => {
  it("keeps two decimals in the list", () => {
    expect(formatValue(column(2), "1234.56", formatters())).toBe("1,234.56");
    expect(formatValue(column(2), "1234.50", formatters())).toBe("1,234.50");
  });
  it("is unchanged for a column that declares none", () => {
    expect(formatValue(column(), 1234, formatters())).toBe("1,234");
  });
});

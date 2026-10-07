import { describe, expect, it } from "vitest";
import { groupTotal, type Formatters } from "./format";
import type { ListColumn, ListGroup } from "./model";

const f: Formatters = {
  t: (key) => key,
  formatDateTime: (v) => String(v),
  formatDate: (v) => String(v),
  formatNumber: (v) => String(v),
  formatDecimal: (v, scale) => `${v}@${scale}`,
};

const amount: ListColumn = {
  key: "amount",
  labelKey: "amount",
  type: "money",
  sortable: true,
  filterable: true,
  groupable: false,
  aggregate: true,
  hidden: false,
  choices: [],
  operators: [],
  currencyField: "currency",
};

const quantity: ListColumn = { ...amount, key: "quantity", type: "number", currencyField: null };

describe("group totals", () => {
  it("shows a money column's total in each currency, never one sum across currencies", () => {
    const group: ListGroup = {
      key: "open",
      count: 3,
      totals: null,
      moneyTotals: { amount: [{ currency: "AED", amount: "1250.00" }, { currency: "USD", amount: "40.5" }] },
    };
    expect(groupTotal(amount, group, f)).toBe("AED 1250.00@2 · USD 40.5@2");
  });

  it("shows zero for a group without amounts, and a number column's plain sum", () => {
    const group: ListGroup = { key: null, count: 0, totals: { quantity: "7" }, moneyTotals: { amount: [] } };
    expect(groupTotal(amount, group, f)).toBe("0@2");
    expect(groupTotal(quantity, group, f)).toBe("7");
  });
});

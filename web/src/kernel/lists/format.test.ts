import { describe, expect, it } from "vitest";
import { conditionLabel, formatValue, groupTotal, type Formatters } from "./format";
import type { ListColumn, ListDefinition, ListGroup } from "./model";

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

describe("boolean values", () => {
  const status: ListColumn = { ...amount, key: "isActive", labelKey: "status", type: "boolean", aggregate: false, groupable: true, currencyField: null, trueLabelKey: "active", falseLabelKey: "inactive" };
  const flag: ListColumn = { ...status, key: "flag", trueLabelKey: null, falseLabelKey: null };

  it("are called what the column calls them in cells, groups and filter chips (a status is Active, not Yes)", () => {
    expect(formatValue(status, true, f)).toBe("active");
    expect(formatValue(status, false, f)).toBe("inactive");
    const definition = { columns: [status] } as unknown as ListDefinition;
    expect(conditionLabel(definition, { column: "isActive", op: "eq", values: [true] }, f)).toBe("status lists.op.eq active");
  });

  it("are Yes and No when the column names no words", () => {
    expect(formatValue(flag, true, f)).toBe("lists.yes");
    expect(formatValue(flag, false, f)).toBe("lists.no");
  });
});

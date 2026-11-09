import { describe, expect, it } from "vitest";
import { createFormatter } from "../../kernel/format";
import { fieldDecimals, fieldLabel, valueText, type ValueFormat } from "./model";

const fields = { "custom_fields.cfCreditLimit": { labelEn: "Credit review limit", labelAr: "حد مراجعة الائتمان", type: "number", decimals: 2 } };

function format(language: "en" | "ar", withFields = true): ValueFormat {
  const f = createFormatter(language, "latn");
  return {
    t: (key) => key,
    formatDateTime: (v) => v,
    formatNumber: (v) => f.number(v),
    formatDecimal: (v, scale) => f.decimal(v, scale),
    language,
    fields: withFields ? fields : undefined,
  };
}

describe("the trail names a custom field by the tenant's own label", () => {
  it("in the screen's language, never as custom_fields.<key>", () => {
    expect(fieldLabel("custom_fields.cfCreditLimit", format("en"))).toBe("Credit review limit");
    expect(fieldLabel("custom_fields.cfCreditLimit", format("ar"))).toBe("حد مراجعة الائتمان");
  });
  it("falls back to the stored name for a field it was not told about", () => {
    expect(fieldLabel("custom_fields.cfOther", format("en"))).toBe("custom_fields.cfOther");
  });
  it("knows a number field's declared decimals", () => {
    expect(fieldDecimals("custom_fields.cfCreditLimit", format("en"))).toBe(2);
    expect(fieldDecimals("name", format("en"))).toBeUndefined();
  });
});

describe("a recorded number keeps its fraction", () => {
  it("shows the declared decimals", () => {
    expect(valueText(1234.5, format("en"), 2)).toBe("1,234.50");
    expect(valueText(1234.56, format("en"), 2)).toBe("1,234.56");
  });
  it("shows a native number's own fraction instead of rounding it to a whole number", () => {
    expect(valueText(25000.5, format("en"))).toBe("25,000.5");
  });
  it("leaves a whole number alone", () => {
    expect(valueText(12, format("en"))).toBe("12");
  });
});

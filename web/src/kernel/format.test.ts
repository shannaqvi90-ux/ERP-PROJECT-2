import { describe, expect, it } from "vitest";
import { createFormatter, effectiveNumerals, intlLocale, isDecimalString, minorUnitsOf, scaleOf } from "./format";

describe("number and date formatting per language and digit choice", () => {
  it("uses Latin digits on English screens whatever the choice, the user's choice on Arabic screens", () => {
    expect(effectiveNumerals("en", "arab")).toBe("latn");
    expect(effectiveNumerals("ar", "arab")).toBe("arab");
    expect(intlLocale("ar", "arab")).toBe("ar-AE-u-ca-gregory-nu-arab");
    expect(intlLocale("en", "arab")).toBe("en-AE-u-ca-gregory-nu-latn");
  });

  it("formats counts with grouping in each digit system", () => {
    expect(createFormatter("en", "latn").number(100004)).toBe("100,004");
    expect(createFormatter("ar", "latn").number(100004)).toBe("100,004");
    expect(createFormatter("ar", "arab").number(100004)).toBe("١٠٠٬٠٠٤");
    expect(createFormatter("en", "arab").number(100004)).toBe("100,004");
  });

  it("formats decimal strings without passing through binary floating point", () => {
    const en = createFormatter("en", "latn");
    // 2^53 + 1 and 18 significant digits: a float would round both.
    expect(en.decimal("9007199254740993")).toBe("9,007,199,254,740,993");
    expect(en.decimal("12345678901234567.89")).toBe("12,345,678,901,234,567.89");
    expect(en.decimal("0.1", 4)).toBe("0.1000");
    expect(en.decimal("-1234.5")).toBe("-1,234.5");
    expect(createFormatter("ar", "arab").decimal("1234.50")).toBe("١٬٢٣٤٫٥٠");
    expect(() => en.decimal("1e3")).toThrow(RangeError);
    expect(() => en.decimal("12,5")).toThrow(RangeError);
  });

  it("shows an amount with its currency at the currency's minor units, keeping extra precision", () => {
    const en = createFormatter("en", "latn");
    expect(en.amount("1234.5", "AED")).toMatch(/^AED\s1,234\.50$/);
    expect(en.amount("10", "KWD")).toMatch(/^KWD\s10\.000$/);
    expect(en.amount("1.23456", "USD")).toMatch(/^USD\s1\.23456$/);
    const ar = createFormatter("ar", "arab").amount("1234.5", "AED");
    expect(ar).toContain("١٬٢٣٤٫٥٠");
    expect(ar).toContain("AED");
    expect(minorUnitsOf("aed")).toBe(2);
    expect(minorUnitsOf("OMR")).toBe(3);
  });

  it("formats percentages from decimal ratios", () => {
    expect(createFormatter("en", "latn").percent("0.05")).toBe("5%");
    expect(createFormatter("en", "latn").percent("0.125")).toBe("12.5%");
  });

  it("writes dates in the Gregorian calendar in both languages", () => {
    const when = new Date(Date.UTC(2026, 9, 3, 6, 0));
    expect(createFormatter("en", "latn").date(when)).toContain("2026");
    const arab = createFormatter("ar", "arab").date(when);
    expect(arab).toContain("٢٠٢٦");
    expect(arab).not.toMatch(/[0-9]/);
    const latn = createFormatter("ar", "latn").date(when);
    expect(latn).toContain("2026");
    expect(latn).not.toMatch(/[٠-٩]/);
    // Not the Hijri calendar.
    expect(latn).not.toContain("1448");
  });

  it("shapes digits of codes and references", () => {
    expect(createFormatter("ar", "arab").digits("INV-2026-0042")).toBe("INV-٢٠٢٦-٠٠٤٢");
    expect(createFormatter("ar", "latn").digits("INV-2026-0042")).toBe("INV-2026-0042");
  });

  it("recognises decimal strings and their scale", () => {
    expect(isDecimalString("12.50")).toBe(true);
    expect(isDecimalString("NaN")).toBe(false);
    expect(scaleOf("12.50")).toBe(2);
    expect(scaleOf("12")).toBe(0);
  });
});

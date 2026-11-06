import { describe, expect, it } from "vitest";
import { optionsText, parseOptions } from "./model";

const existing = [
  { value: "retail", labelEn: "Retail", labelAr: "تجزئة" },
  { value: "wholesale", labelEn: "Wholesale", labelAr: "جملة" },
];

describe("select options typed one per line", () => {
  it("reads English | Arabic per line and keeps the value of an option whose English label is unchanged", () => {
    const { options, bad } = parseOptions("Wholesale | جملة\nGovernment | حكومي", existing);
    expect(bad).toBe(false);
    expect(options).toEqual([
      { value: "wholesale", labelEn: "Wholesale", labelAr: "جملة" },
      { value: null, labelEn: "Government", labelAr: "حكومي" },
    ]);
  });

  it("matches by place when a label is edited and keeps blank lines out", () => {
    const { options } = parseOptions("Retail shops | تجزئة\n\nWholesale | جملة\n", existing);
    expect(options.map((o) => o.value)).toEqual(["retail", "wholesale"]);
  });

  it("flags a line missing its Arabic or English label", () => {
    expect(parseOptions("Retail", existing).bad).toBe(true);
    expect(parseOptions("| تجزئة", existing).bad).toBe(true);
  });

  it("writes the options back as the lines the form edits", () => {
    expect(optionsText(existing)).toBe("Retail | تجزئة\nWholesale | جملة");
    expect(parseOptions(optionsText(existing), existing).options.map((o) => o.value)).toEqual(["retail", "wholesale"]);
  });
});

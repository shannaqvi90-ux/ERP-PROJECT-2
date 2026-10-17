import { describe, expect, it } from "vitest";
import { trnDigits } from "./trn";

describe("the TRN as typed", () => {
  it("turns the Arabic-Indic digits of an Arabic keyboard into 0 to 9", () => {
    expect(trnDigits("١٠٠١٢٣٤٥٦٧٨٩٠٠٣")).toBe("100123456789003");
  });

  it("keeps 0 to 9, spaces and dashes as typed, mixed with Arabic-Indic digits", () => {
    expect(trnDigits("١٠٠-1234 ٥٦٧٨-9003")).toBe("100-1234 5678-9003");
  });

  it("leaves every other digit set as typed, for the server to refuse", () => {
    expect(trnDigits("۱۰۰۱۲۳۴۵۶۷۸۹۰۰۳")).toBe("۱۰۰۱۲۳۴۵۶۷۸۹۰۰۳");
    expect(trnDigits("１００")).toBe("１００");
  });
});

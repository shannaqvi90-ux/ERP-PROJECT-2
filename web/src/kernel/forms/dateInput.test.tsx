import { describe, expect, it } from "vitest";
import { displayDate, parseDate } from "./dateInput";

describe("day-first dates", () => {
  it("reads dd/mm/yyyy and yyyy-mm-dd, in either set of digits, as yyyy-mm-dd", () => {
    expect(parseDate("18/11/2026")).toBe("2026-11-18");
    expect(parseDate("8/1/2026")).toBe("2026-01-08");
    expect(parseDate("18-11-2026")).toBe("2026-11-18");
    expect(parseDate("18.11.2026")).toBe("2026-11-18");
    expect(parseDate("2026-11-18")).toBe("2026-11-18");
    expect(parseDate("١٨/١١/٢٠٢٦")).toBe("2026-11-18");
  });

  it("refuses what is not a real date, and never reads month first", () => {
    expect(parseDate("31/02/2026")).toBeNull();
    expect(parseDate("11/18/2026")).toBeNull();
    expect(parseDate("2026-13-01")).toBeNull();
    expect(parseDate("18/11")).toBeNull();
    expect(parseDate("")).toBeNull();
    // 03/04/2026 is the third of April, not the fourth of March.
    expect(parseDate("03/04/2026")).toBe("2026-04-03");
  });

  it("shows a held date day first and leaves other text as typed", () => {
    expect(displayDate("2026-11-18")).toBe("18/11/2026");
    expect(displayDate("18/1")).toBe("18/1");
    expect(displayDate("")).toBe("");
  });
});

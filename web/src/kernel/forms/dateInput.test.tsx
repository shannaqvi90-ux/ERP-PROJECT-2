import { describe, expect, it } from "vitest";
import { displayDate, parseDate, typedDate } from "./dateInput";

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

describe("typing a date keystroke by keystroke", () => {
  // The box holds at most ten characters (maxLength): a longer value is cut before the page sees it.
  const type = (keys: string): string => {
    let draft: string | null = null;
    let shown = "";
    for (const key of keys) {
      const next = typedDate(draft, (shown + key).slice(0, 10));
      draft = next;
      shown = next;
    }
    return shown;
  };

  it("takes a date typed the way the placeholder shows it", () => {
    expect(type("15/06/2027")).toBe("15/06/2027");
    expect(parseDate(type("15/06/2027"))).toBe("2027-06-15");
  });

  it("takes bare digits, and a year first", () => {
    expect(type("15062027")).toBe("15/06/2027");
    expect(type("2027-06-15")).toBe("2027-06-15");
    expect(parseDate(type("2027-06-15"))).toBe("2027-06-15");
  });

  it("takes a dot or a dash as the separator (the slash that came by itself stands)", () => {
    expect(type("15.06.2027")).toBe("15/06/2027");
    expect(type("15-06-2027")).toBe("15/06/2027");
  });
});

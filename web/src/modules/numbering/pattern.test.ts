import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { checkPattern, formatNumber, nextStart, periodOf } from "./pattern";

// The server's examples (tests/Erp.Modules.Numbering.Tests/pattern-examples.json, which the server's
// own tests run too): the screen previews exactly the numbers the server gives and refuses exactly
// the patterns it refuses.
type Examples = {
  formats: { prefix: string; suffix: string; padding: number; date: string; branch: string | null; value: number; number: string }[];
  periods: { reset: string; date: string; period: string }[];
  checks: { prefix: string; suffix: string; padding: number; reset: string; perBranch: boolean; problems: [string, string][] }[];
};
const examples = JSON.parse(readFileSync(resolve(import.meta.dirname, "../../../../tests/Erp.Modules.Numbering.Tests/pattern-examples.json"), "utf8")) as Examples;

describe("document number patterns, as the server makes them", () => {
  it("formats every shared example to its number", () => {
    expect(examples.formats.length).toBeGreaterThanOrEqual(7);
    for (const e of examples.formats) expect(formatNumber(e.prefix, e.suffix, e.padding, e.date, e.branch, e.value), JSON.stringify(e)).toBe(e.number);
  });

  it("puts every shared example in its period", () => {
    for (const e of examples.periods) expect(periodOf(e.reset, e.date)).toBe(e.period);
  });

  it("finds exactly the problems the server finds", () => {
    expect(examples.checks.length).toBeGreaterThanOrEqual(15);
    for (const e of examples.checks) {
      expect(checkPattern(e.prefix, e.suffix, e.padding, e.reset, e.perBranch).map((p) => [p.field, p.code]), JSON.stringify(e)).toEqual(e.problems);
    }
  });

  it("says when the numbers next start again", () => {
    expect(nextStart("yearly", "2026-10-25")).toBe("2027-01-01");
    expect(nextStart("monthly", "2026-10-25")).toBe("2026-11-01");
    expect(nextStart("monthly", "2026-12-31")).toBe("2027-01-01");
    expect(nextStart("never", "2026-10-25")).toBeNull();
  });
});

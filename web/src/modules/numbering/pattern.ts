/**
 * Document number patterns, exactly as the server makes and checks them (NumberPattern in
 * src/Modules/Numbering/Erp.Modules.Numbering.Contracts): a prefix, the counter with leading zeros
 * to a number of digits, a suffix; prefix and suffix hold letters A to Z, digits, - / . _ # and the
 * tokens {YYYY}, {YY}, {MM} and {BRANCH}. The screen previews numbers with it as they are typed; the
 * shared examples in tests/Erp.Modules.Numbering.Tests/pattern-examples.json hold both sides equal.
 */

export type Reset = "never" | "yearly" | "monthly";

export const resets: readonly Reset[] = ["yearly", "monthly", "never"];

export const tokens = ["{YYYY}", "{YY}", "{MM}", "{BRANCH}"] as const;

export const maxAffixLength = 30;
export const minPadding = 1;
export const maxPadding = 12;

const tokenPattern = /\{(?:YYYY|YY|MM|BRANCH)\}/g;
const literalPattern = /^[A-Za-z0-9\-/._#]*$/;

/** A date as yyyy-mm-dd, read without time zones. */
function parts(date: string): { year: number; month: number } {
  const [year, month] = date.split("-").map((p) => Number.parseInt(p, 10));
  return { year: year!, month: month! };
}

function expand(text: string, date: string, branch: string | null): string {
  const { year, month } = parts(date);
  return text.replace(tokenPattern, (token) => {
    switch (token) {
      case "{YYYY}":
        return String(year).padStart(4, "0");
      case "{YY}":
        return String(year % 100).padStart(2, "0");
      case "{MM}":
        return String(month).padStart(2, "0");
      default:
        return branch ?? "";
    }
  });
}

/** The number as printed. */
export function formatNumber(prefix: string, suffix: string, padding: number, date: string, branch: string | null, value: number): string {
  const digits = Math.min(Math.max(Number.isFinite(padding) ? padding : minPadding, minPadding), maxPadding);
  return expand(prefix, date, branch) + String(value).padStart(digits, "0") + expand(suffix, date, branch);
}

/** The period a date is counted in: "" (never starts again), "yyyy" or "yyyy-mm". */
export function periodOf(reset: Reset | string, date: string): string {
  const { year, month } = parts(date);
  if (reset === "yearly") return String(year).padStart(4, "0");
  if (reset === "monthly") return `${String(year).padStart(4, "0")}-${String(month).padStart(2, "0")}`;
  return "";
}

/** The first day of the next period (when the numbers next start again), or null for never. */
export function nextStart(reset: Reset | string, date: string): string | null {
  const { year, month } = parts(date);
  if (reset === "yearly") return `${String(year + 1).padStart(4, "0")}-01-01`;
  if (reset === "monthly") return month === 12 ? `${String(year + 1).padStart(4, "0")}-01-01` : `${String(year).padStart(4, "0")}-${String(month + 1).padStart(2, "0")}-01`;
  return null;
}

export type PatternProblem = { field: "prefix" | "suffix" | "padding" | "reset" | "perBranch"; code: string; args?: (string | number)[] };

function checkAffix(problems: PatternProblem[], field: "prefix" | "suffix", text: string) {
  if (text.length > maxAffixLength) {
    problems.push({ field, code: "maxLength", args: [maxAffixLength] });
    return;
  }
  const literal = text.replace(tokenPattern, "");
  if (literal.includes("{") || literal.includes("}")) {
    problems.push({ field, code: "numberingToken" });
    return;
  }
  if (!literalPattern.test(literal)) problems.push({ field, code: "numberingCharacters" });
}

/** Every problem with a pattern, in the server's order. */
export function checkPattern(prefix: string, suffix: string, padding: number | null, reset: string, perBranch: boolean): PatternProblem[] {
  const problems: PatternProblem[] = [];
  checkAffix(problems, "prefix", prefix);
  checkAffix(problems, "suffix", suffix);
  if (padding === null || !Number.isInteger(padding) || padding < minPadding || padding > maxPadding) {
    problems.push({ field: "padding", code: "numberingPadding", args: [minPadding, maxPadding] });
  }
  if (!(resets as readonly string[]).includes(reset)) problems.push({ field: "reset", code: "numberingReset" });
  const text = prefix + suffix;
  const hasYear = text.includes("{YYYY}") || text.includes("{YY}");
  if (reset === "yearly" && !hasYear) problems.push({ field: "reset", code: "numberingYearlyNeedsYear" });
  if (reset === "monthly" && (!hasYear || !text.includes("{MM}"))) problems.push({ field: "reset", code: "numberingMonthlyNeedsMonth" });
  if (perBranch && !text.includes("{BRANCH}")) problems.push({ field: "perBranch", code: "numberingPerBranchNeedsBranch" });
  if (!perBranch && text.includes("{BRANCH}")) problems.push({ field: "perBranch", code: "numberingBranchNeedsPerBranch" });
  return problems;
}

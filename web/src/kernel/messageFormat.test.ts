import { describe, expect, it } from "vitest";
import { catalog } from "./i18n";
import { formatMessage, pluralCategories, plurals } from "./messageFormat";

describe("message format", () => {
  it("fills placeholders and leaves unknown ones visible", () => {
    expect(formatMessage("{from}–{to} of {total}", "en-AE", { from: 1, to: 50, total: 1234 })).toBe("1–50 of 1,234");
    expect(formatMessage("Hello {name}", "en-AE", {})).toBe("Hello {name}");
  });

  it("prefers an exact =N branch over the category", () => {
    const text = "{n, plural, =0 {none} one {# file} other {# files}}";
    expect(formatMessage(text, "en-AE", { n: 0 })).toBe("none");
    expect(formatMessage(text, "en-AE", { n: 1 })).toBe("1 file");
    expect(formatMessage(text, "en-AE", { n: 7 })).toBe("7 files");
  });

  it("supports placeholders inside plural branches", () => {
    expect(formatMessage("{n, plural, one {# row in {table}} other {# rows in {table}}}", "en-AE", { n: 2, table: "users" })).toBe(
      "2 rows in users",
    );
  });

  it("finds every plural message with its selectors", () => {
    expect(plurals("a {n, plural, one {x} other {y {m, plural, one {p} other {q}}}}")).toEqual([
      { variable: "n", selectors: ["one", "other"] },
      { variable: "m", selectors: ["one", "other"] },
    ]);
  });

  it("every plural message in the catalogue covers its language's categories", () => {
    for (const language of ["en", "ar"] as const) {
      for (const [key, text] of Object.entries(catalog[language])) {
        for (const plural of plurals(text)) {
          for (const category of pluralCategories[language]) {
            expect(plural.selectors, `${language} ${key}`).toContain(category);
          }
        }
      }
    }
  });
});

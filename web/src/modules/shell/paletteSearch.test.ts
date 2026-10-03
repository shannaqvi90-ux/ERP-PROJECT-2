import { describe, expect, it } from "vitest";
import { fold, rank, score } from "./paletteSearch";

describe("command palette matching", () => {
  it("folds Arabic letter variants, short vowels, tatweel and Arabic-Indic digits", () => {
    expect(fold("الأدوار")).toBe(fold("الادوار"));
    expect(fold("مُستخدِم")).toBe("مستخدم");
    expect(fold("مـسـتـخـدم")).toBe("مستخدم");
    expect(fold("فاتورة")).toBe(fold("فاتوره"));
    expect(fold("مستشفى")).toBe(fold("مستشفي"));
    expect(fold("٢٠٢٦")).toBe("2026");
    expect(fold("Café")).toBe("cafe");
  });

  it("matches the start of a name before a match inside it", () => {
    const items = ["Workspace roles", "Roles", "Payroll"];
    expect(rank("rol", items, (i) => [i])).toEqual(["Roles", "Workspace roles", "Payroll"]);
  });

  it("finds an entry by its text in either language and by initials", () => {
    const users = { en: "Users", ar: "المستخدمون" };
    expect(score("المستخدم", [users.en, users.ar])).toBeGreaterThan(0);
    expect(score("user", [users.en, users.ar])).toBeGreaterThan(0);
    expect(score("ws", ["Work space"])).toBeGreaterThan(0);
    expect(score("zzz", [users.en, users.ar])).toBe(0);
  });

  it("needs every word of the query", () => {
    expect(score("switch arabic", ["Switch the interface to Arabic"])).toBeGreaterThan(0);
    expect(score("switch french", ["Switch the interface to Arabic"])).toBe(0);
  });
});

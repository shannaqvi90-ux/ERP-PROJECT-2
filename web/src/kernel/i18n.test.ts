import { describe, expect, it } from "vitest";
import { applyLanguage, buildCatalog, catalog, direction, documentTitle, translate } from "./i18n";

describe("strings", () => {
  it("has the same keys in English and Arabic", () => {
    expect(Object.keys(catalog.ar).sort()).toEqual(Object.keys(catalog.en).sort());
    expect(Object.keys(catalog.en).length).toBeGreaterThan(20);
  });

  it("fills placeholders and shows unknown keys as themselves", () => {
    expect(translate("en", "shell.home.welcome", { name: "Mariam" })).toBe("Welcome, Mariam");
    expect(translate("ar", "shell.home.welcome", { name: "مريم" })).toBe("مرحبًا، مريم");
    expect(translate("en", "no.such.key")).toBe("no.such.key");
  });

  it("chooses English and Arabic plural forms (CLDR categories)", () => {
    const en = [0, 1, 2, 100000].map((count) => translate("en", "identity.users.count", { count }));
    expect(en).toEqual(["0 users", "1 user", "2 users", "100,000 users"]);
    const ar = [0, 1, 2, 3, 10, 11, 99, 100, 102].map((count) => translate("ar", "identity.users.count", { count }));
    expect(ar).toEqual([
      "لا يوجد مستخدمون",
      "مستخدم واحد",
      "مستخدمان",
      "3 مستخدمين",
      "10 مستخدمين",
      "11 مستخدمًا",
      "99 مستخدمًا",
      "100 مستخدم",
      "102 مستخدم",
    ]);
  });

  it("refuses a key defined twice", () => {
    expect(() =>
      buildCatalog({
        "../modules/a/i18n/en.json": { default: { "a.x": "1" } },
        "../modules/b/i18n/en.json": { default: { "a.x": "2" } },
      }),
    ).toThrow(/twice/);
  });

  it("writes Arabic right to left", () => {
    expect(direction("ar")).toBe("rtl");
    expect(direction("en")).toBe("ltr");
  });
});

describe("document title", () => {
  it("names the screen, then the product, in the screen language", () => {
    expect(documentTitle("en", "shell.home.title")).toBe("Home · ERP");
    expect(documentTitle("ar", "shell.home.title")).toBe("الرئيسية · نظام تخطيط الموارد");
    expect(documentTitle("ar", null)).toBe("نظام تخطيط الموارد");
  });

  it("switching the language does not reset the title to the product's name alone", () => {
    document.title = documentTitle("en", "shell.home.title");
    applyLanguage("ar");
    expect(document.title).toBe("Home · ERP");
    applyLanguage("en");
  });
});

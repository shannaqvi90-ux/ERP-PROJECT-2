import { describe, expect, it } from "vitest";
import { buildCatalog, catalog, direction, translate } from "./i18n";

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

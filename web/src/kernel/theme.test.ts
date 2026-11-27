import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { applyLook, applyUserLook, chooseDensity, chooseTheme, densityKey, resolveTheme, storedDensity, storedThemeChoice, themeKey } from "./theme";

const media = (matching: string[]) =>
  vi.fn((query: string) => ({ matches: matching.includes(query), addEventListener: () => {}, removeEventListener: () => {} }) as unknown as MediaQueryList);

describe("theme and density", () => {
  beforeEach(() => {
    localStorage.clear();
    delete document.documentElement.dataset.theme;
    delete document.documentElement.dataset.density;
  });
  afterEach(() => vi.unstubAllGlobals());

  it("is dark until a theme is chosen (direction G), high contrast when the system asks for it, and never light by the system alone", () => {
    vi.stubGlobal("matchMedia", media(["(prefers-color-scheme: light)"]));
    expect(storedThemeChoice()).toBe("system");
    expect(applyLook()).toBe("dark");
    expect(document.documentElement.dataset.theme).toBe("dark");
    vi.stubGlobal("matchMedia", media(["(prefers-contrast: more)", "(prefers-color-scheme: light)"]));
    expect(resolveTheme("system")).toBe("hc");
    vi.stubGlobal("matchMedia", media([]));
    expect(resolveTheme("system")).toBe("dark");
    expect(localStorage.getItem(themeKey)).toBeNull();
  });

  it("remembers a chosen theme and density and applies them again on the next load", () => {
    vi.stubGlobal("matchMedia", media(["(prefers-color-scheme: light)"]));
    chooseTheme("hc");
    chooseDensity("comfortable");
    expect(localStorage.getItem(themeKey)).toBe("hc");
    expect(localStorage.getItem(densityKey)).toBe("comfortable");
    delete document.documentElement.dataset.theme;
    applyLook();
    expect(document.documentElement.dataset.theme).toBe("hc");
    expect(document.documentElement.dataset.density).toBe("comfortable");
  });

  it("ignores a stored value that is not a theme or a density", () => {
    localStorage.setItem(themeKey, "neon");
    localStorage.setItem(densityKey, "huge");
    expect(storedThemeChoice()).toBe("system");
    expect(storedDensity()).toBe("compact");
  });

  it("takes a signed-in user's own look, and the default (not the previous person's) where they never chose", () => {
    vi.stubGlobal("matchMedia", media([]));
    chooseTheme("light");
    chooseDensity("comfortable");
    applyUserLook({ theme: "hc", density: null });
    expect(document.documentElement.dataset.theme).toBe("hc");
    expect(document.documentElement.dataset.density).toBe("compact");
    expect(localStorage.getItem(themeKey)).toBe("hc");
    expect(localStorage.getItem(densityKey)).toBeNull();
    applyUserLook({});
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(localStorage.getItem(themeKey)).toBeNull();
    applyUserLook({ theme: "neon", density: "huge" });
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(document.documentElement.dataset.density).toBe("compact");
  });
});

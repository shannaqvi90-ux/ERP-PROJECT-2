import { useCallback, useEffect, useState } from "react";

/**
 * The look of the screens: theme (light, dark, high contrast) and density (compact, comfortable).
 * Both are the person's own choice, remembered on this device (the same two device keys serve the
 * next sign-in of the same browser: kernel/deviceState.ts deviceKeys, tests/Gates/device-keys.txt).
 * Until a theme is chosen the screen is dark (the product's own look), or high contrast when the
 * system asks for more contrast, and it changes when the system's contrast setting does.
 * The colours themselves live in design/tokens.css only; this file sets two attributes on <html>.
 */
export type ThemeChoice = "system" | "light" | "dark" | "hc";
export type Theme = "light" | "dark" | "hc";
export type Density = "compact" | "comfortable";

export const themeKey = "erp.theme";
export const densityKey = "erp.density";
export const themeChoices: readonly ThemeChoice[] = ["system", "light", "dark", "hc"];
export const densities: readonly Density[] = ["compact", "comfortable"];

export const isThemeChoice = (value: unknown): value is ThemeChoice => typeof value === "string" && (themeChoices as readonly string[]).includes(value);
export const isDensity = (value: unknown): value is Density => value === "compact" || value === "comfortable";

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Applied for this visit only.
  }
}

const matches = (query: string): boolean => (typeof window.matchMedia === "function" ? window.matchMedia(query).matches : false);

export function storedThemeChoice(): ThemeChoice {
  const stored = read(themeKey);
  return isThemeChoice(stored) ? stored : "system";
}

export function storedDensity(): Density {
  const stored = read(densityKey);
  return isDensity(stored) ? stored : "compact";
}

/** The theme a choice shows now: a chosen theme as it is, "system" as the system is set. */
export function resolveTheme(choice: ThemeChoice): Theme {
  if (choice !== "system") return choice;
  if (matches("(prefers-contrast: more)")) return "hc";
  // Direction G's look is dark; light is a choice a person makes (owner, 2026-11-17), not the system's.
  return "dark";
}

/** Put the remembered (or system) look on the document. Called before the first screen draws. */
export function applyLook(choice: ThemeChoice = storedThemeChoice(), density: Density = storedDensity()): Theme {
  const theme = resolveTheme(choice);
  const root = document.documentElement;
  root.dataset.theme = theme;
  root.dataset.density = density;
  return theme;
}

export function chooseTheme(choice: ThemeChoice): void {
  write(themeKey, choice);
  applyLook(choice, storedDensity());
  window.dispatchEvent(new Event("erp:look"));
}

export function chooseDensity(density: Density): void {
  write(densityKey, density);
  applyLook(storedThemeChoice(), density);
  window.dispatchEvent(new Event("erp:look"));
}

/** While the choice is "system", follow the system's settings as they change. */
export function followSystem(): () => void {
  if (typeof window.matchMedia !== "function") return () => {};
  const queries = [window.matchMedia("(prefers-color-scheme: light)"), window.matchMedia("(prefers-contrast: more)")];
  const changed = () => {
    if (storedThemeChoice() === "system") {
      applyLook();
      window.dispatchEvent(new Event("erp:look"));
    }
  };
  for (const q of queries) q.addEventListener?.("change", changed);
  return () => {
    for (const q of queries) q.removeEventListener?.("change", changed);
  };
}

/** The current choices, for the controls that change them. */
export function useLook() {
  const [state, setState] = useState(() => ({ choice: storedThemeChoice(), density: storedDensity(), theme: resolveTheme(storedThemeChoice()) }));
  useEffect(() => {
    const update = () => setState({ choice: storedThemeChoice(), density: storedDensity(), theme: resolveTheme(storedThemeChoice()) });
    window.addEventListener("erp:look", update);
    return () => window.removeEventListener("erp:look", update);
  }, []);
  return {
    ...state,
    setTheme: useCallback((choice: ThemeChoice) => chooseTheme(choice), []),
    setDensity: useCallback((density: Density) => chooseDensity(density), []),
  };
}

import { useCallback, useEffect, useState } from "react";

/**
 * The look of the screens: theme (light, dark, high contrast) and density (compact, comfortable).
 * Both are the person's own choice and belong to the person: they are kept with the user on the
 * server (PUT /api/identity/me/look, sent by modules/shell/LookSync.tsx when a choice is made) and
 * applied from the session at sign-in (applyUserLook), so the next person on the device gets their
 * own look, or the default when they never chose. The two device keys only hold the look last shown
 * here, for the sign-in screen before anyone is known (kernel/deviceState.ts deviceKeys,
 * tests/Gates/device-keys.txt).
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

function forget(key: string): void {
  try {
    localStorage.removeItem(key);
  } catch {
    // Storage unavailable: nothing was kept.
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

/** Sent on window when the person chooses a theme or a density: what the shell saves to their user. */
export const lookChosenEvent = "erp:look-chosen";
export type LookChange = { theme?: ThemeChoice; density?: Density };

export function chooseTheme(choice: ThemeChoice): void {
  write(themeKey, choice);
  applyLook(choice, storedDensity());
  window.dispatchEvent(new Event("erp:look"));
  window.dispatchEvent(new CustomEvent<LookChange>(lookChosenEvent, { detail: { theme: choice } }));
}

export function chooseDensity(density: Density): void {
  write(densityKey, density);
  applyLook(storedThemeChoice(), density);
  window.dispatchEvent(new Event("erp:look"));
  window.dispatchEvent(new CustomEvent<LookChange>(lookChosenEvent, { detail: { density } }));
}

/**
 * The look a user signed in with: their own saved choices, or the defaults where they never chose
 * (never the previous person's on this device). It is kept as the device's look for the sign-in
 * screen, and applied at once.
 */
export function applyUserLook(user: { theme?: string | null; density?: string | null }): void {
  const choice: ThemeChoice = isThemeChoice(user.theme) ? user.theme : "system";
  const density: Density = isDensity(user.density) ? user.density : "compact";
  // The device remembers this person's look for the sign-in screen; nothing of the previous person's is left.
  if (isThemeChoice(user.theme)) write(themeKey, choice);
  else forget(themeKey);
  if (isDensity(user.density)) write(densityKey, density);
  else forget(densityKey);
  applyLook(choice, density);
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

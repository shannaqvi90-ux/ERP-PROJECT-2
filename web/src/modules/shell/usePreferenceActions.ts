import { useCallback, useState } from "react";
import { translate, useI18n, type Language, type Numerals } from "../../kernel/i18n";
import { preferencesPermission, savePreferences, type SaveOutcome } from "../../kernel/preferences";
import { useSession } from "../../kernel/session";
import { announce } from "./announcer";

export type SaveState = SaveOutcome | "device" | null;

/**
 * Change the interface language or digits in one step: the screen changes at once, the change is
 * announced to screen readers in the new language, and for a signed-in user allowed to change
 * their profile it is saved (safely across a reload, see kernel/preferences.ts). Signed out, or
 * without the permission, the choice stays on this device.
 */
export function usePreferenceActions() {
  const { language, numerals, setLanguage, setNumerals } = useI18n();
  const { state, can } = useSession();
  const [saveState, setSaveState] = useState<SaveState>(null);

  const persist = useCallback(
    async (change: { language?: Language; numerals?: Numerals }) => {
      if (state.status !== "signedIn") return;
      if (!can(preferencesPermission)) {
        setSaveState("device");
        return;
      }
      setSaveState(await savePreferences(state.session.user.id, change));
    },
    [state, can],
  );

  const changeLanguage = useCallback(
    (next: Language) => {
      if (next === language) return;
      setLanguage(next);
      announce(translate(next, "shell.announce.language"));
      void persist({ language: next });
    },
    [language, setLanguage, persist],
  );

  const changeNumerals = useCallback(
    (next: Numerals) => {
      if (next === numerals) return;
      setNumerals(next);
      announce(translate(language, `shell.announce.digits.${next}`));
      void persist({ numerals: next });
    },
    [numerals, language, setNumerals, persist],
  );

  return { language, numerals, changeLanguage, changeNumerals, saveState };
}

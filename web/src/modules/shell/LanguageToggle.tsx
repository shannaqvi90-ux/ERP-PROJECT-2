import { useI18n, type Language } from "../../kernel/i18n";
import { chordForAria } from "../../kernel/shortcuts";
import { usePreferenceActions } from "./usePreferenceActions";

export const languageChord = "Alt+KeyL";

/**
 * One step from English to Arabic and back (also Alt+L). The button names the other language in
 * that language. The whole screen mirrors at once, without a reload; a signed-in user's choice is
 * saved to their profile.
 */
export function LanguageToggle({ className = "" }: { className?: string }) {
  const { t } = useI18n();
  const { language, changeLanguage } = usePreferenceActions();
  const next: Language = language === "ar" ? "en" : "ar";

  return (
    <button
      type="button"
      className={`button ghost lang-toggle ${className}`.trim()}
      lang={next}
      onClick={() => changeLanguage(next)}
      title={t("shell.language.switchTo")}
      aria-keyshortcuts={chordForAria(languageChord)}
    >
      {t(`shell.language.native.${next}`)}
    </button>
  );
}

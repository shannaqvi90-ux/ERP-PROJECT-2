import { api } from "../../kernel/api";
import { useI18n, type Language } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";

/**
 * One step from English to Arabic and back. The button names the other language in that
 * language. When signed in with the right permission, the choice is saved to the user's profile.
 */
export function LanguageToggle() {
  const { t, language, setLanguage } = useI18n();
  const { state, can } = useSession();
  const next: Language = language === "ar" ? "en" : "ar";

  async function toggle() {
    setLanguage(next);
    if (state.status === "signedIn" && can("identity.profile.update")) {
      try {
        await api("PUT", "/api/identity/me/preferences", { language: next });
      } catch {
        // The screen already switched; the profile keeps the old choice until the next try.
      }
    }
  }

  return (
    <button
      type="button"
      className="button ghost lang-toggle"
      lang={next}
      onClick={() => void toggle()}
      title={t("shell.language.switchTo")}
    >
      {t(`shell.language.native.${next}`)}
    </button>
  );
}

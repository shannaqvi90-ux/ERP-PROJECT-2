import { useCallback } from "react";
import { I18nProvider, useI18n, type Language, type Numerals } from "../../kernel/i18n";
import { effectivePreferences, preferencesPermission, savePreferences } from "../../kernel/preferences";
import { SessionProvider, useSession, type Session } from "../../kernel/session";
import { ShortcutProvider, useShortcut } from "../../kernel/shortcuts";
import { Announcer } from "./announcer";
import { AppShell } from "./AppShell";
import { languageChord } from "./LanguageToggle";
import { SignInPage } from "./SignInPage";
import { usePreferenceActions } from "./usePreferenceActions";

/** Alt+L works everywhere, the sign-in screen included. */
function LanguageShortcut() {
  const { language, changeLanguage } = usePreferenceActions();
  useShortcut({
    id: "shell.language",
    chord: languageChord,
    labelKey: "shell.language.switchTo",
    groupKey: "shell.shortcuts.group.general",
    inDialogs: true,
    run: () => changeLanguage(language === "ar" ? "en" : "ar"),
  });
  return null;
}

function Gate() {
  const { state } = useSession();
  const { t } = useI18n();
  return (
    <>
      <LanguageShortcut />
      <Announcer />
      {state.status === "loading" ? (
        <div className="splash" aria-busy="true">
          {t("shell.loading")}
        </div>
      ) : state.status === "leaving" ? (
        <div className="splash" aria-busy="true" role="status">
          {t("shell.signingOut")}
        </div>
      ) : state.status === "anonymous" ? (
        <SignInPage />
      ) : (
        <AppShell session={state.session} />
      )}
    </>
  );
}

function WithSession() {
  const { setLanguage, setNumerals } = useI18n();
  // After sign-in the user's saved preferences win over the device's, except changes this device
  // made that the server may not have received yet (a reload right after switching language):
  // those win and are sent again.
  const onSignedIn = useCallback(
    (session: Session) => {
      const { preferences, pending } = effectivePreferences(session.user.id, session.user);
      setLanguage(preferences.language);
      setNumerals(preferences.numerals);
      if (pending && session.permissions.includes(preferencesPermission)) void savePreferences(session.user.id, pending);
    },
    [setLanguage, setNumerals],
  );
  return (
    <SessionProvider onSignedIn={onSignedIn}>
      <Gate />
    </SessionProvider>
  );
}

export function App({ language, numerals }: { language?: Language; numerals?: Numerals }) {
  return (
    <I18nProvider initial={language} initialDigits={numerals}>
      <ShortcutProvider>
        <WithSession />
      </ShortcutProvider>
    </I18nProvider>
  );
}

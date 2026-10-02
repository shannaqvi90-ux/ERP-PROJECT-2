import { useCallback } from "react";
import { I18nProvider, useI18n, type Language } from "../../kernel/i18n";
import { SessionProvider, useSession, type Session } from "../../kernel/session";
import { AppShell } from "./AppShell";
import { SignInPage } from "./SignInPage";

function Gate() {
  const { state } = useSession();
  const { t } = useI18n();
  if (state.status === "loading") {
    return (
      <div className="splash" aria-busy="true">
        {t("shell.loading")}
      </div>
    );
  }
  if (state.status === "anonymous") return <SignInPage />;
  return <AppShell session={state.session} />;
}

function WithSession() {
  const { setLanguage } = useI18n();
  // After sign-in the user's saved language wins over the device's.
  const onSignedIn = useCallback((session: Session) => setLanguage(session.user.language), [setLanguage]);
  return (
    <SessionProvider onSignedIn={onSignedIn}>
      <Gate />
    </SessionProvider>
  );
}

export function App({ language }: { language?: Language }) {
  return (
    <I18nProvider initial={language}>
      <WithSession />
    </I18nProvider>
  );
}

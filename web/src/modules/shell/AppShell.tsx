import { useEffect, useRef } from "react";
import { useI18n } from "../../kernel/i18n";
import { Link, matchRoute, usePath } from "../../kernel/router";
import { useSession, type Session } from "../../kernel/session";
import { TopBarSlot } from "../../kernel/slots";
import { LanguageToggle } from "./LanguageToggle";

/**
 * The signed-in frame: top bar (workspace, navigation, language, user, sign out), the workspace
 * area and a status line. Navigation shows only entries the user's roles grant; a screen the
 * user cannot open is never offered, and opening its address shows "no access".
 */
export function AppShell({ session }: { session: Session }) {
  const { t, language, formatDateTime } = useI18n();
  const { signOut, can } = useSession();
  const path = usePath();
  const route = matchRoute(path);
  const mainRef = useRef<HTMLElement>(null);
  const tenantName = language === "ar" ? session.tenant.nameAr : session.tenant.nameEn;

  useEffect(() => {
    const title = route ? t(route.titleKey) : t("shell.notFound.title");
    document.title = `${title} · ${t("shell.app.title")}`;
    mainRef.current?.focus();
  }, [route, t]);

  const allowed = route && (!route.permission || can(route.permission));
  const Screen = allowed ? route.component : null;

  return (
    <div className="app">
      <a className="skip-link" href="#main">
        {t("shell.skipToContent")}
      </a>
      <header className="topbar">
        <Link to="/" className="brand">
          {t("shell.app.name")}
        </Link>
        <span className="workspace-name" title={t("shell.workspace")}>
          {tenantName}
        </span>
        <TopBarSlot />
        <nav className="topnav" aria-label={t("shell.navigation")}>
          {session.menu.map((item) => (
            <Link key={item.key} to={item.path} aria-current={path === item.path ? "page" : undefined}>
              {t(item.labelKey)}
            </Link>
          ))}
        </nav>
        <div className="topbar-end">
          <LanguageToggle />
          <span className="user-name" title={session.user.email}>
            {session.user.displayName}
          </span>
          <button type="button" className="button ghost" onClick={() => void signOut()}>
            {t("shell.signOut")}
          </button>
        </div>
      </header>
      <main id="main" ref={mainRef} tabIndex={-1} className="workspace">
        {Screen ? (
          <Screen />
        ) : route ? (
          <section className="empty-state" role="alert">
            <h1>{t("shell.noAccess.title")}</h1>
            <p>{t("shell.noAccess.body")}</p>
          </section>
        ) : (
          <section className="empty-state">
            <h1>{t("shell.notFound.title")}</h1>
            <p>
              <Link to="/">{t("shell.notFound.home")}</Link>
            </p>
          </section>
        )}
      </main>
      <footer className="statusbar">
        <span>{t("shell.status.signedInAs", { name: session.user.displayName, workspace: session.tenant.code })}</span>
        {session.expiresAt && <span>{t("shell.status.sessionUntil", { time: formatDateTime(session.expiresAt) })}</span>}
      </footer>
    </div>
  );
}

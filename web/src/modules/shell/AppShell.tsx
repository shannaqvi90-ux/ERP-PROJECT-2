import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { allowed, extensions } from "../../kernel/extensions";
import { Icon } from "../../kernel/icons";
import { translate, useI18n, type Language } from "../../kernel/i18n";
import { Link, matchRoute, navigate, usePath } from "../../kernel/router";
import { useSession, type Session } from "../../kernel/session";
import { Keys, chordForAria, useShortcut } from "../../kernel/shortcuts";
import { Breadcrumbs } from "./Breadcrumbs";
import { CommandPalette, type PaletteEntry } from "./CommandPalette";
import { LanguageToggle, languageChord } from "./LanguageToggle";
import { NavPane } from "./NavPane";
import { entryFor, groupLabelKey } from "./navigation";
import { PreferencesDialog } from "./PreferencesDialog";
import { ShortcutHelp } from "./ShortcutHelp";
import { usePreferenceActions } from "./usePreferenceActions";

export const shellChords = {
  palette: "Mod+KeyK",
  help: "Shift+Slash",
  helpAnywhere: "Mod+Slash",
  navigation: "Alt+KeyM",
  home: "Alt+KeyH",
  preferences: "Alt+KeyP",
  toggleNavigation: "Alt+KeyB",
} as const;

const navKey = "erp.navOpen";
const recentKey = (userId: string) => `erp.recent.${userId}`;
const maxRecent = 5;

function readNavOpen(): boolean {
  try {
    return localStorage.getItem(navKey) !== "0";
  } catch {
    return true;
  }
}

function readRecent(userId: string): string[] {
  try {
    const parsed = JSON.parse(localStorage.getItem(recentKey(userId)) ?? "[]") as unknown;
    return Array.isArray(parsed) ? parsed.filter((p): p is string => typeof p === "string").slice(0, maxRecent) : [];
  } catch {
    return [];
  }
}

const other = (language: Language): Language => (language === "ar" ? "en" : "ar");

/**
 * The signed-in frame, dense and keyboard first: top bar (navigation toggle, workspace and its
 * context switchers, command palette, language, help, user, sign out), the module navigation
 * pane, breadcrumbs, the screen, and a status line. Navigation, palette and breadcrumbs offer only
 * what the user's roles grant; a screen the user cannot open shows "No access" if reached by
 * address. Everything is reachable without a mouse (see the shortcut help sheet: ? or Ctrl+/).
 */
export function AppShell({ session }: { session: Session }) {
  const { t, language, numerals, format } = useI18n();
  const { signOut, can } = useSession();
  const { changeLanguage, changeNumerals } = usePreferenceActions();
  const path = usePath();
  const route = matchRoute(path);
  const mainRef = useRef<HTMLElement>(null);
  const navRef = useRef<HTMLElement>(null);
  const [navOpen, setNavOpen] = useState(readNavOpen);
  const [dialog, setDialog] = useState<"palette" | "help" | "preferences" | null>(null);
  const [recent, setRecent] = useState(() => readRecent(session.user.id));
  const tenantName = language === "ar" ? session.tenant.nameAr : session.tenant.nameEn;
  const entry = entryFor(session.menu, path);

  const allowedRoute = route && (!route.permission || can(route.permission));
  const Screen = allowedRoute ? route.component : null;
  const titleKey = route ? route.titleKey : "shell.notFound.title";

  useEffect(() => {
    document.title = `${t(titleKey)} · ${t("shell.app.title")}`;
  }, [titleKey, t]);

  // A new screen takes the focus, unless it already put the focus somewhere inside itself.
  useEffect(() => {
    const main = mainRef.current;
    if (main && !main.contains(document.activeElement)) main.focus({ preventScroll: true });
  }, [path]);

  useEffect(() => {
    try {
      localStorage.setItem(navKey, navOpen ? "1" : "0");
    } catch {
      // Remembered for this visit only.
    }
  }, [navOpen]);

  const rememberScreen = useCallback(
    (target: string) => {
      setRecent((list) => {
        const next = [target, ...list.filter((p) => p !== target)].slice(0, maxRecent);
        try {
          localStorage.setItem(recentKey(session.user.id), JSON.stringify(next));
        } catch {
          // Not remembered on this device.
        }
        return next;
      });
    },
    [session.user.id],
  );

  const focusNavigation = useCallback(() => {
    setNavOpen(true);
    window.setTimeout(() => {
      const nav = navRef.current;
      (nav?.querySelector<HTMLElement>('a[tabindex="0"]') ?? nav?.querySelector<HTMLElement>("a"))?.focus();
    }, 0);
  }, []);

  const close = useCallback(() => setDialog(null), []);
  const group = "shell.shortcuts.group.general";
  const navGroup = "shell.shortcuts.group.navigation";
  useShortcut({ id: "shell.palette", chord: shellChords.palette, labelKey: "shell.action.palette", groupKey: group, inDialogs: true, run: () => setDialog((d) => (d === "palette" ? null : "palette")) });
  useShortcut({ id: "shell.help", chord: shellChords.help, labelKey: "shell.action.shortcuts", groupKey: group, run: () => setDialog("help") });
  useShortcut({ id: "shell.helpAnywhere", chord: shellChords.helpAnywhere, labelKey: "shell.action.shortcuts", groupKey: group, inDialogs: true, run: () => setDialog((d) => (d === "help" ? null : "help")) });
  useShortcut({ id: "shell.preferences", chord: shellChords.preferences, labelKey: "shell.action.preferences", groupKey: group, run: () => setDialog("preferences") });
  useShortcut({ id: "shell.navigation", chord: shellChords.navigation, labelKey: "shell.action.focusNav", groupKey: navGroup, run: focusNavigation });
  useShortcut({ id: "shell.home", chord: shellChords.home, labelKey: "shell.action.home", groupKey: navGroup, run: () => navigate("/") });
  useShortcut({ id: "shell.toggleNavigation", chord: shellChords.toggleNavigation, labelKey: "shell.action.toggleNav", groupKey: navGroup, run: () => setNavOpen((o) => !o) });

  const screens = useMemo<PaletteEntry[]>(() => {
    const home: PaletteEntry = {
      id: "screen:/",
      title: t("shell.home.title"),
      keywords: [translate(other(language), "shell.home.title")],
      icon: "home",
      path: "/",
      chord: shellChords.home,
    };
    return [
      home,
      ...session.menu.map<PaletteEntry>((item) => {
        const groupKey = item.group ? groupLabelKey(item.group) : null;
        return {
          id: `screen:${item.path}`,
          title: t(item.labelKey),
          subtitle: groupKey ? t(groupKey) : undefined,
          keywords: [translate(other(language), item.labelKey), ...(groupKey ? [translate(other(language), groupKey)] : []), item.path],
          icon: "screen",
          path: item.path,
        };
      }),
    ];
  }, [session.menu, t, language]);

  const actions = useMemo<PaletteEntry[]>(() => {
    const both = (key: string) => [translate(other(language), key)];
    const nextLanguage = other(language);
    const nextDigits = numerals === "arab" ? "latn" : "arab";
    return [
      { id: "action:language", title: t(`shell.action.language.${nextLanguage}`), keywords: [...both(`shell.action.language.${nextLanguage}`), "language", "اللغة", "عربي", "arabic", "english"], icon: "globe", chord: languageChord, run: () => changeLanguage(nextLanguage) },
      { id: "action:digits", title: t(`shell.action.digits.${nextDigits}`), keywords: [...both(`shell.action.digits.${nextDigits}`), "digits", "numbers", "أرقام"], icon: "keyboard", run: () => changeNumerals(nextDigits) },
      { id: "action:preferences", title: t("shell.action.preferences"), keywords: both("shell.action.preferences"), icon: "user", chord: shellChords.preferences, run: () => setDialog("preferences") },
      { id: "action:shortcuts", title: t("shell.action.shortcuts"), keywords: both("shell.action.shortcuts"), icon: "help", chord: shellChords.helpAnywhere, run: () => setDialog("help") },
      { id: "action:navigation", title: t("shell.action.focusNav"), keywords: both("shell.action.focusNav"), icon: "menu", chord: shellChords.navigation, run: focusNavigation },
      { id: "action:toggleNavigation", title: t("shell.action.toggleNav"), keywords: both("shell.action.toggleNav"), icon: "menu", chord: shellChords.toggleNavigation, run: () => setNavOpen((o) => !o) },
      { id: "action:print", title: t("shell.action.print"), keywords: both("shell.action.print"), icon: "print", run: () => void window.setTimeout(() => window.print(), 50) },
      { id: "action:signOut", title: t("shell.action.signOut"), keywords: both("shell.action.signOut"), icon: "signOut", run: () => void signOut() },
    ];
  }, [t, language, numerals, changeLanguage, changeNumerals, focusNavigation, signOut]);

  const contextItems = allowed(extensions.topbar, can);
  const statusItems = allowed(extensions.status, can);

  return (
    <div className="app" data-nav={navOpen ? "open" : "closed"}>
      <a className="skip-link" href="#main">
        {t("shell.skipToContent")}
      </a>
      <header className="topbar">
        <button
          type="button"
          className="button ghost icon-button"
          aria-label={t("shell.nav.toggle")}
          aria-expanded={navOpen}
          aria-controls="navpane"
          aria-keyshortcuts={chordForAria(shellChords.toggleNavigation)}
          onClick={() => setNavOpen((o) => !o)}
        >
          <Icon name="menu" />
        </button>
        <Link to="/" className="brand" data-brand="">
          {t("shell.app.name")}
        </Link>
        <span className="workspace-name" title={t("shell.workspace")}>
          {tenantName}
        </span>
        {contextItems.length > 0 && (
          <div className="topbar-context" role="group" aria-label={t("shell.topbar.context")}>
            {contextItems.map(({ key, component: Item }) => (
              <Item key={key} />
            ))}
          </div>
        )}
        <button
          type="button"
          className="palette-trigger"
          onClick={() => setDialog("palette")}
          aria-keyshortcuts={chordForAria(shellChords.palette)}
          aria-haspopup="dialog"
        >
          <Icon name="search" />
          <span className="palette-trigger-text">{t("shell.palette.open")}</span>
          <Keys chord={shellChords.palette} />
        </button>
        <div className="topbar-end">
          <LanguageToggle />
          <button
            type="button"
            className="button ghost icon-button"
            aria-label={t("shell.topbar.help")}
            title={t("shell.topbar.help")}
            aria-keyshortcuts={chordForAria(shellChords.helpAnywhere)}
            onClick={() => setDialog("help")}
          >
            <Icon name="help" />
          </button>
          <button
            type="button"
            className="button ghost user-button"
            aria-label={t("shell.topbar.user", { name: session.user.displayName })}
            aria-keyshortcuts={chordForAria(shellChords.preferences)}
            onClick={() => setDialog("preferences")}
          >
            <Icon name="user" />
            <span className="user-name">{session.user.displayName}</span>
          </button>
          <button type="button" className="button ghost" onClick={() => void signOut()}>
            <Icon name="signOut" />
            <span>{t("shell.signOut")}</span>
          </button>
        </div>
      </header>
      <div className="frame">
        <NavPane ref={navRef} menu={session.menu} path={path} open={navOpen} />
        <main id="main" ref={mainRef} tabIndex={-1} className="workspace">
          <div className="print-only print-screen-head">
            <span>{tenantName}</span>
            <span>
              {t("shell.print.printedBy", { name: session.user.displayName })} · {t("shell.print.printedAt", { time: format.dateTime(new Date()) })}
            </span>
          </div>
          {path !== "/" && <Breadcrumbs entry={entry} titleKey={titleKey} />}
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
      </div>
      <footer className="statusbar">
        <span>{t("shell.status.signedInAs", { name: session.user.displayName, workspace: session.tenant.code })}</span>
        {statusItems.map(({ key, component: Item }) => (
          <span key={key} className="status-item">
            <Item />
          </span>
        ))}
        {session.expiresAt && <span>{t("shell.status.sessionUntil", { time: format.dateTime(session.expiresAt) })}</span>}
        <span className="status-hints" aria-hidden="true">
          <Keys chord={shellChords.palette} /> {t("shell.status.commands")} · <Keys chord={shellChords.help} /> {t("shell.status.shortcuts")}
        </span>
      </footer>
      {dialog === "palette" && <CommandPalette screens={screens} actions={actions} recent={recent} onOpenScreen={rememberScreen} onClose={close} />}
      {dialog === "help" && <ShortcutHelp onClose={close} />}
      {dialog === "preferences" && <PreferencesDialog session={session} onClose={close} />}
    </div>
  );
}

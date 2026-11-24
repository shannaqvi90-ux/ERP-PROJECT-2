import { useCallback, useState } from "react";
import { allowed, extensions } from "../../kernel/extensions";
import { AttentionContext, type AttentionMeta } from "../../kernel/home";
import { useI18n } from "../../kernel/i18n";
import { Icon } from "../../kernel/icons";
import { Link } from "../../kernel/router";
import { sessionUserName, useSession } from "../../kernel/session";

/**
 * The screen a user lands on after signing in: a greeting, how many things wait for them, the
 * cards the modules offer (kernel/home.tsx; only those the user's roles allow) and every area
 * the user can open. A user whose roles open nothing is told so and nothing else.
 */
export function HomePage() {
  const { t, language, format } = useI18n();
  const { state, can } = useSession();
  const [attention, setAttention] = useState<Record<string, number>>({});
  const [meta, setMeta] = useState<Record<string, AttentionMeta>>({});
  const report = useCallback((key: string, count: number, about?: AttentionMeta) => {
    setAttention((a) => (a[key] === count ? a : { ...a, [key]: count }));
    if (about) setMeta((m) => (m[key]?.path === about.path && m[key]?.labelKey === about.labelKey ? m : { ...m, [key]: about }));
  }, []);
  if (state.status !== "signedIn") return null;
  const { session } = state;
  const cards = allowed(extensions.home, can);
  // What needs doing first: the figures that need this user, the largest first.
  const first = Object.entries(attention).filter(([key, n]) => n > 0 && meta[key]).sort((a, b) => b[1] - a[1]).slice(0, 3);
  const waiting = Object.values(attention).reduce((sum, n) => sum + n, 0);
  return (
    <section className="home">
      <h1>{t("shell.home.welcome", { name: sessionUserName(session.user, language) })}</h1>
      {session.menu.length === 0 ? (
        <p className="muted">{t("shell.home.nothingYet")}</p>
      ) : (
        <AttentionContext.Provider value={report}>
          {cards.length > 0 && (
            <p className="home-summary" role="status" data-waiting={waiting > 0 || undefined}>
              {waiting > 0 ? t("shell.home.needsYou", { count: waiting, number: format.number(waiting) }) : t("shell.home.allClear")}
            </p>
          )}
          {first.length > 0 && (
            <section className="home-first" aria-label={t("shell.home.first")}>
              <h2 className="home-first-title">{t("shell.home.first")}</h2>
              <ol className="home-first-list">
                {first.map(([key, count]) => (
                  <li key={key}>
                    <Link to={meta[key]!.path} className="home-first-item">
                      <Icon name="warning" />
                      <span className="home-first-label">{t(meta[key]!.labelKey)}</span>
                      <span className="home-first-count">{format.number(count)}</span>
                    </Link>
                  </li>
                ))}
              </ol>
            </section>
          )}
          {cards.length > 0 && (
            <div className="home-cards">
              {cards.map(({ key, component: Card }) => (
                <Card key={key} />
              ))}
            </div>
          )}
          <h2 className="home-areas-title">{t("shell.home.openAnArea")}</h2>
          <ul className="tiles">
            {session.menu.map((item) => (
              <li key={item.key}>
                <Link to={item.path} className="tile">
                  {t(item.labelKey)}
                </Link>
              </li>
            ))}
          </ul>
        </AttentionContext.Provider>
      )}
    </section>
  );
}

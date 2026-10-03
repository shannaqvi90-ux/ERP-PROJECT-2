import { useI18n } from "../../kernel/i18n";
import { Link } from "../../kernel/router";
import { sessionUserName, useSession } from "../../kernel/session";

/** The empty workspace a user lands on after signing in. */
export function HomePage() {
  const { t, language } = useI18n();
  const { state } = useSession();
  if (state.status !== "signedIn") return null;
  const { session } = state;
  return (
    <section className="home">
      <h1>{t("shell.home.welcome", { name: sessionUserName(session.user, language) })}</h1>
      {session.menu.length === 0 ? (
        <p className="muted">{t("shell.home.nothingYet")}</p>
      ) : (
        <>
          <p className="muted">{t("shell.home.openAnArea")}</p>
          <ul className="tiles">
            {session.menu.map((item) => (
              <li key={item.key}>
                <Link to={item.path} className="tile">
                  {t(item.labelKey)}
                </Link>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}

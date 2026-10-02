import { useEffect, useState } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";

type User = {
  id: string;
  email: string;
  displayName: string;
  language: "en" | "ar";
  isActive: boolean;
  roleIds: string[];
  lastSignInAt: string | null;
};

type Page = { items: User[]; total: number };

const pageSize = 50;

/** Read-only user list (the shared list framework replaces it in p05). */
export function UsersPage() {
  const { t, formatDateTime, formatNumber } = useI18n();
  const [search, setSearch] = useState("");
  const [skip, setSkip] = useState(0);
  const [page, setPage] = useState<Page | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      const query = new URLSearchParams({ skip: String(skip), take: String(pageSize) });
      if (search.trim()) query.set("search", search.trim());
      api<Page>("GET", `/api/identity/users?${query}`)
        .then((p) => {
          if (!controller.signal.aborted) setPage(p);
        })
        .catch((e: Error) => setError(e.message));
    }, 200);
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [search, skip]);

  return (
    <section>
      <div className="screen-header">
        <h1>{t("identity.users.title")}</h1>
        <input
          type="search"
          className="search"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setSkip(0);
          }}
          placeholder={t("identity.users.search")}
          aria-label={t("identity.users.search")}
        />
        {page && <span className="muted">{t("identity.users.count", { count: formatNumber(page.total) })}</span>}
      </div>
      {error && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}
      <table className="grid">
        <thead>
          <tr>
            <th scope="col">{t("identity.users.name")}</th>
            <th scope="col">{t("identity.users.email")}</th>
            <th scope="col">{t("identity.users.language")}</th>
            <th scope="col">{t("identity.users.status")}</th>
            <th scope="col">{t("identity.users.lastSignIn")}</th>
          </tr>
        </thead>
        <tbody>
          {page?.items.map((u) => (
            <tr key={u.id}>
              <td>{u.displayName}</td>
              <td dir="ltr">{u.email}</td>
              <td>{t(`identity.language.${u.language}`)}</td>
              <td>{u.isActive ? t("identity.users.active") : t("identity.users.inactive")}</td>
              <td>{u.lastSignInAt ? formatDateTime(u.lastSignInAt) : t("identity.users.never")}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {page && page.total > pageSize && (
        <div className="pager">
          <button type="button" className="button" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - pageSize))}>
            {t("identity.pager.previous")}
          </button>
          <span className="muted">
            {t("identity.pager.range", {
              from: formatNumber(skip + 1),
              to: formatNumber(Math.min(skip + pageSize, page.total)),
              total: formatNumber(page.total),
            })}
          </span>
          <button type="button" className="button" disabled={skip + pageSize >= page.total} onClick={() => setSkip(skip + pageSize)}>
            {t("identity.pager.next")}
          </button>
        </div>
      )}
    </section>
  );
}

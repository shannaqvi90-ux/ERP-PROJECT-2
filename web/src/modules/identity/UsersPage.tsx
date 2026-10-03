import { useCallback, useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";
import { isTyping, roleName, type Role, type RolePage, type User, type UserPage } from "./model";
import { NewUserForm, UserDetail, type Notice } from "./UserPanel";
import "./identity.css";

const pageSize = 50;

type Panel = { kind: "none" } | { kind: "new" } | { kind: "user"; id: string; notice?: Notice };

function panelFromUrl(): Panel {
  const query = new URLSearchParams(window.location.search);
  if (query.has("new")) return { kind: "new" };
  const id = query.get("user");
  return id ? { kind: "user", id } : { kind: "none" };
}

function panelToUrl(panel: Panel) {
  const query = panel.kind === "new" ? "?new" : panel.kind === "user" ? `?user=${encodeURIComponent(panel.id)}` : "";
  window.history.replaceState(null, "", `${window.location.pathname}${query}`);
}

/**
 * Users: a server-paged, searchable list beside the selected user's panel. Keyboard first: "/"
 * finds, "n" starts a new user, arrow keys move through the rows, Enter opens one, Escape closes
 * the panel; in a form Ctrl+Enter saves. Buttons and fields the user's roles do not grant are not
 * shown (the API refuses them anyway).
 */
export function UsersPage() {
  const { t, formatDateTime, formatNumber, language } = useI18n();
  const { can } = useSession();
  const [search, setSearch] = useState("");
  const [skip, setSkip] = useState(0);
  const [page, setPage] = useState<UserPage | null>(null);
  const [roles, setRoles] = useState<Role[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [panel, setPanelState] = useState<Panel>(panelFromUrl);
  const [reload, setReload] = useState(0);
  const searchRef = useRef<HTMLInputElement>(null);
  const rowsRef = useRef<HTMLTableSectionElement>(null);

  const setPanel = useCallback((next: Panel) => {
    setPanelState(next);
    panelToUrl(next);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      const query = new URLSearchParams({ skip: String(skip), take: String(pageSize) });
      if (search.trim()) query.set("search", search.trim());
      api<UserPage>("GET", `/api/identity/users?${query}`)
        .then((p) => {
          if (!controller.signal.aborted) setPage(p);
        })
        .catch((e: Error) => setError(e.message));
    }, 150);
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [search, skip, reload]);

  useEffect(() => {
    if (!can("identity.roles.read")) return;
    api<RolePage>("GET", "/api/identity/roles").then((p) => setRoles(p.items), () => setRoles([]));
  }, [can]);

  // Screen shortcuts, only while the user is not typing in a field.
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.ctrlKey || event.metaKey || event.altKey || isTyping(event)) return;
      if (event.key === "/") {
        event.preventDefault();
        searchRef.current?.focus();
      } else if (event.key.toLowerCase() === "n" && can("identity.users.create")) {
        event.preventDefault();
        setPanel({ kind: "new" });
      } else if (event.key === "Escape" && panel.kind !== "none") {
        setPanel({ kind: "none" });
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [can, panel.kind, setPanel]);

  function focusRow(index: number) {
    const rows = rowsRef.current?.querySelectorAll<HTMLTableRowElement>("tr");
    if (!rows || rows.length === 0) return;
    rows[Math.max(0, Math.min(rows.length - 1, index))]?.focus();
  }

  function onRowKey(event: ReactKeyboardEvent<HTMLTableRowElement>, index: number, user: User) {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      focusRow(index + 1);
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      if (index === 0) searchRef.current?.focus();
      else focusRow(index - 1);
    } else if (event.key === "Enter") {
      event.preventDefault();
      setPanel({ kind: "user", id: user.id });
    }
  }

  const roleNames = new Map(roles.map((r) => [r.id, roleName(r, language)]));

  return (
    <section className={panel.kind === "none" ? "id-screen" : "id-screen with-panel"}>
      <div className="id-list">
        <div className="screen-header">
          <h1>{t("identity.users.title")}</h1>
          <input
            ref={searchRef}
            type="search"
            className="search"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setSkip(0);
            }}
            onKeyDown={(e) => {
              if (e.key === "ArrowDown") {
                e.preventDefault();
                focusRow(0);
              } else if (e.key === "Enter") {
                const first = page?.items[0];
                if (first) setPanel({ kind: "user", id: first.id });
              }
            }}
            placeholder={t("identity.users.search")}
            aria-label={t("identity.users.search")}
            aria-keyshortcuts="/"
          />
          {page && <span className="muted">{t("identity.users.count", { count: page.total })}</span>}
          {can("identity.users.create") && (
            <button type="button" className="button primary id-push" onClick={() => setPanel({ kind: "new" })} aria-keyshortcuts="N">
              {t("identity.users.new")}
            </button>
          )}
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
              <th scope="col">{t("identity.users.roles")}</th>
              <th scope="col">{t("identity.users.language")}</th>
              <th scope="col">{t("identity.users.status")}</th>
              <th scope="col">{t("identity.users.lastSignIn")}</th>
            </tr>
          </thead>
          <tbody ref={rowsRef}>
            {page?.items.map((u, i) => (
              <tr
                key={u.id}
                tabIndex={0}
                aria-selected={panel.kind === "user" && panel.id === u.id}
                className={panel.kind === "user" && panel.id === u.id ? "id-selected" : undefined}
                onClick={() => setPanel({ kind: "user", id: u.id })}
                onKeyDown={(e) => onRowKey(e, i, u)}
              >
                <td>{u.displayName}</td>
                <td dir="ltr">{u.email}</td>
                <td className="id-ellipsis">{u.roleIds.map((r) => roleNames.get(r)).filter(Boolean).join(language === "ar" ? "، " : ", ")}</td>
                <td>{t(`identity.language.${u.language}`)}</td>
                <td>
                  {u.isActive ? t("identity.users.active") : t("identity.users.inactive")}
                  {u.pendingSetup && <span className="id-badge warn">{t("identity.users.pendingSetup")}</span>}
                </td>
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
      </div>
      {panel.kind !== "none" && (
        <aside className="id-panel" aria-label={t("identity.users.panel")}>
          {panel.kind === "new" ? (
            <NewUserForm
              roles={roles}
              onClose={() => setPanel({ kind: "none" })}
              onCreated={(user) => {
                setReload((n) => n + 1);
                setPanel({
                  kind: "user",
                  id: user.id,
                  notice: user.setupCode
                    ? { kind: "code", code: user.setupCode, expiresAt: user.setupCodeExpiresAt, email: user.email }
                    : { kind: "info", text: t("identity.form.created") },
                });
              }}
            />
          ) : (
            <UserDetail
              key={panel.id}
              userId={panel.id}
              roles={roles}
              notice={panel.notice}
              onClose={() => setPanel({ kind: "none" })}
              onSaved={() => setReload((n) => n + 1)}
            />
          )}
        </aside>
      )}
    </section>
  );
}

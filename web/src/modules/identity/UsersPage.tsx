import { useCallback, useEffect, useState } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { ListView } from "../../kernel/lists/ListView";
import { useSession } from "../../kernel/session";
import { isTyping, roleName, userName, type Role, type RolePage } from "./model";
import { NewUserForm, UserDetail, type Notice } from "./UserPanel";
import "./identity.css";

/** The new-user form's part of the address (?new); the list keeps its own (?q=, ?open=id, …). */
function newToUrl(open: boolean) {
  const query = new URLSearchParams(window.location.search);
  query.delete("new");
  const parts = [query.toString(), open ? "new" : ""].filter(Boolean);
  window.history.replaceState(null, "", `${window.location.pathname}${parts.length ? `?${parts.join("&")}` : ""}`);
}

/**
 * Users: the shared list (search as you type, filters, sort, views, keyboard) with the open user
 * in the list's details panel (?open=id). Keyboard first: "/" finds, "n" starts a new user, arrow
 * keys move through the rows, Enter opens one, Escape closes the panel; in a form Ctrl+Enter
 * saves. Buttons and fields the user's roles do not grant are not shown (the API refuses them
 * anyway).
 */
export function UsersPage() {
  const { t, language } = useI18n();
  const { can } = useSession();
  const [roles, setRoles] = useState<Role[]>([]);
  const [creating, setCreatingState] = useState(() => new URLSearchParams(window.location.search).has("new"));
  const [openId, setOpenId] = useState<string | null>(() => new URLSearchParams(window.location.search).get("open"));
  // The one-time notice (set-up code) of a user just created, shown once in their panel.
  const [notices, setNotices] = useState<Record<string, Notice>>({});
  const [reload, setReload] = useState(0);
  const [message, setMessage] = useState<string | null>(null);

  const setCreating = useCallback((next: boolean) => {
    setCreatingState(next);
    newToUrl(next);
  }, []);

  const onOpenIdChange = useCallback(
    (id: string | null) => {
      setOpenId(id);
      if (id) setCreating(false);
    },
    [setCreating],
  );

  useEffect(() => {
    if (!can("identity.roles.read")) return;
    api<RolePage>("GET", "/api/identity/roles").then((p) => setRoles(p.items), () => setRoles([]));
  }, [can]);

  // Screen shortcuts, only while the user is not typing in a field.
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.ctrlKey || event.metaKey || event.altKey || event.defaultPrevented || isTyping(event)) return;
      if (event.key === "/") {
        const search = document.querySelector<HTMLInputElement>(".list-search input");
        if (!search) return;
        event.preventDefault();
        search.focus();
        search.select();
      } else if (event.key.toLowerCase() === "n" && can("identity.users.create")) {
        event.preventDefault();
        setOpenId(null);
        setCreating(true);
      } else if (event.key === "Escape" && creating) {
        setCreating(false);
      } else if (event.key === "Escape" && openId) {
        setOpenId(null);
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [can, creating, openId, setCreating]);

  const roleNames = new Map(roles.map((r) => [r.id, roleName(r, language)]));

  return (
    <section className={creating ? "id-screen with-panel" : "id-screen"}>
      <div className="id-list">
        {message && (
          <div className="id-notice" role="status">
            {message}
          </div>
        )}
        <ListView
          listKey="identity.users"
          titleKey="identity.users.title"
          countKey="identity.users.count"
          searchPlaceholderKey="identity.users.search"
          can={can}
          openOnClick
          reloadKey={reload}
          openId={openId}
          onOpenIdChange={onOpenIdChange}
          renderRecord={(id, close) => (
            <UserDetail
              key={id}
              userId={id}
              roles={roles}
              notice={notices[id]}
              onClose={close}
              onSaved={() => setReload((n) => n + 1)}
              onDeleted={(user) => {
                setMessage(t("identity.users.deleted", { name: userName(user, language) }));
                setReload((n) => n + 1);
                close();
              }}
            />
          )}
          actions={
            can("identity.users.create") && (
              <button
                type="button"
                className="button primary"
                onClick={() => {
                  setOpenId(null);
                  setCreating(true);
                }}
                aria-keyshortcuts="N"
              >
                {t("identity.users.new")}
              </button>
            )
          }
          renderCell={{
            displayName: (u) => String((language === "ar" && u.displayNameAr ? u.displayNameAr : u.displayName) ?? ""),
            email: (u) => <span dir="ltr">{String(u.email ?? "")}</span>,
            roleIds: (u) => (
              <span className="id-ellipsis">
                {(Array.isArray(u.roleIds) ? (u.roleIds as string[]) : [])
                  .map((r) => roleNames.get(r))
                  .filter(Boolean)
                  .join(language === "ar" ? "، " : ", ")}
              </span>
            ),
            isActive: (u) => (
              <span className="id-status">
                {u.isActive ? t("identity.users.active") : <span className="id-badge off">{t("identity.users.inactive")}</span>}
                {u.pendingSetup ? <span className="id-badge warn">{t("identity.users.pendingSetup")}</span> : null}
              </span>
            ),
            lastSignInAt: (u) => (u.lastSignInAt ? undefined : <span className="muted">{t("identity.users.never")}</span>),
          }}
        />
      </div>
      {creating && (
        <aside className="id-panel" aria-label={t("identity.users.panel")}>
          <NewUserForm
            roles={roles}
            onClose={() => setCreating(false)}
            onCreated={(user) => {
              setReload((n) => n + 1);
              setNotices((all) => ({
                ...all,
                [user.id]: user.setupCode
                  ? { kind: "code", code: user.setupCode, expiresAt: user.setupCodeExpiresAt, email: user.email }
                  : { kind: "info", text: t("identity.form.created") },
              }));
              setCreating(false);
              setOpenId(user.id);
            }}
          />
        </aside>
      )}
    </section>
  );
}

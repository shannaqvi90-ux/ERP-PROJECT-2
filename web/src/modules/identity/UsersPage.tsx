import { useEffect, useState } from "react";
import { api } from "../../kernel/api";
import { newRecord, useRecordPanel } from "../../kernel/forms/recordPanel";
import { useI18n } from "../../kernel/i18n";
import { ListView } from "../../kernel/lists/ListView";
import { useSession } from "../../kernel/session";
import { chordForAria, chordKeys, useShortcut } from "../../kernel/shortcuts";
import { isTyping, newRecordChord, roleName, userName, type Role, type RolePage } from "./model";
import { NewUserForm, UserDetail, type Notice } from "./UserPanel";
import "./identity.css";

/**
 * Users: the shared list (search as you type, filters, sort, views, keyboard) with the open user
 * in the list's details panel (?open=id) and a new user at ?open=new, as on every list screen.
 * Keyboard first: "/" finds, "n" or Alt+N starts a new user, arrow keys move through the rows,
 * Enter opens one, Escape closes the panel; in the form Ctrl+S or Ctrl+Enter saves and
 * Alt+PageDown/PageUp moves to the next or previous user. Buttons and fields the user's roles do
 * not grant are not shown (the API refuses them anyway).
 */
export function UsersPage() {
  const { t, language } = useI18n();
  const { can } = useSession();
  const [roles, setRoles] = useState<Role[]>([]);
  const panel = useRecordPanel(can("identity.users.create"));
  // The one-time notice (set-up code) of a user just created, shown once in their panel.
  const [notices, setNotices] = useState<Record<string, Notice>>({});
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!can("identity.roles.read")) return;
    api<RolePage>("GET", "/api/identity/roles").then((p) => setRoles(p.items), () => setRoles([]));
  }, [can]);

  // Alt+N starts a new user from anywhere on the screen, including the search box the list
  // focuses on arrival (where a plain "n" is typed into the search).
  useShortcut({
    id: "identity.users.new",
    chord: newRecordChord,
    labelKey: "identity.users.new",
    groupKey: "identity.shortcuts.group",
    enabled: Boolean(panel.startNew),
    run: () => panel.startNew?.(),
  });

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
      } else if (event.key.toLowerCase() === "n" && panel.startNew) {
        event.preventDefault();
        panel.startNew();
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [panel.startNew]);

  const roleNames = new Map(roles.map((r) => [r.id, roleName(r, language)]));

  return (
    <section className="id-screen">
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
          reloadKey={panel.reload}
          openId={panel.openId}
          onOpenIdChange={panel.onOpenIdChange}
          renderRecord={(id, close, nav) =>
            id === newRecord ? (
              <NewUserForm
                key={panel.formKey}
                roles={roles}
                onClose={close}
                onCreated={(user) => {
                  setNotices((all) => ({
                    ...all,
                    [user.id]: user.setupCode
                      ? { kind: "code", code: user.setupCode, expiresAt: user.setupCodeExpiresAt, email: user.email }
                      : { kind: "info", text: t("identity.form.created") },
                  }));
                  panel.saved(user.id);
                }}
              />
            ) : (
              <UserDetail
                key={id}
                userId={id}
                roles={roles}
                notice={notices[id]}
                nav={nav}
                onClose={close}
                onSaved={() => panel.refresh()}
                onDeleted={(user) => {
                  setMessage(t("identity.users.deleted", { name: userName(user, language) }));
                  panel.removed();
                }}
              />
            )
          }
          actions={
            panel.startNew && (
              <button
                type="button"
                className="button primary"
                onClick={panel.startNew}
                aria-keyshortcuts={`${chordForAria(newRecordChord)} N`}
                title={chordKeys(newRecordChord).join("+")}
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
    </section>
  );
}

import { useEffect, useState } from "react";
import { api } from "../../kernel/api";
import { newRecord, useRecordPanel } from "../../kernel/forms/recordPanel";
import { useI18n } from "../../kernel/i18n";
import { ListView, type BulkAction } from "../../kernel/lists/ListView";
import type { Row } from "../../kernel/lists/model";
import { useSession } from "../../kernel/session";
import { chordForAria, chordKeys, useShortcut } from "../../kernel/shortcuts";
import { isTyping, newRecordChord, roleName, userName, type Role, type RolePage } from "./model";
import { NewUserForm, UserDetail, type Notice } from "./UserPanel";
import "./identity.css";

/**
 * Sets the chosen users active or inactive, one saved change per user (the same change the user
 * panel saves, so every rule of the API applies: never oneself, never someone holding more than
 * the caller, the record's version). Users already in that state are left alone. Returns how many
 * changed and how many the API refused.
 */
export async function setUsersActive(rows: Row[], active: boolean): Promise<{ changed: number; refused: number }> {
  let changed = 0;
  let refused = 0;
  for (const row of rows) {
    if (Boolean(row.isActive) === active) continue;
    try {
      await api("PUT", `/api/identity/users/${row.id}`, {
        displayName: row.displayName,
        language: row.language,
        isActive: active,
        roleIds: Array.isArray(row.roleIds) ? row.roleIds : [],
        version: row.version,
      });
      changed++;
    } catch {
      refused++;
    }
  }
  return { changed, refused };
}

export type MatchingUsersActiveResult = { matched: number; changed: number; unchanged: number; refusedSelf: number; refusedBeyondOwn: number };

/**
 * Sets every user the list's search and filter match active or inactive in one change on the
 * server, which applies the same rules as one user's edit (never oneself, never someone holding
 * more than the caller). The count the list showed travels with it: when a different number of
 * users matches by then, nothing changes and the API answers 409 with the reason.
 */
export async function setMatchingUsersActive(query: URLSearchParams, expectedCount: number, active: boolean): Promise<MatchingUsersActiveResult> {
  return api<MatchingUsersActiveResult>("POST", "/api/identity/users/matching/active", {
    active,
    search: query.get("search") ?? "",
    filter: query.get("filter") ?? "",
    expectedCount,
  });
}

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

  // Bulk actions on the chosen rows (the selection bar): activate or deactivate accounts.
  const bulkActive = (active: boolean): BulkAction => ({
    key: active ? "activate" : "deactivate",
    labelKey: active ? "identity.users.bulk.activate" : "identity.users.bulk.deactivate",
    permission: "identity.users.update",
    run: async (rows) => {
      const { changed, refused } = await setUsersActive(rows, active);
      setMessage(
        [t("identity.users.bulk.changed", { count: changed }), refused > 0 ? t("identity.users.bulk.refused", { count: refused }) : ""]
          .filter(Boolean)
          .join(" "),
      );
    },
    // "All that match": one set-based change on the server, for exactly the rows the list counted.
    runAll: async (query, total) => {
      if (!window.confirm(t(active ? "identity.users.bulk.activateAllConfirm" : "identity.users.bulk.deactivateAllConfirm", { count: total }))) return false;
      try {
        const result = await setMatchingUsersActive(query, total, active);
        const refused = result.refusedSelf + result.refusedBeyondOwn;
        setMessage(
          [
            t("identity.users.bulk.changed", { count: result.changed }),
            result.unchanged > 0 ? t("identity.users.bulk.unchanged", { count: result.unchanged }) : "",
            refused > 0 ? t("identity.users.bulk.refused", { count: refused }) : "",
          ]
            .filter(Boolean)
            .join(" "),
        );
      } catch (error) {
        setMessage(error instanceof Error ? error.message : String(error));
      }
    },
  });

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
          bulkActions={[bulkActive(false), bulkActive(true)]}
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

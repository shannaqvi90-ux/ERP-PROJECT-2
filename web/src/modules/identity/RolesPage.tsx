import { useCallback, useEffect, useState } from "react";
import { api } from "../../kernel/api";
import { TextField } from "../../kernel/forms/fields";
import { FormSection, RecordForm, type RecordNavigation } from "../../kernel/forms/RecordForm";
import { newRecord, useRecordPanel } from "../../kernel/forms/recordPanel";
import { useRecordForm, type FormErrors } from "../../kernel/forms/useRecordForm";
import { useI18n } from "../../kernel/i18n";
import { ListView } from "../../kernel/lists/ListView";
import { useSession } from "../../kernel/session";
import { chordForAria, chordKeys, useShortcut } from "../../kernel/shortcuts";
import { isTyping, newRecordChord, roleActions, roleName, type Permission, type Role, type RolePage } from "./model";
import { PermissionMatrix } from "./PermissionMatrix";
import "./identity.css";

/**
 * Roles (the shared list: search, filters, sort, views) beside the role editor in the list's
 * panel (?open=id, a new role at ?open=new): English and Arabic names and the permission matrix.
 * Create, copy, edit and delete; the Administrator system role can only be copied. Keyboard: "n"
 * or Alt+N new role, Enter on a row opens it, Ctrl+S or Ctrl+Enter saves, Alt+PageDown/PageUp
 * moves between roles, Escape closes.
 */
export function RolesPage() {
  const { t, language, formatNumber } = useI18n();
  const { can } = useSession();
  const [roles, setRoles] = useState<Role[] | null>(null);
  const [permissions, setPermissions] = useState<Permission[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const panel = useRecordPanel(can("identity.roles.create"));

  const load = useCallback(async () => {
    try {
      const [page, catalogue] = await Promise.all([
        api<RolePage>("GET", "/api/identity/roles"),
        api<Permission[]>("GET", "/api/identity/permissions"),
      ]);
      setRoles(page.items);
      setPermissions(catalogue);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  // Alt+N from anywhere on the screen, including the search box the list focuses on arrival.
  useShortcut({
    id: "identity.roles.new",
    chord: newRecordChord,
    labelKey: "identity.roles.new",
    groupKey: "identity.shortcuts.group",
    enabled: Boolean(panel.startNew),
    run: () => panel.startNew?.(),
  });

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.ctrlKey || event.metaKey || event.altKey || event.defaultPrevented || isTyping(event)) return;
      if (event.key.toLowerCase() === "n" && panel.startNew) {
        event.preventDefault();
        panel.startNew();
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [panel.startNew]);

  const changed = (role: Role, text: string, deleted = false) => {
    void load();
    setMessage(text);
    if (deleted) panel.removed();
    else panel.saved(role.id);
  };

  return (
    <section className="id-screen">
      <div className="id-list">
        {error && (
          <div className="alert" role="alert">
            {error}
          </div>
        )}
        {message && (
          <div className="id-notice" role="status">
            {message}
          </div>
        )}
        <ListView
          listKey="identity.roles"
          titleKey="identity.roles.title"
          countKey="identity.roles.count"
          searchPlaceholderKey="identity.roles.search"
          can={can}
          openOnClick
          reloadKey={panel.reload}
          openId={panel.openId}
          onOpenIdChange={panel.onOpenIdChange}
          renderRecord={(id, close, nav) => {
            const role = id === newRecord ? undefined : roles?.find((r) => r.id === id);
            if (id !== newRecord && !role) return <p className="muted">{t("identity.loading")}</p>;
            return (
              <RoleRecord
                key={id === newRecord ? panel.formKey : `${id}:${role?.version ?? 0}`}
                role={role}
                permissions={permissions}
                nav={nav}
                onClose={close}
                onSaved={(saved, deleted) =>
                  changed(saved, deleted ? t("identity.roles.deleted", { name: roleName(saved, language) }) : t("identity.roles.saved", { name: roleName(saved, language) }), deleted)
                }
                onCopied={(copy) => changed(copy, t("identity.roles.copied", { name: roleName(copy, language) }))}
              />
            );
          }}
          actions={
            panel.startNew && (
              <button type="button" className="button primary" onClick={panel.startNew}
                aria-keyshortcuts={`${chordForAria(newRecordChord)} N`}
                title={chordKeys(newRecordChord).join("+")}
              >
                {t("identity.roles.new")}
              </button>
            )
          }
          renderCell={{
            nameEn: (r) => String((language === "ar" ? r.nameAr : r.nameEn) ?? ""),
            isSystem: (r) => (r.isSystem ? t("identity.roles.system") : t("identity.roles.custom")),
            permissions: (r) => formatNumber(Array.isArray(r.permissions) ? r.permissions.length : 0),
          }}
        />
      </div>
    </section>
  );
}

/** A role in the panel: its editor, or the copy form while copying it. */
function RoleRecord({
  role,
  permissions,
  nav,
  onClose,
  onSaved,
  onCopied,
}: {
  role?: Role;
  permissions: Permission[];
  nav?: RecordNavigation;
  onClose: () => void;
  onSaved: (role: Role, deleted?: boolean) => void;
  onCopied: (role: Role) => void;
}) {
  const [copying, setCopying] = useState(false);
  return copying && role ? (
    <CopyRoleForm source={role} onClose={() => setCopying(false)} onCopied={onCopied} />
  ) : (
    <RoleEditor role={role} permissions={permissions} nav={nav} onClose={onClose} onCopy={() => setCopying(true)} onSaved={onSaved} />
  );
}

type RoleDraft = { nameEn: string; nameAr: string; permissions: string[] };

function RoleEditor({
  role,
  permissions,
  nav,
  onClose,
  onCopy,
  onSaved,
}: {
  role?: Role;
  permissions: Permission[];
  nav?: RecordNavigation;
  onClose: () => void;
  onCopy: (role: Role) => void;
  onSaved: (role: Role, deleted?: boolean) => void;
}) {
  const { t, language } = useI18n();
  const { state } = useSession();
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const held = new Set(state.status === "signedIn" ? state.session.permissions : []);
  const actions = roleActions(role, held);
  const form = useRecordForm<Role, RoleDraft>({
    load: role ? async () => role : undefined,
    initial: (r) => ({ nameEn: r?.nameEn ?? "", nameAr: r?.nameAr ?? "", permissions: [...(r?.permissions ?? [])].sort() }),
    canEdit: actions.edit,
    validate: (draft) => {
      const local: FormErrors = {};
      if (!draft.nameEn.trim()) local.nameEn = [t("identity.form.nameRequired")];
      if (!draft.nameAr.trim()) local.nameAr = [t("identity.form.nameRequired")];
      return local;
    },
    save: (draft, current) => {
      const body = { nameEn: draft.nameEn.trim(), nameAr: draft.nameAr.trim(), permissions: [...draft.permissions].sort() };
      return current ? api<Role>("PUT", `/api/identity/roles/${current.id}`, { ...body, version: current.version }) : api<Role>("POST", "/api/identity/roles", body);
    },
    onSaved: (saved) => onSaved(saved),
  }, role?.id ?? null);
  const selected = new Set(form.draft.permissions);

  async function remove() {
    if (!role) return;
    setBusy(true);
    setDeleteError(null);
    try {
      await api<void>("DELETE", `/api/identity/roles/${role.id}`);
      onSaved(role, true);
    } catch (e) {
      setDeleteError(e instanceof Error ? e.message : String(e));
      setConfirmDelete(false);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="id-form">
      <RecordForm
        form={form}
        title={role ? roleName(role, language) : t("identity.roles.new")}
        onClose={onClose}
        nav={nav}
        saveLabel={role ? undefined : t("identity.form.create")}
        readOnlyReason={role?.isSystem ? t("identity.roles.systemNote") : actions.beyondOwn ? t("identity.roles.beyondOwnNote") : undefined}
        actions={
          <>
            {role && actions.copy && (
              <button type="button" className="button" onClick={() => onCopy(role)}>
                {t("identity.roles.copy")}
              </button>
            )}
            {role && actions.delete && !confirmDelete && (
              <button type="button" className="button danger" onClick={() => setConfirmDelete(true)}>
                {t("identity.roles.delete")}
              </button>
            )}
          </>
        }
      >
        {confirmDelete && role && (
          <p className="id-confirm" role="alert">
            {t("identity.roles.deleteConfirm", { count: role.userCount })}
            <button type="button" className="button danger" disabled={busy} onClick={() => void remove()}>
              {t("identity.roles.deleteYes")}
            </button>
            <button type="button" className="button" onClick={() => setConfirmDelete(false)}>
              {t("identity.form.cancel")}
            </button>
          </p>
        )}
        {deleteError && (
          <div className="alert" role="alert">
            {deleteError}
          </div>
        )}
        <FormSection columns={false}>
          <div className="id-two">
            <TextField field={form.bind("nameEn")} label={t("identity.roles.nameEn")} dir="ltr" autoFocus />
            <TextField field={form.bind("nameAr")} label={t("identity.roles.nameAr")} dir="rtl" />
          </div>
          <PermissionMatrix
            permissions={permissions}
            selected={selected}
            onChange={(next) => form.set("permissions")([...next].sort())}
            canChange={(key) => held.has(key)}
            readOnly={form.readOnly}
          />
        </FormSection>
      </RecordForm>
    </div>
  );
}

function CopyRoleForm({ source, onClose, onCopied }: { source: Role; onClose: () => void; onCopied: (role: Role) => void }) {
  const { t, language } = useI18n();
  const form = useRecordForm<Role, { nameEn: string; nameAr: string }>({
    initial: () => ({ nameEn: t("identity.roles.copyOf", { name: source.nameEn }), nameAr: `${source.nameAr} (2)` }),
    canEdit: true,
    validate: (draft) => {
      const local: FormErrors = {};
      if (!draft.nameEn.trim()) local.nameEn = [t("identity.form.nameRequired")];
      if (!draft.nameAr.trim()) local.nameAr = [t("identity.form.nameRequired")];
      return local;
    },
    save: (draft) => api<Role>("POST", `/api/identity/roles/${source.id}/copy`, { nameEn: draft.nameEn.trim(), nameAr: draft.nameAr.trim() }),
    onSaved: (copy) => onCopied(copy),
  });
  return (
    <div className="id-form">
      <RecordForm form={form} title={t("identity.roles.copyTitle", { name: roleName(source, language) })} onClose={onClose} saveLabel={t("identity.roles.copy")}>
        <FormSection columns={false}>
          <TextField field={form.bind("nameEn")} label={t("identity.roles.nameEn")} dir="ltr" autoFocus />
          <TextField field={form.bind("nameAr")} label={t("identity.roles.nameAr")} dir="rtl" />
        </FormSection>
      </RecordForm>
    </div>
  );
}

import { useCallback, useEffect, useId, useRef, useState } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";
import { isTyping, roleName, type Permission, type Role, type RolePage } from "./model";
import { PermissionMatrix } from "./PermissionMatrix";
import { formKeys } from "./UserPanel";
import "./identity.css";

type Selection = { kind: "none" } | { kind: "new" } | { kind: "role"; id: string } | { kind: "copy"; id: string };

/**
 * Roles beside the role editor: English and Arabic names and the permission matrix. Create,
 * copy, edit and delete; the Administrator system role can only be copied. Keyboard: "n" new
 * role, Enter on a row opens it, Ctrl+Enter saves, Escape closes.
 */
export function RolesPage() {
  const { t, language, formatNumber } = useI18n();
  const { can } = useSession();
  const [roles, setRoles] = useState<Role[] | null>(null);
  const [permissions, setPermissions] = useState<Permission[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [selection, setSelection] = useState<Selection>({ kind: "none" });
  const [message, setMessage] = useState<string | null>(null);

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

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.ctrlKey || event.metaKey || event.altKey || isTyping(event)) return;
      if (event.key.toLowerCase() === "n" && can("identity.roles.create")) {
        event.preventDefault();
        setSelection({ kind: "new" });
      } else if (event.key === "Escape") {
        setSelection({ kind: "none" });
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [can]);

  const selectedRole = selection.kind === "role" || selection.kind === "copy" ? roles?.find((r) => r.id === selection.id) : undefined;

  return (
    <section className={selection.kind === "none" ? "id-screen" : "id-screen with-panel wide"}>
      <div className="id-list">
        <div className="screen-header">
          <h1>{t("identity.roles.title")}</h1>
          {roles && <span className="muted">{t("identity.roles.count", { count: roles.length })}</span>}
          {can("identity.roles.create") && (
            <button type="button" className="button primary id-push" onClick={() => setSelection({ kind: "new" })} aria-keyshortcuts="N">
              {t("identity.roles.new")}
            </button>
          )}
        </div>
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
        <table className="grid">
          <thead>
            <tr>
              <th scope="col">{t("identity.roles.name")}</th>
              <th scope="col">{t("identity.roles.permissions")}</th>
              <th scope="col">{t("identity.roles.users")}</th>
              <th scope="col">{t("identity.roles.kind")}</th>
            </tr>
          </thead>
          <tbody>
            {roles?.map((r) => (
              <tr
                key={r.id}
                role="row"
                tabIndex={0}
                aria-selected={selection.kind === "role" && selection.id === r.id}
                className={selectedRole?.id === r.id ? "id-selected" : undefined}
                onClick={() => setSelection({ kind: "role", id: r.id })}
                onKeyDown={(e) => {
                  if (e.key === "Enter") setSelection({ kind: "role", id: r.id });
                  else if (e.key === "ArrowDown") (e.currentTarget.nextElementSibling as HTMLElement | null)?.focus();
                  else if (e.key === "ArrowUp") (e.currentTarget.previousElementSibling as HTMLElement | null)?.focus();
                }}
              >
                <td>{roleName(r, language)}</td>
                <td>{formatNumber(r.permissions.length)}</td>
                <td>{formatNumber(r.userCount)}</td>
                <td>{r.isSystem ? t("identity.roles.system") : t("identity.roles.custom")}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {selection.kind !== "none" && (
        <aside className="id-panel" aria-label={t("identity.roles.panel")}>
          {selection.kind === "copy" && selectedRole ? (
            <CopyRoleForm
              source={selectedRole}
              onClose={() => setSelection({ kind: "role", id: selectedRole.id })}
              onCopied={(role) => {
                void load();
                setMessage(t("identity.roles.copied", { name: roleName(role, language) }));
                setSelection({ kind: "role", id: role.id });
              }}
            />
          ) : (
            <RoleEditor
              key={selection.kind === "role" ? `${selection.id}:${selectedRole ? "loaded" : "loading"}` : "new"}
              role={selection.kind === "role" ? selectedRole : undefined}
              permissions={permissions}
              onClose={() => setSelection({ kind: "none" })}
              onCopy={(role) => setSelection({ kind: "copy", id: role.id })}
              onSaved={(role, deleted) => {
                void load();
                setMessage(deleted ? t("identity.roles.deleted", { name: roleName(role, language) }) : t("identity.roles.saved", { name: roleName(role, language) }));
                setSelection(deleted ? { kind: "none" } : { kind: "role", id: role.id });
              }}
            />
          )}
        </aside>
      )}
    </section>
  );
}

function RoleEditor({
  role,
  permissions,
  onClose,
  onCopy,
  onSaved,
}: {
  role?: Role;
  permissions: Permission[];
  onClose: () => void;
  onCopy: (role: Role) => void;
  onSaved: (role: Role, deleted?: boolean) => void;
}) {
  const { t, language } = useI18n();
  const { state, can } = useSession();
  const [nameEn, setNameEn] = useState(role?.nameEn ?? "");
  const [nameAr, setNameAr] = useState(role?.nameAr ?? "");
  const [selected, setSelected] = useState<Set<string>>(new Set(role?.permissions ?? []));
  const [error, setError] = useState<string | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [busy, setBusy] = useState(false);
  const firstRef = useRef<HTMLInputElement>(null);
  const id = useId();
  const held = new Set(state.status === "signedIn" ? state.session.permissions : []);
  const readOnly = role?.isSystem === true || !can(role ? "identity.roles.update" : "identity.roles.create");

  useEffect(() => firstRef.current?.focus(), []);

  async function save() {
    if (readOnly || busy) return;
    const local: Record<string, string> = {};
    if (!nameEn.trim()) local.nameEn = t("identity.form.nameRequired");
    if (!nameAr.trim()) local.nameAr = t("identity.form.nameRequired");
    setErrors(local);
    if (Object.keys(local).length) return;
    setBusy(true);
    setError(null);
    try {
      const body = { nameEn: nameEn.trim(), nameAr: nameAr.trim(), permissions: [...selected].sort() };
      const saved = role
        ? await api<Role>("PUT", `/api/identity/roles/${role.id}`, { ...body, version: role.version })
        : await api<Role>("POST", "/api/identity/roles", body);
      onSaved(saved);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    if (!role) return;
    setBusy(true);
    try {
      await api<void>("DELETE", `/api/identity/roles/${role.id}`);
      onSaved(role, true);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setConfirmDelete(false);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form
      className="id-form"
      noValidate
      aria-labelledby={`${id}-title`}
      onSubmit={(e) => {
        e.preventDefault();
        void save();
      }}
      onKeyDown={(e) => formKeys(e, () => void save(), onClose)}
    >
      <h2 id={`${id}-title`}>{role ? roleName(role, language) : t("identity.roles.new")}</h2>
      {role?.isSystem && <p className="muted">{t("identity.roles.systemNote")}</p>}
      <div className="id-two">
        <label className="field">
          <span className="field-label">{t("identity.roles.nameEn")}</span>
          <input ref={firstRef} name="nameEn" dir="ltr" lang="en" value={nameEn} disabled={readOnly} onChange={(e) => setNameEn(e.target.value)} aria-invalid={errors.nameEn ? true : undefined} />
          {errors.nameEn && <span className="field-error">{errors.nameEn}</span>}
        </label>
        <label className="field">
          <span className="field-label">{t("identity.roles.nameAr")}</span>
          <input name="nameAr" dir="rtl" lang="ar" value={nameAr} disabled={readOnly} onChange={(e) => setNameAr(e.target.value)} aria-invalid={errors.nameAr ? true : undefined} />
          {errors.nameAr && <span className="field-error">{errors.nameAr}</span>}
        </label>
      </div>
      <PermissionMatrix permissions={permissions} selected={selected} onChange={setSelected} canChange={(key) => held.has(key)} readOnly={readOnly} />
      {error && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}
      <div className="id-actions">
        {!readOnly && (
          <button type="submit" className="button primary" disabled={busy} aria-keyshortcuts="Control+Enter">
            {role ? t("identity.form.save") : t("identity.form.create")}
          </button>
        )}
        {role && can("identity.roles.create") && (
          <button type="button" className="button" onClick={() => onCopy(role)}>
            {t("identity.roles.copy")}
          </button>
        )}
        {role && !role.isSystem && can("identity.roles.delete") && !confirmDelete && (
          <button type="button" className="button danger" onClick={() => setConfirmDelete(true)}>
            {t("identity.roles.delete")}
          </button>
        )}
        {confirmDelete && role && (
          <span className="id-confirm" role="alert">
            {t("identity.roles.deleteConfirm", { count: role.userCount })}
            <button type="button" className="button danger" disabled={busy} onClick={() => void remove()}>
              {t("identity.roles.deleteYes")}
            </button>
            <button type="button" className="button" onClick={() => setConfirmDelete(false)}>
              {t("identity.form.cancel")}
            </button>
          </span>
        )}
        <button type="button" className="button" onClick={onClose} aria-keyshortcuts="Escape">
          {t("identity.form.close")}
        </button>
      </div>
    </form>
  );
}

function CopyRoleForm({ source, onClose, onCopied }: { source: Role; onClose: () => void; onCopied: (role: Role) => void }) {
  const { t, language } = useI18n();
  const [nameEn, setNameEn] = useState(t("identity.roles.copyOf", { name: source.nameEn }));
  const [nameAr, setNameAr] = useState(`${source.nameAr} (2)`);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const firstRef = useRef<HTMLInputElement>(null);
  useEffect(() => {
    firstRef.current?.focus();
    firstRef.current?.select();
  }, []);

  async function copy() {
    setBusy(true);
    setError(null);
    try {
      onCopied(await api<Role>("POST", `/api/identity/roles/${source.id}/copy`, { nameEn: nameEn.trim(), nameAr: nameAr.trim() }));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form
      className="id-form"
      noValidate
      onSubmit={(e) => {
        e.preventDefault();
        void copy();
      }}
      onKeyDown={(e) => formKeys(e, () => void copy(), onClose)}
    >
      <h2>{t("identity.roles.copyTitle", { name: roleName(source, language) })}</h2>
      <label className="field">
        <span className="field-label">{t("identity.roles.nameEn")}</span>
        <input ref={firstRef} dir="ltr" lang="en" value={nameEn} onChange={(e) => setNameEn(e.target.value)} />
      </label>
      <label className="field">
        <span className="field-label">{t("identity.roles.nameAr")}</span>
        <input dir="rtl" lang="ar" value={nameAr} onChange={(e) => setNameAr(e.target.value)} />
      </label>
      {error && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}
      <div className="id-actions">
        <button type="submit" className="button primary" disabled={busy || !nameEn.trim() || !nameAr.trim()}>
          {t("identity.roles.copy")}
        </button>
        <button type="button" className="button" onClick={onClose}>
          {t("identity.form.cancel")}
        </button>
      </div>
    </form>
  );
}
